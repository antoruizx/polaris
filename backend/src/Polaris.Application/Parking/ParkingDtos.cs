namespace Polaris.Application.Parking;

public sealed record ReservationDto(
    Guid Id,
    int Number,
    string Label,
    string Status,
    string? SpotCode,
    DateTimeOffset CreatedAt,
    DateTimeOffset? AssignedAt,
    DateTimeOffset? ExpiresAt,
    DateTimeOffset? ClosedAt);

public sealed record SpotDto(
    string Code,
    int Row,
    int Order,
    string State,
    int LedIndex,
    int SensorChannel,
    double? DistanceCm,
    DateTimeOffset? LastChangeAt,
    ReservationDto? Reservation);

public sealed record ParkingMetricsDto(
    DateOnly Date,
    int Total,
    int Occupied,
    int Reserved,
    int Free,
    int Waiting,
    int OccupancyPercent,
    int ArrivalsToday,
    int EntriesToday,
    int ExitsToday);

public sealed record OccupancyRequest(bool Occupied);
