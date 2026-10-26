using System.Numerics;
using static Tertius.Rendering.Tests.ReflectionTesting;

namespace Tertius.Rendering.Tests;

/// <summary>
/// The trace and the cone of reflection_trace.comp pinned to their C# copy (docs/plans/screen-space-reflections.md, "GPU
/// tests of the reflections"): the C# trace over the frame's own distance finds the pixel that the GPU reflects, and the
/// C# cone gives the level of the radiance pyramid that the GPU reads, with the share of the ambient specular term that
/// its coverage keeps. The half-resolution trace holds the same ray for each texel's representative pixel.
/// </summary>
[Collection(GpuCollection.Name)]
public sealed class ReflectionShaderTests
{
    /// <summary>The wall's front face, in m along −Z, its sides and its top. Its bottom stands on the floor.</summary>
    private const float WallFront = -9.5f,
        WallHalfWidth = 6,
        WallTop = 3.5f;

    /// <summary>The radiance of the bright squares of <see cref="CheckeredWall"/>; the dark ones have a tenth of it.</summary>
    private const float BrightSquare = 3;

    /// <summary>
    /// The ambient light of <see cref="CheckeredWall"/>, from the sky and the ground alike: the sky above the wall falls in
    /// the cones near its top, whose coverage then keeps a share of the floor's ambient specular term.
    /// </summary>
    private const float CheckeredGray = 1;

    [Fact]
    public void TheGpuTraceReflectsThePixelThatTheCSharpTraceHits()
    {
        float[] lit = RenderHdr(new NoReflections(), TiledWall).Hdr;
        VulkanRenderer renderer = Headless(Reflections);
        try
        {
            renderer.Render(TiledWall(renderer), 1f);
            float[] reflected = renderer.ReadHdr();
            float[] distance = renderer.ReadDistance();
            DepthPyramid pyramid = PyramidOf(renderer);
            CameraView camera = renderer.LastScene.Camera;

            int compared = 0;
            for (int y = 232; y < Height; y += 4)
            for (int x = 130; x < 510; x += 6)
            {
                (RayTrace trace, _) = TraceFromFloor(
                    camera: camera,
                    distance: distance,
                    pyramid: pyramid,
                    pixel: (x, y),
                    normal: FlatMapNormal,
                    roughness: 0,
                    maxSteps: Reflections.MaxSteps
                );
                // Only a ray with the full fade, whose hit lies inside one tile, so that its trilinear read is the tile.
                if (
                    trace is not RayHit hit
                    || hit.EntryShare != 0
                    || camera.EdgeFade(hit.Position) != 1
                    || !InsideOneTile(lit, hit.Pixel)
                )
                    continue;
                Vector3 expected = SpecularWeight(Mirror(), camera, (x, y)) * At(lit, hit.Pixel);
                Vector3 shown = At(reflected, (x, y));
                BrdfTesting
                    .LargestChannelDifference(shown, expected)
                    .Should()
                    .BeLessThanOrEqualTo(
                        0.02f * expected.Y,
                        "floor pixel ({0}, {1}) reflects the tile of pixel {2}: shown {3}, expected {4}",
                        x,
                        y,
                        hit.Pixel,
                        shown,
                        expected
                    );
                compared++;
            }

            compared.Should().BeGreaterThan(300, "most floor pixels below the wall reflect the inside of a tile");
        }
        finally
        {
            Gpu.DisposeAndAssertClean(renderer);
        }
    }

    [Fact]
    public void EachTexelOfTheHalfResolutionTraceHoldsTheRayOfItsRepresentativePixel()
    {
        float[] lit = RenderHdr(new NoReflections(), TiledWall).Hdr;
        VulkanRenderer renderer = Headless(Reflections with { Resolution = TraceResolution.Half });
        try
        {
            renderer.Render(TiledWall(renderer), 1f);
            (float[] traced, int tracedWidth, int tracedHeight) = renderer.ReadReflectionTrace();
            float[] distance = renderer.ReadDistance();
            DepthPyramid pyramid = PyramidOf(renderer);
            CameraView camera = renderer.LastScene.Camera;

            (tracedWidth, tracedHeight).Should().Be((Width / 2, Height / 2));
            int compared = 0;
            for (int j = 116; j < tracedHeight; j += 2)
            for (int i = 65; i < 255; i += 3)
            {
                // The representative pixel of texel (i, j) is the top-left pixel of its 2 × 2 block.
                (int X, int Y) representative = (X: 2 * i, Y: 2 * j);
                (RayTrace trace, _) = TraceFromFloor(
                    camera: camera,
                    distance: distance,
                    pyramid: pyramid,
                    pixel: representative,
                    normal: FlatMapNormal,
                    roughness: 0,
                    maxSteps: Reflections.MaxSteps
                );
                if (
                    trace is not RayHit hit
                    || hit.EntryShare != 0
                    || camera.EdgeFade(hit.Position) != 1
                    || !InsideOneTile(lit, hit.Pixel)
                )
                    continue;
                Vector3 expected = At(lit, hit.Pixel);
                Vector3 shown = At(traced, (i, j), tracedWidth);
                BrdfTesting
                    .LargestChannelDifference(shown, expected)
                    .Should()
                    .BeLessThanOrEqualTo(
                        0.02f * expected.Y,
                        "texel ({0}, {1}) holds the tile of pixel {2}: shown {3}, expected {4}",
                        i,
                        j,
                        hit.Pixel,
                        shown,
                        expected
                    );
                traced[4 * (j * tracedWidth + i) + 3]
                    .Should()
                    .BeApproximately(1, 1e-3f, "the cone covers only surface");
                compared++;
            }

            compared
                .Should()
                .BeGreaterThan(150, "most texels of the floor below the wall reflect the inside of a tile");
        }
        finally
        {
            Gpu.DisposeAndAssertClean(renderer);
        }
    }

    [Fact]
    public void ATexelWhoseRepresentativePixelStartsNoRayHoldsNoRay()
    {
        VulkanRenderer renderer = Headless(Reflections with { Resolution = TraceResolution.Half });
        try
        {
            renderer.Render(TiledWall(renderer), 1f);
            (float[] traced, int tracedWidth, _) = renderer.ReadReflectionTrace();

            // The sky beside the wall, above the horizon, is no surface, so it starts no ray.
            (int X, int Y) sky = PixelOf(new(-WallHalfWidth - 1, 0.5f, WallFront));
            (int X, int Y) floor = PixelOf(new(0, FloorHeight, -5));
            float CoverageOf((int X, int Y) pixel) => traced[4 * (pixel.Y / 2 * tracedWidth + pixel.X / 2) + 3];
            CoverageOf(sky).Should().BeNegative("a negative coverage marks a texel with no ray");
            CoverageOf(floor).Should().BeInRange(0, 1, "the mirror floor's ray hits the wall or misses");
        }
        finally
        {
            Gpu.DisposeAndAssertClean(renderer);
        }
    }

    [Theory]
    // About 0.3 and 0.5, which the G-buffer's 8 bits hold exactly. At 0.5 the dominant direction leans far from the
    // mirror direction.
    [InlineData(77 / 255f)]
    [InlineData(128 / 255f)]
    public void TheGpuConeReadsTheLevelThatTheCSharpConeGives(float roughness)
    {
        VulkanRenderer renderer = Headless(Reflections);
        try
        {
            renderer.Render(CheckeredWall(renderer, roughness), 1f);
            var frame = ConeFrame.Of(renderer);

            var coneLevels = new List<float>();
            for (int y = 232; y < Height; y += 4)
            for (int x = 130; x < 510; x += 3)
            {
                if (frame.ThroughCone(roughness, (x, y)) is not { } read)
                    continue;
                Vector3 shown = At(frame.Reflected, (x, y));
                BrdfTesting
                    .LargestChannelDifference(shown, read.Expected)
                    .Should()
                    .BeLessThanOrEqualTo(
                        0.03f * read.Weight.Y * BrightSquare,
                        "floor pixel ({0}, {1}) reads the pyramid at level {2}: shown {3}, expected {4}",
                        x,
                        y,
                        read.Level,
                        shown,
                        read.Expected
                    );
                coneLevels.Add(read.Level);
            }

            coneLevels.Count.Should().BeGreaterThan(1000, "most floor pixels below the wall reflect it");
            coneLevels
                .Average()
                .Should()
                .BeInRange(
                    3.5f,
                    8,
                    "the squares have the period of level 5, so the levels around it decide their contrast"
                );
        }
        finally
        {
            Gpu.DisposeAndAssertClean(renderer);
        }
    }

    /// <summary>
    /// A dark scene, so that the metal floor shows only what the trace adds: a mirror floor before a wall of 16 × 8
    /// emissive tiles of 0.75 m by 0.625 m, each of its own color. The red grows across and the green down, so a
    /// reflection of the wrong tile differs by at least 6 % in one channel.
    /// </summary>
    private static RenderSnapshot TiledWall(VulkanRenderer renderer)
    {
        RenderSnapshot snapshot = Dark();
        snapshot.Instances.Add(Floor(renderer, Mirror()));
        MeshId box = renderer.CreateMesh(Primitives.Box(new(0.5f)));
        const float tileWidth = 2 * WallHalfWidth / 16,
            tileHeight = (WallTop - FloorHeight) / 8;
        for (int j = 0; j < 8; j++)
        for (int i = 0; i < 16; i++)
        {
            MaterialId tile = Glow(renderer, 2 * new Vector3(x: (i + 1) / 16f, y: (j + 1) / 8f, z: 0.5f));
            var center = new Vector3(
                x: -WallHalfWidth + tileWidth * (i + 0.5f),
                y: WallTop - tileHeight * (j + 0.5f),
                z: WallFront - 0.5f
            );
            snapshot.Instances.Add(
                Block(box: box, material: tile, center: center, size: new(x: tileWidth, y: tileHeight, z: 1))
            );
        }

        return snapshot;
    }

    /// <summary>
    /// A metal floor of <paramref name="roughness"/> before a checkered wall of squares 0.5 m wide, bright and dark in
    /// turn: 16 pixels each at the wall's distance, so a period of 32 pixels, the texel of level 5 of the radiance
    /// pyramid. The rows make the read depend on the height of the hit, which the ray's direction decides.
    /// </summary>
    private static RenderSnapshot CheckeredWall(VulkanRenderer renderer, float roughness)
    {
        RenderSnapshot snapshot = Gray(CheckeredGray);
        snapshot.Instances.Add(Floor(renderer, Mirror(roughness)));
        MeshId box = renderer.CreateMesh(Primitives.Box(new(0.5f)));
        MaterialId bright = Glow(renderer, new(BrightSquare));
        MaterialId dark = Glow(renderer, new(BrightSquare / 10));
        const float side = 0.5f;
        for (int j = 0; j < (WallTop - FloorHeight) / side; j++)
        for (int i = 0; i < 2 * WallHalfWidth / side; i++)
        {
            var center = new Vector3(
                x: -WallHalfWidth + side * (i + 0.5f),
                y: FloorHeight + side * (j + 0.5f),
                z: WallFront - 0.5f
            );
            snapshot.Instances.Add(
                Block(
                    box: box,
                    material: (i + j) % 2 == 0 ? bright : dark,
                    center: center,
                    size: new(x: side, y: side, z: 1)
                )
            );
        }

        return snapshot;
    }

    /// <summary>
    /// What the C# cone expects at one floor pixel: the value that the GPU shows, the level of the radiance pyramid that
    /// it reads and the floor's specular weight W.
    /// </summary>
    private readonly record struct ConeRead(Vector3 Expected, float Level, Vector3 Weight);

    /// <summary>The images of one frame that the cone test reads back, with the radiance pyramid's levels.</summary>
    private sealed record ConeFrame(
        float[] Reflected,
        float[] Distance,
        DepthPyramid Pyramid,
        CameraView Camera,
        (float[] Rgba, int Width, int Height)[] Levels
    )
    {
        private int TopLevel => Levels.Length - 1;

        public static ConeFrame Of(VulkanRenderer renderer) =>
            new(
                Reflected: renderer.ReadHdr(),
                Distance: renderer.ReadDistance(),
                Pyramid: PyramidOf(renderer),
                Camera: renderer.LastScene.Camera,
                Levels:
                [
                    .. Enumerable.Range(0, BitOperations.Log2(Width) + 1).Select(renderer.ReadRadiancePyramidLevel),
                ]
            );

        /// <summary>
        /// The C# trace and cone from floor pixel <paramref name="pixel"/> of <paramref name="roughness"/>, or null when
        /// its ray misses.
        /// </summary>
        public ConeRead? ThroughCone(float roughness, (int X, int Y) pixel)
        {
            (RayTrace trace, Vector3 point) = TraceFromFloor(
                camera: Camera,
                distance: Distance,
                pyramid: Pyramid,
                pixel: pixel,
                normal: FlatMapNormal,
                roughness: roughness,
                maxSteps: Reflections.MaxSteps
            );
            if (trace is not RayHit hit)
                return null;
            float hitDistance = Distance[hit.Pixel.Y * Width + hit.Pixel.X];
            Vector3 hitPoint = Vector3.Normalize(Camera.ViewRayThrough(hit.Position)) * hitDistance;
            Vector3 origin =
                point + Vector3.UnitY * ScreenRay.OriginOffset(Vector3.Dot(point, Camera.Forward), Camera.PixelAngle);
            float level = ReflectionCone
                .OfRoughness(roughness)
                .Level(
                    rayLength: (hitPoint - origin).Length(),
                    hitDistance: hitDistance,
                    pixelAngle: Camera.PixelAngle,
                    topLevel: TopLevel
                );
            Vector4 traced = Trilinear(Levels, hit.Position / new Vector2(Width, Height), level);
            float fade = Reflections.RoughnessFade(roughness) * Camera.EdgeFade(hit.Position) * (1 - hit.EntryShare);
            Vector3 weight = SpecularWeight(Mirror(roughness), Camera, pixel);
            // Level 0 of the pyramid is the lit floor before the trace, with its ambient specular term W · gray, of
            // which the trace replaces the share that the cone's coverage resolves.
            Vector4 lit = Trilinear(
                Levels,
                new Vector2(pixel.X + 0.5f, pixel.Y + 0.5f) / new Vector2(Width, Height),
                0
            );
            Vector3 expected =
                new Vector3(lit.X, lit.Y, lit.Z)
                + fade * (weight * new Vector3(traced.X, traced.Y, traced.Z) - traced.W * weight * CheckeredGray);
            return new(expected, level, weight);
        }
    }

    /// <summary>Whether the 5 × 5 pixels around <paramref name="pixel"/> all hold one value: the inside of one tile.</summary>
    private static bool InsideOneTile(float[] lit, (int X, int Y) pixel)
    {
        Vector3 center = At(lit, pixel);
        for (int dy = -2; dy <= 2; dy++)
        for (int dx = -2; dx <= 2; dx++)
            if (BrdfTesting.LargestChannelDifference(At(lit, (pixel.X + dx, pixel.Y + dy)), center) > 1e-3f)
                return false;
        return center.Y > 0;
    }

    /// <summary>
    /// A trilinear read of the radiance pyramid's <paramref name="levels"/> at <paramref name="coordinate"/> in 0 to 1 and
    /// <paramref name="level"/>, clamped to the edge, as the trace's sampler reads it.
    /// </summary>
    private static Vector4 Trilinear((float[] Rgba, int Width, int Height)[] levels, Vector2 coordinate, float level)
    {
        int below = (int)MathF.Floor(level);
        int above = Math.Min(below + 1, levels.Length - 1);
        return Vector4.Lerp(Bilinear(levels[below], coordinate), Bilinear(levels[above], coordinate), level - below);
    }

    private static Vector4 Bilinear((float[] Rgba, int Width, int Height) image, Vector2 coordinate)
    {
        float x = coordinate.X * image.Width - 0.5f,
            y = coordinate.Y * image.Height - 0.5f;
        int left = (int)MathF.Floor(x),
            top = (int)MathF.Floor(y);
        Vector4 Texel(int tx, int ty)
        {
            int index = 4 * (Math.Clamp(ty, 0, image.Height - 1) * image.Width + Math.Clamp(tx, 0, image.Width - 1));
            return new(
                x: image.Rgba[index],
                y: image.Rgba[index + 1],
                z: image.Rgba[index + 2],
                w: image.Rgba[index + 3]
            );
        }

        var upper = Vector4.Lerp(Texel(left, top), Texel(left + 1, top), x - left);
        var lower = Vector4.Lerp(Texel(left, top + 1), Texel(left + 1, top + 1), x - left);
        return Vector4.Lerp(upper, lower, y - top);
    }
}
