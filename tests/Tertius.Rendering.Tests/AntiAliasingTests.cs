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

    /// <summary>
    /// The scene of <see cref="DiagonalEdge"/> with the square's edge at <paramref name="angle"/> radians from vertical,
    /// turned counterclockwise as the camera sees it.
    /// </summary>
    private static RenderSnapshot EdgeBetween(
        VulkanRenderer renderer,
        Vector3 squareGlow,
        Vector3 wallGlow,
        float angle = EdgeAngle
    )
    {
        const float halfSize = 4;
        MeshId square = renderer.CreateMesh(Primitives.Plane(halfSize));
        MeshId wall = renderer.CreateMesh(Primitives.Plane(30));
        RenderSnapshot snapshot = Gpu.Snapshot(Vector3.UnitZ, starIrradiance: 0);
        snapshot.Ambient.Intensity = 0;
        var facing = Quaternion.CreateFromAxisAngle(Vector3.UnitX, MathF.PI / 2);
        Quaternion turned = Quaternion.CreateFromAxisAngle(Vector3.UnitZ, angle) * facing;
        var toEdge = new Vector3(MathF.Cos(angle), MathF.Sin(angle), 0);
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
    public void ARendererWhoseOptionsDoNotNameAnAntiAliasingRunsTheThreeSmaaPassesBetweenTheToneMapAndTheOverlay()
    {
        var renderer = VulkanRenderer.CreateHeadless(
            new()
            {
                Width = Width,
                Height = Height,
                Impostors = false,
            }
        );
        try
        {
            string[] passes = [.. renderer.Graph.Passes.Select(pass => pass.Name)];

            passes
                .SkipWhile(pass => pass != "tone map")
                .Take(5)
                .Should()
                .Equal(
                    ["tone map", "SMAA edges", "SMAA weights", "SMAA blending", "outlines"],
                    "the user chose SMAA 1x as the default"
                );
        }
        finally
        {
            Gpu.DisposeAndAssertClean(renderer);
        }
    }

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
    public void OnAUnormOutputSmaaAppliesTheSrgbCurveOnceSoThePixelsFarFromTheEdgeMatchThoseOfAnSrgbOutput()
    {
        Capture srgb = Render(AntiAliasing.Smaa, "aa-smaa-srgb-output", DiagonalEdge);
        var renderer = VulkanRenderer.CreateOffscreen(
            new()
            {
                Validation = true,
                Width = Width,
                Height = Height,
                Impostors = false,
                // As Gpu.Headless does, so that only the output format differs from the sRGB image.
                Exposure = new FixedExposure(0),
                Bloom = new NoBloom(),
                AntiAliasing = AntiAliasing.Smaa,
            },
            (device, allocator, extent) => new UnormOutput(device: device, allocator: allocator, extent: extent)
        );
        try
        {
            Capture unorm = Gpu.RenderAndCapture(
                renderer: renderer,
                snapshot: DiagonalEdge(renderer),
                name: "aa-smaa-unorm-output"
            );

            Differences(unorm, srgb, counts: (x, y) => Math.Abs(x - EdgeX(y)) > Reach)
                .Should()
                .Be(0, "the SMAA blending encodes a UNORM output in its shader as the hardware encodes an sRGB one");
        }
        finally
        {
            Gpu.DisposeAndAssertClean(renderer);
        }
    }

    [Fact]
    public void WithSmaaTheOverlayKeepsItsHardPixels()
    {
        static RenderSnapshot Outlined(VulkanRenderer renderer)
        {
            MeshId wall = renderer.CreateMesh(Primitives.Plane(30));
            RenderSnapshot snapshot = Gpu.Snapshot(Vector3.UnitZ, starIrradiance: 0);
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

    /// <summary>
    /// The light of each pixel along straight edges at many slopes, against the area of the pixel that the square covers
    /// (tests/STYLE.md, "Long tests").
    /// </summary>
    public sealed class PixelCoverage
    {
        /// <summary>How far, in pixels, the center of a pixel that the error counts may lie from the edge.</summary>
        private const float Band = 1.5f;

        /// <summary>The pixels that the error leaves out at each border of the image.</summary>
        private const int Margin = 40;

        /// <summary>
        /// The mean, over the pixels whose centers lie within <see cref="Band"/> of the edge, of the difference between a
        /// pixel's square share and the area of it that the square covers.
        /// </summary>
        private static double MeanPixelError(Capture image, ProjectedEdge edge)
        {
            Vector2 inside = edge.Point + 3 * Reach * edge.Normal;
            Vector2 outside = edge.Point - 3 * Reach * edge.Normal;
            float square = Light(image, (int)inside.X, (int)inside.Y);
            float wall = Light(image, (int)outside.X, (int)outside.Y);
            double total = 0;
            int pixels = 0;
            for (int y = Margin; y < Height - Margin; y++)
            for (int x = Margin; x < Width - Margin; x++)
            {
                if (MathF.Abs(edge.Distance(new(x + 0.5f, y + 0.5f))) >= Band)
                    continue;
                float share = (Light(image, x, y) - wall) / (square - wall);
                total += Math.Abs(share - edge.Coverage(x, y));
                pixels++;
            }

            return total / pixels;
        }

        /// <summary>
        /// The error of SMAA is at most this share of the error without anti-aliasing at every slope. Measured on
        /// 2026-10-25, the largest share was 0.52, near 45 degrees. An area table uploaded with its rows reversed, which
        /// turns the blend along the runs of a staircase the wrong way, gave 0.87 or more at every slope.
        /// </summary>
        private const double MostSmaaErrorShare = 0.6;

        /// <remarks>
        /// The slopes leave out exactly 45 degrees: that edge passes through pixel centers, so the rasterizer's rule for
        /// ties decides those pixels, not their covered area. 49.7 degrees is 1.18 pixels of x per row.
        /// </remarks>
        [Theory]
        [InlineData(5f)]
        [InlineData(30f)]
        [InlineData(40f)]
        [InlineData(44f)]
        [InlineData(46f)]
        [InlineData(49.7f)]
        [InlineData(60f)]
        [InlineData(85f)]
        [InlineData(-5f)]
        [InlineData(-30f)]
        [InlineData(-40f)]
        [InlineData(-44f)]
        [InlineData(-46f)]
        [InlineData(-49.7f)]
        [InlineData(-60f)]
        [InlineData(-85f)]
        public void WithSmaaEachPixelAlongAStraightEdgeIsCloserToTheAreaThatTheSquareCoversThanWithout(float degrees)
        {
            float angle = degrees * MathF.PI / 180;
            Capture off = Render(
                AntiAliasing.None,
                $"aa-off-pixels-{degrees}",
                renderer => EdgeBetween(renderer: renderer, squareGlow: SquareGlow, wallGlow: WallGlow, angle: angle)
            );
            Capture smaa = Render(
                AntiAliasing.Smaa,
                $"aa-smaa-pixels-{degrees}",
                renderer => EdgeBetween(renderer: renderer, squareGlow: SquareGlow, wallGlow: WallGlow, angle: angle)
            );
            var edge = ProjectedEdge.At(angle);

            double offError = MeanPixelError(off, edge);
            double smaaError = MeanPixelError(smaa, edge);
            TestContext.Current.TestOutputHelper?.WriteLine($"{degrees}: off {offError:F4}, smaa {smaaError:F4}");

            smaaError
                .Should()
                .BeLessThan(
                    MostSmaaErrorShare * offError,
                    "SMAA blends each pixel by the area of it that the square covers, at {0} degrees (off {1:F4})",
                    degrees,
                    offError
                );
        }
    }

    /// <summary>The square's edge in the image: a point on it, in pixels, and the unit normal that points into the square.</summary>
    private readonly record struct ProjectedEdge(Vector2 Point, Vector2 Normal)
    {
        /// <summary>The subdivisions of a pixel along the axis that <see cref="Coverage"/> sums over.</summary>
        private const int Strips = 64;

        /// <summary>The edge of the square of <see cref="EdgeBetween"/> at <paramref name="angle"/> radians from vertical.</summary>
        public static ProjectedEdge At(float angle)
        {
            Vector2 point = Gpu.Project(point: new(0, 0, -3), width: Width, height: Height);
            Vector2 along = Gpu.Project(
                point: new(-MathF.Sin(angle), MathF.Cos(angle), -3),
                width: Width,
                height: Height
            );
            Vector2 intoSquare = Gpu.Project(
                point: new(-MathF.Cos(angle), -MathF.Sin(angle), -3),
                width: Width,
                height: Height
            );
            var direction = Vector2.Normalize(along - point);
            var normal = new Vector2(-direction.Y, direction.X);
            if (Vector2.Dot(normal, intoSquare - point) < 0)
                normal = -normal;
            return new(point, normal);
        }

        /// <summary>The signed distance of a point from the edge, in pixels: positive inside the square.</summary>
        public float Distance(Vector2 pixel) => Vector2.Dot(pixel - Point, Normal);

        /// <summary>
        /// The area of the pixel whose top left corner is at (<paramref name="x"/>, <paramref name="y"/>) that the square
        /// covers, from 0 to 1: the sum, over thin strips across the axis that the edge crosses more steeply, of the exact
        /// covered length of each strip.
        /// </summary>
        public double Coverage(int x, int y)
        {
            bool stripsAreRows = MathF.Abs(Normal.X) >= MathF.Abs(Normal.Y);
            (double across, double along) = stripsAreRows ? (Normal.X, Normal.Y) : (Normal.Y, Normal.X);
            (double start, double alongStart) = stripsAreRows ? (x - Point.X, y - Point.Y) : (y - Point.Y, x - Point.X);
            double covered = 0;
            for (int strip = 0; strip < Strips; strip++)
            {
                double offset = alongStart + (strip + 0.5) / Strips;
                // Inside where across * t + along * offset > 0, for t from start to start + 1.
                double bound = -along * offset / across;
                double length = across > 0 ? start + 1 - bound : bound - start;
                covered += Math.Clamp(length, 0, 1);
            }

            return covered / Strips;
        }
    }
}
