using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Polaris.Application.Abstractions;
using Polaris.Application.Common;
using Polaris.Domain.Devices;
using Polaris.Domain.Logistics;
using Polaris.Domain.Parking;

namespace Polaris.Application.Devices;

/// <summary>
/// Traduce el estado del negocio a "cómo debe verse la tira" de una placa.
/// La versión es un hash del contenido: si nada cambió, la placa recibe 304 y no hace nada.
/// </summary>
public sealed class DesiredStateComposer(IPolarisDbContext db, TimeProvider time, PolarisOptions options)
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public async Task<DesiredStateDto> ComposeAsync(Device device, CancellationToken ct)
    {
        var now = time.GetUtcNow();
        DesiredContextDto? context = null;
        SensorConfigDto? config = null;
        List<LedDto> leds;

        if (device.Kind == DeviceKind.Logistics)
        {
            (leds, context) = await ComposeLogisticsAsync(device.Id, now, ct);
        }
        else
        {
            leds = await ComposeParkingAsync(device.Id, now, ct);
            config = new SensorConfigDto(
                options.OccupiedBelowCm, options.FreeAboveCm, options.SampleIntervalMs, options.ParkingEntranceChannel);
        }

        var state = new DesiredStateDto(
            device.Id, 0, options.LedBrightness, options.FailSafeAfterMs,
            new LedDefaultDto(LedColors.Off), leds.OrderBy(l => l.Index).ToList(), context, config);

        return state with { Version = ContentHash(JsonSerializer.Serialize(state, Json)) };
    }

    private async Task<(List<LedDto>, DesiredContextDto)> ComposeLogisticsAsync(string deviceId, DateTimeOffset now, CancellationToken ct)
    {
        var flashCutoff = now.AddMilliseconds(-options.ConfirmedFlashMs);
        var picks = await db.PickOrders.AsNoTracking()
            .Include(p => p.Compartment)
            .Where(p => p.Compartment.DeviceId == deviceId &&
                        (p.Status == PickStatus.Lit || (p.Status == PickStatus.Confirmed && p.ClosedAt >= flashCutoff)))
            .ToListAsync(ct);

        // Verde unos instantes en lo recién confirmado; dorado pulsante en lo que hay que retirar.
        var leds = picks
            .Where(p => p.Status == PickStatus.Confirmed)
            .Select(p => new LedDto(p.Compartment.LedIndex, LedColors.Confirmed, LedEffects.Solid))
            .ToList();

        var lit = picks.Where(p => p.IsLit).OrderBy(p => p.LitAt).FirstOrDefault();
        if (lit is not null)
        {
            leds.RemoveAll(l => l.Index == lit.Compartment.LedIndex);
            leds.Add(new LedDto(lit.Compartment.LedIndex, LedColors.Pick, LedEffects.Pulse));
        }

        return (leds, new DesiredContextDto(lit?.Id.ToString(), lit is not null));
    }

    private async Task<List<LedDto>> ComposeParkingAsync(string deviceId, DateTimeOffset now, CancellationToken ct)
    {
        var spots = await db.ParkingSpots.AsNoTracking().Where(s => s.DeviceId == deviceId).ToListAsync(ct);
        var assigned = (await db.Reservations.AsNoTracking()
                .Where(r => r.Status == ReservationStatus.Assigned && r.ExpiresAt > now && r.SpotId != null)
                .ToListAsync(ct))
            .GroupBy(r => r.SpotId!.Value)
            .ToDictionary(g => g.Key, g => g.First());
        var waiting = await db.Reservations.AsNoTracking().AnyAsync(r => r.Status == ReservationStatus.Waiting, ct);

        var leds = new List<LedDto>();
        var anyFree = false;
        foreach (var spot in spots)
        {
            var state = spot.StateAt(now, assigned.GetValueOrDefault(spot.Id));
            anyFree |= state == SpotState.Free;
            leds.Add(state switch
            {
                SpotState.Free => new LedDto(spot.LedIndex, LedColors.Free, LedEffects.Solid),
                SpotState.Occupied => new LedDto(spot.LedIndex, LedColors.Occupied, LedEffects.Solid),
                SpotState.Reserved => new LedDto(spot.LedIndex, LedColors.Guide, LedEffects.Blink),
                _ => new LedDto(spot.LedIndex, LedColors.Off, LedEffects.Solid)
            });
        }

        // Cartel de la entrada: verde si hay lugar libre y nadie esperando; rojo si está completo.
        var entranceColor = anyFree && !waiting ? LedColors.Free : LedColors.Occupied;
        leds.Add(new LedDto(options.ParkingEntranceLedIndex, entranceColor, LedEffects.Solid));
        return leds;
    }

    /// <summary>FNV-1a de 64 bits, positivo. Estable entre reinicios (a diferencia de string.GetHashCode).</summary>
    private static long ContentHash(string content)
    {
        unchecked
        {
            var hash = 14695981039346656037UL;
            foreach (var b in Encoding.UTF8.GetBytes(content))
            {
                hash ^= b;
                hash *= 1099511628211UL;
            }

            return (long)(hash & 0x7FFF_FFFF_FFFF_FFFFUL);
        }
    }
}
