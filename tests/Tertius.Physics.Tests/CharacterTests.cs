using System.Numerics;
using Tertius.Simulation;
using static Tertius.Testing.WorldTesting;

namespace Tertius.Physics.Tests;

/// <summary>
/// How a character moves, on the World and the real Jolt backend. A component writes a command through
/// <see cref="BodyHandle"/> during a tick, and physics applies it in the step of the next tick. So a test sends a
/// command with <see cref="Send"/>, which ticks once, and ticks on to see what the command did. The step of the tick that
/// sends a command does not know the command yet.
/// </summary>
public class CharacterTests
{
    // The capsule center rests 0.9 m above the floor, plus Jolt's character padding.
    private const double StandingHeight = 0.9;

    private static readonly BodyDesc Person = new(Shape: new CapsuleShapeDesc(0.6f, 0.3f), Motion: BodyMotion.Character,
        Mass: 70);

    /// <summary>A dynamic cube with sides of <c>2 · half</c> m that nothing slows down and that does not bounce.</summary>
    private static BodyDesc Box(float half, float mass = 1f) =>
        new(Shape: new BoxShapeDesc(new(half)), Motion: BodyMotion.Dynamic, Mass: mass, Restitution: 0,
            LinearDamping: 0,
            AngularDamping: 0);

    private static BodyDesc StaticBox(Vector3 halfExtents) =>
        new(Shape: new BoxShapeDesc(halfExtents), Motion: BodyMotion.Static);

    /// <summary>A world with a root frame of 9.81 m/s² of gravity along -Y and a floor whose top is at y = 0.</summary>
    private static (World World, FrameHandle Ground) WorldWithFloor()
    {
        World world = NewWorld(backend: out _);
        FrameHandle ground = Ground(world, 9.81f);
        Floor(world, ground);
        return (world, ground);
    }

    /// <summary>
    /// Gives <paramref name="command"/> to the pilot and ticks once. The tick writes the command into the body's intent, so
    /// the step of that tick does not know it yet. The step of the next tick applies it.
    /// </summary>
    private static void Send(World world, Pilot pilot, BodyCommand command)
    {
        pilot.Once = command;
        world.Tick();
    }

    private static Entity SpawnPerson(World world, FrameHandle frame, DPose pose, Pilot? pilot = null) =>
        world.Spawn(new() { Frame = frame, Pose = pose, Body = Person, Components = pilot is null ? [] : [pilot] });

    private static Vector3 UpOf(World world, Entity entity) =>
        world.PoseInOwnFrame(entity).Rotation.Rotate(Vector3.UnitY);

    [Fact]
    public void StandsOnStaticFloor()
    {
        (World world, FrameHandle ground) = WorldWithFloor();
        using (world)
        {
            Entity person = SpawnPerson(world: world, frame: ground, pose: At(1, StandingHeight + 0.3, 2));
            Tick(world, 90);
            world.GetFrame(person).Should().Be(ground);
            DPose pose = world.PoseInOwnFrame(person);
            pose.Position.Y.Should().BeInRange(StandingHeight - 0.01, StandingHeight + 0.05);
            pose.Position.X.Should().BeApproximately(1, 1e-3);
            pose.Position.Z.Should().BeApproximately(2, 1e-3);
            world.VelocityInOwnFrame(person).Length().Should().BeLessThan(0.2f);
            // Upright along the gravity's up direction.
            Vector3.Distance(UpOf(world, person), Vector3.UnitY).Should().BeLessThan(1e-4f);
            world.Errors.Should().BeEmpty();
        }
    }

    [Fact]
    public void WalksWithCharacterMoveAndStops()
    {
        (World world, FrameHandle ground) = WorldWithFloor();
        using (world)
        {
            var pilot = new Pilot();
            Entity person = SpawnPerson(world: world, frame: ground, pose: At(0, StandingHeight + 0.02, 0),
                pilot: pilot);
            Tick(world, 10);
            // The vertical part of a desired velocity is ignored under gravity.
            Send(world, pilot, body => body.MoveCharacter(new(3, 5, 0)));
            Tick(world, 60);
            DPose walking = world.PoseInOwnFrame(person);
            walking.Position.X.Should().BeInRange(2.8, 3.1);
            walking.Position.Y.Should().BeInRange(StandingHeight - 0.01, StandingHeight + 0.05);
            world.VelocityInOwnFrame(person).X.Should().BeApproximately(3, 0.05f);

            // A sideways push is not a new CharacterMove: the character keeps walking.
            Send(world, pilot, body => body.AddImpulse(new(0, 0, 70)));
            double beforePush = world.PoseInOwnFrame(person).Position.X;
            Tick(world, 30);
            (world.PoseInOwnFrame(person).Position.X - beforePush).Should().BeInRange(1.4, 1.6);

            Send(world, pilot, body => body.MoveCharacter(Vector3.Zero));
            Tick(world, 30);
            double x = world.PoseInOwnFrame(person).Position.X;
            Tick(world, 30);
            world.PoseInOwnFrame(person).Position.X.Should().BeApproximately(x, 1e-3);
            world.Errors.Should().BeEmpty();
        }
    }

    [Fact]
    public void JumpsAndLandsAgain()
    {
        (World world, FrameHandle ground) = WorldWithFloor();
        using (world)
        {
            var pilot = new Pilot();
            Entity person = SpawnPerson(world: world, frame: ground, pose: At(0, StandingHeight + 0.02, 0),
                pilot: pilot);
            Tick(world, 20);
            double floor = world.PoseInOwnFrame(person).Position.Y;
            Send(world, pilot, body => body.Jump(5));
            double peak = floor;
            for (int i = 0; i < 90; i++)
            {
                world.Tick();
                peak = Math.Max(peak, world.PoseInOwnFrame(person).Position.Y);
            }

            // v² / 2g = 1.27 m.
            (peak - floor).Should().BeInRange(1.15, 1.35);
            world.PoseInOwnFrame(person).Position.Y.Should().BeInRange(floor - 0.02, floor + 0.02);
            world.Errors.Should().BeEmpty();
        }
    }

    [Fact]
    public void IsCarriedByKinematicPlatform()
    {
        (World world, FrameHandle ground) = WorldWithFloor();
        using (world)
        {
            var liftPilot = new SelfPilot();
            var platform = new BodyDesc(Shape: new BoxShapeDesc(new(2, 0.25f, 2)), Motion: BodyMotion.Kinematic);
            Entity lift = world.Spawn(new()
                { Frame = ground, Pose = At(0, 1, 0), Body = platform, Components = [liftPilot] });
            Entity person = SpawnPerson(world: world, frame: ground, pose: At(0, 1.25 + StandingHeight + 0.02, 0));
            Tick(world, 20);
            Double3 start = world.PoseInOwnFrame(person).Position;

            // The platform slides sideways and rises, driven by end poses: tick i writes the i-th of 60 end poses, and the
            // step of the next tick moves the platform there. The 61st tick applies the last end pose.
            int written = 0;
            liftPilot.EveryTick = self =>
            {
                if (written == 60)
                    return;
                written++;
                self.MoveKinematic(new(new(2.0 * written / 60, 1 + 0.5 * written / 60, 0), DQuaternion.Identity));
            };
            Tick(world, 61);

            Double3 moved = world.PoseInOwnFrame(person).Position - start;
            moved.X.Should().BeInRange(1.85, 2.1);
            moved.Y.Should().BeInRange(0.45, 0.55);
            (world.PoseInOwnFrame(lift).Position - new Double3(2, 1.5, 0)).Length.Should().BeLessThan(1e-4);
            // The platform stops at its last target.
            Tick(world, 5);
            (world.PoseInOwnFrame(lift).Position - new Double3(2, 1.5, 0)).Length.Should().BeLessThan(1e-4);
            world.Errors.Should().BeEmpty();
        }
    }

    [Fact]
    public void IsCarriedByPlatformMovingWithConstantVelocity()
    {
        (World world, FrameHandle ground) = WorldWithFloor();
        using (world)
        {
            var platform = new BodyDesc(Shape: new BoxShapeDesc(new(3, 0.25f, 3)), Motion: BodyMotion.Kinematic);
            Entity deck = world.Spawn(new() { Frame = ground, Pose = At(0, 1, 0), Body = platform });
            Entity person = SpawnPerson(world: world, frame: ground, pose: At(0, 1.25 + StandingHeight + 0.02, 0));
            Tick(world, 10);
            // A kinematic body moves only by its motion intents, so the deck drifts by end poses from the step after this tick.
            world.AddComponent(deck, new KinematicDrift(new(0, 0, 2)));
            world.Tick();
            double z = world.PoseInOwnFrame(person).Position.Z;
            Tick(world, 60);
            (world.PoseInOwnFrame(person).Position.Z - z).Should().BeInRange(1.9, 2.05);
            world.PoseInOwnFrame(deck).Position.Z.Should().BeInRange(1.95, 2.05);
            world.Errors.Should().BeEmpty();
        }
    }

    [Fact]
    public void ImpulsesForcesAndSetVelocityMoveACharacterInTheAirByItsMass()
    {
        (World world, FrameHandle ground) = WorldWithFloor();
        using (world)
        {
            var pilot = new Pilot();
            // High above the floor, so the character is in the air and keeps its momentum.
            Entity person = SpawnPerson(world: world, frame: ground, pose: At(0, 40, 0), pilot: pilot);
            world.Tick();

            // 140 N·s and 4200 N for one step on 70 kg: +2 m/s along X and +1 m/s along Z.
            Send(world, pilot, body =>
            {
                body.AddImpulse(new(140, 0, 0));
                body.AddForce(new(0, 0, 4200));
            });
            Vector3 falling = world.VelocityInOwnFrame(person);
            world.Tick();
            Vector3 pushed = world.VelocityInOwnFrame(person);
            pushed.X.Should().BeApproximately(2, 1e-3f);
            pushed.Z.Should().BeApproximately(1, 1e-3f);
            pushed.Y.Should().BeApproximately(falling.Y - 9.81f * world.DeltaTime, 1e-3f);

            // SetVelocity replaces the velocity; gravity then acts for the step.
            Send(world, pilot, body => body.SetVelocity(new(0, 3, 0)));
            world.Tick();
            Vector3 thrown = world.VelocityInOwnFrame(person);
            thrown.X.Should().BeApproximately(0, 1e-3f);
            thrown.Z.Should().BeApproximately(0, 1e-3f);
            // The set velocity is an instant velocity, and the velocity after the step is the chord of the step.
            thrown.Y.Should().BeApproximately(3 - 9.81f * world.DeltaTime / 2, 1e-3f);
            world.Errors.Should().BeEmpty();
        }
    }

    /// <summary>A frame with no gravity, and a person floating at its origin.</summary>
    private static (World World, FrameHandle Space, Entity Person, Pilot Pilot) Floating()
    {
        World world = NewWorld(backend: out _);
        FrameHandle space = world.CreateRootFrame(new(FrameBounds.Sphere(1e6f)));
        var pilot = new Pilot();
        Entity person = SpawnPerson(world: world, frame: space, pose: At(0, 0, 0), pilot: pilot);
        world.Tick();
        return (world, space, person, pilot);
    }

    [Fact]
    public void InZeroGravityACharacterKeepsDriftingAtTheVelocityThatAPushGaveIt()
    {
        (World world, _, Entity person, Pilot pilot) = Floating();
        using (world)
        {
            // 70 N·s on 70 kg: 1 m/s along X and 1 m/s along Y, in the step of the push and in every step after it.
            Send(world, pilot, body => body.AddImpulse(new(70, 70, 0)));
            Tick(world, 60);

            Vector3.Distance(world.VelocityInOwnFrame(person), new(1, 1, 0)).Should().BeLessThan(1e-3f);
            (world.PoseInOwnFrame(person).Position - new Double3(1, 1, 0)).Length.Should().BeLessThan(1e-3);
            world.Errors.Should().BeEmpty();
        }
    }

    [Fact]
    public void InZeroGravityItsDesiredVelocityAndItsJumpDoNotMoveACharacter()
    {
        (World world, _, Entity person, Pilot pilot) = Floating();
        using (world)
        {
            Send(world, pilot, body =>
            {
                body.MoveCharacter(new(0, 1, 1));
                body.Jump(4);
            });
            Tick(world, 60);

            world.PoseInOwnFrame(person).Position.Length.Should().BeLessThan(1e-3);
            world.VelocityInOwnFrame(person).Should().Be(Vector3.Zero);
            world.Errors.Should().BeEmpty();
        }
    }

    [Fact]
    public void InZeroGravityAForceAcceleratesACharacterByItsMass()
    {
        (World world, _, Entity person, Pilot pilot) = Floating();
        using (world)
        {
            // 105 N on 70 kg for one second: 1.5 m/s², so 1.5 m/s at the end. The step adds each step's change before it
            // moves, so the way is 1.5 m/s² × (1/60 s)² × (1 + 2 + … + 60) = 0.7625 m. The force of tick i acts in the step of
            // tick i + 1, so 61 ticks hold the 60 steps with a force.
            pilot.EveryTick = body => body.AddForce(new(0, 0, -105));
            Tick(world, 61);

            world.VelocityInOwnFrame(person).Z.Should().BeApproximately(-1.5f, 1e-3f);
            world.PoseInOwnFrame(person).Position.Z.Should().BeApproximately(-0.7625, 1e-3);
            world.Errors.Should().BeEmpty();
        }
    }

    /// <summary>
    /// A weightless character is a rigid body (its rigid form), so friction acts on it as on any body: against a wall with
    /// friction, the hit takes friction times the stopping impulse from its slide. The wall here has none, so the slide
    /// goes on whole.
    /// </summary>
    [Fact]
    public void InZeroGravityACharacterThatHitsAWallLosesTheMotionIntoItAndSlidesAlongIt()
    {
        (World world, FrameHandle space, Entity person, Pilot pilot) = Floating();
        using (world)
        {
            // A wall whose face is at x = 1.5. The capsule's side touches it when its center reaches x = 1.2.
            world.Spawn(new()
                { Frame = space, Pose = At(2, 0, 0), Body = StaticBox(new(0.5f, 5, 5)) with { Friction = 0 } });
            Send(world, pilot, body => body.AddImpulse(new(140, 0, 70)));
            Tick(world, 60);

            DPose sliding = world.PoseInOwnFrame(person);
            Vector3 slidingVelocity = world.VelocityInOwnFrame(person);
            sliding.Position.X.Should().BeInRange(1.15, 1.2);
            slidingVelocity.X.Should().BeApproximately(0, 0.01f, "the wall stopped the motion into it");
            slidingVelocity.Z.Should().BeApproximately(1, 0.01f, "the motion along the wall goes on");

            // A push away from the wall works at once: no motion into the wall is left to undo.
            Send(world, pilot, body => body.AddImpulse(new(-70, 0, 0)));
            world.Tick();
            world.VelocityInOwnFrame(person).X.Should().BeApproximately(-1, 0.01f);
            world.Errors.Should().BeEmpty();
        }
    }

    [Fact]
    public void InZeroGravityACharacterThatClipsAnEdgeAboveItSlidesUnderTheEdge()
    {
        (World world, FrameHandle space, Entity person, Pilot pilot) = Floating();
        using (world)
        {
            // A lintel from x = 1.5 whose lower face, at y = 0.8, is 0.1 m below the top of the capsule. The capsule meets the
            // lintel's lower front edge with its top, so nothing blocks it below the edge.
            world.Spawn(new() { Frame = space, Pose = At(2.5, 1.8, 0), Body = StaticBox(new(1, 1, 5)) });
            pilot.Once = body => body.AddImpulse(new(35, 0, 0));
            // The push and the force of tick i act in the step of tick i + 1, so 301 ticks hold 300 steps with a force.
            pilot.EveryTick = body => body.AddForce(new(105, 0, 0));
            Tick(world, 301);

            world.PoseInOwnFrame(person).Position.X.Should()
                .BeGreaterThan(3.5, "the edge turned the character down and under the lintel");
            world.VelocityInOwnFrame(person).X.Should().BeGreaterThan(1);
            world.Errors.Should().BeEmpty();
        }
    }

    [Fact]
    public void InZeroGravityAWallThatMovesIntoACharacterGivesItTheWallsVelocityAndTheCharacterKeepsIt()
    {
        (World world, FrameHandle space, Entity person, _) = Floating();
        using (world)
        {
            // A kinematic wall whose face is at x = 0.5 moves toward the character at 1 m/s for one second, then stops. A
            // kinematic body moves only by its motion intents, so the wall drifts by end poses.
            Entity wall = world.Spawn(new()
            {
                Frame = space,
                Pose = At(1, 0, 0),
                Body = new(Shape: new BoxShapeDesc(new(0.5f, 5, 5)), Motion: BodyMotion.Kinematic)
            });
            world.Tick();
            var drift = new KinematicDrift(new(-1, 0, 0));
            world.AddComponent(wall, drift);
            world.Tick();
            // The wall moves in the steps of 60 ticks, from the one that applies its first end pose. The drift is gone before
            // the tick after the last of them, so the step of that tick applies the last end pose, and the wall holds still
            // from the step after it.
            Tick(world, 59);
            world.RemoveComponent(wall, drift);
            world.Tick();
            Tick(world, 60);

            world.VelocityInOwnFrame(person).X.Should().BeApproximately(-1, 0.01f,
                "the character drifts on at the speed that the wall gave it");
            world.PoseInOwnFrame(person).Position.X.Should()
                .BeLessThan(-1.2, "it moved with the wall, then away from it");
            world.Errors.Should().BeEmpty();
        }
    }

    /// <summary>
    /// A weightless character is a rigid body (its rigid form), so a hit conserves momentum. Neither body bounces, so the two
    /// move on together: 70 kg at 1 m/s into 5 kg at rest leaves both at 70/75 m/s. Before the rigid form, the character kept
    /// its whole momentum and the crate gained some from nothing.
    /// </summary>
    [Fact]
    public void InZeroGravityACharacterThatHitsALightDynamicBodySharesItsMomentumWithIt()
    {
        (World world, FrameHandle space, Entity person, Pilot pilot) = Floating();
        using (world)
        {
            // A 5 kg crate 1 m ahead.
            Entity crate = world.Spawn(new() { Frame = space, Pose = At(1.5, 0, 0), Body = Box(0.25f, mass: 5) });
            Send(world, pilot, body => body.AddImpulse(new(70, 0, 0)));
            Tick(world, 120);

            const float shared = 70f / 75f;
            world.VelocityInOwnFrame(person).X.Should().BeApproximately(shared, 0.01f);
            world.VelocityInOwnFrame(crate).X.Should().BeApproximately(shared, 0.01f);
            world.Errors.Should().BeEmpty();
        }
    }

    [Fact]
    public void InGravityBelowTheThresholdACharacterFallsLikeLooseCargo()
    {
        using World world = NewWorld(backend: out _);
        // 0.04 m/s², too weak to stand in, as when a ship thrusts gently.
        FrameHandle space = world.CreateRootFrame(new(FrameBounds.Sphere(1e6f),
            GravityField.Uniform(new(0, 0, 0.04f))));
        Entity person = SpawnPerson(world: world, frame: space, pose: At(0, 0, 0));
        Entity crate = world.Spawn(new() { Frame = space, Pose = At(3, 0, 0), Body = Box(0.25f, mass: 5) });
        Tick(world, 60);

        // The character's velocity is the chord of the last step, the instant velocity half a step before its end.
        world.VelocityInOwnFrame(person).Z.Should().BeApproximately(0.04f * (1 - world.DeltaTime / 2), 1e-5f);
        world.VelocityInOwnFrame(person).Z.Should().BeApproximately(world.VelocityInOwnFrame(crate).Z, 1e-4f);
        Vector3.Distance(UpOf(world, person), Vector3.UnitY).Should()
            .BeLessThan(1e-5f, "a weightless character keeps its up");
        world.Errors.Should().BeEmpty();
    }

    [Fact]
    public void InZeroGravityACharacterThatJoltPushesOutOfAWallGainsNoVelocity()
    {
        (World world, FrameHandle space, Entity person, _) = Floating();
        using (world)
        {
            // The wall's face is at x = 0.28, so the capsule, 0.3 m in radius, sinks 2 cm into it.
            world.Spawn(new() { Frame = space, Pose = At(0.78, 0, 0), Body = StaticBox(new(0.5f, 5, 5)) });
            Tick(world, 30);

            world.PoseInOwnFrame(person).Position.X.Should()
                .BeLessThan(-0.015, "Jolt pushed the character out of the wall");
            world.VelocityInOwnFrame(person).Length().Should()
                .BeLessThan(1e-4f, "the push moved the character but gave it no momentum");
            world.Errors.Should().BeEmpty();
        }
    }

    [Fact]
    public void ACharacterThatWalksOutOfTheGravityKeepsDriftingAtItsWalkingVelocity()
    {
        using World world = NewWorld(backend: out _);
        // Gravity acts only where x < 0. The floor goes on to x = 1, so the character walks out of the gravity on the
        // floor and then off its edge.
        FrameHandle ground = world.CreateRootFrame(new(FrameBounds.Sphere(1e6f),
            GravityField.Uniform(new(0, -9.81f, 0), new BoundingBox(new(-50), new(0, 50, 50)))));
        world.Spawn(new() { Frame = ground, Pose = At(-4, -0.5, 0), Body = StaticBox(new(5, 0.5f, 5)) });
        var pilot = new Pilot();
        Entity person = SpawnPerson(world: world, frame: ground, pose: At(-3, StandingHeight + 0.02, 0), pilot: pilot);
        Tick(world, 20);
        double height = world.PoseInOwnFrame(person).Position.Y;
        Send(world, pilot, body => body.MoveCharacter(new(3, 0, 0)));
        TickUntil(world, () => world.PoseInOwnFrame(person).Position.X > 0, 90).Should().BeLessThan(90);
        // The step of this tick still walks, because the command to stop acts in the step of the next tick.
        Send(world, pilot, body => body.MoveCharacter(Vector3.Zero));
        double leftTheGravity = world.PoseInOwnFrame(person).Position.X;
        Tick(world, 60);

        DPose drifting = world.PoseInOwnFrame(person);
        (drifting.Position.X - leftTheGravity).Should()
            .BeApproximately(3, 0.01, "it drifts 3 m in the second after it left the gravity");
        drifting.Position.Y.Should().BeApproximately(height, 0.01, "nothing pulls it down out of the gravity");
        Vector3.Distance(world.VelocityInOwnFrame(person), new(3, 0, 0)).Should().BeLessThan(0.01f,
            "the floor stopped the step of gravity that pressed the character onto it");
        world.Errors.Should().BeEmpty();
    }

    [Fact]
    public void StandsOnPlanetFarFromOrigin()
    {
        using World world = NewWorld(backend: out _);
        const double radius = 6_371_000, mu = 3.986004418e14;
        FrameHandle planet = world.CreateRootFrame(new(FrameBounds.Sphere(1e9f), GravityField.Point(mu)));
        // Stand on the side of the planet along +X, where up is +X.
        DQuaternion slabRotation = DQuaternion.FromAxisAngle(Double3.UnitZ, -Math.PI / 2);
        world.Spawn(new()
        {
            Frame = planet,
            Pose = new(new(radius - 0.5, 0, 0), slabRotation),
            Body = StaticBox(new(20, 0.5f, 20))
        });
        Entity person = SpawnPerson(world: world, frame: planet,
            pose: new(new(radius + StandingHeight + 0.1, 0, 0), DQuaternion.Identity));
        Tick(world, 60);
        DPose pose = world.PoseInOwnFrame(person);
        (pose.Position.X - radius).Should().BeInRange(StandingHeight - 0.01, StandingHeight + 0.05);
        Vector3.Distance(UpOf(world, person), Vector3.UnitX).Should().BeLessThan(1e-3f);
        world.Errors.Should().BeEmpty();
    }
}
