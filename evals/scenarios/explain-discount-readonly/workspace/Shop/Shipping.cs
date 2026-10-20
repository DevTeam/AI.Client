namespace Shop;

public static class Shipping
{
    public static decimal Cost(Order order) => order.Subtotal >= 500m ? 0m : 25m;
}
