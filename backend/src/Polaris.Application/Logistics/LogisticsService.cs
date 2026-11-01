using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Polaris.Application.Abstractions;
using Polaris.Application.Common;
using Polaris.Domain.Common;
using Polaris.Domain.Logistics;

namespace Polaris.Application.Logistics;

/// <summary>
/// Casos de uso de Logística. Los retiros se atienden por orden de llegada: una luz por estantería,
/// el resto espera en cola y se enciende al cerrarse el anterior.
/// </summary>
public sealed class LogisticsService(
    IPolarisDbContext db,
    IRealtimeNotifier realtime,
    TimeProvider time,
    PolarisOptions options,
    CommandGate gate,
    ILogger<LogisticsService> logger)
{
    // ---------- Consultas ----------

    public async Task<IReadOnlyList<ProductDto>> SearchAsync(string? query, CancellationToken ct)
    {
        var products = await ProductsWithStock().AsNoTracking().OrderBy(p => p.Sku).ToListAsync(ct);
        if (string.IsNullOrWhiteSpace(query))
        {
            return products.Select(x => ToDto(x)).ToList();
        }

        // Pocas decenas de productos en la maqueta: filtrar en memoria permite ignorar acentos sin extensiones de Postgres.
        var q = TextSearch.Normalize(query.Trim());
        return products
            .Where(p => TextSearch.Normalize($"{p.Name} {p.Sku} {p.Category}").Contains(q))
            .Select(x => ToDto(x))
            .ToList();
    }

    public async Task<IReadOnlyList<CompartmentDto>> CompartmentsAsync(CancellationToken ct)
    {
        var compartments = await db.Compartments.AsNoTracking().OrderBy(c => c.Code).ToListAsync(ct);
        var stock = await db.StockItems.AsNoTracking().Include(s => s.Product).ToListAsync(ct);
        var byCompartment = stock.GroupBy(s => s.CompartmentId).ToDictionary(g => g.Key, g => g.First());

        return compartments.Select(c =>
        {
            byCompartment.TryGetValue(c.Id, out var item);
            return new CompartmentDto(
                c.Code, c.Rack, c.Level, c.Slot, c.DeviceId, c.LedIndex,
                item?.Product.Sku, item?.Product.Name, item?.Quantity ?? 0);
        }).ToList();
    }

    /// <summary>Retiros abiertos (encendidos y en cola), en orden de llegada.</summary>
    public async Task<IReadOnlyList<PickDto>> OpenPicksAsync(CancellationToken ct)
    {
        var picks = await PicksWithDetails().AsNoTracking()
            .Where(p => p.Status == PickStatus.Lit || p.Status == PickStatus.Queued)
            .OrderBy(p => p.CreatedAt)
            .ToListAsync(ct);
        return picks.Select(x => ToDto(x)).ToList();
    }

    public async Task<IReadOnlyList<OperationDto>> OperationsAsync(DateTimeOffset? from, int? limit, CancellationToken ct)
    {
        var take = Math.Clamp(limit ?? 50, 1, 500);
        var query = db.InventoryMovements.AsNoTracking()
            .Include(m => m.Product)
            .Include(m => m.Compartment)
            .Include(m => m.PickOrder)
            .AsQueryable();

        if (from is { } f)
        {
            var fromUtc = f.ToUniversalTime();
            query = query.Where(m => m.CreatedAt >= fromUtc);
        }

        var movements = await query.OrderByDescending(m => m.CreatedAt).Take(take).ToListAsync(ct);
        return movements.Select(m => new OperationDto(
            m.Id, m.CreatedAt, Names.Of(m.Type), m.Product.Sku, m.Product.Name,
            m.Compartment.Code, m.Quantity, m.PickOrder?.DurationMs)).ToList();
    }

    // ---------- Comandos ----------

    /// <summary>Pide un retiro. Si la estantería está libre se enciende ya; si no, entra en la cola.</summary>
    public Task<PickDto> StartPickAsync(StartPickRequest request, CancellationToken ct) => gate.Run(async () =>
    {
        var quantity = request.Quantity ?? 1;
        if (quantity < 1) throw DomainException.Validation("invalid_quantity", "La cantidad debe ser al menos 1.");

        var product = await FindProductAsync(request.ProductSku, ct);
        var item = product.Stock
            .Where(s => s.Quantity > 0)
            .OrderBy(s => s.Compartment.Code)
            .FirstOrDefault()
            ?? throw DomainException.Conflict("out_of_stock", $"{product.Name} no tiene stock.");

        if (quantity > item.Quantity)
            throw DomainException.Conflict("insufficient_stock", $"Solo quedan {item.Quantity} unidades de {product.Name}.");

        var deviceId = item.Compartment.DeviceId;
        var rackBusy = await db.PickOrders
            .AnyAsync(p => p.Status == PickStatus.Lit && p.Compartment.DeviceId == deviceId, ct);

        var pick = PickOrder.Create(product, item.Compartment, quantity, time.GetUtcNow(), light: !rackBusy);
        db.PickOrders.Add(pick);
        await db.SaveChangesAsync(ct);

        var dto = ToDto(pick);
        await realtime.PickChanged(dto);
        return dto;
    }, ct);

    public Task<PickDto> ConfirmPickAsync(Guid pickId, int? quantity, CancellationToken ct) =>
        gate.Run(async () =>
        {
            var pick = await FindPickAsync(pickId, ct);
            return await ConfirmCoreAsync(pick, quantity, PickSource.Web, ct);
        }, ct);

    /// <summary>
    /// Confirmación desde el botón físico. Si el retiro que la placa veía encendido ya se cerró, se ignora:
    /// nunca se descuenta stock de otro producto por un botón apretado tarde.
    /// </summary>
    public Task<bool> ConfirmFromButtonAsync(string deviceId, Guid? activePickId, CancellationToken ct) =>
        gate.Run(async () =>
        {
            PickOrder? pick;
            if (activePickId is { } id)
            {
                pick = await PicksWithDetails().FirstOrDefaultAsync(p => p.Id == id, ct);
            }
            else
            {
                pick = await PicksWithDetails()
                    .Where(p => p.Status == PickStatus.Lit && p.Compartment.DeviceId == deviceId)
                    .OrderBy(p => p.LitAt)
                    .FirstOrDefaultAsync(ct);
            }

            if (pick is null || pick.Compartment.DeviceId != deviceId || !pick.IsLit)
            {
                logger.LogInformation("Botón de {Device} ignorado: el retiro {Pick} no está encendido.", deviceId, activePickId);
                return false;
            }

            await ConfirmCoreAsync(pick, null, PickSource.Button, ct);
            return true;
        }, ct);

    public Task<PickDto> CancelPickAsync(Guid pickId, CancellationToken ct) => gate.Run(async () =>
    {
        var pick = await FindPickAsync(pickId, ct);
        var wasLit = pick.IsLit;
        var now = time.GetUtcNow();

        if (pick.Cancel(now))
        {
            await db.SaveChangesAsync(ct);
            await realtime.PickChanged(ToDto(pick));
            if (wasLit) await LightNextAsync(pick.Compartment.DeviceId, now, ct);
        }

        return ToDto(pick);
    }, ct);

    public Task<ProductDto> RestockAsync(RestockRequest request, CancellationToken ct) => gate.Run(async () =>
    {
        if (request.Quantity < 1 || request.Quantity > 10_000)
            throw DomainException.Validation("invalid_quantity", "La cantidad a reponer debe estar entre 1 y 10000.");

        var product = await FindProductAsync(request.ProductSku, ct);
        StockItem? item;

        if (!string.IsNullOrWhiteSpace(request.CompartmentCode))
        {
            var code = request.CompartmentCode.Trim().ToUpperInvariant();
            var compartment = await db.Compartments.FirstOrDefaultAsync(c => c.Code == code, ct)
                ?? throw DomainException.NotFound("compartment_not_found", $"No existe el compartimento {code}.");

            item = product.Stock.FirstOrDefault(s => s.CompartmentId == compartment.Id);
            if (item is null)
            {
                item = new StockItem(product, compartment, 0);
                db.StockItems.Add(item);
                product.Stock.Add(item);
            }
        }
        else
        {
            item = product.Stock.OrderBy(s => s.Compartment.Code).FirstOrDefault()
                ?? throw DomainException.Validation("compartment_required",
                    $"{product.Name} no tiene compartimento asignado: indicá dónde reponer.");
        }

        item.Add(request.Quantity);
        db.InventoryMovements.Add(InventoryMovement.Restock(item, request.Quantity, time.GetUtcNow()));
        await db.SaveChangesAsync(ct);

        var dto = ToDto(product);
        await realtime.StockChanged(dto);
        return dto;
    }, ct);

    /// <summary>Apaga las luces que nadie confirmó a tiempo y enciende el siguiente de cada cola.</summary>
    public Task ExpireStalePicksAsync(CancellationToken ct) => gate.Run(async () =>
    {
        var now = time.GetUtcNow();
        var cutoff = now.AddSeconds(-options.PickTimeoutSeconds);
        var stale = await PicksWithDetails()
            .Where(p => p.Status == PickStatus.Lit && p.LitAt < cutoff)
            .ToListAsync(ct);

        if (stale.Count == 0) return;

        foreach (var pick in stale) pick.Expire(now);
        await db.SaveChangesAsync(ct);
        foreach (var pick in stale) await realtime.PickChanged(ToDto(pick));

        foreach (var deviceId in stale.Select(p => p.Compartment.DeviceId).Distinct())
        {
            await LightNextAsync(deviceId, now, ct);
        }
    }, ct);

    // ---------- Internos ----------

    private async Task<PickDto> ConfirmCoreAsync(PickOrder pick, int? quantity, PickSource source, CancellationToken ct)
    {
        if (pick.Status == PickStatus.Confirmed)
        {
            return ToDto(pick); // idempotente: el segundo confirmar no descuenta de nuevo
        }

        if (!pick.IsLit)
            throw DomainException.Conflict("pick_not_active", "Este retiro no tiene la luz encendida.");

        var qty = quantity ?? pick.Quantity;
        var item = pick.Product.Stock.FirstOrDefault(s => s.CompartmentId == pick.CompartmentId)
            ?? throw DomainException.Conflict("insufficient_stock", $"No hay stock de {pick.Product.Name} en {pick.Compartment.Code}.");

        item.Withdraw(qty);
        var now = time.GetUtcNow();
        pick.Confirm(qty, source, now);
        db.InventoryMovements.Add(InventoryMovement.Withdrawal(pick, qty, now));
        await db.SaveChangesAsync(ct);

        var dto = ToDto(pick);
        await realtime.PickChanged(dto);
        await realtime.StockChanged(ToDto(pick.Product));
        await LightNextAsync(pick.Compartment.DeviceId, now, ct);
        return dto;
    }

    /// <summary>Enciende el retiro más antiguo en cola de esa estantería, si la estantería quedó libre.</summary>
    private async Task LightNextAsync(string deviceId, DateTimeOffset now, CancellationToken ct)
    {
        var stillLit = await db.PickOrders
            .AnyAsync(p => p.Status == PickStatus.Lit && p.Compartment.DeviceId == deviceId, ct);
        if (stillLit) return;

        var next = await PicksWithDetails()
            .Where(p => p.Status == PickStatus.Queued && p.Compartment.DeviceId == deviceId)
            .OrderBy(p => p.CreatedAt)
            .FirstOrDefaultAsync(ct);
        if (next is null) return;

        next.Light(now);
        await db.SaveChangesAsync(ct);
        await realtime.PickChanged(ToDto(next));
    }

    private IQueryable<Product> ProductsWithStock() =>
        db.Products.Include(p => p.Stock).ThenInclude(s => s.Compartment);

    private IQueryable<PickOrder> PicksWithDetails() =>
        db.PickOrders
            .Include(p => p.Compartment)
            .Include(p => p.Product).ThenInclude(p => p.Stock).ThenInclude(s => s.Compartment);

    private async Task<Product> FindProductAsync(string sku, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(sku))
            throw DomainException.Validation("sku_required", "Indicá el código del producto.");

        var normalized = sku.Trim().ToUpperInvariant();
        return await ProductsWithStock().FirstOrDefaultAsync(p => p.Sku == normalized, ct)
            ?? throw DomainException.NotFound("product_not_found", $"No existe el producto {normalized}.");
    }

    private async Task<PickOrder> FindPickAsync(Guid id, CancellationToken ct) =>
        await PicksWithDetails().FirstOrDefaultAsync(p => p.Id == id, ct)
        ?? throw DomainException.NotFound("pick_not_found", "No existe ese retiro.");

    private static ProductDto ToDto(Product p) => new(
        p.Id, p.Sku, p.Name, p.Category, p.TotalStock, p.MinStock, Names.Of(p.StockState),
        p.Stock.OrderBy(s => s.Compartment.Code)
            .Select(s => new ProductLocationDto(s.Compartment.Code, s.Quantity))
            .ToList());

    private static PickDto ToDto(PickOrder p) => new(
        p.Id, p.Product.Sku, p.Product.Name, p.Compartment.Code, p.Compartment.DeviceId, p.Compartment.LedIndex,
        p.Quantity, Names.Of(p.Status), p.CreatedAt, p.LitAt, p.ClosedAt,
        p.Source is { } s ? Names.Of(s) : null, p.DurationMs, p.Product.TotalStock);
}
