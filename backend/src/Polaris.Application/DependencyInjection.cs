using Microsoft.Extensions.DependencyInjection;
using Polaris.Application.Common;
using Polaris.Application.Devices;
using Polaris.Application.Logistics;
using Polaris.Application.Parking;

namespace Polaris.Application;

public static class DependencyInjection
{
    public static IServiceCollection AddPolarisApplication(this IServiceCollection services)
    {
        services.AddSingleton<CommandGate>();
        services.AddSingleton<DeviceRegistry>();
        services.AddScoped<LogisticsService>();
        services.AddScoped<ParkingService>();
        services.AddScoped<DesiredStateComposer>();
        services.AddScoped<DeviceService>();
        return services;
    }
}
