namespace AI.Web.Resources;

using System.Text;
using AI.Contracts.Resources;
using AI.Contracts.Workspace;

public sealed class ResourcePresenter(IDiffSnapshotReader snapshots) : IResourcePresenter
{
    /// <summary>A tooltip is 18rem wide: past this many rows the list stops helping.</summary>
    private const int HintFileLimit = 8;

    public string Label(ChatResource reference) => reference.Kind switch
    {
        ChatResourceKind.File when reference.AssetId is not null => reference.Name ?? "File",
        ChatResourceKind.File => Name(reference.Path) + (reference.Lines is { } lines ? ":" + Range(lines) : string.Empty),
        ChatResourceKind.Directory => Name(reference.Path),
        ChatResourceKind.Diff => reference.Name ?? Name(reference.Path),
        _ => reference.Name ?? reference.Mention ?? reference.Path
    };

    public string Hint(ChatResource reference)
    {
        var hint = new StringBuilder();
        if (reference.Mention is { } mention) hint.Append(mention).Append('\n');
        switch (reference.Kind)
        {
            case ChatResourceKind.File:
                if (reference.AssetId is not null)
                    hint.Append(reference.Name ?? "File").Append(" · click to preview");
                else
                {
                    hint.Append(reference.Path);
                    if (reference.Lines is { } lines) hint.Append("\nLines ").Append(Range(lines)).Append(", as they were when the message was sent");
                }
                break;
            case ChatResourceKind.Directory:
                hint.Append(reference.Path);
                break;
            case ChatResourceKind.Chat:
                hint.Append("Chat · click to open");
                break;
            case ChatResourceKind.Review:
                hint.Append(reference.ReviewKind == ChatReviewKind.Message ? "Message comments" : "Review").Append(" · click to open");
                break;
            case ChatResourceKind.Project:
                hint.Append("Project");
                break;
            case ChatResourceKind.Image:
                hint.Append(reference.Source == ChatResourceSource.Clipboard
                    ? "Image from clipboard"
                    : reference.Name ?? "Image").Append(" · click to open");
                break;
            case ChatResourceKind.Diff:
                DiffHint(reference, hint);
                break;
            default:
                hint.Append(reference.Path);
                break;
        }
        return hint.ToString();
    }

    public string? Summary(ChatResource reference)
    {
        if (Changes(reference) is not { } snapshot) return null;
        var changes = snapshot.Changes;
        if (changes.IsEmpty) return "no changes";
        if (changes.Additions == 0 && changes.Deletions == 0) return Files(changes.Files.Count, snapshot.IsCut);
        return $"+{changes.Additions} −{changes.Deletions}";
    }

    public DiffSnapshot? Changes(ChatResource reference) =>
        reference.Kind == ChatResourceKind.Diff ? snapshots.Read(reference.Excerpt, reference.Path) : null;

    public string Target(ChatResource reference) => reference.Kind is ChatResourceKind.File or ChatResourceKind.Directory
        ? FileUri(reference.Path) + (reference.Lines is { } lines ? $"#L{lines.Start}-L{lines.End}" : string.Empty)
        : reference.Kind == ChatResourceKind.Skill
            ? $"aiclient://navigate/settings.skills?skillId={Uri.EscapeDataString(reference.Path)}"
            : $"#mention-{reference.Id}";

    /// <summary>A file URI by hand: in the browser <see cref="Uri"/> does not know a Windows path for one.</summary>
    private static string FileUri(string path)
    {
        var slashed = path.Replace('\\', '/');
        var encoded = string.Join('/', slashed.Split('/').Select(Uri.EscapeDataString));
        if (slashed.StartsWith("//", StringComparison.Ordinal)) return "file:" + encoded;
        if (slashed.Length >= 2 && slashed[1] == ':') return $"file:///{slashed[..2]}{encoded[(encoded.IndexOf('/', StringComparison.Ordinal) is var at and >= 0 ? at : encoded.Length)..]}";
        return "file://" + encoded;
    }

    private void DiffHint(ChatResource reference, StringBuilder hint)
    {
        hint.Append(reference.Path);
        if (Changes(reference) is not { } snapshot)
        {
            hint.Append("\nUncommitted changes, as they were when the message was sent");
            return;
        }
        var changes = snapshot.Changes;
        if (changes.IsEmpty)
        {
            hint.Append("\nNo uncommitted changes when the message was sent");
            return;
        }
        hint.Append("\nWhen the message was sent: ").Append(Files(changes.Files.Count, snapshot.IsCut))
            .Append(", +").Append(changes.Additions).Append(" −").Append(changes.Deletions);
        var root = reference.Path.TrimEnd('\\', '/').Length + 1;
        foreach (var file in changes.Files.Take(HintFileLimit))
        {
            // Relative, with the slashes git wrote them with.
            hint.Append('\n').Append((file.Path.Length > root ? file.Path[root..] : file.Path).Replace('\\', '/'));
            if (file.IsBinary) hint.Append("  binary");
            else if (file.Additions is { } added && file.Deletions is { } removed) hint.Append("  +").Append(added).Append(" −").Append(removed);
            else if (file.Kind == FileChangeKind.Added) hint.Append("  new");
        }
        if (changes.Files.Count > HintFileLimit) hint.Append("\n… ").Append(Files(changes.Files.Count - HintFileLimit, false)).Append(" more");
        hint.Append("\nClick to show the changes");
    }

    /// <summary>"7+ files" when the Host cut the list short.</summary>
    private static string Files(int count, bool more) => more ? $"{count}+ files" : count == 1 ? "1 file" : $"{count} files";

    private static string Name(string path)
    {
        var trimmed = path.TrimEnd('\\', '/');
        var slash = trimmed.LastIndexOfAny(['\\', '/']);
        return slash < 0 || slash == trimmed.Length - 1 ? trimmed : trimmed[(slash + 1)..];
    }

    private static string Range(ChatLineRange lines) => lines.Start == lines.End ? $"{lines.Start}" : $"{lines.Start}-{lines.End}";
}
