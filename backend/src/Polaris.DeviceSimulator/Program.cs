using Polaris.DeviceSimulator;

// Simulador de los dos ESP32 de Polaris. Usa exactamente el protocolo del firmware real.
//   dotnet run --project src/Polaris.DeviceSimulator [-- http://localhost:5080] [--no-auto-confirm] [--no-traffic]

var api = args.FirstOrDefault(a => a.StartsWith("http", StringComparison.OrdinalIgnoreCase))
          ?? Environment.GetEnvironmentVariable("POLARIS_API")
          ?? "http://localhost:5080";
var autoConfirm = !args.Contains("--no-auto-confirm");
var autoTraffic = !args.Contains("--no-traffic");

using var cts = new CancellationTokenSource();
Console.CancelKeyPress += (_, e) => { e.Cancel = true; cts.Cancel(); };

Console.WriteLine($"Polaris · simulador de dispositivos → {api}");
Console.WriteLine($"  confirmación automática: {(autoConfirm ? "sí" : "no")} · tráfico automático: {(autoTraffic ? "sí" : "no")}");
Console.WriteLine("  Ctrl+C para salir.\n");

using var logistics = new SimDevice("esp32-log-01", Environment.GetEnvironmentVariable("POLARIS_LOG_KEY") ?? "dev-log-key", api);
using var parking = new SimDevice("esp32-park-01", Environment.GetEnvironmentVariable("POLARIS_PARK_KEY") ?? "dev-park-key", api);

try
{
    await Task.WhenAll(
        Simulations.RunLogisticsAsync(logistics, autoConfirm, cts.Token),
        Simulations.RunParkingAsync(parking, autoTraffic, cts.Token));
}
catch (OperationCanceledException)
{
    // salida normal
}

Console.WriteLine("Simulador detenido.");
