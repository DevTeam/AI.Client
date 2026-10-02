```

BenchmarkDotNet v0.15.8, Windows 10 (10.0.19045.6456/22H2/2022Update)
AMD Ryzen 9 5900X 4.20GHz, 1 CPU, 24 logical and 12 physical cores
.NET SDK 10.0.401
  [Host]     : .NET 10.0.12 (10.0.12, 10.0.1226.42308), X64 RyuJIT x86-64-v3
  Job-VCYZKC : .NET 10.0.12 (10.0.12, 10.0.1226.42308), X64 RyuJIT x86-64-v3
  Dry        : .NET 10.0.12 (10.0.12, 10.0.1226.42308), X64 RyuJIT x86-64-v3

LaunchCount=1  UnrollFactor=1  WarmupCount=1  

```
| Method        | Job        | InvocationCount | IterationCount | RunStrategy | Language | Mean        | Error     | StdDev    | Median      | Gen0      | Gen1      | Gen2      | Allocated |
|-------------- |----------- |---------------- |--------------- |------------ |--------- |------------:|----------:|----------:|------------:|----------:|----------:|----------:|----------:|
| **LoadHunspell**  | **Job-VCYZKC** | **1**               | **5**              | **Monitoring**  | **en**       |    **55.33 ms** | **190.22 ms** | **49.399 ms** |    **30.39 ms** |         **-** |         **-** |         **-** |   **6.37 MB** |
| BuildTrigrams | Job-VCYZKC | 1               | 5              | Monitoring  | en       | 2,971.09 ms |  74.17 ms | 19.262 ms | 2,977.71 ms | 1000.0000 |         - |         - |  19.51 MB |
| LoadHunspell  | Dry        | Default         | 1              | ColdStart   | en       |   249.38 ms |        NA |  0.000 ms |   249.38 ms |         - |         - |         - |   6.37 MB |
| BuildTrigrams | Dry        | Default         | 1              | ColdStart   | en       | 2,986.50 ms |        NA |  0.000 ms | 2,986.50 ms | 1000.0000 |         - |         - |  19.51 MB |
| **LoadHunspell**  | **Job-VCYZKC** | **1**               | **5**              | **Monitoring**  | **es**       |    **86.47 ms** | **227.98 ms** | **59.206 ms** |    **60.02 ms** |         **-** |         **-** |         **-** |   **9.91 MB** |
| BuildTrigrams | Job-VCYZKC | 1               | 5              | Monitoring  | es       | 3,475.34 ms |  20.46 ms |  5.314 ms | 3,474.18 ms | 1000.0000 |         - |         - |  24.13 MB |
| LoadHunspell  | Dry        | Default         | 1              | ColdStart   | es       |   312.68 ms |        NA |  0.000 ms |   312.68 ms |         - |         - |         - |   9.91 MB |
| BuildTrigrams | Dry        | Default         | 1              | ColdStart   | es       | 3,460.75 ms |        NA |  0.000 ms | 3,460.75 ms | 1000.0000 |         - |         - |  24.14 MB |
| **LoadHunspell**  | **Job-VCYZKC** | **1**               | **5**              | **Monitoring**  | **fr**       |   **155.15 ms** | **280.34 ms** | **72.805 ms** |   **123.09 ms** | **1000.0000** | **1000.0000** | **1000.0000** |  **17.71 MB** |
| BuildTrigrams | Job-VCYZKC | 1               | 5              | Monitoring  | fr       | 5,076.62 ms |  60.68 ms | 15.759 ms | 5,083.60 ms | 2000.0000 | 1000.0000 |         - |  37.06 MB |
| LoadHunspell  | Dry        | Default         | 1              | ColdStart   | fr       |   425.88 ms |        NA |  0.000 ms |   425.88 ms | 1000.0000 | 1000.0000 | 1000.0000 |  17.88 MB |
| BuildTrigrams | Dry        | Default         | 1              | ColdStart   | fr       | 5,100.69 ms |        NA |  0.000 ms | 5,100.69 ms | 2000.0000 | 1000.0000 |         - |  37.07 MB |
| **LoadHunspell**  | **Job-VCYZKC** | **1**               | **5**              | **Monitoring**  | **ru**       |   **137.58 ms** |  **40.51 ms** | **10.520 ms** |   **137.71 ms** | **1000.0000** | **1000.0000** | **1000.0000** |  **18.75 MB** |
| BuildTrigrams | Job-VCYZKC | 1               | 5              | Monitoring  | ru       | 8,847.04 ms |  87.50 ms | 22.723 ms | 8,853.04 ms | 4000.0000 | 2000.0000 |         - |   75.8 MB |
| LoadHunspell  | Dry        | Default         | 1              | ColdStart   | ru       |   448.11 ms |        NA |  0.000 ms |   448.11 ms | 1000.0000 | 1000.0000 | 1000.0000 |  18.75 MB |
| BuildTrigrams | Dry        | Default         | 1              | ColdStart   | ru       | 8,859.69 ms |        NA |  0.000 ms | 8,859.69 ms | 4000.0000 | 2000.0000 |         - |   75.8 MB |
