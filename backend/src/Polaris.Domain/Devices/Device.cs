namespace Polaris.Domain.Devices;

public enum DeviceKind
{
    Logistics,
    Parking
}

/// <summary>
/// Un ESP32 físico (o el simulador). La presencia en línea se calcula en memoria; acá queda lo que vale persistir.
/// </summary>
public sealed class Device
{
    private Device() { }

    public Device(string id, DeviceKind kind, string name)
    {
        Id = id;
        Kind = kind;
        Name = name;
    }

    public string Id { get; private set; } = "";
    public DeviceKind Kind { get; private set; }
    public string Name { get; private set; } = "";
    public string? Firmware { get; private set; }
    public string? Ip { get; private set; }
    public int? Rssi { get; private set; }
    public long? FreeHeap { get; private set; }
    public DateTimeOffset? LastHeartbeatAt { get; private set; }

    /// <summary>Identificador del arranque actual; cambia en cada reinicio de la placa.</summary>
    public string? BootId { get; private set; }

    /// <summary>Último número de secuencia procesado dentro de <see cref="BootId"/>.</summary>
    public long LastSeq { get; private set; }

    /// <summary>
    /// Devuelve false si el evento ya se procesó (reintento duplicado). Un BootId nuevo reinicia la secuencia.
    /// </summary>
    public bool AcceptSequence(string bootId, long seq)
    {
        if (bootId != BootId)
        {
            BootId = bootId;
            LastSeq = seq;
            return true;
        }

        if (seq <= LastSeq)
        {
            return false;
        }

        LastSeq = seq;
        return true;
    }

    public void ReportFirmware(string? firmware)
    {
        if (!string.IsNullOrWhiteSpace(firmware))
        {
            Firmware = firmware;
        }
    }

    public void ApplyHeartbeat(string? ip, int? rssi, long? freeHeap, DateTimeOffset now)
    {
        Ip = ip ?? Ip;
        Rssi = rssi ?? Rssi;
        FreeHeap = freeHeap ?? FreeHeap;
        LastHeartbeatAt = now;
    }
}
