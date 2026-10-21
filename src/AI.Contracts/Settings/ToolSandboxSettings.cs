namespace AI.Contracts.Settings;

/// <summary>
/// Which programs of the built-in server run inside an operating system sandbox that lets them
/// read and write only the project's granted directories and the chat's temporary directory.
/// </summary>
/// <param name="Commands">
/// <c>process_run</c> commands. Off by default: toolchains that keep caches elsewhere, such as
/// package caches in the user profile, may fail inside it.
/// </param>
/// <param name="Scripts">
/// <c>cs_run</c> scripts. On by default: a script needs nothing outside the grants but the compiler
/// and the .NET runtime, which the sandbox opens for reading, and a sandboxed script is allowed
/// without asking by default (see <see cref="ToolDefaultDecision"/>). Scripts also run without the
/// network wherever the operating system can close it.
/// </param>
public sealed record ToolSandboxSettings(bool Commands = false, bool Scripts = true);
