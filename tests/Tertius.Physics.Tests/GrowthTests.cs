using System.Diagnostics;
using System.Numerics;
using Tertius.Simulation;
using static Tertius.Testing.WorldTesting;
using static Tertius.Testing.WorldTestingGrowth;
using static Tertius.Testing.NumericFrameTesting;

namespace Tertius.Physics.Tests;

/// <summary>
/// Jolt sizes a physics system's buffers when it creates the system, so a system holds at most a fixed number of bodies.
/// When a frame's system is full and the backend needs to add a body, it builds a system with twice the limits, moves
/// every body into it, and then adds the new body. No path fails for lack of room. These tests use systems of at most 8
/// bodies, so that a few crates fill one. They run the World on the real backend, and they count contacts and hand-offs in
/// the step outputs that Jolt really sent, which <see cref="FaultInjectingBackend"/> records.
/// <para>
/// Some tests read the backend right after a change batch and before any step, because a step changes what they compare:
/// the bodies move, and the sleep timers and the warm start of the contacts depend on the step. The World applies a pending
/// change batch without a step only in <see cref="World.Destroy"/>, so those tests call <see cref="ApplyPendingChanges"/>.
/// The other tests tick, and the comment of the test says so when it matters.
/// </para>
/// </summary>
public class GrowthTests
{
    private static readonly BodyDesc Crate = new(new BoxShapeDesc(new(0.5f)), BodyMotion.Static);

    private static readonly BodyDesc PersonDesc = new(new CapsuleShapeDesc(0.6f, 0.3f), BodyMotion.Character, Mass: 70);

    /// <summary>
    /// A world on the wrapper, whose systems start with room for <paramref name="maxBodiesPerFrame"/> bodies, with the
    /// default numeric frames or, when <paramref name="numericFrames"/> is false, with none.
    /// </summary>
    private static World NewWorldOfSmallSystems(int maxBodiesPerFrame, out FaultInjectingBackend faults,
        List<string>? warnings = null,
        bool numericFrames = true) =>
        NewWorldWithFaultsAndSmallSystems(maxBodiesPerFrame, out faults, warnings,
            configure: numericFrames ? null : options => options.NumericFrames = WithoutNumericFrames);

    /// <summary>A dynamic ball with no damping and no bounce.</summary>
    private static BodyDesc BallDesc(float radius, float mass = 1f) =>
        new(new SphereShapeDesc(radius), BodyMotion.Dynamic, Mass: mass, Restitution: 0, LinearDamping: 0,
            AngularDamping: 0);

    /// <summary>A dynamic box with no damping and no bounce, with half sides of <paramref name="half"/> m.</summary>
    private static BodyDesc BoxDesc(float half, float mass = 1f) =>
        new(new BoxShapeDesc(new(half)), BodyMotion.Dynamic, Mass: mass, Restitution: 0, LinearDamping: 0,
            AngularDamping: 0);

    /// <summary>A static floor of 2 · <paramref name="halfSize"/> m by 2 · <paramref name="halfSize"/> m, 1 m thick.</summary>
    private static BodyDesc FloorDesc(float halfSize) =>
        new(new BoxShapeDesc(new(halfSize, 0.5f, halfSize)), BodyMotion.Static);

    /// <summary>A frame with no body, in <paramref name="parent"/>, at <paramref name="pose"/>, with a bubble of
    /// <paramref name="radius"/> m.</summary>
    private static FrameHandle EmptyFrame(World world, FrameHandle parent, DPose pose, float radius,
        GravityField gravity = default) =>
        world.GetOwnedFrame(world.Spawn(new()
            { Frame = parent, Pose = pose, AsFrame = new(FrameBounds.Sphere(radius), gravity) }));

    /// <summary>The hand-offs that Jolt reported so far, in the order of the steps.</summary>
    private static List<HandOff> HandOffs(FaultInjectingBackend faults) =>
        [.. faults.Outputs.SelectMany(output => output.HandOffs)];

    /// <summary>The contacts that Jolt reported so far, in the order of the steps.</summary>
    private static List<ContactStarted> Contacts(FaultInjectingBackend faults) =>
        [.. faults.Outputs.SelectMany(output => output.Contacts)];

    /// <summary>
    /// Sends the changes that the World holds for physics to the backend now, with no step. <see cref="World.Destroy"/>
    /// sends the pending creations before it destroys anything, so this destroys a marker, an entity with no body, which
    /// changes nothing by itself.
    /// </summary>
    private static void ApplyPendingChanges(World world, FrameHandle frame)
    {
        Entity marker = world.Spawn(new() { Frame = frame });
        world.Destroy(marker).Should().BeTrue();
    }

    /// <summary>Fills the system of <paramref name="frame"/> with static crates in a row far below its origin.</summary>
    private static List<Entity> AddCrates(World world, FrameHandle frame, int count)
    {
        var crates = new List<Entity>();
        for (int i = 0; i < count; i++)
            crates.Add(world.Spawn(new() { Frame = frame, Pose = At(3 * i - 12, -100), Body = Crate }));
        return crates;
    }

    // ---- Every path that adds a body ----

    /// <summary>The kind of body that a test adds to a full system.</summary>
    public enum NewBody
    {
        /// <summary>A plain dynamic ball.</summary>
        Dynamic,

        /// <summary>A capsule character. Its inner body takes a place in the system.</summary>
        Character,

        /// <summary>The hull of a frame whose parent's system is full.</summary>
        Hull,

        /// <summary>The interior of a frame whose own system is full.</summary>
        Interior
    }

    /// <summary>
    /// A tick comes between the change batch and the reads, which is fine here: no body moves in the empty space of this
    /// scene. The interior case needs a trick. The World creates the interior together with its owner, and the owner's
    /// frame has no system before its owner, so the World never creates an interior in a system that is full already. The
    /// test holds the owner's creation back, with <see cref="FaultInjectingBackend.EditNextChanges"/>, while the crates fill
    /// the owner's frame, and then sends it to the backend as a change batch of its own.
    /// </summary>
    [Theory]
    [InlineData(NewBody.Dynamic)]
    [InlineData(NewBody.Character)]
    [InlineData(NewBody.Hull)]
    [InlineData(NewBody.Interior)]
    public void ABodyThatTheSimulationCreatesInAFullSystemGrowsTheSystemToTwiceItsSize(NewBody kind)
    {
        using World world = NewWorldOfSmallSystems(8, out FaultInjectingBackend faults);
        FrameHandle space = world.CreateRootFrame(new(FrameBounds.Sphere(1e7f)));
        FrameHandle hold;
        BodyCreate? heldBack = null;
        if (kind == NewBody.Interior)
        {
            Entity holdOwner = world.Spawn(new()
            {
                Frame = space,
                Pose = At(500),
                Body = new(new BoxShapeDesc(new(5)), BodyMotion.Static),
                AsFrame = new(FrameBounds.Sphere(50))
            });
            hold = world.GetOwnedFrame(holdOwner);
            faults.EditNextChanges = changes =>
            {
                heldBack = changes.BodiesCreated.Single(create => create.Entity == holdOwner);
                changes.BodiesCreated.Remove(heldBack.Value);
            };
        }
        else
        {
            hold = EmptyFrame(world, space, At(500), 50);
        }

        FrameHandle full = kind == NewBody.Interior ? hold : space;
        AddCrates(world, full, 8);
        world.Tick();
        faults.Jolt.CapacityOf(full).Should().Be(8);

        switch (kind)
        {
            case NewBody.Dynamic:
                Entity ball = world.Spawn(new() { Frame = space, Pose = At(0, 10), Body = BallDesc(0.5f) });
                world.Tick();
                faults.Jolt.TryGetBodyFrame(ball, out FrameHandle ballFrame).Should().BeTrue();
                ballFrame.Should().Be(space);
                break;
            case NewBody.Character:
                Entity person = world.Spawn(new() { Frame = space, Pose = At(0, 10), Body = PersonDesc });
                world.Tick();
                faults.Jolt.TryGetBodyFrame(person, out FrameHandle personFrame).Should().BeTrue();
                personFrame.Should().Be(space);
                // The sync point created the character's walking form, whose inner body grew the system. The space has no
                // gravity, so the step turned the character into its rigid form, a dynamic body in the place of the inner body.
                faults.Jolt.BodiesIn(space).Should().ContainSingle(facts => BodyUserData.Names(facts.UserData, person),
                    "the character's body is in the grown system");
                faults.Jolt.HasRigidForm(person).Should().BeTrue();
                break;
            case NewBody.Hull:
                FrameHandle ship = world.GetOwnedFrame(world.Spawn(new()
                {
                    Frame = space,
                    Pose = At(0, 10),
                    Body = BoxDesc(2, mass: 1000),
                    AsFrame = new(FrameBounds.Sphere(10))
                }));
                world.Tick();
                faults.Jolt.TryGetBodyFrame(ship.Entity, out FrameHandle hullFrame).Should().BeTrue();
                hullFrame.Should().Be(space);
                faults.Jolt.HasInterior(ship.Entity).Should().BeTrue();
                break;
            case NewBody.Interior:
                heldBack.Should().NotBeNull("the edit held the creation of the owner back");
                faults.Jolt.TryGetBodyFrame(heldBack!.Value.Entity, out _).Should()
                    .BeFalse("the owner has no body in the backend yet");
                var batch = new PhysicsChanges();
                batch.BodiesCreated.Add(heldBack.Value);
                faults.Jolt.ApplyChanges(batch, world.Frames);
                faults.Jolt.HasInterior(heldBack.Value.Entity).Should().BeTrue();
                faults.Jolt.TryGetBodyFrame(heldBack.Value.Entity, out FrameHandle holdHullFrame).Should().BeTrue();
                holdHullFrame.Should().Be(space);
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(kind), kind, null);
        }

        faults.Jolt.CapacityOf(full).Should().Be(16);
        faults.Jolt.BodiesIn(full).Should().HaveCount(9);
        faults.Jolt.Invoking(backend => backend.AssertInvariants()).Should().NotThrow();
        world.Tick();
        world.Errors.Should().BeEmpty();
    }

    /// <summary>
    /// A hangar bubble of radius 30 m, 1 km along +X, whose system is full of crates, and a ball or a ship 40 m beyond
    /// the hangar's center that flies into it at 60 m/s.
    /// </summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void AHandOffIntoAFullSystemGrowsTheSystemAndTheBodyFliesOnInIt(bool frameHull)
    {
        // Numeric frames off: the test checks that the hangar's own system holds the body after the hand-off.
        using World world = NewWorldOfSmallSystems(8, out FaultInjectingBackend faults, numericFrames: false);
        FrameHandle space = world.CreateRootFrame(new(FrameBounds.Sphere(1e7f)));
        FrameHandle hangar = EmptyFrame(world, space, At(1000), 30);
        AddCrates(world, hangar, 8);
        Entity flyer = frameHull
            ? world.Spawn(new()
            {
                Frame = space,
                Pose = At(1040),
                LinearVelocity = new Vector3(-60, 0, 0),
                Body = BoxDesc(2, mass: 1000),
                AsFrame = new(FrameBounds.Sphere(10))
            })
            : world.Spawn(new()
                { Frame = space, Pose = At(1040), LinearVelocity = new Vector3(-60, 0, 0), Body = BallDesc(0.5f) });

        TickUntil(world, () => HandOffs(faults).Count > 0, 60);
        Tick(world, 5);

        HandOffs(faults).Should().ContainSingle().Which.To.Should().Be(hangar);
        faults.Jolt.CapacityOf(hangar).Should().Be(16);
        faults.Jolt.TryGetBodyFrame(flyer, out FrameHandle bodyFrame).Should().BeTrue("the body survives the hand-off");
        bodyFrame.Should().Be(hangar);
        world.GetFrame(flyer).Should().Be(hangar);
        world.VelocityInOwnFrame(flyer).X.Should().BeApproximately(-60, 0.01f, "the body flies on at the same speed");
        // The World holds what Jolt sent in the last step: the body state of the ball, or the state of the ship's frame.
        PhysicsStepOutput lastStep = faults.Outputs[^1];
        if (frameHull)
        {
            FrameStepRecord sent = lastStep.Frames.Single(state => state.Frame.Entity == flyer);
            world.Frames.Parent(sent.Frame).Should().Be(hangar);
            world.Frames.LocalPose(sent.Frame).Should().Be(sent.Motion.Pose);
            world.VelocityInOwnFrame(flyer).Should().Be(sent.Motion.Velocity.ToVector3());
        }
        else
        {
            BodyState sent = lastStep.Bodies.Single(state => state.Entity == flyer);
            sent.Frame.Should().Be(hangar);
            world.PoseInOwnFrame(flyer).Should().Be(sent.Pose);
            world.VelocityInOwnFrame(flyer).Should().Be(sent.LinearVelocity);
        }

        world.Errors.Should().BeEmpty();
    }

    /// <summary>
    /// A person stands on the floor of a hangar whose system is full: the floor, the person's inner body and six crates. A
    /// ball flies into the hangar, and its hand-off grows the system after physics moved the person in the same step.
    /// The growth moves the person into the new system where it stands, so physics still reports it standing.
    /// </summary>
    [Fact]
    public void AStandingCharacterStillStandsInTheStepInWhichAHandOffGrowsItsSystem()
    {
        using World world = NewWorldOfSmallSystems(8, out FaultInjectingBackend faults);
        FrameHandle space = world.CreateRootFrame(new(FrameBounds.Sphere(1e7f)));
        FrameHandle hangar = EmptyFrame(world, space, At(1000), 30, GravityField.Uniform(new(0, -9.81f, 0)));
        world.Spawn(new() { Frame = hangar, Pose = At(0, -0.5), Body = FloorDesc(10) });
        Entity person = world.Spawn(new() { Frame = hangar, Pose = At(0, 0.92), Body = PersonDesc });
        AddCrates(world, hangar, 6);
        Tick(world, 20);
        faults.Jolt.CapacityOf(hangar).Should().Be(8);
        world.Snapshot.IsStanding(person).Should().BeTrue();

        world.Spawn(new()
            { Frame = space, Pose = At(1040, 10), LinearVelocity = new Vector3(-60, 0, 0), Body = BallDesc(0.5f) });
        var standing = new List<(uint Capacity, bool Standing)>();
        for (int tick = 0; tick < 60 && HandOffs(faults).Count == 0; tick++)
        {
            world.Tick();
            standing.Add((faults.Jolt.CapacityOf(hangar),
                faults.Outputs[^1].Characters.Single(c => c.Entity == person).Standing));
        }

        standing[^1].Capacity.Should().Be(16, "the hand-off grew the system");
        standing.Should().AllSatisfy(step => step.Standing.Should().BeTrue());
        world.Snapshot.IsStanding(person).Should().BeTrue("the World holds what Jolt sent in the last step");
        world.Errors.Should().BeEmpty();
    }

    /// <summary>
    /// The reviewer's probe W2: a ball that sticks out of a small bubble, whose parent's system is full. The parent needs
    /// a ghost of the ball at every step.
    /// </summary>
    [Fact]
    public void AGhostThatAFullSystemNeedsGrowsTheSystemAndEveryStepKeepsItsOutput()
    {
        using World world = NewWorldOfSmallSystems(8, out FaultInjectingBackend faults);
        FrameHandle space = world.CreateRootFrame(new(FrameBounds.Sphere(1e7f)));
        AddCrates(world, space, 8);
        FrameHandle bubble = EmptyFrame(world, space, At(50), 20);
        Entity ball = world.Spawn(new()
            { Frame = bubble, Pose = At(19.8), LinearVelocity = new Vector3(0, 1, 0), Body = BallDesc(0.5f) });

        for (int i = 0; i < 30; i++)
            world.Invoking(x => x.Tick()).Should().NotThrow("the ghost has a place in the grown system at step {0}", i);

        faults.Jolt.HasGhostIn(ball, space).Should().BeTrue();
        faults.Jolt.CapacityOf(space).Should().Be(16);
        faults.Outputs.Sum(output => output.Bodies.Count(state => state.Entity == ball)).Should()
            .Be(30, "no step loses its output");
        world.PoseInOwnFrame(ball).Position.Y.Should().BeApproximately(30 * (1.0 / 60), 1e-4);
        world.PoseInOwnFrame(ball).Should().Be(faults.Outputs[^1].Bodies.Single(state => state.Entity == ball).Pose,
            "the World holds what Jolt sent in the last step");
        world.Errors.Should().BeEmpty();
    }

    /// <summary>
    /// A station frame inside space dies, the frame tree moves the ship's frame up into space, and the same change batch
    /// destroys the ship's body and creates it again (this is what <see cref="World.Destroy"/> does to a frame's contents).
    /// The system of space is full. <see cref="World.Destroy"/> sends the batch to the backend at once, with no step, so the
    /// test reads the backend right after it.
    /// </summary>
    [Fact]
    public void AShipThatADestroyedStationRehomesIntoAFullParentGetsItsHullAndItsInterior()
    {
        using World world = NewWorldOfSmallSystems(8, out FaultInjectingBackend faults);
        FrameHandle space = world.CreateRootFrame(new(FrameBounds.Sphere(1e7f)));
        AddCrates(world, space, 8);
        Entity stationOwner = world.Spawn(new()
            { Frame = space, Pose = At(500), AsFrame = new(FrameBounds.Sphere(100)) });
        FrameHandle station = world.GetOwnedFrame(stationOwner);
        FrameHandle ship = world.GetOwnedFrame(world.Spawn(new()
        {
            Frame = station,
            Pose = At(10),
            Body = BoxDesc(2, mass: 1000),
            AsFrame = new(FrameBounds.Sphere(10))
        }));
        world.Tick();

        world.Destroy(stationOwner).Should().BeTrue();

        faults.Jolt.TryGetBodyFrame(ship.Entity, out FrameHandle hullFrame).Should().BeTrue();
        hullFrame.Should().Be(space);
        world.Frames.Parent(ship).Should().Be(space);
        faults.Jolt.HasInterior(ship.Entity).Should().BeTrue();
        faults.Jolt.CapacityOf(space).Should().Be(16);
        faults.Jolt.Invoking(backend => backend.AssertInvariants()).Should().NotThrow();
        world.Tick();
        world.Errors.Should().BeEmpty();
    }

    // ---- What the move keeps ----

    /// <summary>
    /// A ground frame whose system holds 16 bodies at most, filled to the limit with a body of every kind: a static floor,
    /// the interior of the ground frame itself, a rolling ball, a kinematic platform that slides, a compound body whose
    /// center of mass is off its origin, a ship's hull, a ghost of a kinematic block that sticks out of a small bubble, a
    /// character, static crates, and a box that has fallen asleep. Gravity acts only in a region around the floor. The
    /// box floats at rest above that region, so it falls asleep where it starts and stays out of the way of the others.
    /// </summary>
    private sealed class FullGround : IDisposable
    {
        public FullGround()
        {
            World = NewWorldOfSmallSystems(16, out FaultInjectingBackend faults);
            Faults = faults;
            GravityField gravity =
                GravityField.Uniform(new(0, -9.81f, 0), new BoundingBox(new(-60, -10, -60), new(60, 10, 60)));
            Ground = World.GetOwnedFrame(World.Spawn(new()
            {
                Body = new(new BoxShapeDesc(new(0.5f)), BodyMotion.Static),
                AsFrame = new(FrameBounds.Sphere(1e6f), gravity)
            }));
            World.Spawn(new() { Frame = Ground, Pose = At(0, -0.5), Body = FloorDesc(100) });
            Sleeper = World.Spawn(new() { Frame = Ground, Pose = At(-20, 30), Body = BoxDesc(0.5f, mass: 5) });
            Roller = World.Spawn(new()
            {
                Frame = Ground, Pose = At(20, 0.5), LinearVelocity = new Vector3(2, 0, 0),
                Body = BallDesc(0.5f, mass: 2)
            });
            World.Spawn(KinematicDrift.Drifting(new()
            {
                Frame = Ground,
                Pose = At(-10, 3),
                Body = new(new BoxShapeDesc(new(1, 0.1f, 1)), BodyMotion.Kinematic)
            }, World, new(0, 0, 1)));
            var lopsided = new CompoundShapeDesc([
                new(new BoxShapeDesc(new(0.5f)), new(new(0.6f, 0, 0), Quaternion.Identity)),
                new(new BoxShapeDesc(new(0.2f)), new(new(-0.3f, 0, 0), Quaternion.Identity))
            ]);
            World.Spawn(new() { Frame = Ground, Pose = At(30, 0.6), Body = new(lopsided, Mass: 3) });
            World.Spawn(new()
            {
                Frame = Ground,
                Pose = At(-30, 2.5),
                Body = BoxDesc(2, mass: 500),
                AsFrame = new(FrameBounds.Sphere(5))
            });
            FrameHandle nook = EmptyFrame(World, Ground, At(0, 40), 10);
            World.Spawn(new()
                { Frame = nook, Pose = At(10), Body = new(new BoxShapeDesc(new(1)), BodyMotion.Kinematic) });
            Person = World.Spawn(new()
                { Frame = Ground, Pose = At(0, 0.95, 20), Body = PersonDesc, Components = [PersonPilot] });
            // Two seconds: the box falls asleep, the others settle.
            Tick(World, 120);
            int bodies = Faults.Jolt.BodiesIn(Ground).Count;
            for (int i = bodies; i < 16; i++)
                World.Spawn(new() { Frame = Ground, Pose = At(3 * i, -100), Body = Crate });
            ApplyPendingChanges(World, Ground);
            Faults.Jolt.CapacityOf(Ground).Should().Be(16);
            Faults.Jolt.BodiesIn(Ground).Should().HaveCount(16);
        }

        public World World { get; }
        public FaultInjectingBackend Faults { get; }
        public FrameHandle Ground { get; }
        public Entity Sleeper { get; }
        public Entity Roller { get; }
        public Entity Person { get; }

        /// <summary>Steers <see cref="Person"/>. A test sets <see cref="Pilot.Once"/>, and the character keeps the movement until the next one.</summary>
        public Pilot PersonPilot { get; } = new();

        /// <summary>Adds one more crate, which makes the system of the ground grow. No step runs.</summary>
        public void Grow()
        {
            World.Spawn(new() { Frame = Ground, Pose = At(-3, -100), Body = Crate });
            ApplyPendingChanges(World, Ground);
            Faults.Jolt.CapacityOf(Ground).Should().Be(32);
        }

        public void Dispose() => World.Dispose();
    }

    private static bool IsCharacter(JoltPhysicsBackend.BodyFacts facts) =>
        BodyUserData.Kind(facts.UserData) == BodyKind.CharacterInner;

    private static bool Names(JoltPhysicsBackend.BodyFacts facts, Entity entity) =>
        BodyUserData.Kind(facts.UserData) == BodyKind.Primary && BodyUserData.Names(facts.UserData, entity);

    [Fact]
    public void GrowthKeepsTheIdKindMotionLayerShapeMassMaterialSleepAndStateOfEveryRigidBody()
    {
        using var scene = new FullGround();
        List<JoltPhysicsBackend.BodyFacts> before = scene.Faults.Jolt.BodiesIn(scene.Ground);
        before.Single(facts => Names(facts, scene.Sleeper)).Active.Should().BeFalse("the box is asleep");
        before.Single(facts => Names(facts, scene.Roller)).Active.Should().BeTrue("the ball rolls");
        before.Should().Contain(facts => BodyUserData.Kind(facts.UserData) == BodyKind.Ghost);
        before.Should().Contain(facts => BodyUserData.Kind(facts.UserData) == BodyKind.Interior);

        scene.Grow();

        List<JoltPhysicsBackend.BodyFacts> after = scene.Faults.Jolt.BodiesIn(scene.Ground);
        after.Should().HaveCount(17);
        foreach (JoltPhysicsBackend.BodyFacts old in before.Where(facts => !IsCharacter(facts)))
        {
            JoltPhysicsBackend.BodyFacts moved = after.Single(facts => facts.Id == old.Id);
            // Jolt keeps the center of mass, not the origin, so a body whose center of mass is off its origin can move by
            // a rounding error of the double position.
            (moved.Position - old.Position).Length.Should().BeLessThan(1e-12);
            moved.Should().Be(old with { Position = moved.Position });
        }

        scene.World.Errors.Should().BeEmpty();
    }

    [Fact]
    public void GrowthKeepsACharacterWhereItWasWithItsVelocity()
    {
        using var scene = new FullGround();
        JoltPhysicsBackend.BodyFacts before = scene.Faults.Jolt.BodiesIn(scene.Ground).Single(IsCharacter);

        scene.Grow();

        JoltPhysicsBackend.BodyFacts after = scene.Faults.Jolt.BodiesIn(scene.Ground).Single(IsCharacter);
        after.UserData.Should().Be(before.UserData);
        (after.Position - before.Position).Length.Should().BeLessThan(1e-9);
        after.Rotation.Should().Be(before.Rotation);
        after.LinearVelocity.Should().Be(before.LinearVelocity);
        scene.World.Errors.Should().BeEmpty();
    }

    [Fact]
    public void ACharacterWalksOnAcrossTheGrowthOfItsSystem()
    {
        using var scene = new FullGround();
        World world = scene.World;
        // The pilot writes the movement in this tick, and Jolt applies it in the step of the next one. The character keeps
        // the movement until the next one.
        scene.PersonPilot.Once = body => body.MoveCharacter(new(3, 0, 0));
        Tick(world, 30);
        DPose walking = world.PoseInOwnFrame(scene.Person);

        scene.Grow();
        Tick(world, 30);

        // Half a second at 3 m/s, still standing on the floor.
        (world.PoseInOwnFrame(scene.Person).Position.X - walking.Position.X).Should().BeInRange(1.45, 1.55);
        world.PoseInOwnFrame(scene.Person).Position.Y.Should().BeApproximately(walking.Position.Y, 0.01);
        world.VelocityInOwnFrame(scene.Person).X.Should().BeApproximately(3, 0.05f);
        world.Errors.Should().BeEmpty();
    }

    [Fact]
    public void ABodyThatRestsOnTheFloorAcrossTheGrowthReportsNoNewContact()
    {
        using var scene = new FullGround();
        // The ball rolls on the floor the whole time. Its contact with the floor started when it was created.
        int before = Contacts(scene.Faults).Count(contact => contact.A == scene.Roller || contact.B == scene.Roller);

        scene.Grow();
        Tick(scene.World, 10);

        Contacts(scene.Faults).Count(contact => contact.A == scene.Roller || contact.B == scene.Roller).Should().Be(
            before,
            "a pair that touched in the old system still touches in the new one, so its contact did not start again");
        scene.World.Errors.Should().BeEmpty();
    }

    [Fact]
    public void ABodyThatTouchesTheFloorForTheFirstTimeAfterTheGrowthReportsItsContact()
    {
        using var scene = new FullGround();
        World world = scene.World;
        Entity dropped = world.Spawn(new() { Frame = scene.Ground, Pose = At(40, 3), Body = BallDesc(0.5f) });

        ApplyPendingChanges(world, scene.Ground);
        scene.Faults.Jolt.CapacityOf(scene.Ground).Should().Be(32);
        TickUntil(world, () => Contacts(scene.Faults).Any(contact => contact.A == dropped || contact.B == dropped),
            120);

        Contacts(scene.Faults).Should().ContainSingle(contact => contact.A == dropped || contact.B == dropped);
        world.Errors.Should().BeEmpty();
    }

    // ---- Contacts across more than one growth ----

    /// <summary>
    /// A ball that rolls at 1 m/s, with no damping, on a floor in a ground frame with gravity, whose system holds 8 bodies
    /// at first. The ball's contact with the floor starts in the first steps and never ends.
    /// </summary>
    private sealed class Roller : IDisposable
    {
        public Roller()
        {
            World = NewWorldOfSmallSystems(8, out FaultInjectingBackend faults, Warnings);
            Faults = faults;
            Ground = Ground(World, 9.81f);
            World.Spawn(new() { Frame = Ground, Pose = At(0, -0.5), Body = FloorDesc(100) });
            Ball = World.Spawn(new()
            {
                Frame = Ground, Pose = At(0, 0.5), LinearVelocity = new Vector3(1, 0, 0), Body = BallDesc(0.5f, mass: 2)
            });
            Tick(World, 30);
            BallContacts.Should().Be(1, "the ball's contact with the floor started once");
        }

        public List<string> Warnings { get; } = [];
        public World World { get; }
        public FaultInjectingBackend Faults { get; }
        public FrameHandle Ground { get; }
        public Entity Ball { get; }

        /// <summary>The number of contacts that physics reported for the ball so far.</summary>
        public int BallContacts => Contacts(Faults).Count(contact => contact.A == Ball || contact.B == Ball);

        /// <summary>The number of times that a system grew so far.</summary>
        public int Growths => Warnings.Count(warning => warning.Contains("so it grew"));

        /// <summary>Adds static crates far below the floor, which touch nothing, to the next change batch.</summary>
        public void AddCrates(int count)
        {
            for (int i = 0; i < count; i++)
                World.Spawn(new() { Frame = Ground, Pose = At(3 * i, -100), Body = Crate });
        }

        public void Dispose() => World.Dispose();
    }

    [Fact]
    public void APairThatTouchesAcrossTwoGrowthsInOneChangeBatchReportsNoNewContact()
    {
        using var scene = new Roller();

        // The floor, the ball and 15 crates: the system grows from 8 to 16 at the 7th crate and to 32 at the 15th.
        scene.AddCrates(15);
        Tick(scene.World, 10);

        scene.Growths.Should().Be(2);
        scene.Faults.Jolt.CapacityOf(scene.Ground).Should().Be(32);
        scene.BallContacts.Should().Be(1,
            "the ball touched the floor through both growths, so its contact did not start again");
        scene.World.Errors.Should().BeEmpty();
    }

    [Fact]
    public void APairThatTouchesAcrossGrowthsAtTwoSyncPointsWithNoStepBetweenReportsNoNewContact()
    {
        using var scene = new Roller();

        // One change batch grows the system to 16, and the next one grows it to 32 before the system's next update. The
        // World sends the first batch with no step (see ApplyPendingChanges), and the second one at the start of the next tick.
        scene.AddCrates(7);
        ApplyPendingChanges(scene.World, scene.Ground);
        scene.AddCrates(8);
        Tick(scene.World, 10);

        scene.Growths.Should().Be(2);
        scene.Faults.Jolt.CapacityOf(scene.Ground).Should().Be(32);
        scene.BallContacts.Should().Be(1,
            "the ball touched the floor through both growths, so its contact did not start again");
        scene.World.Errors.Should().BeEmpty();
    }

    /// <summary>
    /// A pit frame with gravity, whose system of 8 bodies holds a floor, a ball that rolls on it, and 6 crates. A ball in
    /// space flies toward the pit. Its ghost needs room in the full pit system, so the pit's system grows during the step
    /// that adds the ghost, after the system's update. The next sync point grows it again, before its next update.
    /// </summary>
    [Fact]
    public void APairThatTouchesAcrossAGrowthDuringAStepAndAnotherAtTheNextSyncPointReportsNoNewContact()
    {
        using World world = NewWorldOfSmallSystems(8, out FaultInjectingBackend faults);
        FrameHandle space = world.CreateRootFrame(new(FrameBounds.Sphere(1e6f)));
        FrameHandle pit = EmptyFrame(world, space, At(0), 20, GravityField.Uniform(new(0, -9.81f, 0)));
        world.Spawn(new() { Frame = pit, Pose = At(0, -0.5), Body = FloorDesc(15) });
        Entity rolling = world.Spawn(new()
        {
            Frame = pit, Pose = At(0, 0.5), LinearVelocity = new Vector3(0.5f, 0, 0), Body = BallDesc(0.5f, mass: 2)
        });
        AddCrates(world, pit, 6);
        world.Spawn(new()
            { Frame = space, Pose = At(40, 3), LinearVelocity = new Vector3(-30, 0, 0), Body = BallDesc(0.5f) });
        Tick(world, 5);

        int ContactsOfTheRollingBall() =>
            Contacts(faults).Count(contact => contact.A == rolling || contact.B == rolling);

        ContactsOfTheRollingBall().Should().Be(1);

        TickUntil(world, () => faults.Jolt.CapacityOf(pit) > 8, 120);
        faults.Jolt.CapacityOf(pit).Should().Be(16, "the ghost grew the pit's system during the last step");
        int bodies = faults.Jolt.BodiesIn(pit).Count;
        AddCrates(world, pit, 16 - bodies + 1);
        Tick(world, 5);

        faults.Jolt.CapacityOf(pit).Should().Be(32, "the crates grew the pit's system again at the sync point");
        ContactsOfTheRollingBall().Should().Be(1,
            "the ball touched the floor through both growths, so its contact did not start again");
        world.Errors.Should().BeEmpty();
    }

    // ---- Determinism ----

    /// <summary>
    /// Boxes fall on the floor of a ground frame whose system holds 16 bodies at most, and a ball rolls into a pit frame
    /// whose system is full: the pit's floor, 14 crates and the ghost of the ground's floor, which reaches the pit's bubble.
    /// The hand-off grows the pit's system during a step, and at tick 20 more boxes grow the ground's system at the sync
    /// point. Returns a hash of the output of every step, the capacity of both systems after it, and a hash of the whole
    /// World after it.
    /// </summary>
    private static (List<ulong> Hashes, List<(uint Ground, uint Pit)> Capacities, List<ulong> WorldHashes)
        RunWithGrowth()
    {
        using World world = NewWorldOfSmallSystems(16, out FaultInjectingBackend faults);
        GravityField gravity = GravityField.Uniform(new(0, -9.81f, 0));
        FrameHandle ground = world.CreateRootFrame(new(FrameBounds.Sphere(1e6f), gravity));
        world.Spawn(new() { Frame = ground, Pose = At(0, -0.5), Body = FloorDesc(100) });
        FrameHandle pit = EmptyFrame(world, ground, At(40), 10, gravity);
        world.Spawn(new() { Frame = pit, Pose = At(0, -0.5), Body = FloorDesc(15) });
        AddCrates(world, pit, 14);
        for (int i = 0; i < 6; i++)
            world.Spawn(new()
                { Frame = ground, Pose = At(0.3 * i, 1 + 1.1 * i, 0.1 * i), Body = BoxDesc(0.4f, mass: 2) });
        world.Spawn(new()
            { Frame = ground, Pose = At(28, 0.5), LinearVelocity = new Vector3(6, 0, 0), Body = BallDesc(0.5f) });

        var hashes = new List<ulong>();
        var capacities = new List<(uint, uint)>();
        var worldHashes = new List<ulong>();
        for (int tick = 0; tick < 90; tick++)
        {
            if (tick == 20)
                for (int i = 0; i < 10; i++)
                    world.Spawn(new()
                        { Frame = ground, Pose = At(-5 + 0.2 * i, 1 + 1.1 * i), Body = BoxDesc(0.4f, mass: 2) });
            world.Tick();
            hashes.Add(Hash(faults.Outputs[^1]));
            capacities.Add((faults.Jolt.CapacityOf(ground), faults.Jolt.CapacityOf(pit)));
            worldHashes.Add(WorldHash.Of(world));
        }

        world.Errors.Should().BeEmpty();
        return (hashes, capacities, worldHashes);
    }

    private static ulong Hash(PhysicsStepOutput output)
    {
        ulong hash = 14695981039346656037;

        void Add(ulong value)
        {
            hash ^= value;
            hash *= 1099511628211;
        }

        void AddDouble(double value) => Add((ulong)BitConverter.DoubleToInt64Bits(value));

        void AddVector(Vector3 v)
        {
            AddDouble(v.X);
            AddDouble(v.Y);
            AddDouble(v.Z);
        }

        void AddPose(DPose pose)
        {
            AddDouble(pose.Position.X);
            AddDouble(pose.Position.Y);
            AddDouble(pose.Position.Z);
            AddDouble(pose.Rotation.X);
            AddDouble(pose.Rotation.Y);
            AddDouble(pose.Rotation.Z);
            AddDouble(pose.Rotation.W);
        }

        foreach (BodyState state in output.Bodies)
        {
            Add(state.Entity.Index);
            Add((ulong)state.Frame.Index);
            AddPose(state.Pose);
            AddVector(state.LinearVelocity);
            AddVector(state.AngularVelocity);
        }

        foreach (HandOff handOff in output.HandOffs)
        {
            Add(handOff.Entity.Index);
            Add((ulong)handOff.To.Index);
            AddPose(handOff.Pose);
        }

        foreach (ContactStarted contact in output.Contacts)
        {
            Add(contact.A.Index);
            Add(contact.B.Index);
            AddDouble(contact.Point.X);
            AddDouble(contact.Point.Y);
            AddDouble(contact.Point.Z);
            AddVector(contact.Normal);
            AddDouble(contact.Impulse);
        }

        return hash;
    }

    [Fact]
    public void TwoRunsThatGrowAtTheSameTicksReportTheSameStepsBitForBit()
    {
        (List<ulong> firstHashes, List<(uint Ground, uint Pit)> firstCapacities, List<ulong> firstWorldHashes) =
            RunWithGrowth();
        (List<ulong> secondHashes, List<(uint Ground, uint Pit)> secondCapacities, List<ulong> secondWorldHashes) =
            RunWithGrowth();

        firstCapacities[0].Should().Be((16, 16));
        firstCapacities[^1].Should().Be((32, 32), "both systems grew during the run");
        secondCapacities.Should().Equal(firstCapacities);
        secondHashes.Should().Equal(firstHashes);
        secondWorldHashes.Should().Equal(firstWorldHashes);
    }

    // ---- Cost ----

    [Fact]
    public void GrowingAFrameOf4096BodiesTakesOneHitch()
    {
        var times = new List<double>();
        for (int run = 0; run < 3; run++)
        {
            using World world = NewWorldWithFaults(out FaultInjectingBackend faults, debugChecks: false);
            FrameHandle hold = world.CreateRootFrame(new(FrameBounds.Sphere(1e6f)));
            // Half static crates and half balls, 2 m apart, so that nothing touches.
            BodyDesc ball = BallDesc(0.4f);
            for (int i = 0; i < 4096; i++)
            {
                DPose pose = At(2 * (i % 64), 2 * (i / 64 % 64));
                world.Spawn(new() { Frame = hold, Pose = pose, Body = i % 2 == 0 ? Crate : ball });
            }

            world.Tick();
            faults.Jolt.CapacityOf(hold).Should().Be(4096);

            world.Spawn(new() { Frame = hold, Pose = At(0, 0, 50), Body = Crate });
            Entity marker = world.Spawn(new() { Frame = hold });
            var watch = Stopwatch.StartNew();
            world.Destroy(marker);
            watch.Stop();

            faults.Jolt.CapacityOf(hold).Should().Be(8192);
            faults.Jolt.BodiesIn(hold).Should().HaveCount(4097);
            times.Add(watch.Elapsed.TotalMilliseconds);
        }

        TestContext.Current.TestOutputHelper?.WriteLine(
            $"Growing a system of 4096 bodies to 8192 took {string.Join(", ", times.Select(t => $"{t:F2} ms"))}.");
    }
}
