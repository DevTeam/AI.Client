public static class ReportBuilder
{
    public static IEnumerable<string> Build(IReadOnlyList<Sale> sales)
    {
        yield return "Region  Units  Revenue";
        foreach (var region in sales.GroupBy(sale => sale.Region).OrderBy(group => group.Key))
            yield return $"{region.Key,-7} {region.Sum(sale => sale.Units),5}  {region.Sum(sale => sale.Revenue),7:0.00}";
    }
}
