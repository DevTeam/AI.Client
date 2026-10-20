public sealed record Sale(string Region, string Product, int Units, decimal Revenue);

public static class SalesData
{
    public static IReadOnlyList<Sale> Load() =>
    [
        new("North", "Widget", 120, 2400m),
        new("North", "Gadget", 40, 3200m),
        new("South", "Widget", 75, 1500m),
        new("South", "Gizmo", 10, 990m),
        new("West", "Gadget", 55, 4400m)
    ];
}
