using System.Numerics;
using Tertius.Testing;

namespace Tertius.Rendering.Tests;

/// <summary>
/// The shader's walk reads a cell's slab as the depth pyramid stores it: from the stored front w to the stored back w,
/// with no thickness of its own (docs/plans/back-face-depth-layer.md, "The walk"). It compares the slab with the ray's
/// inverse depth as the walk keeps it: computed from s after a crossing, and the solved bound after a descent
/// (ter-186q, t53-eyrv). Rays run across a wall, a sheet or a far box that faces the eye and fills the screen.
/// </summary>
[Collection(GpuCollection.Name)]
public sealed class ScreenWalkSlabTests
{
    private const int Width = 64,
        Height = 36;

    /// <summary>The probe's pixel in the middle of the screen, whose rays cross four wall pixels to its right.</summary>
    private const int Middle = 4 * (Height / 2 * Width + Width / 2);

    [Fact]
    public void ARayInsideTheStoredSlabOfAWallHitsItAtTheShareOfTheStoredSlabWhereItEnters()
    {
        float[] values = WallValues();

        values[Middle].Should().Be(1, "the ray runs inside the wall's slab: a hit");
        values[Middle + 1].Should().BeApproximately(0.5f, 1e-3f, "the ray runs halfway from the front w to the back w");
    }

    [Fact]
    public void ARayJustBehindTheStoredBackWOfAWallMissesIt() =>
        WallValues()[Middle + 2]
            .Should()
            .Be(2, "the stored back w ends the wall's slab, with no thickness added by the walk");

    [Fact]
    public void ARayThatPassesBehindAWallInsideItsOwnPixelMissesTheWallsNextPixel() =>
        WallValues()[Middle + 3]
            .Should()
            .Be(2, "the walk starts where the ray leaves its own pixel, already behind the wall's back w");

    [Fact]
    public void EveryRayThatDescendsOntoASheetThatFacesTheEyeHitsIt()
    {
        // The sheet's slab has no depth. Each ray descends onto it at the entry that the coarse levels solved for the
        // sheet's inverse depth, and a recomputation of the ray's inverse depth there can round behind the sheet.
        const int sheetWidth = 128,
            sheetHeight = 72,
            margin = 16;
        float[] outcomes = ProbeValues(
            probe: "screen_walk_sheet_probe.comp",
            width: sheetWidth,
            height: sheetHeight,
            instance: renderer =>
                Gpu.Instance(
                    mesh: renderer.CreateMesh(Primitives.Plane(1)),
                    material: renderer.CreateMaterial(MaterialDesc.Color(Vector4.One, roughness: 0.3f)),
                    position: new(0, 0, -20),
                    scale: new(60, 1, 40),
                    rotation: Quaternion.CreateFromAxisAngle(Vector3.UnitX, MathF.PI / 2),
                    chainDepth: TestChains.CamerasFrame
                )
        );

        var misses = new List<string>();
        for (int y = margin; y < sheetHeight - margin; y++)
        for (int x = margin; x < sheetWidth - margin; x++)
        for (int ray = 0; ray < 4; ray++)
            if (outcomes[4 * (y * sheetWidth + x) + ray] != 1)
                misses.Add($"pixel ({x}, {y}) ray {ray}: {outcomes[4 * (y * sheetWidth + x) + ray]}");

        misses
            .Should()
            .BeEmpty(
                "each ray reaches the sheet's inverse depth 12 to 13 pixels from its start, on the sheet ({0} of {1} missed)",
                misses.Count,
                4 * (sheetWidth - 2 * margin) * (sheetHeight - 2 * margin)
            );
    }

    [Fact]
    public void EveryRayTowardAVanishingPointThatAFarBoxCoversHitsTheBoxThereThroughTheFrontOfItsSlab()
    {
        // The box's slab is about 9e-9 in w, thinner than the rounding of the ray's inverse depth near its vanishing
        // point, about 3e-8 for rays that start 2 m away. Only the bound that a descent solved tells the ray's w there.
        float[] values = ProbeValues(
            probe: "screen_walk_planet_probe.comp",
            width: Width,
            height: Height,
            instance: renderer =>
            {
                var size = new Vector3(4e7f, 3e7f, 1e6f);
                return Gpu.Instance(
                    mesh: renderer.CreateMesh(Primitives.Box(new(0.5f))),
                    material: renderer.CreateMaterial(MaterialDesc.Color(Vector4.One, roughness: 0.5f)),
                    position: new(0, 0, -1e7f - size.Z / 2),
                    scale: size,
                    radius: size.Length() / 2,
                    chainDepth: TestChains.CamerasFrame
                );
            }
        );
        (int X, int Y) vanishing = (Width / 2, Height / 2);

        var wrong = new List<string>();
        for (int y = 0; y < Height; y++)
        for (int x = 0; x < Width; x++)
        {
            // A ray from the vanishing point's own pixel or next to it has hardly any image to walk.
            if (Math.Abs(x - vanishing.X) <= 2 && Math.Abs(y - vanishing.Y) <= 2)
                continue;
            int at = 4 * (y * Width + x);
            if (values[at] != 1 || values[at + 1] != 0 || ((int)values[at + 2], (int)values[at + 3]) != vanishing)
                wrong.Add(
                    $"pixel ({x}, {y}): outcome {values[at]}, share {values[at + 1]}, hit ({values[at + 2]}, {values[at + 3]})"
                );
        }

        wrong
            .Should()
            .BeEmpty(
                "the box covers the vanishing point of each ray, and each ray comes to it from in front ({0} rays wrong)",
                wrong.Count
            );
    }

    /// <summary>The values of screen_walk_slab_probe.comp, before a box wall 1 m deep that fills the screen 19.5 m away
    /// and faces the eye.</summary>
    private static float[] WallValues() =>
        ProbeValues(
            probe: "screen_walk_slab_probe.comp",
            width: Width,
            height: Height,
            instance: renderer =>
                Gpu.Instance(
                    mesh: renderer.CreateMesh(Primitives.Box(new(0.5f))),
                    material: renderer.CreateMaterial(MaterialDesc.Color(Vector4.One, roughness: 0.3f)),
                    position: new(0, 0, -20),
                    scale: new(60, 40, 1),
                    chainDepth: TestChains.CamerasFrame
                )
        );

    /// <summary>The image that <paramref name="probe"/> writes after one frame of a scene that holds only the instance
    /// that <paramref name="instance"/> makes, four values per pixel.</summary>
    private static float[] ProbeValues(
        string probe,
        int width,
        int height,
        Func<VulkanRenderer, RenderInstance> instance
    )
    {
        VulkanRenderer renderer = Gpu.Headless(
            width: width,
            height: height,
            screenTrace: ScreenTrace.Default,
            warmUpFrames: 0
        );
        try
        {
            RenderSnapshot scene = Gpu.Snapshot(Vector3.UnitY);
            scene.Instances.Add(instance(renderer));
            var screenProbe = ScreenTraceProbe.On(renderer, probe);

            renderer.Render(scene, 1f);

            return screenProbe.Read();
        }
        finally
        {
            Gpu.DisposeAndAssertClean(renderer);
        }
    }
}
