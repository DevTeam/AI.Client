namespace Shop;

public sealed record OrderLine(string Sku, int Quantity, decimal UnitPrice)
{
    public decimal Total => Quantity * UnitPrice;
}

public sealed record Order(string CustomerId, IReadOnlyList<OrderLine> Lines, bool IsLoyalCustomer)
{
    public decimal Subtotal => Lines.Sum(line => line.Total);
}
