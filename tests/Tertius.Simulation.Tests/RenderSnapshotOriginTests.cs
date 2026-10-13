using System.Numerics;
using AwesomeAssertions;
using static Tertius.Simulation.Tests.RenderSnapshotSupport;

namespace Tertius.Simulation.Tests;

/// <summary>
/// The render origin is the eye of the camera. A snapshot tells the renderer where that origin is in the camera's frame
/// (<see cref="RenderSnapshot.OriginInFrame"/>) and which frame that is (<see cref="RenderSnapshot.OriginFrameId"/>), so the
/// renderer can anchor things that must not move with the camera, such as the grid of the shadow texels. The id is the
/// generation of the frame in the high 32 bits and its index in the low 32 bits: it is stable for the life of the frame,
/// and it changes when the camera changes frame.
/// </summary>
public class RenderSnapshotOriginTests
{
    [Fact]
    public void TheOriginIsTheCameraPositionInTheFrameOfTheCamera()
    {
        using var test = new TestWorld();
        FrameHandle bay = test.SpawnStaticFrame(TestWorld.At(500), radius: 100);
        test.World.Camera = test.SpawnCamera(bay, TestWorld.At(1234567.25, 2, 3));

        test.Run(1);

        test.World.Latest().OriginInFrame.Should().Be(new Double3(1234567.25, 2, 3));
    }

    [Fact]
    public void TheOriginIsTheEyeSoItIncludesTheViewOffsetOfTheCamera()
    {
        using var test = new TestWorld();
        Entity camera = test.World.Spawn(new SpawnDesc
        {
            Frame = test.Root,
            Pose = TestWorld.At(1234567.25, 2, 3),
            Render = new RenderData { ViewOffset = new Pose(new Vector3(0, 1.5f, 0), Quaternion.Identity) },
        });
        test.World.Camera = camera;

        test.Run(1);

        test.World.Latest().OriginInFrame.Should().Be(new Double3(1234567.25, 3.5, 3));
    }

    [Fact]
    public void TheOriginFrameIdIsMadeFromTheGenerationAndTheIndexOfTheCamerasFrame()
    {
        using var test = new TestWorld();
        FrameHandle bay = test.SpawnStaticFrame(TestWorld.At(500), radius: 100);
        test.World.Camera = test.SpawnCamera(bay, DPose.Identity);

        test.Run(1);

        test.World.Latest().OriginFrameId.Should().Be(((long)bay.Generation << 32) | (uint)bay.Index);
    }

    [Fact]
    public void TheOriginFrameIdDiffersForEachFrame()
    {
        using var test = new TestWorld();
        FrameHandle first = test.SpawnStaticFrame(TestWorld.At(500), radius: 100);
        FrameHandle second = test.SpawnStaticFrame(TestWorld.At(900), radius: 100);
        test.World.Camera = test.SpawnCamera(first, DPose.Identity);
        test.Run(1);
        long idOfFirst = test.World.Latest().OriginFrameId;

        test.World.Camera = test.SpawnCamera(second, DPose.Identity);
        test.Run(1);

        test.World.Latest().OriginFrameId.Should().NotBe(idOfFirst);
    }

    [Fact]
    public void TheOriginFrameIdFollowsTheCameraWhenPhysicsHandsItToAnotherFrame()
    {
        IPhysicsBackend physics = TestPhysics.Create();
        using var test = new TestWorld(physics);
        FrameHandle bay = test.SpawnStaticFrame(TestWorld.At(0), radius: 100);
        FrameHandle station = test.SpawnStaticFrame(TestWorld.At(500), radius: 100);
        Entity camera = test.World.Spawn(new SpawnDesc
        {
            Frame = bay,
            Pose = TestWorld.At(4567.25, 2, 3),
            Body = TestWorld.Ball,
            Render = new RenderData { ViewOffset = new Pose(new Vector3(0, 1.5f, 0), Quaternion.Identity) },
        });
        test.World.Camera = camera;
        physics.OnStep((context, output) =>
        {
            if (context.Tick == 1)
                output.HandOffs.Add(new HandOff(camera, bay, station, TestWorld.At(7, 8, 9), default, default));
        });

        test.Run(1);
        RenderSnapshot before = test.World.Latest();
        test.Run(1);
        RenderSnapshot after = test.World.Latest();

        before.OriginFrameId.Should().Be(IdOf(bay));
        after.OriginFrameId.Should().Be(IdOf(station));
        // The origin is now the camera's position in the station, with the eye 1.5 m above it.
        PoseChecks.AssertNear(after.OriginInFrame, new Double3(7, 9.5, 9));
    }

    [Fact]
    public void AFrameThatReusesTheIndexOfADestroyedFrameHasAnotherId()
    {
        using var test = new TestWorld();
        FrameHandle old = test.SpawnStaticFrame(TestWorld.At(500), radius: 100);
        test.World.Camera = test.SpawnCamera(old, DPose.Identity);
        test.Run(1);
        long idOfOld = test.World.Latest().OriginFrameId;
        test.World.Destroy(old.Entity);

        FrameHandle successor = test.SpawnStaticFrame(TestWorld.At(900), radius: 100);
        test.World.Camera = test.SpawnCamera(successor, DPose.Identity);
        test.Run(1);

        successor.Index.Should().Be(old.Index, "the test needs the new frame to reuse the index of the destroyed one");
        test.World.Latest().OriginFrameId.Should().NotBe(idOfOld);
    }

    [Fact]
    public void WithoutACameraTheOriginIsTheZeroPointOfNoFrame()
    {
        using var test = new TestWorld();
        FrameHandle bay = test.SpawnStaticFrame(TestWorld.At(500), radius: 100);
        Entity camera = test.SpawnCamera(bay, TestWorld.At(3, 4, 5));
        test.World.Camera = camera;
        test.Run(1);
        test.World.Latest().OriginFrameId.Should().NotBe(0);

        test.World.Camera = Entity.Null;
        test.Run(1);

        RenderSnapshot none = test.World.Latest();
        none.OriginFrameId.Should().Be(0);
        none.OriginInFrame.Should().Be(Double3.Zero);
    }

    [Fact]
    public void TheOwnerOfARootFrameLooksOutFromTheOriginOfItsFrame()
    {
        using var test = new TestWorld();
        Entity box = test.Show(test.Root, TestWorld.At(3, 4, 5));
        test.World.Camera = test.Root.Entity;

        test.Run(1);

        RenderSnapshot snapshot = test.World.Latest();
        snapshot.InstanceOf(box).Position.Should().Be(new Vector3(3, 4, 5));
        snapshot.OriginInFrame.Should().Be(Double3.Zero);
        snapshot.OriginFrameId.Should().Be(IdOf(test.Root));
    }
}
