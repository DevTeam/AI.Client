namespace AI.Sandbox;

using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Text;
using Microsoft.Win32.SafeHandles;
using static WindowsNative;

/// <summary>
/// Starts a program on Windows already inside its job. <see cref="Process.Start()"/> cannot do that:
/// the program runs from the moment it is created, and a child it started before the caller put it
/// in the job escaped every limit and outlived the kill. Here the program is created suspended with
/// the job in its attribute list, so it and everything it ever starts are in the job before any of
/// its code runs, and only then is it let go.
/// </summary>
/// <remarks>
/// Creating the process directly means creating its standard streams too: three anonymous pipes,
/// whose child ends are the only handles the program inherits. The caller still gets an ordinary
/// <see cref="Process"/> for waiting, the exit code and killing, opened by id while the program is
/// suspended and cannot have exited yet.
/// </remarks>
[SupportedOSPlatform("windows")]
internal static class WindowsProcess
{
    private static readonly UTF8Encoding Utf8 = new(encoderShouldEmitUTF8Identifier: false);

    public static Started Start(ProcessStartInfo start, SafeHandle job)
    {
        var input = Channel.Open(start.RedirectStandardInput, childReads: true);
        var output = Channel.Open(start.RedirectStandardOutput, childReads: false);
        var error = Channel.Open(start.RedirectStandardError, childReads: false);
        Channel[] channels = [input, output, error];
        ProcessInformation information;
        try
        {
            information = Create(start, job, input.Child, output.Child, error.Child);
        }
        catch
        {
            foreach (var channel in channels) channel.Parent?.Dispose();
            throw;
        }
        finally
        {
            // The program holds its own copies now; the parent keeps only its ends, so a reader sees
            // the end of a stream when the program and its children close theirs.
            foreach (var channel in channels) channel.Child.Dispose();
        }

        var handle = new SafeProcessHandle(information.Process, ownsHandle: true);
        Process process;
        try
        {
            process = Process.GetProcessById(information.ProcessId);
            // Opened while the program is suspended: the process object keeps this handle, so waiting
            // and the exit code still work after the program ends.
            _ = process.SafeHandle;
            if (ResumeThread(information.Thread) == uint.MaxValue)
            {
                throw new Win32Exception(Marshal.GetLastPInvokeError(), "Cannot resume the started program");
            }
        }
        catch
        {
            _ = TerminateProcess(information.Process, 1);
            handle.Dispose();
            foreach (var channel in channels) channel.Parent?.Dispose();
            throw;
        }
        finally
        {
            _ = CloseHandle(information.Thread);
        }

        return new Started(
            process,
            handle,
            input.Parent is { } inputEnd
                ? new StreamWriter(new FileStream(inputEnd, FileAccess.Write, 4096, isAsync: false), start.StandardInputEncoding ?? Utf8) { AutoFlush = true }
                : StreamWriter.Null,
            Reader(output.Parent, start.StandardOutputEncoding),
            Reader(error.Parent, start.StandardErrorEncoding));
    }

    private static StreamReader Reader(SafeFileHandle? parentEnd, Encoding? encoding) =>
        parentEnd is null ? StreamReader.Null : new StreamReader(new FileStream(parentEnd, FileAccess.Read, 4096, isAsync: false), encoding ?? Utf8);

    /// <summary>
    /// One standard stream: the end the program inherits and, when the caller redirected it, the end
    /// the caller keeps. A stream that is not redirected goes to the null device, as it would for a
    /// program without a console.
    /// </summary>
    private sealed record Channel(SafeFileHandle Child, SafeFileHandle? Parent)
    {
        public static Channel Open(bool redirect, bool childReads)
        {
            Channel channel;
            if (redirect)
            {
                Check(CreatePipe(out var read, out var write, 0, 0));
                channel = childReads ? new Channel(read, write) : new Channel(write, read);
            }
            else
            {
                channel = new Channel(File.OpenHandle("NUL", FileMode.Open, childReads ? FileAccess.Read : FileAccess.Write), null);
            }

            Check(SetHandleInformation(channel.Child.DangerousGetHandle(), HandleFlagInherit, HandleFlagInherit));
            return channel;
        }
    }

    private static unsafe ProcessInformation Create(ProcessStartInfo start, SafeHandle job,
        SafeFileHandle input, SafeFileHandle output, SafeFileHandle error)
    {
        using var attributes = new AttributeList(2);
        attributes.Add(HandleListAttribute, [input.DangerousGetHandle(), output.DangerousGetHandle(), error.DangerousGetHandle()]);
        attributes.Add(JobListAttribute, [job.DangerousGetHandle()]);
        var startup = new StartupInfoEx
        {
            StartupInfo = new StartupInfo
            {
                Size = sizeof(StartupInfoEx),
                Flags = UseStandardHandles,
                StandardInput = input.DangerousGetHandle(),
                StandardOutput = output.DangerousGetHandle(),
                StandardError = error.DangerousGetHandle()
            },
            AttributeList = attributes.Handle
        };

        var commandLine = (CommandLineOf(start) + '\0').ToCharArray();
        var environment = EnvironmentBlock(start.Environment).ToCharArray();
        var flags = ExtendedStartupInfoPresent | CreateSuspended | CreateUnicodeEnvironment
                    | (start.CreateNoWindow ? CreateNoWindow : 0);
        var directory = string.IsNullOrEmpty(start.WorkingDirectory) ? null : start.WorkingDirectory;
        ProcessInformation information;
        fixed (char* line = commandLine)
        fixed (char* block = environment)
        {
            if (!CreateProcessW(null, line, 0, 0, true, flags, block, directory, ref startup, out information))
            {
                throw new Win32Exception(Marshal.GetLastPInvokeError());
            }
        }

        return information;
    }

    /// <summary>The program and its arguments quoted the way the C runtime splits them, as <see cref="Process.Start()"/> does.</summary>
    private static string CommandLineOf(ProcessStartInfo start)
    {
        if (start.ArgumentList.Count > 0 || string.IsNullOrEmpty(start.Arguments))
        {
            return CommandLine.Build(start.FileName, start.ArgumentList);
        }

        return CommandLine.Build(start.FileName, []) + " " + start.Arguments;
    }

    /// <summary>
    /// The environment as Windows takes it: <c>name=value</c> entries sorted by name, each ended by
    /// a null character, and one more null character after the last.
    /// </summary>
    private static string EnvironmentBlock(IDictionary<string, string?> environment)
    {
        var block = new StringBuilder();
        foreach (var (name, value) in environment.Where(pair => pair.Value is not null).OrderBy(pair => pair.Key, StringComparer.OrdinalIgnoreCase))
        {
            block.Append(name).Append('=').Append(value).Append('\0');
        }

        return block.Append('\0').ToString();
    }

    /// <summary>The started program: its process, the handle that keeps it addressable, and its streams.</summary>
    public sealed record Started(Process Process, SafeProcessHandle Handle, StreamWriter Input, StreamReader Output, StreamReader Error);
}
