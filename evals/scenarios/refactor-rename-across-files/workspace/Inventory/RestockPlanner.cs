namespace Inventory;

// Suggests what to reorder; reads the StockService but never changes it.
public sealed class RestockPlanner(StockService stock, int minimum)
{
    public IEnumerable<string> ItemsToReorder(IEnumerable<string> skus) =>
        skus.Where(sku => stock.QuantityOf(sku) < minimum);
}
