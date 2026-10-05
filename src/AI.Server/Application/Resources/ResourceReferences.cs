namespace AI.Application.Resources;

/// <summary>Value conversion at the API/domain boundary; no storage or resolution logic.</summary>
public static class ResourceReferences
{
    public static IReadOnlyList<Domain.Resources.ChatResource>? ToDomain(
        IReadOnlyList<Contracts.Resources.ChatResource>? references) =>
        references?.Select(item => new Domain.Resources.ChatResource(item.Id,
            (Domain.Resources.ChatResourceKind)item.Kind, item.Path, item.Name,
            item.ReviewKind is { } reviewKind ? (Domain.Resources.ChatReviewKind)reviewKind : null,
            item.Lines is { } lines ? new Domain.Resources.ChatLineRange(lines.Start, lines.End) : null,
            item.Excerpt, item.Mention, (Domain.Resources.ChatResourceSource)item.Source,
            item.AssetId, item.MediaType, item.Size)).ToArray();

    public static IReadOnlyList<Contracts.Resources.ChatResource>? ToContract(
        IReadOnlyList<Domain.Resources.ChatResource>? references) =>
        references?.Select(item => new Contracts.Resources.ChatResource(item.Id,
            (Contracts.Resources.ChatResourceKind)item.Kind, item.Path, item.Name,
            item.ReviewKind is { } reviewKind ? (Contracts.Resources.ChatReviewKind)reviewKind : null,
            item.Lines is { } lines ? new Contracts.Resources.ChatLineRange(lines.Start, lines.End) : null,
            item.Excerpt, item.Mention, (Contracts.Resources.ChatResourceSource)item.Source,
            item.AssetId, item.MediaType, item.Size)).ToArray();
}
