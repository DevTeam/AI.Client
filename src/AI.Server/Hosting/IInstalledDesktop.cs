namespace AI.Server.Hosting;

/// <summary>Whether the Desktop app is installed next to this Host, so the Web app need not offer it.</summary>
public interface IInstalledDesktop
{
    bool IsInstalled { get; }
}
