namespace Shop;

public static class OrderPricing
{
    private const decimal BulkThreshold = 1000m;
    private const decimal BulkRate = 0.10m;
    private const decimal LoyaltyRate = 0.05m;
    private const decimal MaxDiscountRate = 0.12m;

    public static decimal Discount(Order order)
    {
        var rate = 0m;
        if (order.Subtotal >= BulkThreshold) rate += BulkRate;
        if (order.IsLoyalCustomer) rate += LoyaltyRate;
        return Math.Round(order.Subtotal * Math.Min(rate, MaxDiscountRate), 2, MidpointRounding.ToEven);
    }

    public static decimal Total(Order order) => order.Subtotal - Discount(order) + Shipping.Cost(order);
}
