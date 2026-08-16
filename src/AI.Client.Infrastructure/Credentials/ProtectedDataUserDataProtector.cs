namespace AI.Client.Infrastructure.Credentials;

using System.Runtime.Versioning;
using System.Security.Cryptography;

public sealed class ProtectedDataUserDataProtector : IUserDataProtector
{
    private static readonly byte[] Entropy = [.. "AI.Client.EndpointCredential.v1"u8];

    public byte[] Protect(byte[] data)
    {
        return OperatingSystem.IsWindows()
            ? ProtectForWindows(data)
            : throw new PlatformNotSupportedException("Windows DPAPI is required for endpoint credentials.");
    }

    public byte[] Unprotect(byte[] protectedData)
    {
        return OperatingSystem.IsWindows()
            ? UnprotectForWindows(protectedData)
            : throw new PlatformNotSupportedException("Windows DPAPI is required for endpoint credentials.");
    }

    [SupportedOSPlatform("windows")]
    private static byte[] ProtectForWindows(byte[] data) =>
        ProtectedData.Protect(data, Entropy, DataProtectionScope.CurrentUser);

    [SupportedOSPlatform("windows")]
    private static byte[] UnprotectForWindows(byte[] protectedData) =>
        ProtectedData.Unprotect(protectedData, Entropy, DataProtectionScope.CurrentUser);
}
