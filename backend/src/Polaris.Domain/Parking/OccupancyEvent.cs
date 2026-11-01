namespace Polaris.Domain.Parking;

/// <summary>Cambio de ocupación reportado por un sensor. Alimenta ingresos/egresos y el historial.</summary>
public sealed class OccupancyEvent
{
    private OccupancyEvent() { }

    public OccupancyEvent(ParkingSpot spot, bool occupied, double? distanceCm, DateTimeOffset at)
    {
        Id = Guid.NewGuid();
        SpotId = spot.Id;
        Occupied = occupied;
        DistanceCm = distanceCm;
        At = at;
    }

    public Guid Id { get; private set; }
    public Guid SpotId { get; private set; }
    public ParkingSpot Spot { get; private set; } = null!;
    public bool Occupied { get; private set; }
    public double? DistanceCm { get; private set; }
    public DateTimeOffset At { get; private set; }
}
