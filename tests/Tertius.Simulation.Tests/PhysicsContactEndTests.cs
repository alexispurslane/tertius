using System.Numerics;
using AwesomeAssertions;

namespace Tertius.Simulation.Tests;

/// <summary>
/// An ended contact is physics telling the world that two bodies whose contact started stopped touching. The world sends
/// a <see cref="CollisionEnded"/> to each body that still exists, after the <see cref="Collision"/> events of the same
/// step. One of the two may be gone, because destroying a body ends its contacts. The world runs on real Jolt. A test of
/// an end that Jolt sends makes two balls touch and then part, and checks the world against the end that Jolt really sent,
/// which <see cref="FaultInjectingBackend"/> records. A test of an end that only a bug produces edits the output of one
/// step, through the same wrapper.
/// </summary>
public class PhysicsContactEndTests
{
    /// <summary>
    /// Two balls in the root frame that touch, each with a <see cref="Recorder"/> for <see cref="Collision"/> and
    /// <see cref="CollisionEnded"/>. The root frame has no gravity. The first ball is at the origin and the second one
    /// 1 m along +X, so the balls (radius 0.5 m) touch with a gap of 0. Jolt reports their contact in the first step.
    /// The balls move apart along X at <c>speedApart</c> m/s each, so with a speed above 0 the contact ends in a later
    /// step, and with a speed of 0 it goes on.
    /// </summary>
    private sealed class Scene : IDisposable
    {
        public Scene(float speedApart = 0)
        {
            Test = TestWorld.OnJolt(out FaultInjectingBackend faults);
            Faults = faults;
            FirstLog = new Recorder(Wants);
            SecondLog = new Recorder(Wants);
            First = Test.World.Spawn(new SpawnDesc
            {
                Frame = Test.Root,
                Pose = TestWorld.At(0),
                LinearVelocity = new Vector3(-speedApart, 0, 0),
                Body = WorldTesting.Ball(),
                Components = [FirstLog],
            });
            Second = Test.World.Spawn(new SpawnDesc
            {
                Frame = Test.Root,
                Pose = TestWorld.At(1),
                LinearVelocity = new Vector3(speedApart, 0, 0),
                Body = WorldTesting.Ball(),
                Components = [SecondLog],
            });
        }

        public static EventMask Wants => EventMask.Of<Collision>() | EventMask.Of<CollisionEnded>();

        public FaultInjectingBackend Faults { get; }
        public TestWorld Test { get; }
        public Recorder FirstLog { get; }
        public Recorder SecondLog { get; }
        public Entity First { get; }
        public Entity Second { get; }

        /// <summary>
        /// The number of the tick whose step Jolt reported the end of the contact of the two balls in. Jolt sent that end
        /// once. The first ball is the first body, because its entity is the lower one.
        /// </summary>
        public long TickOfEnd
        {
            get
            {
                var ends = Faults.Outputs.Zip(Faults.Steps, (output, step) => (step.Tick, output.ContactsEnded)).Where(step => step.ContactsEnded.Count > 0).ToList();
                ends.Should().ContainSingle();
                ends[0].ContactsEnded.Should().Equal(new ContactEnded(First, Second));
                return ends[0].Tick;
            }
        }

        public void Dispose() => Test.Dispose();
    }

    /// <summary>The ticks at which a recorder heard a <see cref="CollisionEnded"/>, with what it heard.</summary>
    private static List<(long Tick, CollisionEnded Ended)> EndsHeard(Recorder log) =>
        log.Seen.Where(seen => seen.Event.Is<CollisionEnded>()).Select(seen => (seen.Tick, seen.Event.As<CollisionEnded>())).ToList();

    [Fact]
    public void AnEndedContactSendsOneCollisionEndedToEachBodyOnTheNextTick()
    {
        using var scene = new Scene(speedApart: 1);

        scene.Test.Run(8);

        long tickOfEnd = scene.TickOfEnd;
        EndsHeard(scene.FirstLog).Select(heard => heard.Tick).Should().Equal(tickOfEnd + 1);
        EndsHeard(scene.SecondLog).Select(heard => heard.Tick).Should().Equal(tickOfEnd + 1);
        scene.Test.World.Errors.Should().BeEmpty();
    }

    [Fact]
    public void EachBodyHearsWhichBodyItStoppedTouching()
    {
        using var scene = new Scene(speedApart: 1);

        scene.Test.Run(8);

        scene.TickOfEnd.Should().BeGreaterThan(0, "the contact started in the first step, so it can end only in a later one");
        EndsHeard(scene.FirstLog).Single().Ended.Other.Should().Be(scene.Second);
        EndsHeard(scene.SecondLog).Single().Ended.Other.Should().Be(scene.First);
    }

    /// <summary>
    /// The first ball flies out of the bubble of a frame in the step of tick 0, and overlaps the second ball, a static one of
    /// the frame, which is behind it at the bubble's edge. In that step Jolt finds the contact of the two balls, and then
    /// hands the first ball off to the frame's parent, where the second ball has no body: a static body has no ghost in the
    /// parent of its frame (fork F3 of docs/plans/complete-ghosts.md). The first ball's sphere then ends 3.3 m outside
    /// the bubble, so it has no ghost in the frame, and nothing carries the contact on. So the one step sends the start of the
    /// contact and its end. (Before ter-imi5 the scene flew a ball into the bubble with a kinematic ball behind it, which
    /// now has a ghost in the frame that carries the contact, because the frame's content reaches it.)
    /// </summary>
    [Fact]
    public void AContactThatStartsAndEndsInOneStepArrivesAsTheStartAndThenTheEnd()
    {
        using var test = TestWorld.OnJolt(out FaultInjectingBackend faults);
        // The bubble has a radius of 100 m, so a ball of radius 0.5 m leaves it beyond 102.5 m from its center: its radius
        // and the exit margin of 2 m outside. At 60 m/s the first ball passes that in one step.
        FrameHandle station = test.SpawnStaticFrame(TestWorld.At(500), radius: 100);
        var firstLog = new Recorder(Scene.Wants);
        var secondLog = new Recorder(Scene.Wants);
        Entity first = test.World.Spawn(new SpawnDesc
        {
            Frame = station,
            Pose = TestWorld.At(101.8),
            LinearVelocity = new Vector3(60, 0, 0),
            Body = WorldTesting.Ball(),
            Components = [firstLog],
        });
        Entity second = test.World.Spawn(new SpawnDesc
        {
            Frame = station,
            Pose = TestWorld.At(100.9),
            Body = new BodyDesc(new SphereShapeDesc(0.5f), BodyMotion.Static),
            Components = [secondLog],
        });

        test.Run(2);

        PhysicsStepOutput firstStep = faults.Outputs[0];
        firstStep.HandOffs.Should().ContainSingle().Which.Entity.Should().Be(first);
        ContactStarted contact = firstStep.Contacts.Should().ContainSingle().Subject;
        (contact.A, contact.B).Should().Be((first, second));
        firstStep.ContactsEnded.Should().Equal(new ContactEnded(first, second));
        firstLog.Seen.Select(s => s.Event.Type).Should().Equal(EventType<Collision>.Id, EventType<CollisionEnded>.Id);
        secondLog.Seen.Select(s => s.Event.Type).Should().Equal(EventType<Collision>.Id, EventType<CollisionEnded>.Id);
        test.World.Errors.Should().BeEmpty();
    }

    /// <summary>
    /// The first ball is destroyed between ticks, which ends its contacts. Physics reports the end in its next step. The
    /// destroyed ball hears nothing, and its partner hears the end, which names the entity that is gone.
    /// </summary>
    [Fact]
    public void AnEndWhoseOtherBodyWasDestroyedReachesTheSurvivorOnly()
    {
        using var scene = new Scene();
        scene.Test.Run(3);
        scene.FirstLog.Seen.Select(s => s.Event.Type).Should().Equal(EventType<Collision>.Id);
        scene.SecondLog.Seen.Select(s => s.Event.Type).Should().Equal(EventType<Collision>.Id);
        scene.Faults.Outputs.SelectMany(output => output.ContactsEnded).Should().BeEmpty("the balls keep touching");
        scene.Test.World.Destroy(scene.First);

        scene.Test.Run(2);

        scene.Faults.Outputs.SelectMany(output => output.ContactsEnded).Should().Equal(new ContactEnded(scene.First, scene.Second));
        EndsHeard(scene.SecondLog).Single().Ended.Other.Should().Be(scene.First);
        scene.FirstLog.Seen.Select(s => s.Event.Type).Should().Equal([EventType<Collision>.Id], "the destroyed ball hears no end");
        scene.Test.World.Errors.Should().BeEmpty("a destroyed body ends its contacts, so the end names it");
    }

#if TERTIUS_CONTRACTS
    [Fact]
    public void AnEndOfABodyWithItselfBreaksAContract()
    {
        using var scene = new Scene();
        scene.Faults.EditNextOutput = output => output.ContactsEnded.Add(new ContactEnded(scene.First, scene.First));

        Action tick = () => scene.Test.World.Tick();

        tick.Should().Throw<ContractException>().WithMessage($"*{scene.First} cannot touch itself*");
    }
#else
    [Fact]
    public void AnEndOfABodyWithItselfIsReportedOnceAndReachesNoOne()
    {
        using var scene = new Scene();
        scene.Test.Run(2);
        scene.Faults.EditNextOutput = output => output.ContactsEnded.Add(new ContactEnded(scene.First, scene.First));

        scene.Test.Run(3);

        scene.Test.World.Errors.Should().ContainSingle().Which.Should().Contain($"end of the contact of {scene.First} and {scene.First}").And.Contain("cannot touch itself");
        EndsHeard(scene.FirstLog).Should().BeEmpty();
        EndsHeard(scene.SecondLog).Should().BeEmpty();
    }

    [Fact]
    public void AnEndOfAnEntityWhoseUpdateIsSkippedReachesNeitherBody()
    {
        using var scene = new Scene(speedApart: 1);
        FrameHandle gone = scene.Test.SpawnStaticFrame(TestWorld.At(0, 0, 5000), radius: 10);
        scene.Test.World.Destroy(gone.Entity);
        // The end is sound: it is the end that Jolt sent. But the same report puts the first body in a frame that is gone.
        scene.Faults.EditOutput = output =>
        {
            if (output.ContactsEnded.Count > 0)
                output.Bodies.Add(new BodyState(scene.First, gone, TestWorld.At(9), Vector3.Zero, Vector3.Zero));
        };

        scene.Test.Run(8);

        scene.TickOfEnd.Should().BeGreaterThan(0);
        scene.Test.World.Errors.Should().ContainSingle().Which.Should().Contain($"body state of {scene.First}");
        EndsHeard(scene.FirstLog).Should().BeEmpty();
        EndsHeard(scene.SecondLog).Should().BeEmpty("the end is part of the first body's skipped update");
    }
#endif
}
