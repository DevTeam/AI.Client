namespace AI.TextCorrection;

public interface ITextCorrectionPreparation
{
    bool IsReady(IReadOnlyCollection<string> layoutIds);
    Task PrepareAsync(IReadOnlyCollection<string> layoutIds);
}

public interface IWordLexiconPreparation
{
    Task PrepareAsync(string languageId);
}

public interface IWordPlausibilityPreparation
{
    Task PrepareAsync(string languageId);
}
