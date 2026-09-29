namespace AI.Application.Resources;

/// <summary>Value conversion at the API/domain boundary; no storage or resolution logic.</summary>
public static class ResourceReferences
{
    public static IReadOnlyList<Domain.Resources.ChatResourceRef>? ToDomain(
        IReadOnlyList<Contracts.Resources.ChatResourceRef>? references) =>
        references?.Select(item => new Domain.Resources.ChatResourceRef(item.Id,
            (Domain.Resources.ChatResourceKind)item.Kind, item.Path, item.Name,
            item.ReviewKind is { } reviewKind ? (Domain.Resources.ChatReviewKind)reviewKind : null,
            item.Lines is { } lines ? new Domain.Resources.ChatLineRange(lines.Start, lines.End) : null,
            item.Excerpt, item.Mention)).ToArray();

    public static IReadOnlyList<Contracts.Resources.ChatResourceRef>? ToContract(
        IReadOnlyList<Domain.Resources.ChatResourceRef>? references) =>
        references?.Select(item => new Contracts.Resources.ChatResourceRef(item.Id,
            (Contracts.Resources.ChatResourceKind)item.Kind, item.Path, item.Name,
            item.ReviewKind is { } reviewKind ? (Contracts.Resources.ChatReviewKind)reviewKind : null,
            item.Lines is { } lines ? new Contracts.Resources.ChatLineRange(lines.Start, lines.End) : null,
            item.Excerpt, item.Mention)).ToArray();
}
