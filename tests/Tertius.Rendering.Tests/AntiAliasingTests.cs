using System.Numerics;
using static Tertius.Rendering.Tests.BrdfTesting;

namespace Tertius.Rendering.Tests;

/// <summary>
/// The switch <see cref="RendererOptions.AntiAliasing"/>, and SMAA 1x on the GPU (docs/plans/anti-aliasing.md). The
/// scene is a flat glowing square turned 30 degrees about the view axis in front of a flat dark wall, so one hard edge
/// crosses the image from bottom to top, 30 degrees from vertical, through the center. No light reaches either surface.
/// </summary>
[Collection(GpuCollection.Name)]
public sealed class AntiAliasingTests
{
    private const int Width = 640,
        Height = 360;

    /// <summary>The angle of the edge from vertical.</summary>
    private const float EdgeAngle = MathF.PI / 6;

    /// <summary>How far, in pixels, SMAA may change a pixel from the edge in a row.</summary>
    private const int Reach = 8;

    /// <summary>The rows that the row tests read: the middle of the image, away from its top and bottom borders.</summary>
    private const int FirstRow = 40,
        LastRow = Height - 40;

    private static readonly Vector3 SquareGlow = new(2f);
    private static readonly Vector3 WallGlow = new(0.03f);

    /// <summary>A glow that differs from <see cref="FaintWallGlow"/> by about 0.04 in display-encoded luma, below SMAA's threshold of 0.1.</summary>
    private static readonly Vector3 FaintSquareGlow = new(0.12f);

    private static readonly Vector3 FaintWallGlow = new(0.10f);

    /// <summary>
    /// A glow whose display-encoded luma is about 0.19 above that of <see cref="WallGlow"/>, while its linear light is only
    /// about 0.05 above it.
    /// </summary>
    private static readonly Vector3 DarkSquareGlow = new(0.12f);

    private static VulkanRenderer Headless(AntiAliasing antiAliasing) =>
        Gpu.Headless(width: Width, height: Height, antiAliasing: antiAliasing);

    /// <summary>
    /// The scene: a dark wall 6 m ahead, and a glowing square 3 m ahead whose right edge passes through the point on the
    /// view axis. The square and the wall both fill the image past its borders, so the edge is the image's only edge.
    /// </summary>
    private static RenderSnapshot DiagonalEdge(VulkanRenderer renderer) =>
        EdgeBetween(renderer: renderer, squareGlow: SquareGlow, wallGlow: WallGlow);

    /// <summary>The scene of <see cref="DiagonalEdge"/> with an edge too faint for SMAA to find.</summary>
    private static RenderSnapshot FaintEdge(VulkanRenderer renderer) =>
        EdgeBetween(renderer: renderer, squareGlow: FaintSquareGlow, wallGlow: FaintWallGlow);

    /// <summary>The scene of <see cref="DiagonalEdge"/> with a dark square, whose edge stands out only in display-encoded values.</summary>
    private static RenderSnapshot DarkEdge(VulkanRenderer renderer) =>
        EdgeBetween(renderer: renderer, squareGlow: DarkSquareGlow, wallGlow: WallGlow);

    private static RenderSnapshot EdgeBetween(VulkanRenderer renderer, Vector3 squareGlow, Vector3 wallGlow)
    {
        const float halfSize = 4;
        MeshId square = renderer.CreateMesh(Primitives.Plane(halfSize));
        MeshId wall = renderer.CreateMesh(Primitives.Plane(30));
        RenderSnapshot snapshot = Gpu.Snapshot(Vector3.UnitZ, sunIntensity: 0);
        snapshot.Ambient.Intensity = 0;
        var facing = Quaternion.CreateFromAxisAngle(Vector3.UnitX, MathF.PI / 2);
        Quaternion turned = Quaternion.CreateFromAxisAngle(Vector3.UnitZ, EdgeAngle) * facing;
        var toEdge = new Vector3(MathF.Cos(EdgeAngle), MathF.Sin(EdgeAngle), 0);
        snapshot.Instances.Add(
            Gpu.Instance(
                mesh: wall,
                material: Glow(renderer, wallGlow),
                position: new(0, 0, -6),
                scale: Vector3.One,
                rotation: facing,
                radius: 45
            )
        );
        snapshot.Instances.Add(
            Gpu.Instance(
                mesh: square,
                material: Glow(renderer, squareGlow),
                position: new Vector3(0, 0, -3) - halfSize * toEdge,
                scale: Vector3.One,
                rotation: turned,
                radius: 6
            )
        );
        return snapshot;
    }

    /// <summary>The x of the edge at the center of a pixel row, in pixels, from the projection of two points on it.</summary>
    private static float EdgeX(int row)
    {
        Vector2 center = Gpu.Project(point: new(0, 0, -3), width: Width, height: Height);
        Vector2 up = Gpu.Project(
            point: new(-MathF.Sin(EdgeAngle), MathF.Cos(EdgeAngle), -3),
            width: Width,
            height: Height
        );
        float slope = (up.X - center.X) / (up.Y - center.Y);
        return center.X + (row + 0.5f - center.Y) * slope;
    }

    /// <summary>The three color bytes of one pixel. The fourth byte of a capture holds no defined value.</summary>
    private static uint Bytes(Capture image, int x, int y) =>
        BitConverter.ToUInt32(image.Rgba, (y * Width + x) * 4) & 0x00FF_FFFF;

    /// <summary>How many pixels of <paramref name="image"/> differ from <paramref name="reference"/> in a color byte.</summary>
    private static int Differences(Capture image, Capture reference, Func<int, int, bool> counts)
    {
        int differences = 0;
        for (int y = 0; y < Height; y++)
        for (int x = 0; x < Width; x++)
            if (counts(x, y) && Bytes(image, x, y) != Bytes(reference, x, y))
                differences++;
        return differences;
    }

    /// <summary>The linear light of a pixel's green channel, from its stored sRGB value.</summary>
    private static float Light(Capture image, int x, int y) => Measure.ToLinear(image.Color(x, y)).Y;

    /// <summary>The share of the square's light in a pixel's light: 0 for the wall, 1 for the square.</summary>
    private static float SquareShare(Capture image, int x, int y)
    {
        int row = (FirstRow + LastRow) / 2;
        float square = Light(image, (int)EdgeX(row) - 3 * Reach, row);
        float wall = Light(image, (int)EdgeX(row) + 3 * Reach, row);
        return (Light(image, x, y) - wall) / (square - wall);
    }

    /// <summary>
    /// The mean, over the rows, of the difference between the summed square share of the pixels around the edge and the
    /// true area of the square in them.
    /// </summary>
    private static double MeanCoverageError(Capture image)
    {
        double total = 0;
        for (int row = FirstRow; row < LastRow; row++)
        {
            int first = (int)EdgeX(row) - Reach;
            double share = 0;
            for (int x = first; x <= first + 2 * Reach; x++)
                share += SquareShare(image, x, row);
            // The edge is straight, so the square's area in the row's strip is its width at the row's center.
            double area = EdgeX(row) - first;
            total += Math.Abs(share - area);
        }

        return total / (LastRow - FirstRow);
    }

    private static Capture Render(AntiAliasing antiAliasing, string name, Func<VulkanRenderer, RenderSnapshot> scene)
    {
        VulkanRenderer renderer = Headless(antiAliasing);
        try
        {
            return Gpu.RenderAndCapture(renderer: renderer, snapshot: scene(renderer), name: name);
        }
        finally
        {
            Gpu.DisposeAndAssertClean(renderer);
        }
    }

    [Fact]
    public void SmaaIsTheDefault() =>
        new RendererOptions().AntiAliasing.Should().Be(AntiAliasing.Smaa, "the user chose SMAA 1x as the default");

    [Fact]
    public void AnAntiAliasingOutsideTheEnumIsRefused() =>
        FluentActions
            .Invoking(() =>
                VulkanRenderer.CreateHeadless(
                    new()
                    {
                        Width = 64,
                        Height = 64,
                        AntiAliasing = (AntiAliasing)2,
                    }
                )
            )
            .Should()
            .ThrowExactly<ArgumentOutOfRangeException>();

    [Fact]
    public void WithSmaaTheThreeSmaaPassesRunBetweenTheToneMapAndTheOverlay()
    {
        VulkanRenderer renderer = Headless(AntiAliasing.Smaa);
        try
        {
            string[] passes = [.. renderer.Graph.Passes.Select(pass => pass.Name)];

            passes
                .SkipWhile(pass => pass != "tone map")
                .Take(5)
                .Should()
                .Equal("tone map", "SMAA edges", "SMAA weights", "SMAA blending", "outlines");
        }
        finally
        {
            Gpu.DisposeAndAssertClean(renderer);
        }
    }

    [Fact]
    public void WithoutAntiAliasingTheOverlayFollowsTheToneMapAndNoSmaaPassIsInTheGraph()
    {
        VulkanRenderer renderer = Headless(AntiAliasing.None);
        try
        {
            string[] passes = [.. renderer.Graph.Passes.Select(pass => pass.Name)];

            passes.SkipWhile(pass => pass != "tone map").Take(2).Should().Equal("tone map", "outlines");
            passes.Should().NotContain(pass => pass.StartsWith("SMAA", StringComparison.Ordinal));
        }
        finally
        {
            Gpu.DisposeAndAssertClean(renderer);
        }
    }

    [Fact]
    public void WithoutAntiAliasingEveryPixelAlongTheEdgeHasOneOfTheTwoFlatColors()
    {
        Capture image = Render(AntiAliasing.None, "aa-off-edge", DiagonalEdge);

        int row = (FirstRow + LastRow) / 2;
        uint square = Bytes(image, (int)EdgeX(row) - 3 * Reach, row);
        uint wall = Bytes(image, (int)EdgeX(row) + 3 * Reach, row);
        square.Should().NotBe(wall);
        for (int y = FirstRow; y < LastRow; y++)
        for (int x = (int)EdgeX(y) - Reach; x <= (int)EdgeX(y) + Reach; x++)
            Bytes(image, x, y).Should().BeOneOf([square, wall], "pixel ({0}, {1}) shows the square or the wall", x, y);
    }

    [Fact]
    public void WithSmaaEveryRowAcrossTheEdgeHasAPixelBetweenTheTwoFlatColors()
    {
        Capture image = Render(AntiAliasing.Smaa, "aa-smaa-edge", DiagonalEdge);

        for (int y = FirstRow; y < LastRow; y++)
        {
            int row = y;
            IEnumerable<float> shares = Enumerable
                .Range((int)EdgeX(row) - Reach, 2 * Reach + 1)
                .Select(x => SquareShare(image, x, row));
            shares.Should().Contain(share => share > 0.05f && share < 0.95f, "SMAA blends the edge in row {0}", row);
        }
    }

    [Fact]
    public void SmaaFindsAnEdgeBetweenTwoDarkColorsBecauseItComparesTheirDisplayEncodedLuma()
    {
        Capture image = Render(AntiAliasing.Smaa, "aa-smaa-dark-edge", DarkEdge);

        for (int y = FirstRow; y < LastRow; y++)
        {
            int row = y;
            Enumerable
                .Range((int)EdgeX(row) - Reach, 2 * Reach + 1)
                .Select(x => SquareShare(image, x, row))
                .Should()
                .Contain(share => share > 0.05f && share < 0.95f, "SMAA blends the dark edge in row {0}", row);
        }
    }

    [Fact]
    public void WithSmaaThePixelsFarFromTheEdgeAreBitIdenticalToThoseWithoutAntiAliasing()
    {
        Capture off = Render(AntiAliasing.None, "aa-off-far", DiagonalEdge);
        Capture smaa = Render(AntiAliasing.Smaa, "aa-smaa-far", DiagonalEdge);

        Differences(smaa, off, counts: (x, y) => Math.Abs(x - EdgeX(y)) > Reach)
            .Should()
            .Be(0, "SMAA changes only the pixels next to an edge");
    }

    [Fact]
    public void WithSmaaTheLightOfEachRowAcrossTheEdgeIsCloserToTheAreaThatTheSquareCovers()
    {
        Capture off = Render(AntiAliasing.None, "aa-off-coverage", DiagonalEdge);
        Capture smaa = Render(AntiAliasing.Smaa, "aa-smaa-coverage", DiagonalEdge);

        double offError = MeanCoverageError(off);
        double smaaError = MeanCoverageError(smaa);

        smaaError
            .Should()
            .BeLessThan(offError / 2, "SMAA blends in linear light by the area of each pixel that the square covers");
    }

    [Fact]
    public void WithSmaaTheOverlayKeepsItsHardPixels()
    {
        static RenderSnapshot Outlined(VulkanRenderer renderer)
        {
            MeshId wall = renderer.CreateMesh(Primitives.Plane(30));
            RenderSnapshot snapshot = Gpu.Snapshot(Vector3.UnitZ, sunIntensity: 0);
            snapshot.Ambient.Intensity = 0;
            snapshot.Instances.Add(
                Gpu.Instance(
                    mesh: wall,
                    material: Glow(renderer, WallGlow),
                    position: new(0, 0, -12),
                    scale: Vector3.One,
                    rotation: Quaternion.CreateFromAxisAngle(Vector3.UnitX, MathF.PI / 2),
                    radius: 45
                )
            );
            snapshot.Outlines.Add(new RestingOutline(new(0, 0, -6), 2.4f));
            return snapshot;
        }

        Capture off = Render(AntiAliasing.None, "aa-off-overlay", Outlined);
        Capture smaa = Render(AntiAliasing.Smaa, "aa-smaa-overlay", Outlined);

        Differences(smaa, off, counts: (_, _) => true)
            .Should()
            .Be(0, "the overlay draws after SMAA, so SMAA never blends the outlines");
    }

    [Fact]
    public void AnEdgeTooFaintForSmaaShowsNoBlendingEvenAfterAFrameThatBlendedAStrongEdgeAtTheSamePixels()
    {
        Capture off = Render(AntiAliasing.None, "aa-off-faint", FaintEdge);
        VulkanRenderer renderer = Headless(AntiAliasing.Smaa);
        try
        {
            Gpu.RenderAndCapture(renderer: renderer, snapshot: DiagonalEdge(renderer), name: "aa-smaa-strong");
            Capture smaa = Gpu.RenderAndCapture(
                renderer: renderer,
                snapshot: FaintEdge(renderer),
                name: "aa-smaa-faint"
            );

            Differences(smaa, off, counts: (_, _) => true)
                .Should()
                .Be(
                    0,
                    "the edge and weight images start at zero in every frame, so the strong edge leaves nothing behind"
                );
        }
        finally
        {
            Gpu.DisposeAndAssertClean(renderer);
        }
    }

    [Fact]
    public void AfterAResizeSmaaBlendsTheEdgeAtTheNewSize()
    {
        VulkanRenderer renderer = Gpu.Headless(width: 320, height: 200, antiAliasing: AntiAliasing.Smaa);
        try
        {
            RenderSnapshot snapshot = DiagonalEdge(renderer);
            renderer.Render(snapshot, 1f);
            renderer.Resize(Width, Height);

            Capture image = Gpu.RenderAndCapture(renderer: renderer, snapshot: snapshot, name: "aa-smaa-resized");

            (image.Width, image.Height).Should().Be((Width, Height));
            int row = (FirstRow + LastRow) / 2;
            Enumerable
                .Range((int)EdgeX(row) - Reach, 2 * Reach + 1)
                .Select(x => SquareShare(image, x, row))
                .Should()
                .Contain(share => share > 0.05f && share < 0.95f, "SMAA blends the edge at the new size");
        }
        finally
        {
            Gpu.DisposeAndAssertClean(renderer);
        }
    }
}
