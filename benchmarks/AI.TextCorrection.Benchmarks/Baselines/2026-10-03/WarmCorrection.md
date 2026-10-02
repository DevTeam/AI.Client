```

BenchmarkDotNet v0.15.8, Windows 10 (10.0.19045.6456/22H2/2022Update)
AMD Ryzen 9 5900X 4.20GHz, 1 CPU, 24 logical and 12 physical cores
.NET SDK 10.0.401
  [Host]   : .NET 10.0.12 (10.0.12, 10.0.1226.42308), X64 RyuJIT x86-64-v3
  ShortRun : .NET 10.0.12 (10.0.12, 10.0.1226.42308), X64 RyuJIT x86-64-v3

Job=ShortRun  IterationCount=3  LaunchCount=1  
WarmupCount=3  

```
| Method  | Languages   | Scenario      | Mean     | Error     | StdDev   | Gen0   | Gen1   | Allocated |
|-------- |------------ |-------------- |---------:|----------:|---------:|-------:|-------:|----------:|
| **Analyze** | **en,ru**       | **RussianPhrase** | **24.52 μs** | **12.528 μs** | **0.687 μs** | **2.9297** |      **-** |  **51.54 KB** |
| **Analyze** | **en,ru**       | **EnglishPhrase** | **20.55 μs** |  **6.793 μs** | **0.372 μs** | **2.8076** |      **-** |  **47.63 KB** |
| **Analyze** | **en,ru,fr,es** | **RussianPhrase** | **61.69 μs** |  **4.589 μs** | **0.252 μs** | **7.9346** | **0.2441** | **129.68 KB** |
| **Analyze** | **en,ru,fr,es** | **EnglishPhrase** | **54.08 μs** | **15.566 μs** | **0.853 μs** | **6.7749** | **0.1831** | **110.77 KB** |
