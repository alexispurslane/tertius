using BenchmarkDotNet.Attributes;
using JoltPhysicsSharp;
using Tertius.Physics;
using Tertius.Simulation;

namespace Tertius.Benchmarks;

/// <summary>
/// The cost of the child ghosts of static bodies in the ghost pass (ter-imi5, docs/plans/complete-ghosts.md, "Cost"). One
/// tick of a station, a root frame with a gravity of 1 g, whose floor is a static slab of 200 m by 160 m. On the floor
/// stand <see cref="Props"/> static boxes of 1 m, in 40 rows of 50, 4 m apart. <see cref="DockedShips"/> parked ships, 48 m
/// apart along X, each a static hull of 4 m by 2 m by 10 m that owns a bubble of radius 10 m, stand 1 m above the floor.
/// The system of a semantic frame with a child bubble reads the position of each static member once per step and measures
/// its distance to each child bubble, so with 2,000 props and three ships the pass reads 2,000 positions and measures
/// 6,000 distances in each step. Without a ship, the station has no child bubble, and the pass skips its static members.
/// The slab and the 22 props whose spheres reach a ship's bubble have a ghost in that ship. Every body and every ghost
/// holds still, so no Jolt system steps, and the tick measures the engine around Jolt. Without a ship, the props cost the
/// tick what every body costs outside the ghost pass, mostly in the numeric update, which gathers every body. What the
/// ships add with the props, beyond what they add without them, is the work of the ghost pass on the static props.
/// </summary>
[MemoryDiagnoser]
public class StaticBodyGhostBenchmarks
{
    [Params(0, 2000)] public int Props;

    [Params(0, 3)] public int DockedShips;

    private World world = null!;

    /// <summary>
    /// The ghosts in each ship: the slab's, and those of the props whose bounding spheres, of radius 0.87 m, reach the ship's
    /// bubble. Below each ship's origin, the props stand at 0, ±4 m and ±8 m along X and at ±2 m, ±6 m and ±10 m along Z.
    /// Of those, the 22 props within 10.87 m of the origin reach the bubble.
    /// </summary>
    private int GhostsPerShip => Props == 0 ? 1 : 23;

    [GlobalSetup]
    public void Setup()
    {
        var backend = new JoltPhysicsBackend(new() { WorkerThreads = 2 });
        world = new(new() { MaxDegreeOfParallelism = 1, DisposePhysics = true }, backend);
        GravityField gravity = GravityField.Uniform(new(0, -9.81f, 0));
        FrameHandle station = world.CreateRootFrame(new(FrameBounds.Sphere(1e6f), gravity));
        world.Spawn(new()
        {
            Frame = station, Pose = At(0, -0.5, 0), Body = new(new BoxShapeDesc(new(100, 0.5f, 80)), BodyMotion.Static)
        });
        var prop = new BodyDesc(new BoxShapeDesc(new(0.5f)), BodyMotion.Static);
        for (int i = 0; i < Props; i++)
            world.Spawn(new() { Frame = station, Pose = At(i % 50 * 4 - 98, 0.5, i / 50 * 4 - 78), Body = prop });
        var ships = new FrameHandle[DockedShips];
        for (int s = 0; s < DockedShips; s++)
            ships[s] = world.GetOwnedFrame(world.Spawn(new()
            {
                Frame = station,
                Pose = At(s * 48 - 50, 2, 0),
                Body = new(new BoxShapeDesc(new(2, 1, 5)), BodyMotion.Static,
                    InteriorShape: new BoxShapeDesc(new(1.8f, 0.1f, 4.8f))),
                AsFrame = new(FrameBounds.Sphere(10), gravity)
            }));
        // One second: the first ghost pass makes the ghosts, and the later ones find them in place.
        for (int i = 0; i < 60; i++)
            world.Tick();

        foreach (FrameHandle ship in ships)
        {
            List<JoltPhysicsBackend.BodyFacts> ghosts =
                [.. backend.BodiesIn(ship).Where(body => BodyUserData.Kind(body.UserData) == BodyKind.Ghost)];
            if (ghosts.Count != GhostsPerShip || ghosts.Any(ghost => ghost.Motion != MotionType.Static))
                throw new InvalidOperationException(
                    $"A ship holds {ghosts.Count} ghosts, not {GhostsPerShip} static ones.");
        }

        Console.WriteLine(DockedShips == 0
            ? "// No ship is docked, so the station has no child bubble."
            : $"// Each of {DockedShips} ships holds {GhostsPerShip} static ghost{(GhostsPerShip == 1 ? "" : "s")} of the station's static bodies.");
    }

    private static DPose At(double x, double y, double z) => new(new(x, y, z), DQuaternion.Identity);

    [Benchmark]
    public void Tick() => world.Tick();

    [GlobalCleanup]
    public void Cleanup() => world.Dispose();
}
