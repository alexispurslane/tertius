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
    private const string FilterSwitch = "TERTIUS_DIFFUSE_FILTER";

    private const string GuidedSwitch = "TERTIUS_GUIDED_RAYS";

    /// <summary>The cap of the tracer's light, ShaderInput.MaxRadiance: below the largest half float.</summary>
    private const float MaxRadiance = ShaderInput.MaxRadiance;

    private const int Width = 160,
        Height = 90;

    /// <summary>The step of each frame after the first: one tick, which continues the last frame.</summary>
    private const double Tick = 1.0 / 60.0;

    /// <summary>The markers of hit_light_marker_probe.comp in each channel of the reflection layer and the diffuse layer.</summary>
    private const float ReflectionMarker = 1000,
        DiffuseMarker = 3000;

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
    public void TheHitLightImageHoldsTheLightThatLeavesEachHittablePixelTowardARayOfEachKind(
        int frames,
        string? guided,
        bool environmentMap
    )
    {
        // Without the filter, a hit reads the last fast history, which no pass of the frame writes again, so the probe after
        // the tracer's stage computes the light from the same images as the plan.
        string? previous = Environment.GetEnvironmentVariable(FilterSwitch);
        string? previousGuided = Environment.GetEnvironmentVariable(GuidedSwitch);
        Environment.SetEnvironmentVariable(FilterSwitch, "off");
        Environment.SetEnvironmentVariable(GuidedSwitch, guided);
        (VulkanRenderer renderer, RenderSnapshot scene) = GlossyFloorBeforeAWall(environmentMap: environmentMap);
        try
        {
            var probe = ScreenTraceProbe.On(renderer, "hit_light_probe.comp");
            Draw(renderer, scene, frames);

            float[][] leaving = [probe.Read(), renderer.ReadImage(renderer.ScreenTraceSignals.Diffuse.Sample(0))];
            float[][] stored =
            [
                .. Enumerable
                    .Range(0, ScreenTraceImages.Kinds)
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
                    stored[kind]
                        [at + channel]
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
            Environment.SetEnvironmentVariable(GuidedSwitch, previousGuided);
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
            foreach (
                (float[] traced, float marker) in (ValueTuple<float[], float>[])
                    [(reflection, ReflectionMarker), (diffuse, DiffuseMarker)]
            )
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

    [Fact]
    public void AGlowAboveTheLargestRadianceStoresAtMostMaxRadianceInBothLayers()
    {
        // A half float holds at most 65,504, and a driver that is not Metal stores infinity above it. The plan caps the
        // light at MaxRadiance, as every light of the tracer. The G-buffer already caps the glow, so the sun is bright too:
        // a wall's reflection light adds its specular part to its capped diffuse part.
        (VulkanRenderer renderer, RenderSnapshot scene) = GlossyFloorBeforeAWall(glow: 100_000, starIrradiance: 1e6f);
        try
        {
            Draw(renderer, scene, frames: 2);

            for (int kind = 0; kind < ScreenTraceImages.Kinds; kind++)
            {
                float[] stored = renderer.ReadImageLayer(renderer.ScreenTraceImages.HitLight, kind);
                float most = Enumerable.Range(0, Width * Height).Max(pixel => stored[4 * pixel]);
                most.Should().Be(MaxRadiance, "the panel and the sunlit wall send more than the cap, kind {0}", kind);
            }
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
            float[] expected = renderer.ReadImage(renderer.ScreenTraceSignals.Diffuse.Sample(0));
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
    public void TheHitLightImageHoldsAnImpostorsGlowInBothLayers()
    {
        // The impostor rule counts output pixels, so the view must be large enough that the floor and the wall stay
        // geometry while a small sphere becomes an impostor.
        string? previous = Environment.GetEnvironmentVariable(FilterSwitch);
        Environment.SetEnvironmentVariable(FilterSwitch, "off");
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
            float[][] stored =
            [
                .. Enumerable
                    .Range(0, ScreenTraceImages.Kinds)
                    .Select(kind => renderer.ReadImageLayer(renderer.ScreenTraceImages.HitLight, kind)),
            ];
            int impostor = 0;
            for (int at = 0; at < 4 * width * height; at += 4)
            {
                if (leaving[at + 3] != 2)
                    continue;
                impostor++;
                for (int kind = 0; kind < ScreenTraceImages.Kinds; kind++)
                for (int channel = 0; channel < 3; channel++)
                    stored[kind]
                        [at + channel]
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
            Environment.SetEnvironmentVariable(FilterSwitch, previous);
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
