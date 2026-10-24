using System.Numerics;
using JoltPhysicsSharp;
using Tertius.Frames.Rails;
using Tertius.Simulation;
using static Tertius.Testing.WorldTesting;

namespace Tertius.Physics.Tests;

/// <summary>
/// A frame that turns in place with a <see cref="SpinMotion"/>, on the World and the real Jolt backend: its hull in the parent's system is kinematic and
/// turns with it, so it carries what rests on it, and a body that leaves it keeps the velocity of the ground under it.
/// </summary>
public sealed class WorldSpinFrameTests
{
    private const double Rate = 0.5;
    private static readonly FrameSpin Turning = new(Tilt: DQuaternion.Identity, Rate: Rate);

    private static Entity SpawnTurntable(World world, FrameHandle parent, float bubble, BodyDesc? hull = null,
        bool firstMove = false) => world.Spawn(new SpawnDesc
    {
        Frame = parent,
        Body = hull ?? new BodyDesc(Shape: new BoxShapeDesc(new(5, 3, 15)), Motion: BodyMotion.Kinematic,
            InteriorShape: new SphereShapeDesc(1)),
        AsFrame = new(FrameBounds.Sphere(bubble))
    }.Spinning(world, Turning, firstMove));

    [Fact]
    public void TheHullOfASpinFrameIsKinematicAndTurnsWithTheFrame()
    {
        World world = NewWorld(backend: out JoltPhysicsBackend backend);
        using (world)
        {
            FrameHandle space = world.CreateRootFrame(new(FrameBounds.Sphere(1e6f)));
            Entity owner = SpawnTurntable(world: world, parent: space, bubble: 50);

            Tick(world, 100);

            JoltPhysicsBackend.BodyFacts hull = backend.BodiesIn(space)
                .Single(facts =>
                    BodyUserData.Kind(facts.UserData) == BodyKind.Primary && BodyUserData.Names(facts.UserData, owner));
            hull.Motion.Should().Be(MotionType.Kinematic);
            var frame = world.PoseInOwnFrame(owner).Rotation.ToQuaternion();
            MathF.Abs(Quaternion.Dot(hull.Rotation, frame)).Should().BeGreaterThan(1 - 1e-5f,
                "the hull's rotation is the frame's, up to the sign of the quaternion");
            world.Errors.Should().BeEmpty();
        }
    }

    /// <summary>
    /// The game replaces the turntable's <see cref="SpinMotion"/> with one a quarter turn ahead. The new component's first
    /// run teleports the hull to its new rotation, so the hull never sweeps the quarter turn in one tick, which would spin it
    /// at about 94 rad/s for that tick. The old component's last sample turns it in the tick of the swap, so the swap has no
    /// gap (docs/plans/rails-components.md, "Changes of motion").
    /// </summary>
    [Fact]
    public void ANewSpinMotionPutsTheHullAtItsNewPoseInsteadOfSweepingItThere()
    {
        World world = NewWorld(backend: out JoltPhysicsBackend backend);
        using (world)
        {
            FrameHandle space = world.CreateRootFrame(new(FrameBounds.Sphere(1e6f)));
            Entity owner = SpawnTurntable(world: world, parent: space, bubble: 50);
            Tick(world, 10);
            FrameSpin ahead = Turning with { PhaseAtEpoch = Math.PI / 2 };

            JoltPhysicsBackend.BodyFacts Hull() => backend.BodiesIn(space)
                .Single(facts =>
                    BodyUserData.Kind(facts.UserData) == BodyKind.Primary && BodyUserData.Names(facts.UserData, owner));

            world.RemoveComponent(owner, world.GetComponent<SpinMotion>(owner)!);
            world.AddComponent(owner, new SpinMotion { Spin = ahead });
            Tick(world, 1);
            Hull().AngularVelocity.Y.Should()
                .BeApproximately((float)Rate, 0.01f, "the old component's last sample turns the hull");
            Tick(world, 1);

            Hull().AngularVelocity.Y.Should().BeApproximately((float)Rate, 0.01f);
            MathF.Abs(Quaternion.Dot(Hull().Rotation, world.PoseInOwnFrame(owner).Rotation.ToQuaternion())).Should()
                .BeGreaterThan(1 - 1e-5f);
            MathF.Abs(Quaternion.Dot(Hull().Rotation, ahead.RotationAt(world.Time).ToQuaternion())).Should()
                .BeGreaterThan(1 - 1e-5f, "the hull turns on the new spin");
            world.Errors.Should().BeEmpty();
        }
    }

    [Fact]
    public void AShipWhoseHullRestsOnTheHullOfASpinFrameIsCarriedRoundByIt()
    {
        World world = NewWorld(backend: out _);
        using (world)
        {
            FrameHandle ground = Ground(world, 9.81f);
            // Frame hulls touch only each other and static bodies, so the thing that rests on the platform's hull is the
            // hull of another frame: a small ship with a dynamic hull. Both bubbles are small, so neither frame enters
            // the other, and the ship's hull rests on the platform's hull in the ground frame.
            var platform = new BodyDesc(Shape: new BoxShapeDesc(new(10, 0.5f, 10)), Motion: BodyMotion.Kinematic,
                Friction: 1,
                InteriorShape: new SphereShapeDesc(0.05f));
            SpawnTurntable(world: world, parent: ground, bubble: 0.1f, hull: platform);
            Entity ship = world.Spawn(new()
            {
                Frame = ground,
                Pose = At(5, 1),
                Body = Cube() with { Friction = 1, InteriorShape = new SphereShapeDesc(0.05f) },
                AsFrame = new(FrameBounds.Sphere(0.1f))
            });

            Tick(world, 60);

            // After 1 s the platform has turned 0.5 rad, counterclockwise seen from above, so it carries the ship from +X
            // toward -Z. A static hull that only jumped to each new pose would give the ship no velocity.
            Double3 position = world.PoseInOwnFrame(ship).Position;
            double angle = Math.Atan2(-position.Z, position.X);
            angle.Should().BeInRange(0.35, 0.55, $"the ship is at {position}");
            world.Errors.Should().BeEmpty();
        }
    }

    /// <summary>
    /// The ball starts with the velocity of the carousel's ground under it plus a drift of 5 m/s outward, and leaves the
    /// carousel's bubble. Physics keeps a free body on its inertial line in every turning frame (docs/plans/spin-gravity.md,
    /// "The two-turn update"), so in space the ball moves on a straight line at one velocity from its second step, through
    /// the hand-off and after it. Before the rotation terms, the ball flew straight in the carousel's turning axes, and the
    /// test predicted its place at the hand-off by one step of its velocity there; with the rotation terms that prediction
    /// misses by the centrifugal and Coriolis terms times a step, 0.2 m/s. So the test now measures the ball's line in space.
    /// </summary>
    [Fact]
    public void ABallThatLeavesASpinFrameKeepsTheVelocityOfTheGroundItLeft()
    {
        World world = NewWorld(backend: out _);
        using (world)
        {
            FrameHandle space = world.CreateRootFrame(new(FrameBounds.Sphere(1e6f)));
            // The carousel's spawn carries its first motion intent, so it turns from the ball's first step, and the ball's
            // spawn velocity relative to it holds the velocity of the ground under it.
            FrameHandle carousel =
                world.GetOwnedFrame(SpawnTurntable(world: world, parent: space, bubble: 25, firstMove: true));
            // The ball drifts out along +Z of the carousel at 5 m/s. In space it moves at 5 m/s outward plus ω × r = 5 m/s
            // along the ground, on a straight line, and leaves the bubble after about 2.4 s.
            Entity ball = world.Spawn(new()
            {
                Frame = carousel,
                Pose = At(0, 0, 10),
                LinearVelocity = new Vector3(0, 0, 5),
                Body = new(Shape: new SphereShapeDesc(0.2f), Mass: 1, LinearDamping: 0, AngularDamping: 0)
            });
            // The bodies join their systems at the first sync point, so the ball's line in space is fixed from its second tick.
            Tick(world, 2);
            Double3 previous = world.PoseIn(ball, space).Position;
            Tick(world, 1);
            Double3 line = world.PoseIn(ball, space).Position;
            Double3 velocity = (line - previous) / world.DeltaTime;

            var placesInSpace = new List<Double3>();
            for (int tick = 0; tick < 300 && world.GetFrame(ball) == carousel; tick++)
            {
                world.Tick();
                placesInSpace.Add(world.PoseIn(ball, space).Position);
            }

            world.GetFrame(ball).Should().Be(space);
            int handOff = placesInSpace.Count;
            Tick(world, 30);
            placesInSpace.Add(world.PoseIn(ball, space).Position);

            // Every place lies on the line, also the place of the hand-off tick and 30 ticks after it.
            for (int k = 0; k < handOff; k++)
                Double3.Distance(placesInSpace[k], line + velocity * ((k + 1) * world.DeltaTime)).Should()
                    .BeLessThan(1e-4, $"after tick {k + 1}");
            Double3.Distance(placesInSpace[^1], line + velocity * ((handOff + 30) * world.DeltaTime)).Should()
                .BeLessThan(1e-3);
            // After the hand-off the ball's velocity in space is its velocity on the line: the hand-off carried the
            // velocity of the ground it left.
            (world.VelocityInOwnFrame(ball) - velocity).Length.Should().BeLessThan(1e-3);
            // The spin matters: the ball's speed in space is the drift of 5 m/s and ω × r of the ground at its spawn, 5 m/s,
            // at right angles. Without ω × r the hand-off would carry 5 m/s.
            velocity.Length.Should().BeApproximately(Math.Sqrt(50), 1e-3,
                "the ball's speed in space holds the ground's velocity under it");
            world.Errors.Should().BeEmpty();
        }
    }
}
