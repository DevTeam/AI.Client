namespace AI.Application.Tools.Commands;

using System.Text.Json.Nodes;
using AI.Contracts.FileSystem;
using AI.Contracts.Settings;

/// <summary>
/// <c>process_run</c>: a pattern is a program and its arguments word by word (<c>git diff *</c>),
/// read by <see cref="CommandParser"/> and checked by <see cref="CommandGuard"/>.
/// </summary>
public sealed class ProcessRunRuleProfile(ICommandParser parser, IPath paths) : IToolRuleProfile
{
    public Guid ServerId => DefaultMcpServer.Id;

    public string Tool => CommandRule.ProcessRun;

    public bool TryParsePattern(string text, out IRulePattern? pattern, out string? problem)
    {
        pattern = CommandPattern.TryParse(text, paths.IsCaseSensitive, out var parsed, out problem) ? new Pattern(parsed!) : null;
        return pattern is not null;
    }

    public async Task<RuleSubject?> ReadAsync(string arguments, RuleContext context, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);
        if (await parser.ParseAsync(arguments, cancellationToken) is not { } command) return null;
        var check = CommandGuard.Check(command, context.Grants, paths, context.ScratchRoot);
        return new RuleSubject(command.Display, check.Warnings, check.BlocksAllow, check.ExactOnly,
            CommandGuard.Options(check), command, Pin(arguments, command.ExecutablePath),
            new
            {
                display = command.Display,
                program = command.ExecutablePath,
                // Where each place the command touches lies, worked out here: the scratch path is two
                // long ids, and a model comparing them by eye took it for somewhere else.
                programScope = command.ExecutablePath is { } program ? Scope(program, context) : null,
                installed = command.OnPath,
                interpreter = command.IsInterpreter,
                destructive = command.IsDestructive,
                workingDirectory = command.WorkingDirectory,
                workingDirectoryScope = command.WorkingDirectory is { } directory ? Scope(directory, context) : null,
                paths = command.Paths.Distinct(StringComparer.Ordinal).Take(10)
                    .Select(path => new { path, scope = Scope(path, context) }).ToArray()
            });
    }

    private string Scope(string path, RuleContext context) =>
        CommandGuard.Scope(path, context.Grants, context.ScratchRoot, paths).ToString().ToLowerInvariant();

    /// <summary>The program the rules were checked against is the one that starts.</summary>
    private static string? Pin(string arguments, string? executable)
    {
        if (executable is null || JsonNode.Parse(arguments) is not JsonObject input) return null;
        input["executable"] = executable;
        return input.ToJsonString();
    }

    private sealed class Pattern(CommandPattern pattern) : IRulePattern
    {
        public string Text => pattern.Text;
        public bool IsExact => pattern.IsExact;
        public (int Literal, int Closed, int Characters) Specificity => pattern.Specificity;

        // A command is one thing, so all and any are the same question.
        public bool Matches(RuleSubject subject, bool any) => subject.Value is ParsedCommand command && pattern.Matches(command);
    }
}
