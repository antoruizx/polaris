namespace Polaris.Application.Logistics;

public sealed record ProductLocationDto(string CompartmentCode, int Quantity);

public sealed record ProductDto(
    Guid Id,
    string Sku,
    string Name,
    string Category,
    int Stock,
    int MinStock,
    string StockState,
    IReadOnlyList<ProductLocationDto> Locations);

public sealed record CompartmentDto(
    string Code,
    string Rack,
    int Level,
    int Slot,
    string DeviceId,
    int LedIndex,
    string? ProductSku,
    string? ProductName,
    int Quantity);

public sealed record PickDto(
    Guid Id,
    string ProductSku,
    string ProductName,
    string CompartmentCode,
    string DeviceId,
    int LedIndex,
    int Quantity,
    string Status,
    DateTimeOffset CreatedAt,
    DateTimeOffset? LitAt,
    DateTimeOffset? ClosedAt,
    string? Source,
    long? DurationMs,
    int RemainingStock);

public sealed record OperationDto(
    Guid Id,
    DateTimeOffset At,
    string Type,
    string ProductSku,
    string ProductName,
    string CompartmentCode,
    int Quantity,
    long? DurationMs);

public sealed record StartPickRequest(string ProductSku, int? Quantity);

public sealed record ConfirmPickRequest(int? Quantity);

public sealed record RestockRequest(string ProductSku, string? CompartmentCode, int Quantity);
