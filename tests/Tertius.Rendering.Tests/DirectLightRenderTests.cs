using System.Numerics;
using Tertius.Testing;

namespace Tertius.Rendering.Tests;

/// <summary>
/// The direct light of the main view (docs/plans/temporal-renderer.md, section 7): the exact loop takes every light of
/// the main view in each frame, and the composite reads that frame's light, so a frame shows no light of the frames
/// before it. The renderer has the renderer's default direct light: the tests set no option for it.
/// </summary>
[Collection(GpuCollection.Name)]
public sealed class DirectLightRenderTests
{
    private const int Width = 64,
        Height = 36;

    /// <summary>The step of a frame that continues the last one: one tick.</summary>
    private const double Tick = 1.0 / 60.0;

    /// <summary>
    /// A validating headless renderer with the default direct light, a fixed exposure and no screen-space light, map,
    /// bloom or impostors, so that the HDR image depends only on the frame's G-buffer and its direct light.
    /// </summary>
    private static VulkanRenderer DefaultDirectLight() =>
        VulkanRenderer.CreateHeadless(
            new()
            {
                Validation = true,
                Width = Width,
                Height = Height,
                Impostors = false,
                ScreenSpaceLight = new NoScreenSpaceLight(),
                Exposure = new FixedExposure(0),
                Bloom = new NoBloom(),
                AntiAliasing = AntiAliasing.None,
                EnvironmentMap = new NoEnvironmentMap(),
            }
        );

    /// <summary>A wall 8 m ahead that fills the view, with no light: <see cref="Light"/> gives it its lamps.</summary>
    private static RenderSnapshot Wall(VulkanRenderer renderer)
    {
        MeshId cube = renderer.CreateMesh(Primitives.Box(new(0.5f)));
        MaterialId paint = renderer.CreateMaterial(
            MaterialDesc.Color(new(x: 0.8f, y: 0.6f, z: 0.4f, w: 1f), roughness: 0.4f)
        );
        RenderSnapshot snapshot = Gpu.Snapshot(new(0.3f, 1f, 0.4f));
        snapshot.Lights.Clear();
        snapshot.Instances.Add(
            Gpu.Instance(
                mesh: cube,
                material: paint,
                position: new(0, 0, -8),
                scale: new(30, 20, 1),
                chainDepth: TestChains.CamerasFrame,
                entity: 1
            )
        );
        return snapshot;
    }

    /// <summary>Gives <paramref name="snapshot"/> two lamps of entities 2 and 3, the second of <paramref name="secondIntensity"/> W/sr.</summary>
    private static void Light(RenderSnapshot snapshot, float secondIntensity)
    {
        snapshot.Lights.Clear();
        (Vector3 Position, float Intensity)[] lamps = [(new(-3, 1, -5), 40), (new(3, -1, -5), secondIntensity)];
        for (int i = 0; i < lamps.Length; i++)
            snapshot.Lights.Add(
                Gpu.Lamp(
                    position: lamps[i].Position,
                    color: Vector3.One,
                    intensity: lamps[i].Intensity,
                    chainDepth: TestChains.CamerasFrame
                ) with
                {
                    EntityIndex = (uint)(2 + i),
                }
            );
    }

    [Fact]
    public void AFrameAfterAFrameOfOtherLightShowsTheSameImageAsTheFirstFrameOfItsLight()
    {
        VulkanRenderer renderer = DefaultDirectLight();
        try
        {
            RenderSnapshot snapshot = Wall(renderer);
            Light(snapshot, secondIntensity: 40);
            renderer.Render(snapshot, 1f);
            float[] first = renderer.ReadHdr();

            // The next frame continues the first one, with the second lamp four times as bright, and the frame after it
            // continues that one, with the first frame's lamps.
            Light(snapshot, secondIntensity: 160);
            snapshot.Time += Tick;
            renderer.Render(snapshot, 1f);
            float[] brighter = renderer.ReadHdr();
            Light(snapshot, secondIntensity: 40);
            snapshot.Time += Tick;
            renderer.Render(snapshot, 1f);

            brighter.Should().NotEqual(first, "the brighter lamp lights the wall more");
            renderer.ReadHdr().Should().Equal(first, "the frame's direct light holds no light of the brighter frame");
        }
        finally
        {
            Gpu.DisposeAndAssertClean(renderer);
        }
    }
}
