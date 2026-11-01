using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Polaris.Application.Abstractions;
using Polaris.Infrastructure.Persistence;

namespace Polaris.Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddPolarisInfrastructure(this IServiceCollection services, string connectionString)
    {
        services.AddDbContext<PolarisDbContext>(o => o.UseNpgsql(connectionString));
        services.AddScoped<IPolarisDbContext>(sp => sp.GetRequiredService<PolarisDbContext>());
        return services;
    }

    /// <summary>
    /// Crea el esquema y carga la semilla si la base está vacía.
    /// Para la FAPPE alcanza con EnsureCreated; las migraciones de EF entran en la etapa de endurecimiento.
    /// </summary>
    public static async Task InitializePolarisDatabaseAsync(this IServiceProvider services, CancellationToken ct = default)
    {
        using var scope = services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<PolarisDbContext>();
        await db.Database.EnsureCreatedAsync(ct);
        await PolarisSeeder.SeedAsync(db, ct);
    }
}
