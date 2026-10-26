namespace AI.Sandbox.Tests;

using System.Security.AccessControl;
using System.Security.Principal;
using Shouldly;
using Xunit;

public sealed class WindowsDirectorySecurityTests
{
    [Fact]
    public void UpdatingAnAncestorDoesNotReapplyItsInheritedAclToChildren()
    {
        if (!OperatingSystem.IsWindows()) return;
        var root = Directory.CreateTempSubdirectory("ai-sandbox-acl-");
        try
        {
            // A synthetic SID makes inheritance observable without granting an actual user access.
            var sid = new SecurityIdentifier("S-1-5-21-123456789-123456789-123456789-12345");
            var security = root.GetAccessControl(AccessControlSections.Access);
            security.AddAccessRule(new FileSystemAccessRule(sid, FileSystemRights.ReadAttributes,
                InheritanceFlags.ContainerInherit | InheritanceFlags.ObjectInherit, PropagationFlags.None, AccessControlType.Allow));
            root.SetAccessControl(security);
            var child = root.CreateSubdirectory("child");
            var before = child.GetAccessControl(AccessControlSections.Access);
            var inherited = false;
            foreach (FileSystemAccessRule rule in before.GetAccessRules(true, true, typeof(SecurityIdentifier)))
                inherited |= rule.IdentityReference.Equals(sid) && rule.IsInherited;
            inherited.ShouldBeTrue();
            var bytes = before.GetSecurityDescriptorBinaryForm();

            security = root.GetAccessControl(AccessControlSections.Access);
            security.PurgeAccessRules(sid);
            WindowsDirectorySecurity.SetAccessControl(root.FullName, security);

            foreach (FileSystemAccessRule rule in root.GetAccessControl(AccessControlSections.Access).GetAccessRules(true, true, typeof(SecurityIdentifier)))
                rule.IdentityReference.Equals(sid).ShouldBeFalse();
            child.GetAccessControl(AccessControlSections.Access).GetSecurityDescriptorBinaryForm().ShouldBe(bytes);
        }
        finally
        {
            root.Delete(recursive: true);
        }
    }
}
