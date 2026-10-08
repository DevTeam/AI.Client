namespace AI.Contracts.FileSystem;

/// <summary>
/// How a listing walks a directory. Every default reproduces what the product did before this
/// contract existed — a plain recursive-or-not walk that skips nothing — so a caller that wants
/// entries filtered or links left alone has to say so.
/// </summary>
/// <param name="Recursive">
/// Whether to walk below the directory as well. A caller listing one level asks for one level,
/// because a search that quietly descends is a search that quietly finds too much.
/// </param>
/// <param name="SearchPattern">
/// Which names a listing <em>reports</em>, in the platform's wildcard form. <c>*</c> is every name.
/// It never decides what the walk descends into: with <paramref name="Recursive"/> on, a directory
/// is entered whether or not its own name matches, so <c>*.md</c> finds a document inside a
/// subdirectory that is not itself called <c>*.md</c>.
/// </param>
/// <param name="SkipInaccessible">
/// Whether an entry the account may not read is passed over rather than stopping the whole walk.
/// True by default: one unreadable directory in a tree should not cost the caller every other
/// entry, which is the platform's own enumeration default as well.
/// </param>
/// <param name="AttributesToSkip">
/// Entries carrying any of these attributes are left out. Empty by default, so nothing is hidden
/// from the caller unless it asked. This is how a caller excludes hidden or temporary entries.
/// </param>
/// <param name="SkipReparsePoints">
/// Whether links are left out of the walk. False by default: a link is still an entry the caller
/// would expect to see, and it is never <em>followed</em> by a listing anyway — following one leads
/// back up the tree and turns a listing into an endless walk. A caller that wants links gone says
/// so here.
/// </param>
public sealed record FileEnumerationOptions(
    bool Recursive = false,
    string SearchPattern = "*",
    bool SkipInaccessible = true,
    FileAttributes AttributesToSkip = 0,
    bool SkipReparsePoints = false)
{
    /// <summary>One level, every name, nothing filtered: what a listing without options does.</summary>
    public static FileEnumerationOptions Default { get; } = new();

    /// <summary>Every level, every name, nothing filtered: what a recursive listing without options does.</summary>
    public static FileEnumerationOptions RecursiveEverything { get; } = new(Recursive: true);
}
