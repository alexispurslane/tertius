using System.Numerics;
using Tertius.Testing;
using static Tertius.Rendering.Tests.BrdfTesting;

namespace Tertius.Rendering.Tests;

/// <summary>
/// The lights in SI units on the GPU (docs/plans/local-light-shadows.md, Part 4): a point light of power P lights a
/// surface d meters away with P / (4π d²), a spot light with the intensity of its cone, a light beyond its reach adds
/// nothing, and a star 1 AU away lights as the old sun did for the same irradiance. Far lights shadow with their own
/// cascade sets while the budget lasts, and with far shadows only after it. The exposure is fixed at 0 stops, where
/// the cutoff irradiance is π · 2^−13 ≈ 3.8 × 10^−4 W/m². The camera is at the origin and looks down −Z. A matte white
/// square of roughness 1 stands 3 m ahead and faces it, with no ambient light, and each expected color comes from the C#
/// BRDF, shown through the same tone curve by an emissive square (<see cref="Displayed"/>).
/// </summary>
[Collection(GpuCollection.Name)]
public sealed class LightUnitsRenderTests
{
    private const int Width = 640,
        Height = 360;

    /// <summary>The largest difference of a channel, in 8-bit steps, between a lit pixel and its expected color.</summary>
    private const float Tolerance = 2f / 255;

    private static readonly Double3 Center = new(0, 0, -3);

    private static readonly MaterialSample White = new(
        BaseColor: new(1, 1, 1),
        Metallic: 0,
        Roughness: 1,
        Reflectance: 0.5
    );

    private static readonly double Cutoff = LightUnits.CutoffIrradiance(0);

    private static VulkanRenderer Renderer(int cascadedLights = 1) =>
        Gpu.Headless(width: Width, height: Height, cascadedLights: cascadedLights);

    /// <summary>A snapshot with no light at all: the test star has no power, and the ambient light is off.</summary>
    private static RenderSnapshot Dark()
    {
        RenderSnapshot snapshot = Gpu.Snapshot(Vector3.UnitY, starIrradiance: 0);
        snapshot.Ambient.Intensity = 0;
        return snapshot;
    }

    /// <summary>The matte white square of 1 m that faces the camera 3 m ahead.</summary>
    private static RenderInstance Square(VulkanRenderer renderer, float side = 1) =>
        Gpu.Instance(
            mesh: renderer.CreateMesh(Primitives.Plane(side / 2)),
            material: renderer.CreateMaterial(new(BaseColor: Vector4.One, Roughness: 1, Reflectance: 0.5f)),
            position: Center.ToVector3(),
            scale: Vector3.One,
            rotation: Quaternion.CreateFromAxisAngle(Vector3.UnitX, MathF.PI / 2),
            radius: 0.75f * side,
            chainDepth: TestChains.CamerasFrame
        );

    /// <summary>A caster of <paramref name="mesh"/> at <paramref name="position"/> that only casts shadows: it draws nothing.</summary>
    private static RenderInstance ShadowOnly(MeshId mesh, Vector3 position, Vector3 scale) =>
        RenderInstance.Create(
            mesh: mesh,
            material: new(0),
            position: position,
            rotation: Quaternion.Identity,
            scale: scale,
            boundingRadius: 0,
            hemisphere: HemisphereUp.Even,
            chainDepth: TestChains.CamerasFrame,
            flags: RenderFlags.CastsShadow,
            entityIndex: 0,
            part: 0
        );

    /// <summary>The pixel that sees <paramref name="onSquare"/>, and the point of the square that the pixel's center sees.</summary>
    private static (Vector2 Pixel, Double3 Point) Seen(Double3 onSquare)
    {
        Vector2 pixel = Holding(Gpu.Project(point: onSquare.ToVector3(), width: Width, height: Height));
        return (pixel, OnPlane(Ray(pixel, Width, Height), Center, Double3.UnitZ));
    }

    /// <summary>
    /// The light that the square sends to the eye from <paramref name="point"/> under the irradiance
    /// <paramref name="irradiance"/> from <paramref name="toLight"/>, of a light whose angular radius has the sine
    /// <paramref name="lightSine"/> there: 0 for a lamp of radius 0.
    /// </summary>
    private static Vector3 Lit(Double3 point, Double3 toLight, Double3 irradiance, double lightSine) =>
        (
            White.Reflected(
                normal: Double3.UnitZ,
                toView: -point.Normalized(),
                toLight: toLight.Normalized(),
                tables: BrdfTables.Shared,
                lightSine: lightSine
            ) * irradiance
        ).ToVector3();

    /// <summary>The same irradiance, in W/m², in every channel.</summary>
    private static Double3 Gray(double irradiance) => new(irradiance, irradiance, irradiance);

    /// <summary>The irradiance of a lamp of radiant intensity <paramref name="intensity"/> W/sr at a distance, windowed at its reach at 0 stops.</summary>
    private static double LampIrradiance(double intensity, double distance)
    {
        double reach = LightUnits.Reach(intensity, Cutoff);
        double ratio = distance * distance / (reach * reach);
        double window = Math.Clamp(1 - ratio * ratio, 0, 1);
        return intensity * window * window / (distance * distance);
    }

    /// <summary>
    /// Renders <paramref name="snapshot"/> and compares the pixel that sees <paramref name="pixel"/> with the color that
    /// <paramref name="expected"/> shows. Returns the stats of the frame of <paramref name="snapshot"/>.
    /// </summary>
    private static RenderStats ShouldShow(
        VulkanRenderer renderer,
        RenderSnapshot snapshot,
        Vector2 pixel,
        Vector3 expected,
        string name
    )
    {
        Vector3 shown = Gpu.RenderAndCapture(renderer: renderer, snapshot: snapshot, name: name).Color(pixel);
        RenderStats stats = renderer.LastFrameStats;
        Vector3 reference = Displayed(renderer, [expected], name + "-expected")[0];
        LargestChannelDifference(shown, reference)
            .Should()
            .BeLessThanOrEqualTo(Tolerance, "the square shows {0}, the light {1}, as {2}", shown, expected, reference);
        return stats;
    }

    /// <summary>The HDR image's pixel that sees the center of the square.</summary>
    private static Vector3 HdrAtTheCenter(VulkanRenderer renderer, RenderSnapshot snapshot)
    {
        renderer.Render(snapshot, 1f);
        float[] image = renderer.ReadHdr();
        Vector2 pixel = Seen(Center).Pixel;
        int i = ((int)pixel.Y * Width + (int)pixel.X) * 4;
        return new(image[i], image[i + 1], image[i + 2]);
    }

    [Fact]
    public void APointLightLightsASquareByItsPowerOverFourPiDSquared()
    {
        VulkanRenderer renderer = Renderer();
        try
        {
            var lamp = new Double3(0, 0, -2);
            const float power = 6;
            RenderSnapshot snapshot = Dark();
            snapshot.Instances.Add(Square(renderer));
            snapshot.Lights.Add(
                Gpu.Lamp(
                    position: lamp.ToVector3(),
                    color: Vector3.One,
                    intensity: 0,
                    chainDepth: TestChains.CamerasFrame
                ) with
                {
                    Power = power,
                }
            );
            (Vector2 pixel, Double3 point) = Seen(Center);
            double irradiance = LampIrradiance(power / (4 * Math.PI), (lamp - point).Length);

            _ = ShouldShow(
                renderer: renderer,
                snapshot: snapshot,
                pixel: pixel,
                expected: Lit(point: point, toLight: lamp - point, irradiance: Gray(irradiance), lightSine: 0),
                name: "light-units-point"
            );
        }
        finally
        {
            Gpu.DisposeAndAssertClean(renderer);
        }
    }

    [Fact]
    public void ASpotLightLightsTheSquareOnItsAxisByTheIntensityOfItsCone()
    {
        VulkanRenderer renderer = Renderer();
        try
        {
            var lamp = new Double3(0, 0, -2);
            const float power = 0.5f,
                outer = 0.5f;
            RenderSnapshot snapshot = Dark();
            snapshot.Instances.Add(Square(renderer));
            snapshot.Lights.Add(
                Gpu.Spot(
                    position: lamp.ToVector3(),
                    direction: -Vector3.UnitZ,
                    color: Vector3.One,
                    intensity: 0,
                    innerConeAngle: 0.3f,
                    outerConeAngle: outer,
                    chainDepth: TestChains.CamerasFrame
                ) with
                {
                    Power = power,
                }
            );
            (Vector2 pixel, Double3 point) = Seen(Center);
            // The cone of half-angle θ holds the solid angle 2π (1 − cos θ).
            double intensity = power / (2 * Math.PI * (1 - Math.Cos(outer)));

            _ = ShouldShow(
                renderer: renderer,
                snapshot: snapshot,
                pixel: pixel,
                expected: Lit(
                    point: point,
                    toLight: lamp - point,
                    irradiance: Gray(LampIrradiance(intensity, (lamp - point).Length)),
                    lightSine: 0
                ),
                name: "light-units-spot"
            );
        }
        finally
        {
            Gpu.DisposeAndAssertClean(renderer);
        }
    }

    [Fact]
    public void ALightBeyondItsReachAddsNothing()
    {
        VulkanRenderer renderer = Renderer();
        try
        {
            // Its reach is 1.5 m: it lights the square from 1 m away and adds nothing from 2 m away.
            float intensity = (float)(1.5 * 1.5 * Cutoff);
            RenderSnapshot near = Dark(),
                far = Dark();
            near.Instances.Add(Square(renderer));
            far.Instances.Add(Square(renderer));
            near.Lights.Add(
                Gpu.Lamp(
                    position: new(0, 0, -2),
                    color: Vector3.One,
                    intensity: intensity,
                    chainDepth: TestChains.CamerasFrame
                )
            );
            far.Lights.Add(
                Gpu.Lamp(
                    position: new(0, 0, -1),
                    color: Vector3.One,
                    intensity: intensity,
                    chainDepth: TestChains.CamerasFrame
                )
            );

            HdrAtTheCenter(renderer, near).X.Should().BeGreaterThan(0, "the square lies within the lamp's reach");
            HdrAtTheCenter(renderer, far).Should().Be(Vector3.Zero, "the square lies beyond the lamp's reach");
        }
        finally
        {
            Gpu.DisposeAndAssertClean(renderer);
        }
    }

    [Fact]
    public void WithinItsReachALampFadesByTheSquareOfItsWindow()
    {
        VulkanRenderer renderer = Renderer();
        try
        {
            // Its reach is 1 / 0.7 m, so the square 1 m away lies at 0.7 of it, where the window (1 − 0.7⁴)² ≈ 0.577 is
            // far from 1: a window without its outer square gives 0.760, and one of the ratio, not its square, 0.260.
            var lamp = new Double3(0, 0, -2);
            float intensity = (float)(Cutoff / (0.7 * 0.7));
            RenderSnapshot snapshot = Dark();
            snapshot.Instances.Add(Square(renderer));
            snapshot.Lights.Add(
                Gpu.Lamp(
                    position: lamp.ToVector3(),
                    color: Vector3.One,
                    intensity: intensity,
                    chainDepth: TestChains.CamerasFrame
                )
            );
            Double3 point = Seen(Center).Point;
            Vector3 expected = Lit(
                point: point,
                toLight: lamp - point,
                irradiance: Gray(LampIrradiance(intensity, (lamp - point).Length)),
                lightSine: 0
            );

            // The exposure is 0 stops, so the HDR image holds the radiance itself.
            Vector3 shown = HdrAtTheCenter(renderer, snapshot);

            shown.X.Should().BeApproximately(expected.X, 0.01f * expected.X, "the lamp's light fades by its window");
        }
        finally
        {
            Gpu.DisposeAndAssertClean(renderer);
        }
    }

    [Fact]
    public void AFarStarLightsAsTheSunDidForTheSameIrradiance()
    {
        // The old sun was a direction and an irradiance. A star 1.5 × 10¹¹ m away sends that irradiance from all but the
        // same direction to every point of the square.
        VulkanRenderer renderer = Renderer();
        try
        {
            var toStar = new Double3(0.2, 0.3, 1);
            RenderSnapshot snapshot = Gpu.Snapshot(toStar.ToVector3(), starIrradiance: 2);
            snapshot.Ambient.Intensity = 0;
            snapshot.Instances.Add(Square(renderer));
            (Vector2 pixel, Double3 point) = Seen(Center);

            _ = ShouldShow(
                renderer: renderer,
                snapshot: snapshot,
                pixel: pixel,
                expected: Lit(
                    point: point,
                    toLight: toStar,
                    irradiance: Gpu.StarIrradianceRgb(snapshot),
                    lightSine: Gpu.StarSine
                ),
                name: "light-units-star"
            );
        }
        finally
        {
            Gpu.DisposeAndAssertClean(renderer);
        }
    }

    [Fact]
    public void ASecondStarCastsFarShadowsOnly()
    {
        // One cascade set, which the brighter star behind the camera takes. The other, up and to the right, shadows only by
        // the far shadows, the analytic spheres of the casters: an invisible ball of 0.5 m, 1.5 m from the square's center
        // toward it, hides it from there.
        VulkanRenderer renderer = Renderer();
        try
        {
            var toFirst = new Double3(0, 0, 1);
            Double3 toSecond = new Double3(1, 1, 1).Normalized();
            RenderSnapshot snapshot = Dark();
            snapshot.Lights[0] = Gpu.StarLight(
                direction: toFirst.ToVector3(),
                irradiance: 1,
                angularRadius: Gpu.StarAngularRadius,
                color: Vector3.One,
                chainDepth: TestChains.Root
            );
            snapshot.Lights.Add(
                Gpu.StarLight(
                    direction: toSecond.ToVector3(),
                    irradiance: 0.5f,
                    angularRadius: Gpu.StarAngularRadius,
                    color: Vector3.One,
                    chainDepth: TestChains.Root
                )
            );
            snapshot.Instances.Add(Square(renderer));
            snapshot.Instances.Add(
                ShadowOnly(
                    mesh: renderer.CreateMesh(Primitives.Sphere(1f)),
                    position: (Center + toSecond * 1.5).ToVector3(),
                    scale: new(0.5f)
                )
            );
            (Vector2 pixel, Double3 point) = Seen(Center);

            RenderStats stats = ShouldShow(
                renderer: renderer,
                snapshot: snapshot,
                pixel: pixel,
                expected: Lit(point: point, toLight: toFirst, irradiance: Gray(1), lightSine: Gpu.StarSine),
                name: "light-units-far-shadows-only"
            );
            stats.CascadeSets.Should().Be(1);
        }
        finally
        {
            Gpu.DisposeAndAssertClean(renderer);
        }
    }

    [Fact]
    public void TwoCascadedLightsEachShadowWithTheirOwnMaps()
    {
        // An invisible box of 1 m, 1 m in front of a square of 4 m, between two stars that each have a cascade set: the
        // star to the right throws its shadow 0.8 m to the left of the center, and the star to the left 0.8 m to the
        // right. In the shadow of each star, the other alone lights the square. The points sit 0.5 m inside the edges of
        // the shadows, past the soft edge that the filter of the nearest cascade gives them, about 0.25 m wide here.
        VulkanRenderer renderer = Renderer(cascadedLights: 2);
        try
        {
            Double3 toRight = new Double3(0.8, 0, 1).Normalized();
            Double3 toLeft = new Double3(-0.8, 0, 1).Normalized();
            RenderSnapshot snapshot = Dark();
            snapshot.Lights[0] = Gpu.StarLight(
                direction: toRight.ToVector3(),
                irradiance: 1,
                angularRadius: Gpu.StarAngularRadius,
                color: Vector3.One,
                chainDepth: TestChains.Root
            );
            snapshot.Lights.Add(
                Gpu.StarLight(
                    direction: toLeft.ToVector3(),
                    irradiance: 0.6f,
                    angularRadius: Gpu.StarAngularRadius,
                    color: Vector3.One,
                    chainDepth: TestChains.Root
                )
            );
            snapshot.Instances.Add(Square(renderer, side: 4));
            snapshot.Instances.Add(
                ShadowOnly(
                    mesh: renderer.CreateMesh(Primitives.Box(new(0.5f))),
                    position: new(0, 0, -2f),
                    scale: Vector3.One
                )
            );
            (Vector2 leftPixel, Double3 left) = Seen(new(-0.8, 0, -3));
            (Vector2 rightPixel, Double3 right) = Seen(new(0.8, 0, -3));

            Capture image = Gpu.RenderAndCapture(renderer: renderer, snapshot: snapshot, name: "light-units-two-sets");
            renderer.LastFrameStats.CascadeSets.Should().Be(2);
            Vector3[] expected = Displayed(
                renderer,
                [
                    Lit(point: left, toLight: toLeft, irradiance: Gray(0.6), lightSine: Gpu.StarSine),
                    Lit(point: right, toLight: toRight, irradiance: Gray(1), lightSine: Gpu.StarSine),
                ],
                "light-units-two-sets-expected"
            );

            LargestChannelDifference(image.Color(leftPixel), expected[0])
                .Should()
                .BeLessThanOrEqualTo(Tolerance, "the right star's own map shadows the left point");
            LargestChannelDifference(image.Color(rightPixel), expected[1])
                .Should()
                .BeLessThanOrEqualTo(Tolerance, "the left star's own map shadows the right point");
        }
        finally
        {
            Gpu.DisposeAndAssertClean(renderer);
        }
    }

    [Fact]
    public void ADimStarCastsNoShadow()
    {
        // A star of 1,000 W/m² lights the back of the square, and a star 2,000 times dimmer, below 1/256 of it, lights
        // the front. The dim star gets no shadow slot, so the ball between it and the square's center hides nothing.
        VulkanRenderer renderer = Renderer(cascadedLights: 2);
        try
        {
            Double3 toDim = new Double3(1, 1, 1).Normalized();
            RenderSnapshot snapshot = Dark();
            snapshot.Lights[0] = Gpu.StarLight(
                direction: -Vector3.UnitZ,
                irradiance: 1_000,
                angularRadius: Gpu.StarAngularRadius,
                color: Vector3.One,
                chainDepth: TestChains.Root
            );
            snapshot.Lights.Add(
                Gpu.StarLight(
                    direction: toDim.ToVector3(),
                    irradiance: 0.5f,
                    angularRadius: Gpu.StarAngularRadius,
                    color: Vector3.One,
                    chainDepth: TestChains.Root
                )
            );
            snapshot.Instances.Add(Square(renderer));
            snapshot.Instances.Add(
                ShadowOnly(
                    mesh: renderer.CreateMesh(Primitives.Sphere(1f)),
                    position: (Center + toDim * 1.5).ToVector3(),
                    scale: new(0.5f)
                )
            );
            (Vector2 pixel, Double3 point) = Seen(Center);

            RenderStats stats = ShouldShow(
                renderer: renderer,
                snapshot: snapshot,
                pixel: pixel,
                expected: Lit(point: point, toLight: toDim, irradiance: Gray(0.5), lightSine: Gpu.StarSine),
                name: "light-units-dim-star"
            );
            stats.CascadeSets.Should().Be(1, "only the bright star has a cascade set, though the budget is two");
        }
        finally
        {
            Gpu.DisposeAndAssertClean(renderer);
        }
    }

    [Fact]
    public void ALampBehindTheGeometricNormalAddsNothing()
    {
        VulkanRenderer renderer = Renderer();
        try
        {
            // The normal map's texel (128, 128, 0) turns the shading normal around, so that it faces the lamp half a
            // meter behind the square. The square itself, its geometric normal, faces the camera, and away from the lamp.
            TextureId flipped = renderer.CreateTexture(new(Width: 1, Height: 1, Rgba: [128, 128, 0, 255], Srgb: false));
            RenderSnapshot snapshot = Dark();
            snapshot.Instances.Add(
                Gpu.Instance(
                    mesh: renderer.CreateMesh(Primitives.Plane(0.5f)),
                    material: renderer.CreateMaterial(
                        new(BaseColor: Vector4.One, Roughness: 1, Reflectance: 0.5f, NormalMap: flipped)
                    ),
                    position: Center.ToVector3(),
                    scale: Vector3.One,
                    rotation: Quaternion.CreateFromAxisAngle(Vector3.UnitX, MathF.PI / 2),
                    radius: 0.75f,
                    chainDepth: TestChains.CamerasFrame
                )
            );
            snapshot.Lights.Add(
                Gpu.Lamp(
                    position: new(0, 0, -3.5f),
                    color: Vector3.One,
                    intensity: 10,
                    chainDepth: TestChains.CamerasFrame
                )
            );

            HdrAtTheCenter(renderer, snapshot).Should().Be(Vector3.Zero);
        }
        finally
        {
            Gpu.DisposeAndAssertClean(renderer);
        }
    }
}
