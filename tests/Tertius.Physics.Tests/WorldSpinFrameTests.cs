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
public class WorldSpinFrameTests
{
    private const double Rate = 0.5;
    private static readonly FrameSpin Turning = new(DQuaternion.Identity, Rate);

    private static Entity SpawnTurntable(World world, FrameHandle parent, float bubble, BodyDesc? hull = null) => world.Spawn(new SpawnDesc
    {
        Frame = parent,
        Body = hull ?? new BodyDesc(new BoxShapeDesc(new Vector3(5, 3, 15)), BodyMotion.Kinematic, InteriorShape: new SphereShapeDesc(1)),
        AsFrame = new FrameSpec(FrameBounds.Sphere(bubble)),
    }.Spinning(world, Turning));

    [Fact]
    public void TheHullOfASpinFrameIsKinematicAndTurnsWithTheFrame()
    {
        World world = NewWorld(out JoltPhysicsBackend backend);
        using (world)
        {
            FrameHandle space = world.CreateRootFrame(new FrameSpec(FrameBounds.Sphere(1e6f)));
            Entity owner = SpawnTurntable(world, space, bubble: 50);

            Tick(world, 100);

            JoltPhysicsBackend.BodyFacts hull = backend.BodiesIn(space)
                .Single(facts => BodyUserData.Kind(facts.UserData) == BodyKind.Primary && BodyUserData.Names(facts.UserData, owner));
            hull.Motion.Should().Be(MotionType.Kinematic);
            Quaternion frame = world.PoseInOwnFrame(owner).Rotation.ToQuaternion();
            MathF.Abs(Quaternion.Dot(hull.Rotation, frame)).Should().BeGreaterThan(1 - 1e-5f, "the hull's rotation is the frame's, up to the sign of the quaternion");
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
        World world = NewWorld(out JoltPhysicsBackend backend);
        using (world)
        {
            FrameHandle space = world.CreateRootFrame(new FrameSpec(FrameBounds.Sphere(1e6f)));
            Entity owner = SpawnTurntable(world, space, bubble: 50);
            Tick(world, 10);
            FrameSpin ahead = Turning with { PhaseAtEpoch = Math.PI / 2 };
            JoltPhysicsBackend.BodyFacts Hull() => backend.BodiesIn(space)
                .Single(facts => BodyUserData.Kind(facts.UserData) == BodyKind.Primary && BodyUserData.Names(facts.UserData, owner));

            world.RemoveComponent(owner, world.GetComponent<SpinMotion>(owner)!);
            world.AddComponent(owner, new SpinMotion { Spin = ahead });
            Tick(world, 1);
            Hull().AngularVelocity.Y.Should().BeApproximately((float)Rate, 0.01f, "the old component's last sample turns the hull");
            Tick(world, 1);

            Hull().AngularVelocity.Y.Should().BeApproximately((float)Rate, 0.01f);
            MathF.Abs(Quaternion.Dot(Hull().Rotation, world.PoseInOwnFrame(owner).Rotation.ToQuaternion())).Should().BeGreaterThan(1 - 1e-5f);
            MathF.Abs(Quaternion.Dot(Hull().Rotation, ahead.RotationAt(world.Time).ToQuaternion())).Should().BeGreaterThan(1 - 1e-5f, "the hull turns on the new spin");
            world.Errors.Should().BeEmpty();
        }
    }

    [Fact]
    public void AShipWhoseHullRestsOnTheHullOfASpinFrameIsCarriedRoundByIt()
    {
        World world = NewWorld(out _);
        using (world)
        {
            FrameHandle ground = Ground(world, 9.81f);
            // Frame hulls touch only each other and static bodies, so the thing that rests on the platform's hull is the
            // hull of another frame: a small ship with a dynamic hull. Both bubbles are small, so neither frame enters
            // the other, and the ship's hull rests on the platform's hull in the ground frame.
            var platform = new BodyDesc(new BoxShapeDesc(new Vector3(10, 0.5f, 10)), BodyMotion.Kinematic, Friction: 1, InteriorShape: new SphereShapeDesc(0.05f));
            SpawnTurntable(world, ground, bubble: 0.1f, platform);
            Entity ship = world.Spawn(new SpawnDesc
            {
                Frame = ground,
                Pose = At(5, 1),
                Body = Cube() with { Friction = 1, InteriorShape = new SphereShapeDesc(0.05f) },
                AsFrame = new FrameSpec(FrameBounds.Sphere(0.1f)),
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

    [Fact]
    public void ABallThatLeavesASpinFrameKeepsTheVelocityOfTheGroundItLeft()
    {
        World world = NewWorld(out _);
        using (world)
        {
            FrameHandle space = world.CreateRootFrame(new FrameSpec(FrameBounds.Sphere(1e6f)));
            FrameHandle carousel = world.GetOwnedFrame(SpawnTurntable(world, space, bubble: 25));
            // The ball drifts out along +Z of the carousel at 5 m/s and leaves the bubble after about 3 s.
            Entity ball = world.Spawn(new SpawnDesc
            {
                Frame = carousel,
                Pose = At(0, 0, 10),
                LinearVelocity = new Vector3(0, 0, 5),
                Body = new BodyDesc(new SphereShapeDesc(0.2f), Mass: 1, LinearDamping: 0, AngularDamping: 0),
            });

            DPose before = world.PoseInOwnFrame(ball);
            Vector3 velocityBefore = world.VelocityInOwnFrame(ball);
            for (int tick = 0; tick < 300 && world.GetFrame(ball) == carousel; tick++)
            {
                before = world.PoseInOwnFrame(ball);
                velocityBefore = world.VelocityInOwnFrame(ball);
                world.Tick();
            }
            world.GetFrame(ball).Should().Be(space);

            // The tree has the carousel at the end of the hand-off tick, so it converts the ball's last motion exactly: the
            // velocity in the carousel, turned into the parent's axes, plus ω × r of the ground at the ball's place.
            Double3 position = before.Position + (Double3)velocityBefore * world.DeltaTime;
            Double3 expected = world.Frames.VelocityToParent(carousel, position, velocityBefore);
            ((Double3)world.VelocityInOwnFrame(ball) - expected).Length.Should().BeLessThan(0.01);
            // The spin matters: ω × r is 0.5 rad/s times about 25 m.
            (expected - world.Frames.LocalPose(carousel).TransformDirection((Double3)velocityBefore)).Length.Should().BeGreaterThan(10);
            world.Errors.Should().BeEmpty();
        }
    }
}
