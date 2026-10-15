namespace AI.Application.Tools.Commands;

/// <summary>
/// One <c>process_run</c> call as permission rules see it: which installed program it starts and
/// what it asks that program to do, after the spellings that mean the same thing were made one.
/// </summary>
/// <param name="Name">
/// The program's name as rules write it: the file name, without <c>.exe</c> and in lower case where
/// file names ignore case. <c>C:\Program Files\Git\cmd\git.exe</c> and <c>GIT</c> are both <c>git</c>.
/// </param>
/// <param name="ExecutablePath">The file the call starts, or null when it cannot be found.</param>
/// <param name="OnPath">
/// Whether that file is the one PATH finds for <paramref name="Name"/>. A rule about <c>git</c> is
/// about the installed git, not about any file someone named git.exe.
/// </param>
/// <param name="GlobalOptions">Options the program reads before its subcommand, such as git's <c>-C</c>.</param>
/// <param name="Tokens">
/// The subcommand and its arguments, which patterns are matched against. Aliases are already
/// spelled one way: <c>git push -f</c> and <c>git push origin +main</c> both carry <c>--force</c>.
/// </param>
/// <param name="WorkingDirectory">The full working directory, or null when the call leaves it out.</param>
/// <param name="Paths">Every argument that names a file or directory, as a full path.</param>
/// <param name="IsInterpreter">
/// Whether the program runs code it is handed — a shell, a script host, a launcher — so that what
/// it does is not in its own arguments' shape.
/// </param>
/// <param name="UnsafeOptions">Options that make the program run other commands, as written.</param>
/// <param name="IsDestructive">Whether the command can discard work or rewrite history.</param>
public sealed record ParsedCommand(
    string Name,
    string? ExecutablePath,
    bool OnPath,
    IReadOnlyList<string> GlobalOptions,
    IReadOnlyList<string> Tokens,
    string? WorkingDirectory,
    IReadOnlyList<string> Paths,
    bool IsInterpreter,
    IReadOnlyList<string> UnsafeOptions,
    bool IsDestructive)
{
    /// <summary>The command as a person reads it, global options included.</summary>
    public string Display => CommandPattern.Format([Name, .. GlobalOptions, .. Tokens]);
}
