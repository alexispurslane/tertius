using System.Numerics;
using static Tertius.Rendering.Tests.ReflectionTesting;

namespace Tertius.Rendering.Tests;

/// <summary>
/// The hit light image (ticket ter-67y3, <see cref="ScreenTraceImages.HitLight"/>): the ray plan writes the light that
/// leaves each pixel toward a ray of each kind, and a ray's hit reads it there instead of computing it again. The scene is
/// a sunlit glossy metal floor before a white wall with a glowing panel, under the camera of <see cref="Gpu.Snapshot"/>, which
/// stands still, and each frame steps one tick.
/// </summary>
[Collection(ProcessEnvironmentCollection.Name)]
public sealed class HitLightImageTests
{
    private const string FilterSwitch = "TERTIUS_DIFFUSE_FILTER";

    private const int Width = 160,
        Height = 90;

    /// <summary>The step of each frame after the first: one tick, which continues the last frame.</summary>
    private const double Tick = 1.0 / 60.0;

    /// <summary>The markers of hit_light_marker_probe.comp in each channel of the reflection layer and the diffuse layer.</summary>
    private const float ReflectionMarker = 1000,
        DiffuseMarker = 3000;

    [Theory]
    [InlineData(1)]
    [InlineData(8)]
    public void TheHitLightImageHoldsTheLightThatLeavesEachHittablePixelTowardARayOfEachKind(int frames)
    {
        // Without the filter, a hit reads the last fast history, which no pass of the frame writes again, so the probe after
        // the tracer's stage computes the light from the same images as the plan.
        string? previous = Environment.GetEnvironmentVariable(FilterSwitch);
        Environment.SetEnvironmentVariable(FilterSwitch, "off");
        (VulkanRenderer renderer, RenderSnapshot scene) = GlossyFloorBeforeAWall();
        try
        {
            var probe = ScreenTraceProbe.On(renderer, "hit_light_probe.comp");
            Draw(renderer, scene, frames);

            float[][] leaving =
            [
                probe.Read(),
                renderer.ReadImage(renderer.ScreenTraceSignals.Diffuse.Sample(0)),
            ];
            float[][] stored =
            [
                .. Enumerable.Range(0, ScreenTraceImages.Kinds)
                    .Select(kind => renderer.ReadImageLayer(renderer.ScreenTraceImages.HitLight, kind)),
            ];
            int hittable = 0,
                specular = 0;
            for (int at = 0; at < 4 * Width * Height; at += 4)
            {
                if (leaving[0][at + 3] == 0)
                    continue;
                hittable++;
                for (int kind = 0; kind < ScreenTraceImages.Kinds; kind++)
                for (int channel = 0; channel < 3; channel++)
                {
                    float expected = leaving[kind][at + channel];
                    stored[kind][at + channel]
                        .Should()
                        .BeApproximately(expected, 1e-3f * expected + 1e-6f, "pixel {0}, kind {1}", at / 4, kind);
                }
                if (leaving[0][at] > 1.05f * leaving[1][at])
                    specular++;
            }
            hittable.Should().BeGreaterThan(Width * Height / 2, "the floor and the wall fill most of the view");
            specular.Should().BePositive("the metal floor sends its light toward a reflection ray only");
        }
        finally
        {
            Gpu.DisposeAndAssertClean(renderer);
            Environment.SetEnvironmentVariable(FilterSwitch, previous);
        }
    }

    [Fact]
    public void ARaysHitTakesItsLightFromTheHitLightImageOfItsKind()
    {
        (VulkanRenderer renderer, RenderSnapshot scene) = GlossyFloorBeforeAWall();
        try
        {
            ScreenTraceProbe.On(renderer, "hit_light_marker_probe.comp");
            var rays = ScreenTraceProbe.On(renderer, "hit_light_trace_probe.comp");
            Draw(renderer, scene, frames: 8);

            float[] reflection = rays.Read();
            float[] diffuse = renderer.ReadImage(renderer.ScreenTraceSignals.Diffuse.Sample(0));
            foreach ((float[] traced, float marker) in (ValueTuple<float[], float>[])
                         [(reflection, ReflectionMarker), (diffuse, DiffuseMarker)])
            {
                float[] hitLights =
                [
                    .. Enumerable.Range(0, Width * Height)
                        .Where(pixel => traced[4 * pixel + 1] > 0)
                        .Select(pixel => traced[4 * pixel]),
                ];
                hitLights.Length.Should().BeGreaterThan(100, "rays from the floor hit the wall and the floor");
                // A hit's light is the marker faded into the miss's light, which is far below either marker.
                hitLights.Max().Should().BeInRange(0.9f * marker, 1.001f * marker, "the hits read the marker of their kind");
            }
        }
        finally
        {
            Gpu.DisposeAndAssertClean(renderer);
        }
    }

    /// <summary>
    /// A glossy white metal floor of roughness 0.3 in the sun, which sends no diffuse light, a rough white wall 7.5 m ahead and a panel on the wall that glows
    /// 10 in each channel.
    /// </summary>
    private static (VulkanRenderer Renderer, RenderSnapshot Scene) GlossyFloorBeforeAWall()
    {
        VulkanRenderer renderer = Gpu.Headless(
            width: Width,
            height: Height,
            screenTrace: ScreenTrace.Default,
            warmUpFrames: 0
        );
        RenderSnapshot scene = Gpu.Snapshot(new(0.3f, 1f, 0.4f));
        MeshId box = renderer.CreateMesh(Primitives.Box(new(0.5f)));
        scene.Instances.Add(Floor(renderer, Mirror(roughness: 0.3f)));
        scene.Instances.Add(
            Block(
                box: box,
                material: renderer.CreateMaterial(MaterialDesc.Color(Vector4.One, roughness: 1f)),
                center: new(0, 2, -7.5f),
                size: new(30, 7, 1)
            )
        );
        scene.Instances.Add(
            Block(box: box, material: Glow(renderer, new(10)), center: new(0.8f, FloorHeight + 0.6f, -6.99f), size: new(0.6f, 0.4f, 0.02f))
        );
        return (renderer, scene);
    }

    /// <summary>Draws <paramref name="frames"/> frames of <paramref name="scene"/>, each one tick after the last.</summary>
    private static void Draw(VulkanRenderer renderer, RenderSnapshot scene, int frames)
    {
        for (int frame = 0; frame < frames; frame++)
        {
            renderer.Render(scene, 1f);
            scene.Time += Tick;
        }
    }
}
