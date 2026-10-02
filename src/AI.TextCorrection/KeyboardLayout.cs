namespace AI.TextCorrection;

public sealed record KeyboardLayout(string Id, string Name, string Keys, string ShiftKeys)
{
    /// <summary>Different keyboard variants can share one language dictionary.</summary>
    public string LanguageId { get; init; } = Id;
}

/// <summary>Score is a heuristic ranking, not a calibrated probability.</summary>
public sealed record TextReplacement(int Start, int Length, string Text, string LayoutId, double Score);

public interface IKeyboardLayouts
{
    IReadOnlyList<KeyboardLayout> All { get; }
}

public interface ILayoutConverter
{
    string Convert(string text, KeyboardLayout source, KeyboardLayout target);
    char Convert(char character, KeyboardLayout source, KeyboardLayout target);
}

public interface IWordLexicon
{
    bool Contains(string layoutId, string word);
}

public interface ITextCorrectionAnalyzer
{
    IReadOnlyList<TextReplacement> Analyze(string text, IReadOnlyCollection<string> layoutIds);
}
