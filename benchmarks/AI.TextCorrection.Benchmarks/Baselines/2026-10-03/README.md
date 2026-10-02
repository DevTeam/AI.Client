# Original implementation baseline

Production revision: `c8f58c8766f20eb3d7ad6dbb60b5a3456813be64`.
The benchmark project was added on top of this revision without modifying production code.
Environment and job settings are included in each report.

| Measurement | Two languages (en, ru) | Four languages (en, ru, fr, es) |
| --- | ---: | ---: |
| First request, including preparation | 12.554 s | 21.404 s |
| Warm Russian phrase analysis | 24.52 us / 51.54 KB | 61.69 us / 129.68 KB |
| Warm English phrase analysis | 20.55 us / 47.63 KB | 54.08 us / 110.77 KB |

Trigram preparation means: en 2.971 s, ru 8.847 s, fr 5.077 s, es 3.475 s.
These timings include cooperative production delays, not just CPU work.
Warm figures are exploratory ShortRun measurements, not browser/WASM timings.
The Russian two-language run produced a minimum-iteration-time warning (98 ms versus
the recommended 100 ms); use longer runs before claiming small improvements.

- [Dictionary parsing and trigram preparation](DictionaryLoading.md)
- [First correction including preparation](FirstCorrection.md)
- [Warm correction in both directions](WarmCorrection.md)

Loading and first-request reports contain both the class-defined jobs and an additional Dry job.
Use the Monitoring and three-launch ColdStart rows for comparisons; Dry rows are single observations.
The broad run was stopped after loading and first-request reports completed to avoid redundant
preparation measurements. The warm subset completed separately with exit code 0.
The standalone preparation and remaining warm scenarios are implemented but not part of this baseline.
