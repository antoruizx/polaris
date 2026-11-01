using Polaris.Application.Devices;
using Polaris.Application.Logistics;
using Polaris.Application.Parking;

namespace Polaris.Api.Background;

/// <summary>
/// Una vez por segundo: apaga retiros vencidos, vence reservas, asigna plaza a quien espera
/// y detecta placas que dejaron de responder.
/// </summary>
public sealed class HousekeepingService(IServiceScopeFactory scopes, ILogger<HousekeepingService> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(1));
        while (await timer.WaitForNextTickAsync(stoppingToken))
        {
            try
            {
                using var scope = scopes.CreateScope();
                var services = scope.ServiceProvider;
                await services.GetRequiredService<LogisticsService>().ExpireStalePicksAsync(stoppingToken);
                await services.GetRequiredService<ParkingService>().HousekeepAsync(stoppingToken);
                await services.GetRequiredService<DeviceService>().HousekeepAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Falló una pasada de mantenimiento.");
            }
        }
    }
}
