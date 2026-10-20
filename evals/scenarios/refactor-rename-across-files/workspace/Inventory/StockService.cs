namespace Inventory;

/// <summary>Keeps the quantity of every item and refuses to go below zero.</summary>
public sealed class StockService
{
    private readonly Dictionary<string, int> _quantities = new(StringComparer.OrdinalIgnoreCase);

    public int QuantityOf(string sku) => _quantities.GetValueOrDefault(sku);

    public void Receive(string sku, int quantity)
    {
        if (quantity <= 0) throw new ArgumentOutOfRangeException(nameof(quantity));
        _quantities[sku] = QuantityOf(sku) + quantity;
    }

    public bool TryShip(string sku, int quantity)
    {
        if (QuantityOf(sku) < quantity) return false;
        _quantities[sku] = QuantityOf(sku) - quantity;
        return true;
    }
}
