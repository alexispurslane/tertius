using System.Numerics;
using AwesomeAssertions;

namespace Tertius.Simulation.Tests;

/// <summary>
/// A child follows its parent into another frame when physics hands the parent off. The child has no body, so physics
/// never hands it off: the sync point derives its frame from its chain root, moves its topic membership, and tells it
/// with <see cref="FrameChanged"/> once. The parent is a ball that flies from the root into a station, and Jolt hands it off in the
/// step of tick 1.
/// </summary>
public class AttachmentHandOffTests
{
    private static readonly Vector3 ParentSpin = new(0, 1, 0);

    private sealed class Scene : IDisposable
    {
        public Scene()
        {
            Test = TestWorld.OnJolt(out FaultInjectingBackend faults);
            Faults = faults;
            Station = Test.SpawnStaticFrame(TestWorld.At(500), radius: 100);
            Scenes.Approach flight = Scenes.ApproachToBubble(Test.World, Station, tick: 1);
            Parent = Test.World.Spawn(new()
            {
                Frame = Test.Root,
                Pose = flight.Pose,
                LinearVelocity = flight.Velocity,
                AngularVelocity = ParentSpin,
                Body = WorldTesting.Ball()
            });
            ChildLog = new(EventMask.Of<FrameChanged>().And<Ping>());
            GrandchildLog = new(EventMask.Of<FrameChanged>());
            Child = Test.World.Spawn(new()
            {
                Frame = Test.Root,
                AttachTo = Parent,
                AttachOffset = new(new(0, 0, 2), Quaternion.Identity),
                Components = [ChildLog]
            });
            Grandchild = Test.World.Spawn(new()
            {
                Frame = Test.Root,
                AttachTo = Child,
                AttachOffset = new(new(0, 1, 0), Quaternion.Identity),
                Components = [GrandchildLog]
            });
        }

        public FaultInjectingBackend Faults { get; }
        public TestWorld Test { get; }
        public FrameHandle Station { get; }
        public Entity Parent { get; }
        public Entity Child { get; }
        public Entity Grandchild { get; }
        public Recorder ChildLog { get; }
        public Recorder GrandchildLog { get; }
        public World World => Test.World;

        /// <summary>The hand-off of the parent that Jolt sent in the step of tick 1, with the pose and velocities of the parent in the station.</summary>
        public HandOff ParentHandOff => Faults.Outputs[1].HandOffs.Should().ContainSingle().Subject;

        /// <summary>Where a point is in the station, if it is <paramref name="offset"/> from the parent in the parent's axes.</summary>
        public Double3 InStation(Vector3 offset) =>
            ParentHandOff.Pose.Position + ParentHandOff.Pose.Rotation.Rotate(offset);

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

        // The child is 2 m along the parent's +Z and the grandchild 1 m along the child's +Y. The parent has turned a little
        // in its two steps, and so have they.
        scene.ParentHandOff.Entity.Should().Be(scene.Parent);
        PoseChecks.AssertNear(scene.World.PoseInOwnFrame(scene.Child).Position, scene.InStation(new(0, 0, 2)), 1e-6);
        PoseChecks.AssertNear(scene.World.PoseInOwnFrame(scene.Grandchild).Position, scene.InStation(new(0, 1, 2)),
            1e-6);
    }

    [Fact]
    public void ChildrenTakeTheVelocityThatPhysicsGaveTheirParentInTheNewFrame()
    {
        using var scene = new Scene();

        scene.Test.Run(2);

        // v + w x r, with r the lever arm of 2 m along the parent's +Z in the axes of the station.
        HandOff handOff = scene.ParentHandOff;
        Vector3 leverArm = handOff.Pose.Rotation.Rotate(new(0, 0, 2));
        PoseChecks.AssertNear(scene.World.VelocityInOwnFrame(scene.Child),
            handOff.LinearVelocity + Vector3.Cross(handOff.AngularVelocity, leverArm));
    }

    [Fact]
    public void AChildHearsFrameChangedOnceWhenItsParentIsHandedOff()
    {
        using var scene = new Scene();

        scene.Test.Run(5);

        // The sync point of tick 1 derives the child, so the child handles the message in tick 2.
        (long tick, Event received) =
            scene.ChildLog.Seen.Should().ContainSingle(s => s.Event.Is<FrameChanged>()).Subject;
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
        scene.Test.SpawnWith(SendEvery.Of(scene.Station.Topic, tick => new Ping { Value = (int)tick }));

        scene.Test.Run(5);

        // The sync point of tick 1 moves the child (step 7) before it routes messages (step 8). So the message sent in
        // tick 0, routed at the end of tick 0, misses the child, and the messages sent in ticks 1, 2 and 3 reach it.
        scene.ChildLog.Pings().Should().Equal((2L, 1), (3L, 2), (4L, 3));
    }
}
