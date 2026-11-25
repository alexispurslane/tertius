using System.Numerics;
using AwesomeAssertions;
using static Tertius.Simulation.Tests.RenderSnapshotSupport;

namespace Tertius.Simulation.Tests;

/// <summary>
/// An entity that draws a model (docs/plans/asset-pipeline.md, "Models: nodes and pieces"): its render row names a model
/// and no mesh, and the snapshot hands the renderer one instance with the model and the entity's transform. The renderer
/// expands the parts.
/// </summary>
public sealed class RenderSnapshotModelTests
{
    private static RenderData ModelRow(RenderFlags flags = RenderFlags.Visible) =>
        new()
        {
            Model = new(4),
            Flags = flags,
            Scale = new(2),
        };

    [Fact]
    public void AVisibleEntityWithAModelAndNoMeshIsInTheSnapshot()
    {
        using var test = new TestWorld();
        Entity ship = test.World.Spawn(
            new()
            {
                Frame = test.Root,
                Pose = TestWorld.At(0, 0, -5),
                Render = ModelRow(),
            }
        );
        test.World.Camera = test.SpawnCamera(test.Root, DPose.Identity);

        test.Run(1);

        RenderInstance instance = test.World.Latest().InstanceOf(ship);
        instance.Model.Should().Be(new ModelId(4));
        instance.Mesh.IsNone.Should().BeTrue("a model row names no mesh");
        instance.Scale.Should().Be(new Vector3(2));
    }

    [Fact]
    public void AnEntityWithAModelThatIsNotVisibleIsNotInTheSnapshot()
    {
        using var test = new TestWorld();
        Entity ship = test.World.Spawn(
            new()
            {
                Frame = test.Root,
                Pose = TestWorld.At(0, 0, -5),
                Render = ModelRow(flags: RenderFlags.None),
            }
        );
        test.World.Camera = test.SpawnCamera(test.Root, DPose.Identity);

        test.Run(1);

        test.World.Latest().Instances.Should().NotContain(i => i.EntityIndex == ship.Index);
    }

    [Fact]
    public void AnEntityWithAMeshCarriesNoModel()
    {
        using var test = new TestWorld();
        Entity box = test.Show(test.Root, TestWorld.At(0, 0, -5));
        test.World.Camera = test.SpawnCamera(test.Root, DPose.Identity);

        test.Run(1);

        test.World.Latest().InstanceOf(box).Model.IsNone.Should().BeTrue();
    }
}
