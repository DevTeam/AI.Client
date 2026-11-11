namespace Build.Targets;

internal interface IThirdPartyNoticesTarget
{
    /// <summary>Writes THIRD-PARTY-NOTICES.md, or with <paramref name="check"/> fails when it is out of date.</summary>
    Task<int> RunAsync(bool check, CancellationToken cancellationToken);
}
