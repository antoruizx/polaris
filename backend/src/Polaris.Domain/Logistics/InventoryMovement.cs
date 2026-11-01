namespace Polaris.Domain.Logistics;

public enum MovementType
{
    Withdrawal,
    Restock
}

/// <summary>Libro inmutable de movimientos de inventario.</summary>
public sealed class InventoryMovement
{
    private InventoryMovement() { }

    public static InventoryMovement Withdrawal(PickOrder pick, int quantity, DateTimeOffset now) => new()
    {
        Id = Guid.NewGuid(),
        Type = MovementType.Withdrawal,
        ProductId = pick.ProductId,
        CompartmentId = pick.CompartmentId,
        PickOrderId = pick.Id,
        Quantity = quantity,
        CreatedAt = now
    };

    public static InventoryMovement Restock(StockItem item, int quantity, DateTimeOffset now) => new()
    {
        Id = Guid.NewGuid(),
        Type = MovementType.Restock,
        ProductId = item.ProductId,
        CompartmentId = item.CompartmentId,
        Quantity = quantity,
        CreatedAt = now
    };

    public Guid Id { get; private set; }
    public MovementType Type { get; private set; }
    public Guid ProductId { get; private set; }
    public Product Product { get; private set; } = null!;
    public Guid CompartmentId { get; private set; }
    public Compartment Compartment { get; private set; } = null!;
    public Guid? PickOrderId { get; private set; }
    public PickOrder? PickOrder { get; private set; }
    public int Quantity { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
}
