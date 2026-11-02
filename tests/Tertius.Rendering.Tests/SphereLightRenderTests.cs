using System.Numerics;
using Tertius.Testing;
using static Tertius.Rendering.Tests.BrdfTesting;

namespace Tertius.Rendering.Tests;

/// <summary>
/// The highlight of a sized light on the GPU (docs/plans/pbr-brdf.md, "Sized lights"): a mirror shows the light's disc
/// at the light's angular size and at one radiance across its inside, and a light of radius 0 shades as the point light
/// did before the widened lobe.
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

    /// <summary>
    /// Adds the black glossy mirror, tilted by <paramref name="tilt"/> rad (<see cref="MirrorRotation"/>), to
    /// <paramref name="snapshot"/>, renders it, and captures the image.
    /// </summary>
    private static Capture RenderMirror(VulkanRenderer renderer, RenderSnapshot snapshot, float tilt, string name)
    {
        MeshId square = renderer.CreateMesh(Primitives.Plane(0.6f));
        MaterialId material = renderer.CreateMaterial(
            new(BaseColor: new(x: 0, y: 0, z: 0, w: 1), Roughness: 0, Reflectance: 0.5f)
        );
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

    [Theory]
    [MemberData(nameof(SizedLights))]
    public void AMirrorShowsTheDiscOfASizedLightAtTheLightsAngularSize(string light)
    {
        bool sun = light == "sun";
        float fov = sun ? 0.03f : 0.25f;
        double lightToMirror = sun ? Gpu.StarDistance : MirrorDistance - LampDistance;
        double imageDistance = sun ? Gpu.StarDistance : MirrorDistance + lightToMirror;
        double lightSine = sun ? Gpu.StarSine : LampRadius / lightToMirror;
        double imageAngularRadius = sun ? Gpu.StarAngularRadius : Math.Asin(LampRadius / imageDistance);
        RenderSnapshot snapshot = SizedLightInTheMirror(sun: sun, fov: fov, tilt: 0);
        Double3 irradiance = sun
            ? Gpu.StarIrradianceRgb(snapshot)
            : new Double3(1, 1, 1) * (LampIntensity / (lightToMirror * lightToMirror));
        Double3 plateau = DiscCenter(lightSine: lightSine, tilt: 0) * InReach(irradiance);

        VulkanRenderer renderer = Gpu.Headless(width: Width, height: Height);
        try
        {
            Capture image = RenderMirror(
                renderer: renderer,
                snapshot: snapshot,
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
            double focalLength = Height / 2.0 / Math.Tan(fov / 2);
            double radius = Math.Atan(HalfMaximumRadius(image, threshold: shown[0].X) / focalLength);
            // Outside the disc, the lobe of the roughness floor falls from the disc's edge as it falls from a point
            // light's peak. Seen from the camera, the turn of the reflected ray scales by the light's distance from the
            // mirror over the distance of its image.
            double alpha = new GgxLobe(0).Alpha;
            double imageScale = lightToMirror / imageDistance;
            // A pixel, and the center's 1 %: it moves the threshold from half the shown peak to between 0.495 and
            // 0.505 of it, where the lobe falls by 0.91 of its peak per α of tan θ_h, which moves the edge by up to
            // 0.011 α of the reflected ray.
            radius
                .Should()
                .BeApproximately(
                    imageAngularRadius + HalfMaximumPerAlpha * alpha * imageScale,
                    fov / Height + 0.011 * alpha * imageScale,
                    "the mirror shows the {0}'s disc of angular radius {1} rad, widened by the floored lobe's half "
                        + "maximum",
                    light,
                    imageAngularRadius
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
        bool sun = light == "sun";
        float fov = sun ? 0.03f : 0.25f;
        double lightToMirror = sun ? Gpu.StarDistance : MirrorDistance - LampDistance;
        double imageDistance = sun ? Gpu.StarDistance : MirrorDistance + lightToMirror;
        double lightSine = sun ? Gpu.StarSine : LampRadius / lightToMirror;
        double imageAngularRadius = sun ? Gpu.StarAngularRadius : Math.Asin(LampRadius / imageDistance);
        RenderSnapshot snapshot = SizedLightInTheMirror(sun: sun, fov: fov, tilt: ObliqueTilt);
        Double3 irradiance = sun
            ? Gpu.StarIrradianceRgb(snapshot)
            : new Double3(1, 1, 1) * (LampIntensity / (lightToMirror * lightToMirror));
        Double3 plateau = DiscCenter(lightSine: lightSine, tilt: ObliqueTilt) * InReach(irradiance);

        VulkanRenderer renderer = Gpu.Headless(width: Width, height: Height);
        try
        {
            Capture image = RenderMirror(
                renderer: renderer,
                snapshot: snapshot,
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
            double focalLength = Height / 2.0 / Math.Tan(fov / 2);
            int inside = (int)(0.6 * Math.Tan(imageAngularRadius) * focalLength);
            var outside = new List<(int X, int Y, float Red)>();
            for (int y = -inside; y <= inside; y++)
            for (int x = -inside; x <= inside; x++)
            {
                if (x * x + y * y > inside * inside)
                    continue;
                float red = image.Color(Width / 2 + x, Height / 2 + y).X;
                if (red < shown[0].X || red > shown[1].X)
                    outside.Add((X: x, Y: y, Red: red));
            }

            outside
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

            ReferenceImages.Verify(renderer: renderer, snapshot: snapshot, name: "sphere-light-radius-zero");
        }
        finally
        {
            Gpu.DisposeAndAssertClean(renderer);
        }
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
