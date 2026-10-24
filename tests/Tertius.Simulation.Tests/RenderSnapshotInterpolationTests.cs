using System.Numerics;
using AwesomeAssertions;
using Tertius.Frames.Rails;
using static Tertius.Simulation.Tests.RenderSnapshotSupport;

namespace Tertius.Simulation.Tests;

/// <summary>
/// Each instance and the camera carry the pose of the previous snapshot as well as the pose of this one, so that the
/// renderer can draw a frame between two ticks. Both poses are relative to the render origin of this snapshot, the camera
/// position of this tick, so that the difference between them is the movement relative to the camera. The renderer
/// interpolates only when the same camera was in the same frame at the previous snapshot. Otherwise the previous pose is
/// the current pose. An object that is new has no previous pose either.
/// </summary>
public class RenderSnapshotInterpolationTests
{
    [Fact]
    public void TheFirstSnapshotHasNothingToInterpolateFrom()
    {
        using var test = new TestWorld();
        Entity moving = test.Show(test.Root, TestWorld.At(10, 0, -5), new Mover(new(1, 0, 0)));
        test.World.Camera = test.SpawnCamera(test.Root, DPose.Identity, new Mover(new(0, 1, 0)));

        test.Run(1);

        RenderInstance instance = test.World.Latest().InstanceOf(moving);
        instance.PreviousPosition.Should().Be(instance.Position);
    }

    [Fact]
    public void ThePreviousPositionOfAMovingObjectIsRelativeToTheCurrentOrigin()
    {
        using var test = new TestWorld();
        Entity moving = test.Show(test.Root, TestWorld.At(10, 0, -5), new Mover(new(1, 0, 0)));
        test.World.Camera = test.SpawnCamera(test.Root, DPose.Identity, new Mover(new(0, 1, 0)));
        test.Run(1);
        test.World.Latest();

        test.Run(1);

        // After tick 1 the object is at (12, 0, -5) and the camera at (0, 2, 0). A tick earlier the object was at (11, 0, -5).
        RenderInstance instance = test.World.Latest().InstanceOf(moving);
        instance.Position.Should().Be(new Vector3(12, -2, -5));
        instance.PreviousPosition.Should().Be(new Vector3(11, -2, -5));
    }

    [Fact]
    public void AnObjectThatDoesNotMoveHasTheSamePreviousAndCurrentPositionWhileTheCameraMoves()
    {
        using var test = new TestWorld();
        Entity still = test.Show(test.Root, TestWorld.At(0, 0, -10));
        test.World.Camera = test.SpawnCamera(test.Root, DPose.Identity, new Mover(new(0, 1, 0)));
        test.Run(1);
        test.World.Latest();

        test.Run(1);

        RenderInstance instance = test.World.Latest().InstanceOf(still);
        instance.Position.Should().Be(new Vector3(0, -2, -10));
        instance.PreviousPosition.Should().Be(instance.Position);
    }

    [Fact]
    public void ThePreviousPositionOfTheCameraIsRelativeToTheCurrentOrigin()
    {
        using var test = new TestWorld();
        test.World.Camera = test.SpawnCamera(test.Root, DPose.Identity, new Mover(new(0, 1, 0)));
        test.Run(1);
        test.World.Latest();

        test.Run(1);

        RenderCamera camera = test.World.Latest().Camera;
        camera.Position.Should().Be(Vector3.Zero);
        camera.PreviousPosition.Should().Be(new Vector3(0, -1, 0));
    }

    [Fact]
    public void ThePreviousRotationOfAnObjectIsItsRotationAtTheEndOfThePreviousTick()
    {
        using var test = new TestWorld();
        // The object is turned by 0.5 rad about +Y at the end of tick 0 and by 1.0 rad at the end of tick 1.
        Entity turning = test.Show(test.Root, TestWorld.At(0, 0, -10),
            Lambda.OnTick((self, scoped ref events, scoped ref outbox) =>
                self.SetTransform(self.PoseInOwnFrame() with
                {
                    Rotation = DQuaternion.FromAxisAngle(Double3.UnitY, 0.5 * (self.Tick + 1))
                })));
        test.World.Camera = test.SpawnCamera(test.Root, DPose.Identity);
        test.Run(1);
        test.World.Latest();

        test.Run(1);

        RenderInstance instance = test.World.Latest().InstanceOf(turning);
        PoseChecks.AssertSameRotation(instance.Rotation, DQuaternion.FromAxisAngle(Double3.UnitY, 1.0));
        PoseChecks.AssertSameRotation(instance.PreviousRotation, DQuaternion.FromAxisAngle(Double3.UnitY, 0.5));
    }

    [Fact]
    public void AnObjectThatAppearsInTheWorldHasNoPreviousPoseInItsFirstSnapshot()
    {
        using var test = new TestWorld();
        test.World.Camera = test.SpawnCamera(test.Root, DPose.Identity);
        test.Run(2);
        test.World.Latest();
        Entity newcomer = test.Show(test.Root, TestWorld.At(10, 0, -5), new Mover(new(1, 0, 0)));

        test.Run(1);
        RenderInstance first = test.World.Latest().InstanceOf(newcomer);
        test.Run(1);
        RenderInstance second = test.World.Latest().InstanceOf(newcomer);

        first.PreviousPosition.Should().Be(first.Position, "a new object has nothing to interpolate from");
        second.PreviousPosition.Should().Be(first.Position, "the second snapshot interpolates from the first");
    }

    [Fact]
    public void AnObjectInAStaticFrameThatMovesCarriesItsPreviousPoseAlongWithTheFrame()
    {
        using var test = new TestWorld();
        FrameHandle hangar = test.World.GetOwnedFrame(test.World.Spawn(new()
        {
            Frame = test.Root,
            AsFrame = new(FrameBounds.Sphere(100)),
            Components = [new Mover(new(1, 0, 0))]
        }));
        Entity crate = test.Show(hangar, TestWorld.At(0, 0, -10));
        test.World.Camera = test.SpawnCamera(test.Root, DPose.Identity);
        test.Run(1);
        test.World.Latest();

        test.Run(1);

        // The crate sits still in its frame, and the frame moved from x = 1 to x = 2: the crate moved with it.
        RenderInstance instance = test.World.Latest().InstanceOf(crate);
        instance.Position.Should().Be(new Vector3(2, 0, -10));
        instance.PreviousPosition.Should().Be(new Vector3(1, 0, -10));
    }

    [Fact]
    public void AnObjectInAFrameThatOrbitsMovesByRoughlyTheOrbitalSpeedTimesTheTimeStep()
    {
        const double mu = 3.986004418e14, radius = 7e6;
        KeplerOrbit orbit = KeplerOrbit.Circular(radius);
        using var test = new TestWorld();
        test.GiveRootPointGravity(mu);
        FrameHandle moon = test.World.GetOwnedFrame(test.World.Spawn(new SpawnDesc
        {
            Frame = test.Root,
            AsFrame = new(FrameBounds.Sphere(1e5f))
        }.OnKeplerOrbit(test.World, orbit)));
        Entity satellite = test.Show(moon, TestWorld.At(100, 0, 0));
        test.World.Camera = test.SpawnCamera(test.Root, DPose.Identity);
        // A new frame on rails holds still for its first step and stands on its rails at the second sync point after its
        // spawn (docs/plans/rails-components.md, "The rails components"). So the third tick is the first one in which the
        // frame moves along its orbit from the sync point before to the sync point after.
        test.Run(3);

        RenderInstance instance = test.World.Latest().InstanceOf(satellite);

        // The speed of a circular orbit is sqrt(mu / r), about 7546 m/s. A tick is 1/60 s, so the satellite moved about 126 m.
        double expected = Math.Sqrt(mu / radius) / 60;
        Vector3.Distance(instance.Position, instance.PreviousPosition).Should()
            .BeApproximately((float)expected, (float)(0.02 * expected));
    }

    [Fact]
    public void ObjectsThatDidNotChangeHaveBitIdenticalPreviousTransformsWhileOtherFramesMove()
    {
        const double mu = 3.986004418e14;
        KeplerOrbit orbit = KeplerOrbit.Circular(7e6, spinRate: 0.01);
        using var test = new TestWorld();
        test.GiveRootPointGravity(mu);
        FrameHandle moon = test.World.GetOwnedFrame(test.World.Spawn(new SpawnDesc
        {
            Frame = test.Root,
            AsFrame = new(FrameBounds.Sphere(1e5f))
        }.OnKeplerOrbit(test.World, orbit)));
        FrameHandle dock = test.SpawnStaticFrame(new(new(-3e6, 1e5, 42.5), PoseChecks.QuarterTurnAboutY), radius: 1e6f);
        FrameHandle yard = test.SpawnStaticFrame(
            new(new(4e6, -2e5, 7.25), DQuaternion.FromAxisAngle(new(1, 1, 0), 0.3)), radius: 1e6f);
        test.World.Camera = test.SpawnCamera(dock, TestWorld.At(1.5, 2.5, 3.5));
        Entity crate = test.Show(yard, TestWorld.At(11.1, 22.2, 33.3));
        Entity satellite = test.Show(moon, TestWorld.At(100, 0, 0));
        test.Run(3);

        RenderSnapshot snapshot = test.World.Latest();

        // Nothing moved the dock, the yard or the crate, so the previous pose of the crate is its current pose to the last
        // bit, though the moon next to it moved. The satellite, which rides the moon, did move.
        RenderInstance crateInstance = snapshot.InstanceOf(crate);
        crateInstance.PreviousPosition.Should().Be(crateInstance.Position);
        crateInstance.PreviousRotation.Should().Be(crateInstance.Rotation);
        RenderInstance satelliteInstance = snapshot.InstanceOf(satellite);
        satelliteInstance.PreviousPosition.Should().NotBe(satelliteInstance.Position);
    }

    // ---- When interpolation is turned off ------------------------------------------------------------------------

    /// <summary>
    /// A camera that is a ball with a render row. It flies along +X from the root frame towards a frame <c>B</c> that is 500 m
    /// from the origin of the root, and Jolt hands it off to B in the step of tick 2. An object in the root moves 1 m along
    /// +X in each tick, and starts 10 m to the side of the camera's line.
    /// </summary>
    private sealed class HandOffScene : IDisposable
    {
        public HandOffScene()
        {
            Test = TestWorld.OnJolt(faults: out _);
            B = Test.SpawnStaticFrame(TestWorld.At(500), radius: 100);
            Scenes.Approach flight = Scenes.ApproachToBubble(world: Test.World, into: B, tick: 2);
            Camera = Test.World.Spawn(new()
            {
                Frame = Test.Root,
                Pose = flight.Pose,
                LinearVelocity = flight.Velocity,
                Body = WorldTesting.Ball(),
                Render = new RenderData()
            });
            StartOfMoving = new(flight.Pose.Position.X, 0, -10);
            Moving = Test.Show(Test.Root, new(StartOfMoving, DQuaternion.Identity), new Mover(new(1, 0, 0)));
            Test.World.Camera = Camera;
        }

        public TestWorld Test { get; }
        public FrameHandle B { get; }
        public Entity Camera { get; }
        public Entity Moving { get; }

        /// <summary>Where the moving object started in the root frame, in meters.</summary>
        public Double3 StartOfMoving { get; }

        /// <summary>
        /// Where the moving object is after it has moved <paramref name="ticksOfMovement"/> times, relative to the camera as the
        /// camera is now, in the axes of the root frame. The camera flies, so the test cannot name this position by hand.
        /// </summary>
        public Vector3 MovingRelativeToCamera(int ticksOfMovement)
        {
            Test.World.Snapshot.TryGetPoseIn(Camera, Test.Root, out DPose camera).Should().BeTrue();
            return (StartOfMoving + new Double3(ticksOfMovement, 0, 0) - camera.Position).ToVector3();
        }

        public void Dispose() => Test.Dispose();
    }

    [Fact]
    public void BeforeTheCameraChangesFrameTheSnapshotInterpolates()
    {
        using var scene = new HandOffScene();

        scene.Test.Run(2);

        RenderInstance instance = scene.Test.World.Latest().InstanceOf(scene.Moving);
        PoseChecks.AssertNear(instance.Position, scene.MovingRelativeToCamera(2), 1e-3f);
        PoseChecks.AssertNear(instance.PreviousPosition, scene.MovingRelativeToCamera(1), 1e-3f);
    }

    [Fact]
    public void WhenTheCameraChangesFrameTheSnapshotOfThatTickDoesNotInterpolate()
    {
        using var scene = new HandOffScene();
        scene.Test.Run(2);
        scene.Test.World.Latest();

        scene.Test.Run(1);

        RenderSnapshot snapshot = scene.Test.World.Latest();
        RenderInstance instance = snapshot.InstanceOf(scene.Moving);
        instance.PreviousPosition.Should().Be(instance.Position);
        snapshot.Camera.PreviousPosition.Should().Be(Vector3.Zero);
        snapshot.Camera.PreviousRotation.Should().Be(snapshot.Camera.Rotation);
    }

    [Fact]
    public void WhenTheCameraChangesFrameTheObjectsAreStillWhereTheyAreRelativeToTheCamera()
    {
        using var scene = new HandOffScene();
        scene.Test.Run(2);
        scene.Test.World.Latest();

        scene.Test.Run(1);

        // The camera flew into B, and the object moved three times. Seen from the camera, the object is where the camera
        // and the object are in the root frame.
        PoseChecks.AssertNear(scene.Test.World.Latest().InstanceOf(scene.Moving).Position,
            scene.MovingRelativeToCamera(3), 1e-3f);
        scene.Test.World.GetFrame(scene.Camera).Should().Be(scene.B);
    }

    [Fact]
    public void TheTickAfterTheCameraChangedFrameInterpolatesAgain()
    {
        using var scene = new HandOffScene();
        scene.Test.Run(3);
        scene.Test.World.Latest();

        scene.Test.Run(1);

        RenderInstance instance = scene.Test.World.Latest().InstanceOf(scene.Moving);
        PoseChecks.AssertNear(instance.PreviousPosition, instance.Position - new Vector3(1, 0, 0), 1e-3f);
    }

    [Fact]
    public void WhenAnotherEntityBecomesTheCameraTheSnapshotOfThatTickDoesNotInterpolate()
    {
        using var test = new TestWorld();
        Entity moving = test.Show(test.Root, TestWorld.At(0, 0, -10), new Mover(new(1, 0, 0)));
        Entity first = test.SpawnCamera(test.Root, DPose.Identity);
        Entity second = test.SpawnCamera(test.Root, TestWorld.At(0, 5, 0));
        test.World.Camera = first;
        test.Run(2);
        test.World.Latest();

        test.World.Camera = second;
        test.Run(1);

        RenderSnapshot snapshot = test.World.Latest();
        RenderInstance instance = snapshot.InstanceOf(moving);
        instance.PreviousPosition.Should().Be(instance.Position);
        snapshot.Camera.PreviousPosition.Should().Be(Vector3.Zero);
    }

    [Fact]
    public void WhenTheCameraIsMissingForATickTheSnapshotAfterItDoesNotInterpolate()
    {
        using var test = new TestWorld();
        Entity moving = test.Show(test.Root, TestWorld.At(0, 0, -10), new Mover(new(1, 0, 0)));
        Entity camera = test.SpawnCamera(test.Root, DPose.Identity);
        test.World.Camera = camera;
        test.Run(2);
        test.World.Latest();
        test.World.Camera = Entity.Null;
        test.Run(1);
        test.World.Latest();

        test.World.Camera = camera;
        test.Run(1);

        RenderInstance instance = test.World.Latest().InstanceOf(moving);
        instance.PreviousPosition.Should().Be(instance.Position);
    }

    [Fact]
    public void AnObjectHandedOffBetweenFramesInterpolatesFromItsPoseInTheOldFrame()
    {
        using TestWorld test = TestWorld.OnJolt(faults: out FaultInjectingBackend faults);
        FrameHandle b = test.SpawnStaticFrame(TestWorld.At(500), radius: 100);
        Entity camera = test.SpawnCamera(test.Root, DPose.Identity);
        // The ball flies from the root into the frame b. Jolt hands it off in the step of tick 2.
        Scenes.Approach flight = Scenes.ApproachToBubble(world: test.World, into: b, tick: 2);
        Entity ball = test.World.Spawn(new()
        {
            Frame = test.Root,
            Pose = flight.Pose,
            LinearVelocity = flight.Velocity,
            Body = WorldTesting.Ball(),
            Render = Visible()
        });
        test.World.Camera = camera;
        test.Run(2);
        test.World.Latest();

        test.Run(1);

        // The camera rests at the origin of the root. The ball was in the root at the end of tick 1, and in b at the end
        // of tick 2, where the origin of b is 500 m along +X in the root.
        HandOff handOff = faults.Outputs[2].HandOffs.Should().ContainSingle().Subject;
        handOff.Entity.Should().Be(ball);
        Double3 before = faults.Outputs[1].Bodies.Single(state => state.Entity == ball).Pose.Position;
        Double3 after = handOff.Pose.Position + new Double3(500, 0, 0);
        RenderInstance instance = test.World.Latest().InstanceOf(ball);
        PoseChecks.AssertNear(instance.PreviousPosition, before.ToVector3(), 1e-3f);
        PoseChecks.AssertNear(instance.Position, after.ToVector3(), 1e-3f);
        (after - before).Length.Should().BeGreaterThan(0.1,
            "the ball moved between the two snapshots, or the test cannot tell the old pose from the new one");
    }
}
