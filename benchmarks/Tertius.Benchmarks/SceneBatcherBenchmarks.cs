using System.Numerics;
using BenchmarkDotNet.Attributes;
using Tertius.Rendering;

namespace Tertius.Benchmarks;

/// <summary>
/// The CPU cost of the environment map's culled views (docs/plans/environment-map.md, "Cost"): one
/// <see cref="SceneBatcher.Prepare"/> of a snapshot of 20,000 instances, the size that
/// <see cref="RenderSnapshotBenchmarks"/> builds, on one thread, with the map off, with one face and with six faces.
/// <list type="bullet">
/// <item>The instances are unit cubes in a grid of 100 by 100 by 2, 1 m apart, 100 m in front of the camera, which sits
/// at the render origin and looks down -Z at 1920x1080. So the main view sees each of them.</item>
/// <item>The one face is -Z, the face along the camera's view, which also keeps each instance: the dearest single
/// face. The other five keep none.</item>
/// </list>
/// The median time is the figure to read. The allocations must be 0, because the batcher's arrays have grown to the
/// scene in the setup.
/// </summary>
[MemoryDiagnoser]
[MedianColumn]
public class SceneBatcherBenchmarks
{
    private const int Width = 1920;
    private const int Height = 1080;
    private const int MaterialCount = 8;

    private static readonly MeshInfo[] Meshes = [new(FirstIndex: 0, IndexCount: 36, VertexOffset: 0, Radius: 0.866f)];

    private readonly SceneBatcher batcher = new();
    private readonly PreparedScene scene = new();
    private RenderSnapshot snapshot = new();
    private EnvironmentDraw environment;

    [Params("off", "one-face", "six-faces")]
    public string Map = "off";

    [GlobalSetup]
    public void Setup()
    {
        snapshot = Grid();
        environment = Map switch
        {
            "off" => new(new WithoutMap()),
            "one-face" => new(new WithMap(Faces: new[] { FaceMask.NegativeZ }, Irradiance: new IrradianceReading?[1])),
            "six-faces" => new(new WithMap(Faces: new[] { FaceMask.All }, Irradiance: new IrradianceReading?[1])),
            _ => throw new ArgumentException($"No map setting {Map}."),
        };
        // Grows the batcher's arrays and the scene's to the snapshot, so the measured calls allocate nothing.
        Prepare();
    }

    [GlobalCleanup]
    public void Cleanup()
    {
        Prepare();
        Console.WriteLine(
            $"SceneBatcher map={Map}: {snapshot.Instances.Count} instances, {scene.VisibleCount} visible, "
                + $"{scene.InstanceCount} drawn instances, {scene.CommandCount} commands, "
                + $"{scene.EnvironmentFaces.Count} faces"
        );
    }

    [Benchmark]
    public void Prepare() =>
        batcher.Prepare(
            snapshot: snapshot,
            alpha: 0.5f,
            width: Width,
            height: Height,
            meshes: Meshes,
            materialCount: MaterialCount,
            environment: environment,
            scene: scene
        );

    private static RenderSnapshot Grid()
    {
        var grid = new RenderSnapshot
        {
            Camera = new()
            {
                Rotation = Quaternion.Identity,
                PreviousRotation = Quaternion.Identity,
                VerticalFov = MathF.PI / 3,
            },
            Sun = new()
            {
                Direction = Vector3.Normalize(new(0.3f, 1, 0.2f)),
                Color = Vector3.One,
                Intensity = 2.5f,
                AngularRadius = 0.00467f,
            },
        };
        grid.Chain.Add(new(FrameId: 1, Rotation: Quaternion.Identity, PreviousRotation: Quaternion.Identity));
        for (int i = 0; i < 20_000; i++)
            grid.Instances.Add(
                RenderInstance.Create(
                    mesh: new(0),
                    material: new(1),
                    position: new(x: i % 100 - 50, y: i / 100 % 100 - 50, z: -100 - i / 10_000),
                    rotation: Quaternion.Identity,
                    scale: Vector3.One,
                    boundingRadius: 0.866f,
                    hemisphere: HemisphereUp.Along(Vector3.UnitY),
                    chainDepth: 0,
                    flags: RenderFlags.Visible | RenderFlags.CastsShadow
                )
            );
        return grid;
    }
}
