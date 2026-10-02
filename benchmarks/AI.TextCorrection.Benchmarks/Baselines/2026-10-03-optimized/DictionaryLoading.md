```

BenchmarkDotNet v0.15.8, Windows 10 (10.0.19045.6456/22H2/2022Update)
AMD Ryzen 9 5900X 4.20GHz, 1 CPU, 24 logical and 12 physical cores
.NET SDK 10.0.401
  [Host]     : .NET 10.0.12 (10.0.12, 10.0.1226.42308), X64 RyuJIT x86-64-v3
  Job-VCYZKC : .NET 10.0.12 (10.0.12, 10.0.1226.42308), X64 RyuJIT x86-64-v3

InvocationCount=1  IterationCount=5  LaunchCount=1  
RunStrategy=Monitoring  UnrollFactor=1  WarmupCount=1  

```
| Method        | Language | Mean         | Error        | StdDev       | Median       | Gen0      | Gen1      | Gen2      | Allocated   |
|-------------- |--------- |-------------:|-------------:|-------------:|-------------:|----------:|----------:|----------:|------------:|
| **LoadHunspell**  | **en**       |  **58,581.3 μs** | **181,073.0 μs** | **47,024.09 μs** |  **30,366.9 μs** |         **-** |         **-** |         **-** |  **6523.45 KB** |
| BuildTrigrams | en       |     939.3 μs |   6,889.2 μs |  1,789.11 μs |     138.7 μs |         - |         - |         - |    59.55 KB |
| **LoadHunspell**  | **es**       |  **91,686.5 μs** | **231,682.7 μs** | **60,167.27 μs** |  **62,577.6 μs** |         **-** |         **-** |         **-** | **10147.24 KB** |
| BuildTrigrams | es       |     887.4 μs |   6,550.1 μs |  1,701.03 μs |     129.4 μs |         - |         - |         - |    53.77 KB |
| **LoadHunspell**  | **fr**       | **134,784.7 μs** | **321,891.5 μs** | **83,594.19 μs** |  **90,347.0 μs** | **1000.0000** | **1000.0000** | **1000.0000** | **18308.95 KB** |
| BuildTrigrams | fr       |     922.3 μs |   6,602.0 μs |  1,714.51 μs |     160.0 μs |         - |         - |         - |    72.61 KB |
| **LoadHunspell**  | **ru**       | **136,669.1 μs** |  **42,285.6 μs** | **10,981.44 μs** | **140,059.5 μs** | **1000.0000** | **1000.0000** | **1000.0000** | **19199.24 KB** |
| BuildTrigrams | ru       |     210.6 μs |     202.2 μs |     52.52 μs |     183.3 μs |         - |         - |         - |    84.88 KB |
