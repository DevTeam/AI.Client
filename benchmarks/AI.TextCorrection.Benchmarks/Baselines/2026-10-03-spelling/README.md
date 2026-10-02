# Spelling correction measurements

`SpellingCorrection.md` contains the BenchmarkDotNet ShortRun report for the new
combined analyzer and uncached, one-edit dictionary searches. The measurements
use Windows 10, Ryzen 9 5900X and .NET 10.0.12; dictionaries are prepared outside
timing. WarmAutomaticCorrection also warms the bounded spelling result cache.

```powershell
dotnet run --project benchmarks/AI.TextCorrection.Benchmarks -c Release -- --filter '*SpellingCorrectionBenchmarks*' --artifacts benchmarks/AI.TextCorrection.Benchmarks/artifacts/spelling
```

- English input: `helllo dictioanry`.
- Russian input: `првиет правопсиание`.
- Mixed English/Russian input: `helllo ghbdtn првиет`.
- Uncached search checks `helllo` in English or `првиет` in Russian. For the
  mixed selection this method still measures the English dictionary alone.

Cached combined analysis measured about 3.3–3.5 microseconds for one language
and 16.7 microseconds for the mixed phrase. Uncached searches measured about
301–360 microseconds. The source generates neighbours in stack-allocated spans;
Hunspell's checks still allocate internally, particularly with Russian affixes.
These are exploratory desktop measurements, not browser/WASM timings.
