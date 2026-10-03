namespace AI.Infrastructure.Chat;

using AI.Application.Chat;
using Microsoft.ML.Tokenizers;

/// <summary>Offline model tokenizers with shared vocabularies and bounded model-name lookup.</summary>
public sealed class ContextTextTokenizer : IContextTextTokenizer
{
    private const int MaximumModels = 128;
    private readonly Lock _gate = new();
    private readonly Dictionary<string, Lazy<TiktokenTokenizer?>> _models = new(StringComparer.Ordinal);

    public bool TryCount(string? model, string text, out long tokens)
    {
        tokens = 0;
        if (string.IsNullOrWhiteSpace(model)) return false;
        Lazy<TiktokenTokenizer?> tokenizer;
        lock (_gate)
        {
            if (!_models.TryGetValue(model, out tokenizer!))
            {
                if (_models.Count >= MaximumModels) _models.Remove(_models.Keys.First());
                tokenizer = new Lazy<TiktokenTokenizer?>(() => Create(model));
                _models.Add(model, tokenizer);
            }
        }
        if (tokenizer.Value is not { } known) return false;
        tokens = known.CountTokens(text);
        return true;
    }

    private static TiktokenTokenizer? Create(string model)
    {
        try { return TiktokenTokenizer.CreateForModel(model); }
        catch (Exception error) when (error is ArgumentException or InvalidOperationException or NotSupportedException
            or FileNotFoundException or FileLoadException) { return null; }
    }
}
