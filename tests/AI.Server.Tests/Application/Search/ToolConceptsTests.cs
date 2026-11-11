namespace AI.Application.Tests.Search;

using System.Globalization;
using System.Resources;
using System.Text.Json;
using AI.Application.Search;
using AI.Application.Tools;
using AI.Contracts.Tools;
using Shouldly;
using Xunit;

public sealed class ToolConceptsTests
{
    private readonly ToolConcepts _concepts = new();

    [Theory]
    [InlineData("какие файлы у менят есть", "File List")]
    [InlineData("в мой директории", "Directory")]
    [InlineData("Show the files in my folder", "File Directory List")]
    [InlineData("Welche Dateien sind im Ordner?", "File Directory List")]
    [InlineData("Muestra los archivos de la carpeta", "File Directory List")]
    [InlineData("Quels fichiers sont dans le dossier ?", "File Directory List")]
    [InlineData("文件夹里有哪些文件", "File Directory List")]
    [InlineData("フォルダにどんなファイルがありますか", "File Directory List")]
    [InlineData("폴더에 어떤 파일이 있어요", "File Directory List")]
    [InlineData("ما هي الملفات في المجلد", "File Directory List")]
    [InlineData("מה יש בתיקייה", "Directory List")]
    [InlineData("मेरे फ़ोल्डर में कौन सी फ़ाइलें हैं", "File Directory List")]
    [InlineData("আমার ফোল্ডারে কোন ফাইল আছে", "File Directory List")]
    [InlineData("มีไฟล์อะไรบ้างในโฟลเดอร์", "File Directory List")]
    [InlineData("Thư mục của tôi có những gì", "Directory List")]
    [InlineData("Ada file apa saja di folder saya", "File Directory List")]
    [InlineData("Quali file ci sono nella cartella", "File Directory List")]
    [InlineData("Quais arquivos tem na pasta", "File Directory List")]
    public void ShouldFindConceptsInEverySupportedLanguage(string text, string expected) =>
        _concepts.Find(text).ShouldBe(expected.Split(' '), ignoreOrder: true);

    [Fact]
    public void ShouldMatchAStemOnlyAtTheStartOfAWord()
    {
        _concepts.Find("удалить файлы").ShouldContain("Delete");
        _concepts.Find("The thread is blocked").ShouldNotContain("Read");
        _concepts.Find("Привет!").ShouldBeEmpty();
        _concepts.Find("Hello!").ShouldBeEmpty();
    }

    [Fact]
    public void ShouldKeepTheSameConceptsInEveryLanguage()
    {
        var resources = new ResourceManager(typeof(ToolConcepts).FullName!, typeof(ToolConcepts).Assembly);
        var neutral = Keys(resources, CultureInfo.InvariantCulture);
        neutral.ShouldNotBeEmpty();
        foreach (var language in ToolConcepts.Languages)
            Keys(resources, CultureInfo.GetCultureInfo(language)).ShouldBe(neutral, ignoreOrder: true, customMessage: language);
    }

    [Fact]
    public void ShouldGiveConceptsOnlyToBuiltInAndApplicationTools()
    {
        _concepts.ServedBy(Tool(ToolRef.BuiltInPrefix + "list_directory", "list_directory")).ShouldContain("Directory");
        _concepts.ServedBy(Tool(ToolRef.AppPrefix + "app_theme", "app_theme")).ShouldBe(["Theme"]);
        _concepts.ServedBy(Tool("mcp_github__list_directory", "list_directory")).ShouldBeEmpty();
    }

    private static string[] Keys(ResourceManager resources, CultureInfo culture) =>
        resources.GetResourceSet(culture, true, false)!.Cast<System.Collections.DictionaryEntry>()
            .Select(entry => (string)entry.Key).ToArray();

    internal static AgentTool Tool(string name, string original, string description = "Operation")
    {
        var schema = JsonElement.Parse("{}");
        return new(new(name, description, schema), ToolDescriptor.Basic(name, original, description, schema),
            Guid.NewGuid(), original, original);
    }
}
