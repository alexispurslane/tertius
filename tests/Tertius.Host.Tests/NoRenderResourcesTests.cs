using System.Numerics;

namespace Tertius.Host.Tests;

/// <summary>The resources of a run with no renderer: every id that they make is none, so the scene draws nothing.</summary>
public sealed class NoRenderResourcesTests
{
    [Fact]
    public void AModelMadeWithNoRendererIsNone()
    {
        var part = new ModelPart(
            Mesh: RenderResources.None.CreateMesh(Primitives.Box(Vector3.One)),
            Material: RenderResources.None.CreateMaterial(MaterialDesc.Color(Vector4.One, 0.5f)),
            Offset: Pose.Identity,
            Scale: Vector3.One,
            BoundingRadius: 1
        );

        RenderResources.None.CreateModel(new([part])).IsNone.Should().BeTrue();
    }
}
