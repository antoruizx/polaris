using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Polaris.Application.Abstractions;
using Polaris.Application.Common;
using Polaris.Domain.Common;
using Polaris.Domain.Parking;

namespace Polaris.Application.Parking;

/// <summary>
/// Casos de uso de Parking. Las reservas son por orden de llegada: el sensor de la entrada detecta el auto,
/// se le asigna la plaza libre más cercana y la luz lo guía. Sin lugar, espera en la fila.
/// </summary>
public sealed class ParkingService(
    IPolarisDbContext db,
    IRealtimeNotifier realtime,
    TimeProvider time,
    PolarisOptions options,
    CommandGate gate,
    ILogger<ParkingService> logger)
{
    // ---------- Consultas ----------

    public async Task<IReadOnlyList<SpotDto>> SpotsAsync(CancellationToken ct)
    {
        var now = time.GetUtcNow();
        var spots = await db.ParkingSpots.AsNoTracking().OrderBy(s => s.Code).ToListAsync(ct);
        var assigned = await AssignedBySpotAsync(now, ct);
        return spots.Select(s => ToDto(s, assigned.GetValueOrDefault(s.Id), now)).ToList();
    }

    /// <summary>Autos en camino a su plaza y autos esperando lugar, en orden de llegada.</summary>
    public async Task<IReadOnlyList<ReservationDto>> QueueAsync(CancellationToken ct)
    {
        var open = await db.Reservations.AsNoTracking().Include(r => r.Spot)
            .Where(r => r.Status == ReservationStatus.Waiting || r.Status == ReservationStatus.Assigned)
            .OrderBy(r => r.CreatedAt)
            .ToListAsync(ct);
        return open.Select(x => ToDto(x)).ToList();
    }

    public async Task<ParkingMetricsDto> MetricsAsync(DateOnly? date, CancellationToken ct)
    {
        var now = time.GetUtcNow();
        var (day, start, end) = DayRange(date, now);

        var spots = await db.ParkingSpots.AsNoTracking().ToListAsync(ct);
        var assigned = await AssignedBySpotAsync(now, ct);
        var states = spots.Select(s => s.StateAt(now, assigned.GetValueOrDefault(s.Id))).ToList();

        var events = await db.OccupancyEvents.AsNoTracking()
            .Where(e => e.At >= start && e.At < end)
            .Select(e => e.Occupied)
            .ToListAsync(ct);

        var arrivals = await db.Reservations.AsNoTracking().CountAsync(r => r.CreatedAt >= start && r.CreatedAt < end, ct);
        var waiting = await db.Reservations.AsNoTracking().CountAsync(r => r.Status == ReservationStatus.Waiting, ct);

        var total = spots.Count(s => !s.OutOfService);
        var occupied = states.Count(s => s == SpotState.Occupied);

        return new ParkingMetricsDto(
            day, total, occupied,
            states.Count(s => s == SpotState.Reserved),
            states.Count(s => s == SpotState.Free),
            waiting,
            total == 0 ? 0 : (int)Math.Round(occupied * 100.0 / total),
            arrivals,
            events.Count(o => o),
            events.Count(o => !o));
    }

    // ---------- Comandos ----------

    /// <summary>
    /// Un auto llegó a la entrada (sensor de entrada o botón de la web). Crea su reserva y, si hay lugar,
    /// le asigna la plaza libre más cercana.
    /// </summary>
    public Task<ReservationDto> ArriveAsync(CancellationToken ct) => gate.Run(async () =>
    {
        var now = time.GetUtcNow();
        var (_, start, end) = DayRange(null, now);
        var number = await db.Reservations.CountAsync(r => r.CreatedAt >= start && r.CreatedAt < end, ct) + 1;

        var reservation = Reservation.Arrive(number, now);
        db.Reservations.Add(reservation);
        await db.SaveChangesAsync(ct);

        await AssignWaitingAsync(now, ct);

        var dto = ToDto(reservation);
        if (reservation.IsWaiting)
        {
            await realtime.ReservationChanged(dto); // si se asignó, AssignWaitingAsync ya avisó
        }

        return dto;
    }, ct);

    public Task<ReservationDto> CancelReservationAsync(Guid id, CancellationToken ct) => gate.Run(async () =>
    {
        var reservation = await db.Reservations.Include(r => r.Spot).FirstOrDefaultAsync(r => r.Id == id, ct)
            ?? throw DomainException.NotFound("reservation_not_found", "No existe esa reserva.");

        var now = time.GetUtcNow();
        if (reservation.Cancel(now))
        {
            await db.SaveChangesAsync(ct);
            await realtime.ReservationChanged(ToDto(reservation));
            if (reservation.Spot is { } spot) await realtime.SpotChanged(ToDto(spot, null, now));
            await AssignWaitingAsync(now, ct);
        }

        return ToDto(reservation);
    }, ct);

    /// <summary>
    /// Lectura de un sensor ultrasónico, ya filtrada por el firmware (solo llega cuando cambia).
    /// El canal de la entrada genera una llegada; los demás canales son plazas.
    /// </summary>
    public async Task<bool> RegisterSensorAsync(string deviceId, int channel, bool occupied, double? distanceCm, CancellationToken ct)
    {
        if (channel == options.ParkingEntranceChannel)
        {
            if (!occupied) return false; // el auto terminó de pasar
            await ArriveAsync(ct);
            return true;
        }

        return await gate.Run(() => RegisterSpotOccupancyAsync(deviceId, channel, occupied, distanceCm, ct), ct);
    }

    /// <summary>Vence reservas cuyo auto no llegó a la plaza y asigna lugar a los que esperan.</summary>
    public Task HousekeepAsync(CancellationToken ct) => gate.Run(async () =>
    {
        var now = time.GetUtcNow();
        var expired = await db.Reservations.Include(r => r.Spot)
            .Where(r => r.Status == ReservationStatus.Assigned && r.ExpiresAt <= now)
            .ToListAsync(ct);

        if (expired.Count > 0)
        {
            foreach (var r in expired) r.Expire(now);
            await db.SaveChangesAsync(ct);

            foreach (var r in expired)
            {
                await realtime.ReservationChanged(ToDto(r));
                if (r.Spot is { } spot) await realtime.SpotChanged(ToDto(spot, null, now));
            }
        }

        await AssignWaitingAsync(now, ct);
    }, ct);

    // ---------- Internos ----------

    private async Task<bool> RegisterSpotOccupancyAsync(string deviceId, int channel, bool occupied, double? distanceCm, CancellationToken ct)
    {
        var spot = await db.ParkingSpots.FirstOrDefaultAsync(s => s.DeviceId == deviceId && s.SensorChannel == channel, ct);
        if (spot is null)
        {
            logger.LogWarning("Sensor sin plaza asignada: {Device} canal {Channel}.", deviceId, channel);
            return false;
        }

        var now = time.GetUtcNow();
        if (!spot.ApplyOccupancy(occupied, distanceCm, now))
        {
            await db.SaveChangesAsync(ct); // guarda la última distancia
            return false;
        }

        db.OccupancyEvents.Add(new OccupancyEvent(spot, occupied, distanceCm, now));

        Reservation? fulfilled = null;
        if (occupied)
        {
            // Lo normal: el auto estacionó donde lo guiamos. Si eligió otra plaza libre,
            // asumimos que es el auto que llegó primero y liberamos la plaza que tenía asignada.
            var assigned = await db.Reservations.Include(r => r.Spot)
                .Where(r => r.Status == ReservationStatus.Assigned && r.ExpiresAt > now)
                .OrderBy(r => r.CreatedAt)
                .ToListAsync(ct);

            fulfilled = assigned.FirstOrDefault(r => r.SpotId == spot.Id) ?? assigned.FirstOrDefault();
            fulfilled?.Fulfill(now);
        }

        await db.SaveChangesAsync(ct);

        await realtime.SpotChanged(ToDto(spot, null, now));
        if (fulfilled is not null)
        {
            await realtime.ReservationChanged(ToDto(fulfilled));
            if (fulfilled.Spot is { } other && other.Id != spot.Id)
            {
                await realtime.SpotChanged(ToDto(other, null, now));
            }
        }

        await AssignWaitingAsync(now, ct);
        return true;
    }

    /// <summary>Asigna plazas libres a los autos que esperan, en orden de llegada y de cercanía a la entrada.</summary>
    private async Task AssignWaitingAsync(DateTimeOffset now, CancellationToken ct)
    {
        var waiting = await db.Reservations
            .Where(r => r.Status == ReservationStatus.Waiting)
            .OrderBy(r => r.CreatedAt)
            .ToListAsync(ct);
        if (waiting.Count == 0) return;

        var assigned = await AssignedBySpotAsync(now, ct);
        var free = (await db.ParkingSpots.OrderBy(s => s.Order).ToListAsync(ct))
            .Where(s => s.StateAt(now, assigned.GetValueOrDefault(s.Id)) == SpotState.Free)
            .ToList();

        var pairs = waiting.Zip(free).ToList();
        if (pairs.Count == 0) return;

        var hold = TimeSpan.FromSeconds(options.ArrivalHoldSeconds);
        foreach (var (reservation, spot) in pairs)
        {
            reservation.Assign(spot, now, hold);
        }

        await db.SaveChangesAsync(ct);

        foreach (var (reservation, spot) in pairs)
        {
            await realtime.ReservationChanged(ToDto(reservation));
            await realtime.SpotChanged(ToDto(spot, reservation, now));
        }
    }

    private async Task<Dictionary<Guid, Reservation>> AssignedBySpotAsync(DateTimeOffset now, CancellationToken ct)
    {
        var list = await db.Reservations
            .Where(r => r.Status == ReservationStatus.Assigned && r.ExpiresAt > now && r.SpotId != null)
            .ToListAsync(ct);
        return list.GroupBy(r => r.SpotId!.Value).ToDictionary(g => g.Key, g => g.First());
    }

    private (DateOnly Day, DateTimeOffset Start, DateTimeOffset End) DayRange(DateOnly? date, DateTimeOffset now)
    {
        var tz = TimeZoneInfo.FindSystemTimeZoneById(options.TimeZone);
        var day = date ?? DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(now, tz).DateTime);
        var startLocal = day.ToDateTime(TimeOnly.MinValue);
        var start = new DateTimeOffset(startLocal, tz.GetUtcOffset(startLocal)).ToUniversalTime();
        return (day, start, start.AddDays(1));
    }

    internal static SpotDto ToDto(ParkingSpot s, Reservation? reservation, DateTimeOffset now)
    {
        var active = reservation is not null && reservation.SpotId == s.Id && reservation.IsAssignedAt(now) ? reservation : null;
        return new SpotDto(
            s.Code, s.Row, s.Order, Names.Of(s.StateAt(now, active)), s.LedIndex, s.SensorChannel,
            s.LastDistanceCm, s.LastChangeAt,
            active is null ? null : ToDto(active, s.Code));
    }

    private static ReservationDto ToDto(Reservation r) => ToDto(r, r.Spot?.Code);

    private static ReservationDto ToDto(Reservation r, string? spotCode) =>
        new(r.Id, r.Number, r.Label, Names.Of(r.Status), spotCode, r.CreatedAt, r.AssignedAt, r.ExpiresAt, r.ClosedAt);
}
