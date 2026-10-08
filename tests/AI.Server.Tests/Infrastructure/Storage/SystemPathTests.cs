namespace AI.Infrastructure.Tests.Storage;

using AI.Contracts.FileSystem;
using Shouldly;
using Xunit;

/// <summary>
/// The path rules, asserted against the in-memory algebra with each platform's semantics chosen by
/// the test, so a rule is verified rather than the machine the runner happens to be.
/// </summary>
/// <remarks>
/// Two of these tests are the ones the audit names as catching a silent weakening: containment with a
/// case-only difference (§7.1 #4) and the canonicalization that decides whether a path stands alone
/// (§7.1 #5). Containment is a security decision here — it is what a grant is enforced with — so a
/// comparison that is too permissive is not a cosmetic defect.
/// </remarks>
public sealed class SystemPathTests
{
    private static readonly InMemoryPath Windows = new(PathSemantics.Windows, "C:\\data");

    private static readonly InMemoryPath Unix = new(PathSemantics.Unix, "/data");

    [Fact]
    public void ContainmentShouldNotBeAFooledByACommonPrefix()
    {
        // "ab" starts with "a" but is not inside it: a prefix test would accept the sibling.
        Windows.IsInside("C:\\data\\ab", "C:\\data\\a", recursive: true).ShouldBeFalse();
        Windows.IsInside("C:\\data\\a\\b", "C:\\data\\a", recursive: true).ShouldBeTrue();
        Windows.IsInside("C:\\data", "C:\\data", recursive: true).ShouldBeTrue();
    }

    [Fact]
    public void ContainmentShouldFollowThePlatformsComparison()
    {
        // Windows paths are not case-sensitive, so a differently-cased spelling names the same place.
        Windows.IsInside("c:\\DATA\\chat.json", "C:\\data", recursive: true).ShouldBeTrue();
        // Elsewhere they are different paths, and saying otherwise would widen a grant.
        Unix.IsInside("/data/Chat.json", "/data/chat.json", recursive: true).ShouldBeFalse();
        Unix.IsInside("/data/chat.json", "/data/chat.json", recursive: true).ShouldBeTrue();
    }

    [Fact]
    public void ContainmentShouldRespectTheRecursiveFlagAndRefuseToClimbOut()
    {
        Windows.IsInside("C:\\data\\a\\b", "C:\\data", recursive: false).ShouldBeFalse();
        Windows.IsInside("C:\\data\\a", "C:\\data", recursive: false).ShouldBeTrue();
        // A path that climbs out is outside, whatever it looked like before canonicalization.
        Windows.IsInside("C:\\data\\..\\windows\\system32", "C:\\data", recursive: true).ShouldBeFalse();
        Unix.IsInside("/data/../../etc/passwd", "/data", recursive: true).ShouldBeFalse();
        Windows.IsInside("C:\\data\\a\\..\\b", "C:\\data", recursive: true).ShouldBeTrue();
        // Nothing is inside nothing.
        Windows.IsInside("C:\\data", "  ", recursive: true).ShouldBeFalse();
    }

    [Fact]
    public void CanonicalizingShouldFoldDotsAndResolveAgainstTheGivenBase()
    {
        Windows.GetFullPath("a\\..\\b\\c.txt").ShouldBe("C:\\data\\b\\c.txt");
        Unix.GetFullPath("a/./b").ShouldBe("/data/a/b");
        // The base overload resolves against what it is given, which is how a path from a stored
        // document is pinned down without depending on the process working directory (§7.1 #5).
        Unix.GetFullPath("chat.json", "/other").ShouldBe("/other/chat.json");
        Unix.GetFullPath("/absolute/chat.json", "/other").ShouldBe("/absolute/chat.json");
        // Climbing above the root stops at the root rather than inventing a level.
        Unix.GetFullPath("../../etc").ShouldBe("/etc");
    }

    [Fact]
    public void ASinglePathShouldBeJudgedByWhetherItStandsAlone()
    {
        Windows.IsFullyQualified("C:\\data\\chat.json").ShouldBeTrue();
        Windows.IsFullyQualified("\\data").ShouldBeFalse();
        Windows.IsFullyQualified("data").ShouldBeFalse();
        Windows.IsRooted("\\data").ShouldBeTrue();
        Unix.IsFullyQualified("/data/chat.json").ShouldBeTrue();
        Unix.IsFullyQualified("data/chat.json").ShouldBeFalse();
        Unix.GetPathRoot("/data/chat.json").ShouldBe("/");
        Windows.GetPathRoot("C:\\data\\chat.json").ShouldBe("C:\\");
    }

    [Fact]
    public void TrimmingShouldKeepARootARoot()
    {
        // "C:" is a relative path and "C:\" is a root, so trimming must not turn one into the other.
        Windows.TrimEndingDirectorySeparator("C:\\data\\").ShouldBe("C:\\data");
        Windows.TrimEndingDirectorySeparator("C:\\").ShouldBe("C:\\");
        Unix.TrimEndingDirectorySeparator("/data/").ShouldBe("/data");
        Unix.TrimEndingDirectorySeparator("/").ShouldBe("/");
        Windows.EndsInDirectorySeparator("C:\\data\\").ShouldBeTrue();
        Windows.EndsInDirectorySeparator("C:\\data").ShouldBeFalse();
    }

    [Fact]
    public void NamesShouldBeSplitTheWayCallersExpect()
    {
        Windows.GetFileName("C:\\data\\chat.json").ShouldBe("chat.json");
        Windows.GetExtension("C:\\data\\chat.json").ShouldBe(".json");
        // A leading dot is a name, not an extension: ".gitignore" is not an ignore file of type "gitignore".
        Unix.GetExtension("/data/.gitignore").ShouldBeEmpty();
        Unix.GetFileNameWithoutExtension("/data/chat.json").ShouldBe("chat");
        Windows.GetDirectoryName("C:\\data\\chat.json").ShouldBe("C:\\data");
    }

    [Fact]
    public void RelativePathsShouldTravelOutOfTheBaseWhenTheyHaveTo()
    {
        Windows.GetRelativePath("C:\\data", "C:\\data\\a\\b.txt").ShouldBe("a\\b.txt");
        Windows.GetRelativePath("C:\\data\\a", "C:\\data\\b\\c.txt").ShouldBe("..\\b\\c.txt");
        Unix.GetRelativePath("/data", "/data").ShouldBeEmpty();
    }

    [Fact]
    public void CombiningShouldIgnoreAnEmptySideAndAHostileSecondOne()
    {
        Windows.Combine("C:\\data", "chat.json").ShouldBe("C:\\data\\chat.json");
        Windows.Combine("C:\\data", "").ShouldBe("C:\\data");
        Windows.Combine("", "chat.json").ShouldBe("chat.json");
        // A second path that is fully qualified is not appended: the platform treats it as the answer.
        Windows.Combine("C:\\data", "D:\\other").ShouldBe("D:\\other");
    }

    [Fact]
    public void TheAdapterShouldAgreeWithThePlatformItRunsOn()
    {
        var adapter = new SystemPath();
        adapter.DirectorySeparator.ShouldBe(Path.DirectorySeparatorChar);
        adapter.IsCaseSensitive.ShouldBe(!OperatingSystem.IsWindows());
        adapter.IsInside(Path.Combine(Path.GetTempPath(), "a", "b"), Path.GetTempPath(), recursive: true)
            .ShouldBeTrue();
        // The platform adapter canonicalizes through the platform, so the two agree on what a path is.
        adapter.GetFullPath("a/../b").ShouldBe(Path.GetFullPath("b"));
    }
}
