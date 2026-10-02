using BenchmarkDotNet.Running;
using AI.TextCorrection.Benchmarks;

BenchmarkSwitcher.FromAssembly(typeof(WarmCorrectionBenchmarks).Assembly).Run(args);
