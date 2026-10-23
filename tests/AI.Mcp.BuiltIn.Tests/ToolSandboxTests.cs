using AI.Mcp.BuiltIn.Grants;
using AI.Mcp.BuiltIn.Sandbox;
using AI.Sandbox;
using Shouldly;
using Xunit;

namespace AI.Mcp.BuiltIn.Tests;

/// <summary>What the sandboxed tools tell the model about the sandbox of this machine.</summary>
public sealed class ToolSandboxTests
{
    /// <summary>
    /// A machine whose sandbox cannot open the null device fails Git at every start with what reads
    /// like a denied path; the description must say a grant does not help and what does.
    /// </summary>
    [Fact]
    public void SaysWhenSandboxedProgramsCannotOpenTheNullDevice()
    {
        var closed = new ToolSandbox(new NoGrants(), new Launcher(new SandboxSupport(true, "AppContainer", ClosesNetwork: true, OpensNullDevice: false)));
        var open = new ToolSandbox(new NoGrants(), new Launcher(new SandboxSupport(true, "AppContainer", ClosesNetwork: true)));

        var description = closed.Describe(SandboxedTool.Commands);

        description.ShouldContain("null device");
        description.ShouldContain("process_run_unrestricted rather than asking for a grant");
        closed.Describe(SandboxedTool.Scripts).ShouldContain("null device");
        open.Describe(SandboxedTool.Commands).ShouldNotContain("null device");
    }

    private sealed class NoGrants : IPathGuard
    {
        public IReadOnlyList<DirectoryGrantSpec> Grants => [];

        public string Resolve(string path, GrantCapability capability) => throw new NotSupportedException();
    }

    private sealed class Launcher(SandboxSupport support) : IProcessLauncher
    {
        public SandboxSupport Support => support;

        public ILaunchedProcess Start(ProcessLaunch launch) => throw new NotSupportedException();
    }
}
