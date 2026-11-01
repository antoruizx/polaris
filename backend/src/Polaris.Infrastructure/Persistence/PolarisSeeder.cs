using Microsoft.EntityFrameworkCore;
using Polaris.Domain.Devices;
using Polaris.Domain.Logistics;
using Polaris.Domain.Parking;

namespace Polaris.Infrastructure.Persistence;

/// <summary>
/// Datos iniciales: los mismos productos de la demo, con los casilleros renombrados a B-NN-NN,
/// y las 4 plazas del estacionamiento (A-01 la más cercana a la entrada).
/// </summary>
public static class PolarisSeeder
{
    public const string LogisticsDeviceId = "esp32-log-01";
    public const string ParkingDeviceId = "esp32-park-01";

    private const string Rack = "B";
    private const int Levels = 3;
    private const int SlotsPerLevel = 8;
    private const int ParkingSpots = 4;

    public static async Task SeedAsync(PolarisDbContext db, CancellationToken ct = default)
    {
        if (await db.Devices.AnyAsync(ct)) return;

        db.Devices.Add(new Device(LogisticsDeviceId, DeviceKind.Logistics, "Estantería B"));
        db.Devices.Add(new Device(ParkingDeviceId, DeviceKind.Parking, "Estacionamiento A"));

        // 24 compartimentos: B-01-01 … B-03-08. El LED es la posición en la tira, nivel por nivel.
        var compartments = new List<Compartment>();
        for (var level = 1; level <= Levels; level++)
        {
            for (var slot = 1; slot <= SlotsPerLevel; slot++)
            {
                var led = (level - 1) * SlotsPerLevel + (slot - 1);
                compartments.Add(new Compartment(Rack, level, slot, LogisticsDeviceId, led));
            }
        }

        db.Compartments.AddRange(compartments);

        // Producto, categoría, posición en la tira (0..23), stock, mínimo. Las posiciones 7, 15, 22 y 23 quedan vacías.
        var catalog = new (string Sku, string Name, string Category, int Position, int Stock, int Min)[]
        {
            ("PL-001", "Pilas AA alcalinas (pack x4)", "Electricidad", 0, 34, 10),
            ("PL-002", "Cinta aisladora negra 20 m", "Electricidad", 1, 18, 8),
            ("PL-003", "Lámpara LED E27 9 W", "Electricidad", 2, 6, 8),
            ("PL-004", "Precintos plásticos 200 mm (x100)", "Ferretería", 3, 22, 6),
            ("PL-005", "Tornillos autorroscantes 8x1\" (x50)", "Ferretería", 4, 40, 10),
            ("PL-006", "Tarugos 8 mm (x50)", "Ferretería", 5, 31, 10),
            ("PL-007", "Cutter profesional 18 mm", "Herramientas", 6, 9, 4),
            ("PL-008", "Guantes de nitrilo talle M (caja)", "Seguridad", 8, 3, 5),
            ("PL-009", "Cinta de embalar transparente", "Embalaje", 9, 48, 12),
            ("PL-010", "Film stretch 50 cm", "Embalaje", 10, 12, 4),
            ("PL-011", "Etiquetas térmicas 100x150 (rollo)", "Embalaje", 11, 15, 6),
            ("PL-012", "Resma A4 75 g", "Oficina", 12, 20, 8),
            ("PL-013", "Marcadores permanentes (x4)", "Oficina", 13, 0, 4),
            ("PL-014", "Abrochadora de escritorio", "Oficina", 14, 7, 3),
            ("PL-015", "Cargador USB-C 20 W", "Tecnología", 16, 11, 4),
            ("PL-016", "Mouse inalámbrico", "Tecnología", 17, 8, 3),
            ("PL-017", "Teclado USB español", "Tecnología", 18, 5, 3),
            ("PL-018", "Auriculares con micrófono", "Tecnología", 19, 4, 3),
            ("PL-019", "Cable HDMI 2 m", "Tecnología", 20, 14, 5),
            ("PL-020", "Pendrive 64 GB", "Tecnología", 21, 17, 6),
        };

        foreach (var item in catalog)
        {
            var product = new Product(item.Sku, item.Name, item.Category, item.Min);
            var stock = new StockItem(product, compartments[item.Position], item.Stock);
            product.Stock.Add(stock);
            db.Products.Add(product);
        }

        // 4 plazas en una fila. Order 1 = la más cercana a la entrada. Sensores 0..3, LEDs 0..3;
        // el canal 4 es el sensor de la entrada y el LED 4 el cartel (ver PolarisOptions).
        for (var i = 0; i < ParkingSpots; i++)
        {
            db.ParkingSpots.Add(new ParkingSpot(
                code: $"A-{i + 1:00}",
                row: 1,
                order: i + 1,
                deviceId: ParkingDeviceId,
                ledIndex: i,
                sensorChannel: i));
        }

        await db.SaveChangesAsync(ct);
    }
}
