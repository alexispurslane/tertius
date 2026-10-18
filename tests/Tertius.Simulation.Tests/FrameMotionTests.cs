using System.Numerics;
using AwesomeAssertions;
using Tertius.Frames.Rails;

namespace Tertius.Simulation.Tests;

/// <summary>
/// A frame moves when its owner moves. For a static frame the owner is a bodiless entity whose gameplay writes the
/// frame's pose, and every entity in the frame, and every frame below it, goes along without any write of its own.
/// </summary>
public class FrameMotionTests
{
    [Fact]
    public void AStaticFrameMovesWhenItsOwnerWritesItsTransform()
    {
        using var test = new TestWorld();
        Entity owner = test.World.Spawn(new SpawnDesc
        {
            Frame = test.Root,
            AsFrame = new FrameSpec(FrameBounds.Sphere(10), MotionDriver.Static),
            Components = [new Mover(new Double3(0, 2, 0))],
        });
        FrameHandle frame = test.World.GetOwnedFrame(owner);

        test.Run(3);

        test.World.Frames.LocalPose(frame).Position.Should().Be(new Double3(0, 6, 0));
        test.World.PoseInOwnFrame(owner).Position.Should().Be(new Double3(0, 6, 0));
    }

    [Fact]
    public void AnEntityInAMovingStaticFrameKeepsItsLocalPoseAndMovesWithTheFrame()
    {
        using var test = new TestWorld();
        FrameHandle frame = test.World.GetOwnedFrame(test.World.Spawn(new SpawnDesc
        {
            Frame = test.Root,
            AsFrame = new FrameSpec(FrameBounds.Sphere(10), MotionDriver.Static),
            Components = [new Mover(new Double3(0, 2, 0))],
        }));
        Entity passenger = test.World.SpawnAt(frame, new Double3(1, 0, 0));

        test.Run(3);

        test.World.PoseInOwnFrame(passenger).Position.Should().Be(new Double3(1, 0, 0));
        test.World.Snapshot.TryGetPoseIn(passenger, test.Root, out DPose inRoot).Should().BeTrue();
        inRoot.Position.Should().Be(new Double3(1, 6, 0));
    }

    [Fact]
    public void AnEntityInAStaticFrameTurnsWithTheFrameWhenItsOwnerWritesARotation()
    {
        using var test = new TestWorld();
        FrameHandle frame = test.World.GetOwnedFrame(test.World.Spawn(new SpawnDesc
        {
            Frame = test.Root,
            Pose = TestWorld.At(10),
            AsFrame = new FrameSpec(FrameBounds.Sphere(10), MotionDriver.Static),
            Components = [Lambda.OnTick((self, scoped ref events, scoped ref outbox) =>
                self.SetTransform(self.PoseInOwnFrame() with { Rotation = PoseChecks.QuarterTurnAboutY }))],
        }));
        Entity passenger = test.World.SpawnAt(frame, new Double3(1, 0, 0));

        test.Run(1);

        // The quarter turn about +Y maps the frame's +X to the root's -Z.
        test.World.Snapshot.TryGetPoseIn(passenger, test.Root, out DPose inRoot).Should().BeTrue();
        PoseChecks.AssertNear(inRoot.Position, new Double3(10, 0, -1));
        PoseChecks.AssertSameRotation(inRoot.Rotation, PoseChecks.QuarterTurnAboutY);
    }

    [Fact]
    public void AFrameInsideAMovingFrameMovesWithItAndCarriesItsOwnEntities()
    {
        using var test = new TestWorld();
        FrameHandle outer = test.World.GetOwnedFrame(test.World.Spawn(new SpawnDesc
        {
            Frame = test.Root,
            Pose = TestWorld.At(100),
            AsFrame = new FrameSpec(FrameBounds.Sphere(1000), MotionDriver.Static),
            Components = [new Mover(new Double3(1, 0, 0))],
        }));
        FrameHandle inner = test.World.SpawnStaticFrame(outer, TestWorld.At(0, 10), radius: 10);
        Entity passenger = test.World.SpawnAt(inner, new Double3(0, 0, 5));

        test.Run(3);

        // 100 + 3 m for the outer frame, 10 m for the inner frame inside it, 5 m for the passenger inside that.
        test.World.Snapshot.TryGetPoseIn(passenger, test.Root, out DPose inRoot).Should().BeTrue();
        PoseChecks.AssertNear(inRoot.Position, new Double3(103, 10, 5));
        test.World.Frames.LocalPose(inner).Position.Should().Be(new Double3(0, 10, 0));
    }

    [Fact]
    public void TheOwnerOfAFrameIsInTheParentFrameAndIsNotInItsOwnFrame()
    {
        using var test = new TestWorld();
        FrameHandle station = test.SpawnStaticFrame(TestWorld.At(50));
        FrameHandle hangar = test.World.SpawnStaticFrame(station, TestWorld.At(5), radius: 10);

        test.World.GetFrame(hangar.Entity).Should().Be(station);
        test.World.GetFrame(station.Entity).Should().Be(test.Root);
        test.World.GetFrame(test.Root.Entity).IsNull.Should().BeTrue("the owner of a root frame is in no frame");
    }

    [Fact]
    public void TheOwnerOfAFramesPoseIsTheFramesLocalPose()
    {
        using var test = new TestWorld();
        FrameHandle station = test.SpawnStaticFrame(new DPose(new Double3(50, 6, 7), PoseChecks.QuarterTurnAboutY));

        test.World.PoseInOwnFrame(station.Entity).Should().Be(test.World.Frames.LocalPose(station));
        test.World.PoseInOwnFrame(station.Entity).Position.Should().Be(new Double3(50, 6, 7));
    }

    // ---- The step record of a write (docs/plans/rails-components.md, "One motion record per frame") ----------------

    [Fact]
    public void AFrameWithoutABodyFollowsItsOwnersWriteAtTheSyncPointOfTheTickAndTakesItAsItsStepRecord()
    {
        using var test = new TestWorld();
        var written = new DPose(new Double3(3, 4, 5), PoseChecks.QuarterTurnAboutY);
        FrameHandle frame = test.World.GetOwnedFrame(test.World.Spawn(new SpawnDesc
        {
            Frame = test.Root,
            AsFrame = new FrameSpec(FrameBounds.Sphere(10), MotionDriver.Static),
            Components = [Lambda.OnTick((self, scoped ref events, scoped ref outbox) =>
            {
                if (self.Tick == 1)
                    self.SetTransform(written);
            })],
        }));

        test.Run(2);

        test.World.Frames.LocalPose(frame).Should().Be(written);
        test.World.Frames.Samples.Followed.TryGet(frame, out MotionSample record, out FrameHandle relativeTo).Should().BeTrue();
        relativeTo.Should().Be(test.Root);
        record.Should().Be(new MotionSample(written, Double3.Zero, Double3.Zero, Double3.Zero, Double3.Zero, Double3.Zero, Vector3.Zero),
            "a write moves the frame with no velocity");
    }

    [Fact]
    public void WhenItsParentDiesAtTheSameSyncPointAFrameWithoutABodyStillFollowsItsOwnersWrite()
    {
        using var test = new TestWorld();
        var dockPose = new DPose(new Double3(100, 0, 0), PoseChecks.QuarterTurnAboutY);
        FrameHandle dock = test.World.GetOwnedFrame(test.World.Spawn(new SpawnDesc
        {
            Frame = test.Root,
            Pose = dockPose,
            AsFrame = new FrameSpec(FrameBounds.Sphere(50), MotionDriver.Static),
            Components = [Lambda.OnTick((self, scoped ref events, scoped ref outbox) =>
            {
                if (self.Tick == 1)
                    self.Destroy();
            })],
        }));
        var written = new DPose(new Double3(3, 4, 5), DQuaternion.Identity);
        FrameHandle room = test.World.GetOwnedFrame(test.World.Spawn(new SpawnDesc
        {
            Frame = dock,
            AsFrame = new FrameSpec(FrameBounds.Sphere(5), MotionDriver.Static),
            Components = [Lambda.OnTick((self, scoped ref events, scoped ref outbox) =>
            {
                if (self.Tick == 1)
                    self.SetTransform(written);
            })],
        }));

        test.Run(2);

        test.World.Frames.IsAlive(dock).Should().BeFalse();
        test.World.Frames.Parent(room).Should().Be(test.Root);
        PoseChecks.AssertNear(test.World.Frames.LocalPose(room).Position, DPose.Compose(dockPose, written).Position);
    }
}
