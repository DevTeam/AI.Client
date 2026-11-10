namespace AI.Contracts.Settings;

public sealed record LocalModelStatus(string State, string? Model = null, string? Error = null)
{
    public static readonly Guid ConnectionId = new("9540ce18-a279-41ed-8419-e2dde100d1b0");
}
