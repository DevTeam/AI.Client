namespace AI.Contracts.Settings;

/// <summary>
/// Whether the built-in server runs <c>process_run</c> commands and <c>cs_run</c> scripts inside an
/// operating system sandbox that lets them read and write only the project's granted directories and
/// the chat's temporary directory. Off by default: toolchains that keep caches elsewhere, such as
/// package caches in the user profile, may fail inside it.
/// </summary>
/// <param name="Enabled">Run commands and C# scripts in the sandbox.</param>
public sealed record ToolSandboxSettings(bool Enabled = false);
