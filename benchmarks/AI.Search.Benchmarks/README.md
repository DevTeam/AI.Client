# AI.Search benchmarks

Two ways to measure the engine in `src/AI.Search`, both against the same generated corpus and both
against the real `rg` binary rather than against a re-implementation of its behavior.

## Comparison with ripgrep

```powershell
dotnet run --project benchmarks/AI.Search.Benchmarks -c Release -- compare
dotnet run --project benchmarks/AI.Search.Benchmarks -c Release -- compare --files 50000 --runs 7
dotnet run --project benchmarks/AI.Search.Benchmarks -c Release -- compare --rg "C:\tools\rg.exe"
```

The command generates the corpus once per size, times both tools on the same six workloads and
prints one row per workload: the median of AI.Search, its throughput, the number of matches it
found, the median of `rg` and how many times faster `rg` is (`ratio` above 1 means `rg` is slower).
Both tools run in a child process, so each number describes a tool doing a search rather than a
caller holding a warm runtime, and each gets one untimed run per workload first so the tree is in
the operating system's cache.

A measured run on a Ryzen 9 5900X, 20000 files (67 MB), three runs per workload, ripgrep 15.2.0:

```
| Workload               | AI.Search   |         | matches | ripgrep    | ratio |
| literal, every file    |     260.1 ms |     259 MB/s |  120000 |   376.1 ms |  1.45x |
| literal, few files     |     191.2 ms |     352 MB/s |    1200 |   280.8 ms |  1.47x |
| literal, ignore case   |     257.1 ms |     262 MB/s |  120000 |   338.3 ms |  1.32x |
| regex                  |     432.7 ms |     156 MB/s |   20000 |   335.3 ms |  0.77x |
| word                   |     260.7 ms |     258 MB/s |  120000 |   385.4 ms |  1.48x |
| files with matches     |     211.0 ms |     319 MB/s |   20000 |   332.4 ms |  1.57x |
```

Literal workloads are ahead of `rg` because the walk and the read are cheap and the matching runs over
`IndexOf`; the regex workload is behind by a quarter, which is where the remaining work is.

`rg` is looked up as `--rg <path>`, then `AI_SEARCH_RG`, then `PATH`, then the portable copy this
machine keeps at `D:\App\MCP\ripgrep\ripgrep-15.2.0-x86_64-pc-windows-msvc\rg.exe`. Without it
the table still shows the engine alone.

The corpus lives in the temporary directory (`%TEMP%\AI.Search.Benchmarks\corpus-<files>`): source-like
text in a tree ten directories wide and two levels deep, a `.git` marker with a `.gitignore` that
excludes a `generated` folder, an excluded `build.log` and a hidden folder, one keyword in every
file and one in a hundredth of them. Pass `--regenerate` to rebuild it.

## In-process benchmarks

```powershell
dotnet run --project benchmarks/AI.Search.Benchmarks -c Release -- --list flat
dotnet run --project benchmarks/AI.Search.Benchmarks -c Release -- --filter "*EngineBenchmarks*"
dotnet run --project benchmarks/AI.Search.Benchmarks -c Release -- --filter "*EngineBenchmarks*" --job Medium
```

Reports land in `bin/Release/net11.0/benchmarks`, inside the build output, so a run leaves the
repository clean: the `-report-github.md` and `-report.csv` files are the tables to keep a record of.

`EngineBenchmarks` measures `ISearchEngine.SearchAsync` alone with and without parallel scanning, on
both corpus sizes and four workloads, and reports managed allocations through `MemoryDiagnoser`. Its
`ShortRunJob` keeps the suite quick enough to run after a change; use `--job Medium` or more
iterations for numbers worth comparing revisions.

## Reading the numbers

- The comparison is machine- and disk-specific: quote the ripgrep version and the corpus size with
  any result, and compare revisions of this engine on the same machine.
- ripgrep is a compiled Rust program with a regex engine that skips uninteresting bytes; this engine
  decodes UTF-8 line by line to report columns in characters. The gap on large files is therefore
  expected, and the point of the table is to keep it visible while the engine changes.
- A workload that finds nothing (`literal, few files`) is dominated by the walk and the read; one
  that finds every line is dominated by the matching and the result bookkeeping.