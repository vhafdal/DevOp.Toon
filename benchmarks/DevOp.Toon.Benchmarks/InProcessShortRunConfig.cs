using BenchmarkDotNet.Configs;
using BenchmarkDotNet.Jobs;
using BenchmarkDotNet.Toolchains.InProcess.Emit;

namespace DevOp.Toon.Benchmarks;

public sealed class InProcessShortRunConfig : ManualConfig
{
    public InProcessShortRunConfig()
    {
        AddJob(Job.ShortRun
            .WithToolchain(InProcessEmitToolchain.Instance)
            .WithId("InProcessShortRun"));
    }
}
