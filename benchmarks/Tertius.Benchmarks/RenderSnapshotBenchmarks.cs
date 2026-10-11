using System.Numerics;
using BenchmarkDotNet.Attributes;
using Tertius.Simulation;

namespace Tertius.Benchmarks;

/// <summary>
/// The cost of building one render snapshot on one thread: every entity is visible, in a frame with uniform gravity, so
/// each instance also takes its hemisphere's up from the frame's field. Half the entities stand in a turned child frame.
/// </summary>
[MemoryDiagnoser]
public class RenderSnapshotBenchmarks
{
    private World world = null!;

    [Params(20_000)]
    public int Entities;

    [GlobalSetup]
    public void Setup()
    {
        world = new World(new WorldOptions { MaxDegreeOfParallelism = 1 });
        FrameHandle ground = world.CreateRootFrame(new FrameSpec(FrameBounds.Sphere(1e6f), MotionDriver.Static, GravityField.Uniform(new Vector3(0, -9.81f, 0))));
        FrameHandle ship = world.GetOwnedFrame(world.Spawn(new SpawnDesc
        {
            Frame = ground,
            Pose = new DPose(new Double3(0, 0, -50), DQuaternion.FromAxisAngle(Double3.UnitX, 0.3)),
            AsFrame = new FrameSpec(FrameBounds.Sphere(1e4f), MotionDriver.Static, GravityField.Uniform(new Vector3(0, -9.81f, 0))),
        }));
        for (int i = 0; i < Entities; i++)
            world.Spawn(new SpawnDesc
            {
                Frame = (i & 1) == 0 ? ground : ship,
                Pose = new DPose(new Double3(i % 100, (i / 100) % 100, -(i / 10_000)), DQuaternion.Identity),
                Render = new RenderData { Mesh = new MeshId(1), Material = new MaterialId(1), BoundingRadius = 1, Flags = RenderFlags.Visible },
            });
        world.Camera = world.Spawn(new SpawnDesc { Frame = ground, Pose = new DPose(new Double3(50, 50, 100), DQuaternion.Identity), Render = new RenderData() });
        world.Tick();
    }

    [GlobalCleanup]
    public void Cleanup() => world.Dispose();

    [Benchmark]
    public void Build()
    {
        world.PublishRenderSnapshot();
        world.RenderSnapshots.TryAcquireLatest(out _);
    }
}
