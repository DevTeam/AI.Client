namespace AI.Agent.Evals;

using System.Diagnostics;
using System.Formats.Tar;

/// <summary>Where an attempt's workspace comes from, and the files that are revealed only to the checks.</summary>
public sealed class WorkspaceSource(string repositoryRoot)
{
    public async Task PrepareAsync(Scenario scenario, string root, CancellationToken token)
    {
        Directory.CreateDirectory(root);
        if (scenario.Repository is { } repository)
        {
            // An export, not a clone: a clone would carry the later commits, the fix among them.
            using var archive = await RunAsync(Resolve(repository.Path), ["archive", "--format=tar", repository.Commit], token);
            await TarFile.ExtractToDirectoryAsync(archive, root, overwriteFiles: true, token);
        }

        if (scenario.Workspace is { } fixture) Workspace.Copy(fixture, root);

        if (!scenario.Git) return;
        (await RunAsync(root, ["init", "-q", "-b", "main"], token)).Dispose();
        (await RunAsync(root, ["add", "-A"], token)).Dispose();
        (await RunAsync(root, ["-c", "user.name=Evaluation", "-c", "user.email=evaluation@localhost", "-c", "commit.gpgsign=false",
            "commit", "-q", "--allow-empty", "-m", "Initial state"], token)).Dispose();
    }

    public async Task RevealAsync(Scenario scenario, string root, CancellationToken token)
    {
        foreach (var file in scenario.HiddenFiles ?? [])
        {
            Stream content;
            if (file.Commit is { } commit)
            {
                var repository = scenario.Repository ?? throw new InvalidDataException($"{scenario.Id} takes a hidden file from a commit but has no repository.");
                content = await RunAsync(Resolve(repository.Path), ["show", $"{commit}:{file.Path}"], token);
            }
            else
            {
                content = File.OpenRead(Path.Combine(scenario.Directory, "hidden", file.Path));
            }

            await using (content)
            {
                var target = Path.Combine(root, file.Path);
                Directory.CreateDirectory(Path.GetDirectoryName(target)!);
                await using var output = File.Create(target);
                await content.CopyToAsync(output, token);
            }
        }
    }

    private string Resolve(string path) => Path.GetFullPath(Path.IsPathRooted(path) ? path : Path.Combine(repositoryRoot, path));

    private static async Task<MemoryStream> RunAsync(string directory, string[] arguments, CancellationToken token)
    {
        var start = new ProcessStartInfo("git")
        {
            WorkingDirectory = directory,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true
        };
        foreach (var argument in arguments) start.ArgumentList.Add(argument);
        using var process = Process.Start(start) ?? throw new InvalidOperationException("git did not start.");
        var output = new MemoryStream();
        var copy = process.StandardOutput.BaseStream.CopyToAsync(output, token);
        var error = process.StandardError.ReadToEndAsync(token);
        await process.WaitForExitAsync(token);
        await copy;
        if (process.ExitCode != 0)
            throw new InvalidOperationException($"git {string.Join(' ', arguments)} in {directory} failed: {(await error).Trim()}");
        output.Position = 0;
        return output;
    }
}
