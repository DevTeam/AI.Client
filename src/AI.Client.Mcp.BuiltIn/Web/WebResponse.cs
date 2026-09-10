namespace AI.Client.Mcp.BuiltIn.Web;

public sealed record WebResponse(
    int Status,
    string FinalUrl,
    string ContentType,
    string Body,
    bool BodyTruncated,
    string? Error);
