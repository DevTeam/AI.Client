namespace AI.Client.Cli;

internal interface IHeadlessPaths
{
    string GetSessionPath(Guid id);
    string GetTranscriptPath(Guid id);
}

internal sealed class HeadlessPaths : IHeadlessPaths
{
    private string SessionsDirectory { get; } = Environment.GetEnvironmentVariable("AI_CLIENT_SESSION_DIRECTORY") ?? Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "AI.Client", "test-sessions");

    public string GetSessionPath(Guid id) => Path.Combine(SessionsDirectory, id.ToString("N"), "session.json");
    public string GetTranscriptPath(Guid id) => Path.Combine(SessionsDirectory, id.ToString("N"), "transcript.jsonl");
}
