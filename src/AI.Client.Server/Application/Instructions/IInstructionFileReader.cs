namespace AI.Client.Application.Instructions;

/// <summary>A found instruction file; <c>Truncated</c> is true when it was larger than the read limit.</summary>
public sealed record InstructionFile(string Path, string Text, bool Truncated);

/// <summary>
/// Finds instruction files such as AGENTS.md at the roots of a project's directory grants. They are
/// read on every run, so an edit to the file applies to the next model request.
/// </summary>
public interface IInstructionFileReader
{
    Task<IReadOnlyList<InstructionFile>> ReadAsync(IReadOnlyList<string> roots, CancellationToken cancellationToken);
}
