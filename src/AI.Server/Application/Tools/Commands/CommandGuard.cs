namespace AI.Application.Tools.Commands;

using AI.Contracts.FileSystem;

/// <summary>A directory the project was granted, as the guard checks paths against it.</summary>
public sealed record CommandGrant(string Root, bool Recursive);

/// <summary>
/// What stands between a command and an allow rule. <paramref name="BlocksAllow"/> means no
/// rule may let the call through unasked; the person can still allow it on the card.
/// </summary>
/// <param name="Warnings">One short English sentence each, shown on the card and to the risk check.</param>
/// <param name="ExactOnly">Whether only a rule that spells this exact command may allow it.</param>
public sealed record CommandCheck(ParsedCommand Command, IReadOnlyList<string> Warnings, bool BlocksAllow, bool ExactOnly);

/// <summary>
/// Checks a parsed command for what an allow rule must never cover: another program under an
/// installed one's name, options that run other commands, and paths outside the project.
/// </summary>
public static class CommandGuard
{
    public static CommandCheck Check(ParsedCommand command, IReadOnlyList<CommandGrant> grants, IPath paths)
    {
        ArgumentNullException.ThrowIfNull(command);
        ArgumentNullException.ThrowIfNull(paths);
        var warnings = new List<string>();
        var blocks = false;
        bool Inside(string path) => grants.Any(grant => paths.IsInside(path, grant.Root, grant.Recursive));

        if (command.ExecutablePath is null)
        {
            warnings.Add($"The program {command.Name} was not found on PATH.");
            blocks = true;
        }
        else if (!command.OnPath)
        {
            warnings.Add($"It runs {command.ExecutablePath}, not the {command.Name} found on PATH.");
            blocks = true;
        }
        else if (grants.Any(grant => paths.IsInside(command.ExecutablePath, grant.Root, true))
                 || command.WorkingDirectory is { } directory && paths.IsInside(command.ExecutablePath, directory, true))
        {
            warnings.Add($"It runs a program from inside the project: {command.ExecutablePath}.");
            blocks = true;
        }
        if (command.IsInterpreter)
            warnings.Add("It runs a shell, script or launcher, so a rule can allow only this exact command.");
        foreach (var option in command.UnsafeOptions.Distinct(StringComparer.Ordinal))
        {
            warnings.Add($"Option {option} can make {command.Name} run other commands.");
            blocks = true;
        }
        if (command.WorkingDirectory is null)
        {
            warnings.Add("It gives no working directory.");
            blocks = true;
        }
        else if (!Inside(command.WorkingDirectory))
        {
            warnings.Add($"Its working directory is outside the project: {command.WorkingDirectory}.");
            blocks = true;
        }
        foreach (var path in command.Paths.Where(path => path == CommandParser.Unresolved || !Inside(path)).Take(3))
        {
            warnings.Add($"It names a path outside the project: {path}.");
            blocks = true;
        }
        if (command.IsDestructive) warnings.Add("It can discard work or rewrite history.");
        return new CommandCheck(command, warnings, blocks, command.IsInterpreter || command.IsDestructive);
    }

    /// <summary>
    /// The patterns the approval card offers, the one it preselects first: the subcommand with
    /// anything after it, this exact command, and the whole program. A command a broad rule must
    /// not cover is offered only as itself.
    /// </summary>
    public static IReadOnlyList<string> Options(CommandCheck check)
    {
        ArgumentNullException.ThrowIfNull(check);
        var command = check.Command;
        var exact = CommandPattern.Format([command.Name, .. command.Tokens]);
        // A literal * or ? in an argument would read back as a mask and allow more than was shown.
        if (command.Tokens.Any(CommandPattern.IsMask)) return [];
        if (check.ExactOnly || check.Command.UnsafeOptions.Count > 0) return [exact];
        var options = new List<string>();
        if (command.Tokens.Count > 0 && !command.Tokens[0].StartsWith('-'))
        {
            var subcommand = CommandPattern.Format([command.Name, command.Tokens[0], "*"]);
            options.Add(subcommand);
        }
        options.Add(exact);
        options.Add(CommandPattern.Format([command.Name, "*"]));
        return options.Distinct(StringComparer.Ordinal).ToArray();
    }
}
