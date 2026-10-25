using System.Numerics;

namespace Tertius.Rendering.Tests;

/// <summary>
/// The hemisphere ambient light on the GPU: each instance says which way is up, and the sky color lights what faces up,
/// the ground color what faces down. A white sphere of radius 1 m, 4 m ahead, is lit by the ambient light alone: a red
/// sky and a blue ground. Three pixels show it at the projections of the points 0.8 m from its center toward the right
/// (+X), the left (-X) and the top (+Y). There the normal leans about 40 degrees from the view axis toward that side.
/// </summary>
[Collection(GpuCollection.Name)]
public sealed class HemisphereTests
{
    private const int Width = 320,
        Height = 180;
    private static readonly Vector3 Center = new(0, 0, -4);

    private static Vector2 Toward(Vector3 side) =>
        Gpu.Project(point: Center + 0.8f * side, width: Width, height: Height);

    /// <summary>Renders the sphere and returns its color at the right, the left and the top.</summary>
    private static (Vector3 Right, Vector3 Left, Vector3 Top) Render(
        HemisphereUp up,
        string name,
        RenderFlags flags = RenderFlags.None,
        bool impostors = false
    )
    {
        Vector3[] colors = Render(
            up: up,
            name: name,
            // A reflectance of 0: the lobe's share of the ambient light is small at these view angles.
            material: new(BaseColor: Vector4.One, Roughness: 0.38f, Reflectance: 0),
            pixels: [Toward(Vector3.UnitX), Toward(-Vector3.UnitX), Toward(Vector3.UnitY)],
            flags: flags,
            impostors: impostors
        );
        return (colors[0], colors[1], colors[2]);
    }

    /// <summary>Renders the sphere with <paramref name="material"/> and returns its color at each of <paramref name="pixels"/>.</summary>
    private static Vector3[] Render(
        HemisphereUp up,
        string name,
        MaterialDesc material,
        Vector2[] pixels,
        RenderFlags flags,
        bool impostors
    )
    {
        VulkanRenderer renderer = Gpu.Headless(width: Width, height: Height, impostors: impostors);
        try
        {
            MeshId sphere = renderer.CreateMesh(Primitives.Sphere(1f, 64, 32));
            MaterialId surface = renderer.CreateMaterial(material);
            RenderSnapshot snapshot = Gpu.Snapshot(Vector3.UnitY, sunIntensity: 0);
            snapshot.Ambient = new()
            {
                SkyColor = new(0.6f, 0, 0),
                GroundColor = new(0, 0, 0.6f),
                Intensity = 1,
            };
            snapshot.Instances.Add(
                RenderInstance.Create(
                    mesh: sphere,
                    material: surface,
                    position: Center,
                    rotation: Quaternion.Identity,
                    scale: Vector3.One,
                    boundingRadius: 1f,
                    hemisphere: up,
                    flags: RenderFlags.Visible | flags,
                    entityIndex: 1
                )
            );
            Capture image = Gpu.RenderAndCapture(renderer: renderer, snapshot: snapshot, name: name);
            if (impostors)
                renderer.LastFrameStats.Impostors.Should().Be(1, "the sphere is 78 px tall, below 200");
            return [.. pixels.Select(image.Color)];
        }
        finally
        {
            Gpu.DisposeAndAssertClean(renderer);
        }
    }

    private static void ExpectSkyward(Vector3 color, string side) =>
        (color.X > color.Z + 0.15f).Should().BeTrue($"the {side} faces up, so the red sky lights it, not {color}");

    private static void ExpectGroundward(Vector3 color, string side) =>
        (color.Z > color.X + 0.15f)
            .Should()
            .BeTrue($"the {side} faces down, so the blue ground lights it, not {color}");

    private static void ExpectEven(Vector3 color, string side) =>
        MathF
            .Abs(color.X - color.Z)
            .Should()
            .BeLessThan(0.03f, $"the {side} faces sideways, so sky and ground mix evenly, not {color}");

    [Fact]
    public void WithUpAlongTheCameraFramesXTheRightFacesTheSkyAndTheTopFacesSideways()
    {
        (Vector3 right, Vector3 left, Vector3 top) = Render(
            up: HemisphereUp.Along(Vector3.UnitX),
            name: "hemisphere-along-x"
        );

        ExpectSkyward(right, "right");
        ExpectGroundward(left, "left");
        ExpectEven(top, "top");
    }

    [Fact]
    public void WithoutGravityTheSkyAndTheGroundMixEvenlyOnEverySide()
    {
        (Vector3 right, Vector3 left, Vector3 top) = Render(up: HemisphereUp.Even, name: "hemisphere-even");

        ExpectEven(right, "right");
        ExpectEven(left, "left");
        ExpectEven(top, "top");
    }

    [Fact]
    public void AroundAPointUpLeadsAwayFromItSoAPlanetSeesTheSkyOnEverySide()
    {
        (Vector3 right, Vector3 left, Vector3 top) = Render(
            up: HemisphereUp.AwayFrom(Vector3.Zero),
            name: "hemisphere-away"
        );

        ExpectSkyward(right, "right");
        ExpectSkyward(left, "left");
        ExpectSkyward(top, "top");
    }

    [Fact]
    public void FarAboveThePointUpIsTheSameDirectionAcrossTheWholeObject()
    {
        // 100 m above the point, up leans at most 0.6 degrees from +Y across the sphere, as on a planet's ground.
        (Vector3 right, Vector3 left, Vector3 top) = Render(
            up: HemisphereUp.AwayFrom(new(0, -100, 0)),
            name: "hemisphere-away-far"
        );

        ExpectEven(right, "right");
        ExpectEven(left, "left");
        ExpectSkyward(top, "top");
    }

    [Fact]
    public void ATransparentObjectTakesItsUpFromItsInstanceToo()
    {
        (Vector3 right, Vector3 left, Vector3 top) = Render(
            up: HemisphereUp.Along(Vector3.UnitX),
            name: "hemisphere-transparent",
            flags: RenderFlags.Transparent
        );

        ExpectSkyward(right, "right");
        ExpectGroundward(left, "left");
        ExpectEven(top, "top");
    }

    [Fact]
    public void AnImpostorBakesItsInstancesUpIntoItsCapture()
    {
        (Vector3 right, Vector3 left, Vector3 top) = Render(
            up: HemisphereUp.Along(Vector3.UnitX),
            name: "hemisphere-impostor",
            impostors: true
        );

        ExpectSkyward(right, "right");
        ExpectGroundward(left, "left");
        ExpectEven(top, "top");
    }

    /// <summary>
    /// The pixel of the point on the sphere's front, in its vertical plane, whose lobe's dominant direction for
    /// <paramref name="roughness"/> rises <paramref name="elevation"/> degrees above the horizon, found by bisection.
    /// </summary>
    private static Vector2 WhereTheLobeRises(double elevation, double roughness)
    {
        var lobe = new GgxLobe(roughness);
        Double3 center = Center;
        double RiseOf(double normalElevation)
        {
            var normal = new Double3(0, Math.Sin(normalElevation), Math.Cos(normalElevation));
            Double3 toView = -(center + normal).Normalized();
            return Math.Asin(lobe.DominantDirection(normal, BrdfTesting.Mirror(normal, toView)).Y);
        }

        double target = elevation * Math.PI / 180;
        (double low, double high) = (-Math.PI / 3, Math.PI / 3);
        for (int i = 0; i < 60; i++)
        {
            double middle = (low + high) / 2;
            if (RiseOf(middle) < target)
                low = middle;
            else
                high = middle;
        }

        Double3 point = new Double3(0, Math.Sin(low), Math.Cos(low)) + center;
        return Gpu.Project(point: point.ToVector3(), width: Width, height: Height);
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    public void AShinyMetalReflectsTheSkyAboveItsHorizonAndTheGroundBelow(bool transparent, bool impostor)
    {
        const double roughness = 0.1;
        Vector3[] colors = Render(
            up: HemisphereUp.Along(Vector3.UnitY),
            name: $"hemisphere-metal-transparent-{transparent}-impostor-{impostor}",
            material: new(BaseColor: Vector4.One, Roughness: (float)roughness, Metallic: 1),
            pixels: [WhereTheLobeRises(10, roughness), WhereTheLobeRises(-10, roughness)],
            flags: transparent ? RenderFlags.Transparent : RenderFlags.None,
            impostors: impostor
        );

        ExpectSkyward(colors[0], "point whose lobe rises 10° above the horizon");
        ExpectGroundward(colors[1], "point whose lobe sinks 10° below the horizon");
    }

    [Fact]
    public void WithoutGravityAShinyMetalsLobeMixesTheSkyAndTheGroundEvenly()
    {
        const double roughness = 0.1;
        Vector3[] colors = Render(
            up: HemisphereUp.Even,
            name: "hemisphere-metal-even",
            material: new(BaseColor: Vector4.One, Roughness: (float)roughness, Metallic: 1),
            pixels: [WhereTheLobeRises(10, roughness), WhereTheLobeRises(-10, roughness)],
            flags: RenderFlags.None,
            impostors: false
        );

        ExpectEven(colors[0], "point whose lobe rises 10° above the horizon");
        ExpectEven(colors[1], "point whose lobe sinks 10° below the horizon");
    }

    [Fact]
    public void AroundAPointAShinyMetalsLobeSeesTheSkyOnBothSidesOfTheHorizontal()
    {
        // Up leads away from the sphere's center, so every lobe that leaves the surface points into the sky.
        const double roughness = 0.1;
        Vector3[] colors = Render(
            up: HemisphereUp.AwayFrom(Vector3.Zero),
            name: "hemisphere-metal-away",
            material: new(BaseColor: Vector4.One, Roughness: (float)roughness, Metallic: 1),
            pixels: [WhereTheLobeRises(10, roughness), WhereTheLobeRises(-10, roughness)],
            flags: RenderFlags.None,
            impostors: false
        );

        ExpectSkyward(colors[0], "point whose lobe rises 10° above the horizon");
        ExpectSkyward(colors[1], "point whose lobe sinks 10° below the horizon");
    }
}
