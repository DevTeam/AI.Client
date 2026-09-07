using System.ComponentModel;
using System.Diagnostics;
using System.Text;

namespace AI.Client.Mcp.BuiltIn;

public static class ProcessRunner
{
    private static readonly string[] VariablesNames =
    [
        "PATH", "SystemRoot", "WINDIR", "TEMP", "TMP", "HOME", "USERPROFILE", "LOCALAPPDATA", "APPDATA", "PATHEXT", "DOTNET_ROOT",
        "PROCESSOR_ARCHITECTURE", "PROCESSOR_ARCHITEW6432", "ProgramFiles", "ProgramFiles(x86)", "ProgramW6432", "ProgramData", "ALLUSERSPROFILE",
        "COMSPEC", "HOMEDRIVE", "HOMEPATH"
    ];

    public const int OutputLimit = 32768;

    public static async Task<ProcessResult> RunAsync(ProcessRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.Executable);

        if (request.TimeoutMs is < 1 or > 120000 || request.Arguments is null || request.Arguments.Length > 256)
        {
            throw new ArgumentException("Invalid timeout or arguments.", nameof(request));
        }

        var directory = "";
        if (!string.IsNullOrWhiteSpace(request.WorkingDirectory))
        {
            directory = Path.GetFullPath(request.WorkingDirectory);
            if (!Directory.Exists(directory))
            {
                throw new ArgumentException("Working directory does not exist.", nameof(request));
            }
        }

        string executable;
        if (string.IsNullOrWhiteSpace(request.WorkingDirectory)
            || Path.IsPathFullyQualified(request.Executable)
            || (!request.Executable.Contains(Path.DirectorySeparatorChar) && !request.Executable.Contains(Path.AltDirectorySeparatorChar)))
        {
            executable = request.Executable;
        }
        else
        {
            executable = Path.GetFullPath(request.Executable, directory);
        }

        var start = new ProcessStartInfo(executable)
        {
            WorkingDirectory = directory,
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            RedirectStandardInput = true
        };

        foreach (var argument in request.Arguments)
        {
            start.ArgumentList.Add(argument);
        }

        // Deliberately exclude credentials and arbitrary Host environment variables.
        start.Environment.Clear();
        foreach (var name in VariablesNames)
        {
            if (Environment.GetEnvironmentVariable(name) is { } value)
            {
                start.Environment[name] = value;
            }
        }

        using var process = new Process();
        process.StartInfo = start;

        using var job = OperatingSystem.IsWindows() ? new WindowsProcessJob() : null;
        var timer = Stopwatch.StartNew();
        cancellationToken.ThrowIfCancellationRequested();

        try
        {
            process.Start();
        }
        catch (Exception error) when (error is Win32Exception or InvalidOperationException)
        {
            return new ProcessResult(null, "", "", timer.ElapsedMilliseconds, false, false, error.Message);
        }

        try
        {
            if (OperatingSystem.IsWindows()) job!.Attach(process);
        }
        catch
        {
            try { process.Kill(entireProcessTree: true); }
            catch (InvalidOperationException) { }
            throw;
        }
        process.StandardInput.Close();

        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(request.TimeoutMs);
        var stdout = new StringBuilder();
        var stderr = new StringBuilder();
        var output = DrainAsync(process.StandardOutput, stdout, timeout.Token);
        var errors = DrainAsync(process.StandardError, stderr, timeout.Token);
        var timedOut = false;
        try
        {
            await Task.WhenAll(process.WaitForExitAsync(timeout.Token), output, errors);
        }
        catch (OperationCanceledException)
        {
            timedOut = !cancellationToken.IsCancellationRequested;
            try { process.Kill(entireProcessTree: true); }
            catch (InvalidOperationException) { /* Already exited. */ }
            await process.WaitForExitAsync(CancellationToken.None);
            await Task.WhenAll(output, errors);
            cancellationToken.ThrowIfCancellationRequested();
        }

        cancellationToken.ThrowIfCancellationRequested();
        return new ProcessResult(
            process.ExitCode,
            stdout.ToString(),
            stderr.ToString(),
            timer.ElapsedMilliseconds,
            timedOut || timeout.IsCancellationRequested,
            output.Result || errors.Result,
            null);
    }

    private static async Task<bool> DrainAsync(StreamReader reader, StringBuilder target, CancellationToken token)
    {
        var buffer = new char[4096];
        var truncated = false;
        try
        {
            while (await reader.ReadAsync(buffer.AsMemory(), token) is var count && count != 0)
            {
                var keep = Math.Min(count, OutputLimit - target.Length);
                target.Append(buffer, 0, keep);
                truncated |= keep != count;
            }
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested)
        {

        }

        return truncated;
    }
}
