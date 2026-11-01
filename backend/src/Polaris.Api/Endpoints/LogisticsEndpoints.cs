using Polaris.Application.Logistics;

namespace Polaris.Api.Endpoints;

public static class LogisticsEndpoints
{
    public static IEndpointRouteBuilder MapLogisticsEndpoints(this IEndpointRouteBuilder app)
    {
        var g = app.MapGroup("/api/logistics").WithTags("Logística");

        g.MapGet("/products", (string? q, LogisticsService s, CancellationToken ct) => s.SearchAsync(q, ct))
            .WithSummary("Busca por nombre, SKU o categoría (ignora acentos y mayúsculas).");

        g.MapGet("/inventory", (LogisticsService s, CancellationToken ct) => s.SearchAsync(null, ct))
            .WithSummary("Stock por producto con estado ok / low / out.");

        g.MapGet("/compartments", (LogisticsService s, CancellationToken ct) => s.CompartmentsAsync(ct))
            .WithSummary("Los compartimentos de la estantería con su producto y LED.");

        g.MapGet("/picks/open", (LogisticsService s, CancellationToken ct) => s.OpenPicksAsync(ct))
            .WithSummary("Retiros encendidos y en cola, en orden de llegada.");

        g.MapPost("/picks", async (StartPickRequest request, LogisticsService s, CancellationToken ct) =>
            {
                var pick = await s.StartPickAsync(request, ct);
                return Results.Created($"/api/logistics/picks/{pick.Id}", pick);
            })
            .WithSummary("Pide un retiro: se enciende el compartimento, o entra en cola si la estantería está ocupada.");

        g.MapPost("/picks/{id:guid}/confirm", (Guid id, ConfirmPickRequest? request, LogisticsService s, CancellationToken ct) =>
                s.ConfirmPickAsync(id, request?.Quantity, ct))
            .WithSummary("Confirma el retiro desde la web. Confirmar dos veces no descuenta dos veces.");

        g.MapPost("/picks/{id:guid}/cancel", (Guid id, LogisticsService s, CancellationToken ct) => s.CancelPickAsync(id, ct))
            .WithSummary("Apaga la luz o saca el retiro de la cola, sin descontar stock.");

        g.MapPost("/restocks", (RestockRequest request, LogisticsService s, CancellationToken ct) => s.RestockAsync(request, ct))
            .WithSummary("Repone stock de un producto.");

        g.MapGet("/operations", (DateTimeOffset? from, int? limit, LogisticsService s, CancellationToken ct) =>
                s.OperationsAsync(from, limit, ct))
            .WithSummary("Registro de retiros y reposiciones, con la duración de cada retiro.");

        return app;
    }
}
