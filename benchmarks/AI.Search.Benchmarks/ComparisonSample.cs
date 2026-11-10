namespace AI.Search.Benchmarks;

using System.Globalization;

/// <summary>Timed runs of one tool on one workload, and what that tool found.</summary>
internal sealed record ComparisonSample(int Runs, double MedianMilliseconds, double MeanMilliseconds, int Matches)
{
    /// <summary>Throughput of the median run over <paramref name="bytes"/> of input.</summary>
    public double MegabytesPerSecond(double bytes) =>
        MedianMilliseconds <= 0 ? 0 : bytes / (1024 * 1024) / (MedianMilliseconds / 1000);

    public static ComparisonSample Create(IEnumerable<double> samples, int matches)
    {
        var ordered = samples.OrderBy(value => value).ToArray();
        if (ordered.Length == 0)
        {
            return new ComparisonSample(0, 0, 0, matches);
        }

        var median = ordered.Length % 2 == 1
            ? ordered[ordered.Length / 2]
            : (ordered[(ordered.Length / 2) - 1] + ordered[ordered.Length / 2]) / 2;
        return new ComparisonSample(ordered.Length, median, ordered.Average(), matches);
    }

    public override string ToString() =>
        string.Create(CultureInfo.InvariantCulture, $"median {MedianMilliseconds,9:F1} ms");
}