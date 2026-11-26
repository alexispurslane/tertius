using System.Numerics;
using static Tertius.Rendering.Tests.ReflectionTesting;

namespace Tertius.Rendering.Tests;

/// <summary>
/// The guided diffuse rays on the GPU (ARCHITECTURE.md, "Guided rays"): the light pyramid's levels, the mixture
/// density in the ray records, and a floor's bounce light from a small glowing patch, with the guiding on and off through
/// the debug switch TERTIUS_GUIDED_RAYS (<see cref="LightPyramidPass.SwitchedOn"/>). The camera of
/// <see cref="Gpu.Snapshot"/> stands still, and each frame steps one tick.
/// </summary>
[Collection(ProcessEnvironmentCollection.Name)]
public sealed class GuidedRaysRenderTests
{
    private const string Switch = "TERTIUS_GUIDED_RAYS";

    /// <summary>The debug switch of the jitter (<see cref="DebugJitterSelection"/>).</summary>
    private const int Width = 160,
        Height = 90;

    /// <summary>The step of each frame after the first: one tick, which continues the last frame.</summary>
    /// <summary>The glow of the patch, about 100 times the bounce light that it gives the floor below it.</summary>
    private const float PatchGlow = 40;

    /// <summary>The center of the glowing panel, 0.6 m over the floor, which stands 1 cm out of the wall.</summary>
    private static readonly Vector3 PanelCenter = new(0.8f, FloorHeight + 0.6f, -6.99f);

    [Fact]
    public void WithoutTheSwitchTheLightPyramidPassRunsAndTheReflectionRaysAreGuided() =>
        WithSwitch(
            null,
            () =>
            {
                VulkanRenderer renderer = Gpu.Headless(width: 64, height: 36, screenTrace: ScreenTrace.Default);
                try
                {
                    renderer.ScreenTraceImages.Guided.Should().BeTrue();
                    renderer.Graph.Passes.OfType<LightPyramidPass>().Should().ContainSingle();
                }
                finally
                {
                    Gpu.DisposeAndAssertClean(renderer);
                }
            }
        );

    [Fact]
    public void TheOffSettingBuildsNoLightPyramidAndGuidesNoRay() =>
        WithSwitch(
            "off",
            () =>
            {
                VulkanRenderer renderer = Gpu.Headless(width: 64, height: 36, screenTrace: ScreenTrace.Default);
                try
                {
                    renderer.ScreenTraceImages.Guided.Should().BeFalse();
                    renderer.Graph.Passes.OfType<LightPyramidPass>().Should().BeEmpty();
                    renderer.Render(Dark(), 1f);
                }
                finally
                {
                    Gpu.DisposeAndAssertClean(renderer);
                }
            }
        );

    [Fact]
    public void AnUnknownSettingIsRefused() =>
        WithSwitch(
            "sometimes",
            () =>
            {
                Action select = () => LightPyramidPass.SwitchedOn();

                select.Should().Throw<ArgumentException>().WithMessage("*TERTIUS_GUIDED_RAYS*");
            }
        );

    [Fact]
    public void TheLightPyramidHoldsEachPixelsGlowAndEachCellTheSumOfTheCellsUnderIt() =>
        WithSwitch(
            null,
            () =>
            {
                (VulkanRenderer renderer, RenderSnapshot scene) = GlowOverFloor();
                try
                {
                    renderer.Render(scene, 1f);

                    (float[] finest, int width, int height) = renderer.ReadLightPyramidLevel(0);
                    (width, height).Should().Be((Width, Height));
                    finest.Max().Should().BeApproximately(Luminance(new(PatchGlow)), 0.01f * PatchGlow);
                    finest.Count(light => light > 0).Should().BeInRange(20, 400, "the patch's pixels alone glow");
                    var copy = LightPyramid.Of(finest, Width, Height);
                    for (int level = 1; level <= copy.TopLevel; level++)
                    {
                        (float[] lights, int levelWidth, _) = renderer.ReadLightPyramidLevel(level);
                        (int Width, int Height) size = DepthPyramid.LevelSize(Width, Height, level);
                        for (int y = 0; y < size.Height; y++)
                        for (int x = 0; x < size.Width; x++)
                            lights[y * levelWidth + x]
                                .Should()
                                .BeApproximately(
                                    copy.Light(level, x, y),
                                    1e-5f * copy.Total,
                                    $"cell ({x}, {y}) of {level}"
                                );
                    }
                }
                finally
                {
                    Gpu.DisposeAndAssertClean(renderer);
                }
            }
        );

    [Fact]
    public void ASunlitSurfacesPixelsHoldGuideLight() =>
        WithSwitch(
            null,
            () =>
            {
                (VulkanRenderer renderer, RenderSnapshot scene) = SceneOverFloor(
                    Gpu.Snapshot(Vector3.UnitY),
                    panel: false,
                    ScreenTrace.Default
                );
                try
                {
                    renderer.Render(scene, 1f);

                    (float[] lights, _, _) = renderer.ReadLightPyramidLevel(0);
                    float[] distances = renderer.ReadDistance();
                    int surfaces = distances.Count(distance => distance > 0);
                    int lit = Enumerable
                        .Range(0, lights.Length)
                        .Count(pixel => distances[pixel] > 0 && lights[pixel] > 0);
                    lit.Should().BeGreaterThan(surfaces / 4, "the star overhead lights the floor");
                }
                finally
                {
                    Gpu.DisposeAndAssertClean(renderer);
                }
            }
        );

    private static (VulkanRenderer Renderer, RenderSnapshot Scene) GlowOverFloor() =>
        SceneOverFloor(Dark(), panel: true, ScreenTrace.Default);

    /// <summary>
    /// <paramref name="world"/> with a white floor 1.5 m below the eye, a white wall 7 m ahead, and with
    /// <paramref name="panel"/> the glowing panel of <see cref="GlowOverFloor"/> on the wall, traced by
    /// <paramref name="trace"/>.
    /// </summary>
    private static (VulkanRenderer Renderer, RenderSnapshot Scene) SceneOverFloor(
        RenderSnapshot world,
        bool panel,
        ScreenTrace trace
    )
    {
        VulkanRenderer renderer = Gpu.Headless(width: Width, height: Height, screenTrace: trace, warmUpFrames: 0);
        RenderSnapshot scene = world;
        var white = MaterialDesc.Color(Vector4.One, roughness: 1f);
        MeshId box = renderer.CreateMesh(Primitives.Box(new(0.5f)));
        scene.Instances.Add(Floor(renderer, white));
        scene.Instances.Add(
            Block(box: box, material: renderer.CreateMaterial(white), center: new(0, 2, -7.5f), size: new(30, 7, 1))
        );
        if (panel)
            scene.Instances.Add(
                Block(
                    box: box,
                    material: Glow(renderer, new(PatchGlow)),
                    center: PanelCenter,
                    size: new(0.6f, 0.4f, 0.02f)
                )
            );
        return (renderer, scene);
    }

    private static float Luminance(Vector3 light) => Vector3.Dot(light, new(0.2126f, 0.7152f, 0.0722f));

    private static T WithSwitch<T>(string? value, Func<T> run)
    {
        string? previous = Environment.GetEnvironmentVariable(Switch);
        Environment.SetEnvironmentVariable(Switch, value);
        try
        {
            return run();
        }
        finally
        {
            Environment.SetEnvironmentVariable(Switch, previous);
        }
    }

    private static void WithSwitch(string? value, Action run) =>
        WithSwitch(
            value,
            () =>
            {
                run();
                return 0;
            }
        );
}
