namespace AI.Updates;

using AI.Contracts.Updates;

public interface IUpdateInstaller
{
    Task LaunchAsync(UpdateState state, string directory, string package, string result, CancellationToken token);
}
