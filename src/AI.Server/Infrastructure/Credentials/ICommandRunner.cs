namespace AI.Infrastructure.Credentials;

/// <summary>Runs an operating system tool (<c>security</c>, <c>secret-tool</c>) to completion.</summary>
public interface ICommandRunner
{
    /// <returns>
    /// What the tool did, or null when it could not be started at all — it is not installed.
    /// </returns>
    CommandResult? Run(string fileName, IReadOnlyList<string> arguments, string? standardInput = null);
}

public sealed record CommandResult(int ExitCode, string StandardOutput, string StandardError);
