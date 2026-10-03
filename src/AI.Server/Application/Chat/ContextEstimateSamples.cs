namespace AI.Application.Chat;

using System.Collections.Concurrent;

/// <summary>Reported provider input counts paired with estimates; no prompt content is retained.</summary>
public interface IContextEstimateSamples
{
    void Add(Guid? connectionId, string baseUrl, string model, ContextEstimateSample sample);
    IReadOnlyList<ContextEstimateSample> Read(Guid? connectionId, string baseUrl, string model);
}

public sealed record ContextEstimateSample(long EstimatedTokens, long ReportedTokens);

/// <summary>Shared bounded observations, isolated by connection, endpoint and requested model.</summary>
public sealed class ContextEstimateSamples : IContextEstimateSamples
{
    private readonly ConcurrentDictionary<Key, ConcurrentQueue<ContextEstimateSample>> _samples = new();
    private readonly ConcurrentQueue<Key> _keys = new();

    public void Add(Guid? connectionId, string baseUrl, string model, ContextEstimateSample sample)
    {
        if (sample.EstimatedTokens <= 0 || sample.ReportedTokens <= 0) return;
        var key = new Key(connectionId, baseUrl.TrimEnd('/'), model);
        var values = _samples.GetOrAdd(key, _ =>
        {
            _keys.Enqueue(key);
            return new ConcurrentQueue<ContextEstimateSample>();
        });
        values.Enqueue(sample);
        while (values.Count > 16) values.TryDequeue(out _);
        while (_samples.Count > 256 && _keys.TryDequeue(out var oldest)) _samples.TryRemove(oldest, out _);
    }

    public IReadOnlyList<ContextEstimateSample> Read(Guid? connectionId, string baseUrl, string model) =>
        _samples.TryGetValue(new Key(connectionId, baseUrl.TrimEnd('/'), model), out var values) ? values.ToArray() : [];

    private readonly record struct Key(Guid? ConnectionId, string BaseUrl, string Model);
}
