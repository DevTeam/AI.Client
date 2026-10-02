```

BenchmarkDotNet v0.15.8, Windows 10 (10.0.19045.6456/22H2/2022Update)
AMD Ryzen 9 5900X 4.20GHz, 1 CPU, 24 logical and 12 physical cores
.NET SDK 10.0.401
  [Host]     : .NET 10.0.12 (10.0.12, 10.0.1226.42308), X64 RyuJIT x86-64-v3
  Job-MSEJOC : .NET 10.0.12 (10.0.12, 10.0.1226.42308), X64 RyuJIT x86-64-v3

InvocationCount=1  IterationCount=1  LaunchCount=3  
RunStrategy=ColdStart  UnrollFactor=1  WarmupCount=0  

```
| Method       | Languages   | Mean     | Error    | StdDev   | Gen0      | Gen1      | Gen2      | Allocated |
|------------- |------------ |---------:|---------:|---------:|----------:|----------:|----------:|----------:|
| **FirstRequest** | **en,ru**       | **623.9 ms** | **215.4 ms** | **11.81 ms** | **2000.0000** | **1000.0000** | **1000.0000** |  **25.38 MB** |
| **FirstRequest** | **en,ru,fr,es** | **914.2 ms** | **535.8 ms** | **29.37 ms** | **3000.0000** | **2000.0000** | **1000.0000** |  **53.41 MB** |
