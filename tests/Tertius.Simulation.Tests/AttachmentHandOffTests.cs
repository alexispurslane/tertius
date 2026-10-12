using System.Numerics;
using AwesomeAssertions;

namespace Tertius.Simulation.Tests;

/// <summary>
/// A child follows its parent into another frame when physics hands the parent off. The child has no body, so physics
/// never hands it off: the sync point derives its frame from its chain root, moves its topic membership, and tells it
/// with <see cref="FrameChanged"/> once. A physics double hands the parent off at tick 1, from the root to a station.
/// </summary>
public class AttachmentHandOffTests
{
    private static readonly Vector3 ParentVelocity = new(1, 0, 0);
    private static readonly Vector3 ParentSpin = new(0, 1, 0);

    private sealed class Scene : IDisposable
    {
        public Scene()
        {
            Test = new TestWorld(Physics);
            Station = Test.SpawnStaticFrame(TestWorld.At(500), radius: 100);
            Parent = Test.World.Spawn(new SpawnDesc
            {
                Frame = Test.Root,
                LinearVelocity = ParentVelocity,
                AngularVelocity = ParentSpin,
                Body = TestWorld.Ball,
            });
            ChildLog = new Recorder(EventMask.Of<FrameChanged>().And<Ping>());
            GrandchildLog = new Recorder(EventMask.Of<FrameChanged>());
            Child = Test.World.Spawn(new SpawnDesc
            {
                Frame = Test.Root,
                AttachTo = Parent,
                AttachOffset = new Pose(new Vector3(0, 0, 2), Quaternion.Identity),
                Components = [ChildLog],
            });
            Grandchild = Test.World.Spawn(new SpawnDesc
            {
                Frame = Test.Root,
                AttachTo = Child,
                AttachOffset = new Pose(new Vector3(0, 1, 0), Quaternion.Identity),
                Components = [GrandchildLog],
            });
            Physics.OnStep((context, output) =>
            {
                if (context.Tick == 1)
                    output.HandOffs.Add(new HandOff(Parent, Test.Root, Station, TestWorld.At(-50), ParentVelocity, ParentSpin));
            });
        }

        public IPhysicsBackend Physics { get; } = TestPhysics.Create();
        public TestWorld Test { get; }
        public FrameHandle Station { get; }
        public Entity Parent { get; }
        public Entity Child { get; }
        public Entity Grandchild { get; }
        public Recorder ChildLog { get; }
        public Recorder GrandchildLog { get; }
        public World World => Test.World;

        public void Dispose() => Test.Dispose();
    }

    [Fact]
    public void ChildrenStayInTheOldFrameUntilTheirParentIsHandedOff()
    {
        using var scene = new Scene();

        scene.Test.Run(1);

        scene.World.GetFrame(scene.Child).Should().Be(scene.Test.Root);
        scene.World.GetFrame(scene.Grandchild).Should().Be(scene.Test.Root);
    }

    [Fact]
    public void ChildrenFollowTheirParentsHandOffIntoTheNewFrame()
    {
        using var scene = new Scene();

        scene.Test.Run(2);

        scene.World.GetFrame(scene.Child).Should().Be(scene.Station);
        scene.World.GetFrame(scene.Grandchild).Should().Be(scene.Station);
    }

    [Fact]
    public void ChildrenKeepTheirOffsetFromTheirParentInTheNewFrame()
    {
        using var scene = new Scene();

        scene.Test.Run(2);

        // The parent is at (-50, 0, 0) in the station. The child is 2 m along +Z and the grandchild 1 m above that.
        PoseChecks.AssertNear(scene.World.PoseInOwnFrame(scene.Child).Position, new Double3(-50, 0, 2));
        PoseChecks.AssertNear(scene.World.PoseInOwnFrame(scene.Grandchild).Position, new Double3(-50, 1, 2));
    }

    [Fact]
    public void ChildrenTakeTheVelocityThatPhysicsGaveTheirParentInTheNewFrame()
    {
        using var scene = new Scene();

        scene.Test.Run(2);

        // v + w x r = (1, 0, 0) + (0, 1, 0) x (0, 0, 2) = (3, 0, 0).
        PoseChecks.AssertNear(scene.World.VelocityInOwnFrame(scene.Child), new Vector3(3, 0, 0));
    }

    [Fact]
    public void AChildHearsFrameChangedOnceWhenItsParentIsHandedOff()
    {
        using var scene = new Scene();

        scene.Test.Run(5);

        // The sync point of tick 1 derives the child, so the child handles the message in tick 2.
        (long tick, Event received) = scene.ChildLog.Seen.Should().ContainSingle(s => s.Event.Is<FrameChanged>()).Subject;
        tick.Should().Be(2);
        received.As<FrameChanged>().From.Should().Be(scene.Test.Root);
        received.As<FrameChanged>().To.Should().Be(scene.Station);
    }

    [Fact]
    public void AGrandchildHearsFrameChangedOnceToo()
    {
        using var scene = new Scene();

        scene.Test.Run(5);

        scene.GrandchildLog.Seen.Should().ContainSingle();
    }

    [Fact]
    public void AChildHearsTheTopicOfTheNewFrameAfterItsParentIsHandedOff()
    {
        using var scene = new Scene();
        scene.Test.SpawnWith(SendEvery.Of<Ping>(scene.Station.Topic, tick => new Ping { Value = (int)tick }));

        scene.Test.Run(5);

        // The sync point of tick 1 moves the child (step 7) before it routes messages (step 8). So the message sent in
        // tick 0, routed at the end of tick 0, misses the child, and the messages sent in ticks 1, 2 and 3 reach it.
        scene.ChildLog.Pings().Should().Equal((2L, 1), (3L, 2), (4L, 3));
    }
}
