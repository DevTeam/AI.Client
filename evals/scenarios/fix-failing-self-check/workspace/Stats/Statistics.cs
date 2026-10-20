public static class Statistics
{
    public static double Mean(IReadOnlyList<double> values)
    {
        if (values.Count == 0) throw new ArgumentException("No values.", nameof(values));
        return values.Sum() / values.Count;
    }

    public static double Median(IReadOnlyList<double> values)
    {
        if (values.Count == 0) throw new ArgumentException("No values.", nameof(values));
        var middle = values.Count / 2;
        return values.Count % 2 == 1
            ? values[middle]
            : (values[middle - 1] + values[middle]) / 2;
    }
}
