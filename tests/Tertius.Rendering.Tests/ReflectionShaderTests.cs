using System.Numerics;
using Tertius.Testing;
using static Tertius.Rendering.Tests.ReflectionTesting;

namespace Tertius.Rendering.Tests;

/// <summary>
/// The trace and the cone of reflection_trace.comp pinned to their C# copy (docs/plans/screen-space-reflections.md, "GPU
/// tests of the reflections"): the C# trace over the frame's own distance finds the pixel that the GPU reflects, and the
/// C# cone gives the level of the radiance pyramid that the GPU reads, with the share of the ambient specular term that
/// its coverage keeps. The half-resolution trace holds the same ray for each texel's representative pixel, and the GPU
/// upsample mixes the texels' rays under the weights of the C# upsample.
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

    /// <summary>
    /// How many pixels around a hit the half trace's cone draws on over the mirror floor: its radius stays under 2 pixels,
    /// so it reads at most level 1, whose 13-tap texels, read bilinearly, draw on 5 pixels around the read.
    /// </summary>
    private const int HalfTraceReach = 5;

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
                if (
                    TileHitOf(camera: camera, distance: distance, pyramid: pyramid, lit: lit, pixel: (x, y), reach: 2)
                    is not { } hit
                )
                    continue;
                Vector3 expected = SpecularWeight(Mirror(), camera, (x, y)) * At(lit, hit);
                Vector3 shown = At(reflected, (x, y));
                BrdfTesting
                    .LargestChannelDifference(shown, expected)
                    .Should()
                    .BeLessThanOrEqualTo(
                        0.02f * expected.Y,
                        "floor pixel ({0}, {1}) reflects the tile of pixel {2}: shown {3}, expected {4}",
                        x,
                        y,
                        hit,
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
                if (
                    TileHitOf(
                        camera: camera,
                        distance: distance,
                        pyramid: pyramid,
                        lit: lit,
                        pixel: representative,
                        reach: HalfTraceReach
                    )
                    is not { } hit
                )
                    continue;
                Vector3 expected = At(lit, hit);
                Vector3 shown = At(traced, (i, j), tracedWidth);
                BrdfTesting
                    .LargestChannelDifference(shown, expected)
                    .Should()
                    .BeLessThanOrEqualTo(
                        0.02f * expected.Y,
                        "texel ({0}, {1}) holds the tile of pixel {2}: shown {3}, expected {4}",
                        i,
                        j,
                        hit,
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
    public void EachTexelOfTheHalfResolutionTraceHoldsTheViewDepthOfItsRaysHit()
    {
        VulkanRenderer renderer = Headless(Reflections with { Resolution = TraceResolution.Half });
        try
        {
            renderer.Render(TiledWall(renderer), 1f);
            (float[] traced, int tracedWidth, int tracedHeight) = renderer.ReadReflectionTrace();
            (float[] depths, int depthsWidth, int depthsHeight) = renderer.ReadReflectionHitDepths();

            (depthsWidth, depthsHeight).Should().Be((tracedWidth, tracedHeight));
            // The floor's rays can hit only the wall's front, which faces the eye at one view depth, while its pixels'
            // distances from the eye grow toward its sides and its top. The texels below the row where the wall meets
            // the floor show only the floor.
            int firstFloorTexel = (PixelOf(new(0, FloorHeight, WallFront)).Y / 2 + 1) * depthsWidth;
            int hits = 0;
            for (int texel = firstFloorTexel; texel < depths.Length; texel++)
            {
                if (traced[4 * texel + 3] <= 0)
                    continue;
                depths[texel]
                    .Should()
                    .BeApproximately(
                        -WallFront,
                        1e-3f * -WallFront,
                        "texel ({0}, {1}) hits the wall's front",
                        texel % depthsWidth,
                        texel / depthsWidth
                    );
                hits++;
            }

            hits.Should().BeGreaterThan(5000, "most texels of the floor reflect the wall");
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
    [InlineData(nameof(MirrorWallBehindACube))]
    [InlineData(nameof(MirrorsBeforeTiles))]
    public void EachTexelOfTheHalfTraceHoldsNoRayOrAMissOfZerosOrAHitAtItsDepthWhoseCoverageAHalfFloatHoldsAsANormal(
        string scene
    )
    {
        Func<VulkanRenderer, RenderSnapshot> build = scene switch
        {
            nameof(MirrorWallBehindACube) => MirrorWallBehindACube,
            _ => MirrorsBeforeTiles,
        };
        VulkanRenderer renderer = Headless(ReflectionsAt(TraceResolution.Half));
        try
        {
            renderer.Render(build(renderer), 1f);
            (float[] traced, int tracedWidth, _) = renderer.ReadReflectionTrace();
            float[] depths = renderer.ReadReflectionHitDepths().Depths;

            Vector4 RayOf(int texel) =>
                new(x: traced[4 * texel], y: traced[4 * texel + 1], z: traced[4 * texel + 2], w: traced[4 * texel + 3]);
            int hits = 0;
            var strays = new List<int>();
            for (int texel = 0; texel < depths.Length; texel++)
            {
                Vector4 ray = RayOf(texel);
                float depth = depths[texel];
                bool noRay = ray == -Vector4.UnitW && depth == 0;
                bool miss = ray == Vector4.Zero && depth == 0;
                // A smaller coverage is a half float's denormal, which a driver may flush to 0 on its store or load.
                bool hit = ray.W >= ShaderInput.SmallestNormalHalf && depth > 0;
                if (hit)
                    hits++;
                else if (!noRay && !miss)
                    strays.Add(texel);
            }

            int first = strays.FirstOrDefault();
            strays
                .Should()
                .BeEmpty(
                    "the upsample classes a texel by its coverage's sign alone; texel ({0}, {1}) holds {2} at a depth of {3}",
                    first % tracedWidth,
                    first / tracedWidth,
                    RayOf(first),
                    depths[first]
                );
            hits.Should().BeGreaterThan(100, "the mirrors reflect the glowing surfaces");
        }
        finally
        {
            Gpu.DisposeAndAssertClean(renderer);
        }
    }

    [Fact]
    public void TheGpuUpsampleMixesTheTexelsRaysUnderTheWeightsOfTheCSharpUpsampleOrTracesTheOwnRayThatItChooses()
    {
        float[] lit = RenderHdr(new NoReflections(), MirrorsBeforeTiles).Hdr;
        float[] full = RenderHdr(ReflectionsAt(TraceResolution.Full), MirrorsBeforeTiles).Hdr;
        VulkanRenderer renderer = Headless(ReflectionsAt(TraceResolution.Half));
        try
        {
            renderer.Render(MirrorsBeforeTiles(renderer), 1f);
            float[] shown = renderer.ReadHdr();
            var frame = UpsampleFrame.Of(renderer);

            int compared = 0,
                mixed = 0,
                ownRays = 0;
            for (int y = 0; y < Height; y++)
            for (int x = 0; x < Width; x++)
            {
                // White metal weighs each channel alike, so the color that the upsample adds is its mean ray's.
                Vector3 reflected = At(shown, (x, y)) - At(lit, (x, y));
                if (reflected.X + reflected.Y + reflected.Z < 0.2f)
                    continue;
                // A pixel whose texels' rays hit different surfaces traces its own ray, as the full-resolution trace does.
                UpsampledRay upsampled = frame.Upsampled((x, y));
                if (upsampled.TryGetValue(out OwnRay _))
                {
                    ShouldShowWhatTheFullTraceShows(shown: At(shown, (x, y)), full: At(full, (x, y)), pixel: (x, y));
                    ownRays++;
                    continue;
                }

                ShouldAddTheColorOfTheMean(reflected: reflected, upsampled: upsampled, pixel: (x, y));
                compared++;
                if (frame.TexelsDifferInColor((x, y)))
                    mixed++;
            }

            compared.Should().BeGreaterThan(5000, "the floor, the sphere, the ramp and the slab reflect the tiles");
            mixed.Should().BeGreaterThan(200, "the sphere's rim, the crease and the step mix texels of other colors");
            ownRays
                .Should()
                .BeGreaterThan(
                    100,
                    "where a reflection passes from the wall to the sky or from one surface to another, pixels trace their own rays"
                );
        }
        finally
        {
            Gpu.DisposeAndAssertClean(renderer);
        }
    }

    [Fact]
    public void TheUpsampleHandsEachPixelWhoseOwnRayTheCSharpUpsampleChoosesToTheOwnRayDispatchOnce()
    {
        float[] lit = RenderHdr(new NoReflections(), MirrorsBeforeTiles).Hdr;
        VulkanRenderer renderer = Headless(ReflectionsAt(TraceResolution.Half));
        try
        {
            renderer.Render(MirrorsBeforeTiles(renderer), 1f);
            float[] shown = renderer.ReadHdr();
            var frame = UpsampleFrame.Of(renderer);
            (int X, int Y)[] listed = renderer.ReadOwnRayPixels();

            listed.Should().OnlyHaveUniqueItems("the own-ray dispatch traces each pixel once");
            foreach ((int X, int Y) pixel in listed)
                frame
                    .Upsampled(pixel)
                    .TryGetValue(out OwnRay _)
                    .Should()
                    .BeTrue("listed pixel {0} has texels whose rays disagree", pixel);
            var listedSet = listed.ToHashSet();
            int reflectedOwnRays = 0;
            for (int y = 0; y < Height; y++)
            for (int x = 0; x < Width; x++)
            {
                Vector3 reflected = At(shown, (x, y)) - At(lit, (x, y));
                if (
                    reflected.X + reflected.Y + reflected.Z < 0.2f
                    || !frame.Upsampled((x, y)).TryGetValue(out OwnRay _)
                )
                    continue;
                listedSet.Should().Contain((x, y), "pixel ({0}, {1}) reflects through its own ray", x, y);
                reflectedOwnRays++;
            }

            reflectedOwnRays.Should().BeGreaterThan(100, "the reflections of the wall's outlines trace their own rays");
        }
        finally
        {
            Gpu.DisposeAndAssertClean(renderer);
        }
    }

    [Fact]
    public void TheOwnRayDispatchTracesEveryPixelWhoseOwnRayReflectsAtFullResolution()
    {
        float[] lit = RenderHdr(new NoReflections(), MirrorsBeforeTiles).Hdr;
        float[] full = RenderHdr(ReflectionsAt(TraceResolution.Full), MirrorsBeforeTiles).Hdr;
        VulkanRenderer renderer = Headless(ReflectionsAt(TraceResolution.Half));
        try
        {
            renderer.Render(MirrorsBeforeTiles(renderer), 1f);
            float[] shown = renderer.ReadHdr();
            var frame = UpsampleFrame.Of(renderer);

            int traced = 0;
            for (int y = 0; y < Height; y++)
            for (int x = 0; x < Width; x++)
            {
                // Chosen by the full-resolution render, not by the image under test, so that a pixel which the dispatch
                // skips, and which then shows no reflection, still counts.
                Vector3 reflected = At(full, (x, y)) - At(lit, (x, y));
                if (
                    reflected.X + reflected.Y + reflected.Z < 0.2f
                    || !frame.Upsampled((x, y)).TryGetValue(out OwnRay _)
                )
                    continue;
                ShouldShowWhatTheFullTraceShows(shown: At(shown, (x, y)), full: At(full, (x, y)), pixel: (x, y));
                traced++;
            }

            traced
                .Should()
                .BeGreaterThan(
                    2 * 64,
                    "the reflections of the wall's outlines fill more than two workgroups of the own-ray dispatch"
                );
        }
        finally
        {
            Gpu.DisposeAndAssertClean(renderer);
        }
    }

    [Fact]
    public void AFrameAfterOneWithMoreOwnRaysShowsWhatTheSameFrameShowsInAFreshRenderer()
    {
        float[] fresh = RenderHdr(ReflectionsAt(TraceResolution.Half), CubeBeforeAGlowingWall).Hdr;
        VulkanRenderer renderer = Headless(ReflectionsAt(TraceResolution.Half));
        try
        {
            renderer.Render(TiledWall(renderer), 1f);
            int listedBefore = renderer.ReadOwnRayPixels().Length;
            renderer.Render(CubeBeforeAGlowingWall(renderer), 1f);
            float[] shown = renderer.ReadHdr();
            int listed = renderer.ReadOwnRayPixels().Length;

            // The own-ray list keeps the first frame's entries past the second frame's count: pixels on the outline of the
            // tiled wall's reflection, which reflect the glowing wall in the second frame. A dispatch that read past the
            // count, or a count that the clear did not reset, would add their own rays to the blend of the second frame.
            shown.Should().Equal(fresh, "the own rays have no history");
            listed
                .Should()
                .BeLessThan(listedBefore, "the tiled wall's reflected outline lists more pixels than the cube's");
            (listed % 64)
                .Should()
                .NotBe(0, "the last workgroup of the own-ray dispatch has invocations past the count");
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
    /// <see cref="TiledWall"/> with three white mirrors on its floor, whose texels the upsample weighs apart: a sphere,
    /// whose rim turns its normal fast; a ramp that rises away from the eye by 20° from a crease with the floor 7 m
    /// ahead; and a slab whose top lies 3 cm above the floor, about one texel width at its far edge.
    /// </summary>
    private static RenderSnapshot MirrorsBeforeTiles(VulkanRenderer renderer)
    {
        RenderSnapshot snapshot = TiledWall(renderer);
        MeshId box = renderer.CreateMesh(Primitives.Box(new(0.5f)));
        MaterialId mirror = renderer.CreateMaterial(Mirror());
        snapshot.Instances.Add(
            Gpu.Instance(
                mesh: renderer.CreateMesh(Primitives.Sphere(0.8f)),
                material: mirror,
                position: new(-3, FloorHeight + 0.8f, -6.5f),
                scale: Vector3.One,
                radius: 0.8f,
                chainDepth: TestChains.CamerasFrame
            )
        );
        // The near end of the ramp goes down: its top's near edge lies on the floor.
        var tilt = Quaternion.CreateFromAxisAngle(Vector3.UnitX, 20 * MathF.PI / 180);
        var nearTopEdge = Vector3.Transform(new Vector3(0, 0.05f, 1.5f), tilt);
        snapshot.Instances.Add(
            Gpu.Instance(
                mesh: box,
                material: mirror,
                position: new Vector3(2.5f, FloorHeight, -7) - nearTopEdge,
                scale: new(2.5f, 0.1f, 3),
                rotation: tilt,
                radius: 2,
                chainDepth: TestChains.CamerasFrame
            )
        );
        snapshot.Instances.Add(
            Block(
                box: box,
                material: mirror,
                center: new(-0.5f, FloorHeight + 0.015f, -4.75f),
                size: new(1.5f, 0.03f, 1.5f)
            )
        );
        return snapshot;
    }

    /// <summary>
    /// A dark scene with a mirror floor before a glowing wall 40 m ahead that every floor ray hits, and a glowing cube on the
    /// floor 5 m ahead, left of the center. The own rays lie at the outline of the cube's reflection and along the floor's
    /// seam with the wall, both off the rows of <see cref="TiledWall"/>'s own rays.
    /// </summary>
    private static RenderSnapshot CubeBeforeAGlowingWall(VulkanRenderer renderer)
    {
        RenderSnapshot snapshot = Dark();
        snapshot.Instances.Add(Floor(renderer, Mirror()));
        MeshId box = renderer.CreateMesh(Primitives.Box(new(0.5f)));
        snapshot.Instances.Add(
            Block(box: box, material: Glow(renderer, new(1)), center: new(0, 48.5f, -40.5f), size: new(200, 100, 1))
        );
        snapshot.Instances.Add(
            Block(box: box, material: Glow(renderer, new(3)), center: new(-2, FloorHeight + 0.25f, -5), size: new(0.5f))
        );
        return snapshot;
    }

    /// <summary>
    /// A dark scene with a mirror wall 10 m ahead that faces the eye and fills the view, and a glowing cube 5 m ahead, right
    /// of the center. The wall's rays head back toward the eye and drift outward, so those left of the cube pass behind
    /// it and meet its slab from behind, where the trace's entry share is 1 and the ray's coverage is 0.
    /// </summary>
    private static RenderSnapshot MirrorWallBehindACube(VulkanRenderer renderer)
    {
        RenderSnapshot snapshot = Dark();
        MeshId box = renderer.CreateMesh(Primitives.Box(new(0.5f)));
        snapshot.Instances.Add(
            Block(
                box: box,
                material: renderer.CreateMaterial(Mirror()),
                center: new(0, 0, -10.5f),
                size: new(40, 30, 1)
            )
        );
        snapshot.Instances.Add(
            Block(box: box, material: Glow(renderer, new(2)), center: new(1.5f, 0, -5), size: Vector3.One)
        );
        return snapshot;
    }

    /// <summary>The pixel that traces its own ray shows <paramref name="shown"/>, what the full-resolution trace shows there.</summary>
    private static void ShouldShowWhatTheFullTraceShows(Vector3 shown, Vector3 full, (int X, int Y) pixel) =>
        BrdfTesting
            .LargestChannelDifference(shown, full)
            .Should()
            .BeLessThanOrEqualTo(
                1e-3f * full.Length(),
                "pixel {0} traces its own ray: shown {1}, at full resolution {2}",
                pixel,
                shown,
                full
            );

    /// <summary>
    /// The pixel that blends adds the color <paramref name="reflected"/> of the mean ray that the C# upsample gives it: white
    /// metal weighs each channel alike.
    /// </summary>
    private static void ShouldAddTheColorOfTheMean(Vector3 reflected, UpsampledRay upsampled, (int X, int Y) pixel)
    {
        upsampled
            .TryGetValue(out TexelMean mean)
            .Should()
            .BeTrue("pixel {0} shows a reflection, so a texel keeps a weight", pixel);
        Vector3 expected = Chroma(mean.Mean.Radiance.ToVector3());
        BrdfTesting
            .LargestChannelDifference(Chroma(reflected), expected)
            .Should()
            .BeLessThanOrEqualTo(
                0.01f,
                "pixel {0} shows the mean ray of its texels: shown {1}, expected {2}",
                pixel,
                Chroma(reflected),
                expected
            );
    }

    /// <summary>The share of each channel in the sum of the three.</summary>
    private static Vector3 Chroma(Vector3 radiance) => radiance / (radiance.X + radiance.Y + radiance.Z);

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
            // A flat floor's d moves by about one pixel angle to the next pixel, which the lobe's spread at 0.3 and 0.5
            // passes 30 times over, so f takes p, its floor.
            float level = ReflectionCone
                .Of(roughness: roughness, footprint: Camera.PixelAngle, texelSide: 1)
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

    /// <summary>The images of one frame that the upsample test reads back, and the C# upsample over them.</summary>
    private sealed record UpsampleFrame(
        (float[] Rgba, int Width, int Height) Traced,
        float[] HitDepths,
        float[] Distance,
        float[] Normals,
        CameraView Camera
    )
    {
        public static UpsampleFrame Of(VulkanRenderer renderer) =>
            new(
                Traced: renderer.ReadReflectionTrace(),
                HitDepths: renderer.ReadReflectionHitDepths().Depths,
                Distance: renderer.ReadDistance(),
                Normals: renderer.ReadNormals(),
                Camera: renderer.LastScene.Camera
            );

        private (int Width, int Height) HalfSize => (Traced.Width, Traced.Height);

        /// <summary>What the C# upsample does at <paramref name="pixel"/>, from the GPU's texels.</summary>
        public UpsampledRay Upsampled((int X, int Y) pixel) =>
            ReflectionUpsample.Ray(
                pixel: pixel,
                position: PositionOf(pixel),
                geometricNormal: GeometricNormalOf(pixel),
                texelWidth: 2 * Camera.PixelAngle * Distance[pixel.Y * Width + pixel.X],
                halfSize: HalfSize,
                texelAt: TexelAt
            );

        /// <summary>Whether two texels with a ray that <paramref name="pixel"/> reads differ in color by more than 0.05.</summary>
        public bool TexelsDifferInColor((int X, int Y) pixel)
        {
            Vector3[] colors =
            [
                .. OcclusionUpsample
                    .BlockTexels(pixel, HalfSize)
                    .Select(each => TexelAt(each.Coordinates).Ray)
                    .Select(ray => ray.TryGetValue(out TexelHit hit) ? hit.Result.Radiance : default)
                    .Where(radiance => radiance.X + radiance.Y + radiance.Z > 0.05)
                    .Select(radiance => Chroma(radiance.ToVector3())),
            ];
            return colors.Any(a => colors.Any(b => BrdfTesting.LargestChannelDifference(a, b) > 0.05f));
        }

        private TracedTexel TexelAt((int X, int Y) texel)
        {
            (int X, int Y) representative = (X: 2 * texel.X, Y: 2 * texel.Y);
            int index = 4 * (texel.Y * Traced.Width + texel.X);
            float coverage = Traced.Rgba[index + 3];
            // The classes of reflection_upsample.comp: −1 is no ray, 0 a miss, and a hit has at least 2^-14.
            TexelRay ray = coverage switch
            {
                < -0.5f => new NoRay(),
                <= 0 => new TexelMiss(),
                _ => new TexelHit(
                    Result: new(
                        Radiance: new(Traced.Rgba[index], Traced.Rgba[index + 1], Traced.Rgba[index + 2]),
                        Coverage: coverage
                    ),
                    HitDepth: HitDepths[texel.Y * Traced.Width + texel.X]
                ),
            };
            return new(
                Position: PositionOf(representative),
                GeometricNormal: GeometricNormalOf(representative),
                Ray: ray
            );
        }

        /// <summary>The camera-relative position of a pixel's surface, in world axes, as the upsample computes it.</summary>
        private Double3 PositionOf((int X, int Y) pixel) =>
            Vector3.Normalize(Camera.ViewRayThrough(new(pixel.X + 0.5f, pixel.Y + 0.5f)))
            * Distance[pixel.Y * Width + pixel.X];

        /// <summary>The geometric normal of a pixel, decoded from the G-buffer's octahedral form as octahedral.glsl does.</summary>
        private Double3 GeometricNormalOf((int X, int Y) pixel)
        {
            int index = 4 * (pixel.Y * Width + pixel.X);
            var n = new Vector3(Normals[index + 2], Normals[index + 3], 0);
            n.Z = 1 - MathF.Abs(n.X) - MathF.Abs(n.Y);
            if (n.Z < 0)
                n = new((1 - MathF.Abs(n.Y)) * (n.X >= 0 ? 1 : -1), (1 - MathF.Abs(n.X)) * (n.Y >= 0 ? 1 : -1), n.Z);
            return Vector3.Normalize(n);
        }
    }

    /// <summary>
    /// The pixel that the C# trace from the floor <paramref name="pixel"/> hits, for a mirror, when the ray keeps its
    /// full fade and the hit lies inside one tile, so that a trilinear read there is the tile; otherwise null.
    /// </summary>
    /// <param name="reach">How many pixels around the hit the cone's read draws on, which must all lie in the tile.</param>
    private static (int X, int Y)? TileHitOf(
        CameraView camera,
        float[] distance,
        DepthPyramid pyramid,
        float[] lit,
        (int X, int Y) pixel,
        int reach
    )
    {
        (RayTrace trace, _) = TraceFromFloor(
            camera: camera,
            distance: distance,
            pyramid: pyramid,
            pixel: pixel,
            normal: FlatMapNormal,
            roughness: 0,
            maxSteps: Reflections.MaxSteps
        );
        return
            trace is RayHit hit
            && hit.EntryShare == 0
            && camera.EdgeFade(hit.Position) == 1
            && InsideOneTile(lit: lit, pixel: hit.Pixel, reach: reach)
            ? hit.Pixel
            : null;
    }

    /// <summary>
    /// Whether the pixels up to <paramref name="reach"/> away from <paramref name="pixel"/> along x and y all hold one
    /// value: the inside of one tile.
    /// </summary>
    private static bool InsideOneTile(float[] lit, (int X, int Y) pixel, int reach)
    {
        Vector3 center = At(lit, pixel);
        for (int dy = -reach; dy <= reach; dy++)
        for (int dx = -reach; dx <= reach; dx++)
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
