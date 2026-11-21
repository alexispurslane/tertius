using System.Numerics;
using Tertius.Testing;

namespace Tertius.Rendering.Tests;

/// <summary>
/// The walk over the real slab on the GPU (docs/plans/back-face-depth-layer.md, "The acceptance"): a box whose front faces
/// the eye 5 m away, under the sun and before the sky. Its back face ends its slab, also where the back face lies nearer
/// or farther than a tenth of the box's distance. Rays from the sky below the box run up the screen behind its front, each
/// at one view depth (screen_walk_depth_probe.comp).
/// </summary>
[Collection(GpuCollection.Name)]
public sealed class BackFaceSlabWalkTests
{
    private const int Width = 160,
        Height = 90;

    /// <summary>The distance of the box's front face, in m.</summary>
    private const float FrontDepth = 5;

    /// <summary>The outcome codes of walkScreen in screen_walk.glsl.</summary>
    private const float WalkHit = 1,
        WalkMiss = 2;

    [Fact]
    public void ARayThatLeavesThroughTheOpeningBehindAThinSillMissesIt()
    {
        // The sill's back face lies 5.1 m away. Under the 10 % rule its slab reached 5.5 m.
        float[] outcomes = Outcomes(boxDepth: 0.1f);

        outcomes[0].Should().Be(WalkHit, "the premise: the ray at 5.05 m runs inside the sill");
        outcomes[1].Should().Be(WalkMiss, "the ray at 5.2 m passes behind the sill's back face into the opening");
    }

    [Fact]
    public void ARayThatEntersAWallDeeperThanATenthOfItsDistanceHitsIt() =>
        Outcomes(boxDepth: 1)[2]
            .Should()
            .Be(WalkHit, "the ray at 5.8 m runs inside the wall, whose back face lies 6 m away");

    /// <summary>
    /// The outcomes of the probe's three rays, at 5.05 m, 5.2 m and 5.8 m, from the sky pixel below the middle of a box
    /// <paramref name="boxDepth"/> m deep. The box is 3 m wide and 0.3 m tall, centered on the eye's height, so that its
    /// top and its bottom face away from the eye and only its front shows.
    /// </summary>
    private static float[] Outcomes(float boxDepth)
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
            var size = new Vector3(3, 0.3f, boxDepth);
            scene.Instances.Add(
                Gpu.Instance(
                    mesh: renderer.CreateMesh(Primitives.Box(new(0.5f))),
                    material: renderer.CreateMaterial(MaterialDesc.Color(Vector4.One, roughness: 0.5f)),
                    position: new(0, 0, -FrontDepth - boxDepth / 2),
                    scale: size,
                    radius: size.Length() / 2,
                    chainDepth: TestChains.CamerasFrame
                )
            );
            var probe = ScreenTraceProbe.On(renderer, "screen_walk_depth_probe.comp");

            renderer.Render(scene, 1f);

            float[] values = probe.Read();
            float[] distance = renderer.ReadDistance();
            Vector2 below = Gpu.Project(point: new(0, -0.25f, -FrontDepth), width: Width, height: Height);
            int pixel = (int)below.Y * Width + (int)below.X;
            distance[pixel].Should().BeLessThanOrEqualTo(0, "the premise: the rays start from the sky below the box");
            distance[pixel - 4 * Width].Should().BeGreaterThan(0, "the premise: the box lies above the start");
            return values[(4 * pixel)..(4 * pixel + 3)];
        }
        finally
        {
            Gpu.DisposeAndAssertClean(renderer);
        }
    }
}
