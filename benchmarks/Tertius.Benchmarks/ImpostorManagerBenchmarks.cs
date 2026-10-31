using BenchmarkDotNet.Attributes;
using Tertius.Rendering;

namespace Tertius.Benchmarks;

/// <summary>
/// The CPU cost of the impostor decisions: one <see cref="SceneBatcher.Prepare"/> of the grid of 20,000 instances of
/// <see cref="SceneBatcherBenchmarks"/>, on one thread, without and with an <see cref="ImpostorManager"/> of 64 tiles.
/// <list type="bullet">
/// <item>Each cube of the grid is at most about 16 px tall at 1920x1080, far below
/// <see cref="ImpostorManager.EnterHeight"/>, so every visible instance is a candidate.</item>
/// <item>The warm-up fills the atlas, so the measured frames run the decisions of every instance and capture nothing, as
/// the stress test's frames do once the atlas is full.</item>
/// </list>
/// The median time is the figure to read, and the allocations must be 0.
/// </summary>
[MemoryDiagnoser]
[MedianColumn]
public class ImpostorManagerBenchmarks
{
    private const int Width = 1920;
    private const int Height = 1080;

    /// <summary>The default material's slot and seven more, all opaque.</summary>
    private static readonly MaterialInfo[] Materials = new MaterialInfo[8];

    private readonly SceneBatcher batcher = new(
        cascadedLights: 1,
        shadowMapSize: ShadowMap.DefaultSize,
        depthRangeScale: 16f
    );
    private readonly PreparedScene scene = new();
    private readonly RenderSnapshot snapshot = SceneBatcherBenchmarks.Grid(chainLength: 1);

    [Params(false, true)]
    public bool Impostors;

    [GlobalSetup]
    public void Setup()
    {
        if (Impostors)
            batcher.Processors.Add(new ImpostorManager(64));
        // Grows the batcher's arrays and the scene's to the snapshot, so the measured calls allocate nothing.
        Prepare();
    }

    [GlobalCleanup]
    public void Cleanup() =>
        Console.WriteLine(
            $"ImpostorManager impostors={Impostors}: {scene.VisibleCount} visible, {scene.InstanceCount} drawn instances"
        );

    [Benchmark]
    public void Prepare() =>
        batcher.Prepare(
            snapshot: snapshot,
            alpha: 0.5f,
            // The highest exposure of the default setting: the reach that every light had before the reach followed the
            // exposure, so the benchmark keeps every light.
            highestStops: AutoExposure.Default.MaxStops,
            width: Width,
            height: Height,
            meshes: SceneBatcherBenchmarks.Meshes,
            materials: Materials,
            environment: new(new WithoutMap()),
            region: DeformRegion.None,
            scene: scene
        );
}
