using AI.Client.Mcp.BuiltIn;

var composition = new Composition();
await using var server = composition.Server;
await server.RunAsync();
