using System.Collections.Concurrent;

namespace Polaris.Application.Devices;

/// <summary>
/// Presencia en memoria: cada consulta del ESP32 (cada 250 ms) marca "visto ahora" sin escribir en la base.
/// </summary>
public sealed class DeviceRegistry
{
    private sealed class Live
    {
        public DateTimeOffset LastSeenAt;
        public bool ReportedOnline;
    }

    private readonly ConcurrentDictionary<string, Live> _live = new();

    public void Touch(string deviceId, DateTimeOffset now)
    {
        var live = _live.GetOrAdd(deviceId, _ => new Live());
        lock (live) live.LastSeenAt = now;
    }

    public DateTimeOffset? LastSeen(string deviceId)
    {
        if (!_live.TryGetValue(deviceId, out var live)) return null;
        lock (live) return live.LastSeenAt == default ? null : live.LastSeenAt;
    }

    public bool IsOnline(string deviceId, DateTimeOffset now, TimeSpan threshold) =>
        LastSeen(deviceId) is { } seen && now - seen <= threshold;

    /// <summary>Guarda el último estado avisado a la web. Devuelve true si cambió (hay que avisar).</summary>
    public bool SetReported(string deviceId, bool online)
    {
        var live = _live.GetOrAdd(deviceId, _ => new Live());
        lock (live)
        {
            if (live.ReportedOnline == online) return false;
            live.ReportedOnline = online;
            return true;
        }
    }
}
