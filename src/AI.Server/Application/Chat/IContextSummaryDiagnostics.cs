namespace AI.Application.Chat;

/// <summary>Numeric observations only; source text and credentials never enter diagnostics.</summary>
public interface IContextSummaryDiagnostics
{
    void RecordSummary(string model, ContextSummaryObservation observation);
}

public sealed record ContextSummaryObservation(string Outcome, int Calls, long SourceTokens,
    long SentTokens, long ResultTokens, long InputLimit, long ElapsedMilliseconds);
