using System.Numerics;
using Tertius.Testing;

namespace Tertius.Rendering.Tests;

/// <summary>
/// The two pyramids of the screen-space reflections on the GPU (docs/plans/screen-space-reflections.md, "GPU tests of the
/// pyramids"): the depth pyramid against its C# copy, the padding of its image, the radiance pyramid against the lit
/// image and the 13-tap filter, a resize, and the passes that the setting adds. The scene is a floor, two boxes and a
/// lamp under the sky, at 640 × 360, whose odd levels exercise the sizes that round up.
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
        VulkanRenderer renderer = Gpu.Headless(reflections: ConeTracedReflections.Default);
        try
        {
            renderer.Render(Scene(renderer), 1f);
            DepthPyramid expected = ReflectionTesting.PyramidOf(renderer);
            (float[] bounds, int mipWidth, _) = renderer.ReadDepthPyramidLevel(level);

            (int levelWidth, int levelHeight) = DepthPyramid.LevelSize(Width, Height, level);
            for (int y = 0; y < levelHeight; y++)
            for (int x = 0; x < levelWidth; x++)
            {
                CellBounds cell = expected.Bounds(level, x, y);
                int index = 2 * (y * mipWidth + x);
                bounds[index]
                    .Should()
                    .BeApproximately(
                        cell.Near,
                        cell.Near * InverseDepthTolerance,
                        "the near bound of cell ({0}, {1})",
                        x,
                        y
                    );
                bounds[index + 1]
                    .Should()
                    .BeApproximately(
                        cell.Far,
                        cell.Far * InverseDepthTolerance,
                        "the far bound of cell ({0}, {1})",
                        x,
                        y
                    );
            }
        }
        finally
        {
            Gpu.DisposeAndAssertClean(renderer);
        }
    }

    [Fact]
    public void ACellOfOnlySkyHoldsTheEmptyBounds()
    {
        VulkanRenderer renderer = Gpu.Headless(reflections: ConeTracedReflections.Default);
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
        VulkanRenderer renderer = Gpu.Headless(reflections: ConeTracedReflections.Default);
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
    public void RadianceLevelZeroIsTheLitImageTimesTheSurfaceCoverage()
    {
        float[] lit;
        float[] distance;
        VulkanRenderer without = Gpu.Headless(reflections: new NoReflections());
        try
        {
            without.Render(Scene(without), 1f);
            lit = without.ReadHdr();
            distance = without.ReadDistance();
        }
        finally
        {
            Gpu.DisposeAndAssertClean(without);
        }

        VulkanRenderer renderer = Gpu.Headless(reflections: ConeTracedReflections.Default);
        try
        {
            renderer.Render(Scene(renderer), 1f);
            (float[] radiance, int levelWidth, int levelHeight) = renderer.ReadRadiancePyramidLevel(0);

            (levelWidth, levelHeight).Should().Be((Width, Height), "level 0 has the screen's size");
            distance.Should().Contain(d => d > 0).And.Contain(0f, "the scene shows both surfaces and sky");
            for (int pixel = 0; pixel < Width * Height; pixel++)
            {
                float coverage = distance[pixel] > 0 ? 1 : 0;
                for (int channel = 0; channel < 3; channel++)
                    radiance[4 * pixel + channel]
                        .Should()
                        .Be(
                            lit[4 * pixel + channel] * coverage,
                            "pixel {0} holds the lit image times its coverage",
                            pixel
                        );
                radiance[4 * pixel + 3].Should().Be(coverage, "the alpha of pixel {0} is its surface coverage", pixel);
            }
        }
        finally
        {
            Gpu.DisposeAndAssertClean(renderer);
        }
    }

    [Fact]
    public void EachRadianceLevelIsTheThirteenTapDownsampleOfTheLevelBelow()
    {
        VulkanRenderer renderer = Gpu.Headless(reflections: ConeTracedReflections.Default);
        try
        {
            renderer.Render(Scene(renderer), 1f);
            (float[] below, int belowWidth, int belowHeight) = renderer.ReadRadiancePyramidLevel(1);
            (float[] level, int levelWidth, int levelHeight) = renderer.ReadRadiancePyramidLevel(2);

            // Level 1 is 320 × 180, even on both axes, so every tap lies on a texel corner, where a bilinear tap is the
            // mean of four texels whatever the sampler's sub-texel precision.
            (belowWidth, belowHeight)
                .Should()
                .Be((320, 180));
            (levelWidth, levelHeight).Should().Be((160, 90));
            level.Should().Contain(value => value > 0.01f, "the scene's lit surfaces reach level 2");
            for (int y = 0; y < levelHeight; y++)
            for (int x = 0; x < levelWidth; x++)
            {
                Vector4 expected = Jimenez13(
                    rgba: below,
                    width: belowWidth,
                    height: belowHeight,
                    cornerX: 2 * x + 1,
                    cornerY: 2 * y + 1
                );
                for (int channel = 0; channel < 4; channel++)
                {
                    float wanted = expected[channel];
                    level[4 * (y * levelWidth + x) + channel]
                        .Should()
                        .BeApproximately(
                            wanted,
                            2e-3f * Math.Abs(wanted) + 1e-5f,
                            "texel ({0}, {1}), channel {2}",
                            x,
                            y,
                            channel
                        );
                }
            }
        }
        finally
        {
            Gpu.DisposeAndAssertClean(renderer);
        }
    }

    [Fact]
    public void ThePyramidsFollowAResize()
    {
        const int newWidth = 321,
            newHeight = 181;
        VulkanRenderer renderer = Gpu.Headless(reflections: ConeTracedReflections.Default);
        try
        {
            RenderSnapshot snapshot = Scene(renderer);
            renderer.Render(snapshot, 1f);
            renderer.Resize(newWidth, newHeight);
            renderer.Render(snapshot, 1f);
            DepthPyramid expected = ReflectionTesting.PyramidOf(renderer);
            (float[] bounds, int mipWidth, _) = renderer.ReadDepthPyramidLevel(1);
            (_, int radianceWidth, int radianceHeight) = renderer.ReadRadiancePyramidLevel(0);

            (radianceWidth, radianceHeight).Should().Be((newWidth, newHeight), "the radiance pyramid has the new size");
            expected.Width.Should().Be(newWidth);
            (int levelWidth, int levelHeight) = DepthPyramid.LevelSize(newWidth, newHeight, 1);
            for (int y = 0; y < levelHeight; y++)
            for (int x = 0; x < levelWidth; x++)
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

    [Fact]
    public void TheHalfResolutionTraceFollowsAResizeAndRoundsUp()
    {
        VulkanRenderer renderer = Gpu.Headless(
            reflections: ConeTracedReflections.Default with
            {
                Resolution = TraceResolution.Half,
            }
        );
        try
        {
            RenderSnapshot snapshot = Scene(renderer);
            renderer.Render(snapshot, 1f);
            renderer.Resize(321, 181);
            renderer.Render(snapshot, 1f);

            (_, int width, int height) = renderer.ReadReflectionTrace();

            (width, height).Should().Be((161, 91), "each texel stands for a block of 2 × 2 pixels, cut by the edge");
        }
        finally
        {
            Gpu.DisposeAndAssertClean(renderer);
        }
    }

    [Fact]
    public void TheFullResolutionTraceKeepsNoHalfResolutionImage()
    {
        VulkanRenderer renderer = Gpu.Headless(
            reflections: ConeTracedReflections.Default with
            {
                Resolution = TraceResolution.Full,
            }
        );
        try
        {
            renderer.Render(Scene(renderer), 1f);

            FluentActions
                .Invoking(() => renderer.ReadReflectionTrace())
                .Should()
                .Throw<InvalidOperationException>()
                .WithMessage("*full resolution*");
        }
        finally
        {
            Gpu.DisposeAndAssertClean(renderer);
        }
    }

    // ResizeTargets, not Resize: the output of a device whose largest image is 16,384 px would refuse the size first.
    [Fact]
    public void AResizePastTheLongestSideIsRefusedAndKeepsThePyramids()
    {
        VulkanRenderer renderer = Gpu.Headless(reflections: ConeTracedReflections.Default);
        try
        {
            FluentActions
                .Invoking(() => renderer.ResizeTargets(new(16_385, 16)))
                .Should()
                .ThrowExactly<ArgumentOutOfRangeException>()
                .WithMessage("*16384 px a side*");

            renderer.Render(Scene(renderer), 1f);
            (_, int radianceWidth, int radianceHeight) = renderer.ReadRadiancePyramidLevel(0);
            (radianceWidth, radianceHeight).Should().Be((Width, Height), "the refused size destroyed nothing");
        }
        finally
        {
            Gpu.DisposeAndAssertClean(renderer);
        }
    }

    [Fact]
    public void NoReflectionsAddsNoPyramidPass()
    {
        VulkanRenderer renderer = Gpu.Headless(reflections: new NoReflections());
        try
        {
            renderer
                .Graph.Passes.Select(pass => pass.Name)
                .Should()
                .NotContain(
                    ["depth pyramid", "radiance pyramid", "reflections"],
                    "a frame without reflections is the frame from before"
                );
        }
        finally
        {
            Gpu.DisposeAndAssertClean(renderer);
        }
    }

    [Fact]
    public void ReflectionsBuildTheDepthPyramidAfterTheGBufferAndTheRadiancePyramidAfterTheLighting()
    {
        VulkanRenderer renderer = Gpu.Headless(reflections: ConeTracedReflections.Default);
        try
        {
            renderer
                .Graph.Passes.Select(pass => pass.Name)
                .Should()
                .ContainInOrder(["G-buffer", "depth pyramid", "lighting", "radiance pyramid", "transparent"]);
        }
        finally
        {
            Gpu.DisposeAndAssertClean(renderer);
        }
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
        snapshot.Instances.Add(
            Block(box: box, material: lamp, center: new(0.3f, -1.25f, -4), size: new(0.3f, 0.5f, 0.3f))
        );
        snapshot.Lights.Add(Gpu.Lamp(position: new(0.3f, -0.8f, -4), color: Vector3.One, intensity: 4));
        return snapshot;
    }

    private static RenderInstance Block(MeshId box, MaterialId material, Vector3 center, Vector3 size) =>
        Gpu.Instance(mesh: box, material: material, position: center, scale: size, radius: size.Length() / 2);

    /// <summary>
    /// Jimenez's 13-tap downsample of an RGBA level around the texel corner (<paramref name="cornerX"/>,
    /// <paramref name="cornerY"/>), whose taps lie on texel corners: each is the mean of the four texels around it,
    /// clamped to the edge.
    /// </summary>
    private static Vector4 Jimenez13(float[] rgba, int width, int height, int cornerX, int cornerY)
    {
        Vector4 Tap(int dx, int dy)
        {
            Vector4 sum = Vector4.Zero;
            for (int y = cornerY + dy - 1; y <= cornerY + dy; y++)
            for (int x = cornerX + dx - 1; x <= cornerX + dx; x++)
            {
                int index = 4 * (Math.Clamp(y, 0, height - 1) * width + Math.Clamp(x, 0, width - 1));
                sum += new Vector4(x: rgba[index], y: rgba[index + 1], z: rgba[index + 2], w: rgba[index + 3]);
            }

            return sum / 4;
        }

        Vector4 inner = (Tap(-1, -1) + Tap(1, -1) + Tap(-1, 1) + Tap(1, 1)) / 4;
        Vector4 g = Tap(0, 0);
        Vector4 corners =
            (Tap(-2, -2) + Tap(0, -2) + Tap(-2, 0) + g) / 4
            + (Tap(0, -2) + Tap(2, -2) + g + Tap(2, 0)) / 4
            + (Tap(-2, 0) + g + Tap(-2, 2) + Tap(0, 2)) / 4
            + (g + Tap(2, 0) + Tap(0, 2) + Tap(2, 2)) / 4;
        return inner * 0.5f + corners * 0.125f;
    }
}
