using BenchmarkDotNet.Attributes;
using Tertius.Simulation;

namespace Tertius.Benchmarks;

/// <summary>
/// ter-sn28 profiling aid: the parts of the float view fill of the 1,000 Kepler frames of
/// <see cref="KeplerFrameTickBenchmarks"/>, each over every frame, outside a tick (see World.ProfileMirrorPart).
/// 0 the pass of the sync point, 1 the pass before ter-sn28, frame by frame.
/// </summary>
public class MirrorPartBenchmarks
{
    private readonly KeplerFrameTickBenchmarks scene = new() { MaxDegreeOfParallelism = 1 };
    private World world = null!;

    [Params(0, 1)]
    public int Part;

    [GlobalSetup]
    public void Setup()
    {
        scene.Setup();
        world = scene.World;
    }

    [Benchmark]
    public void Run() => world.ProfileMirrorPart(Part);

    [GlobalCleanup]
    public void Cleanup() => scene.Cleanup();
}
