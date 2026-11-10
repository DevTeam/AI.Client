namespace AI.Search.Matchers;

using System.Text.RegularExpressions;

/// <summary>Builds the matcher a request asks for.</summary>
internal static class LineMatchers
{
    public const int BinaryProbeBytes = 8192;

    /// <summary>
    /// The matcher for the request. A pattern runs in the non-backtracking engine when that engine
    /// can express it — its cost is linear in the line, so no line can make the search hang — and in
    /// the ordinary engine when it cannot, which is where a backreference or a lookaround lands.
    /// The ordinary engine is given the request's timeout as the second line of defence.
    /// </summary>
    /// <remarks>
    /// Both engines are compiled. Without that flag the runtime interprets a pattern it has no cache
    /// entry for, and the linear engine interpreted costs about four times what the compiled one
    /// does — measured on twenty thousand generated files, a regex workload went from 921 ms to
    /// 274 ms when the flag was added, which is the difference between losing to <c>rg</c> and
    /// matching it. Compilation costs about thirty microseconds once per search and every worker of
    /// a parallel search shares the one object, so it is paid once rather than per file.
    /// </remarks>
    /// <exception cref="ArgumentException">The regular expression is invalid.</exception>
    /// <exception cref="NotSupportedException">
    /// The pattern uses a construct neither engine can compile with the requested options.
    /// </exception>
    public static ILineMatcher Create(SearchRequest request)
    {
        var ignoreCase = IgnoreCase(request);
        if (request.Kind == PatternKind.Literal)
        {
            return new LiteralLineMatcher(request.Pattern,
                ignoreCase ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal, request.WholeWord);
        }

        var required = RequiredLiteral.Find(request.Pattern, ignoreCase);

        var options = RegexOptions.CultureInvariant;
        if (ignoreCase)
        {
            options |= RegexOptions.IgnoreCase;
        }

        Regex regex;
        try
        {
            regex = new Regex(request.Pattern, options | RegexOptions.NonBacktracking | RegexOptions.Compiled);
        }
        catch (NotSupportedException)
        {
            // The pattern uses a construct the linear engine does not implement. The caller asked
            // for this pattern, so the engine that can run it compiles it.
            regex = new Regex(request.Pattern, options | RegexOptions.Compiled, Timeout(request));
        }

        return new RegexLineMatcher(regex, request.WholeWord, required);
    }

    /// <summary>
    /// Whether letter case matters: an insensitive request says so directly, a sensitive one never
    /// does, and smart case compares case-sensitively only when the pattern has an uppercase letter.
    /// </summary>
    private static bool IgnoreCase(SearchRequest request) => request.Case switch
    {
        CaseMode.Insensitive => true,
        CaseMode.Sensitive => false,
        _ => !HasUppercase(request.Pattern)
    };

    private static TimeSpan Timeout(SearchRequest request) =>
        request.RegexTimeout > TimeSpan.Zero ? request.RegexTimeout : TimeSpan.FromSeconds(2);

    private static bool HasUppercase(string pattern)
    {
        foreach (var character in pattern)
        {
            if (char.IsUpper(character))
            {
                return true;
            }
        }

        return false;
    }
}
