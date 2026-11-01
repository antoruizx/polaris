using Polaris.Application.Abstractions;
using Polaris.Application.Devices;

namespace Polaris.Api.Infrastructure;

/// <summary>
/// Alimenta la consola de tráfico de la web con cada pedido a /api. Omite los 304 de las placas
/// (consultas sin cambios cada 250 ms) para no inundar la consola.
/// </summary>
public sealed class TrafficMiddleware(RequestDelegate next, ILogger<TrafficMiddleware> logger)
{
    public async Task InvokeAsync(HttpContext context, IRealtimeNotifier realtime, TimeProvider time)
    {
        await next(context);

        var path = context.Request.Path;
        if (!path.StartsWithSegments("/api")) return;

        var status = context.Response.StatusCode;
        if (status == StatusCodes.Status304NotModified) return;

        var source = context.GetRouteValue("deviceId") as string ?? "web";
        try
        {
            await realtime.DeviceTraffic(new TrafficDto(
                time.GetUtcNow(), source, context.Request.Method, path + context.Request.QueryString, status, null));
        }
        catch (Exception ex)
        {
            logger.LogDebug(ex, "No se pudo publicar el tráfico.");
        }
    }
}
