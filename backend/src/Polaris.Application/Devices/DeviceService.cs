using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Polaris.Application.Abstractions;
using Polaris.Application.Common;
using Polaris.Application.Logistics;
using Polaris.Application.Parking;
using Polaris.Domain.Common;
using Polaris.Domain.Devices;

namespace Polaris.Application.Devices;

public sealed class DeviceService(
    IPolarisDbContext db,
    DesiredStateComposer composer,
    DeviceRegistry registry,
    LogisticsService logistics,
    ParkingService parking,
    IRealtimeNotifier realtime,
    TimeProvider time,
    PolarisOptions options,
    ILogger<DeviceService> logger)
{
    private TimeSpan OfflineAfter => TimeSpan.FromSeconds(options.DeviceOfflineSeconds);

    public async Task<IReadOnlyList<DeviceDto>> ListAsync(CancellationToken ct)
    {
        var now = time.GetUtcNow();
        var devices = await db.Devices.AsNoTracking().OrderBy(d => d.Id).ToListAsync(ct);
        return devices.Select(d => ToDto(d, now)).ToList();
    }

    /// <summary>
    /// Procesa un lote de eventos de una placa. Los duplicados (mismo bootId y seq ya visto) se descartan,
    /// así la placa puede reintentar sin miedo.
    /// </summary>
    public async Task<AckDto> IngestAsync(string deviceId, DeviceEventBatch batch, string? remoteIp, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(batch.BootId))
            throw DomainException.Validation("boot_id_required", "Falta bootId en el lote de eventos.");

        var device = await db.Devices.FirstOrDefaultAsync(d => d.Id == deviceId, ct)
            ?? throw DomainException.NotFound("device_not_found", $"Dispositivo desconocido: {deviceId}.");

        var now = time.GetUtcNow();
        await MarkSeenAsync(device, now);
        device.ReportFirmware(batch.Firmware);

        var events = batch.Events ?? Array.Empty<DeviceEventDto>();
        long ack = 0;
        int processed = 0, duplicates = 0;
        var heartbeat = false;

        foreach (var ev in events.OrderBy(e => e.Seq))
        {
            ack = Math.Max(ack, ev.Seq);
            if (!device.AcceptSequence(batch.BootId, ev.Seq))
            {
                duplicates++;
                continue;
            }

            processed++;
            try
            {
                heartbeat |= await HandleAsync(device, ev, remoteIp, now, ct);
            }
            catch (DomainException ex)
            {
                // Un evento rechazado por una regla de negocio no debe frenar el resto del lote.
                logger.LogWarning("Evento {Type} de {Device} rechazado: {Code} {Message}", ev.Type, deviceId, ex.Code, ex.Message);
            }
        }

        await db.SaveChangesAsync(ct);
        if (heartbeat) await realtime.DeviceStatusChanged(ToDto(device, now));

        return new AckDto(ack, processed, duplicates);
    }

    /// <summary>
    /// Estado completo que debe mostrar la placa. Si la versión pedida es la actual, devuelve notModified (HTTP 304).
    /// </summary>
    public async Task<(DesiredStateDto State, bool NotModified)> GetDesiredStateAsync(string deviceId, long? version, CancellationToken ct)
    {
        var device = await db.Devices.AsNoTracking().FirstOrDefaultAsync(d => d.Id == deviceId, ct)
            ?? throw DomainException.NotFound("device_not_found", $"Dispositivo desconocido: {deviceId}.");

        var now = time.GetUtcNow();
        await MarkSeenAsync(device, now);

        var state = await composer.ComposeAsync(device, ct);
        return (state, version == state.Version);
    }

    /// <summary>Detecta placas que dejaron de responder y avisa a la web.</summary>
    public async Task HousekeepAsync(CancellationToken ct)
    {
        var now = time.GetUtcNow();
        var devices = await db.Devices.AsNoTracking().ToListAsync(ct);
        foreach (var device in devices)
        {
            var online = registry.IsOnline(device.Id, now, OfflineAfter);
            if (registry.SetReported(device.Id, online))
            {
                await realtime.DeviceStatusChanged(ToDto(device, now));
            }
        }
    }

    private async Task<bool> HandleAsync(Device device, DeviceEventDto ev, string? remoteIp, DateTimeOffset now, CancellationToken ct)
    {
        switch (ev.Type)
        {
            case DeviceEventTypes.OccupancyChanged:
                if (ev.Channel is not { } channel || ev.Occupied is not { } occupied)
                {
                    logger.LogWarning("occupancy.changed sin channel u occupied desde {Device}.", device.Id);
                    return false;
                }

                await parking.RegisterSensorAsync(device.Id, channel, occupied, ev.DistanceCm, ct);
                return false;

            case DeviceEventTypes.ButtonPressed:
                Guid? pickId = Guid.TryParse(ev.ActivePickId, out var parsed) ? parsed : null;
                await logistics.ConfirmFromButtonAsync(device.Id, pickId, ct);
                return false;

            case DeviceEventTypes.Heartbeat:
                device.ApplyHeartbeat(ev.Ip ?? remoteIp, ev.Rssi, ev.FreeHeap, now);
                return true;

            case DeviceEventTypes.Error:
                logger.LogWarning("Error reportado por {Device}: {Code} {Detail}", device.Id, ev.Code, ev.Detail);
                return false;

            default:
                logger.LogWarning("Tipo de evento desconocido de {Device}: {Type}", device.Id, ev.Type);
                return false;
        }
    }

    private async Task MarkSeenAsync(Device device, DateTimeOffset now)
    {
        registry.Touch(device.Id, now);
        if (registry.SetReported(device.Id, true))
        {
            await realtime.DeviceStatusChanged(ToDto(device, now));
        }
    }

    private DeviceDto ToDto(Device d, DateTimeOffset now) => new(
        d.Id,
        d.Kind == DeviceKind.Logistics ? "logistics" : "parking",
        d.Name,
        registry.IsOnline(d.Id, now, OfflineAfter),
        registry.LastSeen(d.Id),
        d.Firmware,
        d.Ip,
        d.Rssi);
}
