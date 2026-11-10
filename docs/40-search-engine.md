# Recursive search (`AI.Search`)

Status: Accepted

`src/AI.Search` is a recursive text-search library modeled on
[ripgrep](https://github.com/burntsushi/ripgrep). It is a library, not a command-line tool: callers
take `ISearchEngine` from the composition and hand it a `SearchRequest`. The benchmark project in
`benchmarks/AI.Search.Benchmarks` measures it against the real `rg` binary, so the comparison is with
ripgrep itself rather than with a re-implementation of its behavior.

## Why it exists

The application already searches files through ripgrep processes. A first-class engine removes the
process boundary, keeps results in typed form — match counts, line and column, per-file totals — and
runs over the same `IFileSystem` and `IPath` contracts as the rest of the product
(see [ADR-013](decisions/ADR-013-file-system-contract.md)), which is what makes it testable against an
in-memory tree.

## Shape of the engine

The architecture follows ripgrep's split:

1. **Walk.** `Walking/FileWalker` enumerates the tree one directory at a time, honouring the ignore
   rules it has accumulated and the include/exclude globs, and produces a `SearchPlan`.
2. **Scan.** A bounded channel carries planned files to `Scanning/FileScanner` workers. Each worker
   reads one file, decides whether it is binary, splits it into lines and asks a line matcher for the
   first match on each line.
3. **Report.** `Scanning/SearchEngine` merges what the workers found into one `SearchResult`, sorted
   by path so two runs with different parallelism report the same thing.

Literal patterns use `IndexOf` with the comparison the case mode asks for. Regular expressions run
in the non-backtracking engine — compiled, like the fallback — when the pattern can express it,
and fall back to a compiled, timed backtracking engine otherwise, so a pathological pattern cannot
hang the search. Before a line reaches the expression, `Matchers/RequiredLiteral` extracts the
longest run of literal characters every match must hold and `Matchers/ContentScanner` looks for
those bytes first, which is ripgrep's prefilter: a line that cannot match costs a byte comparison
instead of a call into a compiled automaton. A prefilter is only promised when it is sound — the
pattern has no group, alternation or case-insensitive matching, and the run is at least two
characters long — so it never hides a match.

## Defaults

The defaults are ripgrep's defaults, because that is what callers expect and what the benchmark
comparison assumes:

| Concern | Default | ripgrep equivalent |
| --- | --- | --- |
| Pattern | literal text | a plain argument |
| Case | smart case | `-S` |
| Ignore files | `.gitignore`, `.ignore`, `.rgignore` respected | the default |
| Hidden entries | skipped | the default |
| Binary files | skipped, counted as skipped | the default |
| Largest file | 16 MiB | the default |
| Matches | no cap, reported per line | the default |
| Workers | one per processor | the default |

`SearchRequest` exposes the switches that matter for a caller: `Case`, `WholeWord`, `Include`,
`Exclude`, `RespectIgnoreFiles`, `IncludeHidden`, `SkipBinary`, `MaxFileBytes`, `MaxMatches`,
`MaxMatchesPerFile`, `Output`, `Parallelism` and `RegexTimeout`.

## Ignore rules

`Ignoring/IgnoreRules` parses the Git ignore syntax — comments, blank lines, negation with `!`,
directory-only rules with a trailing `/`, anchored rules, and `**`, `*` and `?` — and applies the last
matching rule, as Git does. `Ignoring/IgnoreRuleProvider` reads the repository's `.gitignore` and
`.git/info/exclude` from the repository root down to the searched directory, then directory rules
while walking. A matching directory is not descended into, which is where much of the speed of an
ignore-aware search comes from.

## Composition

`SearchComposition` is the standalone root the tests and benchmarks start from; the shared
registrations sit in `Configuration.Composition`, so an application composition can link them
without the standalone root. Binding and lifetime rules follow
[Dependency injection with Pure.DI](30-dependency-injection.md): interface dependencies, transient
services, and a root only for what a consumer resolves.

```csharp
var engine = new SearchComposition().Engine;
var result = await engine.SearchAsync(new SearchRequest
{
    Path = root,
    Pattern = "needle",
    Include = ["*.cs"],
    Exclude = ["bin", "obj"]
});
```

## Benchmarks

`benchmarks/AI.Search.Benchmarks/README.md` documents both ways to measure it: the `compare` command,
which times `AI.Search` and `rg` on the same generated corpus and prints how many times faster `rg`
is, and the in-process `EngineBenchmarks`, which reports timings and managed allocations through
BenchmarkDotNet.

## Verification

`tests/AI.Search.Tests` covers the behavior callers rely on: matches, columns in characters, smart
case, whole words, regex, include/exclude globs, Git ignore files including nested and negated rules,
hidden and binary files, the size and match caps, CRLF input, long lines and the equivalence of
sequential and parallel runs.