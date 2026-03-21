using BenchmarkDotNet.Running;
using MyAutoMapper.Benchmarks;

BenchmarkSwitcher
    .FromAssembly(typeof(SimpleMappingBenchmark).Assembly)
    .Run(args);
