namespace AI.Application.Tools.Commands;

/// <summary>
/// What the parser knows about particular programs. Anything not named here is read the plain way:
/// every argument is part of the command, and none of them is special.
/// </summary>
public static class CommandGrammar
{
    /// <summary>
    /// Programs that run code they are handed: shells, script hosts and launchers. Their arguments
    /// are a program of their own, so a rule may allow one only word for word.
    /// </summary>
    public static readonly IReadOnlySet<string> Interpreters = new HashSet<string>(StringComparer.Ordinal)
    {
        "cmd", "powershell", "pwsh", "bash", "sh", "zsh", "fish", "dash", "ksh", "csh", "tcsh", "wsl",
        "python", "python3", "py", "pythonw", "node", "deno", "bun", "bunx", "ruby", "perl", "php", "lua",
        "npx", "pnpx", "env", "xargs", "sudo", "doas", "runas", "start", "nohup", "nice", "time", "timeout",
        "cscript", "wscript", "mshta", "rundll32", "regsvr32", "msiexec", "osascript", "ssh", "busybox"
    };

    /// <summary>File types that are scripts rather than programs, whatever their name.</summary>
    public static readonly IReadOnlySet<string> ScriptExtensions = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        ".bat", ".cmd", ".ps1", ".psm1", ".sh", ".bash", ".zsh", ".vbs", ".vbe", ".js", ".jse", ".wsf", ".wsh",
        ".py", ".rb", ".pl", ".php", ".hta", ".msi", ".lnk", ".scr"
    };

    /// <summary>Git options read before the subcommand that take the next argument as their value.</summary>
    private static readonly HashSet<string> GitValueOptions = new HashSet<string>(StringComparer.Ordinal)
    {
        "-C", "-c", "--git-dir", "--work-tree", "--namespace", "--config-env", "--super-prefix", "--list-cmds",
        "--attr-source"
    };

    /// <summary>Git options before the subcommand whose value is a path.</summary>
    private static readonly HashSet<string> GitPathOptions = new HashSet<string>(StringComparer.Ordinal)
    {
        "-C", "--git-dir", "--work-tree"
    };

    /// <summary>
    /// Git options that set configuration or a program to run: any of them can turn an innocent
    /// subcommand into an arbitrary command (<c>-c alias.status=!…</c>, <c>-c core.pager=…</c>).
    /// </summary>
    private static readonly HashSet<string> GitUnsafeGlobalOptions = new HashSet<string>(StringComparer.Ordinal)
    {
        "-c", "--config-env", "--exec-path"
    };

    /// <summary>Subcommand options that name a program for git to run on the other side of a transfer.</summary>
    private static readonly string[] GitUnsafeOptions = ["--upload-pack", "--receive-pack", "--exec"];

    /// <summary>Subcommand options whose value is a file git writes.</summary>
    private static readonly string[] GitPathValueOptions = ["--output", "--output-directory", "-o"];

    /// <summary>
    /// Reads the part of git's command line before the subcommand and spells forced pushes one way.
    /// </summary>
    internal static GitCommand Git(IReadOnlyList<string> arguments)
    {
        var global = new List<string>();
        var pathValues = new List<string>();
        var unsafeOptions = new List<string>();
        var index = 0;
        while (index < arguments.Count && arguments[index].StartsWith('-'))
        {
            var argument = arguments[index++];
            var equals = argument.IndexOf('=', StringComparison.Ordinal);
            var name = argument.StartsWith("--", StringComparison.Ordinal) && equals > 0 ? argument[..equals] : argument;
            global.Add(argument);
            if (GitUnsafeGlobalOptions.Contains(name)) unsafeOptions.Add(name);
            string? value = null;
            if (equals > 0 && name != argument) value = argument[(equals + 1)..];
            else if (GitValueOptions.Contains(name) && index < arguments.Count)
            {
                value = arguments[index++];
                global.Add(value);
            }
            if (value is not null && GitPathOptions.Contains(name)) pathValues.Add(value);
        }
        var tokens = arguments.Skip(index).ToList();
        var subcommand = tokens.Count > 0 ? tokens[0] : null;
        var subArguments = tokens.Skip(1).ToArray();
        foreach (var argument in subArguments)
        {
            if (GitUnsafeOptions.Any(option => argument == option || argument.StartsWith(option + "=", StringComparison.Ordinal))
                || subcommand == "clone" && argument == "-u")
                unsafeOptions.Add(argument.Split('=')[0]);
        }
        for (var position = 0; position < subArguments.Length; position++)
        {
            var argument = subArguments[position];
            foreach (var option in GitPathValueOptions)
            {
                // A bare -o is an output directory only for the commands that write files with it.
                if (option == "-o" && subcommand is not ("format-patch" or "archive")) continue;
                if (argument.StartsWith(option + "=", StringComparison.Ordinal)) pathValues.Add(argument[(option.Length + 1)..]);
                else if (argument == option && position + 1 < subArguments.Length) pathValues.Add(subArguments[position + 1]);
            }
        }
        if (subcommand == "push" && ForcesPush(subArguments) && !subArguments.Contains("--force"))
            tokens.Insert(1, "--force");
        return new GitCommand(global, tokens, pathValues, unsafeOptions, subcommand is not null && Destroys(subcommand, subArguments));
    }

    /// <summary><c>-f</c>, a short-option cluster holding f, or a refspec starting with <c>+</c>.</summary>
    private static bool ForcesPush(string[] arguments) =>
        arguments.Any(argument => argument == "--force" || ShortFlag(argument, 'f')
            || argument.Length > 1 && argument[0] == '+');

    private static bool ShortFlag(string argument, char flag) =>
        argument.Length > 1 && argument[0] == '-' && argument[1] != '-' && argument.AsSpan(1).Contains(flag);

    private static bool Destroys(string subcommand, string[] arguments)
    {
        bool Has(params string[] options) => arguments.Any(options.Contains);
        bool Forced() => Has("--force") || arguments.Any(argument => ShortFlag(argument, 'f'));
        return subcommand switch
        {
            "push" => ForcesPush(arguments) || Has("--delete", "-d", "--mirror", "--prune")
                      || arguments.Any(argument => argument.StartsWith("--force", StringComparison.Ordinal)
                                                   || argument.Length > 1 && argument[0] == ':'),
            "reset" => Has("--hard", "--merge", "--keep"),
            "clean" => Forced(),
            "branch" => Has("-D") || Has("-d", "--delete") && Forced() || Has("-M", "-C"),
            "checkout" => Forced() || Has("--", "."),
            "switch" => Forced() || Has("--discard-changes"),
            "restore" => !(arguments.Contains("--staged") && !arguments.Contains("--worktree")),
            "stash" => arguments.Length > 0 && arguments[0] is "drop" or "clear",
            "rm" => Forced(),
            "rebase" or "filter-branch" or "filter-repo" or "replace" => true,
            "reflog" => arguments.Length > 0 && arguments[0] is "expire" or "delete",
            "gc" or "prune" => arguments.Any(argument => argument.StartsWith("--prune", StringComparison.Ordinal))
                               || subcommand == "prune",
            "update-ref" => Has("-d"),
            "worktree" => arguments.Length > 0 && (arguments[0] == "remove" && Forced() || arguments[0] == "prune"),
            "tag" => Has("-d", "--delete") || Forced(),
            _ => false
        };
    }
}

internal sealed record GitCommand(
    IReadOnlyList<string> GlobalOptions,
    IReadOnlyList<string> Tokens,
    IReadOnlyList<string> PathValues,
    IReadOnlyList<string> UnsafeOptions,
    bool IsDestructive);
