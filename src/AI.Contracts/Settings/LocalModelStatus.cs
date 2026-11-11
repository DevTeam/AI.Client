namespace AI.Contracts.Settings;

/// <summary>
/// The optional on-device model. <paramref name="State"/> is one of <c>notInstalled</c>, <c>verifying</c>,
/// <c>starting</c>, <c>ready</c>, <c>disabled</c>, <c>failed</c> and <c>stopped</c>. <paramref name="Error"/>
/// is written for the person and never names a file path; <paramref name="CanRetry"/> says whether
/// <c>POST api/settings/local-ai/retry</c> can start it again.
/// </summary>
public sealed record LocalModelStatus(string State, string? Model = null, string? Error = null,
    string? Name = null, int? ContextTokens = null, bool CanRetry = false)
{
    public static readonly Guid ConnectionId = new("9540ce18-a279-41ed-8419-e2dde100d1b0");
}
