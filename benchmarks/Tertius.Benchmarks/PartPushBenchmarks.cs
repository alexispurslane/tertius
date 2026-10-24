using System.Numerics;
using BenchmarkDotNet.Attributes;
using Tertius.Physics;
using Tertius.Simulation;

namespace Tertius.Benchmarks;

/// <summary>
/// The cost of the flow of the parts' pushes (docs/plans/parts.md, "Costs and measurements"): 2,500 dynamic crates on the
/// Jolt backend, each with <see cref="PartsPerCrate"/> parts that push in every tick, at places off the crate's center of
/// mass. With no part, the crates push themselves, so the sync point carries no push and the lap of the flow reads only
/// its test for an empty list. The lap "5b carry the pushes of the parts" in the profile (a build with
/// -p:SyncProfile=true) is the flow.
/// </summary>
public class PartPushTickBenchmarks
{
    private const int Crates = 2_500;

    private World world = null!;
    private readonly SyncProfileReport profile = new();

    [Params(1, -1)]
    public int MaxDegreeOfParallelism;

    /// <summary>The parts of each crate: 4 gives 10,000 parts that push in every tick, and 0 none.</summary>
    [Params(0, 4)]
    public int PartsPerCrate;

    /// <summary>Pushes its entity, or as a part its chain root, with a fixed force and torque in every tick.</summary>
    private sealed class Jet(Vector3 force) : IComponent
    {
        public int Priority => 0;

        public EventMask Wants => EventMask.Of<Tick>();

        public void Handle(Self self, scoped ref EventList events, scoped ref Outbox outbox)
        {
            self.Body.AddForce(force);
            self.Body.AddTorque(force * 0.01f);
        }
    }

    [GlobalSetup]
    public void Setup()
    {
        var backend = new JoltPhysicsBackend(new() { WorkerThreads = 4 });
        world = new(
            new()
            {
                MaxDegreeOfParallelism = MaxDegreeOfParallelism,
                InitialCapacity = Crates * (PartsPerCrate + 1) + 1024,
                DisposePhysics = true,
            },
            backend
        );
        FrameHandle space = world.CreateRootFrame(new(FrameBounds.Sphere(1e7f)));
        var crate = new BodyDesc(Shape: new BoxShapeDesc(new(0.5f)), Mass: 10, LinearDamping: 0, AngularDamping: 0);
        for (int i = 0; i < Crates; i++)
        {
            Entity root = world.Spawn(
                new()
                {
                    Frame = space,
                    Pose = new(new(i % 50 * 4.0, i / 50 * 4.0, 0), DQuaternion.Identity),
                    Body = crate,
                    Components = PartsPerCrate == 0 ? [new Jet(new(0, 0, 1e-3f))] : [],
                }
            );
            for (int j = 0; j < PartsPerCrate; j++)
                world.Spawn(
                    new()
                    {
                        Frame = space,
                        AttachTo = root,
                        AttachOffset = new(new(0.5f * (j - 1.5f), 0.25f, -0.5f), Quaternion.Identity),
                        Components = [new Jet(new(1e-4f * (j + 1), 0, 1e-3f))],
                    }
                );
        }

        for (int i = 0; i < 20; i++)
            world.Tick();
        profile.Mark(world);
        SyncSampler.RemarkProfile = () => profile.Mark(world);
        SyncSampler.StateHash = () => WorldStateHash.Of(world);
    }

    [Benchmark]
    public void Tick() => world.Tick();

    [GlobalCleanup]
    public void Cleanup()
    {
        profile.Print(world, $"Parts{PartsPerCrate * Crates}-dop{MaxDegreeOfParallelism}");
        world.Dispose();
    }
}
