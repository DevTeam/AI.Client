namespace Build.Targets;

using System.Diagnostics;
using System.Text;

internal sealed class ProcessRunner(IBuildPaths buildPaths) : IProcessRunner
{
    private const int TailSize = 30;

    public async Task<int> RunAsync(
        string operation,
        string fileName,
        IReadOnlyList<string> arguments,
        CancellationToken cancellationToken)
    {
        var logsDirectory = Path.Combine(buildPaths.SolutionDirectory, "artifacts", "logs");
        Directory.CreateDirectory(logsDirectory);
        var logPath = Path.Combine(logsDirectory, $"{GetFileName(operation)}.log");
        var tail = new Queue<string>();

        var startInfo = new ProcessStartInfo(fileName)
        {
            WorkingDirectory = buildPaths.SolutionDirectory,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false
        };
        foreach (var argument in arguments)
        {
            startInfo.ArgumentList.Add(argument);
        }

        using var process = new Process();
        process.StartInfo = startInfo;
        await using var log = new StreamWriter(logPath, false, new UTF8Encoding(false));
        log.AutoFlush = true;
        process.Start();

        var output = CaptureAsync(process.StandardOutput, "stdout");
        var error = CaptureAsync(process.StandardError, "stderr");
        await process.WaitForExitAsync(cancellationToken);
        await Task.WhenAll(output, error);

        if (process.ExitCode == 0)
        {
            Console.WriteLine($"{operation} completed. Log: {Path.GetRelativePath(buildPaths.SolutionDirectory, logPath)}");
            return 0;
        }

        await Console.Error.WriteLineAsync($"{operation} failed with exit code {process.ExitCode}. Log: {Path.GetRelativePath(buildPaths.SolutionDirectory, logPath)}");
        foreach (var line in tail)
        {
            await Console.Error.WriteLineAsync(line);
        }

        return process.ExitCode;

        async Task CaptureAsync(StreamReader reader, string stream)
        {
            while (await reader.ReadLineAsync(cancellationToken) is { } line)
            {
                // ReSharper disable once AccessToDisposedClosure
                await log.WriteLineAsync($"[{stream}] {line}");
                lock (tail)
                {
                    tail.Enqueue(line);
                    while (tail.Count > TailSize)
                    {
                        tail.Dequeue();
                    }
                }
            }
        }
    }

    private static string GetFileName(string operation) =>
        string.Concat(operation.Select(character =>
            char.IsAsciiLetterOrDigit(character) || character is '-' or '_'
                ? char.ToLowerInvariant(character)
                : '-'));
}
