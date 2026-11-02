using System.Numerics;
using Tertius.Testing;
using static Tertius.Rendering.Tests.ReflectionTesting;

namespace Tertius.Rendering.Tests;

/// <summary>
/// How many times the reflections and the map's light count a lamp: once, through its light
/// (docs/plans/screen-space-reflections.md, "A lamp's glow"). An entity that holds a light sends its power through that
/// light, and its glow is the light's face, not a second source (user, 2026-10-27). The light's specular lobe already
/// shows the lamp in a glossy floor, so the screen's reflections leave the glow out, as the environment map does. A glow
/// that no light holds, such as a screen's, is a source of its own in every reader. A floor 1.5 m below the eye shows a
/// glowing mesh under no other light, with the map on, so that the map's reflection is what a ray that misses reads.
/// </summary>
[Collection(GpuCollection.Name)]
public sealed class LampReflectionRenderTests
{
    /// <summary>The lamp's light, in W, white.</summary>
    private const float LampPower = 10;

    /// <summary>The floor's roughness, which 8 bits hold: 13 / 255, about 0.05, inside the reflections' full weight.</summary>
    private const float GlossyRoughness = 13 / 255f;

    /// <summary>Half the side of a lamp panel of the demo's cabin, in m: the panel is 1.2 m square.</summary>
    private const float PanelHalfSide = 0.6f;

    /// <summary>The glow of the demo's cabin lamps, P / (π A), in W/(m²·sr): the radiance of a panel that sends <see cref="LampPower"/>.</summary>
    private const float CabinGlow = LampPower / (MathF.PI * 4 * PanelHalfSide * PanelHalfSide);

    /// <summary>A white floor of roughness 1: it shows mostly the map's irradiance and the light's diffuse term.</summary>
    private static readonly MaterialDesc MatteWhite = MaterialDesc.Color(new(x: 1, y: 1, z: 1, w: 1), roughness: 1);

    /// <summary>A black dielectric floor of <paramref name="roughness"/> and F0 = 0.04: it shows only reflected light.</summary>
    private static MaterialDesc GlossyBlack(float roughness) =>
        new(BaseColor: new(x: 0, y: 0, z: 0, w: 1), Roughness: roughness, Reflectance: 0.5f);

    /// <summary>A white light of <see cref="LampPower"/> that <see cref="Gpu.LampEntity"/> carries, so that a mesh of that entity holds it.</summary>
    /// <param name="radius">The light's radius, in m.</param>
    private static PointRenderLight Lamp(Vector3 center, float radius) =>
        new(
            EntityIndex: Gpu.LampEntity,
            Position: center,
            PreviousPosition: center,
            Color: Vector3.One,
            Power: LampPower,
            Radius: radius,
            CastsShadow: false,
            ChainDepth: TestChains.CamerasFrame
        );

    /// <summary>
    /// <paramref name="floor"/> under a glowing mesh, rendered with the map on and <paramref name="reflections"/>. The mesh
    /// holds <paramref name="light"/>: its entity is the light's. Without a light, the mesh is of entity 0, and no light
    /// lights the scene.
    /// </summary>
    /// <param name="mesh">The glowing mesh, centered on <paramref name="center"/>.</param>
    /// <param name="glow">The glow's radiance, in W/(m²·sr).</param>
    private static float[] FloorUnder(
        ReflectionSetting reflections,
        MaterialDesc floor,
        MeshData mesh,
        Vector3 center,
        float glow,
        PointRenderLight? light
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
            snapshot.Instances.Add(Floor(renderer, floor));
            snapshot.Instances.Add(
                Gpu.Instance(
                    mesh: renderer.CreateMesh(mesh),
                    material: Glow(renderer, new Vector3(glow)),
                    position: center,
                    scale: Vector3.One,
                    radius: 1,
                    entity: light?.EntityIndex ?? 0,
                    chainDepth: TestChains.CamerasFrame
                )
            );
            if (light is { } held)
                snapshot.Lights.Add(held);
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

    [Theory]
    [InlineData(13)]
    [InlineData(51)]
    [InlineData(97)]
    public void AGlossyFloorShowsABulbWhoseGlowIsItsLightsSphereOnce(int roughnessIn255ths)
    {
        // A bulb as the demo's glow lamp: a sphere of the light's radius that glows with the light's own surface radiance,
        // I / (π R²). The light's lobe alone shows the bulb's disc at that radiance, so with the reflections off the floor
        // holds the bulb once. The roughness values are the probe's of ter-b1j3: about 0.05 and 0.2, where the
        // reflections run, and 0.38, the demo's paint, above their maximum of 0.37.
        const float radius = 0.15f;
        var center = new Vector3(0, 0.5f, -10);
        float surfaceRadiance = LampPower / (4 * MathF.PI) / (MathF.PI * radius * radius);
        float[] Rendered(ReflectionSetting reflections) =>
            FloorUnder(
                reflections: reflections,
                floor: GlossyBlack(roughnessIn255ths / 255f),
                mesh: Primitives.Sphere(radius, 64, 32),
                center: center,
                glow: surfaceRadiance,
                light: Lamp(center: center, radius: radius)
            );
        float once = At(Rendered(new NoReflections()), MirrorPixel(center)).Y;
        float reflected = At(Rendered(ConeTracedReflections.Default), MirrorPixel(center)).Y;

        once.Should().BeGreaterThan(0.02f, "the light's lobe shows the bulb's disc in the floor");
        reflected
            .Should()
            .BeApproximately(once, 0.03f * once, "the reflections add no second image of the bulb over its light's disc");
    }

    [Fact]
    public void TheScreensReflectionOfALampPanelIsTheMapsDarkPanel()
    {
        // A panel 1.2 m square that glows with the radiance of the demo's cabin lamps, and its light of radius 5 cm 10 cm
        // under it. The floor pixel chosen sees the panel's image 30 cm from the light's disc, where the light's lobe adds
        // nothing on a floor this smooth: there the map's reflection, which leaves out the glow, shows black.
        var center = new Vector3(0, 0.5f, -10);
        float[] Rendered(ReflectionSetting reflections) =>
            FloorUnder(
                reflections: reflections,
                floor: GlossyBlack(GlossyRoughness),
                mesh: Primitives.Box(new(PanelHalfSide, 0.05f, PanelHalfSide)),
                center: center,
                glow: CabinGlow,
                light: Lamp(center: center - new Vector3(0, 0.1f, 0), radius: 0.05f)
            );
        (int X, int Y) beside = MirrorPixel(center - new Vector3(0.3f, 0.05f, 0));
        float fromTheMap = At(Rendered(new NoReflections()), beside).Y;
        float fromTheScreen = At(Rendered(ConeTracedReflections.Default), beside).Y;

        fromTheMap.Should().BeLessThan(1e-3f, "the map leaves out the glow of an entity that holds a light");
        fromTheScreen.Should().BeLessThan(1e-3f, "the screen's reflections leave the panel's glow out as the map does");
    }

    [Fact]
    public void AGlossyFloorReflectsAGlowThatNoLightHoldsAsTheMapDoes()
    {
        // A strip 2 m by 10 m above the eye, with the cabin lamps' glow and no light: a source of its own, such as a
        // screen. The floor pixel chosen sees the strip's image 4 m past its middle, where the map's read from the eye
        // also meets the strip, so the map alone shows the glow there.
        var center = new Vector3(0, 0.5f, -6);
        var half = new Vector3(1, 0.05f, 5);
        float[] Rendered(ReflectionSetting reflections) =>
            FloorUnder(
                reflections: reflections,
                floor: GlossyBlack(GlossyRoughness),
                mesh: Primitives.Box(half),
                center: center,
                glow: CabinGlow,
                light: null
            );
        (int X, int Y) under = MirrorPixel(center - new Vector3(0, half.Y, 4));
        float fromTheMap = At(Rendered(new NoReflections()), under).Y;
        float fromTheScreen = At(Rendered(ConeTracedReflections.Default), under).Y;

        fromTheMap.Should().BeGreaterThan(0.3f, "the map keeps a glow that no light holds");
        fromTheScreen
            .Should()
            .BeApproximately(
                fromTheMap,
                0.1f * fromTheMap,
                "the screen's reflections keep a glow that no light holds, as the map does"
            );
    }

    [Fact]
    public void ALampsGlowAddsNoLightToAMatteFloorThroughTheMapsIrradiance()
    {
        // The strip above the eye, now with a light of radius 5 cm 10 cm under its middle that its entity holds, so its
        // glow is that light's face. The map leaves the glow out (docs/plans/environment-map.md, decision 18), and its
        // irradiance lights the floor under the strip as if the strip did not glow: the lamp's power reaches the floor
        // once, through its light.
        var center = new Vector3(0, 0.5f, -6);
        float[] Rendered(float glow) =>
            FloorUnder(
                reflections: new NoReflections(),
                floor: MatteWhite,
                mesh: Primitives.Box(new(1, 0.05f, 5)),
                center: center,
                glow: glow,
                light: Lamp(center: center - new Vector3(0, 0.1f, 0), radius: 0.05f)
            );
        (int X, int Y) under = PixelOf(new(0, FloorHeight, center.Z));
        float glowing = At(Rendered(CabinGlow), under).Y;
        float dark = At(Rendered(0), under).Y;

        dark.Should().BeGreaterThan(0.05f, "the lamp's light reaches the floor under it");
        glowing
            .Should()
            .BeApproximately(dark, 1e-3f + 0.01f * dark, "the map's irradiance leaves out the glow of a lamp's entity");
    }
}
