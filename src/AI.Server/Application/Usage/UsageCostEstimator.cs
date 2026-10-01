namespace AI.Application.Usage;

using System.Collections.Concurrent;
using AI.Contracts.Usage;

/// <summary>
/// Works out what a request cost from what the endpoint quoted for earlier requests of the same
/// connection. Keyed by the connection alone: the model name a response reports often differs from
/// the one the request named, and a request the endpoint reported nothing for has only the latter. Some gateways quote a cost on only some of their responses; without this
/// a chat on such a gateway shows a total that covers a few of its requests and says nothing about
/// the rest.
/// </summary>
public interface IUsageCostEstimator
{
    void Learn(Guid connectionId, TokenCounts tokens, decimal cost);

    /// <summary>The estimated cost, or null while nothing has been quoted for the connection.</summary>
    decimal? Estimate(Guid connectionId, TokenCounts tokens);

    /// <summary>True once the connection has a quote to learn from.</summary>
    bool Knows(Guid connectionId);
}

public sealed class UsageCostEstimator : IUsageCostEstimator
{
    /// <summary>The most recent quotes kept for each connection.</summary>
    private const int MaximumSamples = 200;

    private readonly ConcurrentDictionary<Guid, Samples> _samples = new();

    public void Learn(Guid connectionId, TokenCounts tokens, decimal cost)
    {
        ArgumentNullException.ThrowIfNull(tokens);
        if (cost < 0 || tokens.InputTokens + tokens.OutputTokens <= 0) return;
        _samples.GetOrAdd(connectionId, _ => new Samples()).Add(Features(tokens), (double)cost);
    }

    public bool Knows(Guid connectionId) => _samples.ContainsKey(connectionId);

    public decimal? Estimate(Guid connectionId, TokenCounts tokens)
    {
        ArgumentNullException.ThrowIfNull(tokens);
        if (!_samples.TryGetValue(connectionId, out var samples)) return null;
        var rates = samples.Rates();
        if (rates is null) return null;
        var features = Features(tokens);
        var cost = rates[0] * features[0] + rates[1] * features[1] + rates[2] * features[2];
        return cost >= 0 ? decimal.Round((decimal)cost, 8) : null;
    }

    /// <summary>Fresh input, cached input and output, in millions: the three things a provider prices apart.</summary>
    private static double[] Features(TokenCounts tokens)
    {
        var cached = Math.Min(tokens.CachedInputTokens, tokens.InputTokens);
        return [(tokens.InputTokens - cached) / 1e6, cached / 1e6, tokens.OutputTokens / 1e6];
    }

    private sealed class Samples
    {
        private readonly Queue<(double[] Features, double Cost)> _items = new();
        private double[]? _rates;

        public void Add(double[] features, double cost)
        {
            lock (_items)
            {
                _items.Enqueue((features, cost));
                if (_items.Count > MaximumSamples) _items.Dequeue();
                _rates = null;
            }
        }

        public double[]? Rates()
        {
            lock (_items)
                return _rates ??= Fit(_items.ToArray());
        }

        /// <summary>
        /// Least squares over the kinds of tokens the quotes vary in. A fit that comes out negative
        /// or cannot be solved — too few quotes, or all alike — falls back to one rate for every token.
        /// </summary>
        private static double[]? Fit((double[] Features, double Cost)[] items)
        {
            if (items.Length == 0) return null;
            var used = Enumerable.Range(0, 3).Where(column => items.Any(item => item.Features[column] > 0)).ToArray();
            if (items.Length >= used.Length + 2 && Solve(items, used) is { } solved && solved.All(rate => rate >= 0))
            {
                var rates = new double[3];
                for (var index = 0; index < used.Length; index++) rates[used[index]] = solved[index];
                // A kind of token none of the quotes had is charged as fresh input.
                for (var column = 0; column < 3; column++)
                    if (!used.Contains(column)) rates[column] = rates[0];
                return rates;
            }
            var tokens = items.Sum(item => item.Features.Sum());
            if (tokens <= 0) return null;
            var blended = items.Sum(item => item.Cost) / tokens;
            return [blended, blended, blended];
        }

        private static double[]? Solve((double[] Features, double Cost)[] items, int[] used)
        {
            var size = used.Length;
            if (size == 0) return null;
            // The normal equations, solved by Gaussian elimination with partial pivoting.
            var matrix = new double[size, size + 1];
            foreach (var (features, cost) in items)
                for (var row = 0; row < size; row++)
                {
                    for (var column = 0; column < size; column++)
                        matrix[row, column] += features[used[row]] * features[used[column]];
                    matrix[row, size] += features[used[row]] * cost;
                }
            for (var pivot = 0; pivot < size; pivot++)
            {
                var best = pivot;
                for (var row = pivot + 1; row < size; row++)
                    if (Math.Abs(matrix[row, pivot]) > Math.Abs(matrix[best, pivot])) best = row;
                if (Math.Abs(matrix[best, pivot]) < 1e-12) return null;
                for (var column = 0; column <= size; column++)
                    (matrix[pivot, column], matrix[best, column]) = (matrix[best, column], matrix[pivot, column]);
                for (var row = 0; row < size; row++)
                {
                    if (row == pivot) continue;
                    var factor = matrix[row, pivot] / matrix[pivot, pivot];
                    for (var column = pivot; column <= size; column++) matrix[row, column] -= factor * matrix[pivot, column];
                }
            }
            var result = new double[size];
            for (var row = 0; row < size; row++) result[row] = matrix[row, size] / matrix[row, row];
            return result;
        }
    }
}
