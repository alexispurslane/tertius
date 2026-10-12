using System.Diagnostics;
using System.Numerics;

namespace Tertius.Physics.Tests;

/// <summary>
/// Jolt sizes a physics system's buffers when it creates the system, so a system holds at most a fixed number of bodies.
/// When a frame's system is full and the backend needs to add a body, it builds a system with twice the limits, moves
/// every body into it, and then adds the new body. No path fails for lack of room. These tests use systems of at most 8
/// bodies, so that a few crates fill one.
/// </summary>
public class GrowthTests
{
    private static readonly JoltBackendOptions Small = new() { MaxBodiesPerFrame = 8 };

    private static readonly BodyDesc Crate = Shapes.Box(0.5f, motion: BodyMotion.Static);

    /// <summary>Fills the system of <paramref name="frame"/> with static crates in a row far below its origin.</summary>
    private static List<Entity> AddCrates(PhysicsHarness h, FrameHandle frame, int count)
    {
        var crates = new List<Entity>();
        for (int i = 0; i < count; i++)
            crates.Add(h.AddBody(frame, Crate, Shapes.At(3 * i - 12, -100, 0)));
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
        Interior,
    }

    [Theory]
    [InlineData(NewBody.Dynamic)]
    [InlineData(NewBody.Character)]
    [InlineData(NewBody.Hull)]
    [InlineData(NewBody.Interior)]
    public void ABodyThatTheSimulationCreatesInAFullSystemGrowsTheSystemToTwiceItsSize(NewBody kind)
    {
        using var h = new PhysicsHarness(Small);
        FrameHandle space = h.AddFrame(FrameHandle.Null, DPose.Identity, FrameBounds.Sphere(1e7f), MotionDriver.Static);
        FrameHandle hold = h.AddFrame(space, Shapes.At(500, 0, 0), FrameBounds.Sphere(50), MotionDriver.Static);
        FrameHandle full = kind == NewBody.Interior ? hold : space;
        AddCrates(h, full, 8);
        h.Sync();
        h.Backend.CapacityOf(full).Should().Be(8);

        switch (kind)
        {
            case NewBody.Dynamic:
                Entity ball = h.AddBody(space, Shapes.Ball(0.5f), Shapes.At(0, 10, 0));
                h.Sync();
                h.Backend.TryGetBodyFrame(ball, out FrameHandle ballFrame).Should().BeTrue();
                ballFrame.Should().Be(space);
                break;
            case NewBody.Character:
                Entity person = h.AddBody(space, new BodyDesc(new CapsuleShapeDesc(0.6f, 0.3f), BodyMotion.Character, Mass: 70), Shapes.At(0, 10, 0));
                h.Sync();
                h.Backend.TryGetBodyFrame(person, out FrameHandle personFrame).Should().BeTrue();
                personFrame.Should().Be(space);
                h.Backend.BodiesIn(space).Should().ContainSingle(facts => BodyUserData.Kind(facts.UserData) == BodyKind.CharacterInner,
                    "the character's inner body is in the grown system");
                break;
            case NewBody.Hull:
                FrameHandle ship = h.AddFrameWithBody(space, Shapes.At(0, 10, 0), FrameBounds.Sphere(10), MotionDriver.Physics, Shapes.Box(2, mass: 1000));
                h.Sync();
                h.Backend.TryGetBodyFrame(ship.Entity, out FrameHandle hullFrame).Should().BeTrue();
                hullFrame.Should().Be(space);
                h.Backend.HasInterior(ship.Entity).Should().BeTrue();
                break;
            case NewBody.Interior:
                h.AddBody(space, new BodyDesc(new BoxShapeDesc(new Vector3(5)), BodyMotion.Static), h.Frames.LocalPose(hold), entity: hold.Entity);
                h.Sync();
                h.Backend.HasInterior(hold.Entity).Should().BeTrue();
                h.Backend.TryGetBodyFrame(hold.Entity, out FrameHandle holdHullFrame).Should().BeTrue();
                holdHullFrame.Should().Be(space);
                break;
        }

        h.Backend.CapacityOf(full).Should().Be(16);
        h.Backend.BodiesIn(full).Should().HaveCount(9);
        h.Backend.Invoking(backend => backend.AssertInvariants()).Should().NotThrow();
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
        using var h = new PhysicsHarness(Small);
        FrameHandle space = h.AddFrame(FrameHandle.Null, DPose.Identity, FrameBounds.Sphere(1e7f), MotionDriver.Static);
        FrameHandle hangar = h.AddFrame(space, Shapes.At(1000, 0, 0), FrameBounds.Sphere(30), MotionDriver.Static);
        AddCrates(h, hangar, 8);
        Entity flyer = frameHull
            ? h.AddFrameWithBody(space, Shapes.At(1040, 0, 0), FrameBounds.Sphere(10), MotionDriver.Physics,
                Shapes.Box(2, mass: 1000), velocity: new Vector3(-60, 0, 0)).Entity
            : h.AddBody(space, Shapes.Ball(0.5f), Shapes.At(1040, 0, 0), velocity: new Vector3(-60, 0, 0));

        h.StepUntil(() => h.HandOffs.Count > 0, 60);
        h.Step(5);

        h.HandOffs.Should().ContainSingle().Which.To.Should().Be(hangar);
        h.Backend.CapacityOf(hangar).Should().Be(16);
        h.Backend.TryGetBodyFrame(flyer, out FrameHandle bodyFrame).Should().BeTrue("the body survives the hand-off");
        bodyFrame.Should().Be(hangar);
        h[flyer].Frame.Should().Be(hangar);
        h[flyer].LinearVelocity.X.Should().BeApproximately(-60, 0.01f, "the body flies on at the same speed");
    }

    /// <summary>
    /// A person stands on the floor of a hangar whose system is full: the floor, the person's inner body and six crates. A
    /// ball flies into the hangar, and its hand-off grows the system after physics moved the person in the same step.
    /// The growth moves the person into the new system where it stands, so physics still reports it standing.
    /// </summary>
    [Fact]
    public void AStandingCharacterStillStandsInTheStepInWhichAHandOffGrowsItsSystem()
    {
        using var h = new PhysicsHarness(Small);
        FrameHandle space = h.AddFrame(FrameHandle.Null, DPose.Identity, FrameBounds.Sphere(1e7f), MotionDriver.Static);
        FrameHandle hangar = h.AddFrame(space, Shapes.At(1000, 0, 0), FrameBounds.Sphere(30), MotionDriver.Static,
            GravityField.Uniform(new Vector3(0, -9.81f, 0)));
        h.AddBody(hangar, Shapes.Floor(10), Shapes.At(0, -0.5, 0));
        Entity person = h.AddBody(hangar, new BodyDesc(new CapsuleShapeDesc(0.6f, 0.3f), BodyMotion.Character, Mass: 70), Shapes.At(0, 0.92, 0));
        AddCrates(h, hangar, 6);
        h.Step(20);
        h.Backend.CapacityOf(hangar).Should().Be(8);
        h.Standing[person].Should().BeTrue();

        h.AddBody(space, Shapes.Ball(0.5f), Shapes.At(1040, 10, 0), velocity: new Vector3(-60, 0, 0));
        var standing = new List<(uint Capacity, bool Standing)>();
        h.AfterStep = output => standing.Add((h.Backend.CapacityOf(hangar), output.Characters.Single(c => c.Entity == person).Standing));
        h.StepUntil(() => h.HandOffs.Count > 0, 60);

        standing[^1].Capacity.Should().Be(16, "the hand-off grew the system");
        standing.Should().AllSatisfy(step => step.Standing.Should().BeTrue());
    }

    /// <summary>
    /// The reviewer's probe W2: a ball that sticks out of a small bubble, whose parent's system is full. The parent needs
    /// a ghost of the ball at every step.
    /// </summary>
    [Fact]
    public void AGhostThatAFullSystemNeedsGrowsTheSystemAndEveryStepKeepsItsOutput()
    {
        using var h = new PhysicsHarness(Small);
        FrameHandle space = h.AddFrame(FrameHandle.Null, DPose.Identity, FrameBounds.Sphere(1e7f), MotionDriver.Static);
        AddCrates(h, space, 8);
        FrameHandle bubble = h.AddFrame(space, Shapes.At(50, 0, 0), FrameBounds.Sphere(20), MotionDriver.Static);
        Entity ball = h.AddBody(bubble, Shapes.Ball(0.5f), Shapes.At(19.8, 0, 0), velocity: new Vector3(0, 1, 0));
        int reports = 0;
        h.AfterStep = output => reports += output.Bodies.Count(state => state.Entity == ball);

        for (int i = 0; i < 30; i++)
            h.Invoking(x => x.Step()).Should().NotThrow("the ghost has a place in the grown system at step {0}", i);

        h.Backend.HasGhostIn(ball, space).Should().BeTrue();
        h.Backend.CapacityOf(space).Should().Be(16);
        reports.Should().Be(30, "no step loses its output");
        h[ball].Pose.Position.Y.Should().BeApproximately(30 * PhysicsHarness.Dt, 1e-4);
    }

    /// <summary>
    /// The harness's form of World.Destroy rehoming a ship: a station frame inside space dies, the frame tree moves the
    /// ship's frame up into space, and the same change batch destroys the ship's body and creates it again. The system of
    /// space is full.
    /// </summary>
    [Fact]
    public void AShipThatADestroyedStationRehomesIntoAFullParentGetsItsHullAndItsInterior()
    {
        using var h = new PhysicsHarness(Small);
        FrameHandle space = h.AddFrame(FrameHandle.Null, DPose.Identity, FrameBounds.Sphere(1e7f), MotionDriver.Static);
        AddCrates(h, space, 8);
        FrameHandle station = h.AddFrame(space, Shapes.At(500, 0, 0), FrameBounds.Sphere(100), MotionDriver.Static);
        BodyDesc hull = Shapes.Box(2, mass: 1000);
        FrameHandle ship = h.AddFrameWithBody(station, Shapes.At(10, 0, 0), FrameBounds.Sphere(10), MotionDriver.Physics, hull);
        h.Step();

        h.Frames.Destroy(station);
        h.Changes.FramesDestroyed.Add(station);
        h.Destroy(ship.Entity);
        h.AddBody(space, hull, h.Frames.LocalPose(ship), h.Frames.LinearVelocity(ship).ToVector3(), h.Frames.AngularVelocity(ship), ship.Entity);
        h.Sync();

        h.Backend.TryGetBodyFrame(ship.Entity, out FrameHandle hullFrame).Should().BeTrue();
        hullFrame.Should().Be(space);
        h.Backend.HasInterior(ship.Entity).Should().BeTrue();
        h.Backend.CapacityOf(space).Should().Be(16);
        h.Backend.Invoking(backend => backend.AssertInvariants()).Should().NotThrow();
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
            H = new PhysicsHarness(new JoltBackendOptions { MaxBodiesPerFrame = 16 });
            var gravity = GravityField.Uniform(new Vector3(0, -9.81f, 0), new BoundingBox(new Vector3(-60, -10, -60), new Vector3(60, 10, 60)));
            Ground = H.AddFrameWithBody(FrameHandle.Null, DPose.Identity, FrameBounds.Sphere(1e6f), MotionDriver.Static,
                new BodyDesc(new BoxShapeDesc(new Vector3(0.5f)), BodyMotion.Static), gravity);
            H.AddBody(Ground, Shapes.Floor(100), Shapes.At(0, -0.5, 0));
            Sleeper = H.AddBody(Ground, Shapes.Box(0.5f, mass: 5), Shapes.At(-20, 30, 0));
            Roller = H.AddBody(Ground, Shapes.Ball(0.5f, mass: 2), Shapes.At(20, 0.5, 0), velocity: new Vector3(2, 0, 0));
            H.AddBody(Ground, new BodyDesc(new BoxShapeDesc(new Vector3(1, 0.1f, 1)), BodyMotion.Kinematic), Shapes.At(-10, 3, 0), velocity: new Vector3(0, 0, 1));
            var lopsided = new CompoundShapeDesc([
                new CompoundChild(new BoxShapeDesc(new Vector3(0.5f)), new Pose(new Vector3(0.6f, 0, 0), Quaternion.Identity)),
                new CompoundChild(new BoxShapeDesc(new Vector3(0.2f)), new Pose(new Vector3(-0.3f, 0, 0), Quaternion.Identity)),
            ]);
            H.AddBody(Ground, new BodyDesc(lopsided, Mass: 3), Shapes.At(30, 0.6, 0));
            H.AddFrameWithBody(Ground, Shapes.At(-30, 2.5, 0), FrameBounds.Sphere(5), MotionDriver.Physics, Shapes.Box(2, mass: 500));
            FrameHandle nook = H.AddFrame(Ground, Shapes.At(0, 40, 0), FrameBounds.Sphere(10), MotionDriver.Static);
            H.AddBody(nook, new BodyDesc(new BoxShapeDesc(new Vector3(1)), BodyMotion.Kinematic), Shapes.At(10, 0, 0));
            Person = H.AddBody(Ground, new BodyDesc(new CapsuleShapeDesc(0.6f, 0.3f), BodyMotion.Character, Mass: 70), Shapes.At(0, 0.95, 20));
            // Two seconds: the box falls asleep, the others settle.
            H.Step(120);
            int bodies = H.Backend.BodiesIn(Ground).Count;
            for (int i = bodies; i < 16; i++)
                H.AddBody(Ground, Crate, Shapes.At(3 * i, -100, 0));
            H.Sync();
            H.Backend.CapacityOf(Ground).Should().Be(16);
            H.Backend.BodiesIn(Ground).Should().HaveCount(16);
        }

        public PhysicsHarness H { get; }
        public FrameHandle Ground { get; }
        public Entity Sleeper { get; }
        public Entity Roller { get; }
        public Entity Person { get; }

        /// <summary>Adds one more crate, which makes the system of the ground grow.</summary>
        public void Grow()
        {
            H.AddBody(Ground, Crate, Shapes.At(-3, -100, 0));
            H.Sync();
            H.Backend.CapacityOf(Ground).Should().Be(32);
        }

        public void Dispose() => H.Dispose();
    }

    private static bool IsCharacter(JoltPhysicsBackend.BodyFacts facts) => BodyUserData.Kind(facts.UserData) == BodyKind.CharacterInner;

    private static bool Names(JoltPhysicsBackend.BodyFacts facts, Entity entity) =>
        BodyUserData.Kind(facts.UserData) == BodyKind.Primary && BodyUserData.Names(facts.UserData, entity);

    [Fact]
    public void GrowthKeepsTheIdKindMotionLayerShapeMassMaterialSleepAndStateOfEveryRigidBody()
    {
        using var scene = new FullGround();
        List<JoltPhysicsBackend.BodyFacts> before = scene.H.Backend.BodiesIn(scene.Ground);
        before.Single(facts => Names(facts, scene.Sleeper)).Active.Should().BeFalse("the box is asleep");
        before.Single(facts => Names(facts, scene.Roller)).Active.Should().BeTrue("the ball rolls");
        before.Should().Contain(facts => BodyUserData.Kind(facts.UserData) == BodyKind.Ghost);
        before.Should().Contain(facts => BodyUserData.Kind(facts.UserData) == BodyKind.Interior);

        scene.Grow();

        List<JoltPhysicsBackend.BodyFacts> after = scene.H.Backend.BodiesIn(scene.Ground);
        after.Should().HaveCount(17);
        foreach (JoltPhysicsBackend.BodyFacts old in before.Where(facts => !IsCharacter(facts)))
        {
            JoltPhysicsBackend.BodyFacts moved = after.Single(facts => facts.Id == old.Id);
            // Jolt keeps the center of mass, not the origin, so a body whose center of mass is off its origin can move by
            // a rounding error of the double position.
            (moved.Position - old.Position).Length.Should().BeLessThan(1e-12);
            moved.Should().Be(old with { Position = moved.Position });
        }
    }

    [Fact]
    public void GrowthKeepsACharacterWhereItWasWithItsVelocity()
    {
        using var scene = new FullGround();
        JoltPhysicsBackend.BodyFacts before = scene.H.Backend.BodiesIn(scene.Ground).Single(IsCharacter);

        scene.Grow();

        JoltPhysicsBackend.BodyFacts after = scene.H.Backend.BodiesIn(scene.Ground).Single(IsCharacter);
        after.UserData.Should().Be(before.UserData);
        (after.Position - before.Position).Length.Should().BeLessThan(1e-9);
        after.Rotation.Should().Be(before.Rotation);
        after.LinearVelocity.Should().Be(before.LinearVelocity);
    }

    [Fact]
    public void ACharacterWalksOnAcrossTheGrowthOfItsSystem()
    {
        using var scene = new FullGround();
        PhysicsHarness h = scene.H;
        h.Intent(scene.Person).Flags = IntentFlags.CharacterMove;
        h.Intent(scene.Person).CharacterVelocity = new Vector3(3, 0, 0);
        h.Step(30);
        BodyState walking = h[scene.Person];

        scene.Grow();
        h.Step(30);

        // Half a second at 3 m/s, still standing on the floor.
        (h[scene.Person].Pose.Position.X - walking.Pose.Position.X).Should().BeInRange(1.45, 1.55);
        h[scene.Person].Pose.Position.Y.Should().BeApproximately(walking.Pose.Position.Y, 0.01);
        h[scene.Person].LinearVelocity.X.Should().BeApproximately(3, 0.05f);
    }

    [Fact]
    public void ABodyThatRestsOnTheFloorAcrossTheGrowthReportsNoNewContact()
    {
        using var scene = new FullGround();
        PhysicsHarness h = scene.H;
        // The ball rolls on the floor the whole time. Its contact with the floor started when it was created.
        int before = h.Contacts.Count(contact => contact.A == scene.Roller || contact.B == scene.Roller);

        scene.Grow();
        h.Step(10);

        h.Contacts.Count(contact => contact.A == scene.Roller || contact.B == scene.Roller).Should().Be(before,
            "a pair that touched in the old system still touches in the new one, so its contact did not start again");
    }

    [Fact]
    public void ABodyThatTouchesTheFloorForTheFirstTimeAfterTheGrowthReportsItsContact()
    {
        using var scene = new FullGround();
        PhysicsHarness h = scene.H;
        Entity dropped = h.AddBody(scene.Ground, Shapes.Ball(0.5f), Shapes.At(40, 3, 0));

        h.Sync();
        h.Backend.CapacityOf(scene.Ground).Should().Be(32);
        h.StepUntil(() => h.Contacts.Any(contact => contact.A == dropped || contact.B == dropped), 120);

        h.Contacts.Should().ContainSingle(contact => contact.A == dropped || contact.B == dropped);
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
            H = new PhysicsHarness(Small);
            Ground = H.AddFrame(FrameHandle.Null, DPose.Identity, FrameBounds.Sphere(1e6f), MotionDriver.Static,
                GravityField.Uniform(new Vector3(0, -9.81f, 0)));
            H.AddBody(Ground, Shapes.Floor(100), Shapes.At(0, -0.5, 0));
            Ball = H.AddBody(Ground, Shapes.Ball(0.5f, mass: 2), Shapes.At(0, 0.5, 0), velocity: new Vector3(1, 0, 0));
            H.Step(30);
            BallContacts.Should().Be(1, "the ball's contact with the floor started once");
        }

        public PhysicsHarness H { get; }
        public FrameHandle Ground { get; }
        public Entity Ball { get; }

        /// <summary>The number of contacts that physics reported for the ball so far.</summary>
        public int BallContacts => H.Contacts.Count(contact => contact.A == Ball || contact.B == Ball);

        /// <summary>The number of times that a system grew so far.</summary>
        public int Growths => H.Warnings.Count(warning => warning.Contains("so it grew"));

        /// <summary>Adds static crates far below the floor, which touch nothing, to the next change batch.</summary>
        public void AddCrates(int count)
        {
            for (int i = 0; i < count; i++)
                H.AddBody(Ground, Crate, Shapes.At(3 * i, -100, 0));
        }

        public void Dispose() => H.Dispose();
    }

    [Fact]
    public void APairThatTouchesAcrossTwoGrowthsInOneChangeBatchReportsNoNewContact()
    {
        using var scene = new Roller();

        // The floor, the ball and 15 crates: the system grows from 8 to 16 at the 7th crate and to 32 at the 15th.
        scene.AddCrates(15);
        scene.H.Step(10);

        scene.Growths.Should().Be(2);
        scene.H.Backend.CapacityOf(scene.Ground).Should().Be(32);
        scene.BallContacts.Should().Be(1, "the ball touched the floor through both growths, so its contact did not start again");
    }

    [Fact]
    public void APairThatTouchesAcrossGrowthsAtTwoSyncPointsWithNoStepBetweenReportsNoNewContact()
    {
        using var scene = new Roller();

        // One change batch grows the system to 16, and the next one grows it to 32 before the system's next update.
        scene.AddCrates(7);
        scene.H.Sync();
        scene.AddCrates(8);
        scene.H.Step(10);

        scene.Growths.Should().Be(2);
        scene.H.Backend.CapacityOf(scene.Ground).Should().Be(32);
        scene.BallContacts.Should().Be(1, "the ball touched the floor through both growths, so its contact did not start again");
    }

    /// <summary>
    /// A pit frame with gravity, whose system of 8 bodies holds a floor, a ball that rolls on it, and 6 crates. A ball in
    /// space flies toward the pit. Its ghost needs room in the full pit system, so the pit's system grows during the step
    /// that adds the ghost, after the system's update. The next sync point grows it again, before its next update.
    /// </summary>
    [Fact]
    public void APairThatTouchesAcrossAGrowthDuringAStepAndAnotherAtTheNextSyncPointReportsNoNewContact()
    {
        using var h = new PhysicsHarness(Small);
        FrameHandle space = h.AddFrame(FrameHandle.Null, DPose.Identity, FrameBounds.Sphere(1e6f), MotionDriver.Static);
        FrameHandle pit = h.AddFrame(space, Shapes.At(0, 0, 0), FrameBounds.Sphere(20), MotionDriver.Static,
            GravityField.Uniform(new Vector3(0, -9.81f, 0)));
        h.AddBody(pit, Shapes.Floor(15), Shapes.At(0, -0.5, 0));
        Entity rolling = h.AddBody(pit, Shapes.Ball(0.5f, mass: 2), Shapes.At(0, 0.5, 0), velocity: new Vector3(0.5f, 0, 0));
        AddCrates(h, pit, 6);
        h.AddBody(space, Shapes.Ball(0.5f), Shapes.At(40, 3, 0), velocity: new Vector3(-30, 0, 0));
        h.Step(5);
        int ContactsOfTheRollingBall() => h.Contacts.Count(contact => contact.A == rolling || contact.B == rolling);
        ContactsOfTheRollingBall().Should().Be(1);

        h.StepUntil(() => h.Backend.CapacityOf(pit) > 8, 120);
        h.Backend.CapacityOf(pit).Should().Be(16, "the ghost grew the pit's system during the last step");
        int bodies = h.Backend.BodiesIn(pit).Count;
        AddCrates(h, pit, 16 - bodies + 1);
        h.Step(5);

        h.Backend.CapacityOf(pit).Should().Be(32, "the crates grew the pit's system again at the sync point");
        ContactsOfTheRollingBall().Should().Be(1, "the ball touched the floor through both growths, so its contact did not start again");
    }

    // ---- Determinism ----

    /// <summary>
    /// Boxes fall on the floor of a ground frame whose system holds 16 bodies at most, and a ball rolls into a pit frame
    /// whose system is full. The hand-off grows the pit's system during a step, and at tick 20 more boxes grow the ground's
    /// system at the sync point. Returns a hash of the output of every step and the capacity of both systems after it.
    /// </summary>
    private static (List<ulong> Hashes, List<(uint Ground, uint Pit)> Capacities) RunWithGrowth()
    {
        using var h = new PhysicsHarness(new JoltBackendOptions { MaxBodiesPerFrame = 16 });
        var gravity = GravityField.Uniform(new Vector3(0, -9.81f, 0));
        FrameHandle ground = h.AddFrame(FrameHandle.Null, DPose.Identity, FrameBounds.Sphere(1e6f), MotionDriver.Static, gravity);
        h.AddBody(ground, Shapes.Floor(100), Shapes.At(0, -0.5, 0));
        FrameHandle pit = h.AddFrame(ground, Shapes.At(40, 0, 0), FrameBounds.Sphere(10), MotionDriver.Static, gravity);
        h.AddBody(pit, Shapes.Floor(15), Shapes.At(0, -0.5, 0));
        AddCrates(h, pit, 15);
        for (int i = 0; i < 6; i++)
            h.AddBody(ground, Shapes.Box(0.4f, mass: 2), Shapes.At(0.3 * i, 1 + 1.1 * i, 0.1 * i));
        h.AddBody(ground, Shapes.Ball(0.5f), Shapes.At(28, 0.5, 0), velocity: new Vector3(6, 0, 0));

        var hashes = new List<ulong>();
        var capacities = new List<(uint, uint)>();
        h.AfterStep = output =>
        {
            hashes.Add(Hash(output));
            capacities.Add((h.Backend.CapacityOf(ground), h.Backend.CapacityOf(pit)));
        };
        for (int tick = 0; tick < 90; tick++)
        {
            if (tick == 20)
                for (int i = 0; i < 10; i++)
                    h.AddBody(ground, Shapes.Box(0.4f, mass: 2), Shapes.At(-5 + 0.2 * i, 1 + 1.1 * i, 0));
            h.Step();
        }
        return (hashes, capacities);
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
        var (firstHashes, firstCapacities) = RunWithGrowth();
        var (secondHashes, secondCapacities) = RunWithGrowth();

        firstCapacities[0].Should().Be(((uint)16, (uint)16));
        firstCapacities[^1].Should().Be(((uint)32, (uint)32), "both systems grew during the run");
        secondCapacities.Should().Equal(firstCapacities);
        secondHashes.Should().Equal(firstHashes);
    }

    // ---- Cost ----

    [Fact]
    public void GrowingAFrameOf4096BodiesTakesOneHitch()
    {
        var times = new List<double>();
        for (int run = 0; run < 3; run++)
        {
            using var h = new PhysicsHarness();
            FrameHandle hold = h.AddFrame(FrameHandle.Null, DPose.Identity, FrameBounds.Sphere(1e6f), MotionDriver.Static);
            // Half static crates and half balls, 2 m apart, so that nothing touches.
            BodyDesc ball = Shapes.Ball(0.4f);
            for (int i = 0; i < 4096; i++)
            {
                DPose pose = Shapes.At(2 * (i % 64), 2 * (i / 64 % 64), 0);
                h.AddBody(hold, i % 2 == 0 ? Crate : ball, pose);
            }
            h.Step();
            h.Backend.CapacityOf(hold).Should().Be(4096);

            h.AddBody(hold, Crate, Shapes.At(0, 0, 50));
            var watch = Stopwatch.StartNew();
            h.Sync();
            watch.Stop();

            h.Backend.CapacityOf(hold).Should().Be(8192);
            h.Backend.BodiesIn(hold).Should().HaveCount(4097);
            times.Add(watch.Elapsed.TotalMilliseconds);
        }
        TestContext.Current.TestOutputHelper?.WriteLine($"Growing a system of 4096 bodies to 8192 took {string.Join(", ", times.Select(t => $"{t:F2} ms"))}.");
    }
}
