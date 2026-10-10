using System.Numerics;
using AwesomeAssertions;
using static Tertius.Simulation.Tests.RenderSnapshotSupport;

namespace Tertius.Simulation.Tests;

/// <summary>
/// What a render snapshot contains: one instance for each entity that has a visible mesh, in entity slot order, placed
/// relative to the camera. The sync point builds a snapshot at the end of every tick from the front tables, the state that
/// the tick produced. Positions, previous poses and lights have their own test classes.
/// </summary>
public class RenderSnapshotTests
{
    [Fact]
    public void ASnapshotNamesTheTickAndTheTimeOfTheStateItShows()
    {
        using var test = new TestWorld(configure: options => options.TickRate = 64);
        test.World.Camera = test.SpawnCamera(test.Root, DPose.Identity);

        test.Run(3);

        RenderSnapshot snapshot = test.World.Latest();
        snapshot.Tick.Should().Be(3);
        snapshot.Time.Should().Be(3.0 / 64);
    }

    [Fact]
    public void ASnapshotIsStampedWithTheTimeOfItsPublication()
    {
        using var test = new TestWorld();
        test.World.Camera = test.SpawnCamera(test.Root, DPose.Identity);

        test.Run(1);

        test.World.Latest().PublishedAt.Should().BePositive();
    }

    [Fact]
    public void ASnapshotCanBeBuiltBeforeTheFirstTick()
    {
        using var test = new TestWorld();
        Entity box = test.Show(test.Root, TestWorld.At(3, 0, 0));
        test.World.Camera = test.SpawnCamera(test.Root, DPose.Identity);

        test.World.PublishRenderSnapshot();

        RenderSnapshot snapshot = test.World.Latest();
        snapshot.Tick.Should().Be(0);
        snapshot.InstanceOf(box).Position.Should().Be(new Vector3(3, 0, 0));
    }

    // ---- Without a camera ------------------------------------------------------------------------------------------

    [Fact]
    public void WithoutACameraTheSnapshotHasNothingToDraw()
    {
        using var test = new TestWorld();
        test.Show(test.Root, TestWorld.At(3, 0, 0));

        test.World.PublishRenderSnapshot();

        RenderSnapshot snapshot = test.World.Latest();
        snapshot.Instances.Should().BeEmpty();
        snapshot.Lights.Should().BeEmpty();
        snapshot.Occluders.Should().BeEmpty();
    }

    [Fact]
    public void WithoutACameraTheSnapshotStillCarriesTheViewSettingsOfTheWorld()
    {
        using var test = new TestWorld();
        test.World.VerticalFov = 1.25f;
        test.World.SunIntensity = 4;

        test.World.PublishRenderSnapshot();

        RenderSnapshot snapshot = test.World.Latest();
        snapshot.Camera.VerticalFov.Should().Be(1.25f);
        snapshot.Sun.Intensity.Should().Be(4);
    }

    [Fact]
    public void ACameraThatHasBeenDestroyedLeavesAnEmptySnapshotAndNoError()
    {
        using var test = new TestWorld();
        Entity camera = test.SpawnCamera(test.Root, DPose.Identity);
        test.Show(test.Root, TestWorld.At(3, 0, 0));
        test.World.Camera = camera;
        test.World.Destroy(camera);

        test.Run(1);

        test.World.Latest().Instances.Should().BeEmpty();
        test.World.Errors.Should().BeEmpty();
    }

    // ---- What becomes an instance ----------------------------------------------------------------------------------

    [Fact]
    public void AnEntityWithAVisibleMeshBecomesOneInstanceWithItsRenderRow()
    {
        using var test = new TestWorld();
        Entity box = test.World.Spawn(new SpawnDesc
        {
            Frame = test.Root,
            Render = new RenderData
            {
                Mesh = new MeshId(4),
                Material = new MaterialId(3),
                Flags = RenderFlags.Visible | RenderFlags.CastsShadow,
                BoundingRadius = 1.5f,
                Scale = new Vector3(1, 2, 3),
            },
        });
        test.World.Camera = test.SpawnCamera(test.Root, DPose.Identity);

        test.Run(1);

        RenderInstance instance = test.World.Latest().InstanceOf(box);
        instance.Mesh.Should().Be(new MeshId(4));
        instance.Material.Should().Be(new MaterialId(3));
        instance.Scale.Should().Be(new Vector3(1, 2, 3));
        instance.BoundingRadius.Should().Be(1.5f);
        instance.Flags.Should().Be(RenderFlags.Visible | RenderFlags.CastsShadow);
        instance.Part.Should().Be(0);
    }

    [Fact]
    public void AnEntityWithAVisibleMeshButNoMeshIdDrawsNothing()
    {
        using var test = new TestWorld();
        // A default render row has no mesh, so it draws nothing even when it is visible.
        test.World.Spawn(new SpawnDesc { Frame = test.Root, Render = default(RenderData) with { Flags = RenderFlags.Visible } });
        test.World.Camera = test.SpawnCamera(test.Root, DPose.Identity);

        test.Run(1);

        test.World.Latest().Instances.Should().BeEmpty();
    }

    [Fact]
    public void AnEntityWithAMeshButWithoutTheVisibleFlagDrawsNothing()
    {
        using var test = new TestWorld();
        test.World.Spawn(new SpawnDesc { Frame = test.Root, Render = new RenderData { Mesh = new MeshId(1), Flags = RenderFlags.CastsShadow } });
        test.World.Camera = test.SpawnCamera(test.Root, DPose.Identity);

        test.Run(1);

        test.World.Latest().Instances.Should().BeEmpty();
    }

    [Fact]
    public void AnEntityWithoutARenderRowDrawsNothing()
    {
        using var test = new TestWorld();
        test.SpawnWith();
        test.World.Camera = test.SpawnCamera(test.Root, DPose.Identity);

        test.Run(1);

        test.World.Latest().Instances.Should().BeEmpty();
    }

    [Fact]
    public void InstancesAreInTheOrderOfTheEntitySlots()
    {
        using var test = new TestWorld();
        Entity[] boxes = [.. Enumerable.Range(0, 5).Select(i => test.Show(test.Root, TestWorld.At(i)))];
        test.World.Camera = test.SpawnCamera(test.Root, DPose.Identity);

        test.Run(1);

        test.World.Latest().Instances.Select(i => i.EntityIndex).Should().Equal(boxes.Select(b => b.Index));
    }

    [Fact]
    public void ADestroyedEntityLeavesTheSnapshot()
    {
        using var test = new TestWorld();
        Entity staying = test.Show(test.Root, TestWorld.At(1));
        Entity leaving = test.Show(test.Root, TestWorld.At(2));
        test.World.Camera = test.SpawnCamera(test.Root, DPose.Identity);
        test.World.Destroy(leaving);

        test.Run(1);

        test.World.Latest().Instances.Select(i => i.EntityIndex).Should().Equal(staying.Index);
    }

    [Fact]
    public void TheOwnerOfAFrameIsDrawnAtTheFramesPose()
    {
        using var test = new TestWorld();
        Entity planet = test.World.Spawn(new SpawnDesc
        {
            Frame = test.Root,
            Pose = TestWorld.At(0, 0, -1e7),
            AsFrame = new FrameSpec(FrameBounds.Sphere(1e8f), MotionDriver.Static),
            Render = Visible(radius: 6e6f),
        });
        test.World.Camera = test.SpawnCamera(test.Root, DPose.Identity);

        test.Run(1);

        test.World.Latest().InstanceOf(planet).Position.Should().Be(new Vector3(0, 0, -1e7f));
    }

    [Fact]
    public void TheOwnerOfAFrameInsideAnotherFrameIsDrawnAtThePoseComposedThroughBothFrames()
    {
        using var test = new TestWorld();
        FrameHandle outer = test.SpawnStaticFrame(TestWorld.At(100), radius: 1e4f);
        Entity moon = test.World.Spawn(new SpawnDesc
        {
            Frame = outer,
            Pose = TestWorld.At(0, 5, 0),
            AsFrame = new FrameSpec(FrameBounds.Sphere(10), MotionDriver.Static),
            Render = Visible(),
        });
        test.World.Camera = test.SpawnCamera(test.Root, DPose.Identity);

        test.Run(1);

        test.World.Latest().InstanceOf(moon).Position.Should().Be(new Vector3(100, 5, 0));
    }

    [Fact]
    public void AnObjectInATurnedFrameIsTurnedByTheFrameAndThenByItsOwnRotation()
    {
        using var test = new TestWorld();
        FrameHandle turned = test.SpawnStaticFrame(new DPose(new Double3(100, 0, 0), PoseChecks.QuarterTurnAboutY), radius: 1e4f);
        Entity box = test.Show(turned, new DPose(Double3.Zero, PoseChecks.QuarterTurnAboutY));
        test.World.Camera = test.SpawnCamera(test.Root, DPose.Identity);

        test.Run(1);

        // Two quarter turns about +Y make a half turn.
        PoseChecks.AssertSameRotation(test.World.Latest().InstanceOf(box).Rotation, DQuaternion.FromAxisAngle(Double3.UnitY, Math.PI));
    }

    // ---- When a change shows ---------------------------------------------------------------------------------------

    [Fact]
    public void ARenderChangeMadeDuringATickShowsInTheSnapshotOfThatTick()
    {
        using var test = new TestWorld();
        Entity box = test.World.Spawn(new SpawnDesc
        {
            Frame = test.Root,
            Render = new RenderData(),
            Components = [Lambda.OnTick((self, scoped ref events, scoped ref outbox) =>
            {
                if (self.Tick == 1)
                {
                    self.Render.Mesh = new MeshId(9);
                    self.Render.Flags = RenderFlags.Visible;
                }
            })],
        });
        test.World.Camera = test.SpawnCamera(test.Root, DPose.Identity);

        test.Run(1);
        test.World.Latest().Instances.Should().BeEmpty();
        test.Run(1);

        test.World.Latest().InstanceOf(box).Mesh.Should().Be(new MeshId(9));
    }

    [Fact]
    public void AnEntityStillReadsTheRenderRowOfThePreviousTickWhileItWritesANewOne()
    {
        using var test = new TestWorld();
        MeshId? readAfterWriting = null;
        test.World.Spawn(new SpawnDesc
        {
            Frame = test.Root,
            Render = new RenderData(),
            Components = [Lambda.OnTick((self, scoped ref events, scoped ref outbox) =>
            {
                self.Render.Mesh = new MeshId(9);
                readAfterWriting = self.Snapshot.RenderOf(self.Entity).Mesh;
            })],
        });

        test.Run(1);

        readAfterWriting.Should().Be(MeshId.None);
    }

    [Fact]
    public void TheWorldReadsTheNewRenderRowAfterTheSyncPoint()
    {
        using var test = new TestWorld();
        Entity box = test.World.Spawn(new SpawnDesc
        {
            Frame = test.Root,
            Render = new RenderData(),
            Components = [Lambda.OnTick((self, scoped ref events, scoped ref outbox) => self.Render.Mesh = new MeshId(9))],
        });

        test.Run(1);

        test.World.GetRender(box).Mesh.Should().Be(new MeshId(9));
    }
}
