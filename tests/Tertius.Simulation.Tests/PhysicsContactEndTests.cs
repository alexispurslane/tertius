using System.Numerics;
using AwesomeAssertions;

namespace Tertius.Simulation.Tests;

/// <summary>
/// An ended contact is physics telling the world that two bodies whose contact started stopped touching. The world sends
/// a <see cref="CollisionEnded"/> to each body that still exists, after the <see cref="Collision"/> events of the same
/// step. One of the two may be gone, because destroying a body ends its contacts.
/// </summary>
public class PhysicsContactEndTests
{
    /// <summary>
    /// Two balls in the root frame, each with a <see cref="Recorder"/> for <see cref="Collision"/> and
    /// <see cref="CollisionEnded"/>, and a physics double whose first step reports that their contact ended.
    /// </summary>
    private sealed class Scene : IDisposable
    {
        public Scene()
        {
            Test = new TestWorld(Physics);
            FirstLog = new Recorder(Wants);
            SecondLog = new Recorder(Wants);
            First = Test.World.Spawn(new SpawnDesc { Frame = Test.Root, Body = TestWorld.Ball, Components = [FirstLog] });
            Second = Test.World.Spawn(new SpawnDesc { Frame = Test.Root, Body = TestWorld.Ball, Components = [SecondLog] });
        }

        public static EventMask Wants => EventMask.Of<Collision>() | EventMask.Of<CollisionEnded>();

        public IPhysicsBackend Physics { get; } = TestPhysics.Create();
        public TestWorld Test { get; }
        public Recorder FirstLog { get; }
        public Recorder SecondLog { get; }
        public Entity First { get; }
        public Entity Second { get; }

        /// <summary>Reports in the step of tick 0 that the contact of the two balls ended.</summary>
        public void EndOnFirstStep() => Physics.OnStep((context, output) =>
        {
            if (context.Tick == 0)
                output.ContactsEnded.Add(new ContactEnded(First, Second));
        });

        public void Dispose() => Test.Dispose();
    }

    [Fact]
    public void AnEndedContactSendsOneCollisionEndedToEachBodyOnTheNextTick()
    {
        using var scene = new Scene();
        scene.EndOnFirstStep();

        scene.Test.Run(3);

        scene.FirstLog.Seen.Should().ContainSingle().Which.Tick.Should().Be(1);
        scene.SecondLog.Seen.Should().ContainSingle().Which.Tick.Should().Be(1);
        scene.Test.World.Errors.Should().BeEmpty();
    }

    [Fact]
    public void EachBodyHearsWhichBodyItStoppedTouching()
    {
        using var scene = new Scene();
        scene.EndOnFirstStep();

        scene.Test.Run(2);

        scene.FirstLog.Seen.Single().Event.As<CollisionEnded>().Other.Should().Be(scene.Second);
        scene.SecondLog.Seen.Single().Event.As<CollisionEnded>().Other.Should().Be(scene.First);
    }

    [Fact]
    public void AContactThatStartsAndEndsInOneStepArrivesAsTheStartAndThenTheEnd()
    {
        using var scene = new Scene();
        scene.Physics.OnStep((context, output) =>
        {
            if (context.Tick != 0)
                return;
            output.ContactsEnded.Add(new ContactEnded(scene.First, scene.Second));
            output.Contacts.Add(new ContactStarted(scene.First, scene.Second, scene.Test.Root, Double3.Zero, Vector3.UnitX, 1));
        });

        scene.Test.Run(2);

        scene.FirstLog.Seen.Select(s => s.Event.Type).Should().Equal(EventType<Collision>.Id, EventType<CollisionEnded>.Id);
        scene.SecondLog.Seen.Select(s => s.Event.Type).Should().Equal(EventType<Collision>.Id, EventType<CollisionEnded>.Id);
    }

    /// <summary>
    /// The first ball is destroyed between ticks, which ends its contacts. Physics reports the end in its next step. The
    /// destroyed ball hears nothing, and its partner hears the end, which names the entity that is gone.
    /// </summary>
    [Fact]
    public void AnEndWhoseOtherBodyWasDestroyedReachesTheSurvivorOnly()
    {
        using var scene = new Scene();
        scene.EndOnFirstStep();
        scene.Test.World.Destroy(scene.First);

        scene.Test.Run(2);

        scene.SecondLog.Seen.Single().Event.As<CollisionEnded>().Other.Should().Be(scene.First);
        scene.FirstLog.Seen.Should().BeEmpty();
        scene.Test.World.Errors.Should().BeEmpty("a destroyed body ends its contacts, so the end names it");
    }

#if TERTIUS_CONTRACTS
    [Fact]
    public void AnEndOfABodyWithItselfBreaksAContract()
    {
        using var scene = new Scene();
        scene.Physics.OnStep((context, output) => output.ContactsEnded.Add(new ContactEnded(scene.First, scene.First)));

        Action tick = () => scene.Test.World.Tick();

        tick.Should().Throw<ContractException>().WithMessage($"*{scene.First} cannot touch itself*");
    }
#else
    [Fact]
    public void AnEndOfABodyWithItselfIsReportedOnceAndReachesNoOne()
    {
        using var scene = new Scene();
        scene.Physics.OnStep((context, output) =>
        {
            if (context.Tick == 0)
                output.ContactsEnded.Add(new ContactEnded(scene.First, scene.First));
        });

        scene.Test.Run(2);

        scene.Test.World.Errors.Should().ContainSingle().Which.Should().Contain($"end of the contact of {scene.First} and {scene.First}").And.Contain("cannot touch itself");
        scene.FirstLog.Seen.Should().BeEmpty();
    }

    [Fact]
    public void AnEndOfAnEntityWhoseUpdateIsSkippedReachesNeitherBody()
    {
        using var scene = new Scene();
        FrameHandle gone = scene.Test.SpawnStaticFrame(TestWorld.At(0, 0, 5000), radius: 10);
        scene.Test.World.Destroy(gone.Entity);
        // The end is sound, but the same report puts the first body in a frame that is gone.
        scene.Physics.OnStep((context, output) =>
        {
            if (context.Tick != 0)
                return;
            output.ContactsEnded.Add(new ContactEnded(scene.First, scene.Second));
            output.Bodies.Add(new BodyState(scene.First, gone, TestWorld.At(9), Vector3.Zero, Vector3.Zero));
        });

        scene.Test.Run(2);

        scene.Test.World.Errors.Should().ContainSingle().Which.Should().Contain($"body state of {scene.First}");
        scene.FirstLog.Seen.Should().BeEmpty();
        scene.SecondLog.Seen.Should().BeEmpty("the end is part of the first body's skipped update");
    }
#endif
}
