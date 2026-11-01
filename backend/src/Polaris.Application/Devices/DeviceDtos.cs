namespace Polaris.Application.Devices;

public sealed record DeviceDto(
    string Id,
    string Kind,
    string Name,
    bool Online,
    DateTimeOffset? LastSeenAt,
    string? Firmware,
    string? Ip,
    int? Rssi);

/// <summary>Una línea de la consola de tráfico de la web.</summary>
public sealed record TrafficDto(DateTimeOffset At, string Source, string Method, string Path, int Status, string? Summary);

// ---------- Protocolo ESP32: estado deseado ----------

public sealed record LedDto(int Index, string Color, string Effect);

public sealed record LedDefaultDto(string Color);

public sealed record DesiredContextDto(string? ActivePickId, bool ConfirmEnabled);

public sealed record SensorConfigDto(double OccupiedBelowCm, double FreeAboveCm, int SampleIntervalMs, int EntranceChannel);

public sealed record DesiredStateDto(
    string DeviceId,
    long Version,
    double Brightness,
    int FailSafeAfterMs,
    LedDefaultDto Default,
    IReadOnlyList<LedDto> Leds,
    DesiredContextDto? Context,
    SensorConfigDto? Config);

// ---------- Protocolo ESP32: eventos ----------

public sealed record DeviceEventDto(
    long Seq,
    string Type,
    long? UptimeMs,
    int? Channel,
    bool? Occupied,
    double? DistanceCm,
    string? Button,
    string? ActivePickId,
    int? Rssi,
    long? FreeHeap,
    string? Ip,
    long? AppliedVersion,
    string? Code,
    string? Detail);

public sealed record DeviceEventBatch(string? DeviceId, string BootId, string? Firmware, IReadOnlyList<DeviceEventDto>? Events);

public sealed record AckDto(long Ack, int Processed, int Duplicates);

public static class DeviceEventTypes
{
    public const string OccupancyChanged = "occupancy.changed";
    public const string ButtonPressed = "button.pressed";
    public const string Heartbeat = "heartbeat";
    public const string Error = "device.error";
}
