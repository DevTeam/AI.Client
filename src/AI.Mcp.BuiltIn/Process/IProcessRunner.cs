namespace AI.Mcp.BuiltIn.Process;

public interface IProcessRunner
{
    Task<ProcessResult> RunAsync(ProcessRequest request, CancellationToken cancellationToken, ProcessOutput? output = null);
}

/// <summary>Optional full byte logs. The caller owns the streams; the runner drains both even after its response limit.</summary>
public sealed record ProcessOutput(Stream StandardOutput, Stream StandardError);
