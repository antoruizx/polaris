namespace Polaris.Domain.Parking;

public enum SpotState
{
    Free,
    Occupied,

    /// <summary>Asignada a un auto que acaba de entrar: la luz lo guía hasta acá.</summary>
    Reserved,

    OutOfService
}

public sealed class ParkingSpot
{
    private ParkingSpot() { }

    public ParkingSpot(string code, int row, int order, string deviceId, int ledIndex, int sensorChannel)
    {
        Id = Guid.NewGuid();
        Code = code;
        Row = row;
        Order = order;
        DeviceId = deviceId;
        LedIndex = ledIndex;
        SensorChannel = sensorChannel;
    }

    public Guid Id { get; private set; }

    /// <summary>A-01 … A-04.</summary>
    public string Code { get; private set; } = "";

    public int Row { get; private set; }

    /// <summary>Cercanía a la entrada: 1 = la más cercana. Define cuál se asigna primero.</summary>
    public int Order { get; private set; }

    public string DeviceId { get; private set; } = "";
    public int LedIndex { get; private set; }
    public int SensorChannel { get; private set; }

    public bool Occupied { get; private set; }
    public bool OutOfService { get; private set; }
    public double? LastDistanceCm { get; private set; }
    public DateTimeOffset? LastChangeAt { get; private set; }

    /// <summary>Prioridad: fuera de servicio, ocupada, reservada (guiando), libre.</summary>
    public SpotState StateAt(DateTimeOffset now, Reservation? activeReservation)
    {
        if (OutOfService) return SpotState.OutOfService;
        if (Occupied) return SpotState.Occupied;
        if (activeReservation is not null && activeReservation.IsAssignedAt(now)) return SpotState.Reserved;
        return SpotState.Free;
    }

    /// <summary>Aplica una lectura del sensor. Devuelve true si cambió el estado de ocupación.</summary>
    public bool ApplyOccupancy(bool occupied, double? distanceCm, DateTimeOffset now)
    {
        LastDistanceCm = distanceCm;
        if (Occupied == occupied) return false;

        Occupied = occupied;
        LastChangeAt = now;
        return true;
    }

    public void SetOutOfService(bool value) => OutOfService = value;
}
