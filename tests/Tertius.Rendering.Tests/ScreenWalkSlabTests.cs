using System.Numerics;
using Tertius.Testing;

namespace Tertius.Rendering.Tests;

/// <summary>
/// The shader's walk reads a cell's slab as the depth pyramid stores it: from the stored front w to the stored back w,
/// with no thickness of its own (docs/plans/back-face-depth-layer.md, "The walk"). Rays run at one inverse depth across a
/// wall that faces the eye.
/// </summary>
[Collection(GpuCollection.Name)]
public sealed class ScreenWalkSlabTests
{
    private const int Width = 64,
        Height = 36;

    /// <summary>The probe's pixel in the middle of the screen, whose rays cross four wall pixels to its right.</summary>
    private const int Middle = 4 * (Height / 2 * Width + Width / 2);

    [Fact]
    public void ARayInsideTheStoredSlabOfAWallHitsItAtTheShareOfTheStoredSlabWhereItEnters()
    {
        float[] values = Values();

        values[Middle].Should().Be(1, "the ray runs inside the wall's slab: a hit");
        values[Middle + 1].Should().BeApproximately(0.5f, 1e-3f, "the ray runs halfway from the front w to the back w");
    }

    [Fact]
    public void ARayJustBehindTheStoredBackWOfAWallMissesIt() =>
        Values()[Middle + 2]
            .Should()
            .Be(2, "the stored back w ends the wall's slab, with no thickness added by the walk");

    private static float[] Values()
    {
        VulkanRenderer renderer = Gpu.Headless(
            width: Width,
            height: Height,
            screenTrace: ScreenTrace.Default,
            warmUpFrames: 0
        );
        try
        {
            RenderSnapshot scene = Gpu.Snapshot(Vector3.UnitY);
            scene.Instances.Add(
                Gpu.Instance(
                    mesh: renderer.CreateMesh(Primitives.Box(new(0.5f))),
                    material: renderer.CreateMaterial(MaterialDesc.Color(Vector4.One, roughness: 0.3f)),
                    position: new(0, 0, -20),
                    scale: new(60, 40, 1),
                    chainDepth: TestChains.CamerasFrame
                )
            );
            var probe = ScreenTraceProbe.On(renderer, "screen_walk_slab_probe.comp");

            renderer.Render(scene, 1f);

            return probe.Read();
        }
        finally
        {
            Gpu.DisposeAndAssertClean(renderer);
        }
    }
}
