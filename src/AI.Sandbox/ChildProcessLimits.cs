namespace AI.Sandbox;

using System.Globalization;

/// <summary>
/// The resources one tool call may give the process tree it starts: committed memory of the whole
/// tree and the number of processes alive in it at once. The CPU cap and the lowered priority are
/// not part of a call: they are the same for every child, so they are constants here.
/// </summary>
/// <remarks>
/// Memory is sized from the physical memory of the machine rather than from what happens to be
/// free when the call starts: free memory moves with everything else running, and a command that
/// passes or fails depending on that would fail for no reason a caller could see.
/// </remarks>
public sealed record ChildProcessLimits(long MemoryBytes, int Processes)
{
    /// <summary>Smallest memory limit a call may ask for; a .NET child needs about this much to start at all.</summary>
    public const int MinimumMemoryMb = 256;

    /// <summary>Upper bound written into a tool schema; the real maximum is <see cref="MaximumMemoryMb"/>.</summary>
    public const int SchemaMaximumMemoryMb = 16 * 1024 * 1024;

    /// <summary>Default cap on the memory of a tree, however large the machine.</summary>
    public const int DefaultMemoryCapMb = 8 * 1024;

    public const int DefaultProcesses = 256;

    public const int MaximumProcesses = 1024;

    /// <summary>Share of the machine's CPU time the whole tree may use, enforced as a hard cap.</summary>
    public const int CpuPercent = 75;

    private const long Megabyte = 1024 * 1024;

    private static readonly long PhysicalMemoryMb =
        Math.Max(GC.GetGCMemoryInfo().TotalAvailableMemoryBytes / Megabyte, MinimumMemoryMb);

    // Static initializers run in declaration order, so the maximum comes before the default it bounds.

    /// <summary>Three quarters of the physical memory: a caller may raise the limit, never remove it.</summary>
    public static int MaximumMemoryMb { get; } =
        (int)Math.Clamp(PhysicalMemoryMb * 3 / 4, MinimumMemoryMb, SchemaMaximumMemoryMb);

    /// <summary>Half of the physical memory, but no more than <see cref="DefaultMemoryCapMb"/>.</summary>
    public static int DefaultMemoryMb { get; } =
        (int)Math.Clamp(Math.Min(PhysicalMemoryMb / 2, DefaultMemoryCapMb), MinimumMemoryMb, MaximumMemoryMb);

    public static ChildProcessLimits Default { get; } = new(DefaultMemoryMb * Megabyte, DefaultProcesses);

    /// <summary>
    /// The limits a call asked for, with the defaults filling what it left out. A value outside the
    /// supported range is refused rather than clamped, so the caller learns the range instead of
    /// running under a limit it did not ask for.
    /// </summary>
    /// <exception cref="ArgumentOutOfRangeException">A value is outside its supported range.</exception>
    public static ChildProcessLimits Create(int? memoryLimitMb, int? processLimit)
    {
        var memory = memoryLimitMb ?? DefaultMemoryMb;
        if (memory < MinimumMemoryMb || memory > MaximumMemoryMb)
        {
            throw new ArgumentOutOfRangeException(nameof(memoryLimitMb), memory,
                string.Create(CultureInfo.InvariantCulture,
                    $"memoryLimitMb must be in [{MinimumMemoryMb}, {MaximumMemoryMb}] on this machine."));
        }

        var processes = processLimit ?? DefaultProcesses;
        if (processes < 1 || processes > MaximumProcesses)
        {
            throw new ArgumentOutOfRangeException(nameof(processLimit), processes,
                string.Create(CultureInfo.InvariantCulture, $"processLimit must be in [1, {MaximumProcesses}]."));
        }

        return new ChildProcessLimits(memory * Megabyte, processes);
    }

    /// <summary>One sentence for a tool description, with the values of this machine.</summary>
    public static string Describe() =>
        string.Create(CultureInfo.InvariantCulture,
            $"On Windows the whole process tree is limited to `memoryLimitMb` of committed memory (default {DefaultMemoryMb}, allowed {MinimumMemoryMb}..{MaximumMemoryMb}) and `processLimit` live processes (default {DefaultProcesses}, allowed 1..{MaximumProcesses}), and to {CpuPercent}% of the machine's CPU. ")
        + "An allocation or process start past a limit fails inside the program, and `limitExceeded` names the limit that was hit: "
        + "raise it and run again if the program legitimately needs more. Child processes run at below-normal priority on every OS; "
        + "on Linux and macOS memory, process and CPU limits are not applied.";
}
