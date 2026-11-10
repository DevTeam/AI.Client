namespace AI.Search.Tests;

using AI.Contracts.FileSystem;
using Xunit;

/// <summary>
/// A small tree on disk, built for one test and removed afterwards. The engine walks real
/// directories because the platform's own semantics — separators, case sensitivity, the order a
/// listing comes back in — are part of what a search has to get right.
/// </summary>
internal sealed class SearchCorpus : IDisposable
{
    public SearchCorpus()
    {
        Root = Path.Combine(Path.GetTempPath(), "AI.Search.Tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Root);
    }

    /// <summary>The absolute root of the tree.</summary>
    public string Root { get; }

    /// <summary>Writes a file, creating the directories it needs.</summary>
    public SearchCorpus File(string relativePath, string content)
    {
        var path = Path.Combine(Root, relativePath);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        System.IO.File.WriteAllText(path, content);
        return this;
    }

    /// <summary>Writes a file with exactly the bytes given, for the binary and encoding cases.</summary>
    public SearchCorpus Bytes(string relativePath, byte[] content)
    {
        var path = Path.Combine(Root, relativePath);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        System.IO.File.WriteAllBytes(path, content);
        return this;
    }

    public SearchCorpus Folder(string relativePath)
    {
        System.IO.Directory.CreateDirectory(Path.Combine(Root, relativePath));
        return this;
    }

    /// <summary>The engine under test, built through the production composition.</summary>
    public static ISearchEngine Engine() => new SearchComposition().Engine;

    /// <summary>
    /// Runs a request through a fresh engine with the test's own cancellation token, which keeps a
    /// hung search from holding the whole run.
    /// </summary>
    public static Task<SearchResult> RunAsync(SearchRequest request) =>
        Engine().SearchAsync(request, TestContext.Current.CancellationToken);

    /// <summary>A request rooted at this corpus.</summary>
    public SearchRequest Request(string pattern) => new() { Path = Root, Pattern = pattern };

    public void Dispose()
    {
        try
        {
            if (System.IO.Directory.Exists(Root))
            {
                System.IO.Directory.Delete(Root, recursive: true);
            }
        }
        catch (IOException)
        {
            // A leftover temporary directory is not a test failure.
        }
    }
}