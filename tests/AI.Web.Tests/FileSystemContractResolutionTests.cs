using AI.Contracts.FileSystem;
namespace AI.Web.Tests;

using System.Reflection;
using Shouldly;
using Xunit;

/// <summary>
/// The AC6 check for this executable, which is a negative one on purpose.
/// </summary>
/// <remarks>
/// AI.Web resolves no <see cref="IFileSystem"/>, <see cref="IPath"/> or <see cref="IAtomicFileWriter"/>,
/// and must not: it runs in a browser, where there is no disk to touch and no grants to enforce. Every
/// file-system operation the UI offers travels over HTTP to the host that owns them, through
/// <see cref="AI.Web.FileSystem.IFileSystemApi"/>.
/// <para>
/// This is a test rather than a build assertion because the expectation is negative, and asserting
/// the positive here would be vacuous: the web setup declares no such root, so a root added in the
/// test's own graph would resolve from the test's bindings and could document nothing about what
/// ships — which is exactly how a wiring mistake stayed hidden in the command-line graph. What is
/// proven instead is the substantive claim: no type in the shipped AI.Web assembly takes the contract,
/// so the browser cannot reach the disk behind the grants, while the HTTP route it does use is there.
/// </para>
/// </remarks>
public sealed class FileSystemContractResolutionTests
{
    private static readonly Type[] ContractTypes =
        [typeof(IFileSystem), typeof(IPath), typeof(IAtomicFileWriter)];

    [Fact]
    public void TheWebAssemblyTakesNoFileSystemContractDependency()
    {
        var consumers = WebTypes()
            .SelectMany(type => type.GetConstructors(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance)
                .SelectMany(constructor => constructor.GetParameters())
                .Select(parameter => (Type: type, parameter.ParameterType)))
            .Where(item => ContractTypes.Contains(item.ParameterType))
            .Select(item => item.Type.FullName)
            .Distinct()
            .ToList();

        // If this ever fails, the web application grew a direct file-system dependency and now needs a
        // decision about it; the browser cannot own a disk or a grant.
        consumers.ShouldBeEmpty();
    }

    [Fact]
    public void TheWebAssemblyReachesTheFileSystemOverHttp()
    {
        // The route it really uses, whose answers come from the server's endpoints.
        WebTypes().ShouldContain(type => type == typeof(AI.Web.FileSystem.IFileSystemApi));
    }

    /// <summary>
    /// Types of the assembly AI.Web really ships, tolerating the load failures a Blazor assembly can
    /// report for members this check does not care about.
    /// </summary>
    private static IEnumerable<Type> WebTypes()
    {
        var assembly = typeof(AI.Web.Composition).Assembly;
        try
        {
            return assembly.GetTypes();
        }
        catch (ReflectionTypeLoadException error)
        {
            return error.Types.OfType<Type>();
        }
    }
}
