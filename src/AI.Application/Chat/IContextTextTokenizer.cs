namespace AI.Application.Chat;

/// <summary>Counts text for recognized models; false leaves protocol-independent fallback intact.</summary>
public interface IContextTextTokenizer
{
    bool TryCount(string? model, string text, out long tokens);
}
