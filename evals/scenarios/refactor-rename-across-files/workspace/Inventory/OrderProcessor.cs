namespace Inventory;

public sealed class OrderProcessor(StockService stockService)
{
    public IReadOnlyList<string> Process(IEnumerable<(string Sku, int Quantity)> lines)
    {
        var rejected = new List<string>();
        foreach (var (sku, quantity) in lines)
            if (!stockService.TryShip(sku, quantity)) rejected.Add(sku);
        return rejected;
    }
}
