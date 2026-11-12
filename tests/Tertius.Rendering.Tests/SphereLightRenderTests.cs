using System.Numerics;
using Tertius.Testing;
using static Tertius.Rendering.Tests.BrdfTesting;

namespace Tertius.Rendering.Tests;

/// <summary>
/// The highlight of a sized light on the GPU (docs/plans/pbr-brdf.md, "Sized lights"): a mirror shows the light's disc
/// at the light's angular size and at one radiance across its inside, and a light of radius 0 shades as the point light
/// did before the widened lobe. At the edges of the model, the GPU shows the C# light: on a fully rough surface, inside
/// a light's sphere, and where the representative point sinks below the horizon.
/// </summary>
[Collection(GpuCollection.Name)]
public sealed class SphereLightRenderTests
{
    private const int Width = 640,
        Height = 360;

    /// <summary>The mirror faces the camera from this distance, in m, centered on the view axis.</summary>
    private const float MirrorDistance = 3;

    /// <summary>The radius of the test lamp, in m, and its distance from the camera toward the mirror.</summary>
    private const double LampRadius = 0.15,
        LampDistance = 1;

    /// <summary>The test lamp's radiant intensity, in W/sr.</summary>
    private const float LampIntensity = 0.8f;

    /// <summary>
    /// The half-maximum angle of the floored GGX lobe beyond the disc, in rad of the reflected ray, per α: D falls to half
    /// its peak where tan² θ_h = α² (√2 − 1), and the reflected ray turns by 2 θ_h.
    /// </summary>
    private static readonly double HalfMaximumPerAlpha = 2 * Math.Sqrt(Math.Sqrt(2) - 1);

    /// <summary>The tilt of the mirror, in rad, in the test of the disc's inside: 30°.</summary>
    private const float ObliqueTilt = MathF.PI / 6;

    /// <summary>
    /// A black glossy mirror of roughness 0 and F0 = 0.04 stands on the view axis, and a sized light stands on the axis
    /// that the mirror reflects the view axis into, so the mirror's image of the light's disc is centered on the
    /// screen. A narrow view makes the disc many pixels wide.
    /// </summary>
    public static TheoryData<string> SizedLights => ["sun", "lamp"];

    /// <summary>
    /// The unit normal of the mirror tilted by <paramref name="tilt"/> rad from facing the camera, down toward −Y:
    /// (0, −sin t, cos t), the plane's +Y turned by π / 2 + t about X.
    /// </summary>
    private static Quaternion MirrorRotation(float tilt) =>
        Quaternion.CreateFromAxisAngle(Vector3.UnitX, MathF.PI / 2 + tilt);

    /// <summary>
    /// The light on the axis that the mirror tilted by <paramref name="tilt"/> reflects the view axis into, r = (0,
    /// −sin 2t, cos 2t): toward +Z, behind the camera, for a mirror that faces it. The sun: the test star along r, so
    /// the mirror shows it at its own angular radius. A dim star and a mirror that reflects 4 % keep the disc below the
    /// tone curve's shoulder. The star stays above the cutoff irradiance, π / 8192 W/m² at 0 stops, below which the
    /// batcher leaves a light out. The lamp: a sphere of <see cref="LampRadius"/>, 2 m from the mirror along r, which
    /// is <see cref="LampDistance"/> from the camera for a mirror that faces it. The camera sees its image in the
    /// mirror 5 m away, asin(0.15 / 5) = 0.030 rad wide in radius.
    /// </summary>
    private static RenderSnapshot SizedLightInTheMirror(bool sun, float fov, float tilt)
    {
        var reflectedAxis = new Vector3(0, -MathF.Sin(2 * tilt), MathF.Cos(2 * tilt));
        RenderSnapshot snapshot = Gpu.Snapshot(reflectedAxis, starIrradiance: sun ? 2e-3f : 0);
        snapshot.Camera.VerticalFov = fov;
        snapshot.Ambient.Intensity = 0;
        Vector3 lamp = new Vector3(0, 0, -MirrorDistance) + (MirrorDistance - (float)LampDistance) * reflectedAxis;
        if (!sun)
            snapshot.Lights.Add(
                new PointRenderLight(
                    EntityIndex: Gpu.LampEntity,
                    EntityGeneration: 0,
                    Position: lamp,
                    PreviousPosition: lamp,
                    Color: Vector3.One,
                    Power: 4 * MathF.PI * LampIntensity,
                    Radius: (float)LampRadius,
                    CastsShadow: false,
                    ChainDepth: TestChains.CamerasFrame
                )
            );
        return snapshot;
    }

    /// <summary>The black glossy material of the mirror: roughness 0 and F0 = 0.04.</summary>
    private static readonly MaterialDesc BlackMirror = new(
        BaseColor: new(x: 0, y: 0, z: 0, w: 1),
        Roughness: 0,
        Reflectance: 0.5f
    );

    /// <summary>
    /// Adds the black glossy mirror, tilted by <paramref name="tilt"/> rad (<see cref="MirrorRotation"/>), to
    /// <paramref name="snapshot"/>, renders it, and captures the image.
    /// </summary>
    private static Capture RenderMirror(VulkanRenderer renderer, RenderSnapshot snapshot, float tilt, string name) =>
        RenderSquare(renderer: renderer, snapshot: snapshot, desc: BlackMirror, tilt: tilt, name: name);

    /// <summary>
    /// Adds a square of <paramref name="desc"/> where the mirror stands, tilted by <paramref name="tilt"/> rad
    /// (<see cref="MirrorRotation"/>), to <paramref name="snapshot"/>, renders it, and captures the image.
    /// </summary>
    private static Capture RenderSquare(
        VulkanRenderer renderer,
        RenderSnapshot snapshot,
        MaterialDesc desc,
        float tilt,
        string name
    )
    {
        MeshId square = renderer.CreateMesh(Primitives.Plane(0.6f));
        MaterialId material = renderer.CreateMaterial(desc);
        snapshot.Instances.Add(
            Gpu.Instance(
                mesh: square,
                material: material,
                position: new(0, 0, -MirrorDistance),
                scale: Vector3.One,
                rotation: MirrorRotation(tilt),
                radius: 0.85f,
                chainDepth: TestChains.CamerasFrame
            )
        );
        return Gpu.RenderAndCapture(renderer: renderer, snapshot: snapshot, name: name);
    }

    /// <summary>
    /// The C# light per unit irradiance at the disc's center, where the camera looks at the mirror tilted by
    /// <paramref name="tilt"/> rad, and the light, whose angular radius has the sine <paramref name="lightSine"/>,
    /// stands on the reflected axis: in the mirror's axes, the view and the light are t from the normal on either side
    /// of it.
    /// </summary>
    private static Double3 DiscCenter(double lightSine, double tilt) =>
        new MaterialSample(BaseColor: Double3.Zero, Metallic: 0, Roughness: 0, Reflectance: 0.5).Reflected(
            normal: Double3.UnitZ,
            toView: new(0, Math.Sin(tilt), Math.Cos(tilt)),
            toLight: new(0, -Math.Sin(tilt), Math.Cos(tilt)),
            tables: BrdfTables.Shared,
            lightSine: lightSine
        );

    /// <summary>
    /// <paramref name="irradiance"/> faded by the window of the light's reach, (1 − (d / reach)⁴)², with (d / reach)²
    /// the cutoff irradiance at 0 stops over the brightest channel (LightUnits.Reach). The dim test star loses 7 %.
    /// </summary>
    private static Double3 InReach(Double3 irradiance)
    {
        double ratio = LightUnits.CutoffIrradiance(0) / Math.Max(irradiance.X, Math.Max(irradiance.Y, irradiance.Z));
        double window = 1 - ratio * ratio;
        return irradiance * (window * window);
    }

    /// <summary>
    /// A sized light seen in the tilted mirror (<see cref="SizedLightInTheMirror"/>): its snapshot, the view's
    /// height <see cref="Fov"/> in rad and its <see cref="FocalLength"/> in pixels, the light's distance from the
    /// mirror and its image's from the camera in m, the image's angular radius in rad, and the C# radiance of the disc's
    /// center, with the window of the light's reach.
    /// </summary>
    private sealed record MirrorDisc(
        RenderSnapshot Snapshot,
        float Fov,
        double LightToMirror,
        double ImageDistance,
        double ImageAngularRadius,
        Double3 Plateau
    )
    {
        public double FocalLength => Height / 2.0 / Math.Tan(Fov / 2);

        /// <summary>The "sun" or the "lamp" of <see cref="SizedLights"/> in the mirror tilted by <paramref name="tilt"/> rad.</summary>
        public static MirrorDisc Of(string light, float tilt)
        {
            bool sun = light == "sun";
            float fov = sun ? 0.03f : 0.25f;
            double lightToMirror = sun ? Gpu.StarDistance : MirrorDistance - LampDistance;
            double imageDistance = sun ? Gpu.StarDistance : MirrorDistance + lightToMirror;
            double lightSine = sun ? Gpu.StarSine : LampRadius / lightToMirror;
            RenderSnapshot snapshot = SizedLightInTheMirror(sun: sun, fov: fov, tilt: tilt);
            Double3 irradiance = sun
                ? Gpu.StarIrradianceRgb(snapshot)
                : new Double3(1, 1, 1) * (LampIntensity / (lightToMirror * lightToMirror));
            return new(
                Snapshot: snapshot,
                Fov: fov,
                LightToMirror: lightToMirror,
                ImageDistance: imageDistance,
                ImageAngularRadius: sun ? Gpu.StarAngularRadius : Math.Asin(LampRadius / imageDistance),
                Plateau: DiscCenter(lightSine: lightSine, tilt: tilt) * InReach(irradiance)
            );
        }
    }

    [Theory]
    [MemberData(nameof(SizedLights))]
    public void AMirrorShowsTheDiscOfASizedLightAtTheLightsAngularSize(string light)
    {
        var disc = MirrorDisc.Of(light: light, tilt: 0);
        Double3 plateau = disc.Plateau;

        VulkanRenderer renderer = Gpu.Headless(width: Width, height: Height);
        try
        {
            Capture image = RenderMirror(
                renderer: renderer,
                snapshot: disc.Snapshot,
                tilt: 0,
                name: "sphere-light-" + light
            );
            // The disc's center within 1 % of the C# radiance.
            Vector3[] shown = Displayed(
                renderer,
                [(plateau * 0.5).ToVector3(), (plateau * 0.99).ToVector3(), (plateau * 1.01).ToVector3()],
                "sphere-light-" + light + "-glow"
            );

            image
                .Color(Width / 2, Height / 2)
                .X.Should()
                .BeInRange(
                    shown[1].X,
                    shown[2].X,
                    "the disc's center shows the C# lobe's radiance {0}, below the tone curve's shoulder",
                    plateau.X
                );
            double radius = Math.Atan(HalfMaximumRadius(image, threshold: shown[0].X) / disc.FocalLength);
            // Outside the disc, the lobe of the roughness floor falls from the disc's edge as it falls from a point
            // light's peak. Seen from the camera, the turn of the reflected ray scales by the light's distance from the
            // mirror over the distance of its image.
            double alpha = new GgxLobe(0).Alpha;
            double imageScale = disc.LightToMirror / disc.ImageDistance;
            // A pixel, and the center's 1 %: it moves the threshold from half the shown peak to between 0.495 and
            // 0.505 of it, where the lobe falls by 0.91 of its peak per α of tan θ_h, which moves the edge by up to
            // 0.011 α of the reflected ray.
            radius
                .Should()
                .BeApproximately(
                    disc.ImageAngularRadius + HalfMaximumPerAlpha * alpha * imageScale,
                    disc.Fov / Height + 0.011 * alpha * imageScale,
                    "the mirror shows the {0}'s disc of angular radius {1} rad, widened by the floored lobe's half "
                        + "maximum",
                    light,
                    disc.ImageAngularRadius
                );
        }
        finally
        {
            Gpu.DisposeAndAssertClean(renderer);
        }
    }

    [Theory]
    [MemberData(nameof(SizedLights))]
    public void AMirrorShowsTheInsideOfASizedLightsDiscWithinOnePercentOfOneRadiance(string light)
    {
        var disc = MirrorDisc.Of(light: light, tilt: ObliqueTilt);
        Double3 plateau = disc.Plateau;

        VulkanRenderer renderer = Gpu.Headless(width: Width, height: Height);
        try
        {
            Capture image = RenderMirror(
                renderer: renderer,
                snapshot: disc.Snapshot,
                tilt: ObliqueTilt,
                name: "sphere-light-flat-" + light
            );
            Vector3[] shown = Displayed(
                renderer,
                [(plateau * 0.99).ToVector3(), (plateau * 1.01).ToVector3()],
                "sphere-light-flat-" + light + "-glow"
            );

            // Every pixel within 60 % of the disc's radius sees h = n. Where a 32-bit n·h falls a float step or two
            // below 1, the form 1 − (n·h)² of D's denominator moves D by several percent at the floor's α² = 4.1e-6,
            // from one pixel to the next: a speckle. A mirror that faces the camera gets n·h = 1 exactly and hides it,
            // so this mirror stands at 30°. Across that inside, the view, the light's distance and its sine change the
            // radiance by less than 0.3 %.
            int inside = (int)(0.6 * Math.Tan(disc.ImageAngularRadius) * disc.FocalLength);

            RedsOutside(image: image, radius: inside, low: shown[0].X, high: shown[1].X)
                .Should()
                .BeEmpty(
                    "the inside of the {0}'s disc shows the C# radiance {1} within 1 %, shown from {2} to {3}, at "
                        + "every one of its pixels",
                    light,
                    plateau.X,
                    shown[0].X,
                    shown[1].X
                );
        }
        finally
        {
            Gpu.DisposeAndAssertClean(renderer);
        }
    }

    /// <summary>
    /// The square of <paramref name="desc"/>, tilted by <paramref name="tilt"/> rad, under the lights of
    /// <paramref name="snapshot"/>: the red channel of the pixel at the image's center, which sees the square's center
    /// straight along the view axis, and the reds that 98 % and 102 % of the C# radiance <paramref name="expected"/> show.
    /// </summary>
    private static (float Red, float Low, float High) CenterOfASquare(
        RenderSnapshot snapshot,
        MaterialDesc desc,
        float tilt,
        Double3 expected,
        string name
    )
    {
        VulkanRenderer renderer = Gpu.Headless(width: Width, height: Height);
        try
        {
            Capture image = RenderSquare(renderer: renderer, snapshot: snapshot, desc: desc, tilt: tilt, name: name);
            Vector3[] shown = Displayed(
                renderer,
                [(expected * 0.98).ToVector3(), (expected * 1.02).ToVector3()],
                name + "-glow"
            );
            return (Red: image.Color(Width / 2, Height / 2).X, Low: shown[0].X, High: shown[1].X);
        }
        finally
        {
            Gpu.DisposeAndAssertClean(renderer);
        }
    }

    [Fact]
    public void OnAFullyRoughSurfaceTheWidenedLobeStopsAtRoughnessOne()
    {
        // A black non-metal of roughness 1 faces the camera, and a sun of angular radius 0.5 rad stands 0.6 rad off the
        // view axis. Its representative point lies 0.1 rad from the normal, where α + sin ϕ / (2 √(v·h)) reaches 1.24.
        // The stop at 1 keeps (α / α′)² at 1, where 1.24 would take 35 % of the light. The representative point's
        // n·l of 0.995, not the center's 0.825, gives Vis · (n·l) 10 % above the center's.
        const float angularRadius = 0.5f;
        var toSun = new Double3(0, Math.Sin(0.6), Math.Cos(0.6));
        var rough = new MaterialDesc(BaseColor: new(x: 0, y: 0, z: 0, w: 1), Roughness: 1, Reflectance: 0.5f);
        Double3 reflected = TestMaterials
            .SampleOf(rough)
            .Reflected(
                normal: Double3.UnitZ,
                toView: Double3.UnitZ,
                toLight: toSun,
                tables: BrdfTables.Shared,
                lightSine: Math.Sin(angularRadius)
            );
        // A dim sun keeps the light below 1 before the tone curve.
        RenderSnapshot snapshot = Gpu.Snapshot(toSun.ToVector3(), starIrradiance: (float)(0.4 / reflected.X));
        Gpu.Star(snapshot: snapshot, angularRadius: angularRadius);
        snapshot.Ambient.Intensity = 0;
        Double3 expected = reflected * InReach(Gpu.StarIrradianceRgb(snapshot));

        (float red, float low, float high) = CenterOfASquare(
            snapshot: snapshot,
            desc: rough,
            tilt: 0,
            expected: expected,
            name: "sphere-light-rough"
        );

        red.Should()
            .BeInRange(
                low,
                high,
                "a fully rough surface shows the C# light {0} of the lobe of roughness 1 toward the representative point",
                expected.X
            );
    }

    [Fact]
    public void ASurfaceInsideALightsSphereSeesTheLightWithTheSineOfOne()
    {
        // A lamp of radius 0.3 m stands 0.1 m in front of the mirror's center, so the center lies inside its sphere:
        // sin ϕ = min(R / d, 1) = 1, where R / d = 3 would widen the lobe from α + 1/2 to the stop at 1 and take three
        // quarters of the light. Inside its sphere a light gives I / R², and the window of its reach differs from 1 by
        // under 1e-8 at 0.1 m.
        const float radius = 0.3f,
            gap = 0.1f,
            intensity = 3;
        Double3 reflected = TestMaterials
            .SampleOf(BlackMirror)
            .Reflected(
                normal: Double3.UnitZ,
                toView: Double3.UnitZ,
                toLight: Double3.UnitZ,
                tables: BrdfTables.Shared,
                lightSine: 1
            );
        RenderSnapshot snapshot = Gpu.Snapshot(Vector3.UnitZ, starIrradiance: 0);
        snapshot.Ambient.Intensity = 0;
        var lamp = new Vector3(0, 0, -MirrorDistance + gap);
        snapshot.Lights.Add(
            new PointRenderLight(
                EntityIndex: Gpu.LampEntity,
                EntityGeneration: 0,
                Position: lamp,
                PreviousPosition: lamp,
                Color: Vector3.One,
                Power: 4 * MathF.PI * intensity,
                Radius: radius,
                CastsShadow: false,
                ChainDepth: TestChains.CamerasFrame
            )
        );
        Double3 expected = reflected * (intensity / (radius * radius));

        (float red, float low, float high) = CenterOfASquare(
            snapshot: snapshot,
            desc: BlackMirror,
            tilt: 0,
            expected: expected,
            name: "sphere-light-inside"
        );

        red.Should().BeInRange(low, high, "the mirror inside the lamp shows the C# light {0} of sine 1", expected.X);
    }

    [Fact]
    public void WhereASizedLightsRepresentativePointSinksBelowTheHorizonTheSurfaceShowsOnlyItsDiffuseLight()
    {
        // The square tilts 1.37 rad, so the eye sees its center 0.2 above its plane. A sun of sine 0.3 stands 1.4 rad
        // from the square's normal, on the eye's side: the mirror ray points away from it, so the point of the sphere
        // nearest the ray's line lies 0.12 below the horizon, while the sun's center stands 0.17 above it
        // (SphereLightLobeTests). A lobe toward that point would take 14 % of the gray non-metal's diffuse light.
        const float tilt = 1.37f;
        var normal = new Double3(0, -Math.Sin(tilt), Math.Cos(tilt));
        var toSun = new Double3(0, Math.Sin(1.4 - tilt), Math.Cos(1.4 - tilt));
        var gray = new MaterialDesc(
            BaseColor: new(x: 0.5f, y: 0.5f, z: 0.5f, w: 1),
            Roughness: 0.5f,
            Reflectance: 0.5f
        );
        Double3 reflected = TestMaterials
            .SampleOf(gray)
            .Reflected(
                normal: normal,
                toView: Double3.UnitZ,
                toLight: toSun,
                tables: BrdfTables.Shared,
                lightSine: 0.3
            );
        // A dim sun keeps the light below 1 before the tone curve. The sun casts no shadow, which a light so low over the
        // square's plane would test at its edge.
        RenderSnapshot snapshot = Gpu.Snapshot(toSun.ToVector3(), starIrradiance: (float)(0.4 / reflected.X));
        Gpu.Star(snapshot: snapshot, angularRadius: (float)Math.Asin(0.3));
        snapshot.Lights[0] = Gpu.Star(snapshot) with { CastsShadow = false };
        snapshot.Ambient.Intensity = 0;
        Double3 expected = reflected * InReach(Gpu.StarIrradianceRgb(snapshot));

        (float red, float low, float high) = CenterOfASquare(
            snapshot: snapshot,
            desc: gray,
            tilt: tilt,
            expected: expected,
            name: "sphere-light-below-horizon"
        );

        red.Should()
            .BeInRange(low, high, "the square shows its C# diffuse light {0} and no specular light", expected.X);
    }

    /// <summary>
    /// A star, a lamp and a spot light, each of radius 0, and the camera's usual ambient light: the lights of the
    /// reference image of the point lights.
    /// </summary>
    private static RenderSnapshot PointLights()
    {
        RenderSnapshot snapshot = Gpu.Snapshot(new(0.4f, 0.6f, 0.7f), starIrradiance: 1.5f);
        Gpu.Star(snapshot: snapshot, angularRadius: 0);
        snapshot.Lights.Add(
            Gpu.Lamp(
                position: new(-0.6f, 0.9f, -3.2f),
                color: new(1f, 0.85f, 0.7f),
                intensity: 2,
                chainDepth: TestChains.CamerasFrame
            )
        );
        snapshot.Lights.Add(
            Gpu.Spot(
                position: new(1.2f, 1.6f, -3),
                direction: Vector3.Normalize(new(-0.3f, -1, -0.4f)),
                color: new(0.7f, 0.85f, 1f),
                intensity: 3,
                innerConeAngle: 0.5f,
                outerConeAngle: 0.9f,
                chainDepth: TestChains.CamerasFrame
            )
        );
        return snapshot;
    }

    /// <summary>
    /// Spheres from mirror to rough, a row of a metal over a row of a non-metal, a glass sphere of the transparent pass
    /// and a smooth floor.
    /// </summary>
    private static void AddSpheresOnAFloor(VulkanRenderer renderer, RenderSnapshot snapshot)
    {
        MeshId sphere = renderer.CreateMesh(Primitives.Sphere(0.18f));
        float[] roughnesses = [0, 0.1f, 0.2f, 0.38f, 0.6f, 1];
        for (int row = 0; row < 2; row++)
        for (int column = 0; column < roughnesses.Length; column++)
        {
            bool metal = row == 0;
            MaterialId material = renderer.CreateMaterial(
                new(
                    BaseColor: metal ? new(x: 0.95f, y: 0.75f, z: 0.4f, w: 1) : new(x: 0.7f, y: 0.2f, z: 0.15f, w: 1),
                    Roughness: roughnesses[column],
                    Model: new StandardShading(Metallic: metal ? 1 : 0)
                )
            );
            snapshot.Instances.Add(
                Gpu.Instance(
                    mesh: sphere,
                    material: material,
                    position: new((column - 2.5f) * 0.45f, 0.25f - row * 0.45f, -4),
                    scale: Vector3.One,
                    radius: 0.18f,
                    chainDepth: TestChains.CamerasFrame
                )
            );
        }

        MaterialId glass = renderer.CreateMaterial(
            new(BaseColor: new(x: 0.9f, y: 0.95f, z: 1f, w: 0.3f), Roughness: 0.05f, Alpha: new GlassAlpha())
        );
        snapshot.Instances.Add(
            Gpu.Instance(
                mesh: sphere,
                material: glass,
                position: new(1.5f, -0.35f, -3.4f),
                scale: Vector3.One,
                radius: 0.18f,
                chainDepth: TestChains.CamerasFrame
            )
        );
        MeshId floor = renderer.CreateMesh(Primitives.Plane(4));
        MaterialId tile = renderer.CreateMaterial(
            MaterialDesc.Color(new(x: 0.3f, y: 0.32f, z: 0.35f, w: 1), roughness: 0.1f)
        );
        snapshot.Instances.Add(
            Gpu.Instance(
                mesh: floor,
                material: tile,
                position: new(0, -0.8f, -4),
                scale: Vector3.One,
                radius: 5.7f,
                chainDepth: TestChains.CamerasFrame
            )
        );
    }

    [Fact]
    public void ALightOfRadiusZeroShadesAsThePointLightDidBeforeTheWidenedLobe()
    {
        // The reference image is the render of the code before the widened lobe (ter-gf0t), made on MoltenVK. The
        // `sized` branch of specularResponse keeps a light of radius 0 on the point light's operations by construction.
        // The default tolerance of 0.1 fails on any visible change but lets one 8-bit level pass: the Vulkan spec lets
        // a conforming driver round a division, inversesqrt or pow differently.
        VulkanRenderer renderer = Gpu.Headless(width: Width, height: Height);
        try
        {
            RenderSnapshot snapshot = PointLights();
            AddSpheresOnAFloor(renderer, snapshot);

            var probe = DirectLightProbe.SpecularPartsOn(renderer);
            try
            {
                ReferenceImages.Verify(renderer: renderer, snapshot: snapshot, name: "sphere-light-radius-zero");
            }
            finally
            {
                // THROWAWAY DIAGNOSTIC (ter-8esk)
                float[] parts = probe.Read();
                float[] hdr = renderer.ReadHdr();
                var lines = new System.Text.StringBuilder();
                for (int y = 152; y <= 157; y++)
                for (int x = 264; x <= 274; x++)
                {
                    int at = 4 * (y * Width + x);
                    lines.Append($"px {x} {y} hdr={hdr[at]:R} directSpec={parts[at]:R} factor={parts[at + 1]:R} indirectSpec={parts[at + 2]:R} directDiff={parts[at + 3]:R}\n");
                }
                File.AppendAllText("/tmp/ter-8esk.txt", lines.ToString());
            }
        }
        finally
        {
            Gpu.DisposeAndAssertClean(renderer);
        }
    }

    /// <summary>
    /// The pixels within <paramref name="radius"/> pixels of the image's center whose red channel lies outside
    /// [<paramref name="low"/>, <paramref name="high"/>], each with its offset from the center and its red.
    /// </summary>
    private static List<(int X, int Y, float Red)> RedsOutside(Capture image, int radius, float low, float high)
    {
        var outside = new List<(int X, int Y, float Red)>();
        for (int y = -radius; y <= radius; y++)
        for (int x = -radius; x <= radius; x++)
        {
            if (x * x + y * y > radius * radius)
                continue;
            float red = image.Color(Width / 2 + x, Height / 2 + y).X;
            if (red < low || red > high)
                outside.Add((X: x, Y: y, Red: red));
        }

        return outside;
    }

    /// <summary>
    /// The disc's radius in pixels on the center row: half the distance between the two places, interpolated between
    /// pixel centers, where the red channel falls below <paramref name="threshold"/> on its way out from the center.
    /// </summary>
    private static double HalfMaximumRadius(Capture image, float threshold)
    {
        int y = Height / 2;
        int right = Width / 2;
        while (right < Width - 1 && image.Color(right + 1, y).X >= threshold)
            right++;
        int left = Width / 2 - 1;
        while (left > 0 && image.Color(left - 1, y).X >= threshold)
            left--;
        double rightEdge = right + Crossing(image.Color(right, y).X, image.Color(right + 1, y).X, threshold);
        double leftEdge = left - Crossing(image.Color(left, y).X, image.Color(left - 1, y).X, threshold);
        return (rightEdge - leftEdge) / 2;
    }

    /// <summary>Where between two neighboring pixels, from 0 to 1, a value falls from <paramref name="inside"/> to
    /// <paramref name="threshold"/>.</summary>
    private static double Crossing(float inside, float outside, float threshold) =>
        inside > outside ? Math.Clamp((inside - threshold) / (inside - outside), 0, 1) : 0;
}
