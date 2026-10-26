namespace AI.Sandbox;

using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Security.AccessControl;
using Microsoft.Win32.SafeHandles;

/// <summary>Updates a directory's own DACL without reapplying inherited entries to its descendants.</summary>
[SupportedOSPlatform("windows")]
internal static partial class WindowsDirectorySecurity
{
    private const uint MaximumAllowed = 0x02000000;
    private const uint BackupSemantics = 0x02000000;
    private const int FileObject = 1;
    private const uint DaclSecurityInformation = 4;

    public static unsafe void SetAccessControl(string path, DirectorySecurity security)
    {
        // SetNamedSecurityInfo (used by DirectoryInfo.SetAccessControl) walks the subtree even
        // when the new ACE does not inherit. The documented MAXIMUM_ALLOWED handle form of
        // SetSecurityInfo suppresses propagation, which matters for ancestors such as drive roots.
        using var handle = CreateFileW(path, MaximumAllowed, FileShare.Read | FileShare.Write | FileShare.Delete,
            0, FileMode.Open, BackupSemantics, 0);
        if (handle.IsInvalid)
        {
            throw new Win32Exception(Marshal.GetLastPInvokeError());
        }

        var descriptor = new RawSecurityDescriptor(security.GetSecurityDescriptorBinaryForm(), 0);
        var acl = descriptor.DiscretionaryAcl ?? throw new InvalidOperationException("The directory must have a DACL.");
        var bytes = new byte[acl.BinaryLength];
        acl.GetBinaryForm(bytes, 0);
        fixed (byte* dacl = bytes)
        {
            var error = SetSecurityInfo(handle, FileObject, DaclSecurityInformation, 0, 0, (nint)dacl, 0);
            if (error != 0)
            {
                throw new Win32Exception((int)error);
            }
        }
    }

    [LibraryImport("kernel32.dll", EntryPoint = "CreateFileW", StringMarshalling = StringMarshalling.Utf16, SetLastError = true)]
    private static partial SafeFileHandle CreateFileW(string fileName, uint desiredAccess, FileShare shareMode,
        nint securityAttributes, FileMode creationDisposition, uint flagsAndAttributes, nint templateFile);

    [LibraryImport("advapi32.dll")]
    private static partial uint SetSecurityInfo(SafeFileHandle handle, int objectType, uint securityInformation,
        nint owner, nint group, nint dacl, nint sacl);
}
