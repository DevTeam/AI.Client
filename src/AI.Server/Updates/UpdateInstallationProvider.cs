namespace AI.Updates;

using System.Reflection;
using System.Runtime.InteropServices;

public sealed class UpdateInstallationProvider : IUpdateInstallationProvider
{
    public InstalledUpdateProduct Inspect(string product)
    {
        var runtime = (OperatingSystem.IsWindows() ? "win" : OperatingSystem.IsMacOS() ? "osx" : "linux")
            + "-" + RuntimeInformation.ProcessArchitecture.ToString().ToLowerInvariant();
        var version = Assembly.GetEntryAssembly()?.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion.Split('+')[0] ?? "0.0.0";
        var supported = Assembly.GetEntryAssembly()?.GetName().Name == "AI." + product
            && File.Exists(Path.Combine(AppContext.BaseDirectory, "update-installation.json"))
            && runtime is "win-x64" or "win-arm64" or "osx-x64" or "osx-arm64" or "linux-x64" or "linux-arm64";
        return new InstalledUpdateProduct(version, runtime, supported);
    }
}
