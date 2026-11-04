using System.Numerics;
using Silk.NET.Vulkan;
using Tertius.Testing;

namespace Tertius.Rendering.Tests;

/// <summary>
/// The depth pyramid of the screen tracer on the GPU (docs/plans/screen-space-reflections.md, "GPU tests of the
/// pyramids"): the depth pyramid and its finest level against their C# copy, the padding of its image, a resize, and the
/// passes that the setting adds. The scene is a floor, two boxes and a lamp under the sky, at 640 × 360, whose odd levels exercise the sizes that round up.
/// A second scene puts a panel before a wall, for the bounds at a depth edge.
/// </summary>
[Collection(GpuCollection.Name)]
public sealed class PyramidRenderTests
{
    private const int Width = 640,
        Height = 360;

    /// <summary>
    /// Vulkan allows its square root and its division a few units in the last place, so the GPU's inverse depth of a
    /// pixel may differ from the C# copy's by that much.
    /// </summary>
    private const float InverseDepthTolerance = 1e-6f;

    // At 640 × 360 the depth image is 512 × 256, and levels 5 to 10 are the tail, which one workgroup builds
    // (DepthPyramidTail.FirstMip).
    [Theory]
    [InlineData(1)]
    [InlineData(3)]
    [InlineData(4)]
    [InlineData(5)]
    [InlineData(6)]
    [InlineData(9)]
    [InlineData(10)]
    public void EachDepthPyramidCellHoldsTheBoundsOfThePixelsUnderIt(int level)
    {
        VulkanRenderer renderer = Gpu.Headless(screenTrace: ScreenTrace.Default);
        try
        {
            renderer.Render(Scene(renderer), 1f);
            DepthPyramid expected = ReflectionTesting.PyramidOf(renderer);
            float[] slack = UndecidedSlack(renderer);
            (float[] bounds, int mipWidth, _) = renderer.ReadDepthPyramidLevel(level);

            (int levelWidth, int levelHeight) = DepthPyramid.LevelSize(Width, Height, level);
            int compared = 0;
            for (int y = 0; y < levelHeight; y++)
            for (int x = 0; x < levelWidth; x++)
            {
                CellBounds cell = expected.Bounds(level, x, y);
                (bool nearUndecided, bool farUndecided) = Undecided(
                    expected: expected,
                    slack: slack,
                    level: level,
                    cell: (x, y)
                );
                int index = 2 * (y * mipWidth + x);
                if (!nearUndecided)
                {
                    bounds[index]
                        .Should()
                        .BeApproximately(
                            cell.Near,
                            cell.Near * InverseDepthTolerance,
                            "the near bound of cell ({0}, {1})",
                            x,
                            y
                        );
                    compared++;
                }
                if (!farUndecided)
                {
                    bounds[index + 1]
                        .Should()
                        .BeApproximately(
                            cell.Far,
                            cell.Far * InverseDepthTolerance,
                            "the far bound of cell ({0}, {1})",
                            x,
                            y
                        );
                    compared++;
                }
            }

            compared.Should().BeGreaterThan(levelWidth * levelHeight, "few bounds rest on an undecided pixel");
        }
        finally
        {
            Gpu.DisposeAndAssertClean(renderer);
        }
    }

    [Fact]
    public void ACellAtADepthEdgeBoundsTheNearSurfaceOnlyToTheCenterLineOfItsLastRowWhereItsPlanePassesTheFarSurface()
    {
        // The panel's plane, seen from below at a grazing angle, runs steeply in depth: one pixel below the panel's far
        // edge it lies within 1 % of the wall behind. Only the wall's plane, which never passes back through the panel's
        // last row, tells the depth edge (the light bars of ter-eqtb's floor film at a roughness limit of 0.6).
        VulkanRenderer renderer = Gpu.Headless(screenTrace: ScreenTrace.Default);
        try
        {
            renderer.Render(PanelBeforeAWall(renderer), 1f);
            float[] distance = renderer.ReadDistance();
            CameraView camera = renderer.LastScene.Camera;
            (float[] bounds, int mipWidth, _) = renderer.ReadDepthPyramidLevel(1);

            distance[PanelLastRow * Width + Width / 2]
                .Should()
                .BeLessThan(PanelFarEdge, "the panel's last row sees it");
            distance[(PanelLastRow + 1) * Width + Width / 2]
                .Should()
                .BeGreaterThan(PanelFarEdge, "the next row sees the wall");
            // The cells of level 1 whose lower row is the panel's last row, under the middle of the panel.
            float centerLine = camera.ViewRayThrough(new(Width / 2, PanelLastRow + 0.5f)).Y / PanelHeight;
            for (int x = 100; x < 220; x++)
                bounds[2 * (PanelLastRow / 2 * mipWidth + x) + 1]
                    .Should()
                    .BeGreaterThanOrEqualTo(
                        centerLine * (1 - 1e-5f),
                        "the panel's plane stops at its last row's center line toward the wall, in cell ({0}, {1})",
                        x,
                        PanelLastRow / 2
                    );
        }
        finally
        {
            Gpu.DisposeAndAssertClean(renderer);
        }
    }

    [Fact]
    public void EachTexelOfTheFinestLevelHoldsOneOverTheViewDepthOfItsPixelsCenterAndSkyHoldsZero()
    {
        VulkanRenderer renderer = Gpu.Headless(screenTrace: ScreenTrace.Default);
        try
        {
            renderer.Render(Scene(renderer), 1f);
            CameraView camera = renderer.LastScene.Camera;
            float[] distance = renderer.ReadDistance();
            float[] finest = renderer.ReadDepthFinest();

            int surfaces = 0;
            var wrong = new List<string>();
            for (int y = 0; y < Height; y++)
            for (int x = 0; x < Width; x++)
            {
                float distanceToEye = distance[y * Width + x];
                float expected = distanceToEye <= 0
                    ? 0
                    : camera.ViewRayThrough(new(x + 0.5f, y + 0.5f)).Length() / distanceToEye;
                if (distanceToEye > 0)
                    surfaces++;
                float texel = finest[y * Width + x];
                if (MathF.Abs(texel - expected) > InverseDepthTolerance * expected)
                    wrong.Add($"({x}, {y}): {texel}, not {expected}");
            }

            surfaces.Should().BeInRange(Width * Height / 4, Width * Height - 1, "the premise: floor, boxes and sky");
            wrong.Should().BeEmpty();
        }
        finally
        {
            Gpu.DisposeAndAssertClean(renderer);
        }
    }

    [Fact]
    public void ACellOfOnlySkyHoldsTheEmptyBounds()
    {
        VulkanRenderer renderer = Gpu.Headless(screenTrace: ScreenTrace.Default);
        try
        {
            renderer.Render(Scene(renderer), 1f);
            DepthPyramid expected = ReflectionTesting.PyramidOf(renderer);
            (float[] bounds, _, _) = renderer.ReadDepthPyramidLevel(3);

            // The top row of level 3 covers the 8 top rows of the screen, which look at the sky over the floor.
            expected.Bounds(level: 3, x: 0, y: 0).Should().Be(CellBounds.Empty, "the scene puts sky at the top");
            bounds[0].Should().Be(0, "a cell with no surface has no near bound");
            bounds[1].Should().Be(CellBounds.EmptyFar, "and the empty far bound, exactly");
        }
        finally
        {
            Gpu.DisposeAndAssertClean(renderer);
        }
    }

    [Theory]
    [InlineData(1)]
    [InlineData(3)]
    [InlineData(7)]
    public void ACellBesideTheScreenHoldsTheEmptyBounds(int level)
    {
        VulkanRenderer renderer = Gpu.Headless(screenTrace: ScreenTrace.Default);
        try
        {
            renderer.Render(Scene(renderer), 1f);
            (float[] bounds, int mipWidth, int mipHeight) = renderer.ReadDepthPyramidLevel(level);

            (int levelWidth, int levelHeight) = DepthPyramid.LevelSize(Width, Height, level);
            var padding = new List<(float Near, float Far)>();
            for (int y = 0; y < mipHeight; y++)
            for (int x = 0; x < mipWidth; x++)
                if (x >= levelWidth || y >= levelHeight)
                    padding.Add((bounds[2 * (y * mipWidth + x)], bounds[2 * (y * mipWidth + x) + 1]));

            padding.Should().NotBeEmpty("the image pads level {0} of {1} × {2} cells", level, levelWidth, levelHeight);
            padding
                .Should()
                .AllBeEquivalentTo(
                    (0f, CellBounds.EmptyFar),
                    "the trace reads the padding as cells outside the screen"
                );
        }
        finally
        {
            Gpu.DisposeAndAssertClean(renderer);
        }
    }

    [Fact]
    public void ThePyramidFollowsAResize()
    {
        const int newWidth = 321,
            newHeight = 181;
        VulkanRenderer renderer = Gpu.Headless(screenTrace: ScreenTrace.Default);
        try
        {
            RenderSnapshot snapshot = Scene(renderer);
            renderer.Render(snapshot, 1f);
            renderer.Resize(newWidth, newHeight);
            renderer.Render(snapshot, 1f);
            DepthPyramid expected = ReflectionTesting.PyramidOf(renderer);
            float[] slack = UndecidedSlack(renderer);
            (float[] bounds, int mipWidth, _) = renderer.ReadDepthPyramidLevel(1);

            renderer.ReadDepthFinest().Length.Should().Be(newWidth * newHeight, "the finest level has the new size");
            expected.Width.Should().Be(newWidth);
            (int levelWidth, int levelHeight) = DepthPyramid.LevelSize(newWidth, newHeight, 1);
            for (int y = 0; y < levelHeight; y++)
            for (int x = 0; x < levelWidth; x++)
                if (!Undecided(expected: expected, slack: slack, level: 1, cell: (x, y)).Near)
                    bounds[2 * (y * mipWidth + x)]
                        .Should()
                        .BeApproximately(
                            expected.Bounds(1, x, y).Near,
                            expected.Bounds(1, x, y).Near * InverseDepthTolerance,
                            "cell ({0}, {1}) of level 1 after the resize",
                            x,
                            y
                        );
        }
        finally
        {
            Gpu.DisposeAndAssertClean(renderer);
        }
    }

    // ResizeTargets, not Resize: the output of a device whose largest image is 16,384 px would refuse the size first.
    [Fact]
    public void AResizePastTheLongestSideIsRefusedAndKeepsThePyramid()
    {
        VulkanRenderer renderer = Gpu.Headless(screenTrace: ScreenTrace.Default);
        try
        {
            FluentActions
                .Invoking(() => renderer.ResizeTargets(new(16_385, 16)))
                .Should()
                .ThrowExactly<ArgumentOutOfRangeException>()
                .WithMessage("*16384 px a side*");

            renderer.Render(Scene(renderer), 1f);
            renderer.ReadDepthFinest().Length.Should().Be(Width * Height, "the refused size destroyed nothing");
        }
        finally
        {
            Gpu.DisposeAndAssertClean(renderer);
        }
    }

    [Fact]
    public void NoScreenTraceAddsNoPyramidPass()
    {
        VulkanRenderer renderer = Gpu.Headless(screenTrace: new NoScreenTrace());
        try
        {
            renderer
                .Graph.Passes.Select(pass => pass.Name)
                .Should()
                .NotContain(["depth pyramid", "ray plan"], "only the tracer's rays read the pyramid");
        }
        finally
        {
            Gpu.DisposeAndAssertClean(renderer);
        }
    }

    [Fact]
    public void TheScreenTracerBuildsTheDepthPyramidAfterTheGBufferAndBeforeItsRays()
    {
        VulkanRenderer renderer = Gpu.Headless(screenTrace: ScreenTrace.Default);
        try
        {
            renderer
                .Graph.Passes.Select(pass => pass.Name)
                .Should()
                .ContainInOrder([
                    "G-buffer",
                    "depth pyramid",
                    "direct light",
                    "ray plan",
                    "reflection rays",
                    "composite",
                    "transparent",
                ]);
        }
        finally
        {
            Gpu.DisposeAndAssertClean(renderer);
        }
    }

    /// <summary>
    /// The share of a pixel's inverse depth within which the GPU and its C# copy can decide a continuation differently.
    /// Each computes a plane's inverse depth at a neighbor's center with its own rounding, a few units in the last place
    /// (<see cref="InverseDepthTolerance"/>), and cuts hard at <see cref="CellBounds.ContinuationShare"/>. So a neighbor
    /// whose center stands within rounding of the cut can continue the plane on one side and not on the other, and move
    /// the pixel's bound by half a pixel's change of inverse depth: the scene of these tests has one neighbor 8.6 · 10⁻⁶
    /// from the cut and seven within 10⁻⁴. A hundred times the rounding.
    /// </summary>
    private const float UndecidedShare = 1e-4f;

    /// <summary>
    /// For each pixel, row by row from the top, how far its bounds can move in 1/m where the GPU decides a continuation
    /// into an edge neighbor the other way: half its plane's change of inverse depth across and down, where the
    /// continuation's test passes within <see cref="UndecidedShare"/> of its cut; 0 elsewhere.
    /// </summary>
    private static float[] UndecidedSlack(VulkanRenderer renderer)
    {
        CameraView camera = renderer.LastScene.Camera;
        float[] distances = renderer.ReadDistance();
        float[] packed = renderer.ReadNormals();
        var normals = new Vector3[packed.Length / 4];
        for (int pixel = 0; pixel < normals.Length; pixel++)
            normals[pixel] = OctahedralNormal.Decode(new(packed[4 * pixel + 2], packed[4 * pixel + 3]));
        float[] slack = new float[camera.Width * camera.Height];
        for (int y = 0; y < camera.Height; y++)
        for (int x = 0; x < camera.Width; x++)
        {
            if (plane(x, y) is not { } own)
                continue;
            foreach ((int X, int Y) neighbor in new[] { (x - 1, y), (x + 1, y), (x, y - 1), (x, y + 1) })
            {
                bool inside =
                    neighbor.X >= 0 && neighbor.Y >= 0 && neighbor.X < camera.Width && neighbor.Y < camera.Height;
                if (!inside || plane(neighbor.X, neighbor.Y) is not { } other)
                    continue;
                if (
                    ScreenTesting.NearTheCut(own, other, UndecidedShare)
                    || ScreenTesting.NearTheCut(other, own, UndecidedShare)
                )
                    slack[y * camera.Width + x] = (MathF.Abs(own.Gradient.X) + MathF.Abs(own.Gradient.Y)) / 2;
            }
        }
        return slack;

        PixelPlane? plane(int x, int y) =>
            PixelPlane.Of(x: x, y: y, distances: distances, geometricNormals: normals, camera: camera);
    }

    /// <summary>
    /// Whether a cell's near bound and its far bound can rest on an undecided pixel under it: one whose bound, moved by
    /// its <see cref="UndecidedSlack"/>, reaches the cell's bound. Only there may the GPU's bound differ from the C# copy's.
    /// </summary>
    private static (bool Near, bool Far) Undecided(DepthPyramid expected, float[] slack, int level, (int X, int Y) cell)
    {
        CellBounds bounds = expected.Bounds(level, cell.X, cell.Y);
        (bool near, bool far) = (false, false);
        for (int y = cell.Y << level; y < Math.Min((cell.Y + 1) << level, expected.Height); y++)
        for (int x = cell.X << level; x < Math.Min((cell.X + 1) << level, expected.Width); x++)
        {
            float pixelSlack = slack[y * expected.Width + x];
            if (pixelSlack == 0)
                continue;
            CellBounds pixel = expected.Bounds(0, x, y);
            near |= pixel.Near + pixelSlack >= bounds.Near * (1 - InverseDepthTolerance);
            far |= pixel.Far - pixelSlack <= bounds.Far * (1 + InverseDepthTolerance);
        }
        return (near, far);
    }

    /// <summary>The height of the bottom of the panel of <see cref="PanelBeforeAWall"/> above the eye, in m.</summary>
    private const float PanelHeight = 0.5f;

    /// <summary>The last screen row that sees the panel of <see cref="PanelBeforeAWall"/>: odd, so that it is the lower
    /// row of its cell of level 1, and the wall's first row lies in the next cell.</summary>
    private const int PanelLastRow = 149;

    /// <summary>The tangent of the elevation of one screen row, under <see cref="Gpu.Fov"/> at <see cref="Height"/>.
    /// </summary>
    private static float RowTangent => 2 * MathF.Tan(Gpu.Fov / 2) / Height;

    /// <summary>The distance ahead of the panel's far edge, in m: where its bottom ends on the boundary between
    /// <see cref="PanelLastRow"/> and the next row.</summary>
    private static float PanelFarEdge => PanelHeight / ((Height / 2 - PanelLastRow - 1) * RowTangent);

    /// <summary>
    /// A gray panel 6 m wide whose bottom lies <see cref="PanelHeight"/> above the eye, from 2 m ahead to
    /// <see cref="PanelFarEdge"/>, before a gray wall that faces the eye under the sky. The wall stands where the panel's
    /// plane meets the view ray through the center of the row below <see cref="PanelLastRow"/>, and 0.5 % farther.
    /// </summary>
    private static RenderSnapshot PanelBeforeAWall(VulkanRenderer renderer)
    {
        RenderSnapshot snapshot = Gpu.Snapshot(new(0.4f, 0.8f, 0.3f));
        MeshId box = renderer.CreateMesh(Primitives.Box(new(0.5f)));
        MaterialId gray = renderer.CreateMaterial(
            MaterialDesc.Color(new(x: 0.5f, y: 0.5f, z: 0.5f, w: 1), roughness: 0.5f)
        );
        float wall = 1.005f * PanelHeight / ((Height / 2 - PanelLastRow - 1.5f) * RowTangent);
        snapshot.Instances.Add(
            Block(
                box: box,
                material: gray,
                center: new(0, PanelHeight + 0.05f, -(2 + PanelFarEdge) / 2),
                size: new(6, 0.1f, PanelFarEdge - 2)
            )
        );
        snapshot.Instances.Add(
            Block(box: box, material: gray, center: new(0, 0, -wall - 0.1f), size: new(14, 8, 0.2f))
        );
        return snapshot;
    }

    /// <summary>
    /// A white floor 1.5 m below the eye, a red box, a blue box and a lamp on it, the sun, and sky above the floor's far
    /// edge, seen 12° below the horizontal.
    /// </summary>
    private static RenderSnapshot Scene(VulkanRenderer renderer)
    {
        RenderSnapshot snapshot = Gpu.Snapshot(new(0.4f, 0.8f, 0.3f));
        snapshot.Camera.Rotation = Gpu.PitchDown(MathF.PI / 15);
        snapshot.Camera.PreviousRotation = snapshot.Camera.Rotation;
        MeshId box = renderer.CreateMesh(Primitives.Box(new(0.5f)));
        MaterialId floor = renderer.CreateMaterial(
            MaterialDesc.Color(new(x: 0.8f, y: 0.8f, z: 0.8f, w: 1), roughness: 0.3f)
        );
        MaterialId red = renderer.CreateMaterial(
            MaterialDesc.Color(new(x: 0.8f, y: 0.1f, z: 0.1f, w: 1), roughness: 0.5f)
        );
        MaterialId blue = renderer.CreateMaterial(
            MaterialDesc.Color(new(x: 0.1f, y: 0.2f, z: 0.8f, w: 1), roughness: 0.2f)
        );
        MaterialId lamp = renderer.CreateMaterial(
            MaterialDesc.Color(new(x: 1, y: 1, z: 1, w: 1), roughness: 0.5f) with
            {
                Emissive = new(20, 18, 14),
            }
        );
        snapshot.Instances.Add(Block(box: box, material: floor, center: new(0, -2, -20), size: new(20, 1, 36)));
        snapshot.Instances.Add(Block(box: box, material: red, center: new(-1.5f, -1, -6), size: new(1, 1, 1)));
        snapshot.Instances.Add(Block(box: box, material: blue, center: new(2, -0.5f, -9), size: new(1.2f, 2, 1.2f)));
        var lampSize = new Vector3(0.3f, 0.5f, 0.3f);
        snapshot.Instances.Add(
            Gpu.Instance(
                mesh: box,
                material: lamp,
                position: new(0.3f, -1.25f, -4),
                scale: lampSize,
                radius: lampSize.Length() / 2,
                chainDepth: TestChains.CamerasFrame
            )
        );
        snapshot.Lights.Add(
            Gpu.Lamp(
                position: new(0.3f, -0.8f, -4),
                color: Vector3.One,
                intensity: 4,
                chainDepth: TestChains.CamerasFrame
            )
        );
        return snapshot;
    }

    private static RenderInstance Block(MeshId box, MaterialId material, Vector3 center, Vector3 size) =>
        Gpu.Instance(
            mesh: box,
            material: material,
            position: center,
            scale: size,
            radius: size.Length() / 2,
            chainDepth: TestChains.CamerasFrame
        );
}
