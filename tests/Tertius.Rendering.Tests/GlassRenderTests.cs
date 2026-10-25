using System.Numerics;
using static Tertius.Rendering.Tests.BrdfTesting;

namespace Tertius.Rendering.Tests;

/// <summary>
/// The blend of the transparent pass (ARCHITECTURE.md, "Transparent objects"): a pane's specular light and emission add
/// in full, its diffuse light scales by its opacity, and it lets through (1 − opacity)(1 − E) of what lies behind it.
/// A pane 1 m wide faces the camera 3 m ahead, turned up by a tilt, in front of a square 3 m behind it that emits a
/// known radiance. Each test reads the pixel at the pane's center.
/// </summary>
[Collection(GpuCollection.Name)]
public sealed class GlassRenderTests
{
    private const int Width = 320,
        Height = 180;

    /// <summary>Two steps of an 8-bit channel: the rounding of both images.</summary>
    private const float Tolerance = 2f / 255;

    /// <summary>The roughness of the demo's glass.</summary>
    private const float GlassRoughness = 0.18f;

    /// <summary>A tilt that shows the pane at a grazing angle: its view cosine is about 0.26.</summary>
    private const float Grazing = 75;

    private static readonly Vector3 PaneCenter = new(0, 0, -3);

    private static Vector2 PanePixel => Holding(Gpu.Project(point: PaneCenter, width: Width, height: Height));

    /// <summary>The unit direction from the pane, at the pixel of its center, to the eye.</summary>
    private static Double3 ToView => -Ray(PanePixel, Width, Height);

    private static Double3 NormalOf(float tilt)
    {
        double radians = tilt * Math.PI / 180;
        return new(0, Math.Sin(radians), Math.Cos(radians));
    }

    /// <summary>A snapshot with no ambient light, and the sun along <paramref name="sunDirection"/>.</summary>
    private static RenderSnapshot Unlit(Vector3 sunDirection, float sunIntensity)
    {
        RenderSnapshot snapshot = Gpu.Snapshot(sunDirection, sunIntensity: sunIntensity);
        snapshot.Ambient.Intensity = 0;
        return snapshot;
    }

    /// <summary>
    /// Renders <paramref name="snapshot"/> with a pane of <paramref name="pane"/>, turned up by <paramref name="tilt"/>
    /// degrees, in front of a square that emits <paramref name="backdrop"/>, and gives the color at the pane's center.
    /// </summary>
    private static Vector3 ShownThroughPane(
        VulkanRenderer renderer,
        RenderSnapshot snapshot,
        MaterialDesc pane,
        float tilt,
        Vector3 backdrop,
        string name
    )
    {
        MeshId square = renderer.CreateMesh(Primitives.Plane(1));
        Quaternion facing = Quaternion.CreateFromAxisAngle(Vector3.UnitX, MathF.PI / 2);
        snapshot.Instances.Add(
            Gpu.Instance(
                mesh: square,
                material: Glow(renderer, backdrop),
                position: new(0, 0, -6),
                scale: new(3),
                rotation: facing,
                radius: 4.3f
            )
        );
        snapshot.Instances.Add(
            Gpu.Instance(
                mesh: square,
                material: renderer.CreateMaterial(pane),
                position: PaneCenter,
                scale: new(0.5f),
                rotation: Quaternion.CreateFromAxisAngle(Vector3.UnitX, MathF.PI / 2 - tilt * MathF.PI / 180),
                flags: RenderFlags.Transparent,
                radius: 0.75f
            )
        );
        return Gpu.RenderAndCapture(renderer: renderer, snapshot: snapshot, name: name).Color(PanePixel);
    }

    private static MaterialDesc BlackGlass(float opacity, float roughness) =>
        new(BaseColor: new(x: 0, y: 0, z: 0, w: opacity), Roughness: roughness, Reflectance: 1);

    private static void ShouldShow(Vector3 shown, Vector3 expected, string because) =>
        LargestChannelDifference(shown, expected)
            .Should()
            .BeLessThanOrEqualTo(Tolerance, "{0}: shown {1}, expected {2}", because, shown, expected);

    [Theory]
    [InlineData(0.2f)]
    [InlineData(0f)]
    public void AClearPaneShowsTheSunsHighlightAtTheStrengthOfAnOpaquePane(float opacity)
    {
        VulkanRenderer renderer = Gpu.Headless(width: Width, height: Height);
        try
        {
            // The sun stands behind the eye, so its mirror image lies at the pane's center.
            Vector3 opaque = ShownThroughPane(
                renderer: renderer,
                snapshot: Unlit(Vector3.UnitZ, sunIntensity: 0.3f),
                pane: BlackGlass(opacity: 1, roughness: 0.3f),
                tilt: 0,
                backdrop: Vector3.Zero,
                name: "glass-highlight-opaque"
            );
            Vector3 clear = ShownThroughPane(
                renderer: renderer,
                snapshot: Unlit(Vector3.UnitZ, sunIntensity: 0.3f),
                pane: BlackGlass(opacity: opacity, roughness: 0.3f),
                tilt: 0,
                backdrop: Vector3.Zero,
                name: "glass-highlight-clear"
            );

            opaque.X.Should().BeGreaterThan(0.3f, "the sun's highlight in black glass is bright");
            ShouldShow(clear, opaque, "the highlight reflects at the surface, whatever the pane lets through");
        }
        finally
        {
            Gpu.DisposeAndAssertClean(renderer);
        }
    }

    [Fact]
    public void AClearPaneReflectsTheSkyAtAGrazingAngleAtTheStrengthOfAnOpaquePane()
    {
        VulkanRenderer renderer = Gpu.Headless(width: Width, height: Height);
        try
        {
            RenderSnapshot WhiteSky()
            {
                RenderSnapshot snapshot = Gpu.Snapshot(Vector3.UnitY, sunIntensity: 0);
                snapshot.Ambient = new()
                {
                    SkyColor = Vector3.One,
                    GroundColor = Vector3.One,
                    Intensity = 1,
                };
                return snapshot;
            }

            Vector3 opaque = ShownThroughPane(
                renderer: renderer,
                snapshot: WhiteSky(),
                pane: BlackGlass(opacity: 1, roughness: GlassRoughness),
                tilt: Grazing,
                backdrop: Vector3.Zero,
                name: "glass-sky-opaque"
            );
            Vector3 clear = ShownThroughPane(
                renderer: renderer,
                snapshot: WhiteSky(),
                pane: BlackGlass(opacity: 0.2f, roughness: GlassRoughness),
                tilt: Grazing,
                backdrop: Vector3.Zero,
                name: "glass-sky-clear"
            );

            opaque.X.Should().BeGreaterThan(0.3f, "glass at a grazing angle reflects much of the sky");
            ShouldShow(clear, opaque, "the sky's reflection happens at the surface, whatever the pane lets through");
        }
        finally
        {
            Gpu.DisposeAndAssertClean(renderer);
        }
    }

    [Fact]
    public void APaneShowsItsDiffuseLightTimesItsOpacity()
    {
        const float opacity = 0.4f;
        Vector3 sunDirection = Vector3.Normalize(new(0, 0.6f, 0.8f));
        var white = new MaterialSample(BaseColor: new(0.8, 0.8, 0.8), Metallic: 0, Roughness: 0.5, Reflectance: 0);
        Double3 full = white.Reflected(
            normal: NormalOf(0),
            toView: ToView,
            toLight: sunDirection,
            tables: BrdfTables.Shared
        );
        Double3 specular = (white with { BaseColor = Double3.Zero }).Reflected(
            normal: NormalOf(0),
            toView: ToView,
            toLight: sunDirection,
            tables: BrdfTables.Shared
        );
        RenderSnapshot snapshot = Unlit(sunDirection, sunIntensity: 1);
        Double3 irradiance = snapshot.Sun.Color * snapshot.Sun.Intensity;
        Double3 expected = (opacity * (full - specular) + specular) * irradiance;

        VulkanRenderer renderer = Gpu.Headless(width: Width, height: Height);
        try
        {
            Vector3 shown = ShownThroughPane(
                renderer: renderer,
                snapshot: snapshot,
                pane: new(BaseColor: new(x: 0.8f, y: 0.8f, z: 0.8f, w: opacity), Roughness: 0.5f, Reflectance: 0),
                tilt: 0,
                backdrop: Vector3.Zero,
                name: "glass-diffuse"
            );
            Vector3 glow = Displayed(renderer, [expected.ToVector3()], "glass-diffuse-glow")[0];

            glow.X.Should().BeGreaterThan(0.1f, "the sun lights the pane's diffuse part well");
            ShouldShow(shown, glow, "the pane scatters only its opacity's share of the light");
        }
        finally
        {
            Gpu.DisposeAndAssertClean(renderer);
        }
    }

    [Theory]
    [InlineData(0f)]
    [InlineData(Grazing)]
    public void AClearPaneLetsThroughTheBackgroundTimesOneMinusItsOpacityAndOneMinusItsSpecularAlbedo(float tilt)
    {
        const float opacity = 0.2f;
        var backdrop = new Vector3(0.6f, 0.6f, 0.6f);
        var glass = new MaterialSample(BaseColor: Double3.Zero, Metallic: 0, Roughness: GlassRoughness, Reflectance: 1);
        Double3 transmittance = glass.Transmittance(
            opacity: opacity,
            normal: NormalOf(tilt),
            toView: ToView,
            tables: BrdfTables.Shared
        );

        VulkanRenderer renderer = Gpu.Headless(width: Width, height: Height);
        try
        {
            // No light reaches the pane, so it shows only what it lets through.
            Vector3 shown = ShownThroughPane(
                renderer: renderer,
                snapshot: Unlit(Vector3.UnitZ, sunIntensity: 0),
                pane: BlackGlass(opacity: opacity, roughness: GlassRoughness),
                tilt: tilt,
                backdrop: backdrop,
                name: "glass-transmitted"
            );
            Vector3 glow = Displayed(renderer, [(transmittance * backdrop).ToVector3()], "glass-transmitted-glow")[0];

            ShouldShow(shown, glow, $"T = {transmittance} at a tilt of {tilt}°");
        }
        finally
        {
            Gpu.DisposeAndAssertClean(renderer);
        }
    }

    [Fact]
    public void AtAGrazingAngleAClearPaneLetsThroughLessOfTheBackgroundThanHeadOn()
    {
        var backdrop = new Vector3(0.6f, 0.6f, 0.6f);
        VulkanRenderer renderer = Gpu.Headless(width: Width, height: Height);
        try
        {
            Vector3 headOn = ShownThroughPane(
                renderer: renderer,
                snapshot: Unlit(Vector3.UnitZ, sunIntensity: 0),
                pane: BlackGlass(opacity: 0.2f, roughness: GlassRoughness),
                tilt: 0,
                backdrop: backdrop,
                name: "glass-head-on"
            );
            Vector3 grazing = ShownThroughPane(
                renderer: renderer,
                snapshot: Unlit(Vector3.UnitZ, sunIntensity: 0),
                pane: BlackGlass(opacity: 0.2f, roughness: GlassRoughness),
                tilt: Grazing,
                backdrop: backdrop,
                name: "glass-grazing"
            );

            grazing
                .X.Should()
                .BeLessThan(headOn.X - 0.05f, "glass reflects more and lets through less at grazing angles");
        }
        finally
        {
            Gpu.DisposeAndAssertClean(renderer);
        }
    }

    [Fact]
    public void AnOpaquePaneHidesTheBackgroundCompletely()
    {
        VulkanRenderer renderer = Gpu.Headless(width: Width, height: Height);
        try
        {
            Vector3 shown = ShownThroughPane(
                renderer: renderer,
                snapshot: Unlit(Vector3.UnitZ, sunIntensity: 0),
                pane: BlackGlass(opacity: 1, roughness: GlassRoughness),
                tilt: 0,
                backdrop: new(4, 4, 4),
                name: "glass-opaque"
            );

            ShouldShow(
                shown,
                Vector3.Zero,
                "an unlit black pane of opacity 1 shows nothing of the bright square behind"
            );
        }
        finally
        {
            Gpu.DisposeAndAssertClean(renderer);
        }
    }

    [Fact]
    public void AClearPaneShowsItsEmissionInFull()
    {
        var emission = new Vector3(0.5f, 0.3f, 0.1f);
        VulkanRenderer renderer = Gpu.Headless(width: Width, height: Height);
        try
        {
            Vector3 shown = ShownThroughPane(
                renderer: renderer,
                snapshot: Unlit(Vector3.UnitZ, sunIntensity: 0),
                pane: new(
                    BaseColor: new(x: 0, y: 0, z: 0, w: 0.2f),
                    Roughness: GlassRoughness,
                    Reflectance: 0,
                    Emissive: emission
                ),
                tilt: 0,
                backdrop: Vector3.Zero,
                name: "glass-emission"
            );
            Vector3 glow = Displayed(renderer, [emission], "glass-emission-glow")[0];

            ShouldShow(shown, glow, "a pane's own light does not pass through it, so its opacity does not dim it");
        }
        finally
        {
            Gpu.DisposeAndAssertClean(renderer);
        }
    }
}
