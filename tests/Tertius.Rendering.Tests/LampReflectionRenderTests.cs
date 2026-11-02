using System.Numerics;
using Tertius.Testing;
using static Tertius.Rendering.Tests.ReflectionTesting;

namespace Tertius.Rendering.Tests;

/// <summary>
/// How many times the reflections count a lamp: once, through its light (docs/plans/screen-space-reflections.md, "A lamp's
/// glow"). An entity that holds a light sends its power through that light, and its glow is the light's face, not a
/// second source (user, 2026-10-27). The light's specular lobe already shows the lamp in a glossy floor, so the screen's
/// reflections leave the glow out, as the environment map does. A black glossy floor 1.5 m below the eye reflects a lamp
/// 10 m ahead under no other light, with the map on, so that the map's reflection is what a ray that misses reads.
/// </summary>
[Collection(GpuCollection.Name)]
public sealed class LampReflectionRenderTests
{
    /// <summary>The lamp's light, in W, white.</summary>
    private const float LampPower = 10;

    /// <summary>The floor's roughness, which 8 bits hold: 13 / 255, about 0.05, inside the reflections' full weight.</summary>
    private const float GlossyRoughness = 13 / 255f;

    /// <summary>A black dielectric floor of <see cref="GlossyRoughness"/> and F0 = 0.04: it shows only reflected light.</summary>
    private static readonly MaterialDesc GlossyBlack = new(
        BaseColor: new(x: 0, y: 0, z: 0, w: 1),
        Roughness: GlossyRoughness,
        Reflectance: 0.5f
    );

    /// <summary>
    /// The black floor under a lamp of <see cref="LampPower"/>, whose entity <see cref="Gpu.LampEntity"/> both its glowing
    /// mesh and its light carry, rendered with the map on and <paramref name="reflections"/>.
    /// </summary>
    /// <param name="mesh">The lamp's glowing mesh, centered on <paramref name="center"/>.</param>
    /// <param name="glow">The glow's radiance, in W/(m²·sr).</param>
    /// <param name="lightCenter">The light's center.</param>
    /// <param name="lightRadius">The light's radius, in m.</param>
    private static float[] FloorUnderALamp(
        ReflectionSetting reflections,
        MeshData mesh,
        Vector3 center,
        float glow,
        Vector3 lightCenter,
        float lightRadius
    )
    {
        VulkanRenderer renderer = Gpu.Headless(
            exposure: new FixedExposure(0),
            reflections: reflections,
            environmentMap: SceneEnvironmentMap.Default
        );
        try
        {
            RenderSnapshot snapshot = Dark();
            snapshot.Instances.Add(Floor(renderer, GlossyBlack));
            snapshot.Instances.Add(
                Gpu.Instance(
                    mesh: renderer.CreateMesh(mesh),
                    material: Glow(renderer, new Vector3(glow)),
                    position: center,
                    scale: Vector3.One,
                    radius: 1,
                    entity: Gpu.LampEntity,
                    chainDepth: TestChains.CamerasFrame
                )
            );
            snapshot.Lights.Add(
                new PointRenderLight(
                    EntityIndex: Gpu.LampEntity,
                    Position: lightCenter,
                    PreviousPosition: lightCenter,
                    Color: Vector3.One,
                    Power: LampPower,
                    Radius: lightRadius,
                    CastsShadow: false,
                    ChainDepth: TestChains.CamerasFrame
                )
            );
            // The second frame reads the map that the first one drew.
            renderer.Render(snapshot, 1f);
            renderer.Render(snapshot, 1f);
            return renderer.ReadHdr();
        }
        finally
        {
            Gpu.DisposeAndAssertClean(renderer);
        }
    }

    [Fact]
    public void AGlossyFloorShowsABulbWhoseGlowIsItsLightsSphereOnce()
    {
        // A bulb as the demo's glow lamp: a sphere of the light's radius that glows with the light's own surface radiance,
        // I / (π R²). The light's lobe alone shows the bulb's disc at that radiance, so with the reflections off the floor
        // holds the bulb once.
        const float radius = 0.15f;
        var center = new Vector3(0, 0.5f, -10);
        float surfaceRadiance = LampPower / (4 * MathF.PI) / (MathF.PI * radius * radius);
        float[] Rendered(ReflectionSetting reflections) =>
            FloorUnderALamp(
                reflections: reflections,
                mesh: Primitives.Sphere(radius, 64, 32),
                center: center,
                glow: surfaceRadiance,
                lightCenter: center,
                lightRadius: radius
            );
        float once = At(Rendered(new NoReflections()), MirrorPixel(center)).Y;
        float reflected = At(Rendered(ConeTracedReflections.Default), MirrorPixel(center)).Y;

        once.Should().BeGreaterThan(0.5f, "the light's lobe shows the bulb's disc in the floor");
        reflected
            .Should()
            .BeApproximately(once, 0.03f * once, "the reflections add no second image of the bulb over its light's disc");
    }

    [Fact]
    public void TheScreensReflectionOfALampPanelIsTheMapsDarkPanel()
    {
        // A panel 1.2 m square that glows with the radiance of the demo's cabin lamps, P / (π A), and its light of radius
        // 5 cm 10 cm under it. The floor pixel chosen sees the panel's image 30 cm from the light's disc, where the light's
        // lobe adds nothing on a floor this smooth: there the map's reflection, which leaves out the glow, shows black.
        var center = new Vector3(0, 0.5f, -10);
        const float halfSide = 0.6f;
        float[] Rendered(ReflectionSetting reflections) =>
            FloorUnderALamp(
                reflections: reflections,
                mesh: Primitives.Box(new(halfSide, 0.05f, halfSide)),
                center: center,
                glow: LampPower / (MathF.PI * 4 * halfSide * halfSide),
                lightCenter: center - new Vector3(0, 0.1f, 0),
                lightRadius: 0.05f
            );
        (int X, int Y) beside = MirrorPixel(center - new Vector3(0.3f, 0.05f, 0));
        float fromTheMap = At(Rendered(new NoReflections()), beside).Y;
        float fromTheScreen = At(Rendered(ConeTracedReflections.Default), beside).Y;

        fromTheMap.Should().BeLessThan(1e-3f, "the map leaves out the glow of an entity that holds a light");
        fromTheScreen.Should().BeLessThan(1e-3f, "the screen's reflections leave the panel's glow out as the map does");
    }
}
