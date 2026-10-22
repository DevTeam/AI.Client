namespace AI.Application.Chats;

using Projects;
using Contracts.Chats;
using System.Globalization;
using System.Text.RegularExpressions;

public interface IChatSearchService
{
    Task<ChatSearchResult> SearchAsync(ChatSearchRequest request, CancellationToken cancellationToken);
}

/// <summary>
/// Finds text across the stored conversations. There is no index: chats are read in a fixed order
/// and scanned, which keeps the feature honest about its cost and free of anything to rebuild or
/// invalidate. What makes that workable is that every limit is explicit — matches, characters,
/// messages examined — and a search that hits one says so and hands back a cursor.
/// </summary>
public sealed class ChatSearchService(IProjectService projects, IChatService chats) : IChatSearchService
{
    public async Task<ChatSearchResult> SearchAsync(ChatSearchRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (string.IsNullOrEmpty(request.Query)) return Failure("Query cannot be empty.");

        Matcher matcher;
        try
        {
            matcher = Matcher.Create(request.Query, request.IsRegex, request.IgnoreCase);
        }
        // An unparsable pattern arrives as a RegexParseException (an ArgumentException); a construct
        // the non-backtracking engine does not implement — a backreference, lookaround — arrives as
        // NotSupportedException. Both are the caller's mistake, not a fault here.
        catch (Exception error) when (error is ArgumentException or NotSupportedException)
        {
            return Failure(error.Message);
        }

        var newest = request.Order == ChatSearchOrder.Newest;
        if (newest && !string.IsNullOrWhiteSpace(request.Cursor))
            return Failure("A cursor continues only a search in stable order.");

        var roles = (request.Roles is { Count: > 0 } ? request.Roles : ChatSearchLimits.DefaultRoles)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var (chatOffset, messageOffset) = Cursor.Parse(request.Cursor);
        var limit = Math.Clamp(request.Limit, 1, ChatSearchLimits.MaxMatches);
        var budget = ChatSearchLimits.CharacterBudget;
        var matches = new List<ChatSearchMatch>();
        var examined = 0;
        var searched = 0;

        var targets = await TargetsAsync(request, cancellationToken);
        // Newest first reads the chats last active first, so when the work ceiling cuts the scan
        // short it is the oldest history that goes unread.
        if (newest) targets = targets.OrderByDescending(target => target.LastActivityAt).ToArray();
        for (var index = chatOffset; index < targets.Count; index++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var target = targets[index];
            var chat = await chats.GetAsync(target.ProjectId, target.ChatId, cancellationToken);
            if (chat is null) continue;
            searched++;
            // Only the first chat of a continued search starts part-way through; every later one
            // starts at its beginning.
            var start = index == chatOffset ? messageOffset : 0;
            var messages = Scope(chat, request.BranchId);
            for (var position = start; position < messages.Count; position++)
            {
                if (examined >= ChatSearchLimits.MaxMessagesExamined)
                    return newest
                        ? Newest(matches, limit, searched, examined, true)
                        : new ChatSearchResult(matches, searched, examined, true, Cursor.Of(index, position));
                var message = messages[position];
                examined++;
                if (!roles.Contains(message.Role)) continue;
                if (request.From is { } from && message.CreatedAt < from) continue;
                if (request.To is { } to && message.CreatedAt > to) continue;
                if (message.Content is not { Length: > 0 } content) continue;
                var count = matcher.Count(content);
                if (count == 0) continue;

                var match = new ChatSearchMatch(target.ProjectId, target.ProjectName, chat.Id, chat.Title,
                    message.Id, message.ParentId, message.Role, message.CreatedAt,
                    Snippet(content, matcher.FirstIndex(content)), count, chat.ArchivedAt is not null);
                // Which matches are newest is known only at the end, so limits apply after sorting.
                if (newest)
                {
                    matches.Add(match);
                    continue;
                }

                var cost = Cost(match);
                // The first match always goes in: a search that answers "your budget is too small"
                // and nothing else is of no use to anyone.
                if (matches.Count > 0 && (matches.Count >= limit || cost > budget))
                    return new ChatSearchResult(matches, searched, examined, true, Cursor.Of(index, position));
                matches.Add(match);
                budget -= cost;
            }
        }

        // Falling out of the loop means the scan reached the end: nothing was cut short.
        return newest
            ? Newest(matches, limit, searched, examined, false)
            : new ChatSearchResult(matches, searched, examined, false, null);
    }

    /// <summary>The newest matches that fit the same limits a stable search applies while it scans.</summary>
    private static ChatSearchResult Newest(List<ChatSearchMatch> found, int limit, int searched, int examined, bool cut)
    {
        var budget = ChatSearchLimits.CharacterBudget;
        var matches = new List<ChatSearchMatch>();
        foreach (var match in found.OrderByDescending(match => match.CreatedAt))
        {
            var cost = Cost(match);
            if (matches.Count > 0 && (matches.Count >= limit || cost > budget)) break;
            matches.Add(match);
            budget -= cost;
        }

        return new ChatSearchResult(matches, searched, examined, cut || matches.Count < found.Count, null);
    }

    private static int Cost(ChatSearchMatch match) => match.Snippet.Length + match.ChatTitle.Length + match.ProjectName.Length;

    /// <summary>
    /// The chats to scan, in an order that does not move: by project id, then chat id. The sidebar
    /// orders chats by recent activity, which would shuffle under a paging search and make a cursor
    /// point at a different chat each time.
    /// </summary>
    private async Task<IReadOnlyList<Target>> TargetsAsync(ChatSearchRequest request, CancellationToken cancellationToken)
    {
        var targets = new List<Target>();
        var scope = request.ProjectId is { } only
            ? (await projects.GetAsync(only, cancellationToken)) is { } project
                ? [(project.Id, project.Name)]
                : Array.Empty<(Guid Id, string Name)>()
            : (await projects.ListAsync(cancellationToken)).Select(item => (item.Id, item.Name)).ToArray();
        foreach (var (projectId, projectName) in scope.OrderBy(item => item.Id))
            foreach (var chat in (await chats.ListAsync(projectId, cancellationToken)).OrderBy(item => item.Id))
                if ((request.ChatId is not { } wanted || chat.Id == wanted)
                    && (request.ArchiveScope == ChatArchiveScope.All
                        || (chat.ArchivedAt is not null) == (request.ArchiveScope == ChatArchiveScope.Archived)))
                    targets.Add(new Target(projectId, projectName, chat.Id, chat.LastActivityAt));
        return targets;
    }

    /// <summary>
    /// The messages a search sees: the whole chat, or — when a branch is named — only the chain that
    /// branch reaches, which is the same context the model is given.
    /// </summary>
    private static IReadOnlyList<ChatMessageView> Scope(ChatDetails chat, Guid? branchId)
    {
        if (branchId is not { } id) return chat.Messages;
        if (chat.Branches?.SingleOrDefault(branch => branch.Id == id) is not { } branch) return [];
        var byId = chat.Messages.ToDictionary(message => message.Id);
        var chain = new List<ChatMessageView>();
        var cursor = branch.HeadMessageId;
        while (cursor is { } messageId && byId.TryGetValue(messageId, out var message))
        {
            chain.Add(message);
            cursor = message.ParentId;
        }

        chain.Reverse();
        return chain;
    }

    /// <summary>Text around the first occurrence, with ellipses only where something was actually cut.</summary>
    private static string Snippet(string content, int at)
    {
        var half = ChatSearchLimits.SnippetLength / 2;
        var start = Math.Max(0, at - half);
        var length = Math.Min(ChatSearchLimits.SnippetLength, content.Length - start);
        var text = content.Substring(start, length).ReplaceLineEndings(" ");
        return (start > 0 ? "…" : string.Empty) + text.Trim() + (start + length < content.Length ? "…" : string.Empty);
    }

    private static ChatSearchResult Failure(string error) => new([], 0, 0, false, null, error);

    private sealed record Target(Guid ProjectId, string ProjectName, Guid ChatId, DateTimeOffset LastActivityAt);

    /// <summary>
    /// Position in the scan as "chat index : message index". Opaque by contract — callers return
    /// what they were given — but deliberately trivial, because it has to survive a restart and a
    /// chat being added or removed without pretending to be more precise than the scan order is.
    /// </summary>
    private static class Cursor
    {
        public static string Of(int chat, int message) =>
            string.Create(CultureInfo.InvariantCulture, $"{chat}:{message}");

        public static (int Chat, int Message) Parse(string? cursor)
        {
            if (string.IsNullOrWhiteSpace(cursor)) return (0, 0);
            var parts = cursor.Split(':');
            if (parts.Length != 2
                || !int.TryParse(parts[0], NumberStyles.None, CultureInfo.InvariantCulture, out var chat)
                || !int.TryParse(parts[1], NumberStyles.None, CultureInfo.InvariantCulture, out var message))
                throw new ArgumentException("Cursor is not one this search produced.", nameof(cursor));
            return (chat, message);
        }
    }

    private sealed class Matcher
    {
        private readonly string? _literal;
        private readonly StringComparison _comparison;
        private readonly Regex? _regex;

        private Matcher(string? literal, StringComparison comparison, Regex? regex)
        {
            _literal = literal;
            _comparison = comparison;
            _regex = regex;
        }

        public static Matcher Create(string query, bool isRegex, bool ignoreCase)
        {
            if (!isRegex)
                return new Matcher(query, ignoreCase ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal, null);
            // NonBacktracking runs in time linear in the input, so a pattern the model invented
            // cannot hang the Host the way an ordinary backtracking engine can. The timeout stays
            // as a second line of defence; the cost is that backreferences and lookaround are
            // rejected, which the callers are told.
            var options = RegexOptions.NonBacktracking | RegexOptions.CultureInvariant;
            if (ignoreCase) options |= RegexOptions.IgnoreCase;
            return new Matcher(null, StringComparison.Ordinal, new Regex(query, options, TimeSpan.FromSeconds(2)));
        }

        public int Count(string content)
        {
            if (_regex is not null) return _regex.Count(content);
            var count = 0;
            var index = content.IndexOf(_literal!, _comparison);
            while (index >= 0)
            {
                count++;
                index = content.IndexOf(_literal!, index + _literal!.Length, _comparison);
            }

            return count;
        }

        public int FirstIndex(string content) =>
            _regex is not null ? _regex.Match(content).Index : Math.Max(0, content.IndexOf(_literal!, _comparison));
    }
}
