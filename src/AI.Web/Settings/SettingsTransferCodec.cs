namespace AI.Web.Settings;

using System.Text;
using System.Text.Json;
using AI.Contracts.Usage;
using System.Text.RegularExpressions;
using AI.Contracts.Settings;

public sealed partial class SettingsTransferCodec : ISettingsTransferCodec
{
    private const string StreamableHttp = "StreamableHttp";
    private const string Stdio = "Stdio";
    private const string CredentialHeader = "Authorization";

    private static readonly string[] Policies = ["Allow", "Ask", "Deny"];

    private static readonly JsonWriterOptions WriterOptions = new() { Indented = true };

    // VS Code keeps comments in mcp.json and people paste with a trailing comma left behind.
    private static readonly JsonDocumentOptions ReaderOptions = new()
    {
        CommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true
    };

    public string Export(IReadOnlyList<ConnectionSettings> connections, IReadOnlyList<McpServerSettings> mcpServers)
    {
        using var stream = new MemoryStream();
        using (var writer = new Utf8JsonWriter(stream, WriterOptions))
        {
            writer.WriteStartObject();
            if (connections.Count > 0)
            {
                writer.WriteStartArray("connections");
                foreach (var connection in connections) WriteConnection(writer, connection);
                writer.WriteEndArray();
            }

            if (mcpServers.Count > 0)
            {
                writer.WriteStartObject("mcpServers");
                var names = new HashSet<string>(StringComparer.Ordinal);
                foreach (var server in mcpServers)
                {
                    // The map is keyed by name; a second server with the same name would silently
                    // overwrite the first in every client that reads it.
                    var name = server.Name;
                    for (var index = 2; !names.Add(name); index++) name = $"{server.Name} ({index})";
                    writer.WritePropertyName(name);
                    WriteMcpServer(writer, server);
                }

                writer.WriteEndObject();
            }

            writer.WriteEndObject();
        }

        return Encoding.UTF8.GetString(stream.ToArray());
    }

    private static void WriteConnection(Utf8JsonWriter writer, ConnectionSettings connection)
    {
        writer.WriteStartObject();
        writer.WriteString("name", connection.Name);
        writer.WriteString("baseUrl", connection.BaseUrl);
        writer.WriteString("model", connection.Model);
        if (!connection.Enabled) writer.WriteBoolean("disabled", true);
        if (connection.ContextWindowTokens is { } contextWindow) writer.WriteNumber("contextWindowTokens", contextWindow);
        if (connection.ReservedOutputTokens is { } reserved) writer.WriteNumber("reservedOutputTokens", reserved);
        if (connection.Capability is { } capability) writer.WriteNumber("capability", capability);
        if (!string.IsNullOrWhiteSpace(connection.GoodFor)) writer.WriteString("goodFor", connection.GoodFor);
        if (connection.Prices is { } prices)
        {
            writer.WriteStartObject("prices");
            writer.WriteNumber("input", prices.Input);
            writer.WriteNumber("output", prices.Output);
            if (prices.CachedInput is { } cached) writer.WriteNumber("cachedInput", cached);
            writer.WriteEndObject();
        }
        // The key stays on this Host. An empty value still says that one is needed.
        if (connection.HasCredential) writer.WriteString("apiKey", string.Empty);
        writer.WriteEndObject();
    }

    private static void WriteMcpServer(Utf8JsonWriter writer, McpServerSettings server)
    {
        writer.WriteStartObject();
        if (server.Transport == Stdio)
        {
            writer.WriteString("command", server.Command ?? string.Empty);
            if (server.Arguments.Count > 0)
            {
                writer.WriteStartArray("args");
                foreach (var argument in server.Arguments) writer.WriteStringValue(argument);
                writer.WriteEndArray();
            }

            if (!string.IsNullOrWhiteSpace(server.WorkingDirectory)) writer.WriteString("cwd", server.WorkingDirectory);
            if (server.EnvironmentVariables.Count > 0)
            {
                writer.WriteStartObject("env");
                foreach (var variable in server.EnvironmentVariables)
                    writer.WriteString(variable.Name, variable.IsSecret ? string.Empty : variable.Value ?? string.Empty);
                writer.WriteEndObject();
                // Only the secrets the name does not give away need spelling out; a GITHUB_TOKEN is
                // recognised on the way back in anyway, and other clients ignore the extra field.
                var unrecognised = server.EnvironmentVariables
                    .Where(variable => variable.IsSecret && !LooksSecret(variable.Name)).ToArray();
                if (unrecognised.Length > 0)
                {
                    writer.WriteStartArray("secretEnv");
                    foreach (var variable in unrecognised) writer.WriteStringValue(variable.Name);
                    writer.WriteEndArray();
                }
            }
        }
        else
        {
            writer.WriteString("type", "http");
            writer.WriteString("url", server.Url ?? string.Empty);
            if (server.HasCredential)
            {
                writer.WriteStartObject("headers");
                writer.WriteString(CredentialHeader, string.Empty);
                writer.WriteEndObject();
            }
        }

        if (!server.Enabled) writer.WriteBoolean("disabled", true);
        if (server.Policy != "Ask") writer.WriteString("policy", server.Policy);
        writer.WriteEndObject();
    }

    public SettingsTransferParseResult Parse(string text)
    {
        if (string.IsNullOrWhiteSpace(text)) return SettingsTransferParseResult.Failed("Nothing to import: the text is empty.");
        using var document = TryRead(text.Trim(), out var error);
        if (document is null) return SettingsTransferParseResult.Failed(error!);
        var root = document.RootElement;
        if (root.ValueKind != JsonValueKind.Object)
            return SettingsTransferParseResult.Failed("Nothing to import: expected a JSON object with mcpServers or connections.");

        var reader = new Reader();
        var hasSection = false;
        // "servers" is VS Code's name for the same map.
        foreach (var sectionName in new[] { "mcpServers", "servers" })
        {
            if (!TryGet(root, sectionName, out var section)) continue;
            hasSection = true;
            if (section.ValueKind == JsonValueKind.Object)
            {
                foreach (var property in section.EnumerateObject()) reader.ReadMcpServer(property.Name, property.Value);
            }
        }

        if (TryGet(root, "connections", out var connections))
        {
            hasSection = true;
            if (connections.ValueKind == JsonValueKind.Array)
            {
                foreach (var item in connections.EnumerateArray()) reader.ReadConnection(null, item);
            }
            else if (connections.ValueKind == JsonValueKind.Object)
            {
                foreach (var property in connections.EnumerateObject()) reader.ReadConnection(property.Name, property.Value);
            }
        }

        if (!hasSection)
        {
            if (LooksLikeMcpServer(root)) reader.ReadMcpServer(null, root);
            else if (LooksLikeConnection(root)) reader.ReadConnection(null, root);
            // The inside of an mcpServers map, copied with its braces but without the wrapper.
            else if (root.EnumerateObject().Any() && root.EnumerateObject().All(property => LooksLikeMcpServer(property.Value)))
            {
                foreach (var property in root.EnumerateObject()) reader.ReadMcpServer(property.Name, property.Value);
            }
            else
            {
                return SettingsTransferParseResult.Failed("Nothing to import: expected mcpServers, servers or connections.");
            }
        }

        if (reader.Connections.Count == 0 && reader.McpServers.Count == 0 && reader.Skipped.Count == 0)
            return SettingsTransferParseResult.Failed("Nothing to import: the text lists no connections or MCP servers.");
        return new SettingsTransferParseResult(reader.Connections, reader.McpServers, reader.Skipped, reader.SecretValuesDropped, null);
    }

    private static JsonDocument? TryRead(string text, out string? error)
    {
        try
        {
            error = null;
            return JsonDocument.Parse(text, ReaderOptions);
        }
        catch (JsonException exception)
        {
            // "github": { ... } copied out of an mcpServers map, without the braces around it.
            if (text.StartsWith('"'))
            {
                try
                {
                    error = null;
                    return JsonDocument.Parse($"{{{text}}}", ReaderOptions);
                }
                catch (JsonException)
                {
                    // The original message points at the text the user actually has.
                }
            }

            error = exception.LineNumber is { } line
                ? $"This is not valid JSON (line {line + 1}, position {(exception.BytePositionInLine ?? 0) + 1})."
                : "This is not valid JSON.";
            return null;
        }
    }

    private static bool LooksLikeMcpServer(JsonElement element) =>
        element.ValueKind == JsonValueKind.Object
        && (TryGet(element, "command", out _) || TryGet(element, "url", out _) || TryGet(element, "serverUrl", out _));

    private static bool LooksLikeConnection(JsonElement element) =>
        element.ValueKind == JsonValueKind.Object && ConnectionBaseUrl(element) is not null;

    private static string? ConnectionBaseUrl(JsonElement element) =>
        String(element, "baseUrl") ?? String(element, "apiBase") ?? String(element, "baseURL") ?? String(element, "base_url");

    /// <summary>Names that hold a key in practice: GITHUB_TOKEN, OPENAI_API_KEY, X-Api-Key, DB_PASSWORD.</summary>
    private static bool LooksSecret(string name) => SecretNamePattern().IsMatch(name);

    /// <summary>
    /// What READMEs put where the key goes: &lt;YOUR_TOKEN&gt;, ${input:token}, your-api-key, xxxx.
    /// Leaving one out loses nothing; leaving out anything else means a key was in the text.
    /// </summary>
    private static bool IsPlaceholder(string value) => value.Length == 0 || PlaceholderPattern().IsMatch(value);

    private static bool IsReference(string value) => ReferencePattern().IsMatch(value);

    private static bool TryGet(JsonElement element, string name, out JsonElement value)
    {
        foreach (var property in element.EnumerateObject())
        {
            if (!string.Equals(property.Name, name, StringComparison.OrdinalIgnoreCase)) continue;
            value = property.Value;
            return true;
        }

        value = default;
        return false;
    }

    private static string? String(JsonElement element, string name) =>
        TryGet(element, name, out var value) ? Text(value) : null;

    private static string? Text(JsonElement value) => value.ValueKind switch
    {
        JsonValueKind.String => value.GetString(),
        JsonValueKind.Number or JsonValueKind.True or JsonValueKind.False => value.GetRawText(),
        _ => null
    };

    private static long? Number(JsonElement element, string name) =>
        TryGet(element, name, out var value) && value.ValueKind == JsonValueKind.Number && value.TryGetInt64(out var number) ? number : null;

    private static bool? Boolean(JsonElement element, string name) =>
        TryGet(element, name, out var value) && value.ValueKind is JsonValueKind.True or JsonValueKind.False ? value.GetBoolean() : null;

    private static bool IsEnabled(JsonElement element) =>
        Boolean(element, "disabled") != true && Boolean(element, "enabled") != false;

    /// <summary>
    /// A name for a server pasted on its own: the package it runs, or the host it talks to.
    /// <c>npx -y @modelcontextprotocol/server-github@latest</c> becomes <c>server-github</c>.
    /// </summary>
    private static string DeriveServerName(string? command, IReadOnlyList<string> arguments, string? url)
    {
        if (Uri.TryCreate(url, UriKind.Absolute, out var uri) && uri.Host.Length > 0) return uri.Host;
        var package = arguments.LastOrDefault(argument => !argument.StartsWith('-')) ?? command;
        if (string.IsNullOrWhiteSpace(package)) return "MCP server";
        var name = package.Replace('\\', '/');
        name = name[(name.LastIndexOf('/') + 1)..];
        var version = name.IndexOf('@', 1);
        if (version > 0) name = name[..version];
        if (name.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)) name = name[..^4];
        return name.Length == 0 ? "MCP server" : name;
    }

    [GeneratedRegex("key|token|secret|password|passwd|credential|auth", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex SecretNamePattern();

    [GeneratedRegex(@"^\s*(bearer\s+)?(<[^>]*>|\$\{[^}]*\}|\{\{.*\}\}|\[[^\]]*\]|[^\s]*your[^\s]*|x{3,}|\*{3,}|\.{3}|…|changeme|none|null)\s*$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex PlaceholderPattern();

    [GeneratedRegex(@"^\s*\$\{[^}]+\}\s*$", RegexOptions.CultureInvariant)]
    private static partial Regex ReferencePattern();

    /// <summary>Collects items while walking one document.</summary>
    private sealed class Reader
    {
        public List<ImportedSettingsItem<ConnectionSettings>> Connections { get; } = [];
        public List<ImportedSettingsItem<McpServerSettings>> McpServers { get; } = [];
        public List<SkippedImportItem> Skipped { get; } = [];
        public bool SecretValuesDropped { get; private set; }

        public void ReadMcpServer(string? key, JsonElement element)
        {
            var displayName = key ?? String(element, "name") ?? "MCP server";
            if (element.ValueKind != JsonValueKind.Object)
            {
                Skipped.Add(new SkippedImportItem(displayName, "Not a server description."));
                return;
            }

            var notes = new List<string>();
            var command = String(element, "command");
            var url = String(element, "url") ?? String(element, "serverUrl");
            var type = String(element, "type")?.Trim().ToLowerInvariant().Replace("-", string.Empty).Replace("_", string.Empty);
            string transport;
            switch (type)
            {
                case "stdio":
                    transport = Stdio;
                    break;
                case "http" or "streamablehttp":
                    transport = StreamableHttp;
                    break;
                case "sse":
                    transport = StreamableHttp;
                    notes.Add("SSE servers are reached over Streamable HTTP; older servers that speak only SSE may not connect.");
                    break;
                default:
                    if (!string.IsNullOrWhiteSpace(command)) transport = Stdio;
                    else if (!string.IsNullOrWhiteSpace(url)) transport = StreamableHttp;
                    else
                    {
                        Skipped.Add(new SkippedImportItem(displayName, type is null
                            ? "Neither a command nor a URL."
                            : $"Transport \"{type}\" is not supported."));
                        return;
                    }

                    break;
            }

            if (transport == Stdio && string.IsNullOrWhiteSpace(command))
            {
                Skipped.Add(new SkippedImportItem(displayName, "A stdio server needs a command."));
                return;
            }

            if (transport == StreamableHttp && !Uri.TryCreate(url, UriKind.Absolute, out _))
            {
                Skipped.Add(new SkippedImportItem(displayName, "An HTTP server needs an absolute URL."));
                return;
            }

            IReadOnlyList<string> arguments = TryGet(element, "args", out var args) && args.ValueKind == JsonValueKind.Array
                ? args.EnumerateArray().Select(Text).OfType<string>().ToArray()
                : [];
            var name = key ?? String(element, "name") ?? DeriveServerName(command, arguments, url);
            var declaredSecrets = TryGet(element, "secretEnv", out var secretEnv) && secretEnv.ValueKind == JsonValueKind.Array
                ? secretEnv.EnumerateArray().Select(Text).OfType<string>().ToHashSet(StringComparer.OrdinalIgnoreCase)
                : [];
            var environment = new List<McpEnvironmentVariableSettings>();
            if (transport == Stdio && TryGet(element, "env", out var env) && env.ValueKind == JsonValueKind.Object)
            {
                foreach (var variable in env.EnumerateObject())
                {
                    var value = Text(variable.Value) ?? string.Empty;
                    // A reference to somewhere else (${env:X}, ${input:x}) is a key by another name:
                    // it has no value here, and the real one is what has to be typed in.
                    if (declaredSecrets.Contains(variable.Name) || LooksSecret(variable.Name) || IsReference(value))
                    {
                        Drop(value);
                        environment.Add(new McpEnvironmentVariableSettings(variable.Name, null, true, false));
                    }
                    else
                    {
                        environment.Add(new McpEnvironmentVariableSettings(variable.Name, value, false, false));
                    }
                }
            }

            if (transport == Stdio && TryGet(element, "envFile", out _))
                notes.Add("envFile is not supported; add the variables it holds by hand.");

            var credentialOmitted = false;
            if (transport == StreamableHttp && TryGet(element, "headers", out var headers) && headers.ValueKind == JsonValueKind.Object)
            {
                foreach (var header in headers.EnumerateObject())
                {
                    if (string.Equals(header.Name, CredentialHeader, StringComparison.OrdinalIgnoreCase) || LooksSecret(header.Name))
                    {
                        credentialOmitted = true;
                        Drop(Text(header.Value) ?? string.Empty);
                    }
                    else
                    {
                        notes.Add($"Header {header.Name} is not supported and was left out.");
                    }
                }
            }

            var policy = String(element, "policy") is { } policyText
                ? Policies.FirstOrDefault(item => string.Equals(item, policyText.Trim(), StringComparison.OrdinalIgnoreCase)) ?? "Ask"
                : "Ask";
            var settings = new McpServerSettings(
                Guid.CreateVersion7(), name.Trim(), transport, IsEnabled(element), policy,
                transport == StreamableHttp ? url!.Trim() : null,
                transport == Stdio ? command!.Trim() : null,
                transport == Stdio ? arguments : [],
                transport == Stdio ? String(element, "cwd")?.Trim() : null,
                environment,
                false);
            McpServers.Add(new ImportedSettingsItem<McpServerSettings>(settings, credentialOmitted, notes));
        }

        public void ReadConnection(string? key, JsonElement element)
        {
            var baseUrl = element.ValueKind == JsonValueKind.Object ? ConnectionBaseUrl(element)?.Trim() : null;
            var displayName = (element.ValueKind == JsonValueKind.Object ? String(element, "name") : null) ?? key ?? baseUrl ?? "Connection";
            if (!Uri.TryCreate(baseUrl, UriKind.Absolute, out var uri) || uri.Scheme is not ("http" or "https"))
            {
                Skipped.Add(new SkippedImportItem(displayName, "A connection needs an absolute HTTP or HTTPS base URL."));
                return;
            }

            var notes = new List<string>();
            var model = String(element, "model")?.Trim() ?? string.Empty;
            if (model.Length == 0) notes.Add("No model given; pick one before closing settings.");
            var credentialOmitted = false;
            if (TryGet(element, "apiKey", out var apiKey))
            {
                credentialOmitted = true;
                Drop(Text(apiKey) ?? string.Empty);
            }

            var name = String(element, "name")?.Trim() is { Length: > 0 } explicitName ? explicitName : key?.Trim() is { Length: > 0 } keyName ? keyName : uri.Host;
            var settings = new ConnectionSettings(
                Guid.CreateVersion7(), name, baseUrl!.TrimEnd('/'), model, IsEnabled(element), false, false, false,
                Rating(Number(element, "capability")),
                String(element, "goodFor")?.Trim() is { Length: > 0 } goodFor ? goodFor : null,
                Number(element, "contextWindowTokens"), Number(element, "reservedOutputTokens"), Prices(element));
            Connections.Add(new ImportedSettingsItem<ConnectionSettings>(settings, credentialOmitted, notes));
        }

        private static TokenPrices? Prices(JsonElement element) =>
            element.ValueKind == JsonValueKind.Object && element.TryGetProperty("prices", out var prices)
            && prices.ValueKind == JsonValueKind.Object
            && Price(prices, "input") is { } input && Price(prices, "output") is { } output
                ? new TokenPrices(input, output, Price(prices, "cachedInput"))
                : null;

        private static decimal? Price(JsonElement element, string name) =>
            element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.Number
            && value.TryGetDecimal(out var price) && price >= 0 ? price : null;

        private static int? Rating(long? value) => value is >= 1 and <= 5 ? (int)value : null;

        private void Drop(string value)
        {
            if (!IsPlaceholder(value.Trim())) SecretValuesDropped = true;
        }
    }
}
