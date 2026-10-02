```

BenchmarkDotNet v0.15.8, Windows 10 (10.0.19045.6456/22H2/2022Update)
AMD Ryzen 9 5900X 4.20GHz, 1 CPU, 24 logical and 12 physical cores
.NET SDK 10.0.401
  [Host]   : .NET 10.0.12 (10.0.12, 10.0.1226.42308), X64 RyuJIT x86-64-v3
  ShortRun : .NET 10.0.12 (10.0.12, 10.0.1226.42308), X64 RyuJIT x86-64-v3

Job=ShortRun  IterationCount=3  LaunchCount=1  
WarmupCount=3  

```
| Method                      | Languages | Mean       | Error     | StdDev    | Gen0    | Allocated |
|---------------------------- |---------- |-----------:|----------:|----------:|--------:|----------:|
| **WarmAutomaticCorrection**     | **en**        |   **3.510 μs** |  **2.522 μs** | **0.1382 μs** |  **0.1717** |   **2.83 KB** |
| UncachedSpellingSuggestions | en        | 300.974 μs | 18.964 μs | 1.0395 μs |  1.9531 |  35.26 KB |
| **WarmAutomaticCorrection**     | **en,ru**     |  **16.733 μs** |  **2.356 μs** | **0.1292 μs** |  **0.9766** |  **16.14 KB** |
| UncachedSpellingSuggestions | en,ru     | 309.232 μs | 34.674 μs | 1.9006 μs |  1.9531 |  35.26 KB |
| **WarmAutomaticCorrection**     | **ru**        |   **3.311 μs** |  **2.540 μs** | **0.1392 μs** |  **0.3128** |   **5.16 KB** |
| UncachedSpellingSuggestions | ru        | 360.004 μs | 23.419 μs | 1.2837 μs | 24.9023 |    407 KB |
