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
            state.Frame.Should().Be(ground);
            state.Pose.Position.Y.Should().BeInRange(StandingHeight - 0.01, StandingHeight + 0.05);
            state.Pose.Position.X.Should().BeApproximately(1, 1e-3);
            state.Pose.Position.Z.Should().BeApproximately(2, 1e-3);
            state.LinearVelocity.Length().Should().BeLessThan(0.2f);
            // Upright along the gravity's up direction.
            Vector3.Distance(state.Pose.Rotation.Rotate(Vector3.UnitY), Vector3.UnitY).Should().BeLessThan(1e-4f);
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
            walking.Pose.Position.X.Should().BeInRange(2.8, 3.1);
            walking.Pose.Position.Y.Should().BeInRange(StandingHeight - 0.01, StandingHeight + 0.05);
            walking.LinearVelocity.X.Should().BeApproximately(3, 0.05f);

            // A sideways push is not a new CharacterMove: the character keeps walking.
            h.Intent(person).Impulse = new Vector3(0, 0, 70);
            h.Step(30);
            (h[person].Pose.Position.X - walking.Pose.Position.X).Should().BeInRange(1.4, 1.6);

            h.Intent(person).Flags = IntentFlags.CharacterMove;
            h.Intent(person).CharacterVelocity = Vector3.Zero;
            h.Step(30);
            double x = h[person].Pose.Position.X;
            h.Step(30);
            h[person].Pose.Position.X.Should().BeApproximately(x, 1e-3);
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
            (peak - floor).Should().BeInRange(1.15, 1.35);
            h[person].Pose.Position.Y.Should().BeInRange(floor - 0.02, floor + 0.02);
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
            moved.X.Should().BeInRange(1.85, 2.1);
            moved.Y.Should().BeInRange(0.45, 0.55);
            (h[lift].Pose.Position - new Double3(2, 1.5, 0)).Length.Should().BeLessThan(1e-4);
            // The platform stops at its last target.
            h.Step(5);
            (h[lift].Pose.Position - new Double3(2, 1.5, 0)).Length.Should().BeLessThan(1e-4);
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
            (h[person].Pose.Position.Z - z).Should().BeInRange(1.9, 2.05);
            h[deck].Pose.Position.Z.Should().BeInRange(1.95, 2.05);
        }
    }

    [Fact]
    public void ImpulsesForcesAndSetVelocityMoveACharacterInTheAirByItsMass()
    {
        var (h, ground) = World();
        using (h)
        {
            // High above the floor, so the character is in the air and keeps its momentum.
            Entity person = h.AddBody(ground, Person, Shapes.At(0, 40, 0));
            h.Step();
            Vector3 falling = h[person].LinearVelocity;

            // 140 N·s and 4200 N for one step on 70 kg: +2 m/s along X and +1 m/s along Z.
            h.Intent(person).Impulse = new Vector3(140, 0, 0);
            h.Intent(person).Force = new Vector3(0, 0, 4200);
            h.Step();
            Vector3 pushed = h[person].LinearVelocity;
            pushed.X.Should().BeApproximately(2, 1e-3f);
            pushed.Z.Should().BeApproximately(1, 1e-3f);
            pushed.Y.Should().BeApproximately(falling.Y - 9.81f * PhysicsHarness.Dt, 1e-3f);

            // SetVelocity replaces the velocity; gravity then acts for the step.
            h.Intent(person).Flags = IntentFlags.SetVelocity;
            h.Intent(person).Velocity = new Vector3(0, 3, 0);
            h.Step();
            Vector3 thrown = h[person].LinearVelocity;
            thrown.X.Should().BeApproximately(0, 1e-3f);
            thrown.Z.Should().BeApproximately(0, 1e-3f);
            thrown.Y.Should().BeApproximately(3 - 9.81f * PhysicsHarness.Dt, 1e-3f);
        }
    }

    /// <summary>A frame with no gravity, and a person floating at its origin.</summary>
    private static (PhysicsHarness Harness, FrameHandle Space, Entity Person) Floating()
    {
        var h = new PhysicsHarness();
        FrameHandle space = h.AddFrame(FrameHandle.Null, DPose.Identity, FrameBounds.Sphere(1e6f), MotionDriver.Static);
        Entity person = h.AddBody(space, Person, Shapes.At(0, 0, 0));
        h.Step();
        return (h, space, person);
    }

    [Fact]
    public void InZeroGravityACharacterKeepsDriftingAtTheVelocityThatAPushGaveIt()
    {
        var (h, _, person) = Floating();
        using (h)
        {
            // 70 N·s on 70 kg: 1 m/s along X and 1 m/s along Y, in the step of the push and in every step after it.
            h.Intent(person).Impulse = new Vector3(70, 70, 0);
            h.Step(60);

            BodyState state = h[person];
            Vector3.Distance(state.LinearVelocity, new Vector3(1, 1, 0)).Should().BeLessThan(1e-3f);
            (state.Pose.Position - new Double3(1, 1, 0)).Length.Should().BeLessThan(1e-3);
        }
    }

    [Fact]
    public void InZeroGravityACharacterIgnoresItsDesiredVelocityAndItsJump()
    {
        var (h, _, person) = Floating();
        using (h)
        {
            h.Intent(person).Flags = IntentFlags.CharacterMove | IntentFlags.Jump;
            h.Intent(person).CharacterVelocity = new Vector3(0, 1, 1);
            h.Intent(person).JumpSpeed = 4;
            h.Step(60);

            h[person].Pose.Position.Length.Should().BeLessThan(1e-3);
            h[person].LinearVelocity.Should().Be(Vector3.Zero);
        }
    }

    [Fact]
    public void InZeroGravityAForceAcceleratesACharacterByItsMass()
    {
        var (h, _, person) = Floating();
        using (h)
        {
            // 105 N on 70 kg for one second: 1.5 m/s², so 1.5 m/s at the end. The step adds each step's change before it
            // moves, so the way is 1.5 m/s² × (1/60 s)² × (1 + 2 + … + 60) = 0.7625 m.
            for (int i = 0; i < 60; i++)
            {
                h.Intent(person).Force = new Vector3(0, 0, -105);
                h.Step();
            }

            h[person].LinearVelocity.Z.Should().BeApproximately(-1.5f, 1e-3f);
            h[person].Pose.Position.Z.Should().BeApproximately(-0.7625, 1e-3);
        }
    }

    [Fact]
    public void InZeroGravityACharacterThatHitsAWallLosesTheMotionIntoItAndSlidesAlongIt()
    {
        var (h, space, person) = Floating();
        using (h)
        {
            // A wall whose face is at x = 1.5. The capsule's side touches it when its center reaches x = 1.2.
            h.AddBody(space, new BodyDesc(new BoxShapeDesc(new Vector3(0.5f, 5, 5)), BodyMotion.Static), Shapes.At(2, 0, 0));
            h.Intent(person).Impulse = new Vector3(140, 0, 70);
            h.Step(60);

            BodyState sliding = h[person];
            sliding.Pose.Position.X.Should().BeInRange(1.15, 1.2);
            sliding.LinearVelocity.X.Should().BeApproximately(0, 0.01f, "the wall stopped the motion into it");
            sliding.LinearVelocity.Z.Should().BeApproximately(1, 0.01f, "the motion along the wall goes on");

            // A push away from the wall works at once: no motion into the wall is left to undo.
            h.Intent(person).Impulse = new Vector3(-70, 0, 0);
            h.Step();
            h[person].LinearVelocity.X.Should().BeApproximately(-1, 0.01f);
        }
    }

    [Fact]
    public void InZeroGravityACharacterThatClipsAnEdgeAboveItSlidesUnderTheEdge()
    {
        var (h, space, person) = Floating();
        using (h)
        {
            // A lintel from x = 1.5 whose lower face, at y = 0.8, is 0.1 m below the top of the capsule. The capsule meets the
            // lintel's lower front edge with its top, so nothing blocks it below the edge.
            h.AddBody(space, new BodyDesc(new BoxShapeDesc(new Vector3(1, 1, 5)), BodyMotion.Static), Shapes.At(2.5, 1.8, 0));
            h.Intent(person).Impulse = new Vector3(35, 0, 0);
            for (int i = 0; i < 300; i++)
            {
                h.Intent(person).Force = new Vector3(105, 0, 0);
                h.Step();
            }

            h[person].Pose.Position.X.Should().BeGreaterThan(3.5, "the edge turned the character down and under the lintel");
            h[person].LinearVelocity.X.Should().BeGreaterThan(1);
        }
    }

    [Fact]
    public void InZeroGravityAWallThatMovesIntoACharacterGivesItTheWallsVelocityAndTheCharacterKeepsIt()
    {
        var (h, space, person) = Floating();
        using (h)
        {
            // A kinematic wall whose face is at x = 0.5 moves toward the character at 1 m/s for one second, then stops.
            Entity wall = h.AddBody(space, new BodyDesc(new BoxShapeDesc(new Vector3(0.5f, 5, 5)), BodyMotion.Kinematic), Shapes.At(1, 0, 0));
            h.Step();
            h.Intent(wall).Flags = IntentFlags.SetVelocity;
            h.Intent(wall).Velocity = new Vector3(-1, 0, 0);
            h.Step(60);
            h.Intent(wall).Flags = IntentFlags.SetVelocity;
            h.Intent(wall).Velocity = Vector3.Zero;
            h.Step(60);

            h[person].LinearVelocity.X.Should().BeApproximately(-1, 0.01f, "the character drifts on at the speed that the wall gave it");
            h[person].Pose.Position.X.Should().BeLessThan(-1.2, "it moved with the wall, then away from it");
        }
    }

    [Fact]
    public void InZeroGravityACharacterKeepsItsMomentumAgainstALightDynamicBody()
    {
        var (h, space, person) = Floating();
        using (h)
        {
            // A 5 kg crate 1 m ahead. The character pushes it out of the way as it does in the air.
            Entity crate = h.AddBody(space, Shapes.Box(0.25f, mass: 5), Shapes.At(1.5, 0, 0));
            h.Intent(person).Impulse = new Vector3(70, 0, 0);
            h.Step(120);

            h[person].LinearVelocity.X.Should().BeApproximately(1, 0.01f);
            h[crate].LinearVelocity.X.Should().BeGreaterThan(0.9f);
        }
    }

    [Fact]
    public void InGravityBelowTheThresholdACharacterFallsLikeLooseCargo()
    {
        using var h = new PhysicsHarness();
        // 0.04 m/s², too weak to stand in, as when a ship thrusts gently.
        FrameHandle space = h.AddFrame(FrameHandle.Null, DPose.Identity, FrameBounds.Sphere(1e6f), MotionDriver.Static,
            GravityField.Uniform(new Vector3(0, 0, 0.04f)));
        Entity person = h.AddBody(space, Person, Shapes.At(0, 0, 0));
        Entity crate = h.AddBody(space, Shapes.Box(0.25f, mass: 5), Shapes.At(3, 0, 0));
        h.Step(60);

        h[person].LinearVelocity.Z.Should().BeApproximately(0.04f, 1e-4f);
        h[person].LinearVelocity.Z.Should().BeApproximately(h[crate].LinearVelocity.Z, 1e-4f);
        Vector3.Distance(h[person].Pose.Rotation.Rotate(Vector3.UnitY), Vector3.UnitY).Should().BeLessThan(1e-5f, "a weightless character keeps its up");
    }

    [Fact]
    public void InZeroGravityACharacterThatJoltPushesOutOfAWallGainsNoVelocity()
    {
        var (h, space, person) = Floating();
        using (h)
        {
            // The wall's face is at x = 0.28, so the capsule, 0.3 m in radius, sinks 2 cm into it.
            h.AddBody(space, new BodyDesc(new BoxShapeDesc(new Vector3(0.5f, 5, 5)), BodyMotion.Static), Shapes.At(0.78, 0, 0));
            h.Step(30);

            h[person].Pose.Position.X.Should().BeLessThan(-0.015, "Jolt pushed the character out of the wall");
            h[person].LinearVelocity.Length().Should().BeLessThan(1e-4f, "the push moved the character but gave it no momentum");
        }
    }

    [Fact]
    public void ACharacterThatWalksOutOfTheGravityKeepsDriftingAtItsWalkingVelocity()
    {
        using var h = new PhysicsHarness();
        // Gravity acts only where x < 0. The floor goes on to x = 1, so the character walks out of the gravity on the
        // floor and then off its edge.
        FrameHandle ground = h.AddFrame(FrameHandle.Null, DPose.Identity, FrameBounds.Sphere(1e6f), MotionDriver.Static,
            GravityField.Uniform(new Vector3(0, -9.81f, 0), new BoundingBox(new Vector3(-50), new Vector3(0, 50, 50))));
        h.AddBody(ground, new BodyDesc(new BoxShapeDesc(new Vector3(5, 0.5f, 5)), BodyMotion.Static), Shapes.At(-4, -0.5, 0));
        Entity person = h.AddBody(ground, Person, Shapes.At(-3, StandingHeight + 0.02, 0));
        h.Step(20);
        double height = h[person].Pose.Position.Y;
        h.Intent(person).Flags = IntentFlags.CharacterMove;
        h.Intent(person).CharacterVelocity = new Vector3(3, 0, 0);
        h.StepUntil(() => h[person].Pose.Position.X > 0, 90).Should().BeLessThan(90);
        h.Intent(person).Flags = IntentFlags.CharacterMove;
        h.Intent(person).CharacterVelocity = Vector3.Zero;
        h.Step(60);

        BodyState drifting = h[person];
        drifting.Pose.Position.X.Should().BeInRange(3, 3.1, "it drifts 3 m in the second after it left the gravity");
        drifting.Pose.Position.Y.Should().BeApproximately(height, 0.01, "nothing pulls it down out of the gravity");
        Vector3.Distance(drifting.LinearVelocity, new Vector3(3, 0, 0)).Should().BeLessThan(0.01f, "the floor stopped the step of gravity that pressed the character onto it");
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
        (state.Pose.Position.X - radius).Should().BeInRange(StandingHeight - 0.01, StandingHeight + 0.05);
        Vector3.Distance(state.Pose.Rotation.Rotate(Vector3.UnitY), Vector3.UnitX).Should().BeLessThan(1e-3f);
    }
}
