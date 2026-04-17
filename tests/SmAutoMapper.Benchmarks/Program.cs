using BenchmarkDotNet.Running;
using SmAutoMapper.Benchmarks;

BenchmarkSwitcher
    .FromAssembly(typeof(SimpleMappingBenchmark).Assembly)
    .Run(args);
