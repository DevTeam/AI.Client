namespace AI.Web.Tests.Skills;

using System.IO.Compression;
using System.Text;
using AI.Web.Skills;
using Shouldly;
using Xunit;

public class SkillTransferCodecTests
{
    private const string Stored = """
        ---
        revision: 3
        enabled: true
        id: release-notes
        name: Release notes
        description: Write release notes from merged changes.
        kind: playbook
        parameters: {"type":"object","properties":{},"additionalProperties":false}
        ---

        Read the merged changes and write the notes.
        """;

    private readonly SkillTransferCodec _codec = new();

    [Fact]
    public void ShouldExportOneSkillAsItsSkillMdWithoutTheHostsBookkeeping()
    {
        var skill = _codec.ToTransfer(Stored, true);

        var text = _codec.ExportText([skill]);

        text.ShouldStartWith("---\nid: release-notes\n");
        text.ShouldNotContain("revision:");
        text.ShouldNotContain("enabled:");
        text.ShouldEndWith("write the notes.");
        skill.Id.ShouldBe("release-notes");
        skill.Name.ShouldBe("Release notes");
    }

    [Fact]
    public void ShouldCarryASwitchedOffSkillThroughCopyAndPaste()
    {
        var text = _codec.ExportText([_codec.ToTransfer(Stored, false)]);

        text.ShouldContain("enabled: false");
        var imported = _codec.Parse(text).Skills.Single().Settings;
        imported.Enabled.ShouldBeFalse();
        imported.Content.ShouldNotContain("enabled:");
        imported.Content.ShouldBe(_codec.ToTransfer(Stored, false).Content);
    }

    [Fact]
    public void ShouldRoundTripSeveralSkillsAsJson()
    {
        var first = _codec.ToTransfer(Stored, true);
        var second = _codec.WithIdentity(first, "release-notes-2", "Release notes (2)");

        var parsed = _codec.Parse(_codec.ExportText([first, second]));

        parsed.Error.ShouldBeNull();
        parsed.Skills.Select(item => item.Settings.Id).ShouldBe(["release-notes", "release-notes-2"]);
        parsed.Skills[1].Settings.Name.ShouldBe("Release notes (2)");
        parsed.Skills[0].Notes.ShouldBeEmpty();
    }

    [Fact]
    public void ShouldRoundTripSeveralSkillsAsAZipOfSkillFolders()
    {
        var first = _codec.ToTransfer(Stored, true);
        var second = _codec.WithIdentity(first, "changelog", "Changelog");

        var archive = _codec.ExportArchive([first, second]);

        using (var zip = new ZipArchive(new MemoryStream(archive)))
        {
            zip.Entries.Select(entry => entry.FullName).ShouldBe(["release-notes/SKILL.md", "changelog/SKILL.md"]);
        }

        var parsed = _codec.ParseArchive(archive);
        parsed.Error.ShouldBeNull();
        parsed.Skills.Select(item => item.Settings.Content).ShouldBe([second.Content, first.Content], ignoreOrder: true);
    }

    [Fact]
    public void ShouldAdaptAnAgentSkillWrittenForAnotherClient()
    {
        const string foreign = """
            ---
            name: PDF tools
            description: >
              Extract text and tables from PDF files,
              fill forms and merge documents.
            license: Apache-2.0
            allowed-tools: Read, Bash
            ---

            # PDF tools

            Use pypdf to read the file.
            """;

        var parsed = _codec.Parse(foreign);

        var item = parsed.Skills.Single();
        item.Settings.Id.ShouldBe("pdf-tools");
        item.Settings.Name.ShouldBe("PDF tools");
        item.Settings.Description.ShouldBe("Extract text and tables from PDF files, fill forms and merge documents.");
        item.Settings.Content.ShouldStartWith("---\nid: pdf-tools\nname: PDF tools\ndescription: Extract text");
        item.Settings.Content.ShouldContain("kind: playbook\nparameters: {\"type\":\"object\"");
        item.Settings.Content.ShouldContain("license: Apache-2.0");
        item.Settings.Content.ShouldEndWith("Use pypdf to read the file.");
        item.Notes.ShouldContain(note => note.Contains("id, kind, parameters added", StringComparison.Ordinal));
        item.Notes.ShouldContain(note => note.Contains("allowed-tools", StringComparison.Ordinal));
    }

    [Fact]
    public void ShouldUnquoteAQuotedDescription()
    {
        var parsed = _codec.Parse("---\nname: \"git-helper\"\ndescription: 'Helps with ''git'' history.'\n---\nBody");

        var skill = parsed.Skills.Single().Settings;
        skill.Id.ShouldBe("git-helper");
        skill.Description.ShouldBe("Helps with 'git' history.");
    }

    [Theory]
    [InlineData("---\nid: x\nname: X\n---\nBody", "It has no description")]
    [InlineData("---\nid: Bad_Id\nname: X\ndescription: d\nparameters: {}\n---\nBody", "Its id must be")]
    [InlineData("---\nid: run\nname: Run\ndescription: d\nkind: executor\nparameters: {}\n---\nBody", "Executor skills")]
    public void ShouldSkipWhatTheHostWouldRefuse(string text, string reason)
    {
        var parsed = _codec.Parse(text);

        parsed.Error.ShouldBeNull();
        parsed.Skills.ShouldBeEmpty();
        parsed.Skipped.Single().Reason.ShouldStartWith(reason);
    }

    [Theory]
    [InlineData("", "Nothing to import: the text is empty.")]
    [InlineData("just some words", "Nothing to import: expected a SKILL.md")]
    [InlineData("{ \"skills\": [", "This is not valid JSON")]
    public void ShouldExplainTextThatHoldsNoSkills(string text, string error) =>
        _codec.Parse(text).Error.ShouldNotBeNull().ShouldStartWith(error);

    [Fact]
    public void ShouldReadSkillsFromAnArchiveAndNameTheFilesLeftBehind()
    {
        using var stream = new MemoryStream();
        using (var zip = new ZipArchive(stream, ZipArchiveMode.Create, true))
        {
            Add(zip, "skills/pdf/SKILL.md", "---\nname: pdf\ndescription: Read PDF files.\n---\nUse the script.");
            Add(zip, "skills/pdf/scripts/extract.py", "print()");
            Add(zip, "skills/pdf/reference.md", "# Reference");
            Add(zip, "__MACOSX/skills/pdf/._SKILL.md", "junk");
            Add(zip, "skills/docx/SKILL.md", "---\nname: docx\ndescription: Edit Word files.\n---\nBody");
        }

        var parsed = _codec.ParseArchive(stream.ToArray());

        parsed.Skills.Select(item => item.Settings.Id).ShouldBe(["docx", "pdf"]);
        parsed.Skills.Single(item => item.Settings.Id == "pdf").Notes.ShouldContain("2 other files in its folder were not imported: only the SKILL.md is kept.");
        parsed.Skills.Single(item => item.Settings.Id == "docx").Notes.ShouldNotContain(note => note.Contains("other file", StringComparison.Ordinal));
    }

    [Fact]
    public void ShouldRejectAFileThatIsNotAZip() =>
        _codec.ParseArchive(Encoding.UTF8.GetBytes("not a zip")).Error.ShouldBe("This is not a valid .zip file.");

    private static void Add(ZipArchive zip, string path, string text)
    {
        using var writer = new StreamWriter(zip.CreateEntry(path).Open());
        writer.Write(text);
    }
}
