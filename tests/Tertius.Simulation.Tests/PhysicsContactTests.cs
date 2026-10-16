using System.Numerics;
using AwesomeAssertions;

namespace Tertius.Simulation.Tests;

/// <summary>
/// A contact is physics telling the world that two bodies touched during a step. The world sends a
/// <see cref="Collision"/> to each body, with the point and the normal in that body's own frame. The contact names its
/// point and normal in the contact's frame, the frame whose physics system holds both bodies, and the normal points from
/// the first body toward the second, so the second body hears the opposite direction. The world runs on real Jolt. A test
/// of a valid contact starts two balls that overlap, so that Jolt reports the contact in the first step, and checks the
/// world against the contact that Jolt really sent. A test of a contact that only a bug produces adds it to the output of
/// one step, through <see cref="FaultInjectingBackend"/>.
/// </summary>
public class PhysicsContactTests
{
    /// <summary>The distance between the centers of the two balls of <see cref="Scene"/>, in meters. The balls have a radius of 0.5 m, so they overlap by 0.1 m.</summary>
    private const double Distance = 0.9;

    /// <summary>
    /// Two balls in the root frame that overlap, each with a <see cref="Recorder"/> for <see cref="Collision"/>. Jolt reports
    /// the contact between them in the first step. The first ball is at the origin and the second 0.9 m along +X.
    /// </summary>
    private sealed class Scene : IDisposable
    {
        public Scene()
        {
            Test = TestWorld.OnJolt(out FaultInjectingBackend faults);
            Faults = faults;
            FirstLog = new Recorder(EventMask.Of<Collision>());
            SecondLog = new Recorder(EventMask.Of<Collision>());
            First = Test.World.Spawn(new SpawnDesc { Frame = Test.Root, Pose = TestWorld.At(0), Body = WorldTesting.Ball(), Components = [FirstLog] });
            Second = Test.World.Spawn(new SpawnDesc { Frame = Test.Root, Pose = TestWorld.At(Distance), Body = WorldTesting.Ball(), Components = [SecondLog] });
        }

        public FaultInjectingBackend Faults { get; }
        public TestWorld Test { get; }
        public Recorder FirstLog { get; }
        public Recorder SecondLog { get; }
        public Entity First { get; }
        public Entity Second { get; }

        /// <summary>The contact between the two balls that Jolt sent in the first step. The first ball is the first body, because its entity is the lower one.</summary>
        public ContactStarted Contact
        {
            get
            {
                ContactStarted contact = Faults.Outputs[0].Contacts.Should().ContainSingle().Subject;
                contact.A.Should().Be(First);
                contact.B.Should().Be(Second);
                contact.Frame.Should().Be(Test.Root);
                return contact;
            }
        }

        public void Dispose() => Test.Dispose();
    }

    [Fact]
    public void AContactSendsOneCollisionToEachBodyOnTheNextTick()
    {
        using var scene = new Scene();

        scene.Test.Run(3);

        scene.Contact.Should().NotBeNull();
        scene.FirstLog.Seen.Select(s => s.Tick).Should().Equal(1L);
        scene.SecondLog.Seen.Select(s => s.Tick).Should().Equal(1L);
    }

    [Fact]
    public void EachBodyHearsWhichBodyItTouched()
    {
        using var scene = new Scene();

        scene.Test.Run(2);

        scene.FirstLog.Seen.Single().Event.As<Collision>().Other.Should().Be(scene.Second);
        scene.SecondLog.Seen.Single().Event.As<Collision>().Other.Should().Be(scene.First);
    }

    [Fact]
    public void BothBodiesHearTheSameImpulse()
    {
        using var scene = new Scene();

        scene.Test.Run(2);

        float impulse = scene.Contact.Impulse;
        scene.FirstLog.Seen.Single().Event.As<Collision>().Impulse.Should().Be(impulse);
        scene.SecondLog.Seen.Single().Event.As<Collision>().Impulse.Should().Be(impulse);
    }

    [Fact]
    public void TheFirstBodyHearsThePointAndNormalOfTheContact()
    {
        using var scene = new Scene();

        scene.Test.Run(2);

        ContactStarted contact = scene.Contact;
        PoseChecks.AssertNear(contact.Normal, Vector3.UnitX, 0.01f);
        contact.Point.X.Should().BeInRange(0, Distance, "the balls touch between their centers");
        Collision forFirst = scene.FirstLog.Seen.Single().Event.As<Collision>();
        forFirst.Point.Should().Be(contact.Point);
        forFirst.Normal.Should().Be(contact.Normal);
    }

    [Fact]
    public void TheSecondBodyHearsTheNormalFlippedAndTheSamePointWhenBothAreInOneFrame()
    {
        using var scene = new Scene();

        scene.Test.Run(2);

        ContactStarted contact = scene.Contact;
        Collision forSecond = scene.SecondLog.Seen.Single().Event.As<Collision>();
        forSecond.Point.Should().Be(contact.Point);
        forSecond.Normal.Should().Be(-contact.Normal);
    }

    // Jolt reports a contact only between two bodies that the same physics system holds, and not between a body and the ghost
    // of a body in another frame. So the bodies of a contact are in different frames only when one of them is the owner of the
    // contact's frame: the owner is in the parent frame, and its interior, the body that the crate touches, is in its own frame.
    // The ship is turned a quarter turn about +Y and is 100 m along +X in the root. A crate falls onto its deck.
    [Fact]
    public void ABodyInAnotherFrameHearsThePointAndNormalInItsOwnFrame()
    {
        using var test = TestWorld.OnJolt(out FaultInjectingBackend faults);
        var shipPose = new DPose(new Double3(100, 0, 0), PoseChecks.QuarterTurnAboutY);
        var deck = new CompoundShapeDesc([new CompoundChild(new BoxShapeDesc(new Vector3(5, 0.25f, 5)), new Pose(new Vector3(0, -2.25f, 0), Quaternion.Identity))]);
        var ownerLog = new Recorder(EventMask.Of<Collision>());
        var crateLog = new Recorder(EventMask.Of<Collision>());
        Entity owner = test.World.Spawn(new SpawnDesc
        {
            Frame = test.Root,
            Pose = shipPose,
            Body = new BodyDesc(new BoxShapeDesc(new Vector3(5, 3, 5)), InteriorShape: deck),
            AsFrame = new FrameSpec(FrameBounds.Sphere(30), MotionDriver.Static, GravityField.Uniform(new Vector3(0, -9.81f, 0))),
            Components = [ownerLog],
        });
        FrameHandle ship = test.World.GetOwnedFrame(owner);
        var crateBody = new BodyDesc(new BoxShapeDesc(new Vector3(0.5f)), Restitution: 0, LinearDamping: 0, AngularDamping: 0);
        Entity crate = test.World.Spawn(new SpawnDesc { Frame = ship, Pose = TestWorld.At(0, 0, 0), Body = crateBody, Components = [crateLog] });

        test.Run(90);

        ContactStarted contact = faults.Outputs.SelectMany(output => output.Contacts).Should().ContainSingle().Subject;
        contact.Frame.Should().Be(ship, "the interior of the ship and the crate are in the physics system of the ship");
        contact.A.Should().Be(owner);
        contact.B.Should().Be(crate);
        // The crate is in the frame of the contact, so it hears the contact as it is, with the normal flipped. The owner is
        // in the root, so it hears the point and the normal turned and moved from the ship into the root.
        Collision forCrate = crateLog.Seen.Single().Event.As<Collision>();
        forCrate.Point.Should().Be(contact.Point);
        forCrate.Normal.Should().Be(-contact.Normal);
        Collision forOwner = ownerLog.Seen.Single().Event.As<Collision>();
        PoseChecks.AssertNear(forOwner.Point, shipPose.Position + shipPose.Rotation.Rotate(contact.Point), 1e-6);
        PoseChecks.AssertNear(forOwner.Normal, shipPose.Rotation.Rotate(contact.Normal), 1e-5f);
        PoseChecks.AssertNear(contact.Normal, Vector3.UnitY, 0.01f);
        forOwner.Point.X.Should().BeInRange(95, 105, "the crate fell onto the deck, near the center of the ship");
    }

    // Physics hears of every destroyed entity and frame before its next step, and an entity keeps its body for life. So a
    // contact names two different live entities with bodies and a live frame, unless the backend has a bug.
#if TERTIUS_CONTRACTS
    [Fact]
    public void AContactThatNamesNoFrameBreaksAContract()
    {
        using var scene = new Scene();
        scene.Faults.EditOutput = output =>
            output.Contacts.Add(new ContactStarted(scene.First, scene.Second, FrameHandle.Null, new Double3(1, 2, 3), Vector3.UnitX, 5));

        Action tick = () => scene.Test.World.Tick();

        tick.Should().Throw<ContractException>();
    }

    [Fact]
    public void AContactInAFrameThatIsGoneBreaksAContract()
    {
        using var scene = new Scene();
        FrameHandle gone = scene.Test.SpawnStaticFrame(TestWorld.At(0, 0, 5000), radius: 10);
        scene.Test.World.Destroy(gone.Entity);
        scene.Faults.EditOutput = output =>
            output.Contacts.Add(new ContactStarted(scene.First, scene.Second, gone, new Double3(1, 2, 3), Vector3.UnitX, 5));

        Action tick = () => scene.Test.World.Tick();

        tick.Should().Throw<ContractException>();
    }

    [Fact]
    public void AContactWithAnEntityThatNoLongerExistsBreaksAContract()
    {
        using var scene = new Scene();
        Entity gone = scene.Test.World.Spawn(new SpawnDesc { Frame = scene.Test.Root, Pose = TestWorld.At(0, 50), Body = WorldTesting.Ball() });
        scene.Test.World.Destroy(gone);
        scene.Faults.EditOutput = output =>
            output.Contacts.Add(new ContactStarted(gone, scene.Second, scene.Test.Root, new Double3(1, 2, 3), Vector3.UnitX, 5));

        Action tick = () => scene.Test.World.Tick();

        tick.Should().Throw<ContractException>();
    }

    [Fact]
    public void AContactWithAnEntityWithoutABodyBreaksAContract()
    {
        using var scene = new Scene();
        Entity bodiless = scene.Test.SpawnAt(new Double3(3, 0, 0));
        scene.Faults.EditOutput = output =>
            output.Contacts.Add(new ContactStarted(scene.First, bodiless, scene.Test.Root, new Double3(1, 2, 3), Vector3.UnitX, 5));

        Action tick = () => scene.Test.World.Tick();

        tick.Should().Throw<ContractException>();
    }

    [Fact]
    public void AContactOfABodyWithItselfBreaksAContract()
    {
        using var scene = new Scene();
        scene.Faults.EditOutput = output =>
            output.Contacts.Add(new ContactStarted(scene.First, scene.First, scene.Test.Root, new Double3(1, 2, 3), Vector3.UnitX, 5));

        Action tick = () => scene.Test.World.Tick();

        tick.Should().Throw<ContractException>().WithMessage($"*{scene.First} cannot touch itself*");
    }
#else
    [Fact]
    public void AContactThatNamesNoFrameIsReportedOnceAndReachesNeitherBody()
    {
        using var scene = new Scene();
        scene.Faults.EditOutputOfTick(0, output =>
            output.Contacts.Add(new ContactStarted(scene.Second, scene.First, FrameHandle.Null, new Double3(1, 2, 3), Vector3.UnitX, 5)));

        scene.Test.Run(2);

        // The contact that Jolt sent comes first in the report, and it names the same two bodies.
        scene.Test.World.Errors.Should().ContainSingle().Which.Should().Contain("names no frame");
        scene.Faults.Outputs[0].Contacts.Should().HaveCount(2);
        scene.FirstLog.Seen.Should().BeEmpty();
        scene.SecondLog.Seen.Should().BeEmpty();
    }

    [Fact]
    public void AContactInAFrameThatIsGoneSkipsTheWholeUpdateOfBothBodies()
    {
        using var scene = new Scene();
        FrameHandle gone = scene.Test.SpawnStaticFrame(TestWorld.At(0, 0, 5000), radius: 10);
        scene.Test.World.Destroy(gone.Entity);
        scene.Faults.EditOutputOfTick(0, output =>
            output.Contacts.Add(new ContactStarted(scene.First, scene.Second, gone, new Double3(1, 2, 3), Vector3.UnitX, 5)));

        scene.Test.Run(1);

        // Jolt pushed the overlapping balls apart in the step, and sent the new state of the second ball. It is part of the skipped update.
        scene.Faults.Outputs[0].Bodies.Single(state => state.Entity == scene.Second).Pose.Position.X.Should().BeGreaterThan(Distance);
        scene.Test.World.PoseInOwnFrame(scene.Second).Should().Be(TestWorld.At(Distance), "the body state of the same step is part of the skipped update");
        scene.Test.Run(1);
        scene.Test.World.Errors.Should().ContainSingle().Which.Should().Contain($"stale frame {gone}");
        scene.FirstLog.Seen.Should().BeEmpty();
        scene.SecondLog.Seen.Should().BeEmpty();
    }

    [Fact]
    public void AContactWithAnEntityThatNoLongerExistsIsReportedOnceAndReachesNeitherBody()
    {
        using var test = TestWorld.OnJolt(out FaultInjectingBackend faults);
        var log = new Recorder(EventMask.Of<Collision>());
        Entity gone = test.World.Spawn(new SpawnDesc { Frame = test.Root, Body = WorldTesting.Ball() });
        Entity survivor = test.World.Spawn(new SpawnDesc { Frame = test.Root, Pose = TestWorld.At(0, 5), Body = WorldTesting.Ball(), Components = [log] });
        test.World.Destroy(gone);
        faults.EditOutputOfTick(0, output =>
            output.Contacts.Add(new ContactStarted(gone, survivor, test.Root, new Double3(1, 2, 3), Vector3.UnitX, 5)));

        test.Run(2);

        test.World.Errors.Should().ContainSingle().Which.Should().Contain($"contact of {gone} and {survivor}");
        log.Seen.Should().BeEmpty();
    }

    [Fact]
    public void AContactWithAnEntityWithoutABodyIsReportedOnceAndReachesNeitherEntity()
    {
        using var scene = new Scene();
        var log = new Recorder(EventMask.Of<Collision>());
        Entity bodiless = scene.Test.SpawnAt(new Double3(3, 0, 0), log);
        scene.Faults.EditOutputOfTick(0, output =>
            output.Contacts.Add(new ContactStarted(scene.First, bodiless, scene.Test.Root, new Double3(1, 2, 3), Vector3.UnitX, 5)));

        scene.Test.Run(2);

        scene.Test.World.Errors.Should().ContainSingle().Which.Should().Contain($"contact of {scene.First} and {bodiless}");
        log.Seen.Should().BeEmpty();
        scene.FirstLog.Seen.Should().BeEmpty();
    }

    [Fact]
    public void AContactOfABodyWithItselfIsReportedOnceAndReachesNoOne()
    {
        using var scene = new Scene();
        scene.Faults.EditOutputOfTick(1, output =>
            output.Contacts.Add(new ContactStarted(scene.First, scene.First, scene.Test.Root, new Double3(1, 2, 3), Vector3.UnitX, 5)));

        scene.Test.Run(3);

        scene.Test.World.Errors.Should().ContainSingle().Which.Should().Contain($"contact of {scene.First} and {scene.First}").And.Contain("cannot touch itself");
        scene.FirstLog.Seen.Should().ContainSingle("only the contact of tick 0 reaches the first body")
            .Which.Event.As<Collision>().Other.Should().Be(scene.Second);
    }

    [Fact]
    public void ABrokenContactOfTwoEntitiesWhoseUpdatesAreSkippedAlreadyIsReportedToo()
    {
        using var scene = new Scene();
        FrameHandle gone = scene.Test.SpawnStaticFrame(TestWorld.At(0, 0, 5000), radius: 10);
        scene.Test.World.Destroy(gone.Entity);
        scene.Faults.EditOutputOfTick(0, output =>
        {
            output.Bodies.Add(new BodyState(scene.First, gone, TestWorld.At(9), Vector3.Zero, Vector3.Zero));
            output.Bodies.Add(new BodyState(scene.Second, gone, TestWorld.At(9), Vector3.Zero, Vector3.Zero));
            output.Contacts.Add(new ContactStarted(scene.First, scene.Second, gone, new Double3(1, 2, 3), Vector3.UnitX, 5));
        });

        scene.Test.Run(2);

        // The two body states and the contact are three broken outputs. The contact that Jolt sent is sound.
        scene.Test.World.Errors.Should().HaveCount(3);
        scene.Test.World.Errors.Should().ContainSingle(error => error.Contains($"contact of {scene.First} and {scene.Second}"));
        scene.FirstLog.Seen.Should().BeEmpty();
    }

    [Fact]
    public void AContactOfAnEntityWhoseUpdateIsSkippedReachesNeitherBody()
    {
        using var scene = new Scene();
        FrameHandle gone = scene.Test.SpawnStaticFrame(TestWorld.At(0, 0, 5000), radius: 10);
        scene.Test.World.Destroy(gone.Entity);
        // The contact that Jolt sent is sound, but the same report puts the first body in a frame that is gone.
        scene.Faults.EditOutputOfTick(0, output =>
            output.Bodies.Add(new BodyState(scene.First, gone, TestWorld.At(9), Vector3.Zero, Vector3.Zero)));

        scene.Test.Run(2);

        scene.Test.World.Errors.Should().ContainSingle().Which.Should().Contain($"body state of {scene.First}");
        scene.Faults.Outputs[0].Contacts.Should().ContainSingle("Jolt did send the contact");
        scene.FirstLog.Seen.Should().BeEmpty();
        scene.SecondLog.Seen.Should().BeEmpty("the contact is part of the first body's skipped update");
    }
#endif

    [Fact]
    public void ABodyThatDestroysItselfInTheTickOfTheContactDoesNotStopItsPartnerFromHearingIt()
    {
        using var test = TestWorld.OnJolt(out FaultInjectingBackend faults);
        var log = new Recorder(EventMask.Of<Collision>());
        Entity doomed = test.World.Spawn(new SpawnDesc
        {
            Frame = test.Root,
            Body = WorldTesting.Ball(),
            Components = [Lambda.OnTick((self, scoped ref events, scoped ref outbox) => self.Destroy())],
        });
        Entity partner = test.World.Spawn(new SpawnDesc { Frame = test.Root, Pose = TestWorld.At(Distance), Body = WorldTesting.Ball(), Components = [log] });

        test.Run(2);

        // Jolt reports the contact while both bodies exist. The doomed one dies at the same sync point, after the
        // collisions are queued, and the one for the partner is still delivered.
        faults.Outputs[0].Contacts.Should().ContainSingle().Which.A.Should().Be(doomed);
        test.World.IsAlive(doomed).Should().BeFalse();
        log.Seen.Single().Event.As<Collision>().Other.Should().Be(doomed);
        test.World.Errors.Should().BeEmpty();
    }
}
