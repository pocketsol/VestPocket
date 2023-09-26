using BenchmarkDotNet.Configs;
using BenchmarkDotNet.Diagnosers;
using BenchmarkDotNet.Environments;
using BenchmarkDotNet.Jobs;
using BenchmarkDotNet.Running;
using VestPocket.Benchmark;

public class Program
{
    public static void Main(string[] args)
    {
        var summary = BenchmarkRunner.Run<VestPocketBenchmarks>(
            config: DefaultConfig.Instance
                .AddDiagnoser(EventPipeProfiler.Default)
                .AddJob(Job.Default.WithMaxRelativeError(.05)),
            args: args
        );
    }
}