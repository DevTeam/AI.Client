namespace AI.Client.Cli;

internal sealed class HeadlessPaths
{
    public string SessionsDirectory { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "AI.Client", "test-sessions");

    public string GetSessionPath(Guid id) => Path.Combine(SessionsDirectory, id.ToString("N"), "session.json");
    public string GetTranscriptPath(Guid id) => Path.Combine(SessionsDirectory, id.ToString("N"), "transcript.jsonl");
}
