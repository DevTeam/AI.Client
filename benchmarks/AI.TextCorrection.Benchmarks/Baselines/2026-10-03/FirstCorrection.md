```

BenchmarkDotNet v0.15.8, Windows 10 (10.0.19045.6456/22H2/2022Update)
AMD Ryzen 9 5900X 4.20GHz, 1 CPU, 24 logical and 12 physical cores
.NET SDK 10.0.401
  [Host]     : .NET 10.0.12 (10.0.12, 10.0.1226.42308), X64 RyuJIT x86-64-v3
  Job-MSEJOC : .NET 10.0.12 (10.0.12, 10.0.1226.42308), X64 RyuJIT x86-64-v3
  Dry        : .NET 10.0.12 (10.0.12, 10.0.1226.42308), X64 RyuJIT x86-64-v3

IterationCount=1  RunStrategy=ColdStart  UnrollFactor=1  

```
| Method       | Job        | InvocationCount | LaunchCount | WarmupCount | Languages   | Mean    | Error   | StdDev  | Gen0       | Gen1      | Gen2      | Allocated |
|------------- |----------- |---------------- |------------ |------------ |------------ |--------:|--------:|--------:|-----------:|----------:|----------:|----------:|
| **FirstRequest** | **Job-MSEJOC** | **1**               | **3**           | **0**           | **en,ru**       | **12.55 s** | **0.533 s** | **0.029 s** |  **9000.0000** | **6000.0000** | **2000.0000** | **120.53 MB** |
| FirstRequest | Dry        | Default         | 1           | 1           | en,ru       | 12.55 s |      NA | 0.000 s |  9000.0000 | 6000.0000 | 2000.0000 | 120.53 MB |
| **FirstRequest** | **Job-MSEJOC** | **1**               | **3**           | **0**           | **en,ru,fr,es** | **21.40 s** | **0.676 s** | **0.037 s** | **14000.0000** | **9000.0000** | **2000.0000** | **209.41 MB** |
| FirstRequest | Dry        | Default         | 1           | 1           | en,ru,fr,es | 21.40 s |      NA | 0.000 s | 14000.0000 | 9000.0000 | 2000.0000 | 209.41 MB |
