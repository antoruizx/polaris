namespace Polaris.Domain.Logistics;

public sealed class Product
{
    private Product() { }

    public Product(string sku, string name, string category, int minStock)
    {
        Id = Guid.NewGuid();
        Sku = sku;
        Name = name;
        Category = category;
        MinStock = minStock;
    }

    public Guid Id { get; private set; }
    public string Sku { get; private set; } = "";
    public string Name { get; private set; } = "";
    public string Category { get; private set; } = "";
    public int MinStock { get; private set; }

    public List<StockItem> Stock { get; private set; } = [];

    public int TotalStock => Stock.Sum(s => s.Quantity);

    public StockState StockState => StockStates.Of(TotalStock, MinStock);
}

public enum StockState
{
    Ok,
    Low,
    Out
}

public static class StockStates
{
    public static StockState Of(int stock, int minStock) =>
        stock == 0 ? StockState.Out : stock < minStock ? StockState.Low : StockState.Ok;
}
