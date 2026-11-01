using Polaris.Domain.Common;

namespace Polaris.Domain.Logistics;

/// <summary>Cantidad de un producto en un compartimento.</summary>
public sealed class StockItem
{
    private StockItem() { }

    public StockItem(Product product, Compartment compartment, int quantity)
    {
        if (quantity < 0) throw DomainException.Validation("invalid_quantity", "La cantidad no puede ser negativa.");

        Id = Guid.NewGuid();
        Product = product;
        ProductId = product.Id;
        Compartment = compartment;
        CompartmentId = compartment.Id;
        Quantity = quantity;
    }

    public Guid Id { get; private set; }
    public Guid ProductId { get; private set; }
    public Product Product { get; private set; } = null!;
    public Guid CompartmentId { get; private set; }
    public Compartment Compartment { get; private set; } = null!;
    public int Quantity { get; private set; }

    public void Withdraw(int quantity)
    {
        if (quantity < 1) throw DomainException.Validation("invalid_quantity", "La cantidad debe ser al menos 1.");
        if (quantity > Quantity)
            throw DomainException.Conflict("insufficient_stock", $"Solo quedan {Quantity} unidades en {Compartment.Code}.");

        Quantity -= quantity;
    }

    public void Add(int quantity)
    {
        if (quantity < 1) throw DomainException.Validation("invalid_quantity", "La cantidad debe ser al menos 1.");
        Quantity += quantity;
    }
}
