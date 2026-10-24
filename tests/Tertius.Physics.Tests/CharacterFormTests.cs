using System.Numerics;
using Tertius.Simulation;
using static Tertius.Testing.ContactNews;
using static Tertius.Testing.NumericFrameTesting;
using static Tertius.Testing.WorldTesting;
using static Tertius.Testing.WorldTestingGrowth;

namespace Tertius.Physics.Tests;

/// <summary>
/// The two forms of a character (ticket ter-1w06, design B), on the World and the real Jolt backend. In the walking form a
/// character is a Jolt CharacterVirtual. When the effective gravity at it falls below
/// <see cref="FrameTree.ZeroGravityThreshold"/>, physics turns it into the rigid form: a dynamic Jolt body with the
/// character's capsule and mass, whose rotation is locked, so that Jolt's solver handles its contacts as it handles those of
/// any rigid body. When the gravity rises well above the threshold again, or above it while the character touches
/// something, the character walks again. A force or a command
/// that a component writes during a tick acts in the physics step of the next tick, and a change of a frame's gravity
/// between two ticks acts from the step of the next tick on.
/// </summary>
public sealed class CharacterFormTests
{
    private const float PersonMass = 70;
    private const float CrateMass = 20;

    /// <summary>The thrust with which a test presses a character into a crate, in newtons.</summary>
    private const float Thrust = 105;

    /// <summary>
    /// The height of a standing character's center above the floor, in m: the capsule's half height and radius. A Jolt
    /// character rests its capsule on a floor, but keeps 0.02 m from a wall at its side.
    /// </summary>
    private const double StandingHeight = 0.9;

    private static readonly BodyDesc Person = new(
        Shape: new CapsuleShapeDesc(0.6f, 0.3f),
        Motion: BodyMotion.Character,
        Mass: PersonMass
    );

    /// <summary>A dynamic crate of 1 m by 2 m by 1 m and 20 kg that nothing slows down, so that a standing character pushes it at its middle.</summary>
    private static readonly BodyDesc TallCrate = new(
        Shape: new BoxShapeDesc(new(0.5f, 1, 0.5f)),
        Mass: CrateMass,
        LinearDamping: 0,
        AngularDamping: 0
    );

    /// <summary>A dynamic cube with sides of 1 m and 20 kg that nothing slows down.</summary>
    private static readonly BodyDesc Crate = new(
        Shape: new BoxShapeDesc(new(0.5f)),
        Mass: CrateMass,
        LinearDamping: 0,
        AngularDamping: 0
    );

    private static readonly Vector3 Down = new(0, -9.81f, 0);

    /// <summary>A uniform gravity of 0.08 m/s² along -Y, between the two thresholds of the forms, about that of a small moon.</summary>
    private static readonly GravityField BandGravity = GravityField.Uniform(new(0, -0.08f, 0));

    private static FrameHandle Space(World world) => world.CreateRootFrame(new(FrameBounds.Sphere(1e6f)));

    private static Entity SpawnPerson(
        World world,
        FrameHandle frame,
        DPose pose,
        Double3 velocity = default,
        params IComponent[] components
    ) =>
        world.Spawn(
            new()
            {
                Frame = frame,
                Pose = pose,
                LinearVelocity = velocity,
                Body = Person,
                Components = components,
            }
        );

    private static Vector3 UpOf(World world, Entity entity) =>
        world.PoseInOwnFrame(entity).Rotation.Rotate(Vector3.UnitY);

    /// <summary>Counts the steps in which a pilot's thrust acted: each call of the pilot acts in the step of the next tick.</summary>
    private sealed class ThrustCounter
    {
        private int calls;

        /// <summary>How many steps the thrust has acted in, after the last tick.</summary>
        public int Steps { get; private set; }

        public void Start(Pilot pilot, Vector3 force) =>
            pilot.EveryTick = body =>
            {
                body.AddForce(force);
                calls++;
            };

        public void Tick(World world)
        {
            Steps = calls;
            world.Tick();
        }
    }

    /// <summary>The path in m of a pair of <paramref name="mass"/> kg that a thrust of <see cref="Thrust"/> pushes from rest, by the step rule.</summary>
    private static double PairPath(World world, ThrustCounter thrust, int ticks, float mass)
    {
        // Each step adds the thrust's velocity change before it moves the pair.
        double dt = world.DeltaTime,
            path = 0;
        for (int step = 1; step <= thrust.Steps && step <= ticks; step++)
            path += Thrust / mass * step * dt * dt;
        return path;
    }

    [Fact]
    public void ACharacterThatEntersZeroGravityPushesACrate()
    {
        using World world = NewWorld(backend: out JoltPhysicsBackend backend);
        FrameHandle ground = Ground(world, 9.81f);
        Floor(world, ground);
        var pilot = new Pilot();
        Entity person = SpawnPerson(
            world: world,
            frame: ground,
            pose: At(0, StandingHeight + 0.03),
            components: [pilot]
        );
        // The crate stands on the floor, its face 0.02 m ahead of the capsule's side: the gap that a Jolt character keeps.
        Entity crate = world.Spawn(
            new()
            {
                Frame = ground,
                Pose = At(0.3 + 0.02 + 0.5, 1),
                Body = TallCrate,
            }
        );
        Tick(world, 60);
        world.Snapshot.IsStanding(person).Should().BeTrue("the character stands on the floor beside the crate");

        world.SetFrameGravity(ground, GravityField.None);
        world.Tick();
        backend.HasRigidForm(person).Should().BeTrue("the gravity is gone, so the character is weightless");
        double crateStart = world.PoseIn(crate, ground).Position.X;
        var thrust = new ThrustCounter();
        thrust.Start(pilot, new(Thrust, 0, 0));
        for (int tick = 0; tick < 121; tick++)
            thrust.Tick(world);

        double pairPath = PairPath(world: world, thrust: thrust, ticks: 120, mass: PersonMass + CrateMass);
        (world.PoseIn(crate, ground).Position.X - crateStart)
            .Should()
            .BeGreaterThan(
                0.8 * pairPath,
                $"a pair of 90 kg under {Thrust} N moves {pairPath:F2} m, and the weightless character pushes the crate along"
            );
        world.Errors.Should().BeEmpty();
    }

    [Fact]
    public void ACharacterThatLeavesZeroGravityStandsAndWalksAgain()
    {
        using World world = NewWorld(backend: out JoltPhysicsBackend backend);
        FrameHandle ground = world.CreateRootFrame(new(FrameBounds.Sphere(1e6f)));
        Floor(world, ground);
        var pilot = new Pilot();
        Entity person = SpawnPerson(world: world, frame: ground, pose: At(0, StandingHeight), components: [pilot]);
        Tick(world, 30);
        backend.HasRigidForm(person).Should().BeTrue("the frame has no gravity");

        world.SetFrameGravity(ground, GravityField.Uniform(Down));
        Tick(world, 10);
        backend.HasRigidForm(person).Should().BeFalse("the gravity came back");
        world.Snapshot.IsStanding(person).Should().BeTrue("the character stands on the floor again");
        pilot.Once = body => body.MoveCharacter(new(2, 0, 0));
        world.Tick();
        double start = world.PoseIn(person, ground).Position.X;
        Tick(world, 60);

        (world.PoseIn(person, ground).Position.X - start).Should().BeApproximately(2, 0.05, "it walks at 2 m/s");
        world.PoseIn(person, ground).Position.Y.Should().BeApproximately(StandingHeight, 0.01, "it walks on the floor");
        world.Errors.Should().BeEmpty();
    }

    [Fact]
    public void ASwitchOfFormKeepsTheCharactersContactsWithoutEndingAndStartingThem()
    {
        using World world = NewWorld(backend: out JoltPhysicsBackend backend);
        FrameHandle ground = Ground(world, 9.81f);
        Entity floor = Floor(world, ground);
        var log = new CollisionLog();
        Entity person = SpawnPerson(world: world, frame: ground, pose: At(0, StandingHeight + 0.03), components: [log]);
        Tick(world, 60);
        log.NewsOf(floor).Should().Equal([Started], "the character landed on the floor");

        world.SetFrameGravity(ground, GravityField.None);
        Tick(world, 60);
        backend.HasRigidForm(person).Should().BeTrue();
        world.SetFrameGravity(ground, GravityField.Uniform(Down));
        Tick(world, 60);
        backend.HasRigidForm(person).Should().BeFalse();

        log.NewsOf(floor).Should().Equal([Started], "the character touched the floor the whole time, in both forms");
        world
            .PoseIn(person, ground)
            .Position.Y.Should()
            .BeApproximately(StandingHeight, 0.01, "it still stands where it stood");
        world.Errors.Should().BeEmpty();
    }

    [Fact]
    public void ACharacterKeepsItsPlaceOnAFloorAndBesideAWallThroughBothSwitches()
    {
        using World world = NewWorld(backend: out JoltPhysicsBackend backend);
        FrameHandle ground = Ground(world, 9.81f);
        Floor(world, ground);
        // A wall whose face is at x = 1.5.
        world.Spawn(
            new()
            {
                Frame = ground,
                Pose = At(2, 2),
                Body = new(Shape: new BoxShapeDesc(new(0.5f, 2, 5)), Motion: BodyMotion.Static),
            }
        );
        var pilot = new Pilot();
        Entity person = SpawnPerson(
            world: world,
            frame: ground,
            pose: At(0.5, StandingHeight + 0.03),
            components: [pilot]
        );
        Tick(world, 20);
        pilot.Once = body => body.MoveCharacter(new(1, 0, 0));
        Tick(world, 60);
        pilot.Once = body => body.MoveCharacter(Vector3.Zero);
        Tick(world, 10);
        DPose walking = world.PoseIn(person, ground);
        walking.Position.Y.Should().BeApproximately(StandingHeight, 1e-4, "the character stands on the floor");
        walking
            .Position.X.Should()
            .BeApproximately(1.5 - 0.3 - 0.02, 1e-4, "it walked into the wall, which it keeps 0.02 m from");

        world.SetFrameGravity(ground, GravityField.None);
        for (int tick = 0; tick < 30; tick++)
        {
            world.Tick();
            (world.PoseIn(person, ground).Position - walking.Position)
                .Length.Should()
                .BeLessThan(1e-4, $"the rigid form does not move off the floor or the wall, tick {tick}");
        }

        backend.HasRigidForm(person).Should().BeTrue();

        // A thrust presses the rigid form into the wall, where it rests when the thrust stops.
        pilot.EveryTick = body => body.AddForce(new(70, 0, 0));
        Tick(world, 10);
        pilot.EveryTick = null;
        Tick(world, 30);
        DPose resting = world.PoseIn(person, ground);
        world.SetFrameGravity(ground, GravityField.Uniform(Down));
        for (int tick = 0; tick < 30; tick++)
        {
            world.Tick();
            (world.PoseIn(person, ground).Position - resting.Position)
                .Length.Should()
                .BeLessThan(1e-3, $"the walking form does not jump off the wall, tick {tick}");
        }

        backend.HasRigidForm(person).Should().BeFalse();
        world.Errors.Should().BeEmpty();
    }

    /// <summary>The path in m along X of a character that walks at 1 m/s on a floor for 60 ticks, from the tick after the walk.</summary>
    private static double WalkedPath(World world, Entity person, Pilot pilot, FrameHandle frame)
    {
        pilot.Once = body => body.MoveCharacter(new(1, 0, 0));
        world.Tick();
        double start = world.PoseIn(person, frame).Position.X;
        Tick(world, 60);
        return world.PoseIn(person, frame).Position.X - start;
    }

    [Fact]
    public void ACharacterThatLiesOnAFloorWhenAWeakGravityComesStandsAndWalksAsOneThatSpawnedInThatGravity()
    {
        using World world = NewWorld(backend: out JoltPhysicsBackend backend);
        FrameHandle ship = Space(world);
        Floor(world, ship);
        var pilot = new Pilot();
        Entity person = SpawnPerson(world: world, frame: ship, pose: At(0, StandingHeight), components: [pilot]);
        Tick(world, 10);
        backend
            .HasRigidForm(person)
            .Should()
            .BeTrue("the ship has no gravity yet, so the character lies weightless on the floor");

        // The drive starts with a thrust whose gravity lies between the two thresholds.
        world.SetFrameGravity(ship, BandGravity);
        Tick(world, 120);

        world
            .Snapshot.IsStanding(person)
            .Should()
            .BeTrue("the gravity is strong enough to stand a character, and the character lies on the floor");
        WalkedPath(world: world, person: person, pilot: pilot, frame: ship)
            .Should()
            .BeApproximately(1, 0.05, "it walks at 1 m/s, as one that spawned in this gravity does");
        world.Errors.Should().BeEmpty();
    }

    [Fact]
    public void ACharacterThatFellAsleepOnAFloorWhenAWeakGravityComesStandsAndWalks()
    {
        using World world = NewWorld(backend: out JoltPhysicsBackend backend);
        FrameHandle ship = Space(world);
        Floor(world, ship);
        var pilot = new Pilot();
        Entity person = SpawnPerson(world: world, frame: ship, pose: At(0, StandingHeight), components: [pilot]);
        Tick(world, 120);
        backend
            .HasRigidForm(person)
            .Should()
            .BeTrue("the ship has no gravity yet, so the character lies weightless on the floor");
        backend
            .BodiesIn(ship)
            .Single(facts =>
                BodyUserData.Kind(facts.UserData) == BodyKind.Primary && BodyUserData.Names(facts.UserData, person)
            )
            .Active.Should()
            .BeFalse("a rigid character that lies still falls asleep");

        world.SetFrameGravity(ship, BandGravity);
        Tick(world, 1);
        backend
            .HasRigidForm(person)
            .Should()
            .BeFalse(
                "its contact with the floor counts while it sleeps, so it walks again in the first step, as an awake character does"
            );
        Tick(world, 119);

        world.Snapshot.IsStanding(person).Should().BeTrue();
        WalkedPath(world: world, person: person, pilot: pilot, frame: ship).Should().BeApproximately(1, 0.05);
        world.Errors.Should().BeEmpty();
    }

    [Fact]
    public void ACharacterThatSpawnedInAWeakGravityStandsAndWalks()
    {
        using World world = NewWorld(backend: out JoltPhysicsBackend backend);
        FrameHandle moon = world.CreateRootFrame(new(FrameBounds.Sphere(1e6f), BandGravity));
        Floor(world, moon);
        var pilot = new Pilot();
        Entity person = SpawnPerson(world: world, frame: moon, pose: At(0, StandingHeight), components: [pilot]);
        Tick(world, 120);

        backend.HasRigidForm(person).Should().BeFalse("a walking character keeps its form between the two thresholds");
        world.Snapshot.IsStanding(person).Should().BeTrue();
        WalkedPath(world: world, person: person, pilot: pilot, frame: moon).Should().BeApproximately(1, 0.05);
        world.Errors.Should().BeEmpty();
    }

    [Fact]
    public void ASwitchToTheRigidFormEndsNoContactWithAWallThatTheWalkingFormStillHeld()
    {
        using World world = NewWorld(backend: out JoltPhysicsBackend backend);
        FrameHandle ground = Ground(world, 9.81f);
        Floor(world, ground);
        // A wall whose face is at x = 1.5.
        Entity wall = world.Spawn(
            new()
            {
                Frame = ground,
                Pose = At(2, 2),
                Body = new(Shape: new BoxShapeDesc(new(0.5f, 2, 5)), Motion: BodyMotion.Static),
            }
        );
        var pilot = new Pilot();
        var log = new CollisionLog();
        Entity person = SpawnPerson(
            world: world,
            frame: ground,
            pose: At(0.5, StandingHeight + 0.03),
            components: [pilot, log]
        );
        Tick(world, 20);
        pilot.Once = body => body.MoveCharacter(new(1, 0, 0));
        Tick(world, 60);
        // The character steps back from the wall, 0.05 m in three steps at 1 m/s, and stands.
        pilot.Once = body => body.MoveCharacter(new(-1, 0, 0));
        Tick(world, 3);
        pilot.Once = body => body.MoveCharacter(Vector3.Zero);
        Tick(world, 10);
        double gap = 1.5 - 0.3 - world.PoseIn(person, ground).Position.X;
        gap.Should().BeInRange(0.06, 0.08, "the character stands about 0.07 m from the wall");
        log.NewsOf(wall)
            .Should()
            .Equal([Started], "the walking form holds the contact of each body within 0.1 m of it");

        world.SetFrameGravity(ground, GravityField.None);
        Tick(world, 30);
        backend.HasRigidForm(person).Should().BeTrue();

        log.NewsOf(wall).Should().Equal([Started], "the switch moved nothing, so the contact goes on");
        (1.5 - 0.3 - world.PoseIn(person, ground).Position.X)
            .Should()
            .BeApproximately(gap, 1e-4, "the character has not moved");

        // An impulse sends the rigid form away from the wall at 0.5 m/s. Its side is 0.05 m from the wall, and the contact
        // ends once it is 0.1 m away, as for the walking form, which takes about six ticks.
        pilot.Once = body => body.AddImpulse(new(-0.5f * PersonMass, 0, 0));
        world.Tick();
        Tick(world, 2);
        log.NewsOf(wall).Should().Equal([Started], "the character is still within 0.1 m of the wall");
        Tick(world, 27);
        (1.5 - 0.3 - world.PoseIn(person, ground).Position.X).Should().BeGreaterThan(0.2);
        log.NewsOf(wall).Should().Equal([Started, Ended], "the character left the wall, so the contact ended once");
        world.Errors.Should().BeEmpty();
    }

    [Fact]
    public void CharactersOfTwoDescriptionsWithOneCapsuleShareOneNativeCapsuleInTheRigidForm()
    {
        using World world = NewWorld(backend: out JoltPhysicsBackend backend);
        FrameHandle space = Space(world);
        // A game that builds a new body description for each spawn around one shared capsule, as the demo scenes do.
        var capsule = new CapsuleShapeDesc(0.6f, 0.3f);
        Entity first = world.Spawn(
            new()
            {
                Frame = space,
                Pose = At(0),
                Body = new(Shape: capsule, Motion: BodyMotion.Character, Mass: PersonMass),
            }
        );
        Entity second = world.Spawn(
            new()
            {
                Frame = space,
                Pose = At(5),
                Body = new(Shape: capsule, Motion: BodyMotion.Character, Mass: PersonMass),
            }
        );
        world.Tick();
        backend.HasRigidForm(first).Should().BeTrue();
        backend.HasRigidForm(second).Should().BeTrue();

        List<JoltPhysicsBackend.BodyFacts> bodies = backend.BodiesIn(space);
        nint ShapeOf(Entity person) => bodies.Single(body => BodyUserData.Names(body.UserData, person)).Shape;
        ShapeOf(second)
            .Should()
            .Be(ShapeOf(first), "the two characters have one capsule, so their rigid forms share one native capsule");
        world.Errors.Should().BeEmpty();
    }

    [Fact]
    public void ACharacterWhoseGravityHoversAroundTheThresholdSwitchesOnceAndKeepsItsForm()
    {
        using World world = NewWorld(backend: out JoltPhysicsBackend backend);
        FrameHandle space = world.CreateRootFrame(
            new(FrameBounds.Sphere(1e6f), GravityField.Uniform(new(0, -0.06f, 0)))
        );
        Entity person = SpawnPerson(world: world, frame: space, pose: At(0));
        world.Tick();
        backend.HasRigidForm(person).Should().BeFalse("0.06 m/s² is above the threshold of 0.05 m/s²");

        var forms = new List<bool>();
        for (int tick = 0; tick < 60; tick++)
        {
            world.SetFrameGravity(space, GravityField.Uniform(new(0, tick % 2 == 0 ? -0.04f : -0.06f, 0)));
            world.Tick();
            forms.Add(backend.HasRigidForm(person));
        }

        forms
            .Should()
            .AllSatisfy(
                rigid => rigid.Should().BeTrue(),
                "the character became rigid at the first tick below the threshold, and 0.06 m/s² is too weak to make it walk again"
            );
        world.Errors.Should().BeEmpty();
    }

    [Theory]
    [InlineData(BodyMotion.Character, 0.0f, false, BodyMotion.Dynamic)]
    [InlineData(BodyMotion.Character, 0.0f, true, BodyMotion.Dynamic)]
    [InlineData(BodyMotion.Character, 0.0499f, true, BodyMotion.Dynamic)]
    [InlineData(BodyMotion.Character, 0.05f, false, BodyMotion.Character)]
    [InlineData(BodyMotion.Character, 9.81f, false, BodyMotion.Character)]
    [InlineData(BodyMotion.Dynamic, 0.0f, false, BodyMotion.Dynamic)]
    [InlineData(BodyMotion.Dynamic, 0.05f, false, BodyMotion.Dynamic)]
    [InlineData(BodyMotion.Dynamic, 0.0999f, false, BodyMotion.Dynamic)]
    [InlineData(BodyMotion.Dynamic, 0.1f, false, BodyMotion.Character)]
    [InlineData(BodyMotion.Dynamic, 9.81f, false, BodyMotion.Character)]
    [InlineData(BodyMotion.Dynamic, 0.0f, true, BodyMotion.Dynamic)]
    [InlineData(BodyMotion.Dynamic, 0.0499f, true, BodyMotion.Dynamic)]
    [InlineData(BodyMotion.Dynamic, 0.05f, true, BodyMotion.Character)]
    [InlineData(BodyMotion.Dynamic, 0.0999f, true, BodyMotion.Character)]
    [InlineData(BodyMotion.Dynamic, 9.81f, true, BodyMotion.Character)]
    public void AWalkingCharacterBecomesRigidBelowTheThresholdAndARigidOneWalksAgainFromTheThresholdWhenItTouchesSomethingAndFromTwiceTheThresholdOtherwise(
        BodyMotion form,
        float gravity,
        bool touches,
        BodyMotion next
    ) => JoltPhysicsBackend.NextCharacterMotion(form, gravity, touches).Should().Be(next);

    [Fact]
    public void AWeightlessCharacterHandsOffIntoAnotherFramesBubbleAndKeepsItsMotion()
    {
        using World world = NewWorldWithFaults(
            faults: out FaultInjectingBackend faults,
            configure: options => options.NumericFrames = WithoutNumericFrames
        );
        FrameHandle space = Space(world);
        FrameHandle bay = world.GetOwnedFrame(
            world.Spawn(
                new()
                {
                    Frame = space,
                    Pose = At(40),
                    AsFrame = new(FrameBounds.Sphere(10)),
                }
            )
        );
        // The capsule's bounding sphere has a radius of 0.9 m.
        Scenes.Approach approach = Scenes.ApproachToBubble(
            world: world,
            into: bay,
            tick: 30,
            ballRadius: 0.9f,
            speed: 6,
            lateral: 1
        );
        Entity person = SpawnPerson(world: world, frame: space, pose: approach.Pose, velocity: approach.Velocity);

        var path = new List<Double3>();
        for (int tick = 0; tick < 60; tick++)
        {
            world.Tick();
            path.Add(world.PoseIn(person, space).Position);
        }

        faults
            .Outputs.SelectMany(output => output.HandOffs)
            .Should()
            .ContainSingle(handOff => handOff.Entity == person && handOff.To == bay);
        world.GetFrame(person).Should().Be(bay);
        faults.Jolt.HasRigidForm(person).Should().BeTrue("the bay has no gravity either");
        double step = 6 * world.DeltaTime;
        for (int i = 1; i < path.Count; i++)
            (path[i] - path[i - 1] - new Double3(step, 0, 0))
                .Length.Should()
                .BeLessThan(1e-6, $"the character drifts on at 6 m/s in tick {i}");
        (world.VelocityIn(person, space) - new Double3(6, 0, 0)).Length.Should().BeLessThan(1e-5);
        world.Errors.Should().BeEmpty();
    }

    [Fact]
    public void AWeightlessCharacterKeepsItsFormAndMotionThroughASystemGrowth()
    {
        using World world = NewWorldWithFaultsAndSmallSystems(
            maxBodiesPerFrame: 8,
            faults: out FaultInjectingBackend faults
        );
        FrameHandle space = Space(world);
        var velocity = new Double3(1, 0.5, 0);
        Entity person = SpawnPerson(world: world, frame: space, pose: At(0), velocity: velocity);
        Tick(world, 2);
        faults.Jolt.HasRigidForm(person).Should().BeTrue();
        DPose before = world.PoseIn(person, space);

        for (int i = 0; i < 8; i++)
            world.Spawn(
                new()
                {
                    Frame = space,
                    Pose = At(3 * i - 12, -100),
                    Body = Crate,
                }
            );
        Tick(world, 30);

        faults.Jolt.CapacityOf(space).Should().Be(16, "the crates filled the system, so it grew");
        faults.Jolt.HasRigidForm(person).Should().BeTrue("the growth keeps the character's form");
        JoltPhysicsBackend.BodyFacts facts = faults
            .Jolt.BodiesIn(space)
            .Single(body => BodyUserData.Names(body.UserData, person));
        facts.InverseInertia.Should().Be(Vector3.Zero, "the rigid form cannot turn");
        facts.InverseMass.Should().BeApproximately(1 / PersonMass, 1e-7f);
        (world.PoseIn(person, space).Position - before.Position - velocity * (30 * world.DeltaTime))
            .Length.Should()
            .BeLessThan(1e-6, "the character drifts on at its velocity");
        world.Errors.Should().BeEmpty();
    }

    [Fact]
    public void AWeightlessCharacterInANumericFramePushesACrate()
    {
        using World world = NewWorld(backend: out JoltPhysicsBackend backend);
        FrameHandle space = Space(world);
        // A character always thrusts, so at 100 m/s together the two get a falling frame of their own: a group with a body
        // that thrusts falls from 50 m/s (NumericFrameOptions.ThrustCreateSpeed).
        var drift = new Double3(0, 0, 100);
        var pilot = new Pilot();
        Entity person = SpawnPerson(world: world, frame: space, pose: At(0), velocity: drift, pilot);
        Entity crate = world.Spawn(
            new()
            {
                Frame = space,
                Pose = At(0.3 + 0.02 + 0.5),
                LinearVelocity = drift,
                Body = Crate,
            }
        );
        Tick(world, 2);
        world.Frames.Numeric(world.PhysicsHomeOf(person)).Should().Be(NumericMode.Falling);
        world.PhysicsHomeOf(crate).Should().Be(world.PhysicsHomeOf(person));
        backend.HasRigidForm(person).Should().BeTrue();
        double crateStart = world.PoseIn(crate, space).Position.X;
        var thrust = new ThrustCounter();
        thrust.Start(pilot, new(Thrust, 0, 0));
        for (int tick = 0; tick < 121; tick++)
            thrust.Tick(world);

        double pairPath = PairPath(world: world, thrust: thrust, ticks: 120, mass: PersonMass + CrateMass);
        (world.PoseIn(crate, space).Position.X - crateStart)
            .Should()
            .BeGreaterThan(
                0.8 * pairPath,
                $"a pair of 90 kg under {Thrust} N moves {pairPath:F2} m, also in a numeric frame"
            );
        world
            .Frames.Numeric(world.PhysicsHomeOf(crate))
            .Should()
            .Be(NumericMode.Falling, "the push happened in the falling frame");
        world.PhysicsHomeOf(person).Should().Be(world.PhysicsHomeOf(crate));
        world.Errors.Should().BeEmpty();
    }

    [Fact]
    public void ThePoseAndVelocityOfACharacterAreContinuousAcrossBothSwitches()
    {
        using World world = NewWorld(backend: out JoltPhysicsBackend backend);
        FrameHandle space = Space(world);
        Entity person = SpawnPerson(world: world, frame: space, pose: At(0), velocity: new(1, 0, 0.5));
        Tick(world, 10);
        backend.HasRigidForm(person).Should().BeTrue();
        float dt = world.DeltaTime;

        // Each tick around a switch: the step adds its gravity to the velocity, then moves the character by the velocity.
        void ExpectOneStep(Vector3 gravity, string because)
        {
            DPose before = world.PoseIn(person, space);
            Double3 velocity = world.VelocityIn(person, space);
            world.Tick();
            Double3 expected = velocity + gravity * dt;
            (world.VelocityIn(person, space) - expected).Length.Should().BeLessThan(1e-5, because);
            (world.PoseIn(person, space).Position - before.Position - expected * dt)
                .Length.Should()
                .BeLessThan(1e-6, because);
            MathF
                .Abs(Quaternion.Dot(world.PoseIn(person, space).Rotation.ToJolt(), before.Rotation.ToJolt()))
                .Should()
                .BeApproximately(1, 1e-6f, because);
        }

        world.SetFrameGravity(space, GravityField.Uniform(Down));
        ExpectOneStep(Down, "the step in which the character starts to walk again");
        backend.HasRigidForm(person).Should().BeFalse();
        ExpectOneStep(Down, "the step after it");
        world.SetFrameGravity(space, GravityField.None);
        ExpectOneStep(Vector3.Zero, "the step in which the character becomes rigid again");
        backend.HasRigidForm(person).Should().BeTrue();
        ExpectOneStep(Vector3.Zero, "the step after it");
        world.Errors.Should().BeEmpty();
    }

    /// <summary>
    /// Changed on purpose by ter-1b21 (docs/plans/spin-gravity.md, "Characters", fork F5): up follows the gravity in the
    /// air too. Before, this test was ACharacterThatWalksAgainKeepsTheUpThatItHadBeforeItWasWeightless: a character that
    /// touched nothing kept the up of before its rigid form under the new gravity, as in free fall. Since the user's choice
    /// of 2026-10-20 a character in the air turns its up at most at the air rate, so the quarter turn takes about 1.6 s at
    /// the default rate of 1 rad/s, and the test waits that long.
    /// </summary>
    [Fact]
    public void ACharacterThatWalksAgainTurnsItsUpAgainstTheNewGravityThoughItTouchesNothing()
    {
        using World world = NewWorld(backend: out JoltPhysicsBackend backend);
        // Gravity along -X, so the character's up is +X.
        FrameHandle space = world.CreateRootFrame(
            new(FrameBounds.Sphere(1e6f), GravityField.Uniform(new(-9.81f, 0, 0)))
        );
        Entity person = SpawnPerson(world: world, frame: space, pose: At(0));
        world.Tick();
        Vector3.Distance(UpOf(world, person), Vector3.UnitX).Should().BeLessThan(1e-5f);

        world.SetFrameGravity(space, GravityField.None);
        Tick(world, 10);
        backend.HasRigidForm(person).Should().BeTrue();
        // The new gravity is along -Y. The character touches nothing, but it walks again under gravity, so its up turns.
        world.SetFrameGravity(space, GravityField.Uniform(Down));
        float quarterTurnSeconds = MathF.PI / 2 / new JoltBackendOptions().CharacterAirUpTurnRate;
        Tick(world, 10 + (int)MathF.Ceiling(quarterTurnSeconds * 60));

        backend.HasRigidForm(person).Should().BeFalse();
        Vector3
            .Distance(UpOf(world, person), Vector3.UnitY)
            .Should()
            .BeLessThan(1e-5f, "under gravity, up turns against it in the air too");
        world.Errors.Should().BeEmpty();
    }

    /// <summary>
    /// A frame with gravity and a floor, a free wall panel of 50 kg, 1 m thick and 10 m high and wide, that stands on the floor,
    /// and a character in the air beside the panel, 0.02 m from its face and 2 m below the panel's top, that slides along the
    /// face at 3 m/s. A push that high tips the panel over rather than sliding it.
    /// </summary>
    private static (
        World World,
        FrameHandle Ground,
        Entity Person,
        Entity Panel,
        Pilot Pilot
    ) CharacterBesideAFreePanel(out JoltPhysicsBackend backend)
    {
        World world = NewWorld(backend: out backend);
        FrameHandle ground = Ground(world, 9.81f);
        Floor(world, ground, top: -10);
        Entity panel = world.Spawn(
            new()
            {
                Frame = ground,
                Pose = At(2, -5),
                Body = new(Shape: new BoxShapeDesc(new(0.5f, 5, 5)), Mass: 50, LinearDamping: 0, AngularDamping: 0),
            }
        );
        Tick(world, 60);
        var pilot = new Pilot();
        Entity person = SpawnPerson(
            world: world,
            frame: ground,
            pose: At(1.5 - 0.3 - 0.02, -2),
            velocity: new(0, 0, 3),
            pilot
        );
        return (world, ground, person, panel, pilot);
    }

    [Fact]
    public void ACharacterInTheAirUnderGravityIsNotHeldBackByTheSteepSlopeCancelFromAFreeWallThatItSlidesAlong()
    {
        (World world, FrameHandle ground, _, Entity panel, Pilot pilot) = CharacterBesideAFreePanel(out _);
        using (world)
        {
            double panelStart = world.PoseIn(panel, ground).Position.X;
            pilot.EveryTick = body => body.AddForce(new(70, 0, 0));
            Tick(world, 41);

            (world.PoseIn(panel, ground).Position.X - panelStart)
                .Should()
                .BeGreaterThan(
                    0.1,
                    "the character presses into the panel as it slides along it, and the panel gives way"
                );
            world.Errors.Should().BeEmpty();
        }
    }

    [Fact]
    public void ACharacterInTheAirUnderGravityThatSlidesDownAFreeWallFallsAsGravitySays()
    {
        (World world, FrameHandle ground, Entity person, _, Pilot pilot) = CharacterBesideAFreePanel(out _);
        using (world)
        {
            double start = world.PoseIn(person, ground).Position.Y;
            pilot.EveryTick = body => body.AddForce(new(70, 0, 0));
            Tick(world, 40);

            // From rest, the chords of n steps of g add up to g · (n · dt)² / 2.
            double t = 40 * world.DeltaTime;
            (start - world.PoseIn(person, ground).Position.Y)
                .Should()
                .BeApproximately(
                    9.81 * t * t / 2,
                    0.01,
                    "nothing but gravity moves the character along the panel's face"
                );
            world.Errors.Should().BeEmpty();
        }
    }
}
