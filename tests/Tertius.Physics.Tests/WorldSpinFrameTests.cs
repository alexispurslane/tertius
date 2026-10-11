using System.Numerics;
using JoltPhysicsSharp;
using Tertius.Simulation;
using static Tertius.Physics.Tests.WorldTesting;

namespace Tertius.Physics.Tests;

/// <summary>
/// A frame with the Spin driver on the World and the real Jolt backend: its hull in the parent's system is kinematic and
/// turns with it, so it carries what rests on it, and a body that leaves it keeps the velocity of the ground under it.
/// </summary>
public class WorldSpinFrameTests
{
    private const double Rate = 0.5;
    private static readonly FrameSpin Turning = new(DQuaternion.Identity, Rate);

    private static Entity SpawnTurntable(World world, FrameHandle parent, float bubble, BodyDesc? hull = null) => world.Spawn(new SpawnDesc
    {
        Frame = parent,
        Body = hull ?? new BodyDesc(new BoxShapeDesc(new Vector3(5, 3, 15)), BodyMotion.Static, InteriorShape: new SphereShapeDesc(1)),
        AsFrame = new FrameSpec(FrameBounds.Sphere(bubble), MotionDriver.Spin, Spin: Turning),
    });

    [Fact]
    public void TheHullOfASpinFrameIsKinematicAndTurnsWithTheFrame()
    {
        World world = NewWorld(out JoltPhysicsBackend backend);
        using (world)
        {
            FrameHandle space = world.CreateRootFrame(new FrameSpec(FrameBounds.Sphere(1e6f), MotionDriver.Static));
            Entity owner = SpawnTurntable(world, space, bubble: 50);

            Tick(world, 100);

            JoltPhysicsBackend.BodyFacts hull = backend.BodiesIn(space)
                .Single(facts => BodyUserData.Kind(facts.UserData) == BodyKind.Primary && BodyUserData.Names(facts.UserData, owner));
            hull.Motion.Should().Be(MotionType.Kinematic);
            Quaternion frame = world.GetPose(owner).Rotation.ToQuaternion();
            MathF.Abs(Quaternion.Dot(hull.Rotation, frame)).Should().BeGreaterThan(1 - 1e-5f, "the hull's rotation is the frame's, up to the sign of the quaternion");
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
            // hull of another frame: a small ship with the physics driver. Both bubbles are small, so neither frame enters
            // the other, and the ship's hull rests on the platform's hull in the ground frame.
            var platform = new BodyDesc(new BoxShapeDesc(new Vector3(10, 0.5f, 10)), BodyMotion.Static, Friction: 1, InteriorShape: new SphereShapeDesc(0.05f));
            SpawnTurntable(world, ground, bubble: 0.1f, platform);
            Entity ship = world.Spawn(new SpawnDesc
            {
                Frame = ground,
                Pose = At(5, 1),
                Body = Cube() with { Friction = 1, InteriorShape = new SphereShapeDesc(0.05f) },
                AsFrame = new FrameSpec(FrameBounds.Sphere(0.1f), MotionDriver.Physics),
            });

            Tick(world, 60);

            // After 1 s the platform has turned 0.5 rad, counterclockwise seen from above, so it carries the ship from +X
            // toward -Z. A static hull that only jumped to each new pose would give the ship no velocity.
            Double3 position = world.GetPose(ship).Position;
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
            FrameHandle space = world.CreateRootFrame(new FrameSpec(FrameBounds.Sphere(1e6f), MotionDriver.Static));
            FrameHandle carousel = world.GetOwnedFrame(SpawnTurntable(world, space, bubble: 25));
            // The ball drifts out along +Z of the carousel at 5 m/s and leaves the bubble after about 3 s.
            Entity ball = world.Spawn(new SpawnDesc
            {
                Frame = carousel,
                Pose = At(0, 0, 10),
                LinearVelocity = new Vector3(0, 0, 5),
                Body = new BodyDesc(new SphereShapeDesc(0.2f), Mass: 1, LinearDamping: 0, AngularDamping: 0),
            });

            DPose before = world.GetPose(ball);
            Vector3 velocityBefore = world.GetVelocity(ball);
            for (int tick = 0; tick < 300 && world.GetFrame(ball) == carousel; tick++)
            {
                before = world.GetPose(ball);
                velocityBefore = world.GetVelocity(ball);
                world.Tick();
            }
            world.GetFrame(ball).Should().Be(space);

            // The tree has the carousel at the end of the hand-off tick, so it converts the ball's last motion exactly: the
            // velocity in the carousel, turned into the parent's axes, plus ω × r of the ground at the ball's place.
            Double3 position = before.Position + (Double3)velocityBefore * world.DeltaTime;
            Double3 expected = world.Frames.VelocityToParent(carousel, position, velocityBefore);
            ((Double3)world.GetVelocity(ball) - expected).Length.Should().BeLessThan(0.01);
            // The spin matters: ω × r is 0.5 rad/s times about 25 m.
            (expected - world.Frames.LocalPose(carousel).TransformDirection((Double3)velocityBefore)).Length.Should().BeGreaterThan(10);
            world.Errors.Should().BeEmpty();
        }
    }
}
