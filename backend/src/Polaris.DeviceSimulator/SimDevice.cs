using System.Net;
using System.Net.Http.Json;
using System.Text.Json;

namespace Polaris.DeviceSimulator;

public sealed record SimLed(int Index, string Color, string Effect);

public sealed record SimContext(string? ActivePickId, bool ConfirmEnabled);

public sealed record SimConfig(double OccupiedBelowCm, double FreeAboveCm, int SampleIntervalMs, int EntranceChannel);

public sealed record SimDesiredState(
    string DeviceId,
    long Version,
    double Brightness,
    int FailSafeAfterMs,
    List<SimLed> Leds,
    SimContext? Context,
    SimConfig? Config);

/// <summary>
/// Cliente del protocolo de dispositivos, igual al que implementa el firmware del ESP32:
/// eventos por POST con bootId + seq, y estado deseado por GET con ?version=.
/// </summary>
public sealed class SimDevice : IDisposable
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    private readonly HttpClient _http;
    private readonly string _bootId = Guid.NewGuid().ToString("N")[..6];
    private long _seq;

    public SimDevice(string deviceId, string key, string apiBase)
    {
        DeviceId = deviceId;
        _http = new HttpClient { BaseAddress = new Uri(apiBase.TrimEnd('/') + "/"), Timeout = TimeSpan.FromSeconds(3) };
        _http.DefaultRequestHeaders.Add("X-Device-Key", key);
    }

    public string DeviceId { get; }
    public SimDesiredState? State { get; private set; }

    /// <summary>Consulta el estado deseado. Devuelve true si cambió.</summary>
    public async Task<bool> PollAsync(CancellationToken ct)
    {
        var url = $"api/devices/{DeviceId}/desired-state" + (State is null ? "" : $"?version={State.Version}");
        using var response = await _http.GetAsync(url, ct);
        if (response.StatusCode == HttpStatusCode.NotModified) return false;
        response.EnsureSuccessStatusCode();

        State = await response.Content.ReadFromJsonAsync<SimDesiredState>(Json, ct);
        return true;
    }

    public Task SendAsync(object payload, CancellationToken ct) => SendManyAsync([payload], ct);

    public async Task SendManyAsync(IEnumerable<object> payloads, CancellationToken ct)
    {
        var events = payloads.Select(p => WithSeq(p)).ToList();
        var batch = new { deviceId = DeviceId, bootId = _bootId, firmware = "sim-0.1.0", events };
        using var response = await _http.PostAsJsonAsync($"api/devices/{DeviceId}/events", batch, Json, ct);
        response.EnsureSuccessStatusCode();
    }

    public Task HeartbeatAsync(CancellationToken ct) =>
        SendAsync(new { type = "heartbeat", rssi = -48, freeHeap = 182_000L, ip = "simulador", appliedVersion = State?.Version }, ct);

    private Dictionary<string, object?> WithSeq(object payload)
    {
        var dict = JsonSerializer.Deserialize<Dictionary<string, object?>>(JsonSerializer.Serialize(payload, Json), Json)!;
        dict["seq"] = Interlocked.Increment(ref _seq);
        dict["uptimeMs"] = Environment.TickCount64;
        return dict;
    }

    public void Dispose() => _http.Dispose();
}
