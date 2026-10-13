using System.Numerics;
using AwesomeAssertions;
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
        Entity moving = test.Show(test.Root, TestWorld.At(10, 0, -5), new Mover(new Double3(1, 0, 0)));
        test.World.Camera = test.SpawnCamera(test.Root, DPose.Identity, new Mover(new Double3(0, 1, 0)));

        test.Run(1);

        RenderInstance instance = test.World.Latest().InstanceOf(moving);
        instance.PreviousPosition.Should().Be(instance.Position);
    }

    [Fact]
    public void ThePreviousPositionOfAMovingObjectIsRelativeToTheCurrentOrigin()
    {
        using var test = new TestWorld();
        Entity moving = test.Show(test.Root, TestWorld.At(10, 0, -5), new Mover(new Double3(1, 0, 0)));
        test.World.Camera = test.SpawnCamera(test.Root, DPose.Identity, new Mover(new Double3(0, 1, 0)));
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
        test.World.Camera = test.SpawnCamera(test.Root, DPose.Identity, new Mover(new Double3(0, 1, 0)));
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
        test.World.Camera = test.SpawnCamera(test.Root, DPose.Identity, new Mover(new Double3(0, 1, 0)));
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
        Entity turning = test.Show(test.Root, TestWorld.At(0, 0, -10), Lambda.OnTick((self, scoped ref events, scoped ref outbox) =>
            self.SetTransform(self.PoseInOwnFrame() with { Rotation = DQuaternion.FromAxisAngle(Double3.UnitY, 0.5 * (self.Tick + 1)) })));
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
        Entity newcomer = test.Show(test.Root, TestWorld.At(10, 0, -5), new Mover(new Double3(1, 0, 0)));

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
        FrameHandle hangar = test.World.GetOwnedFrame(test.World.Spawn(new SpawnDesc
        {
            Frame = test.Root,
            AsFrame = new FrameSpec(FrameBounds.Sphere(100), MotionDriver.Static),
            Components = [new Mover(new Double3(1, 0, 0))],
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
        var orbit = KeplerOrbit.Circular(radius);
        using var test = new TestWorld();
        test.GiveRootPointGravity(mu);
        FrameHandle moon = test.World.GetOwnedFrame(test.World.Spawn(new SpawnDesc
        {
            Frame = test.Root,
            AsFrame = new FrameSpec(FrameBounds.Sphere(1e5f), MotionDriver.Kepler, Orbit: orbit),
        }));
        Entity satellite = test.Show(moon, TestWorld.At(100, 0, 0));
        test.World.Camera = test.SpawnCamera(test.Root, DPose.Identity);
        test.Run(2);

        RenderInstance instance = test.World.Latest().InstanceOf(satellite);

        // The speed of a circular orbit is sqrt(mu / r), about 7546 m/s. A tick is 1/60 s, so the satellite moved about 126 m.
        double expected = Math.Sqrt(mu / radius) / 60;
        Vector3.Distance(instance.Position, instance.PreviousPosition).Should().BeApproximately((float)expected, (float)(0.02 * expected));
    }

    [Fact]
    public void ObjectsThatDidNotChangeHaveBitIdenticalPreviousTransformsWhileOtherFramesMove()
    {
        const double mu = 3.986004418e14;
        var orbit = KeplerOrbit.Circular(7e6, spinRate: 0.01);
        using var test = new TestWorld();
        test.GiveRootPointGravity(mu);
        FrameHandle moon = test.World.GetOwnedFrame(test.World.Spawn(new SpawnDesc
        {
            Frame = test.Root,
            AsFrame = new FrameSpec(FrameBounds.Sphere(1e5f), MotionDriver.Kepler, Orbit: orbit),
        }));
        FrameHandle dock = test.SpawnStaticFrame(new DPose(new Double3(-3e6, 1e5, 42.5), PoseChecks.QuarterTurnAboutY), radius: 1e6f);
        FrameHandle yard = test.SpawnStaticFrame(
            new DPose(new Double3(4e6, -2e5, 7.25), DQuaternion.FromAxisAngle(new Double3(1, 1, 0), 0.3)), radius: 1e6f);
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
    /// A camera with a body in frame <c>A</c>, at the origin of A, and a frame <c>B</c> 500 m away. At tick 2 physics hands the camera
    /// off to B at the same place in the root frame. An object in A moves 1 m along +X in each tick.
    /// </summary>
    private sealed class HandOffScene : IDisposable
    {
        public HandOffScene()
        {
            Test = new TestWorld(Physics);
            FrameHandle a = Test.SpawnStaticFrame(TestWorld.At(0), radius: 100);
            FrameHandle b = Test.SpawnStaticFrame(TestWorld.At(500), radius: 100);
            Camera = Test.World.Spawn(new SpawnDesc { Frame = a, Body = TestWorld.Ball, Render = new RenderData() });
            Moving = Test.Show(a, TestWorld.At(0, 0, -10), new Mover(new Double3(1, 0, 0)));
            Test.World.Camera = Camera;
            Physics.OnStep((context, output) =>
            {
                if (context.Tick == 2)
                    output.HandOffs.Add(new HandOff(Camera, a, b, TestWorld.At(-500), default, default));
            });
        }

        public IPhysicsBackend Physics { get; } = TestPhysics.Create();
        public TestWorld Test { get; }
        public Entity Camera { get; }
        public Entity Moving { get; }

        public void Dispose() => Test.Dispose();
    }

    [Fact]
    public void BeforeTheCameraChangesFrameTheSnapshotInterpolates()
    {
        using var scene = new HandOffScene();

        scene.Test.Run(2);

        RenderInstance instance = scene.Test.World.Latest().InstanceOf(scene.Moving);
        instance.Position.Should().Be(new Vector3(2, 0, -10));
        instance.PreviousPosition.Should().Be(new Vector3(1, 0, -10));
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

        // The camera is at the same place in the root frame as before the hand-off, and the object is 3 m along +X and 10 m ahead.
        PoseChecks.AssertNear(scene.Test.World.Latest().InstanceOf(scene.Moving).Position, new Vector3(3, 0, -10), 1e-3f);
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
        Entity moving = test.Show(test.Root, TestWorld.At(0, 0, -10), new Mover(new Double3(1, 0, 0)));
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
        Entity moving = test.Show(test.Root, TestWorld.At(0, 0, -10), new Mover(new Double3(1, 0, 0)));
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
        IPhysicsBackend physics = TestPhysics.Create();
        using var test = new TestWorld(physics);
        FrameHandle a = test.SpawnStaticFrame(TestWorld.At(0), radius: 100);
        FrameHandle b = test.SpawnStaticFrame(TestWorld.At(500), radius: 100);
        Entity camera = test.SpawnCamera(test.Root, DPose.Identity);
        Entity ball = test.World.Spawn(new SpawnDesc
        {
            Frame = a,
            Pose = TestWorld.At(3, 0, -10),
            Body = TestWorld.Ball,
            Render = RenderSnapshotSupport.Visible(),
        });
        test.World.Camera = camera;
        physics.OnStep((context, output) =>
        {
            // The ball moves 1 m along +X and into frame b in the same step: (3 + 1, 0, -10) in a is (-496, 0, -10) in b.
            if (context.Tick == 2)
                output.HandOffs.Add(new HandOff(ball, a, b, TestWorld.At(-496, 0, -10), default, default));
        });
        test.Run(2);
        test.World.Latest();

        test.Run(1);

        RenderInstance instance = test.World.Latest().InstanceOf(ball);
        instance.PreviousPosition.Should().Be(new Vector3(3, 0, -10));
        instance.Position.Should().Be(new Vector3(4, 0, -10));
    }
}
