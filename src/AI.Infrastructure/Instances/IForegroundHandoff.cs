namespace AI.Infrastructure.Instances;

/// <summary>
/// Lets the running instance take the foreground from the launch that is about to exit. Windows
/// gives the foreground only to the process the user just started, so that process has to pass it
/// on; other systems need nothing here.
/// </summary>
public interface IForegroundHandoff
{
    void AllowForeground(int processId);
}
