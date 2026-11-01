namespace Polaris.Application.Common;

/// <summary>Sección "Polaris" de appsettings.json.</summary>
public sealed class PolarisOptions
{
    public int PickTimeoutSeconds { get; set; } = 60;
    public int ConfirmedFlashMs { get; set; } = 1600;
    /// <summary>Tiempo que una plaza queda reservada para el auto que entró, mientras la luz lo guía.</summary>
    public int ArrivalHoldSeconds { get; set; } = 120;

    /// <summary>Canal del sensor ultrasónico ubicado en la entrada del estacionamiento (las plazas usan 0..3).</summary>
    public int ParkingEntranceChannel { get; set; } = 4;

    /// <summary>LED del cartel de la entrada: verde si hay lugar, rojo si está completo.</summary>
    public int ParkingEntranceLedIndex { get; set; } = 4;
    public int DeviceOfflineSeconds { get; set; } = 15;
    public double LedBrightness { get; set; } = 0.4;
    public int FailSafeAfterMs { get; set; } = 5000;
    public double OccupiedBelowCm { get; set; } = 8;
    public double FreeAboveCm { get; set; } = 11;
    public int SampleIntervalMs { get; set; } = 60;
    public string TimeZone { get; set; } = "America/Argentina/Buenos_Aires";
    public string[] WebOrigins { get; set; } = ["http://localhost:5173"];

    /// <summary>Clave por dispositivo, enviada en el encabezado X-Device-Key.</summary>
    public Dictionary<string, string> DeviceKeys { get; set; } = new();
}
