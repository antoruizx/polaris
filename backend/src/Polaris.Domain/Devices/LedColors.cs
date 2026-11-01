namespace Polaris.Domain.Devices;

/// <summary>Colores acordados en el documento de arquitectura (los mismos de la demo).</summary>
public static class LedColors
{
    public const string Off = "#000000";
    public const string Free = "#3DDC8F";
    public const string Occupied = "#FF5B6B";
    public const string Reserved = "#FFB547";
    public const string Guide = "#5EC8FF";
    public const string Pick = "#F4D088";
    public const string Confirmed = "#3DDC8F";
}

public static class LedEffects
{
    public const string Solid = "solid";
    public const string Blink = "blink";
    public const string Pulse = "pulse";
}
