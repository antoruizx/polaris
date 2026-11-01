namespace Polaris.DeviceSimulator;

public static class Simulations
{
    private const int PollMs = 250;
    private const int HeartbeatMs = 5000;
    private const int ParkingSpots = 4;

    private const string ColorOccupied = "#FF5B6B";

    private static readonly Random Rng = new();
    private static readonly object ConsoleLock = new();

    /// <summary>
    /// ESP32 de la estantería: aplica el estado deseado y, si hay una luz encendida,
    /// "aprieta el botón" entre 3 y 6 segundos después (como un operario retirando).
    /// </summary>
    public static async Task RunLogisticsAsync(SimDevice device, bool autoConfirm, CancellationToken ct)
    {
        string? scheduledPick = null;
        var confirmAt = DateTime.MaxValue;
        var nextHeartbeat = DateTime.UtcNow;

        while (!ct.IsCancellationRequested)
        {
            try
            {
                if (DateTime.UtcNow >= nextHeartbeat)
                {
                    await device.HeartbeatAsync(ct);
                    nextHeartbeat = DateTime.UtcNow.AddMilliseconds(HeartbeatMs);
                }

                if (await device.PollAsync(ct))
                {
                    Log("LOG", Describe(device.State));
                }

                var active = device.State?.Context?.ActivePickId;
                if (autoConfirm && active is not null && active != scheduledPick)
                {
                    scheduledPick = active;
                    confirmAt = DateTime.UtcNow.AddMilliseconds(Rng.Next(3000, 6000));
                }

                if (active is not null && active == scheduledPick && DateTime.UtcNow >= confirmAt)
                {
                    await device.SendAsync(new { type = "button.pressed", button = "confirm", activePickId = active }, ct);
                    Log("LOG", "botón de confirmación apretado");
                    confirmAt = DateTime.MaxValue;
                }
            }
            catch (HttpRequestException ex)
            {
                Log("LOG", $"API no disponible ({ex.Message}); reintento en 2 s");
                await Task.Delay(2000, ct);
            }
            catch (TaskCanceledException) when (!ct.IsCancellationRequested)
            {
                Log("LOG", "timeout; reintento");
            }

            await Task.Delay(PollMs, ct);
        }
    }

    /// <summary>
    /// ESP32 del estacionamiento: 4 sensores de plaza (canales 0..3) y el sensor de la entrada.
    /// Genera llegadas por la entrada; el auto guiado estaciona en su plaza 3 segundos después;
    /// cada tanto un auto se va.
    /// </summary>
    public static async Task RunParkingAsync(SimDevice device, bool autoTraffic, CancellationToken ct)
    {
        var occupied = new bool[ParkingSpots];
        var initialized = false;
        var parkAt = new Dictionary<int, DateTime>();
        var nextHeartbeat = DateTime.UtcNow;
        var nextTraffic = DateTime.UtcNow.AddSeconds(4);

        while (!ct.IsCancellationRequested)
        {
            try
            {
                if (DateTime.UtcNow >= nextHeartbeat)
                {
                    await device.HeartbeatAsync(ct);
                    nextHeartbeat = DateTime.UtcNow.AddMilliseconds(HeartbeatMs);
                }

                if (await device.PollAsync(ct))
                {
                    Log("PARK", Describe(device.State));
                }

                var state = device.State;
                if (state is null) { await Task.Delay(PollMs, ct); continue; }
                var entrance = state.Config?.EntranceChannel ?? ParkingSpots;

                if (!initialized)
                {
                    // Arranca reflejando lo que el servidor cree que está ocupado.
                    foreach (var led in state.Leds.Where(l => l.Index < ParkingSpots))
                    {
                        occupied[led.Index] = led.Color.Equals(ColorOccupied, StringComparison.OrdinalIgnoreCase);
                    }

                    initialized = true;
                }

                // Plazas que parpadean (guía): el auto llega 3 s después.
                foreach (var led in state.Leds.Where(l => l.Index < ParkingSpots && l.Effect == "blink"))
                {
                    if (!occupied[led.Index] && !parkAt.ContainsKey(led.Index))
                    {
                        parkAt[led.Index] = DateTime.UtcNow.AddSeconds(3);
                    }
                }

                foreach (var (channel, at) in parkAt.Where(p => DateTime.UtcNow >= p.Value).ToList())
                {
                    parkAt.Remove(channel);
                    await SetSpotAsync(device, occupied, channel, true, ct);
                }

                if (autoTraffic && DateTime.UtcNow >= nextTraffic)
                {
                    nextTraffic = DateTime.UtcNow.AddMilliseconds(Rng.Next(5000, 9000));
                    var busy = occupied.Count(o => o);
                    var arriveChance = busy <= 1 ? 0.75 : busy >= ParkingSpots ? 0.3 : 0.5;

                    if (Rng.NextDouble() < arriveChance)
                    {
                        // Auto pasa por el sensor de la entrada: flanco ocupado y luego libre.
                        await device.SendManyAsync(
                        [
                            new { type = "occupancy.changed", channel = entrance, occupied = true, distanceCm = 5.2 },
                            new { type = "occupancy.changed", channel = entrance, occupied = false, distanceCm = 30.0 }
                        ], ct);
                        Log("PARK", "auto detectado en la entrada");
                    }
                    else
                    {
                        var leaving = Enumerable.Range(0, ParkingSpots).Where(i => occupied[i]).ToList();
                        if (leaving.Count > 0)
                        {
                            await SetSpotAsync(device, occupied, leaving[Rng.Next(leaving.Count)], false, ct);
                        }
                    }
                }
            }
            catch (HttpRequestException ex)
            {
                Log("PARK", $"API no disponible ({ex.Message}); reintento en 2 s");
                await Task.Delay(2000, ct);
            }
            catch (TaskCanceledException) when (!ct.IsCancellationRequested)
            {
                Log("PARK", "timeout; reintento");
            }

            await Task.Delay(PollMs, ct);
        }
    }

    private static async Task SetSpotAsync(SimDevice device, bool[] occupied, int channel, bool value, CancellationToken ct)
    {
        occupied[channel] = value;
        var distance = value ? Math.Round(4 + Rng.NextDouble() * 3, 1) : Math.Round(20 + Rng.NextDouble() * 10, 1);
        await device.SendAsync(new { type = "occupancy.changed", channel, occupied = value, distanceCm = distance }, ct);
        Log("PARK", $"sensor {channel} → {(value ? "ocupado" : "libre")} ({distance} cm)");
    }

    private static string Describe(SimDesiredState? s)
    {
        if (s is null) return "sin estado";
        var leds = s.Leds.Count == 0
            ? "todos apagados"
            : string.Join("  ", s.Leds.Select(l => $"{l.Index}:{ColorName(l.Color)}{(l.Effect == "solid" ? "" : "/" + l.Effect)}"));
        return $"v{s.Version % 100000:00000} {leds}";
    }

    private static string ColorName(string hex) => hex.ToUpperInvariant() switch
    {
        "#3DDC8F" => "verde",
        "#FF5B6B" => "rojo",
        "#FFB547" => "ámbar",
        "#5EC8FF" => "celeste",
        "#F4D088" => "dorado",
        "#000000" => "off",
        _ => hex
    };

    private static void Log(string source, string message)
    {
        lock (ConsoleLock)
        {
            Console.WriteLine($"{DateTime.Now:HH:mm:ss.fff} [{source,-4}] {message}");
        }
    }
}
