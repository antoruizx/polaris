namespace Polaris.Domain.Logistics;

/// <summary>
/// Compartimento físico. Código B-01-01 = estantería B, nivel 01, compartimento 01.
/// El vínculo con el hardware (placa + índice de LED) se guarda acá y se puede cambiar sin tocar firmware.
/// </summary>
public sealed class Compartment
{
    private Compartment() { }

    public Compartment(string rack, int level, int slot, string deviceId, int ledIndex)
    {
        if (string.IsNullOrWhiteSpace(rack)) throw new ArgumentException("Estantería requerida.", nameof(rack));
        if (level < 1 || slot < 1) throw new ArgumentException("Nivel y compartimento empiezan en 1.");

        Id = Guid.NewGuid();
        Rack = rack.Trim().ToUpperInvariant();
        Level = level;
        Slot = slot;
        Code = FormatCode(Rack, level, slot);
        DeviceId = deviceId;
        LedIndex = ledIndex;
    }

    public Guid Id { get; private set; }
    public string Code { get; private set; } = "";
    public string Rack { get; private set; } = "";
    public int Level { get; private set; }
    public int Slot { get; private set; }
    public string DeviceId { get; private set; } = "";
    public int LedIndex { get; private set; }

    public static string FormatCode(string rack, int level, int slot) => $"{rack}-{level:00}-{slot:00}";
}
