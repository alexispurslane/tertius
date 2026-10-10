using System.Numerics;

namespace Tertius.Physics.Tests;

public class CharacterTests
{
    private static readonly BodyDesc Person = new(new CapsuleShapeDesc(0.6f, 0.3f), BodyMotion.Character, Mass: 70);

    // The capsule center rests 0.9 m above the floor, plus Jolt's character padding.
    private const double StandingHeight = 0.9;

    private static (PhysicsHarness Harness, FrameHandle Ground) World()
    {
        var h = new PhysicsHarness();
        FrameHandle ground = h.AddFrame(FrameHandle.Null, DPose.Identity, FrameBounds.Sphere(1e6f), MotionDriver.Static,
            GravityField.Uniform(new Vector3(0, -9.81f, 0)));
        h.AddBody(ground, Shapes.Floor(50), Shapes.At(0, -0.5, 0));
        return (h, ground);
    }

    [Fact]
    public void StandsOnStaticFloor()
    {
        var (h, ground) = World();
        using (h)
        {
            Entity person = h.AddBody(ground, Person, Shapes.At(1, StandingHeight + 0.3, 2));
            h.Step(90);
            BodyState state = h[person];
            Assert.Equal(ground, state.Frame);
            Assert.InRange(state.Pose.Position.Y, StandingHeight - 0.01, StandingHeight + 0.05);
            Assert.True(Math.Abs(state.Pose.Position.X - 1) < 1e-3 && Math.Abs(state.Pose.Position.Z - 2) < 1e-3);
            Assert.True(state.LinearVelocity.Length() < 0.2f, state.LinearVelocity.ToString());
            // Upright along the gravity's up direction.
            Assert.True(Vector3.Distance(state.Pose.Rotation.Rotate(Vector3.UnitY), Vector3.UnitY) < 1e-4f);
        }
    }

    [Fact]
    public void WalksWithCharacterMoveAndStops()
    {
        var (h, ground) = World();
        using (h)
        {
            Entity person = h.AddBody(ground, Person, Shapes.At(0, StandingHeight + 0.02, 0));
            h.Step(10);
            h.Intent(person).Flags = IntentFlags.CharacterMove;
            // The vertical part of a desired velocity is ignored under gravity.
            h.Intent(person).CharacterVelocity = new Vector3(3, 5, 0);
            h.Step(60);
            BodyState walking = h[person];
            Assert.InRange(walking.Pose.Position.X, 2.8, 3.1);
            Assert.InRange(walking.Pose.Position.Y, StandingHeight - 0.01, StandingHeight + 0.05);
            Assert.Equal(3, walking.LinearVelocity.X, 1);

            h.Intent(person).Flags = IntentFlags.CharacterMove;
            h.Intent(person).CharacterVelocity = Vector3.Zero;
            h.Step(30);
            double x = h[person].Pose.Position.X;
            h.Step(30);
            Assert.True(Math.Abs(h[person].Pose.Position.X - x) < 1e-3);
        }
    }

    [Fact]
    public void JumpsAndLandsAgain()
    {
        var (h, ground) = World();
        using (h)
        {
            Entity person = h.AddBody(ground, Person, Shapes.At(0, StandingHeight + 0.02, 0));
            h.Step(20);
            double floor = h[person].Pose.Position.Y;
            h.Intent(person).Flags = IntentFlags.Jump;
            h.Intent(person).JumpSpeed = 5;
            double peak = floor;
            for (int i = 0; i < 90; i++)
            {
                h.Step();
                peak = Math.Max(peak, h[person].Pose.Position.Y);
            }
            // v² / 2g = 1.27 m.
            Assert.InRange(peak - floor, 1.15, 1.35);
            Assert.InRange(h[person].Pose.Position.Y, floor - 0.02, floor + 0.02);
        }
    }

    [Fact]
    public void IsCarriedByKinematicPlatform()
    {
        var (h, ground) = World();
        using (h)
        {
            var platform = new BodyDesc(new BoxShapeDesc(new Vector3(2, 0.25f, 2)), BodyMotion.Kinematic);
            Entity lift = h.AddBody(ground, platform, Shapes.At(0, 1, 0));
            Entity person = h.AddBody(ground, Person, Shapes.At(0, 1.25 + StandingHeight + 0.02, 0));
            h.Step(20);
            Double3 start = h[person].Pose.Position;

            // The platform slides sideways and rises, driven by kinematic targets.
            for (int i = 1; i <= 60; i++)
            {
                h.Intent(lift).Flags = IntentFlags.KinematicTarget;
                h.Intent(lift).Target = new DPose(new Double3(2.0 * i / 60, 1 + 0.5 * i / 60, 0), DQuaternion.Identity);
                h.Step();
            }

            Double3 moved = h[person].Pose.Position - start;
            Assert.InRange(moved.X, 1.85, 2.1);
            Assert.InRange(moved.Y, 0.45, 0.55);
            Assert.True((h[lift].Pose.Position - new Double3(2, 1.5, 0)).Length < 1e-4);
            // The platform stops at its last target.
            h.Step(5);
            Assert.True((h[lift].Pose.Position - new Double3(2, 1.5, 0)).Length < 1e-4);
        }
    }

    [Fact]
    public void IsCarriedByPlatformMovingWithConstantVelocity()
    {
        var (h, ground) = World();
        using (h)
        {
            var platform = new BodyDesc(new BoxShapeDesc(new Vector3(3, 0.25f, 3)), BodyMotion.Kinematic);
            Entity deck = h.AddBody(ground, platform, Shapes.At(0, 1, 0));
            Entity person = h.AddBody(ground, Person, Shapes.At(0, 1.25 + StandingHeight + 0.02, 0));
            h.Step(10);
            h.Intent(deck).Flags = IntentFlags.SetVelocity;
            h.Intent(deck).Velocity = new Vector3(0, 0, 2);
            double z = h[person].Pose.Position.Z;
            h.Step(60);
            Assert.InRange(h[person].Pose.Position.Z - z, 1.9, 2.05);
            Assert.InRange(h[deck].Pose.Position.Z, 1.95, 2.05);
        }
    }

    [Fact]
    public void ZeroGravityMovementIsThreeDimensional()
    {
        using var h = new PhysicsHarness();
        FrameHandle space = h.AddFrame(FrameHandle.Null, DPose.Identity, FrameBounds.Sphere(1e6f), MotionDriver.Static);
        Entity person = h.AddBody(space, Person, Shapes.At(0, 0, 0));
        h.Intent(person).Flags = IntentFlags.CharacterMove;
        h.Intent(person).CharacterVelocity = new Vector3(0, 1, 1);
        h.Step(60);
        Assert.True((h[person].Pose.Position - new Double3(0, 1, 1)).Length < 0.05, h[person].Pose.Position.ToString());
    }

    [Fact]
    public void StandsOnPlanetFarFromOrigin()
    {
        using var h = new PhysicsHarness();
        const double radius = 6_371_000, mu = 3.986004418e14;
        FrameHandle planet = h.AddFrame(FrameHandle.Null, DPose.Identity, FrameBounds.Sphere(1e9f), MotionDriver.Static, GravityField.Point(mu));
        // Stand on the side of the planet along +X, where up is +X.
        var slabRotation = DQuaternion.FromAxisAngle(Double3.UnitZ, -Math.PI / 2);
        h.AddBody(planet, Shapes.Floor(20), new DPose(new Double3(radius - 0.5, 0, 0), slabRotation));
        Entity person = h.AddBody(planet, Person, new DPose(new Double3(radius + StandingHeight + 0.1, 0, 0), DQuaternion.Identity));
        h.Step(60);
        BodyState state = h[person];
        Assert.InRange(state.Pose.Position.X - radius, StandingHeight - 0.01, StandingHeight + 0.05);
        Assert.True(Vector3.Distance(state.Pose.Rotation.Rotate(Vector3.UnitY), Vector3.UnitX) < 1e-3f);
    }
}
