namespace AI.Application.Tools.Commands;

using System.Buffers;
using System.Text.Json;
using AI.Contracts.FileSystem;

/// <summary>Finds the file a program name starts, the way <c>process_run</c> is given it.</summary>
public interface IExecutableLocator
{
    /// <summary>
    /// The full path of the file, or null when there is none: a fully qualified path as it is, a
    /// relative path with a directory against <paramref name="workingDirectory"/>, a bare name
    /// through PATH.
    /// </summary>
    Task<string?> LocateAsync(string executable, string? workingDirectory, CancellationToken cancellationToken);
}

public interface ICommandParser
{
    /// <summary>
    /// Reads a <c>process_run</c> call's arguments, or returns null when they do not name a program.
    /// </summary>
    Task<ParsedCommand?> ParseAsync(string arguments, CancellationToken cancellationToken);
}

public sealed class CommandParser(IExecutableLocator locator, IPath paths) : ICommandParser
{
    public async Task<ParsedCommand?> ParseAsync(string arguments, CancellationToken cancellationToken)
    {
        string? executable;
        string[] argv;
        string? workingDirectory;
        try
        {
            using var document = JsonDocument.Parse(arguments);
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object) return null;
            executable = String(root, "executable");
            argv = root.TryGetProperty("arguments", out var list) && list.ValueKind == JsonValueKind.Array
                ? list.EnumerateArray().Select(item => item.ValueKind == JsonValueKind.String ? item.GetString()! : item.GetRawText()).ToArray()
                : [];
            workingDirectory = String(root, "workingDirectory");
        }
        catch (JsonException)
        {
            return null;
        }
        if (string.IsNullOrWhiteSpace(executable)) return null;
        var directory = string.IsNullOrWhiteSpace(workingDirectory) || !paths.IsFullyQualified(workingDirectory)
            ? null
            : paths.GetFullPath(workingDirectory);

        var caseSensitive = paths.IsCaseSensitive;
        var name = CommandNames.Normalize(executable, caseSensitive);
        var located = await locator.LocateAsync(executable, directory, cancellationToken);
        // The installed program is the one a bare name finds; a path that only ends in its name is
        // some other file, and a rule written for the program says nothing about it.
        var installed = executable.IndexOfAny(['/', '\\']) < 0
            ? located
            : await locator.LocateAsync(paths.GetFileName(executable), null, cancellationToken);
        var onPath = located is not null && installed is not null && string.Equals(located, installed, paths.Comparison);

        var extension = paths.GetExtension(located ?? executable);
        var interpreter = CommandGrammar.Interpreters.Contains(name) || CommandGrammar.ScriptExtensions.Contains(extension);

        IReadOnlyList<string> global = [];
        IReadOnlyList<string> tokens = argv;
        IReadOnlyList<string> optionPaths = [];
        IReadOnlyList<string> unsafeOptions = [];
        var destructive = false;
        if (name == "git")
        {
            var git = CommandGrammar.Git(argv);
            (global, tokens, optionPaths, unsafeOptions, destructive) =
                (git.GlobalOptions, git.Tokens, git.PathValues, git.UnsafeOptions, git.IsDestructive);
        }
        // A git -C moves where the rest of the command's relative paths point; each -C is itself
        // relative to the one before it.
        var resolved = new List<string>();
        var baseDirectory = directory;
        for (var index = 0; index + 1 < global.Count; index++)
            if (global[index] == "-C" && Full(global[index + 1], baseDirectory) is { } moved)
            {
                baseDirectory = moved;
                resolved.Add(moved);
            }
        foreach (var value in optionPaths.Concat(tokens.Select(OptionValue)).Where(LooksLikePath))
            if (Full(value, baseDirectory) is { } full)
                resolved.Add(full);
        if (optionPaths.Concat(tokens.Select(OptionValue)).Any(value => LooksLikePath(value) && Full(value, baseDirectory) is null))
            resolved.Add(Unresolved);
        var distinct = resolved.Distinct(paths.IsCaseSensitive ? StringComparer.Ordinal : StringComparer.OrdinalIgnoreCase)
            .ToArray();
        return new ParsedCommand(name, located, onPath, global, tokens, directory, distinct, interpreter, unsafeOptions,
            destructive);
    }

    /// <summary>
    /// Stands in for a relative path that could not be placed because the call gives no working
    /// directory: it is inside no project, so a rule cannot let it through unasked.
    /// </summary>
    public const string Unresolved = "<relative to an unknown directory>";

    private string OptionValue(string argument)
    {
        // On Windows /nodeReuse:false or /p:OutDir=..\out is a switch, not a file in the drive's
        // root; only what it sets can name a path.
        if (paths.DirectorySeparator == '\\' && argument.StartsWith('/'))
        {
            var colon = argument.IndexOf(':', StringComparison.Ordinal);
            if (colon > 1 && !argument.AsSpan(1, colon - 1).ContainsAnyExcept(SwitchNameCharacters))
            {
                var value = argument[(colon + 1)..];
                var assignment = value.IndexOf('=', StringComparison.Ordinal);
                return assignment > 0 ? value[(assignment + 1)..] : value;
            }
        }
        var equals = argument.IndexOf('=', StringComparison.Ordinal);
        return argument.StartsWith('-') && equals > 0 ? argument[(equals + 1)..] : argument;
    }

    private static readonly SearchValues<char> SwitchNameCharacters =
        SearchValues.Create("ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz0123456789-_");

    /// <summary>
    /// A value is treated as a path when it names a root, or has a directory separator and is not
    /// a URL. Refs such as <c>origin/main</c> pass too; they land inside the working directory,
    /// which is harmless, and the alternative is missing <c>..\..\.ssh</c>.
    /// </summary>
    private bool LooksLikePath(string value) =>
        value.Length > 0 && !value.Contains("://", StringComparison.Ordinal)
        && (paths.IsRooted(value) || value.IndexOfAny(['/', '\\']) >= 0 || value is "." or "..");

    private string? Full(string value, string? directory)
    {
        try
        {
            if (paths.IsFullyQualified(value)) return paths.GetFullPath(value);
            return directory is null ? null : paths.GetFullPath(value, directory);
        }
        catch (Exception error) when (error is ArgumentException or NotSupportedException or PathTooLongException)
        {
            return null;
        }
    }

    private static string? String(JsonElement root, string name) =>
        root.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;
}
