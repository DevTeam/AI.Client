namespace AI.TextCorrection.Tests;

using Shouldly;
using Xunit;
using System.Text;

public sealed class TextCorrectionPreparationTests
{
    [Fact]
    [Trait("Category", "Slow")]
    public async Task PreparesOnlySelectedLanguagesAndSharesTheAnalyzerCache()
    {
        var composition = new TextCorrectionComposition();
        composition.Preparation.IsReady(["en"]).ShouldBeFalse();
        await composition.Preparation.PrepareAsync(["en"]);
        composition.Preparation.IsReady(["en"]).ShouldBeTrue();
        composition.Preparation.IsReady(["ru"]).ShouldBeFalse();
        await composition.Preparation.PrepareAsync(["en", "ru"]);
        composition.Preparation.IsReady(["en", "ru"]).ShouldBeTrue();
        composition.Analyzer.Analyze("ghbdtn", ["en", "ru"]).Single().Text.ShouldBe("привет");
    }

    [Fact]
    public async Task AlreadyPreparedLanguagesAreNotPreparedAgain()
    {
        var lexicon = new CountingLexiconPreparation();
        var model = new CountingModelPreparation();
        var preparation = new TextCorrectionPreparation(new KeyboardLayouts(), lexicon, model);
        await preparation.PrepareAsync(["en"]);
        await Task.WhenAll(preparation.PrepareAsync(["en"]), preparation.PrepareAsync(["en"]));
        lexicon.Count.ShouldBe(1);
        model.Count.ShouldBe(0);
    }

    [Fact]
    public async Task ASecondLanguageLoadsLayoutModelsAfterSpellingOnlyPreparation()
    {
        var lexicon = new CountingLexiconPreparation();
        var model = new CountingModelPreparation();
        var preparation = new TextCorrectionPreparation(new KeyboardLayouts(), lexicon, model);
        await preparation.PrepareAsync(["en"]);
        model.Count.ShouldBe(0);
        preparation.IsReady(["en"]).ShouldBeTrue();
        preparation.IsReady(["en", "ru"]).ShouldBeFalse();
        await preparation.PrepareAsync(["en", "ru"]);
        model.Count.ShouldBe(2);
        preparation.IsReady(["en", "ru"]).ShouldBeTrue();
        await preparation.PrepareAsync(["en"]);
        model.Count.ShouldBe(2);
    }

    [Fact]
    public async Task FailedPreparationNeverMarksTheLanguageReady()
    {
        var preparation = new TextCorrectionPreparation(new KeyboardLayouts(), new FailingLexiconPreparation(), new CountingModelPreparation());
        await Should.ThrowAsync<InvalidOperationException>(() => preparation.PrepareAsync(["en"]));
        preparation.IsReady(["en"]).ShouldBeFalse();
    }

    [Fact]
    public async Task PreparationReturnsBeforeItStartsHeavyWork()
    {
        var lexicon = new GatedLexiconPreparation();
        var preparation = new TextCorrectionPreparation(new KeyboardLayouts(), lexicon, new CountingModelPreparation());
        var work = preparation.PrepareAsync(["en"]);
        await lexicon.Started.Task;
        work.IsCompleted.ShouldBeFalse();
        preparation.IsReady(["en"]).ShouldBeFalse();
        lexicon.Release.SetResult();
        await work;
        preparation.IsReady(["en"]).ShouldBeTrue();
    }

    [Fact]
    public async Task ConcurrentPreparationAndLaterQueriesReuseLoadedResources()
    {
        var source = new CountingResources();
        var lexicon = new HunspellWordLexicon(new WordLexicon(), source);
        var model = new DictionaryWordPlausibility(source);
        await Task.WhenAll(lexicon.PrepareAsync("custom"), lexicon.PrepareAsync("custom"),
            model.PrepareAsync("custom"), model.PrepareAsync("custom"));
        source.WordReads.ShouldBe(2); // One Hunspell load and one model load.
        source.AffixReads.ShouldBe(1);
        lexicon.Contains("custom", "hello").ShouldBeTrue();
        model.Score("custom", "hello").ShouldBe(1);
        source.WordReads.ShouldBe(2);
        source.AffixReads.ShouldBe(1);
    }

    private sealed class CountingLexiconPreparation : IWordLexiconPreparation
    {
        public int Count { get; private set; }
        public Task PrepareAsync(string languageId) { Count++; return Task.CompletedTask; }
    }

    private sealed class CountingModelPreparation : IWordPlausibilityPreparation
    {
        public int Count { get; private set; }
        public Task PrepareAsync(string languageId) { Count++; return Task.CompletedTask; }
    }

    private sealed class FailingLexiconPreparation : IWordLexiconPreparation
    {
        public Task PrepareAsync(string languageId) => Task.FromException(new InvalidOperationException("Dictionary unavailable."));
    }

    private sealed class GatedLexiconPreparation : IWordLexiconPreparation
    {
        public TaskCompletionSource Started { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public Task PrepareAsync(string languageId) { Started.SetResult(); return Release.Task; }
    }

    private sealed class CountingResources : ITextDictionaries, ITextDictionaryResource
    {
        private int _wordReads;
        private int _affixReads;
        public int WordReads => _wordReads;
        public int AffixReads => _affixReads;
        public string LanguageId => "custom";
        public IReadOnlyList<ITextDictionaryResource> All => [this];
        public Stream OpenWords()
        {
            Interlocked.Increment(ref _wordReads);
            return new MemoryStream(Encoding.UTF8.GetBytes("2\nhello\nworld\n"));
        }
        public Stream OpenAffixes()
        {
            Interlocked.Increment(ref _affixReads);
            return new MemoryStream(Encoding.UTF8.GetBytes("SET UTF-8\n"));
        }
    }
}
