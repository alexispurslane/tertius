using System.Numerics;
using AwesomeAssertions;

namespace Tertius.Simulation.Tests;

/// <summary>
/// A contact is physics telling the world that two bodies touched during a step. The world sends a
/// <see cref="Collision"/> to each body, with the point and the normal in that body's own frame. The contact names its
/// point and normal in the contact's frame, the frame whose physics system holds both bodies, and the normal points from
/// the first body toward the second, so the second body hears the opposite direction.
/// </summary>
public class PhysicsContactTests
{
    private static readonly Double3 ContactPoint = new(1, 2, 3);
    private static readonly Vector3 ContactNormal = new(1, 0, 0);
    private const float ContactImpulse = 5;

    /// <summary>
    /// Two balls in the root frame, each with a <see cref="Recorder"/> for <see cref="Collision"/>, and a physics double
    /// whose first step reports a contact between them.
    /// </summary>
    private sealed class Scene : IDisposable
    {
        public Scene()
        {
            Test = new TestWorld(Physics);
            FirstLog = new Recorder(EventMask.Of<Collision>());
            SecondLog = new Recorder(EventMask.Of<Collision>());
            First = Test.World.Spawn(new SpawnDesc { Frame = Test.Root, Body = TestWorld.Ball, Components = [FirstLog] });
            Second = Test.World.Spawn(new SpawnDesc { Frame = Test.Root, Body = TestWorld.Ball, Components = [SecondLog] });
            Physics.OnStep((context, output) =>
            {
                if (context.Tick == 0)
                    output.Contacts.Add(new ContactStarted(First, Second, Test.Root, ContactPoint, ContactNormal, ContactImpulse));
            });
        }

        public IPhysicsBackend Physics { get; } = TestPhysics.Create();
        public TestWorld Test { get; }
        public Recorder FirstLog { get; }
        public Recorder SecondLog { get; }
        public Entity First { get; }
        public Entity Second { get; }

        public void Dispose() => Test.Dispose();
    }

    [Fact]
    public void AContactSendsOneCollisionToEachBodyOnTheNextTick()
    {
        using var scene = new Scene();

        scene.Test.Run(3);

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

        scene.FirstLog.Seen.Single().Event.As<Collision>().Impulse.Should().Be(ContactImpulse);
        scene.SecondLog.Seen.Single().Event.As<Collision>().Impulse.Should().Be(ContactImpulse);
    }

    [Fact]
    public void TheFirstBodyHearsThePointAndNormalOfTheContact()
    {
        using var scene = new Scene();

        scene.Test.Run(2);

        Collision forFirst = scene.FirstLog.Seen.Single().Event.As<Collision>();
        forFirst.Point.Should().Be(ContactPoint);
        forFirst.Normal.Should().Be(ContactNormal);
    }

    [Fact]
    public void TheSecondBodyHearsTheNormalFlippedAndTheSamePointWhenBothAreInOneFrame()
    {
        using var scene = new Scene();

        scene.Test.Run(2);

        Collision forSecond = scene.SecondLog.Seen.Single().Event.As<Collision>();
        forSecond.Point.Should().Be(ContactPoint);
        forSecond.Normal.Should().Be(-ContactNormal);
    }

    [Fact]
    public void ABodyInAnotherFrameHearsThePointAndNormalInItsOwnFrame()
    {
        IPhysicsBackend physics = TestPhysics.Create();
        using var test = new TestWorld(physics);
        // A frame at (100, 0, 0), turned a quarter turn about +Y. The turn maps (x, y, z) to (z, y, -x), so its inverse
        // maps (x, y, z) to (-z, y, x).
        FrameHandle turned = test.SpawnStaticFrame(new DPose(new Double3(100, 0, 0), PoseChecks.QuarterTurnAboutY), radius: 50);
        var firstLog = new Recorder(EventMask.Of<Collision>());
        var secondLog = new Recorder(EventMask.Of<Collision>());
        Entity first = test.World.Spawn(new SpawnDesc { Frame = test.Root, Body = TestWorld.Ball, Components = [firstLog] });
        Entity second = test.World.Spawn(new SpawnDesc { Frame = turned, Body = TestWorld.Ball, Components = [secondLog] });
        physics.OnStep((context, output) =>
        {
            if (context.Tick == 0)
                output.Contacts.Add(new ContactStarted(first, second, test.Root, ContactPoint, ContactNormal, ContactImpulse));
        });

        test.Run(2);

        // For the first body nothing changes. For the second body, the point (1, 2, 3) - (100, 0, 0) = (-99, 2, 3)
        // turned back is (-3, 2, -99), and the flipped normal (-1, 0, 0) turned back is (0, 0, -1).
        Collision forFirst = firstLog.Seen.Single().Event.As<Collision>();
        forFirst.Point.Should().Be(ContactPoint);
        forFirst.Normal.Should().Be(ContactNormal);
        Collision forSecond = secondLog.Seen.Single().Event.As<Collision>();
        PoseChecks.AssertNear(forSecond.Point, new Double3(-3, 2, -99));
        PoseChecks.AssertNear(forSecond.Normal, new Vector3(0, 0, -1));
    }

    // Physics hears of every destroyed entity and frame before its next step, and an entity keeps its body for life. So a
    // contact names two different live entities with bodies and a live frame, unless the backend has a bug.
#if TERTIUS_CONTRACTS
    [Fact]
    public void AContactThatNamesNoFrameBreaksAContract()
    {
        using var scene = new Scene();
        scene.Physics.OnStep((context, output) =>
            output.Contacts.Add(new ContactStarted(scene.First, scene.Second, FrameHandle.Null, ContactPoint, ContactNormal, ContactImpulse)));

        Action tick = () => scene.Test.World.Tick();

        tick.Should().Throw<ContractException>();
    }

    [Fact]
    public void AContactInAFrameThatIsGoneBreaksAContract()
    {
        using var scene = new Scene();
        FrameHandle gone = scene.Test.SpawnStaticFrame(TestWorld.At(0, 0, 5000), radius: 10);
        scene.Test.World.Destroy(gone.Entity);
        scene.Physics.OnStep((context, output) =>
            output.Contacts.Add(new ContactStarted(scene.First, scene.Second, gone, ContactPoint, ContactNormal, ContactImpulse)));

        Action tick = () => scene.Test.World.Tick();

        tick.Should().Throw<ContractException>();
    }

    [Fact]
    public void AContactWithAnEntityThatNoLongerExistsBreaksAContract()
    {
        using var scene = new Scene();
        Entity gone = scene.Test.World.Spawn(new SpawnDesc { Frame = scene.Test.Root, Body = TestWorld.Ball });
        scene.Test.World.Destroy(gone);
        scene.Physics.OnStep((context, output) =>
            output.Contacts.Add(new ContactStarted(gone, scene.Second, scene.Test.Root, ContactPoint, ContactNormal, ContactImpulse)));

        Action tick = () => scene.Test.World.Tick();

        tick.Should().Throw<ContractException>();
    }

    [Fact]
    public void AContactWithAnEntityWithoutABodyBreaksAContract()
    {
        using var scene = new Scene();
        Entity bodiless = scene.Test.SpawnAt(new Double3(3, 0, 0));
        scene.Physics.OnStep((context, output) =>
            output.Contacts.Add(new ContactStarted(scene.First, bodiless, scene.Test.Root, ContactPoint, ContactNormal, ContactImpulse)));

        Action tick = () => scene.Test.World.Tick();

        tick.Should().Throw<ContractException>();
    }

    [Fact]
    public void AContactOfABodyWithItselfBreaksAContract()
    {
        using var scene = new Scene();
        scene.Physics.OnStep((context, output) =>
            output.Contacts.Add(new ContactStarted(scene.First, scene.First, scene.Test.Root, ContactPoint, ContactNormal, ContactImpulse)));

        Action tick = () => scene.Test.World.Tick();

        tick.Should().Throw<ContractException>().WithMessage($"*{scene.First} cannot touch itself*");
    }
#else
    [Fact]
    public void AContactThatNamesNoFrameIsReportedOnceAndReachesNeitherBody()
    {
        using var scene = new Scene();
        scene.Physics.OnStep((context, output) =>
        {
            if (context.Tick == 0)
                output.Contacts.Add(new ContactStarted(scene.Second, scene.First, FrameHandle.Null, ContactPoint, ContactNormal, ContactImpulse));
        });

        scene.Test.Run(2);

        // The scene's own contact comes first in the report, and it names the same two bodies.
        scene.Test.World.Errors.Should().ContainSingle().Which.Should().Contain("names no frame");
        scene.FirstLog.Seen.Should().BeEmpty();
        scene.SecondLog.Seen.Should().BeEmpty();
    }

    [Fact]
    public void AContactInAFrameThatIsGoneSkipsTheWholeUpdateOfBothBodies()
    {
        using var scene = new Scene();
        FrameHandle gone = scene.Test.SpawnStaticFrame(TestWorld.At(0, 0, 5000), radius: 10);
        scene.Test.World.Destroy(gone.Entity);
        scene.Physics.OnStep((context, output) =>
        {
            if (context.Tick != 0)
                return;
            output.Contacts.Add(new ContactStarted(scene.First, scene.Second, gone, ContactPoint, ContactNormal, ContactImpulse));
            output.Bodies.Add(new BodyState(scene.Second, scene.Test.Root, TestWorld.At(9), Vector3.Zero, Vector3.Zero));
        });

        scene.Test.Run(2);

        scene.Test.World.Errors.Should().ContainSingle().Which.Should().Contain($"stale frame {gone}");
        scene.FirstLog.Seen.Should().BeEmpty();
        scene.SecondLog.Seen.Should().BeEmpty();
        scene.Test.World.GetPose(scene.Second).Should().Be(DPose.Identity, "the body state of the same step is part of the skipped update");
    }

    [Fact]
    public void AContactWithAnEntityThatNoLongerExistsIsReportedOnceAndReachesNeitherBody()
    {
        IPhysicsBackend physics = TestPhysics.Create();
        using var test = new TestWorld(physics);
        var log = new Recorder(EventMask.Of<Collision>());
        Entity gone = test.World.Spawn(new SpawnDesc { Frame = test.Root, Body = TestWorld.Ball });
        Entity survivor = test.World.Spawn(new SpawnDesc { Frame = test.Root, Body = TestWorld.Ball, Components = [log] });
        test.World.Destroy(gone);
        physics.OnStep((context, output) =>
        {
            if (context.Tick == 0)
                output.Contacts.Add(new ContactStarted(gone, survivor, test.Root, ContactPoint, ContactNormal, ContactImpulse));
        });

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
        scene.Physics.OnStep((context, output) =>
        {
            if (context.Tick == 0)
                output.Contacts.Add(new ContactStarted(scene.First, bodiless, scene.Test.Root, ContactPoint, ContactNormal, ContactImpulse));
        });

        scene.Test.Run(2);

        scene.Test.World.Errors.Should().ContainSingle().Which.Should().Contain($"contact of {scene.First} and {bodiless}");
        log.Seen.Should().BeEmpty();
        scene.FirstLog.Seen.Should().BeEmpty();
    }

    [Fact]
    public void AContactOfABodyWithItselfIsReportedOnceAndReachesNoOne()
    {
        using var scene = new Scene();
        scene.Physics.OnStep((context, output) =>
        {
            if (context.Tick == 1)
                output.Contacts.Add(new ContactStarted(scene.First, scene.First, scene.Test.Root, ContactPoint, ContactNormal, ContactImpulse));
        });

        scene.Test.Run(3);

        scene.Test.World.Errors.Should().ContainSingle().Which.Should().Contain($"contact of {scene.First} and {scene.First}").And.Contain("cannot touch itself");
        scene.FirstLog.Seen.Should().ContainSingle("only the scene's own contact of tick 0 reaches the first body")
            .Which.Event.As<Collision>().Other.Should().Be(scene.Second);
    }

    [Fact]
    public void ABrokenContactOfTwoEntitiesWhoseUpdatesAreSkippedAlreadyIsReportedToo()
    {
        using var scene = new Scene();
        FrameHandle gone = scene.Test.SpawnStaticFrame(TestWorld.At(0, 0, 5000), radius: 10);
        scene.Test.World.Destroy(gone.Entity);
        scene.Physics.OnStep((context, output) =>
        {
            if (context.Tick != 0)
                return;
            output.Bodies.Add(new BodyState(scene.First, gone, TestWorld.At(9), Vector3.Zero, Vector3.Zero));
            output.Bodies.Add(new BodyState(scene.Second, gone, TestWorld.At(9), Vector3.Zero, Vector3.Zero));
            output.Contacts.Add(new ContactStarted(scene.First, scene.Second, gone, ContactPoint, ContactNormal, ContactImpulse));
        });

        scene.Test.Run(2);

        // The two body states and the contact are three broken outputs. The scene's own contact is sound.
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
        // The scene's contact is sound, but the same report puts the first body in a frame that is gone.
        scene.Physics.OnStep((context, output) =>
        {
            if (context.Tick == 0)
                output.Bodies.Add(new BodyState(scene.First, gone, TestWorld.At(9), Vector3.Zero, Vector3.Zero));
        });

        scene.Test.Run(2);

        scene.Test.World.Errors.Should().ContainSingle().Which.Should().Contain($"body state of {scene.First}");
        scene.FirstLog.Seen.Should().BeEmpty();
        scene.SecondLog.Seen.Should().BeEmpty("the contact is part of the first body's skipped update");
    }
#endif

    [Fact]
    public void ABodyThatDestroysItselfInTheTickOfTheContactDoesNotStopItsPartnerFromHearingIt()
    {
        IPhysicsBackend physics = TestPhysics.Create();
        using var test = new TestWorld(physics);
        var log = new Recorder(EventMask.Of<Collision>());
        Entity doomed = test.World.Spawn(new SpawnDesc
        {
            Frame = test.Root,
            Body = TestWorld.Ball,
            Components = [Lambda.OnTick((self, scoped ref events, scoped ref outbox) => self.Destroy())],
        });
        Entity partner = test.World.Spawn(new SpawnDesc { Frame = test.Root, Body = TestWorld.Ball, Components = [log] });
        physics.OnStep((context, output) =>
        {
            if (context.Tick == 0)
                output.Contacts.Add(new ContactStarted(doomed, partner, test.Root, ContactPoint, ContactNormal, ContactImpulse));
        });

        test.Run(2);

        // Physics reports the contact while both bodies exist. The doomed one dies at the same sync point, after the
        // collisions are queued, and the one for the partner is still delivered.
        test.World.IsAlive(doomed).Should().BeFalse();
        log.Seen.Single().Event.As<Collision>().Other.Should().Be(doomed);
        test.World.Errors.Should().BeEmpty();
    }
}
