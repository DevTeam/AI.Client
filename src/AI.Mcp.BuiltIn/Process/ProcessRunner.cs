using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.Text;
using AI.Contracts.FileSystem;
using AI.Contracts.Processes;

namespace AI.Mcp.BuiltIn.Process;

public sealed class ProcessRunner : IProcessRunner
{
    private readonly IFileSystem _files;
    private readonly IPath _paths;

    public ProcessRunner(IFileSystem? files = null, IPath? paths = null)
    {
        _files = files ?? new SystemFileSystem();
        _paths = paths ?? new SystemPath();
    }
    private static readonly string[] VariablesNames =
    [
        "PATH",
        "SystemRoot",
        "WINDIR",
        "TEMP",
        "TMP",
        "HOME",
        "USERPROFILE",
        "LOCALAPPDATA",
        "APPDATA",
        "PATHEXT",
        "DOTNET_ROOT",
        "PROCESSOR_ARCHITECTURE",
        "PROCESSOR_ARCHITEW6432",
        "ProgramFiles",
        "ProgramFiles(x86)",
        "ProgramW6432",
        "ProgramData",
        "ALLUSERSPROFILE",
        "COMSPEC",
        "HOMEDRIVE",
        "HOMEPATH"
    ];

    public const int OutputLimit = 32768;
    public const int DefaultTimeoutMs = 600000;
    public const int MaxTimeoutMs = 86400000;

    public async Task<ProcessResult> RunAsync(ProcessRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.Executable);

        if (request.TimeoutMs is < 1 or > MaxTimeoutMs || request.Arguments is null || request.Arguments.Length > 256)
        {
            throw new ArgumentException("Invalid timeout or arguments.", nameof(request));
        }

        var directory = "";
        if (!string.IsNullOrWhiteSpace(request.WorkingDirectory))
        {
            directory = _paths.GetFullPath(request.WorkingDirectory);
            if (!await _files.DirectoryExistsAsync(directory, cancellationToken))
            {
                throw new ArgumentException("Working directory does not exist.", nameof(request));
            }
        }

        string executable;
        if (string.IsNullOrWhiteSpace(request.WorkingDirectory)
            || _paths.IsFullyQualified(request.Executable)
            || (!request.Executable.Contains(_paths.DirectorySeparator) && !request.Executable.Contains(_paths.AltDirectorySeparator)))
        {
            executable = request.Executable;
        }
        else
        {
            executable = _paths.GetFullPath(request.Executable, directory);
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

        // Python writes redirected output in the ANSI code page unless told otherwise, which turns
        // every non-Latin character into a guess; UTF-8 is what the output is decoded as first.
        start.Environment["PYTHONUTF8"] = "1";
        start.Environment["PYTHONIOENCODING"] = "utf-8";

        using var process = new System.Diagnostics.Process();
        process.StartInfo = start;

        // Created before the process starts: a limit the OS cannot apply fails the call, not the machine.
        using var job = new ChildProcessJob(request.Limits ?? ChildProcessLimits.Default);
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
            job.Attach(process);
        }
        catch
        {
            try
            {
                process.Kill(entireProcessTree: true);
            }
            catch (InvalidOperationException)
            {
                // Ignored
            }

            throw;
        }
        process.StandardInput.Close();

        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(request.TimeoutMs);
        var stdout = new MemoryStream();
        var stderr = new MemoryStream();
        var output = DrainAsync(process.StandardOutput.BaseStream, stdout, timeout.Token);
        var errors = DrainAsync(process.StandardError.BaseStream, stderr, timeout.Token);
        var timedOut = false;
        try
        {
            await Task.WhenAll(process.WaitForExitAsync(timeout.Token), output, errors);
        }
        catch (OperationCanceledException)
        {
            timedOut = !cancellationToken.IsCancellationRequested;
            try
            {
                process.Kill(entireProcessTree: true);
            }
            catch (InvalidOperationException)
            {
                 /* Already exited. */
            }

            await process.WaitForExitAsync(CancellationToken.None);
            await Task.WhenAll(output, errors);
            cancellationToken.ThrowIfCancellationRequested();
        }

        cancellationToken.ThrowIfCancellationRequested();
        var (outText, outCut) = Decode(stdout, output.Result);
        var (errorText, errorCut) = Decode(stderr, errors.Result);
        return new ProcessResult(
            process.ExitCode,
            outText,
            errorText,
            timer.ElapsedMilliseconds,
            timedOut || timeout.IsCancellationRequested,
            outCut || errorCut,
            null,
            job.LimitExceeded);
    }

    /// <summary>Raw bytes, enough for <see cref="OutputLimit"/> characters in any encoding.</summary>
    private const int OutputBytes = OutputLimit * 4;

    private static readonly UTF8Encoding StrictUtf8 = new(false, true);

    private static readonly Lazy<Encoding> Legacy = new(() =>
    {
        // Console programs such as cmd and Windows PowerShell write redirected output in the OEM
        // code page of the user's locale, not in UTF-8.
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
        try
        {
            return Encoding.GetEncoding(CultureInfo.CurrentCulture.TextInfo.OEMCodePage);
        }
        catch (Exception error) when (error is ArgumentException or NotSupportedException)
        {
            return Encoding.Latin1;
        }
    });

    private static async Task<bool> DrainAsync(Stream source, MemoryStream target, CancellationToken token)
    {
        var buffer = new byte[4096];
        var truncated = false;
        try
        {
            while (await source.ReadAsync(buffer.AsMemory(), token) is var count && count != 0)
            {
                var keep = (int)Math.Min(count, OutputBytes - target.Length);
                target.Write(buffer, 0, keep);
                truncated |= keep != count;
            }
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested)
        {
            // Ignored
        }

        return truncated;
    }

    /// <summary>
    /// The bytes as UTF-8 when they are valid UTF-8, which most tools write and nothing else
    /// produces by accident, and in the locale's OEM code page otherwise. Decoding everything in
    /// one fixed code page garbled whichever of the two the program did not use.
    /// </summary>
    private static (string Text, bool Truncated) Decode(MemoryStream bytes, bool cut)
    {
        var data = bytes.GetBuffer().AsSpan(0, (int)bytes.Length);
        if (data.StartsWith((ReadOnlySpan<byte>)[0xEF, 0xBB, 0xBF])) data = data[3..];
        string? text = null;
        // A cut at the byte limit can split the last character; its partial bytes say nothing
        // about the encoding of the rest.
        for (var drop = 0; text is null && drop <= (cut ? Math.Min(3, data.Length) : 0); drop++)
        {
            try
            {
                text = StrictUtf8.GetString(data[..^drop]);
            }
            catch (DecoderFallbackException)
            {
                // Not UTF-8, or not yet at a character boundary.
            }
        }

        text ??= Legacy.Value.GetString(data);
        return text.Length > OutputLimit ? (text[..OutputLimit], true) : (text, cut);
    }
}
