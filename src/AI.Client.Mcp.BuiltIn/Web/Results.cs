namespace AI.Client.Mcp.BuiltIn.Web;

public sealed record FetchResult(
    string Url,
    int Status,
    string ContentType,
    string Content,
    int StartIndex,
    int NextIndex,
    bool Truncated,
    string? Error);

public sealed record WebResponse(
    int Status,
    string FinalUrl,
    string ContentType,
    string Body,
    bool BodyTruncated,
    string? Error);
