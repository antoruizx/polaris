using System.Security.Cryptography;
using System.Text;
using Polaris.Application.Common;

namespace Polaris.Api.Infrastructure;

/// <summary>Valida el encabezado X-Device-Key contra la clave configurada para el {deviceId} de la ruta.</summary>
public sealed class DeviceKeyFilter(PolarisOptions options) : IEndpointFilter
{
    public const string HeaderName = "X-Device-Key";

    public async ValueTask<object?> InvokeAsync(EndpointFilterInvocationContext context, EndpointFilterDelegate next)
    {
        var http = context.HttpContext;
        var deviceId = http.GetRouteValue("deviceId") as string;
        var provided = http.Request.Headers[HeaderName].ToString();

        if (deviceId is null || !options.DeviceKeys.TryGetValue(deviceId, out var expected) || !SameKey(expected, provided))
        {
            return Results.Problem(
                statusCode: StatusCodes.Status401Unauthorized,
                title: "Clave de dispositivo inválida.",
                extensions: new Dictionary<string, object?> { ["code"] = "invalid_device_key" });
        }

        return await next(context);
    }

    private static bool SameKey(string expected, string provided) =>
        CryptographicOperations.FixedTimeEquals(Encoding.UTF8.GetBytes(expected), Encoding.UTF8.GetBytes(provided));
}
