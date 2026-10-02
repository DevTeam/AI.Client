namespace AI.Web.Tests;

using System.Reflection;
using AI.TextCorrection;
using AI.Web.Pages;
using AI.Web.Settings;
using Moq;
using Shouldly;
using Xunit;

public sealed class HomeTextCorrectionTests
{
    private readonly PropertyInfo[] _properties = typeof(Home)
        .GetProperties(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
    [Fact]
    public async Task FirstWordWaitsForPreparationInsteadOfLosingItsCorrection()
    {
        var loaded = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var preparation = new Mock<ITextCorrectionPreparation>();
        preparation.Setup(value => value.PrepareAsync(It.IsAny<IReadOnlyCollection<string>>())).Returns(loaded.Task);
        var analyzer = new Mock<ITextAutoCorrectionAnalyzer>();
        analyzer.Setup(value => value.Analyze("ghbdtn", It.IsAny<IReadOnlyCollection<string>>(), It.IsAny<IReadOnlyList<TextRange>>()))
            .Returns([new TextReplacement(0, 6, "привет", "ru", 0.98)]);
        var page = CreatePage(["en", "ru"], preparation.Object, analyzer.Object);

        var pending = page.AnalyzeComposerLayout("ghbdtn");
        pending.IsCompleted.ShouldBeFalse();
        analyzer.Verify(value => value.Analyze(It.IsAny<string>(), It.IsAny<IReadOnlyCollection<string>>(), It.IsAny<IReadOnlyList<TextRange>>()), Times.Never);
        loaded.SetResult();
        (await pending).Single().Text.ShouldBe("привет");
    }

    [Fact]
    public async Task WithoutSelectedLanguagesCorrectionDoesNotWaitForDictionaries()
    {
        var preparation = new Mock<ITextCorrectionPreparation>(MockBehavior.Strict);
        var analyzer = new Mock<ITextAutoCorrectionAnalyzer>(MockBehavior.Strict);
        var page = CreatePage([], preparation.Object, analyzer.Object);
        (await page.AnalyzeComposerLayout("ghbdtn")).ShouldBeEmpty();
    }

    [Fact]
    public async Task OneLanguagePreparesItsDictionaryAndPassesPasteExclusionsToTheAnalyzer()
    {
        var preparation = new Mock<ITextCorrectionPreparation>();
        preparation.Setup(value => value.PrepareAsync(It.IsAny<IReadOnlyCollection<string>>())).Returns(Task.CompletedTask);
        var analyzer = new Mock<ITextAutoCorrectionAnalyzer>();
        TextRange[] excluded = [new(0, 6)];
        analyzer.Setup(value => value.Analyze("helllo dictioanry", It.IsAny<IReadOnlyCollection<string>>(), excluded))
            .Returns([new TextReplacement(7, 9, "dictionary", "en", 0.95)]);
        var page = CreatePage(["en"], preparation.Object, analyzer.Object);
        (await page.AnalyzeComposerLayout("helllo dictioanry", excluded)).Single().Text.ShouldBe("dictionary");
        preparation.Verify(value => value.PrepareAsync(It.IsAny<IReadOnlyCollection<string>>()), Times.Once);
    }

    [Fact]
    public async Task DeselectingALanguageWhileLoadingPreventsAnOutdatedCorrection()
    {
        var loaded = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var preparation = new Mock<ITextCorrectionPreparation>();
        preparation.Setup(value => value.PrepareAsync(It.IsAny<IReadOnlyCollection<string>>())).Returns(loaded.Task);
        var analyzer = new Mock<ITextAutoCorrectionAnalyzer>(MockBehavior.Strict);
        var languages = new Mock<ITextCorrectionLanguages>();
        languages.SetupSequence(value => value.GetLayoutIdsAsync())
            .Returns(new ValueTask<IReadOnlyCollection<string>>(["en", "ru"]))
            .Returns(new ValueTask<IReadOnlyCollection<string>>(["en"]));
        var page = CreatePage(["en", "ru"], preparation.Object, analyzer.Object);
        Inject(page, "CorrectionLanguages", languages.Object);
        var pending = page.AnalyzeComposerLayout("ghbdtn");
        loaded.SetResult();
        (await pending).ShouldBeEmpty();
    }

    private Home CreatePage(IReadOnlyCollection<string> layoutIds, ITextCorrectionPreparation preparation, ITextAutoCorrectionAnalyzer analyzer)
    {
        var layouts = new Mock<ITextCorrectionLanguages>();
        layouts.Setup(value => value.GetLayoutIdsAsync()).Returns(new ValueTask<IReadOnlyCollection<string>>(layoutIds));
        var page = new Home();
        Inject(page, "CorrectionLanguages", layouts.Object);
        Inject(page, "TextCorrectionPreparation", preparation);
        Inject(page, "TextCorrection", analyzer);
        return page;
    }

    private void Inject(Home page, string property, object value) =>
        _properties.Single(info => info.Name == property).SetValue(page, value);
}
