using Polaris.Application.Parking;

namespace Polaris.Api.Endpoints;

public static class ParkingEndpoints
{
    public static IEndpointRouteBuilder MapParkingEndpoints(this IEndpointRouteBuilder app)
    {
        var g = app.MapGroup("/api/parking").WithTags("Estacionamiento");

        g.MapGet("/spots", (ParkingService s, CancellationToken ct) => s.SpotsAsync(ct))
            .WithSummary("Las plazas con su estado y la reserva asignada, si la hay.");

        g.MapGet("/reservations", (ParkingService s, CancellationToken ct) => s.QueueAsync(ct))
            .WithSummary("Autos guiados y autos esperando lugar, en orden de llegada.");

        g.MapPost("/arrivals", async (ParkingService s, CancellationToken ct) =>
            {
                var reservation = await s.ArriveAsync(ct);
                return Results.Created($"/api/parking/reservations/{reservation.Id}", reservation);
            })
            .WithSummary("Auto en la entrada (lo mismo que dispara el sensor de entrada). Asigna plaza o lo pone en la fila.");

        g.MapDelete("/reservations/{id:guid}", (Guid id, ParkingService s, CancellationToken ct) => s.CancelReservationAsync(id, ct))
            .WithSummary("Cancela una reserva (por ejemplo, un auto que se fue de la fila).");

        g.MapGet("/metrics", (DateOnly? date, ParkingService s, CancellationToken ct) => s.MetricsAsync(date, ct))
            .WithSummary("Ocupación actual, llegadas, ingresos y egresos del día.");

        return app;
    }
}
