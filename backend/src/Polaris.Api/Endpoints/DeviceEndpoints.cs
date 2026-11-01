using Polaris.Api.Infrastructure;
using Polaris.Application.Devices;

namespace Polaris.Api.Endpoints;

public static class DeviceEndpoints
{
    public static IEndpointRouteBuilder MapDeviceEndpoints(this IEndpointRouteBuilder app)
    {
        var g = app.MapGroup("/api/devices").WithTags("Dispositivos");

        g.MapGet("", (DeviceService s, CancellationToken ct) => s.ListAsync(ct))
            .WithSummary("Estado de cada ESP32: en línea, última señal, firmware, WiFi.");

        // Endpoints que usan las placas: requieren X-Device-Key.
        var device = g.MapGroup("/{deviceId}").AddEndpointFilter<DeviceKeyFilter>();

        device.MapPost("/events", (string deviceId, DeviceEventBatch batch, DeviceService s, HttpContext http, CancellationToken ct) =>
                s.IngestAsync(deviceId, batch, http.Connection.RemoteIpAddress?.MapToIPv4().ToString(), ct))
            .WithSummary("La placa informa hechos: presencia, botón, heartbeat, errores.");

        device.MapGet("/desired-state", async (string deviceId, long? version, DeviceService s, CancellationToken ct) =>
            {
                var (state, notModified) = await s.GetDesiredStateAsync(deviceId, version, ct);
                return notModified ? Results.StatusCode(StatusCodes.Status304NotModified) : Results.Ok(state);
            })
            .WithSummary("Estado completo que debe mostrar la tira. 304 si sigue igual a ?version=.");

        return app;
    }
}
