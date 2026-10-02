namespace AI.Updates;

public sealed record InstalledUpdateProduct(string Version, string Runtime, bool Supported);

public interface IUpdateInstallationProvider
{
    InstalledUpdateProduct Inspect(string product);
}
