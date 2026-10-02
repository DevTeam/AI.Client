# Text correction benchmarks

Initial measurements are saved in [Baselines/2026-10-03](Baselines/2026-10-03/README.md).
The optimized comparison is in [Baselines/2026-10-03-optimized](Baselines/2026-10-03-optimized/README.md).

Run from the repository root using Release configuration:

```powershell
dotnet run --project benchmarks/AI.TextCorrection.Benchmarks -c Release -- --list flat
dotnet run --project benchmarks/AI.TextCorrection.Benchmarks -c Release -- --filter '*' --artifacts artifacts/text-correction
```

To measure only the two correction directions (with two and four languages):

```powershell
dotnet run --project benchmarks/AI.TextCorrection.Benchmarks -c Release -- --filter '*Warm*RussianPhrase*' '*Warm*EnglishPhrase*' --artifacts artifacts/text-correction-warm
```

For a quick execution check (not a statistically reliable performance result):

```powershell
dotnet run --project benchmarks/AI.TextCorrection.Benchmarks -c Release -- --filter '*' --iterationCount 1 --warmupCount 0 --launchCount 1 --invocationCount 1 --unrollFactor 1 --artifacts artifacts/text-correction-smoke
```

Measurements use the production dictionaries and services. Whole-pipeline benchmarks
resolve services through the production Pure.DI composition. No mock lexicon is used.

- `DictionaryLoadingBenchmarks`: separately loads Hunspell and prepares trigrams for each language.
  `BuildTrigrams` retains its historical name for comparisons; it now loads the compiled index.
- `PreparationBenchmarks`: prepares a fresh graph with two or four selected languages on every operation.
- `FirstCorrectionBenchmarks`: one first request per process, including preparation, with three launches.
- `WarmCorrectionBenchmarks`: prepares once outside timing and measures repeated analysis in both directions,
  already correct text, unknown words, French/Spanish conversion, and a long message containing protected code and URLs.

Every class reports managed allocations and GC through MemoryDiagnoser. Cold measurements include
the intentional cooperative delays in the production preparation path. Process startup itself is
outside the measured method. Filesystem caches are not flushed; "cold" means fresh application
dictionary/model state, not a cold operating system cache.

Keep BenchmarkDotNet's environment header and generated Markdown/CSV/JSON reports when comparing
revisions. Use the same machine, runtime, input, language set and job for before/after measurements.
Single-iteration runs only validate execution. ShortRun results are exploratory; use
`--iterationCount 15 --warmupCount 10 --launchCount 3` for a more thorough warm analysis comparison.
Adding `--job Dry` or `--job Medium` adds jobs alongside those declared on the classes.
Desktop .NET timings do not measure browser/WASM event-loop
responsiveness, JS interop, download or decompression; verify those separately in the UI.

This project establishes the baseline and does not change the correction algorithm or resource format.

## Resource preparation

Resource generation is an instance target behind `IPrepareTextCorrectionTarget` in `build/Targets`,
registered in the existing Pure.DI build composition and exposed by `BuildApplication`.
It reads the original licensed `.dic` inputs and writes deterministic,
versioned binary trigram indexes with input fingerprints. Run **Prepare Text Correction**
or `dotnet run --project build -c Release -- prepare-text-correction` after importing dictionaries.
Commit the generated `Dictionaries/<language>/index.trigrams` files alongside the sources.
Normal builds and publishing embed those resources without invoking or depending on the build tool.

The bundled models now load packed, sorted trigram indexes instead of scanning `.dic` again,
allocating three-character strings or yielding every 512 lines. Custom dictionary providers can
continue to prepare models from word lists. Hunspell requires `.aff` for inflections and other dictionary rules. Replacing runtime
Hunspell parsing requires either compiled rules or an equivalent generated word index, with
behavior and resource size verified against the existing tests. Do not simply discard `.aff`.
For layout conversion, cached character lookups replace repeated `IndexOf` scans.
For tokenization, a span-based scanner replaces the per-request regular expression.
