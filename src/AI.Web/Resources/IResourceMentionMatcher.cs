namespace AI.Web.Resources;

/// <summary>
/// The composer's "@" list: the keyboard way into the "+" menu. It finds the word being typed and
/// orders what can be attached for it.
/// </summary>
public interface IResourceMentionMatcher
{
    /// <summary>
    /// The "@word" the caret is in or right after, or null. "@" counts only at the start of the
    /// text or after whitespace, so "name@example.com" stays text.
    /// </summary>
    ResourceMention? GetMention(string text, int caret);

    /// <summary>The rows for <paramref name="mention"/>, grouped, best group first, browse rows last.</summary>
    IReadOnlyList<ResourceMentionItem> Match(ResourceMentionSources sources, ResourceMention mention);
}
