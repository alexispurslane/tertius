using System.Numerics;
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
/// <item><c>orbit</c>: a planet with a station on a Kepler orbit at 7,000 km, so the station's hull lives in an orbit frame,
/// and a ship with 10 crates that coast beside it at orbital speed, so they fall in numeric frames: about ten frames, as in
/// the demo's scene. On Jolt.</item>
/// <item><c>frames1000</c>: 900 static frames without bodies near the root's origin, and 100 clusters of 200 static boxes
/// 10,000 km out, so that each cluster rests in a numeric frame: 1,000 frames and 20,000 bodies, each one visible. On
/// Jolt.</item>
/// </list>
/// The cleanup prints the frames, the outlines and the bytes of the outlines in one snapshot.
/// </summary>
[MemoryDiagnoser]
public class RenderSnapshotBenchmarks
{
    private const double EarthMu = 3.986004418e14;

    private static readonly RenderData Visible = new() { Mesh = new MeshId(1), Material = new MaterialId(1), BoundingRadius = 1, Flags = RenderFlags.Visible };

    private World world = null!;

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
        world.RenderSnapshots.TryAcquireLatest(out _);
    }

    [GlobalCleanup]
    public void Cleanup()
    {
        world.PublishRenderSnapshot();
        world.RenderSnapshots.TryAcquireLatest(out RenderSnapshot snapshot);
        Console.WriteLine($"RenderSnapshot {Scene} outlines={Outlines}: {world.Frames.Count} frames, {world.Frames.NumericCount} numeric, " +
            $"{snapshot.Instances.Count} instances, {snapshot.Outlines.Count} outlines, {snapshot.Outlines.Count * Unsafe.SizeOf<FrameOutline>()} bytes of outlines");
        world.Dispose();
    }

    [Benchmark]
    public void Build()
    {
        world.PublishRenderSnapshot();
        world.RenderSnapshots.TryAcquireLatest(out _);
    }

    private static World Flat()
    {
        const int entities = 20_000;
        var world = new World(new WorldOptions { MaxDegreeOfParallelism = 1 });
        FrameHandle ground = world.CreateRootFrame(new FrameSpec(FrameBounds.Sphere(1e6f), GravityField.Uniform(new Vector3(0, -9.81f, 0))));
        FrameHandle ship = world.GetOwnedFrame(world.Spawn(new SpawnDesc
        {
            Frame = ground,
            Pose = new DPose(new Double3(0, 0, -50), DQuaternion.FromAxisAngle(Double3.UnitX, 0.3)),
            AsFrame = new FrameSpec(FrameBounds.Sphere(1e4f), GravityField.Uniform(new Vector3(0, -9.81f, 0))),
        }));
        for (int i = 0; i < entities; i++)
            world.Spawn(new SpawnDesc
            {
                Frame = (i & 1) == 0 ? ground : ship,
                Pose = new DPose(new Double3(i % 100, (i / 100) % 100, -(i / 10_000)), DQuaternion.Identity),
                Render = Visible,
            });
        world.Camera = world.Spawn(new SpawnDesc { Frame = ground, Pose = new DPose(new Double3(50, 50, 100), DQuaternion.Identity), Render = new RenderData() });
        world.Tick();
        return world;
    }

    private static World Orbit()
    {
        var world = new World(new WorldOptions { MaxDegreeOfParallelism = 1, DisposePhysics = true },
            new JoltPhysicsBackend(new JoltBackendOptions { WorkerThreads = 2 }));
        FrameHandle planet = world.CreateRootFrame(new FrameSpec(FrameBounds.Sphere(1e9f), GravityField.Point(EarthMu)));
        var orbit = KeplerOrbit.Circular(7e6);
        (Double3 place, Double3 velocity) = orbit.StateAt(world.Time, EarthMu);
        Entity stationHull = world.Spawn(new SpawnDesc
        {
            Frame = planet,
            Pose = new DPose(place, orbit.RotationAt(world.Time, EarthMu)),
            LinearVelocity = velocity,
            AngularVelocity = orbit.AngularVelocityAt(world.Time, EarthMu).ToVector3(),
            Body = new BodyDesc(new BoxShapeDesc(new Vector3(10, 5, 10)), BodyMotion.Kinematic),
            Render = Visible,
            AsFrame = new FrameSpec(FrameBounds.Sphere(100)),
            Components = [new KeplerMotion { Orbit = orbit }],
        });
        // The ship and its crates coast 2 km behind the station along the orbit, at the station's velocity, too far for
        // its bubble.
        Double3 behind = place - velocity.Normalized() * 2_000;
        world.Spawn(new SpawnDesc
        {
            Frame = planet,
            Pose = new DPose(behind, DQuaternion.Identity),
            LinearVelocity = velocity,
            Body = new BodyDesc(new BoxShapeDesc(new Vector3(5, 3, 15)), Mass: 1000, LinearDamping: 0, AngularDamping: 0),
            Render = Visible,
            AsFrame = new FrameSpec(FrameBounds.Sphere(50)),
        });
        var crate = new BodyDesc(new BoxShapeDesc(new Vector3(0.5f)), Mass: 10, LinearDamping: 0, AngularDamping: 0);
        for (int i = 0; i < 10; i++)
            world.Spawn(new SpawnDesc
            {
                Frame = planet,
                Pose = new DPose(behind + new Double3(0, 100 + 30 * i, 0), DQuaternion.Identity),
                LinearVelocity = velocity,
                Body = crate,
                Render = Visible,
            });
        world.Camera = world.Spawn(new SpawnDesc { Frame = world.GetOwnedFrame(stationHull), Pose = new DPose(new Double3(0, 20, 60), DQuaternion.Identity), Render = new RenderData() });
        for (int i = 0; i < 5; i++)
            world.Tick();
        return world;
    }

    private static World ThousandFrames()
    {
        const int semanticFrames = 900, clusters = 100, boxesPerCluster = 200;
        var world = new World(new WorldOptions { MaxDegreeOfParallelism = 1, DisposePhysics = true, InitialCapacity = semanticFrames + clusters * boxesPerCluster + 1024 },
            new JoltPhysicsBackend(new JoltBackendOptions { WorkerThreads = 2 }));
        FrameHandle space = world.CreateRootFrame(new FrameSpec(FrameBounds.Sphere(1e12f)));
        for (int i = 0; i < semanticFrames; i++)
            world.Spawn(new SpawnDesc
            {
                Frame = space,
                Pose = new DPose(new Double3(i % 30 * 200.0, i / 30 * 200.0, -500), DQuaternion.Identity),
                AsFrame = new FrameSpec(FrameBounds.Sphere(50)),
            });
        // Each cluster is a grid of 10 by 20 boxes 1.5 m apart, whose bounding spheres overlap, so the boxes form one group.
        var box = new BodyDesc(new BoxShapeDesc(new Vector3(0.5f)), BodyMotion.Static);
        for (int c = 0; c < clusters; c++)
            for (int b = 0; b < boxesPerCluster; b++)
                world.Spawn(new SpawnDesc
                {
                    Frame = space,
                    Pose = new DPose(new Double3(1e7 + c * 1e4 + b % 10 * 1.5, b / 10 * 1.5, 0), DQuaternion.Identity),
                    Body = box,
                    Render = Visible,
                });
        world.Camera = world.Spawn(new SpawnDesc { Frame = space, Pose = new DPose(new Double3(1e7 - 100, 0, 0), DQuaternion.Identity), Render = new RenderData() });
        for (int i = 0; i < 3; i++)
            world.Tick();
        return world;
    }
}
