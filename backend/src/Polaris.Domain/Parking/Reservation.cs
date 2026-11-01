using Polaris.Domain.Common;

namespace Polaris.Domain.Parking;

public enum ReservationStatus
{
    /// <summary>El auto entró pero no hay plaza libre: espera en la fila.</summary>
    Waiting,

    /// <summary>Plaza asignada; la luz guía al auto.</summary>
    Assigned,

    /// <summary>El auto estacionó.</summary>
    Fulfilled,

    /// <summary>Pasó el tiempo de espera sin que el auto llegara a la plaza.</summary>
    Expired,

    Cancelled
}

/// <summary>
/// Reserva por orden de llegada. Nace cuando el sensor de la entrada detecta un auto: si hay plaza,
/// se asigna la más cercana; si no, espera su turno. Se cumple cuando el sensor de la plaza detecta el auto.
/// </summary>
public sealed class Reservation
{
    private Reservation() { }

    public static Reservation Arrive(int number, DateTimeOffset now)
    {
        if (number < 1) throw DomainException.Validation("invalid_number", "El número de llegada empieza en 1.");

        return new Reservation
        {
            Id = Guid.NewGuid(),
            Number = number,
            CreatedAt = now,
            Status = ReservationStatus.Waiting
        };
    }

    public Guid Id { get; private set; }

    /// <summary>Número de llegada del día (Auto 1, Auto 2…). Ordena la fila.</summary>
    public int Number { get; private set; }

    public Guid? SpotId { get; private set; }
    public ParkingSpot? Spot { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset? AssignedAt { get; private set; }
    public DateTimeOffset? ExpiresAt { get; private set; }
    public ReservationStatus Status { get; private set; }
    public DateTimeOffset? ClosedAt { get; private set; }

    public string Label => $"Auto {Number}";

    public bool IsWaiting => Status == ReservationStatus.Waiting;

    public bool IsAssignedAt(DateTimeOffset now) =>
        Status == ReservationStatus.Assigned && ExpiresAt is { } e && e > now;

    public void Assign(ParkingSpot spot, DateTimeOffset now, TimeSpan hold)
    {
        if (Status != ReservationStatus.Waiting)
            throw DomainException.Conflict("reservation_not_waiting", $"{Label} ya tiene plaza o está cerrado.");

        Spot = spot;
        SpotId = spot.Id;
        AssignedAt = now;
        ExpiresAt = now + hold;
        Status = ReservationStatus.Assigned;
    }

    public bool Fulfill(DateTimeOffset now) => Close(ReservationStatus.Fulfilled, now, onlyAssigned: true);

    public bool Expire(DateTimeOffset now) => Close(ReservationStatus.Expired, now, onlyAssigned: true);

    public bool Cancel(DateTimeOffset now) => Close(ReservationStatus.Cancelled, now, onlyAssigned: false);

    private bool Close(ReservationStatus status, DateTimeOffset now, bool onlyAssigned)
    {
        var open = Status == ReservationStatus.Assigned || (!onlyAssigned && Status == ReservationStatus.Waiting);
        if (!open) return false;

        Status = status;
        ClosedAt = now;
        return true;
    }
}
