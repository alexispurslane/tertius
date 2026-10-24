using System.Reflection;
using System.Runtime.CompilerServices;
using BenchmarkDotNet.Attributes;
using Tertius.Frames.Rails;
using Tertius.Physics;
using Tertius.Simulation;

namespace Tertius.Benchmarks;

/// <summary>
/// The cost of building one render snapshot on one thread, with the frame outlines off and on
/// (<see cref="World.ShowFrameOutlines"/>, ter-84iw). With them off, the time must equal the time before the outlines,
/// within the noise. Three scenes:
/// <list type="bullet">
/// <item><c>flat</c>: 20,000 visible entities without bodies, in a frame with uniform gravity, so each instance also takes
/// its hemisphere's up from the frame's field. Half of them stand in a turned child frame. No physics.</item>
/// <item><c>orbit</c>: a planet with a station on a Kepler orbit at 7,000 km, so the station's hull lives in a following frame,
/// and a ship with 10 crates that coast beside it at orbital speed, so they fall in numeric frames: about ten frames, as in
/// the demo's scene. On Jolt.</item>
/// <item><c>frames1000</c>: 900 static frames without bodies near the root's origin, and 100 clusters of 200 static boxes
/// 10,000 km out, so that each cluster rests in a numeric frame: 1,000 frames and 20,000 bodies, each one visible. On
/// Jolt.</item>
/// </list>
/// The cleanup prints the frames, the outlines and the bytes of the outlines in one snapshot.
/// <para>
/// <see cref="PlacePuffs"/> times the placement of the puffs alone (ter-4zt6, task 4), per puff: 4,096 puffs, the default
/// capacity, half in the root frame and half in the last semantic frame, placed into a snapshot after a build that follows
/// the build of the tick before, so that each puff also takes its previous place one chord back. The outlines do not
/// change it. Its cleanup prints the puffs, the age step and the looks of the snapshot.
/// </para>
/// </summary>
[MemoryDiagnoser]
public class RenderSnapshotBenchmarks
{
    private const double EarthMu = 3.986004418e14;

    private const int PuffCount = 4_096;

    private static readonly RenderData Visible = new()
    {
        Mesh = new(1),
        Material = new(1),
        BoundingRadius = 1,
        Flags = RenderFlags.Visible,
    };

    private World world = null!;

    private RenderSnapshotBuilder puffBuilder = null!;
    private readonly RenderSnapshot puffSnapshot = new();
    private Puff[] puffRows = [];
    private PuffRange[] puffRanges = [];

    private readonly PuffLook[] puffLooks =
    [
        new(Life: 2, StartRadius: 0.5f, EndRadius: 2, TemperatureRatio: 3, Shading: PuffShading.Glow(new(4, 2, 1))),
        new(Life: 5, StartRadius: 1, EndRadius: 4, TemperatureRatio: 0.5f, Shading: PuffShading.Lit(new(0.5f))),
    ];

    [Params("flat", "orbit", "frames1000")]
    public string Scene = "flat";

    [Params(false, true)]
    public bool Outlines;

    [GlobalSetup]
    public void Setup()
    {
        world = Scene switch
        {
            "flat" => Flat(),
            "orbit" => Orbit(),
            "frames1000" => ThousandFrames(),
            _ => throw new ArgumentException($"No scene {Scene}."),
        };
        world.ShowFrameOutlines = Outlines;
        world.PublishRenderSnapshot();
        world.RenderSnapshots.AcquireLatest();
    }

    [GlobalCleanup]
    public void Cleanup()
    {
        world.PublishRenderSnapshot();
        RenderSnapshot snapshot = world.RenderSnapshots.AcquireLatest() ?? world.RenderSnapshots.ReadBuffer;
        Console.WriteLine(
            $"RenderSnapshot {Scene} outlines={Outlines}: {world.Frames.Count} frames, {world.Frames.NumericCount} numeric, "
                + $"{snapshot.Instances.Count} instances, {snapshot.Outlines.Count} outlines, {snapshot.Outlines.Count * Unsafe.SizeOf<FrameOutline>()} bytes of outlines"
        );
        world.Dispose();
    }

    [Benchmark]
    public void Build()
    {
        world.PublishRenderSnapshot();
        world.RenderSnapshots.AcquireLatest();
    }

    [GlobalSetup(Target = nameof(PlacePuffs))]
    public void SetupPuffs()
    {
        Setup();
        // The tick's build follows the build of the setup, which was of the tick before.
        world.Tick();
        puffBuilder = (RenderSnapshotBuilder)
            typeof(World).GetField("renderBuilder", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(world)!;
        FrameTree frames = world.Frames;
        FrameHandle root = frames.AtDense(0),
            last = root;
        for (int i = frames.Count - 1; i > 0 && last == root; i--)
            if (!frames.IsNumeric(frames.AtDense(i)))
                last = frames.AtDense(i);
        puffRows = new Puff[PuffCount];
        for (int i = 0; i < PuffCount; i++)
            puffRows[i] = new()
            {
                Place = new(i % 64 * 0.5, i / 64 % 64 * 0.5, -i * 0.01),
                Chord = new(20, 3, -5),
                Age = i % 120 / 60f,
                Look = i & 1,
            };
        puffRanges = [new(root, 0, PuffCount / 2), new(last, PuffCount / 2, PuffCount - PuffCount / 2)];
        PlacePuffs();
    }

    [GlobalCleanup(Target = nameof(PlacePuffs))]
    public void CleanupPuffs()
    {
        Console.WriteLine(
            $"RenderSnapshot {Scene} puffs: {puffSnapshot.Puffs.Count} puffs, age step {puffSnapshot.PuffAgeStep} s, "
                + $"{puffSnapshot.PuffLooks.Count} looks, {puffRanges.Length} ranges"
        );
        world.Dispose();
    }

    /// <summary>The placement of the puffs into a snapshot, per puff.</summary>
    [Benchmark(OperationsPerInvoke = PuffCount)]
    public void PlacePuffs() =>
        puffBuilder.BuildPuffs(
            puffSnapshot,
            new(rows: puffRows, ranges: puffRanges, looks: puffLooks, step: world.DeltaTime)
        );

    private static World Flat()
    {
        const int entities = 20_000;
        var world = new World(new() { MaxDegreeOfParallelism = 1 });
        FrameHandle ground = world.CreateRootFrame(
            new(FrameBounds.Sphere(1e6f), GravityField.Uniform(new(0, -9.81f, 0)))
        );
        FrameHandle ship = world.GetOwnedFrame(
            world.Spawn(
                new()
                {
                    Frame = ground,
                    Pose = new(new(0, 0, -50), DQuaternion.FromAxisAngle(Double3.UnitX, 0.3)),
                    AsFrame = new(FrameBounds.Sphere(1e4f), GravityField.Uniform(new(0, -9.81f, 0))),
                }
            )
        );
        for (int i = 0; i < entities; i++)
            world.Spawn(
                new()
                {
                    Frame = (i & 1) == 0 ? ground : ship,
                    Pose = new(new(i % 100, i / 100 % 100, -(i / 10_000)), DQuaternion.Identity),
                    Render = Visible,
                }
            );
        world.Camera = world.Spawn(
            new()
            {
                Frame = ground,
                Pose = new(new(50, 50, 100), DQuaternion.Identity),
                Render = new RenderData(),
            }
        );
        world.Tick();
        return world;
    }

    private static World Orbit()
    {
        var world = new World(
            new() { MaxDegreeOfParallelism = 1, DisposePhysics = true },
            new JoltPhysicsBackend(new() { WorkerThreads = 2 })
        );
        FrameHandle planet = world.CreateRootFrame(new(FrameBounds.Sphere(1e9f), GravityField.Point(EarthMu)));
        var orbit = KeplerOrbit.Circular(7e6);
        (Double3 place, Double3 velocity) = orbit.StateAt(world.Time, EarthMu);
        Entity stationHull = world.Spawn(
            new()
            {
                Frame = planet,
                Pose = new(place, orbit.RotationAt(world.Time, EarthMu)),
                LinearVelocity = velocity,
                AngularVelocity = orbit.AngularVelocityAt(world.Time, EarthMu).ToVector3(),
                Body = new(Shape: new BoxShapeDesc(new(10, 5, 10)), Motion: BodyMotion.Kinematic),
                Render = Visible,
                AsFrame = new(FrameBounds.Sphere(100)),
                Components = [new KeplerMotion { Orbit = orbit }],
            }
        );
        // The ship and its crates coast 2 km behind the station along the orbit, at the station's velocity, too far for
        // its bubble.
        Double3 behind = place - velocity.Normalized() * 2_000;
        world.Spawn(
            new()
            {
                Frame = planet,
                Pose = new(behind, DQuaternion.Identity),
                LinearVelocity = velocity,
                Body = new(Shape: new BoxShapeDesc(new(5, 3, 15)), Mass: 1000, LinearDamping: 0, AngularDamping: 0),
                Render = Visible,
                AsFrame = new(FrameBounds.Sphere(50)),
            }
        );
        var crate = new BodyDesc(Shape: new BoxShapeDesc(new(0.5f)), Mass: 10, LinearDamping: 0, AngularDamping: 0);
        for (int i = 0; i < 10; i++)
            world.Spawn(
                new()
                {
                    Frame = planet,
                    Pose = new(behind + new Double3(0, 100 + 30 * i, 0), DQuaternion.Identity),
                    LinearVelocity = velocity,
                    Body = crate,
                    Render = Visible,
                }
            );
        world.Camera = world.Spawn(
            new()
            {
                Frame = world.GetOwnedFrame(stationHull),
                Pose = new(new(0, 20, 60), DQuaternion.Identity),
                Render = new RenderData(),
            }
        );
        for (int i = 0; i < 5; i++)
            world.Tick();
        return world;
    }

    private static World ThousandFrames()
    {
        const int semanticFrames = 900,
            clusters = 100,
            boxesPerCluster = 200;
        var world = new World(
            new()
            {
                MaxDegreeOfParallelism = 1,
                DisposePhysics = true,
                InitialCapacity = semanticFrames + clusters * boxesPerCluster + 1024,
            },
            new JoltPhysicsBackend(new() { WorkerThreads = 2 })
        );
        FrameHandle space = world.CreateRootFrame(new(FrameBounds.Sphere(1e12f)));
        for (int i = 0; i < semanticFrames; i++)
            world.Spawn(
                new()
                {
                    Frame = space,
                    Pose = new(new(i % 30 * 200.0, i / 30 * 200.0, -500), DQuaternion.Identity),
                    AsFrame = new(FrameBounds.Sphere(50)),
                }
            );
        // Each cluster is a grid of 10 by 20 boxes 1.5 m apart, whose bounding spheres overlap, so the boxes form one group.
        var box = new BodyDesc(Shape: new BoxShapeDesc(new(0.5f)), Motion: BodyMotion.Static);
        for (int c = 0; c < clusters; c++)
        for (int b = 0; b < boxesPerCluster; b++)
            world.Spawn(
                new()
                {
                    Frame = space,
                    Pose = new(new(1e7 + c * 1e4 + b % 10 * 1.5, b / 10 * 1.5, 0), DQuaternion.Identity),
                    Body = box,
                    Render = Visible,
                }
            );
        world.Camera = world.Spawn(
            new()
            {
                Frame = space,
                Pose = new(new(1e7 - 100, 0, 0), DQuaternion.Identity),
                Render = new RenderData(),
            }
        );
        for (int i = 0; i < 3; i++)
            world.Tick();
        return world;
    }
}
