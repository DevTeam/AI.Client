# Compiled indexes and scanner comparison

Compared with [the original implementation](../2026-10-03/README.md) on the same machine,
.NET runtime, input, language sets and class-defined jobs. Environment/settings are saved in each report.

| Measurement | Original | Optimized |
| --- | ---: | ---: |
| First request, en/ru | 12.554 s | 0.624 s |
| First request, four languages | 21.404 s | 0.914 s |
| Warm Russian phrase, en/ru | 24.52 us / 51.54 KB | 14.379 us / 17.88 KB |
| Warm English phrase, en/ru | 20.55 us / 47.63 KB | 9.010 us / 12.23 KB |
| Warm Russian phrase, four languages | 61.69 us / 129.68 KB | 43.299 us / 41.99 KB |
| Warm English phrase, four languages | 54.08 us / 110.77 KB | 29.949 us / 27.20 KB |

Compiled trigram loading medians are 0.129–0.183 ms per language, versus original preparation
means of 2.97–8.85 s (which included deliberate cooperative delays). Loading allocations are
54–85 KB per language instead of 19.51–75.80 MB. The four generated resources total
258,048 bytes. Hunspell dictionary parsing and inflection rules are retained.

- [Dictionary/model preparation](DictionaryLoading.md)
- [First correction](FirstCorrection.md)
- [Warm correction](WarmCorrection.md)

Cold results use three process launches; warm results use ShortRun. These are exploratory
measurements with confidence intervals in the reports, not precise guarantees for every input
or browser/WASM. The dictionary report was captured before the final delegate-cache refinement,
which does not change dictionary/index loading. First-request and warm reports use the final code.
All eight remaining warm cases and both standalone preparation cases also completed a
single-iteration execution check; these smoke observations are not used for performance claims.

Validation: 141 library tests, 361 Web tests and 19 JavaScript tests passed. Full Release solution
build and Web publish passed. Incremental builds, missing index regeneration and added/removed
dictionary detection were checked. The published output excludes the build generator.

These reports were captured with automatic generation. Resource preparation is now manual
through **Prepare Text Correction**, with identical index bytes committed beside each dictionary.
This workflow change does not alter the measured runtime algorithms.
