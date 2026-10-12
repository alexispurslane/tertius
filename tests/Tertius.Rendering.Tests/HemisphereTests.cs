using System.Numerics;
using Tertius.Rendering;

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
    private const int Width = 320, Height = 180;
    private static readonly Vector3 Center = new(0, 0, -4);

    private static Vector2 Toward(Vector3 side) => Gpu.Project(Center + 0.8f * side, Width, Height);

    /// <summary>Renders the sphere and returns its color at the right, the left and the top.</summary>
    private static (Vector3 Right, Vector3 Left, Vector3 Top) Render(HemisphereUp up, string name, RenderFlags flags = RenderFlags.None, bool impostors = false)
    {
        VulkanRenderer renderer = Gpu.Headless(Width, Height, impostors: impostors);
        try
        {
            MeshId sphere = renderer.CreateMesh(Primitives.Sphere(1f, 64, 32));
            MaterialId white = renderer.CreateMaterial(new MaterialDesc(Vector4.One, SpecularIntensity: 0));
            RenderSnapshot snapshot = Gpu.Snapshot(Vector3.UnitY, sunIntensity: 0);
            snapshot.Ambient = new AmbientLight { SkyColor = new Vector3(0.6f, 0, 0), GroundColor = new Vector3(0, 0, 0.6f), Intensity = 1 };
            snapshot.Instances.Add(RenderInstance.Create(sphere, white, Center, Quaternion.Identity, Vector3.One, 1f, up,
                RenderFlags.Visible | flags, entityIndex: 1));
            Capture image = Gpu.RenderAndCapture(renderer, snapshot, name);
            if (impostors)
                renderer.LastFrameStats.Impostors.Should().Be(1, "the sphere is 78 px tall, below 200");
            return (image.Color(Toward(Vector3.UnitX)), image.Color(Toward(-Vector3.UnitX)), image.Color(Toward(Vector3.UnitY)));
        }
        finally
        {
            Gpu.DisposeAndAssertClean(renderer);
        }
    }

    private static void ExpectSkyward(Vector3 color, string side) =>
        (color.X > color.Z + 0.15f).Should().BeTrue($"the {side} faces up, so the red sky lights it, not {color}");

    private static void ExpectGroundward(Vector3 color, string side) =>
        (color.Z > color.X + 0.15f).Should().BeTrue($"the {side} faces down, so the blue ground lights it, not {color}");

    private static void ExpectEven(Vector3 color, string side) =>
        MathF.Abs(color.X - color.Z).Should().BeLessThan(0.03f, $"the {side} faces sideways, so sky and ground mix evenly, not {color}");

    [Fact]
    public void WithUpAlongTheCameraFramesXTheRightFacesTheSkyAndTheTopFacesSideways()
    {
        (Vector3 right, Vector3 left, Vector3 top) = Render(HemisphereUp.Along(Vector3.UnitX), "hemisphere-along-x");

        ExpectSkyward(right, "right");
        ExpectGroundward(left, "left");
        ExpectEven(top, "top");
    }

    [Fact]
    public void WithoutGravityTheSkyAndTheGroundMixEvenlyOnEverySide()
    {
        (Vector3 right, Vector3 left, Vector3 top) = Render(HemisphereUp.Even, "hemisphere-even");

        ExpectEven(right, "right");
        ExpectEven(left, "left");
        ExpectEven(top, "top");
    }

    [Fact]
    public void AroundAPointUpLeadsAwayFromItSoAPlanetSeesTheSkyOnEverySide()
    {
        (Vector3 right, Vector3 left, Vector3 top) = Render(HemisphereUp.AwayFrom(Vector3.Zero), "hemisphere-away");

        ExpectSkyward(right, "right");
        ExpectSkyward(left, "left");
        ExpectSkyward(top, "top");
    }

    [Fact]
    public void FarAboveThePointUpIsTheSameDirectionAcrossTheWholeObject()
    {
        // 100 m above the point, up leans at most 0.6 degrees from +Y across the sphere, as on a planet's ground.
        (Vector3 right, Vector3 left, Vector3 top) = Render(HemisphereUp.AwayFrom(new Vector3(0, -100, 0)), "hemisphere-away-far");

        ExpectEven(right, "right");
        ExpectEven(left, "left");
        ExpectSkyward(top, "top");
    }

    [Fact]
    public void ATransparentObjectTakesItsUpFromItsInstanceToo()
    {
        (Vector3 right, Vector3 left, Vector3 top) = Render(HemisphereUp.Along(Vector3.UnitX), "hemisphere-transparent", RenderFlags.Transparent);

        ExpectSkyward(right, "right");
        ExpectGroundward(left, "left");
        ExpectEven(top, "top");
    }

    [Fact]
    public void AnImpostorBakesItsInstancesUpIntoItsCapture()
    {
        (Vector3 right, Vector3 left, Vector3 top) = Render(HemisphereUp.Along(Vector3.UnitX), "hemisphere-impostor", impostors: true);

        ExpectSkyward(right, "right");
        ExpectGroundward(left, "left");
        ExpectEven(top, "top");
    }
}
