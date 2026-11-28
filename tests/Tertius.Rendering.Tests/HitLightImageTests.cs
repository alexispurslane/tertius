using System.Numerics;
using Tertius.Testing;
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
    private const string GuidedSwitch = "TERTIUS_GUIDED_RAYS";

    private const int Width = 160,
        Height = 90;

    /// <summary>The step of each frame after the first: one tick, which continues the last frame.</summary>
    private const double Tick = 1.0 / 60.0;

    /// <summary>The markers of hit_light_marker_probe.comp in each channel of the reflection layer and the diffuse layer.</summary>
    private const float ReflectionMarker = 1000;

    /// <summary>
    /// After <paramref name="frames"/> frames, with the guided rays' switch at <paramref name="guided"/> and with or without
    /// an environment map: the first frame takes the closed form everywhere, and without the guided rays no light pyramid
    /// reads the direct light before the plan.
    /// </summary>
    [Theory]
    [InlineData(1, null, false)]
    [InlineData(8, null, false)]
    [InlineData(8, "off", false)]
    [InlineData(1, null, true)]
    public void TheHitLightImageHoldsTheLightThatLeavesEachHittablePixelTowardAReflectionRay(
        int frames,
        string? guided,
        bool environmentMap
    )
    {
        // Without the filter, a hit reads the last fast history, which no pass of the frame writes again, so the probe after
        // the tracer's stage computes the light from the same images as the plan.
        string? previousGuided = Environment.GetEnvironmentVariable(GuidedSwitch);
        Environment.SetEnvironmentVariable(GuidedSwitch, guided);
        (VulkanRenderer renderer, RenderSnapshot scene) = GlossyFloorBeforeAWall(environmentMap: environmentMap);
        try
        {
            var probe = ScreenTraceProbe.On(renderer, "hit_light_probe.comp");
            Draw(renderer, scene, frames);

            float[] leaving = probe.Read();
            float[] stored = renderer.ReadImage(renderer.ScreenTraceImages.HitLight);
            int hittable = 0;
            for (int at = 0; at < 4 * Width * Height; at += 4)
            {
                if (leaving[at + 3] == 0)
                    continue;
                hittable++;
                for (int channel = 0; channel < 3; channel++)
                {
                    float expected = leaving[at + channel];
                    stored[at + channel]
                        .Should()
                        .BeApproximately(expected, 1e-3f * expected + 1e-6f, "pixel {0}", at / 4);
                }
            }
            hittable.Should().BeGreaterThan(Width * Height / 2, "the floor and the wall fill most of the view");
        }
        finally
        {
            Gpu.DisposeAndAssertClean(renderer);
            Environment.SetEnvironmentVariable(GuidedSwitch, previousGuided);
        }
    }

    [Fact]
    public void ARaysHitTakesItsLightFromTheHitLightImage()
    {
        (VulkanRenderer renderer, RenderSnapshot scene) = GlossyFloorBeforeAWall();
        try
        {
            ScreenTraceProbe.On(renderer, "hit_light_marker_probe.comp");
            var rays = ScreenTraceProbe.On(renderer, "hit_light_trace_probe.comp");
            Draw(renderer, scene, frames: 8);

            float[] traced = rays.Read();
            const float marker = ReflectionMarker;
            {
                float[] hitLights =
                [
                    .. Enumerable
                        .Range(0, Width * Height)
                        .Where(pixel => traced[4 * pixel + 1] > 0)
                        .Select(pixel => traced[4 * pixel]),
                ];
                hitLights.Length.Should().BeGreaterThan(100, "rays from the floor hit the wall and the floor");
                // A hit's light is the marker faded into the miss's light, which is far below either marker.
                hitLights
                    .Max()
                    .Should()
                    .BeInRange(0.9f * marker, 1.001f * marker, "the hits read the marker of their kind");
            }
        }
        finally
        {
            Gpu.DisposeAndAssertClean(renderer);
        }
    }

    [Theory]
    [InlineData(100_000f)]
    [InlineData(1e30f)]
    public void AHitRetainsPhysicalEmissionAboveTheHalfFloatBudgetWithFiniteBoundedRaySamples(float glow)
    {
        (VulkanRenderer renderer, RenderSnapshot scene) = GlossyFloorBeforeAWall(glow: glow, starIrradiance: 0);
        try
        {
            scene.Ambient.Intensity = 0;
            // The first draw has no reprojected glossy light, so emission is the only source.
            Draw(renderer, scene, frames: 1);

            float expected = MathF.Min(glow, ShaderInput.RadianceLimit);
            float[] stored = renderer.ReadImage(renderer.ScreenTraceImages.HitLight);
            float[] samples = renderer.ReadImage(renderer.ScreenTraceImages.Specular.Sample(0));
            float most = Enumerable.Range(0, Width * Height).Max(pixel => stored[4 * pixel]);
            most.Should()
                .BeApproximately(
                    expected,
                    expected * 1e-5f,
                    "the black panel sends its public emission in scene units"
                );
            for (int pixel = 0; pixel < Width * Height; pixel++)
            {
                for (int channel = 0; channel < 3; channel++)
                {
                    stored[4 * pixel + channel].Should().BeInRange(0, expected * 1.00001f);
                    samples[4 * pixel + channel]
                        .Should()
                        .BeInRange(
                            0,
                            4 * expected * 1.00001f,
                            "the ray weight is at most four and no other light enters this scene"
                        );
                }
                stored[4 * pixel + 3].Should().BeOneOf(0, 1);
                samples[4 * pixel + 3].Should().BeInRange(0, float.MaxValue);
            }
            Enumerable
                .Range(0, Width * Height)
                .Max(pixel => samples[4 * pixel])
                .Should()
                .BePositive("the mirror floor must actually receive the panel's emission");
        }
        finally
        {
            Gpu.DisposeAndAssertClean(renderer);
        }
    }

    [Fact]
    public void WhereARoughnessChangeFailsOnlyTheSpecularHistoryAReflectionRayTakesTheClosedFormAsItsSpecularHistory()
    {
        (VulkanRenderer renderer, RenderSnapshot scene) = GlossyFloorBeforeAWall();
        try
        {
            var probe = ScreenTraceProbe.On(renderer, "hit_light_closed_probe.comp");
            // Histories longer than FullRateFrames, 8, so that the floor's pixels share their blocks' rays before the change.
            Draw(renderer, scene, frames: 12);
            // A roughness change of more than 0.1, from 0.3 to 0.45, fails the specular test of the floor's pixels and keeps the diffuse test.
            RenderInstance floor = scene.Instances[0];
            floor.Material = renderer.CreateMaterial(
                Mirror(roughness: 0.45f) with
                {
                    NormalMap = FlatNormalMap(renderer),
                }
            );
            scene.Instances[0] = floor;
            Draw(renderer, scene, frames: 1);

            float[] part = probe.Read();
            float[] expected = renderer.ReadImage(renderer.ScreenTraceImages.OwnLight);
            float[] plan = renderer.ReadImage(renderer.ScreenTraceImages.Plan);
            int tested = 0,
                ownReflections = 0;
            for (int at = 0; at < 4 * Width * Height; at += 4)
            {
                if (part[at + 3] == 0)
                    continue;
                tested++;
                if (((int)plan[at / 4] & 1) != 0)
                    ownReflections++;
                for (int channel = 0; channel < 3; channel++)
                    part[at + channel]
                        .Should()
                        .BeApproximately(
                            expected[at + channel],
                            2e-3f * expected[at + channel] + 1e-4f,
                            "pixel {0}",
                            at / 4
                        );
            }
            tested.Should().BeGreaterThan(Width * Height / 10, "the floor fills the lower part of the view");
            ownReflections
                .Should()
                .BeGreaterThan(
                    tested / 2,
                    "a pixel without a specular history traces its own reflection ray, but its block's chosen pixel"
                );
        }
        finally
        {
            Gpu.DisposeAndAssertClean(renderer);
        }
    }

    [Fact]
    public void TheHitLightImageHoldsAnImpostorsGlow()
    {
        // The impostor rule counts output pixels, so the view must be large enough that the floor and the wall stay
        // geometry while a small sphere becomes an impostor.
        const int width = 640,
            height = 360;
        (VulkanRenderer renderer, RenderSnapshot scene) = GlossyFloorBeforeAWall(
            width: width,
            height: height,
            impostors: true
        );
        try
        {
            scene.Instances.Add(
                Gpu.Instance(
                    mesh: renderer.CreateMesh(Primitives.Sphere(1f, 48, 24)),
                    material: renderer.CreateMaterial(
                        MaterialDesc.Color(new(x: 0.8f, y: 0.2f, z: 0.2f, w: 1f), roughness: 0.5f)
                    ),
                    position: new(-1, FloorHeight + 0.3f, -5),
                    scale: new(0.3f),
                    entity: 9,
                    chainDepth: TestChains.CamerasFrame
                )
            );
            var probe = ScreenTraceProbe.On(renderer, "hit_light_probe.comp");
            Draw(renderer, scene, frames: 4);

            float[] leaving = probe.Read();
            float[] stored = renderer.ReadImage(renderer.ScreenTraceImages.HitLight);
            int impostor = 0;
            for (int at = 0; at < 4 * width * height; at += 4)
            {
                if (leaving[at + 3] != 2)
                    continue;
                impostor++;
                for (int channel = 0; channel < 3; channel++)
                    stored[at + channel]
                        .Should()
                        .BeApproximately(
                            leaving[at + channel],
                            1e-3f * leaving[at + channel] + 1e-6f,
                            "pixel {0}",
                            at / 4
                        );
            }
            renderer.LastFrameStats.Impostors.Should().BePositive("the small sphere and the panel are impostors");
            impostor.Should().BePositive("the impostor's pixels hold no base color");
        }
        finally
        {
            Gpu.DisposeAndAssertClean(renderer);
        }
    }

    /// <summary>
    /// A glossy white metal floor of roughness 0.3 in the sun, which sends no diffuse light, a rough white wall 7.5 m ahead
    /// and a panel on the wall that glows <paramref name="glow"/> in each channel, at <paramref name="width"/> by
    /// <paramref name="height"/>, under a sun of <paramref name="starIrradiance"/>, with impostors and an environment map
    /// when asked.
    /// </summary>
    private static (VulkanRenderer Renderer, RenderSnapshot Scene) GlossyFloorBeforeAWall(
        float glow = 10,
        int width = Width,
        int height = Height,
        bool impostors = false,
        bool environmentMap = false,
        float starIrradiance = 3
    )
    {
        VulkanRenderer renderer = Gpu.Headless(
            width: width,
            height: height,
            impostors: impostors,
            environmentMap: environmentMap ? SceneEnvironmentMap.Default : null,
            screenTrace: ScreenTrace.Default,
            warmUpFrames: 0
        );
        RenderSnapshot scene = Gpu.Snapshot(new(0.3f, 1f, 0.4f), starIrradiance);
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
            Block(
                box: box,
                material: Glow(renderer, new(glow)),
                center: new(0.8f, FloorHeight + 0.6f, -6.99f),
                size: new(0.6f, 0.4f, 0.02f)
            )
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
