```

BenchmarkDotNet v0.15.8, Windows 10 (10.0.19045.6456/22H2/2022Update)
AMD Ryzen 9 5900X 4.20GHz, 1 CPU, 24 logical and 12 physical cores
.NET SDK 10.0.401
  [Host]   : .NET 10.0.12 (10.0.12, 10.0.1226.42308), X64 RyuJIT x86-64-v3
  ShortRun : .NET 10.0.12 (10.0.12, 10.0.1226.42308), X64 RyuJIT x86-64-v3

Job=ShortRun  IterationCount=3  LaunchCount=1  
WarmupCount=3  

```
| Method  | Languages   | Scenario      | Mean      | Error    | StdDev    | Gen0   | Allocated |
|-------- |------------ |-------------- |----------:|---------:|----------:|-------:|----------:|
| **Analyze** | **en,ru**       | **RussianPhrase** | **14.379 μs** | **3.531 μs** | **0.1935 μs** | **1.0834** |  **17.88 KB** |
| **Analyze** | **en,ru**       | **EnglishPhrase** |  **9.010 μs** | **1.055 μs** | **0.0578 μs** | **0.7477** |  **12.23 KB** |
| **Analyze** | **en,ru,fr,es** | **RussianPhrase** | **43.299 μs** | **1.855 μs** | **0.1017 μs** | **2.5635** |  **41.99 KB** |
| **Analyze** | **en,ru,fr,es** | **EnglishPhrase** | **29.949 μs** | **2.760 μs** | **0.1513 μs** | **1.6479** |   **27.2 KB** |
