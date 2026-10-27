using System.Numerics;
using Tertius.Testing;
using static Tertius.Rendering.Tests.BrdfTesting;

namespace Tertius.Rendering.Tests;

/// <summary>
/// The acceptance criteria of the PBR BRDF on the GPU (docs/plans/pbr-brdf.md, "GPU tests of the acceptance
/// criteria"). The camera is at the origin with identity rotation, so it looks down −Z with +Y up. Each expected value
/// comes from the C# BRDF, and an emissive square of that value shows it through the same tone curve.
/// </summary>
[Collection(GpuCollection.Name)]
public sealed class BrdfRenderTests
{
    private const int Width = 640,
        Height = 360;

    /// <summary>The roughness of the demo's painted parts, which the head-on test of F0 uses.</summary>
    private const double DemoRoughness = 0.38;

    private static Vector2 Pixel(Double3 point) =>
        Holding(Gpu.Project(point: point.ToVector3(), width: Width, height: Height));

    /// <summary>A snapshot with a sun of <paramref name="starIrradiance"/> along <paramref name="toSun"/> and no ambient light.</summary>
    private static RenderSnapshot SunOnly(Vector3 toSun, float starIrradiance)
    {
        RenderSnapshot snapshot = Gpu.Snapshot(toSun, starIrradiance: starIrradiance);
        snapshot.Ambient.Intensity = 0;
        return snapshot;
    }

    /// <summary>A snapshot lit by a hemisphere ambient light of <paramref name="sky"/> and <paramref name="ground"/> alone.</summary>
    private static RenderSnapshot AmbientOnly(Vector3 sky, Vector3 ground)
    {
        RenderSnapshot snapshot = Gpu.Snapshot(Vector3.UnitY, starIrradiance: 0);
        snapshot.Ambient = new()
        {
            SkyColor = sky,
            GroundColor = ground,
            Intensity = 1,
        };
        return snapshot;
    }

    /// <summary>A visible square of 0.6 m that faces the camera, centered on <paramref name="center"/>.</summary>
    private static RenderInstance FacingSquare(MeshId mesh, MaterialId material, Vector3 center) =>
        Gpu.Instance(
            mesh: mesh,
            material: material,
            position: center,
            scale: Vector3.One,
            rotation: Quaternion.CreateFromAxisAngle(Vector3.UnitX, MathF.PI / 2),
            radius: 0.45f,
            chainDepth: TestChains.CamerasFrame
        );

    /// <summary>
    /// The C# light that a smooth white metal facing the camera at <paramref name="point"/> reflects toward the eye from a
    /// point light of 1,000 W/sr at <paramref name="lightPosition"/>, whose reach is set by the exposure FixedExposure(0).
    /// </summary>
    private static double LampHighlight(Double3 point, Double3 lightPosition)
    {
        Double3 toLight = (lightPosition - point).Normalized();
        double distanceSquared = (lightPosition - point).LengthSquared;
        var white = new MaterialSample(BaseColor: new(1, 1, 1), Metallic: 1, Roughness: 0, Reflectance: 0.5);
        double reach = LightUnits.Reach(1000, LightUnits.CutoffIrradiance(new FixedExposure(0)));
        double ratio = distanceSquared / (reach * reach);
        double window = Math.Pow(Math.Clamp(1 - ratio * ratio, 0, 1), 2);
        Double3 reflected = white.Reflected(
            normal: Double3.UnitZ,
            toView: -point.Normalized(),
            toLight: toLight,
            tables: BrdfTables.Shared
        );
        return reflected.X * 1000 * window / distanceSquared;
    }

    /// <summary>
    /// The C# light that the hemisphere ambient light sends to the eye from the point of a sphere of radius 1 m that
    /// <paramref name="pixel"/> sees, in the opaque pass: the specular target stores the lobe's sky share in 8 bits, and
    /// the emissive target the normal's in a half float.
    /// </summary>
    private static Double3 SphereAmbientLight(
        MaterialSample material,
        Double3 center,
        Vector2 pixel,
        Double3 sky,
        Double3 ground
    )
    {
        Double3 point = OnSphere(Ray(pixel, Width, Height), center, 1);
        Double3 normal = (point - center).Normalized();
        Double3 toView = -point.Normalized();
        double lobeSky = LobeSkyShare(normal: normal, toView: toView, roughness: material.Roughness);
        return material.ReflectedHemisphere(
            normal: normal,
            toView: toView,
            sky: sky,
            ground: ground,
            normalSkyShare: (double)(Half)NormalSkyShare(normal),
            lobeSkyShare: Math.Round(lobeSky * 255) / 255,
            occlusion: 1,
            bounce: Double3.Zero,
            tables: BrdfTables.Shared
        );
    }

    [Fact]
    public void AMetalShowsAHighlightInItsBaseColorAndNoDiffuseLight()
    {
        var center = new Double3(0, 0, -4);
        var gold = new MaterialSample(BaseColor: new(1, 0.71, 0.29), Metallic: 1, Roughness: 0.2, Reflectance: 0.5);
        // The highlight: the front of the sphere, where the sun behind the camera and the view meet the normal.
        Vector2 highlight = Pixel(center + Double3.UnitZ);
        Double3 front = OnSphere(Ray(highlight, Width, Height), center, 1);
        Double3 peak = gold.Reflected(
            normal: (front - center).Normalized(),
            toView: -front.Normalized(),
            toLight: Double3.UnitZ,
            tables: BrdfTables.Shared
        );
        // 60° from the highlight the sun meets the surface at n·l = 0.5, where a non-metal shows its diffuse light.
        Vector2 aside = Pixel(center + new Double3(Math.Sin(Math.PI / 3), 0, Math.Cos(Math.PI / 3)));
        Double3 side = OnSphere(Ray(aside, Width, Height), center, 1);
        Double3 sideLight = gold.Reflected(
            normal: (side - center).Normalized(),
            toView: -side.Normalized(),
            toLight: Double3.UnitZ,
            tables: BrdfTables.Shared
        );
        sideLight.X.Should().BeLessThan(1e-4 * peak.X, "the C# lobe 60° from its peak is under a ten-thousandth of it");

        // A dim sun keeps the highlight below 1 before the tone curve, which turns any bright color white. A bright one
        // makes the diffuse light that a non-metal would show there visible: 0.3 · 0.5 / π, about 0.05.
        float dim = (float)(0.8 / peak.X);
        VulkanRenderer renderer = Gpu.Headless(width: Width, height: Height);
        try
        {
            MeshId sphere = renderer.CreateMesh(Primitives.Sphere(1f, 128, 64));
            MaterialId material = renderer.CreateMaterial(
                new(
                    BaseColor: new(x: 1, y: 0.71f, z: 0.29f, w: 1),
                    Roughness: 0.2f,
                    Model: new StandardShading(Metallic: 1)
                )
            );
            RenderInstance instance = Gpu.Instance(
                mesh: sphere,
                material: material,
                position: center.ToVector3(),
                scale: Vector3.One,
                radius: 1,
                chainDepth: TestChains.CamerasFrame
            );
            RenderSnapshot dimSnapshot = SunOnly(Vector3.UnitZ, dim);
            dimSnapshot.Instances.Add(instance);
            Vector3 shine = Gpu.RenderAndCapture(renderer: renderer, snapshot: dimSnapshot, name: "brdf-gold-dim")
                .Color(highlight);
            RenderSnapshot brightSnapshot = SunOnly(Vector3.UnitZ, 0.3f);
            brightSnapshot.Instances.Add(instance);
            Vector3 dark = Gpu.RenderAndCapture(renderer: renderer, snapshot: brightSnapshot, name: "brdf-gold-bright")
                .Color(aside);

            (shine.X > shine.Y && shine.Y > shine.Z).Should().BeTrue($"gold reflects in its own color, not {shine}");
            dark.Should().Be(Vector3.Zero, "a metal has no diffuse light, and its lobe is all but zero there");
        }
        finally
        {
            Gpu.DisposeAndAssertClean(renderer);
        }
    }

    [Fact]
    public void ANonMetalsHighlightIsWhiteWithAnF0NearFourPercent()
    {
        var center = new Double3(0, 0, -3);
        Vector2 pixel = Pixel(center);
        Double3 point = OnPlane(Ray(pixel, Width, Height), center, Double3.UnitZ);
        Double3 toView = -point.Normalized();
        double roughness = DemoRoughness;
        // The light of a white highlight with an F0 of f under the sun: the red diffuse light plus the lobe of that F0.
        Vector3 HighlightOf(double f0, Double3 sunIrradiance)
        {
            var red = new MaterialSample(BaseColor: new(1, 0, 0), Metallic: 0, Roughness: roughness, Reflectance: 0.5);
            Double3 diffuse = red.DiffuseColor / Math.PI;
            // A metal of base color f0 has exactly that F0 and no diffuse color.
            var lobe = new MaterialSample(
                BaseColor: new(f0, f0, f0),
                Metallic: 1,
                Roughness: roughness,
                Reflectance: 0.5
            );
            Double3 reflected =
                diffuse
                + lobe.Reflected(
                    normal: Double3.UnitZ,
                    toView: toView,
                    toLight: Double3.UnitZ,
                    tables: BrdfTables.Shared
                );
            return (reflected * sunIrradiance).ToVector3();
        }

        VulkanRenderer renderer = Gpu.Headless(width: Width, height: Height);
        try
        {
            MeshId square = renderer.CreateMesh(Primitives.Plane(0.3f));
            MaterialId red = renderer.CreateMaterial(
                new(BaseColor: new(x: 1, y: 0, z: 0, w: 1), Roughness: (float)roughness, Reflectance: 0.5f)
            );
            RenderSnapshot snapshot = SunOnly(Vector3.UnitZ, 1);
            snapshot.Instances.Add(FacingSquare(mesh: square, material: red, center: center.ToVector3()));
            Vector3 highlight = Gpu.RenderAndCapture(renderer: renderer, snapshot: snapshot, name: "brdf-red-highlight")
                .Color(pixel);
            Double3 sunIrradiance = Gpu.StarIrradianceRgb(snapshot);
            Vector3[] bounds = Displayed(
                renderer,
                [HighlightOf(0.036, sunIrradiance), HighlightOf(0.044, sunIrradiance)],
                "brdf-red-bounds"
            );

            highlight.Y.Should().BeInRange(bounds[0].Y, bounds[1].Y, "the highlight's green is that of an F0 near 4 %");
            highlight.Z.Should().BeInRange(bounds[0].Z, bounds[1].Z, "the highlight is white, so its blue is too");
        }
        finally
        {
            Gpu.DisposeAndAssertClean(renderer);
        }
    }

    [Fact]
    public void ARoughSurfacesHighlightHasTheLongTailOfGgx()
    {
        // The sun stands behind the camera, 65° up. Along the center column, the angle between the half vector and the
        // ground's normal is 90° − (65° + the view's depression) / 2, so the lobe's twentieth lies in view.
        var toSun = new Double3(0, Math.Sin(65 * Math.PI / 180), Math.Cos(65 * Math.PI / 180));
        const double roughness = 0.6;
        var lobe = new GgxLobe(roughness);
        // A black non-metal: the tone curve mixes the channels, so a red base color would leak into the green.
        var black = new MaterialSample(BaseColor: Double3.Zero, Metallic: 0, Roughness: roughness, Reflectance: 0.5);
        Vector2 pixel = Vector2.Zero;
        Double3 point = Double3.Zero;
        double nearest = double.MaxValue;
        for (int y = Height / 2 + 1; y < Height; y++)
        {
            var candidate = new Vector2(Width / 2, y);
            Double3 onGround = OnPlane(Ray(candidate, Width, Height), new(0, -1, 0), Double3.UnitY);
            Double3 half = (toSun - onGround.Normalized()).Normalized();
            double share = Math.Abs(lobe.Distribution(half.Y) / lobe.Distribution(1) - 1.0 / 20);
            if (share < nearest)
                (nearest, pixel, point) = (share, candidate, onGround);
        }

        Double3 toView = -point.Normalized();
        Double3 halfVector = (toSun + toView).Normalized();
        double blinnPhongExponent = 2 / (lobe.Alpha * lobe.Alpha) - 2;
        double blinnPhong = (blinnPhongExponent + 2) / (2 * Math.PI) * Math.Pow(halfVector.Y, blinnPhongExponent);
        (blinnPhong / lobe.Distribution(halfVector.Y))
            .Should()
            .BeLessThan(0.2, "the Blinn-Phong lobe of the same highlight falls far faster");
        // A bright sun lifts the tail above the toe of the tone curve.
        RenderSnapshot snapshot = SunOnly(toSun.ToVector3(), 40);
        Double3 tail =
            black.Reflected(normal: Double3.UnitY, toView: toView, toLight: toSun, tables: BrdfTables.Shared)
            * Gpu.StarIrradianceRgb(snapshot);

        VulkanRenderer renderer = Gpu.Headless(width: Width, height: Height);
        try
        {
            MeshId ground = renderer.CreateMesh(Primitives.Plane(50));
            MaterialId material = renderer.CreateMaterial(
                new(BaseColor: new(x: 0, y: 0, z: 0, w: 1), Roughness: (float)roughness, Reflectance: 0.5f)
            );
            snapshot.Instances.Add(
                Gpu.Instance(
                    mesh: ground,
                    material: material,
                    position: new(0, -1, 0),
                    scale: Vector3.One,
                    radius: 71,
                    chainDepth: TestChains.CamerasFrame
                )
            );
            Vector3 shown = Gpu.RenderAndCapture(renderer: renderer, snapshot: snapshot, name: "brdf-tail")
                .Color(pixel);
            Vector3[] bounds = Displayed(
                renderer,
                [(tail * 0.8).ToVector3(), (tail * 1.2).ToVector3()],
                "brdf-tail-bounds"
            );

            shown.Y.Should().BeInRange(bounds[0].Y, bounds[1].Y, "the GGX lobe keeps a twentieth of its peak at 46°");
        }
        finally
        {
            Gpu.DisposeAndAssertClean(renderer);
        }
    }

    [Fact]
    public void AMetalLitOnlyByTheAmbientLightIsNotBlack()
    {
        var center = new Double3(0, 0, -4);
        var sky = new Vector3(0.6f, 0, 0);
        var ground = new Vector3(0, 0, 0.6f);
        Vector2 top = Pixel(center + 0.8 * Double3.UnitY);
        Vector2 bottom = Pixel(center - 0.8 * Double3.UnitY);
        var metal = new MaterialSample(BaseColor: new(0.9, 0.9, 0.9), Metallic: 1, Roughness: 0.2, Reflectance: 0.5);
        Double3 reflected = SphereAmbientLight(material: metal, center: center, pixel: top, sky: sky, ground: ground);

        VulkanRenderer renderer = Gpu.Headless(width: Width, height: Height);
        try
        {
            MeshId sphere = renderer.CreateMesh(Primitives.Sphere(1f, 64, 32));
            MaterialId material = renderer.CreateMaterial(
                new(
                    BaseColor: new(x: 0.9f, y: 0.9f, z: 0.9f, w: 1),
                    Roughness: (float)metal.Roughness,
                    Model: new StandardShading(Metallic: 1)
                )
            );
            RenderSnapshot snapshot = AmbientOnly(sky: sky, ground: ground);
            snapshot.Instances.Add(
                Gpu.Instance(
                    mesh: sphere,
                    material: material,
                    position: center.ToVector3(),
                    scale: Vector3.One,
                    radius: 1,
                    chainDepth: TestChains.CamerasFrame
                )
            );
            Capture image = Gpu.RenderAndCapture(renderer: renderer, snapshot: snapshot, name: "brdf-ambient-metal");
            Vector3 topColor = image.Color(top);
            Vector3 bottomColor = image.Color(bottom);
            Vector3 glow = Displayed(renderer, [reflected.ToVector3()], "brdf-ambient-metal-glow")[0];

            (topColor.X > topColor.Z + 0.15f).Should().BeTrue($"the top reflects the red sky, not {topColor}");
            (bottomColor.Z > bottomColor.X + 0.15f)
                .Should()
                .BeTrue($"the bottom reflects the blue ground, not {bottomColor}");
            LargestChannelDifference(topColor, glow)
                .Should()
                .BeLessThanOrEqualTo(
                    2f / 255,
                    $"the top shows the metal's specular ambient light {reflected}: shaded {topColor}, emissive {glow}"
                );
        }
        finally
        {
            Gpu.DisposeAndAssertClean(renderer);
        }
    }

    [Fact]
    public void ASmoothMetalReflectsTheSunWithTheLobeOfTheRoughnessFloor()
    {
        var center = new Double3(0, 0, -3);
        Vector2 pixel = Pixel(center);
        Double3 point = OnPlane(Ray(pixel, Width, Height), center, Double3.UnitZ);
        // The sun stands 0.6 rad above the view axis, so the half vector leans θ = 0.3 rad from the normal. There D is
        // about α² / (π θ⁴), which scales with the floor's fourth power. Near the peak, the rounding of 32-bit floats
        // swamps D. Here it moves it a few percent.
        var toSun = new Double3(0, Math.Sin(0.6), Math.Cos(0.6));
        var mirror = new MaterialSample(BaseColor: new(1, 1, 1), Metallic: 1, Roughness: 0, Reflectance: 0.5);
        Double3 reflected = mirror.Reflected(
            normal: Double3.UnitZ,
            toView: -point.Normalized(),
            toLight: toSun,
            tables: BrdfTables.Shared
        );
        // A dim sun keeps the light below 1 before the tone curve, which turns any bright color white.
        RenderSnapshot snapshot = SunOnly(toSun.ToVector3(), (float)(0.8 / reflected.X));
        Double3 shine = reflected * Gpu.StarIrradianceRgb(snapshot);

        VulkanRenderer renderer = Gpu.Headless(width: Width, height: Height);
        try
        {
            MeshId square = renderer.CreateMesh(Primitives.Plane(0.3f));
            MaterialId material = renderer.CreateMaterial(
                new(BaseColor: Vector4.One, Roughness: 0, Model: new StandardShading(Metallic: 1))
            );
            snapshot.Instances.Add(FacingSquare(mesh: square, material: material, center: center.ToVector3()));
            Vector3 shown = Gpu.RenderAndCapture(renderer: renderer, snapshot: snapshot, name: "brdf-floor-lobe")
                .Color(pixel);
            Vector3[] bounds = Displayed(
                renderer,
                [(shine * 0.8).ToVector3(), (shine * 1.2).ToVector3()],
                "brdf-floor-lobe-bounds"
            );

            shown
                .X.Should()
                .BeInRange(bounds[0].X, bounds[1].X, "the floor of the roughness sets the height of the lobe's tail");
        }
        finally
        {
            Gpu.DisposeAndAssertClean(renderer);
        }
    }

    /// <summary>
    /// The color at the center of a mirror square at <paramref name="center"/>, facing the camera, under a lamp of
    /// intensity 1,000 at <paramref name="lamp"/>, drawn by a new renderer at <paramref name="exposure"/>.
    /// </summary>
    private static Vector3 MirrorUnderALamp(ExposureSetting exposure, Double3 center, Double3 lamp, string name)
    {
        VulkanRenderer renderer = Gpu.Headless(width: Width, height: Height, exposure: exposure);
        try
        {
            MeshId square = renderer.CreateMesh(Primitives.Plane(0.3f));
            MaterialId mirror = renderer.CreateMaterial(
                new(BaseColor: Vector4.One, Roughness: 0, Model: new StandardShading(Metallic: 1))
            );
            RenderSnapshot snapshot = SunOnly(Vector3.UnitY, 0);
            snapshot.Lights.Add(
                Gpu.Lamp(
                    position: lamp.ToVector3(),
                    color: Vector3.One,
                    intensity: 1000,
                    chainDepth: TestChains.CamerasFrame
                )
            );
            snapshot.Instances.Add(FacingSquare(mesh: square, material: mirror, center: center.ToVector3()));
            return Gpu.RenderAndCapture(renderer: renderer, snapshot: snapshot, name: name).Color(Pixel(center));
        }
        finally
        {
            Gpu.DisposeAndAssertClean(renderer);
        }
    }

    [Fact]
    public void AHighlightBrighterThanTheHdrTargetHoldsShowsWhite()
    {
        var center = new Double3(0, 0, -1);
        // The light hangs 0.3 m in front of the square, on the view axis, so the eye sees it at the square's center.
        var lightPosition = new Double3(0, 0, -0.7);
        Vector2 pixel = Pixel(center);
        Double3 point = OnPlane(Ray(pixel, Width, Height), center, Double3.UnitZ);
        double reflected = LampHighlight(point: point, lightPosition: lightPosition);
        reflected.Should().BeGreaterThan(ShaderInput.MaxRadiance, "the C# highlight is beyond the HDR target's cap");
        const float dimming = 1e-5f;

        Vector3 shown = MirrorUnderALamp(
            exposure: new FixedExposure(0),
            center: center,
            lamp: lightPosition,
            name: "brdf-clamp"
        );
        // Turned down by the exposure before the clamp, the highlight fits the target and shows as itself.
        Vector3 dimmed = MirrorUnderALamp(
            exposure: new FixedExposure(MathF.Log2(dimming)),
            center: center,
            lamp: lightPosition,
            name: "brdf-clamp-dimmed"
        );
        VulkanRenderer reference = Gpu.Headless(width: Width, height: Height);
        Vector3 expected;
        try
        {
            expected = Displayed(reference, [new((float)reflected * dimming)], "brdf-clamp-expected")[0];
        }
        finally
        {
            Gpu.DisposeAndAssertClean(reference);
        }

        shown.Should().Be(Vector3.One, "the clamp keeps the value finite, and the tone curve turns it white");
        // The clamp before the exposure would show the cap, 32,256, times the dimming, far darker.
        LargestChannelDifference(dimmed, expected)
            .Should()
            .BeLessThanOrEqualTo(
                2f / 255,
                "the exposure multiplies the highlight of {0} before the clamp, so it shows as {1}, not {2}",
                reflected,
                expected,
                dimmed
            );
    }

    [Fact]
    public void AMetallicRoughnessMapHasGltfsLayoutWithRoughnessInGreenAndMetallicInBlue()
    {
        VulkanRenderer renderer = Gpu.Headless(width: Width, height: Height);
        try
        {
            MeshId square = renderer.CreateMesh(Primitives.Plane(0.3f));
            // Red is unread: glTF keeps occlusion there. Green 51 of 255 is a roughness of 0.2, and blue 0 a non-metal.
            TextureId map = renderer.CreateTexture(new(Width: 1, Height: 1, Rgba: [255, 51, 0, 255], Srgb: false));
            var gold = new Vector4(x: 1, y: 0.71f, z: 0.29f, w: 1);
            MaterialId mapped = renderer.CreateMaterial(
                new(BaseColor: gold, Roughness: 1, Model: new StandardShading(Metallic: 1), MetallicRoughnessMap: map)
            );
            MaterialId plain = renderer.CreateMaterial(
                new(BaseColor: gold, Roughness: 0.2f, Model: new StandardShading(Metallic: 0))
            );
            RenderSnapshot snapshot = SunOnly(new(0, 0.3f, 1), 2);
            // Mirror images about the view axis, under a sun in the mirror plane, so both receive the same light.
            var left = new Vector3(-0.5f, 0, -3);
            var right = new Vector3(0.5f, 0, -3);
            snapshot.Instances.Add(FacingSquare(mesh: square, material: mapped, center: left));
            snapshot.Instances.Add(FacingSquare(mesh: square, material: plain, center: right));
            Capture image = Gpu.RenderAndCapture(
                renderer: renderer,
                snapshot: snapshot,
                name: "brdf-metallic-roughness-map"
            );
            Vector3 throughTheMap = image.Color(Gpu.Project(point: left, width: Width, height: Height));
            Vector3 fromTheFactors = image.Color(Gpu.Project(point: right, width: Width, height: Height));

            throughTheMap.Should().Be(fromTheFactors, "the map's green scales the roughness and its blue the metallic");
        }
        finally
        {
            Gpu.DisposeAndAssertClean(renderer);
        }
    }
}
