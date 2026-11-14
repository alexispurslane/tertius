using System.Numerics;
using Tertius.Testing;

namespace Tertius.Rendering.Tests;

/// <summary>
/// The prefilter of the environment map on the GPU (docs/plans/environment-map.md, "The prefilter"): the specular
/// levels, the open-sky share and the irradiance, against the radiance map they derive from and their C# copies.
/// </summary>
[Collection(GpuCollection.Name)]
public sealed class EnvironmentPrefilterTests
{
    private const int Size = EnvironmentScenes.FaceSize;

    /// <summary>The levels of the specular map at the tests' face size: 32, 16, 8 and 4 texels.</summary>
    private static readonly int Levels = PrefilterLevels.Count(Size);

    /// <summary>Six squares of six colors on the six axes, and a sunlit white wall toward −Z.</summary>
    private static RenderSnapshot Colorful(VulkanRenderer renderer, ushort chainDepth)
    {
        MeshId plane = renderer.CreateMesh(Primitives.Plane(1f));
        RenderSnapshot snapshot = Gpu.Snapshot(starDirection: new(0.3f, 0.5f, 1f), starIrradiance: 3f);
        for (int layer = 0; layer < CubeFaces.Count; layer++)
            snapshot.Instances.Add(
                EnvironmentScenes.Square(
                    plane: plane,
                    material: EnvironmentScenes.Emissive(renderer, 2 * EnvironmentScenes.ColorOf(layer)),
                    center: CubeFaces.Basis((CubeFace)layer).Forward * 10 + new Vector3(1, 2, 0.5f),
                    halfSize: 3,
                    chainDepth: chainDepth
                )
            );
        snapshot.Instances.Add(
            EnvironmentScenes.Square(
                plane: plane,
                material: EnvironmentScenes.Matte(renderer),
                center: new(-4, -3, -8),
                halfSize: 3,
                chainDepth: chainDepth
            )
        );
        return snapshot;
    }

    /// <summary>The six faces of one level of map <paramref name="map"/>, in layer order, each RGBA, rows from the top.</summary>
    private static Half[] AllFaces(VulkanRenderer renderer, int level, EnvironmentImage image, int map = 0)
    {
        var all = new List<Half>();
        for (int layer = 0; layer < CubeFaces.Count; layer++)
            all.AddRange(renderer.ReadEnvironmentFace(map: map, face: (CubeFace)layer, level: level, image: image));
        return [.. all];
    }

    /// <summary>
    /// <see cref="Colorful"/> seen from a camera whose chain holds three frames, each turned from the one below, so that
    /// each of the three maps holds the same scene in its own axes. Every object is in the root, so every map draws it.
    /// </summary>
    private static RenderSnapshot ColorfulInThreeFrames(VulkanRenderer renderer)
    {
        RenderSnapshot snapshot = Colorful(renderer, TestChains.Root);
        TestChains.Of(
            snapshot,
            (2, Quaternion.Identity),
            (1, Quaternion.CreateFromYawPitchRoll(0.6f, 0.2f, 0f)),
            (0, Quaternion.CreateFromYawPitchRoll(-0.4f, 1.1f, 0.3f))
        );
        return snapshot;
    }

    /// <summary>A headless renderer with a map of <paramref name="faceSize"/> texels, 160 × 90.</summary>
    private static VulkanRenderer Renderer(int faceSize) =>
        Gpu.Headless(width: 160, height: 90, environmentMap: SceneEnvironmentMap.Default with { FaceSize = faceSize });

    /// <summary>
    /// Every level of map <paramref name="map"/>'s cube in <paramref name="image"/>, the radiance map or the distance cube,
    /// then each face in layer order, rows from the top: RGBA for the radiance map, one inverse distance for the distance
    /// cube.
    /// </summary>
    private static Half[][][] DrawnLevels(
        VulkanRenderer renderer,
        int faceSize,
        EnvironmentImage image = EnvironmentImage.Radiance,
        int map = 0
    )
    {
        var levels = new Half[PrefilterLevels.RadianceLevels(faceSize)][][];
        for (int level = 0; level < levels.Length; level++)
        {
            levels[level] = new Half[CubeFaces.Count][];
            for (int layer = 0; layer < CubeFaces.Count; layer++)
                levels[level][layer] = renderer.ReadEnvironmentFace(
                    map: map,
                    face: (CubeFace)layer,
                    level: level,
                    image: image
                );
        }

        return levels;
    }

    /// <summary>The mean of the four children of texel (x, y) of a face of <paramref name="size"/>, by their solid angles.</summary>
    private static Vector4 ChildrenMean(Half[] children, int size, int x, int y)
    {
        Vector4 light = Vector4.Zero;
        double solidAngle = 0;
        for (int dy = 0; dy < 2; dy++)
        for (int dx = 0; dx < 2; dx++)
        {
            double childAngle = CubeFaces.TexelSolidAngle(2 * x + dx, 2 * y + dy, 2 * size);
            light +=
                EnvironmentScenes.Texel(face: children, size: 2 * size, x: 2 * x + dx, y: 2 * y + dy)
                * (float)childAngle;
            solidAngle += childAngle;
        }

        return light / (float)solidAngle;
    }

    /// <summary>
    /// The mean of the four children of texel (x, y) of a face of <paramref name="size"/> in the distance cube, by their
    /// solid angles: the rule of <see cref="ChildrenMean"/>, for one channel.
    /// </summary>
    private static double DistanceChildrenMean(Half[] children, int size, int x, int y)
    {
        double sum = 0;
        double solidAngle = 0;
        for (int dy = 0; dy < 2; dy++)
        for (int dx = 0; dx < 2; dx++)
        {
            double childAngle = CubeFaces.TexelSolidAngle(2 * x + dx, 2 * y + dy, 2 * size);
            sum += (float)children[(2 * y + dy) * 2 * size + 2 * x + dx] * childAngle;
            solidAngle += childAngle;
        }

        return sum / solidAngle;
    }

    /// <summary>
    /// The light of one level of the specular map, Σ radiance × solid angle over every texel, by luminance, and its
    /// light-weighted mean angle from <paramref name="axis"/>, in rad.
    /// </summary>
    private static (double Light, double MeanAngle) Spread(VulkanRenderer renderer, int level, Vector3 axis)
    {
        int size = Size >> level;
        double light = 0;
        double angle = 0;
        for (int layer = 0; layer < CubeFaces.Count; layer++)
        {
            Half[] face = renderer.ReadEnvironmentFace(
                map: 0,
                face: (CubeFace)layer,
                level: level,
                image: EnvironmentImage.Specular
            );
            for (int y = 0; y < size; y++)
            for (int x = 0; x < size; x++)
            {
                Vector4 texel = EnvironmentScenes.Texel(face: face, size: size, x: x, y: y);
                double luminance = 0.2126 * texel.X + 0.7152 * texel.Y + 0.0722 * texel.Z;
                double share = luminance * CubeFaces.TexelSolidAngle(x, y, size);
                Vector3 direction = CubeFaces.Direction((CubeFace)layer, (x + 0.5f) / size, (y + 0.5f) / size);
                light += share;
                angle += share * Math.Acos(Math.Clamp(Vector3.Dot(direction, axis), -1, 1));
            }
        }

        return (light, angle / light);
    }

    /// <summary>
    /// Every texel of each rough level is Karis's sum over the level's samples, each a trilinear read of the radiance map at
    /// the level that filtered importance sampling gives: the result of <see cref="PrefilterReference"/>, which reads the
    /// radiance map that the GPU drew. So however the shader splits a texel's samples among its invocations, it takes each
    /// sample once, in its own direction, weight and level. At 128 texels the levels take two dispatches, so the second
    /// dispatch writes its own levels with their own lanes.
    /// </summary>
    [Theory]
    [InlineData(Size)]
    [InlineData(128)]
    public void EveryTexelOfARoughLevelIsTheSumOverItsSamplesOfTheRadianceMap(int faceSize)
    {
        VulkanRenderer renderer = Renderer(faceSize);
        int levels = PrefilterLevels.Count(faceSize);
        try
        {
            renderer.Render(Colorful(renderer, TestChains.CamerasFrame), 1f);

            var reference = new PrefilterReference(DrawnLevels(renderer: renderer, faceSize: faceSize), faceSize);
            // Level 0 is the mirror level, which TheMirrorLevelIsTheRadianceMap holds.
            float[] worstOfLevel = new float[levels];
            for (int level = 1; level < levels; level++)
            {
                int size = faceSize >> level;
                float roughness = PrefilterLevels.Roughness(level, levels);
                float worst = 0;
                for (int layer = 0; layer < CubeFaces.Count; layer++)
                {
                    Half[] face = renderer.ReadEnvironmentFace(
                        map: 0,
                        face: (CubeFace)layer,
                        level: level,
                        image: EnvironmentImage.Specular
                    );
                    for (int y = 0; y < size; y++)
                    for (int x = 0; x < size; x++)
                    {
                        Vector3 n = CubeFaces.Direction((CubeFace)layer, (x + 0.5f) / size, (y + 0.5f) / size);
                        Vector4 expected = reference.Prefilter(n, roughness);
                        Vector4 read = EnvironmentScenes.Texel(face: face, size: size, x: x, y: y);
                        worst = MathF.Max(worst, (read - expected).Length() / (1 + expected.Length()));
                    }
                }

                worstOfLevel[level] = worst;
            }

            // Within 0.35 % of a texel's light: the GPU's filter weights and the halves of the stored levels.
            worstOfLevel[1..]
                .Should()
                .OnlyContain(worst => worst < 3.5e-3f, "each level's texels sum over their samples");
        }
        finally
        {
            Gpu.DisposeAndAssertClean(renderer);
        }
    }

    /// <summary>
    /// Every texel of every mip of the radiance map is the mean of its four children in the level above, by their exact
    /// solid angles. At 32 texels the chain takes two dispatches and at 128 three, so a dispatch makes each of its levels
    /// from the one above, and reads the last level that the dispatch before made.
    /// </summary>
    [Theory]
    [InlineData(Size)]
    [InlineData(128)]
    public void EveryMipTexelIsTheMeanOfItsChildrenByTheirSolidAngles(int faceSize)
    {
        VulkanRenderer renderer = Renderer(faceSize);
        try
        {
            renderer.Render(Colorful(renderer, TestChains.CamerasFrame), 1f);

            Half[][][] mips = DrawnLevels(renderer: renderer, faceSize: faceSize);
            float[] worstOfLevel = new float[mips.Length];
            for (int level = 1; level < mips.Length; level++)
            {
                int size = faceSize >> level;
                for (int layer = 0; layer < CubeFaces.Count; layer++)
                for (int y = 0; y < size; y++)
                for (int x = 0; x < size; x++)
                {
                    Vector4 expected = ChildrenMean(children: mips[level - 1][layer], size: size, x: x, y: y);
                    Vector4 read = EnvironmentScenes.Texel(face: mips[level][layer], size: size, x: x, y: y);
                    float error = (read - expected).Length() / (1 + expected.Length());
                    worstOfLevel[level] = MathF.Max(worstOfLevel[level], error);
                }
            }

            // Within 0.1 % of a texel's light: the halves of the stored levels.
            worstOfLevel[1..].Should().OnlyContain(worst => worst < 1e-3f, "each mip texel is its children's mean");
        }
        finally
        {
            Gpu.DisposeAndAssertClean(renderer);
        }
    }

    /// <summary>
    /// Every texel of every level of the distance cube is the mean of its four children's inverse distances, by their exact
    /// solid angles: the rule of the radiance map's mips (docs/plans/environment-map-distances.md, "The level chain"). Each
    /// of three maps holds the scene in its own axes, so each makes its levels from its own faces. At 32 texels the chain
    /// takes two dispatches and at 128 three.
    /// </summary>
    [Theory]
    [InlineData(Size)]
    [InlineData(128)]
    public void EachLevelOfTheDistanceCubeIsTheSolidAngleMeanOfItsChildren(int faceSize)
    {
        VulkanRenderer renderer = Renderer(faceSize);
        try
        {
            renderer.Render(ColorfulInThreeFrames(renderer), 1f);

            float[] worstOfMap = new float[3];
            float[] nearest = new float[worstOfMap.Length];
            for (int map = 0; map < worstOfMap.Length; map++)
            {
                Half[][][] levels = DrawnLevels(
                    renderer: renderer,
                    faceSize: faceSize,
                    image: EnvironmentImage.Distance,
                    map: map
                );
                nearest[map] = levels[0].SelectMany(face => face).Max(distance => (float)distance);
                for (int level = 1; level < levels.Length; level++)
                {
                    int size = faceSize >> level;
                    for (int layer = 0; layer < CubeFaces.Count; layer++)
                    for (int y = 0; y < size; y++)
                    for (int x = 0; x < size; x++)
                    {
                        double expected = DistanceChildrenMean(
                            children: levels[level - 1][layer],
                            size: size,
                            x: x,
                            y: y
                        );
                        double read = (float)levels[level][layer][y * size + x];
                        worstOfMap[map] = MathF.Max(
                            worstOfMap[map],
                            (float)(Math.Abs(read - expected) / (1 + expected))
                        );
                    }
                }
            }

            nearest.Should().OnlyContain(w => w > 50f, "each map's faces hold the squares about 10 m away");
            // Within 0.1 % of a texel's inverse distance: the halves of the stored levels.
            worstOfMap.Should().OnlyContain(worst => worst < 1e-3f, "each texel is its children's mean");
        }
        finally
        {
            Gpu.DisposeAndAssertClean(renderer);
        }
    }

    /// <summary>
    /// A texel of level 1 whose two upper children see a wall 4 m away and whose two lower children see the sky holds the
    /// mean of their inverse distances, half the wall's, because the sky's is 0 (docs/plans/environment-map-distances.md,
    /// "The level chain", design A). The wall's lower edge lies on the line between rows 14 and 15 of face +X.
    /// </summary>
    [Fact]
    public void ATexelHalfWallAndHalfSkyHoldsHalfTheWallsInverseDistance()
    {
        VulkanRenderer renderer = EnvironmentScenes.Renderer();
        try
        {
            MeshId plane = renderer.CreateMesh(Primitives.Plane(1f));
            RenderSnapshot snapshot = EnvironmentScenes.Dark();
            // Row r of face +X looks up by 1 − (2r + 1) / 32 of the distance ahead, so the line between rows 14 and 15
            // lies 1 / 16 of 4 m up: the wall spans y = 0.25 m to 12.25 m and z = −6 m to 6 m on the plane x = 4 m.
            const float halfSize = 6;
            snapshot.Instances.Add(
                EnvironmentScenes.Facing(
                    plane: plane,
                    material: EnvironmentScenes.Matte(renderer),
                    center: new(4, 0.25f + halfSize, 0),
                    normal: -Vector3.UnitX,
                    halfSize: halfSize,
                    chainDepth: TestChains.CamerasFrame
                )
            );

            renderer.Render(snapshot, 1f);

            Half[] level1 = EnvironmentScenes.Distances(renderer, CubeFace.PositiveX, level: 1);
            const int row = 7;
            float worst = 0;
            double worstShare = 0;
            for (int x = 0; x < Size / 2; x++)
            {
                (double expected, double wallMean) = HalfWallTexel(x: x, row: row);
                float read = (float)level1[row * Size / 2 + x];
                worst = MathF.Max(worst, (float)(Math.Abs(read - expected) / expected));
                worstShare = Math.Max(worstShare, Math.Abs(read / wallMean - 0.5));
            }

            // Two roundings to a half float, the face's and the level's, each within one unit in the last place, 2⁻¹⁰ of
            // the value at most: Vulkan lets a device round toward zero.
            worst
                .Should()
                .BeLessThan(2e-3f, "each texel is the solid-angle mean of two wall children and two of sky");
            worstShare.Should().BeLessThan(0.01, "the wall's half of the texel holds about half its solid angle");
        }
        finally
        {
            Gpu.DisposeAndAssertClean(renderer);
        }
    }

    /// <summary>
    /// Texel (<paramref name="x"/>, <paramref name="row"/>) of level 1 of face +X under a wall on the plane x = 4 m whose
    /// lower edge lies between the rows of its children: the solid-angle mean of its children, the upper two on the wall
    /// and the lower two on the sky, and the solid-angle mean of the two wall children alone.
    /// </summary>
    private static (double Expected, double WallMean) HalfWallTexel(int x, int row)
    {
        double wall = 0;
        double wallAngle = 0;
        double solidAngle = 0;
        for (int dy = 0; dy < 2; dy++)
        for (int dx = 0; dx < 2; dx++)
        {
            (int childX, int childY) = (2 * x + dx, 2 * row + dy);
            double childAngle = CubeFaces.TexelSolidAngle(childX, childY, Size);
            Vector3 direction = CubeFaces.Direction(CubeFace.PositiveX, (childX + 0.5f) / Size, (childY + 0.5f) / Size);
            // The upper child sees the wall, 4 m / direction.X away, and the lower one the sky, whose w is 0.
            if (dy == 0)
            {
                wall += 1000.0 * direction.X / 4 * childAngle;
                wallAngle += childAngle;
            }

            solidAngle += childAngle;
        }

        return (wall / solidAngle, wall / wallAngle);
    }

    [Fact]
    public void TheMirrorLevelIsTheRadianceMap()
    {
        VulkanRenderer renderer = EnvironmentScenes.Renderer();
        try
        {
            renderer.Render(Colorful(renderer, TestChains.CamerasFrame), 1f);

            Half[] radiance = AllFaces(renderer: renderer, level: 0, image: EnvironmentImage.Radiance);
            Half[] mirror = AllFaces(renderer: renderer, level: 0, image: EnvironmentImage.Specular);

            radiance.Should().Contain(texel => (float)texel > 0.1f, "the faces hold the squares and the wall");
            mirror.Should().Equal(radiance, "a lobe of roughness 0 reads the radiance map texel for texel");
        }
        finally
        {
            Gpu.DisposeAndAssertClean(renderer);
        }
    }

    [Fact]
    public void AUniformMapStaysUniformAtEveryLevel()
    {
        VulkanRenderer renderer = EnvironmentScenes.Renderer();
        try
        {
            MeshId plane = renderer.CreateMesh(Primitives.Plane(1f));
            RenderSnapshot snapshot = EnvironmentScenes.Dark();
            EnvironmentScenes.AddEnclosure(
                snapshot: snapshot,
                plane: plane,
                material: EnvironmentScenes.Emissive(renderer, Vector3.One),
                distance: 5,
                chainDepth: TestChains.CamerasFrame
            );

            renderer.Render(snapshot, 1f);

            for (int level = 0; level < Levels; level++)
            {
                Half[] faces = AllFaces(renderer: renderer, level: level, image: EnvironmentImage.Specular);
                for (int i = 0; i < faces.Length; i++)
                    ((float)faces[i])
                        .Should()
                        .BeApproximately(
                            i % 4 == 3 ? 0f : 1f,
                            2e-3f,
                            "the weights of level {0} add up to one, and no sky shows",
                            level
                        );
            }
        }
        finally
        {
            Gpu.DisposeAndAssertClean(renderer);
        }
    }

    /// <summary>
    /// A spot at the center of the +Z face, where a texel spans the most solid angle, halfway out along the face's
    /// diagonal, and toward a corner of the cube, where a texel spans the least. Its direction is (x, y, 1).
    /// </summary>
    [Theory]
    [InlineData(0f, 0f)]
    [InlineData(0.5f, 0.5f)]
    [InlineData(0.8f, 0.8f)]
    public void ABrightSpotSpreadsWiderAtEachRougherLevelAndKeepsItsLightWhereverItLies(float x, float y)
    {
        VulkanRenderer renderer = EnvironmentScenes.Renderer();
        try
        {
            MeshId plane = renderer.CreateMesh(Primitives.Plane(1f));
            RenderSnapshot snapshot = EnvironmentScenes.Dark();
            var axis = Vector3.Normalize(new(x, y, 1));
            snapshot.Instances.Add(
                EnvironmentScenes.Facing(
                    plane: plane,
                    material: EnvironmentScenes.Emissive(renderer, new(100)),
                    center: 10 * axis,
                    normal: -axis,
                    halfSize: 1,
                    chainDepth: TestChains.CamerasFrame
                )
            );

            renderer.Render(snapshot, 1f);

            (double light, double angle) mirror = Spread(renderer: renderer, level: 0, axis: axis);
            mirror.light.Should().BeGreaterThan(1, "the spot covers about 0.04 sr at a radiance of 100");
            double previous = mirror.angle;
            for (int level = 1; level < Levels; level++)
            {
                (double light, double angle) = Spread(renderer: renderer, level: level, axis: axis);
                light
                    .Should()
                    .BeApproximately(mirror.light, 0.05 * mirror.light, "level {0} keeps the spot's light", level);
                angle.Should().BeGreaterThan(previous, "the lobe of level {0} is wider than the one before", level);
                previous = angle;
            }
        }
        finally
        {
            Gpu.DisposeAndAssertClean(renderer);
        }
    }

    /// <summary>
    /// A strip of emission 1 on the plane z = 10, where the +Z face's coordinates are x / 10 and y / 10. It is 0.6 texels
    /// wide around the centers of <paramref name="column"/> of a face of <paramref name="size"/>, and runs from the face's
    /// middle row down past its corner.
    /// </summary>
    private static RenderSnapshot StripDownColumn(VulkanRenderer renderer, int column, int size)
    {
        const float distance = 10;
        float u = 2f * (column + 0.5f) / size - 1;
        RenderSnapshot snapshot = EnvironmentScenes.Dark();
        snapshot.Instances.Add(
            Gpu.Instance(
                mesh: renderer.CreateMesh(Primitives.Plane(1f)),
                material: EnvironmentScenes.Emissive(renderer, Vector3.One),
                position: new(u * distance, -0.6f * distance, distance),
                scale: new(0.6f / size * distance, 1, 0.6f * distance),
                rotation: EnvironmentScenes.FrontToward(-Vector3.UnitZ),
                chainDepth: TestChains.CamerasFrame
            )
        );
        return snapshot;
    }

    /// <summary>The share of the solid angle of mip texel (x / 2, y) that its two children in column x cover.</summary>
    private static double LeftChildrenShare(int x, int y, int size)
    {
        double left = CubeFaces.TexelSolidAngle(x, 2 * y, size) + CubeFaces.TexelSolidAngle(x, 2 * y + 1, size);
        double right =
            CubeFaces.TexelSolidAngle(x + 1, 2 * y, size) + CubeFaces.TexelSolidAngle(x + 1, 2 * y + 1, size);
        return left / (left + right);
    }

    [Fact]
    public void OnTheLargestMapAMipTexelWeightsItsChildrenByTheirExactSolidAnglesUpToTheCorner()
    {
        const int size = EnvironmentMapSetting.MaxFaceSize;
        const int column = size - 2;
        VulkanRenderer renderer = Gpu.Headless(
            width: 160,
            height: 90,
            environmentMap: SceneEnvironmentMap.Default with
            {
                FaceSize = size,
            }
        );
        try
        {
            // The strip lights the left half of each mip texel along the column's lower half.
            renderer.Render(StripDownColumn(renderer: renderer, column: column, size: size), 1f);

            Half[] children = renderer.ReadEnvironmentFace(
                map: 0,
                face: CubeFace.PositiveZ,
                level: 0,
                image: EnvironmentImage.Radiance
            );
            Half[] parents = renderer.ReadEnvironmentFace(
                map: 0,
                face: CubeFace.PositiveZ,
                level: 1,
                image: EnvironmentImage.Radiance
            );
            for (int y = size / 4; y < size / 2; y++)
            {
                float lit = EnvironmentScenes.Texel(face: children, size: size, x: column, y: 2 * y).X;
                float dark = EnvironmentScenes.Texel(face: children, size: size, x: column + 1, y: 2 * y).X;
                lit.Should().BeGreaterThan(0.5f, "the strip covers the column's texel centers");
                dark.Should().Be(0, "the strip misses the column to its right");
                float parent = EnvironmentScenes.Texel(face: parents, size: size / 2, x: column / 2, y: y).X;
                parent
                    .Should()
                    .BeApproximately(
                        (float)(lit * LeftChildrenShare(x: column, y: y, size: size)),
                        1e-3f,
                        "the mip texel of row {0} is its children's mean by their exact solid angles",
                        y
                    );
            }
        }
        finally
        {
            Gpu.DisposeAndAssertClean(renderer);
        }
    }

    /// <summary>
    /// On the largest map the first dispatch of the specular map spreads its 172,032 workgroups over three rows
    /// (<see cref="ComputeLimits.Grid"/>), so each workgroup rebuilds its place from its row and column. Level 0, whose
    /// workgroups fill the first row and part of the second, is the radiance map texel for texel. Levels 1 to 3, whose
    /// workgroups lie in the second and third rows, hold at texels spread over every face the sum over their samples.
    /// </summary>
    [Fact]
    public void OnTheLargestMapEveryWorkgroupOfTheGridsRowsWritesItsOwnTexels()
    {
        const int faceSize = EnvironmentMapSetting.MaxFaceSize;
        VulkanRenderer renderer = Renderer(faceSize);
        int levels = PrefilterLevels.Count(faceSize);
        try
        {
            renderer.Render(Colorful(renderer, TestChains.CamerasFrame), 1f);

            Half[] radiance = AllFaces(renderer: renderer, level: 0, image: EnvironmentImage.Radiance);
            Half[] mirror = AllFaces(renderer: renderer, level: 0, image: EnvironmentImage.Specular);
            int differing = 0;
            for (int i = 0; i < radiance.Length; i++)
                if (!radiance[i].Equals(mirror[i]))
                    differing++;
            differing.Should().Be(0, "the mirror level reads the radiance map texel for texel");

            var reference = new PrefilterReference(DrawnLevels(renderer: renderer, faceSize: faceSize), faceSize);
            for (int level = 1; level < PrefilterLevels.LevelsPerDispatch; level++)
            {
                int size = faceSize >> level;
                float roughness = PrefilterLevels.Roughness(level, levels);
                float worst = 0;
                for (int layer = 0; layer < CubeFaces.Count; layer++)
                {
                    Half[] face = renderer.ReadEnvironmentFace(
                        map: 0,
                        face: (CubeFace)layer,
                        level: level,
                        image: EnvironmentImage.Specular
                    );
                    // About 200 texels spread over each face, in row order.
                    int stride = Math.Max(1, size * size / 200 + 1);
                    for (int texel = 0; texel < size * size; texel += stride)
                    {
                        int x = texel % size;
                        int y = texel / size;
                        Vector3 n = CubeFaces.Direction((CubeFace)layer, (x + 0.5f) / size, (y + 0.5f) / size);
                        Vector4 expected = reference.Prefilter(n, roughness);
                        Vector4 read = EnvironmentScenes.Texel(face: face, size: size, x: x, y: y);
                        worst = MathF.Max(worst, (read - expected).Length() / (1 + expected.Length()));
                    }
                }

                worst.Should().BeLessThan(3.5e-3f, "level {0}'s workgroups write their own texels", level);
            }
        }
        finally
        {
            Gpu.DisposeAndAssertClean(renderer);
        }
    }

    /// <summary>The six faces of level 0 of map 0's irradiance cube, in layer order, each RGBA, rows from the top.</summary>
    private static Half[] CubeFaces0(VulkanRenderer renderer) =>
        AllFaces(renderer: renderer, level: 0, image: EnvironmentImage.Irradiance);

    /// <summary>The unit direction through the center of texel <paramref name="texel"/> of the cube's level 0, in layer, row and column order.</summary>
    private static Vector3 CubeTexelDirection(int texel)
    {
        const int size = IrradianceCube.FaceSize;
        int layer = texel / (size * size);
        int x = texel % size;
        int y = texel / size % size;
        return CubeFaces.Direction((CubeFace)layer, (x + 0.5f) / size, (y + 0.5f) / size);
    }

    /// <summary>
    /// Level 0 of map <paramref name="map"/>'s irradiance cube is the C# copy's convolution of the map's source level, in
    /// every texel and all four channels, within the rounding of half floats, about 2^-11 of a value.
    /// </summary>
    private static void CubeShouldBeTheConvolution(VulkanRenderer renderer, int faceSize, int map, string situation)
    {
        Half[] source = AllFaces(
            renderer: renderer,
            level: IrradianceCube.SourceLevel(faceSize),
            image: EnvironmentImage.Radiance,
            map: map
        );
        Half[] cube = AllFaces(renderer: renderer, level: 0, image: EnvironmentImage.Irradiance, map: map);
        int sourceSize = Math.Min(faceSize, IrradianceCube.SourceFaceSize);

        cube.Length.Should().Be(CubeFaces.Count * IrradianceCube.FaceSize * IrradianceCube.FaceSize * 4);
        for (int texel = 0; texel < cube.Length / 4; texel++)
        {
            Vector4 expected = IrradianceCube.Convolve(source, sourceSize, CubeTexelDirection(texel));
            var read = new Vector4(
                x: (float)cube[4 * texel],
                y: (float)cube[4 * texel + 1],
                z: (float)cube[4 * texel + 2],
                w: (float)cube[4 * texel + 3]
            );
            (read - expected)
                .Length()
                .Should()
                .BeLessThan(
                    1e-3f * (1 + expected.Length()),
                    "texel {0} of map {1} is the C# copy's convolution {2}",
                    texel,
                    map,
                    situation
                );
        }
    }

    [Fact]
    public void TheIrradianceCubeIsTheConvolutionOfTheMapsLevelOf32Texels()
    {
        const int faceSize = 128;
        VulkanRenderer renderer = Renderer(faceSize);
        try
        {
            renderer.Render(Colorful(renderer, TestChains.CamerasFrame), 1f);

            CubeShouldBeTheConvolution(renderer: renderer, faceSize: faceSize, map: 0, situation: "at level 2");
        }
        finally
        {
            Gpu.DisposeAndAssertClean(renderer);
        }
    }

    /// <summary>
    /// The smallest face size, which convolves its own level 0 of 16 texels, and the largest, which convolves its level 5.
    /// </summary>
    [Theory]
    [InlineData(EnvironmentMapSetting.MinFaceSize)]
    [InlineData(EnvironmentMapSetting.MaxFaceSize)]
    public void AtEveryFaceSizeTheIrradianceCubeIsTheConvolutionOfItsSourceLevel(int size)
    {
        VulkanRenderer renderer = Renderer(size);
        try
        {
            renderer.Render(Colorful(renderer, TestChains.CamerasFrame), 1f);

            CubeShouldBeTheConvolution(renderer: renderer, faceSize: size, map: 0, situation: $"at face size {size}");
        }
        finally
        {
            Gpu.DisposeAndAssertClean(renderer);
        }
    }

    /// <summary>
    /// The cube of each frame is the convolution of that frame's map, frame after frame: nothing of one frame's cube carries
    /// into the next. The six faces are drawn in every frame, under a sun that moves between frames, so each frame's map
    /// differs from the one before.
    /// </summary>
    [Fact]
    public void EachFramesIrradianceCubeIsTheConvolutionOfThatFramesMap()
    {
        VulkanRenderer renderer = EnvironmentScenes.Renderer(facesPerFrame: CubeFaces.Count);
        try
        {
            RenderSnapshot colorful = Colorful(renderer, TestChains.CamerasFrame);
            RenderSnapshot moved = Gpu.Snapshot(starDirection: new(-0.6f, 0.2f, 0.4f), starIrradiance: 1f);
            moved.Instances.AddRange(colorful.Instances);
            Vector3? before = null;
            int frame = 0;
            foreach (RenderSnapshot snapshot in (RenderSnapshot[])[colorful, moved, colorful])
            {
                renderer.Render(snapshot, 1f);

                CubeShouldBeTheConvolution(renderer: renderer, faceSize: Size, map: 0, situation: $"in frame {frame}");
                Vector3 mean = renderer.ReadEnvironmentIrradiance(map: 0).MeanRadiance;
                if (before is { } last)
                    Vector3.Distance(last, mean).Should().BeGreaterThan(1e-3f, "the sun's move changes the map");
                before = mean;
                frame++;
            }
        }
        finally
        {
            Gpu.DisposeAndAssertClean(renderer);
        }
    }

    /// <summary>
    /// Each map of a chain of three has the cube of its own faces, in its own layers: the maps take turns with the cube's
    /// levels set, and no map's convolution reads or writes another's layers.
    /// </summary>
    [Fact]
    public void EachMapsIrradianceCubeIsTheConvolutionOfItsOwnFaces()
    {
        VulkanRenderer renderer = EnvironmentScenes.Renderer();
        try
        {
            renderer.Render(ColorfulInThreeFrames(renderer), 1f);

            for (int map = 0; map < 3; map++)
                CubeShouldBeTheConvolution(renderer: renderer, faceSize: Size, map: map, situation: "of its own faces");
            Half[] second = AllFaces(renderer: renderer, level: 0, image: EnvironmentImage.Irradiance, map: 1);
            Half[] third = AllFaces(renderer: renderer, level: 0, image: EnvironmentImage.Irradiance, map: 2);
            second.Should().NotEqual(third, "the maps lie in different axes, so their cubes differ");
        }
        finally
        {
            Gpu.DisposeAndAssertClean(renderer);
        }
    }

    /// <summary>
    /// The reading of the cube's last level holds, in each quarter of each face, the mean of level 0 over that quarter,
    /// weighted by the solid angles of its texels: the mean that the downsample's three levels make, within their rounding
    /// to half floats.
    /// </summary>
    [Fact]
    public void TheReadingHoldsTheMeanOfDOverEachQuarterOfEachFace()
    {
        VulkanRenderer renderer = EnvironmentScenes.Renderer();
        try
        {
            renderer.Render(Colorful(renderer, TestChains.CamerasFrame), 1f);

            Half[] cube = AllFaces(renderer: renderer, level: 0, image: EnvironmentImage.Irradiance);
            Irradiance reading = renderer.ReadEnvironmentIrradiance(map: 0);
            const int size = IrradianceCube.FaceSize;
            const int quarter = size / IrradianceCube.CoarsestSize;
            for (int texel = 0; texel < Irradiance.Texels; texel++)
            {
                int layer = texel / 4;
                int qx = texel % 2;
                int qy = texel / 2 % 2;
                Vector3 sum = Vector3.Zero;
                double weight = 0;
                for (int y = qy * quarter; y < (qy + 1) * quarter; y++)
                for (int x = qx * quarter; x < (qx + 1) * quarter; x++)
                {
                    float solidAngle = (float)CubeFaces.TexelSolidAngle(x, y, size);
                    int at = 4 * ((layer * size + y) * size + x);
                    sum += new Vector3((float)cube[at], (float)cube[at + 1], (float)cube[at + 2]) * solidAngle;
                    weight += solidAngle;
                }

                Vector3 expected = sum / (float)weight;
                Vector3
                    .Distance(reading[texel], expected)
                    .Should()
                    .BeLessThan(3e-3f * (1 + expected.Length()), "texel {0} of the last level", texel);
            }
        }
        finally
        {
            Gpu.DisposeAndAssertClean(renderer);
        }
    }

    /// <summary>
    /// The reading that a later frame's batcher gets for each map of a chain of three is the last level of that map's own
    /// cube: each frame copies the last level of every served map's six layers, and the CPU reads each map's texels from
    /// its own place in the slot's readback buffer. The scene holds still, so every frame's cubes are the same.
    /// </summary>
    [Fact]
    public void EachMapsReadingInALaterFrameIsTheLastLevelOfItsOwnCube()
    {
        // Gpu.Headless's default: a reading reaches the CPU this many frames after its frame.
        const int framesInFlight = 2;
        VulkanRenderer renderer = EnvironmentScenes.Renderer(facesPerFrame: CubeFaces.Count);
        try
        {
            RenderSnapshot snapshot = ColorfulInThreeFrames(renderer);
            for (int frame = 0; frame < framesInFlight + 2; frame++)
                renderer.Render(snapshot, 1f);

            renderer.LastScene.Ambient.TryGetValue(out MapAmbient ambient).Should().BeTrue("the map is on");
            for (int map = 0; map < 3; map++)
            {
                Irradiance cube = renderer.ReadEnvironmentIrradiance(map: map);
                Irradiance read = ambient.Irradiance.Span[map]!.Value.Irradiance;
                for (int texel = 0; texel < Irradiance.Texels; texel++)
                    read[texel].Should().Be(cube[texel], "texel {0} of map {1}'s reading is its cube's", texel, map);
            }

            renderer
                .ReadEnvironmentIrradiance(map: 1)
                .MeanRadiance.Should()
                .NotBe(renderer.ReadEnvironmentIrradiance(map: 2).MeanRadiance, "the maps lie in different axes");
        }
        finally
        {
            Gpu.DisposeAndAssertClean(renderer);
        }
    }

    /// <summary>
    /// The cube convolves the map's level of 32 texels, not a finer one, at the default face size of 128: under a small
    /// bright square of 0.86° from its center to its sides, whose light a texel of 32 moves by up to half its own width,
    /// level 0 of the cube is the C# copy's convolution of that level. The C# copy puts the two levels up to 0.5 % of a
    /// unit irradiance apart under such a light, and the square sends 1.8. Under the broad squares of
    /// <see cref="Colorful"/> the levels of 32 and 64 give the same convolution within the rounding of half floats.
    /// </summary>
    [Fact]
    public void UnderASmallBrightSquareTheIrradianceCubeIsTheConvolutionOfTheMapsLevelOf32Texels()
    {
        const int faceSize = 128;
        VulkanRenderer renderer = Renderer(faceSize);
        try
        {
            RenderSnapshot snapshot = EnvironmentScenes.Dark();
            snapshot.Instances.Add(
                EnvironmentScenes.Square(
                    plane: renderer.CreateMesh(Primitives.Plane(1f)),
                    material: EnvironmentScenes.Emissive(renderer, new(2000)),
                    center: Vector3.Normalize(new(0.1f, 0.2f, 1f)) * 10,
                    halfSize: 0.15f,
                    chainDepth: TestChains.CamerasFrame
                )
            );

            renderer.Render(snapshot, 1f);

            CubeShouldBeTheConvolution(
                renderer: renderer,
                faceSize: faceSize,
                map: 0,
                situation: "under a small square"
            );
        }
        finally
        {
            Gpu.DisposeAndAssertClean(renderer);
        }
    }

    /// <summary>
    /// A map of one radiance everywhere gives a cube of exactly that radiance at every face size: the convolution divides
    /// by the sum of its weights, which is the sum of the light that it weighs, not by π, which their sum misses by up to
    /// 0.07 % at a source of 16 texels. The half float below 1 lies 0.05 % below it.
    /// </summary>
    [Theory]
    [InlineData(EnvironmentMapSetting.MinFaceSize)]
    [InlineData(Size)]
    public void AUniformMapGivesACubeOfItsOwnRadiance(int faceSize)
    {
        VulkanRenderer renderer = Renderer(faceSize);
        try
        {
            RenderSnapshot snapshot = EnvironmentScenes.Dark();
            EnvironmentScenes.AddEnclosure(
                snapshot: snapshot,
                plane: renderer.CreateMesh(Primitives.Plane(1f)),
                material: EnvironmentScenes.Emissive(renderer, Vector3.One),
                distance: 5,
                chainDepth: TestChains.CamerasFrame
            );

            renderer.Render(snapshot, 1f);

            // Within one half-float step of 1, 2^-10 above and 2^-11 below: a driver can round the float result toward
            // zero when it stores the half-float cube, as NVIDIA's does (ticket t8-6qne). A weight that the sum misses or
            // counts twice moves a texel by far more than 0.1 %.
            Half[] cube = CubeFaces0(renderer);
            for (int i = 0; i < cube.Length; i++)
                if (i % 4 != 3)
                    ((float)cube[i])
                        .Should()
                        .BeApproximately(
                            1f,
                            1f / 1024,
                            "texel {0} of a uniform map of 1 at face size {1}",
                            i / 4,
                            faceSize
                        );
        }
        finally
        {
            Gpu.DisposeAndAssertClean(renderer);
        }
    }

    [Fact]
    public void UnderAWhiteSkyAndABlackGroundTheIrradianceCubeHoldsTheHemispheresDiffuseLight()
    {
        VulkanRenderer renderer = EnvironmentScenes.Renderer();
        try
        {
            MeshId plane = renderer.CreateMesh(Primitives.Plane(1f));
            MaterialId white = EnvironmentScenes.Emissive(renderer, Vector3.One);
            RenderSnapshot snapshot = EnvironmentScenes.Dark();
            // A dome of radiance 1 over the horizon: a lid and four walls that end at y = 0, between two rows of the side
            // faces. Below it, the black sky.
            snapshot.Instances.Add(
                EnvironmentScenes.Facing(
                    plane: plane,
                    material: white,
                    center: new(0, 5, 0),
                    normal: -Vector3.UnitY,
                    halfSize: 5.5f,
                    chainDepth: TestChains.CamerasFrame
                )
            );
            foreach (
                Vector3 side in (ReadOnlySpan<Vector3>)[Vector3.UnitX, -Vector3.UnitX, Vector3.UnitZ, -Vector3.UnitZ]
            )
                snapshot.Instances.Add(
                    EnvironmentScenes.Facing(
                        plane: plane,
                        material: white,
                        center: side * 5 + new Vector3(0, 5.5f, 0),
                        normal: -side,
                        halfSize: 5.5f,
                        chainDepth: TestChains.CamerasFrame
                    )
                );

            renderer.Render(snapshot, 1f);

            Half[] cube = CubeFaces0(renderer);
            for (int texel = 0; texel < cube.Length / 4; texel++)
            {
                Vector3 normal = CubeTexelDirection(texel);
                ((float)cube[4 * texel])
                    .Should()
                    .BeApproximately((1 + normal.Y) / 2, 0.03f, "D(n) = (1 + n_y) / 2 at {0}", normal);
            }
        }
        finally
        {
            Gpu.DisposeAndAssertClean(renderer);
        }
    }

    /// <summary>
    /// Each map of a chain of three takes its mips from its own level 0: every mip texel of each map is the mean of its
    /// children in the same map, through the three runs of mips at 128 texels, each of which every map takes in turn.
    /// </summary>
    [Fact]
    public void EachMapsMipsAreTheMeansOfItsOwnChildren()
    {
        const int faceSize = 128;
        VulkanRenderer renderer = Renderer(faceSize);
        try
        {
            renderer.Render(ColorfulInThreeFrames(renderer), 1f);

            float[] worstOfMap = new float[3];
            for (int map = 0; map < worstOfMap.Length; map++)
            {
                Half[][][] mips = DrawnLevels(renderer: renderer, faceSize: faceSize, map: map);
                for (int level = 1; level < mips.Length; level++)
                {
                    int size = faceSize >> level;
                    for (int layer = 0; layer < CubeFaces.Count; layer++)
                    for (int y = 0; y < size; y++)
                    for (int x = 0; x < size; x++)
                    {
                        Vector4 expected = ChildrenMean(children: mips[level - 1][layer], size: size, x: x, y: y);
                        Vector4 read = EnvironmentScenes.Texel(face: mips[level][layer], size: size, x: x, y: y);
                        worstOfMap[map] = MathF.Max(
                            worstOfMap[map],
                            (read - expected).Length() / (1 + expected.Length())
                        );
                    }
                }
            }

            AllFaces(renderer: renderer, level: 0, image: EnvironmentImage.Radiance, map: 1)
                .Should()
                .NotEqual(
                    AllFaces(renderer: renderer, level: 0, image: EnvironmentImage.Radiance, map: 2),
                    "each map holds the scene in its own axes"
                );
            worstOfMap.Should().OnlyContain(worst => worst < 1e-3f, "each map's mip texel is its own children's mean");
        }
        finally
        {
            Gpu.DisposeAndAssertClean(renderer);
        }
    }

    /// <summary>
    /// Each map of a chain of three takes its rough levels from its own radiance map: at texels spread over every face of
    /// every level, through the two dispatches of the specular levels at 128 texels, each map's texel is the sum over its
    /// samples of that map's radiance map.
    /// </summary>
    [Fact]
    public void EachMapsRoughLevelsAreTheSumsOverItsOwnRadianceMap()
    {
        const int faceSize = 128;
        VulkanRenderer renderer = Renderer(faceSize);
        int levels = PrefilterLevels.Count(faceSize);
        try
        {
            renderer.Render(ColorfulInThreeFrames(renderer), 1f);

            float[] worstOfMap = new float[3];
            for (int map = 0; map < worstOfMap.Length; map++)
            {
                var reference = new PrefilterReference(
                    DrawnLevels(renderer: renderer, faceSize: faceSize, map: map),
                    faceSize
                );
                for (int level = 1; level < levels; level++)
                {
                    int size = faceSize >> level;
                    float roughness = PrefilterLevels.Roughness(level, levels);
                    for (int layer = 0; layer < CubeFaces.Count; layer++)
                    {
                        Half[] face = renderer.ReadEnvironmentFace(
                            map: map,
                            face: (CubeFace)layer,
                            level: level,
                            image: EnvironmentImage.Specular
                        );
                        // About 50 texels spread over each face, in row order.
                        int stride = Math.Max(1, size * size / 50 + 1);
                        for (int texel = 0; texel < size * size; texel += stride)
                        {
                            int x = texel % size;
                            int y = texel / size;
                            Vector3 n = CubeFaces.Direction((CubeFace)layer, (x + 0.5f) / size, (y + 0.5f) / size);
                            Vector4 expected = reference.Prefilter(n, roughness);
                            Vector4 read = EnvironmentScenes.Texel(face: face, size: size, x: x, y: y);
                            worstOfMap[map] = MathF.Max(
                                worstOfMap[map],
                                (read - expected).Length() / (1 + expected.Length())
                            );
                        }
                    }
                }
            }

            // Within 0.5 % of a texel's light. The turned maps measured up to 0.43 % at their roughest levels, where the
            // map in the camera's axes measured 0.25 %, and a map's texels measure 3.7 % or more against the samples of
            // another map's radiance map.
            worstOfMap.Should().OnlyContain(worst => worst < 5e-3f, "each map's texels sum over its own samples");
        }
        finally
        {
            Gpu.DisposeAndAssertClean(renderer);
        }
    }

    /// <summary>
    /// The prefilter records no barrier that the next one covers. For M drawn maps at 128 texels, whose mips take three
    /// runs, the first frame's barriers are: one into the storage layout for the mips; one after each map's run but the
    /// last drawn map's last run, whose writes the levels' barrier orders as it makes the mips read-only; that one; one
    /// before each map's downsample of its irradiance cube; one between two maps of the levels, none after the last, whose
    /// writes the specular map's read-only barrier and the readback's barrier order; and the readback's two, before and
    /// after its copy. So 5M + 2 in all.
    /// </summary>
    [Theory]
    [InlineData(1)]
    [InlineData(3)]
    public void ThePrefilterRecordsNoBarrierThatTheNextOneCovers(int maps)
    {
        const int faceSize = 128;
        VulkanRenderer renderer = Renderer(faceSize);
        try
        {
            var before = new FlushProbe();
            var after = new FlushProbe();
            // The face pass and the prefilter were added first, so the probes record right after each of them.
            renderer.Graph.Add(FrameGraph.Stage.EnvironmentFaces, before);
            renderer.Graph.Add(FrameGraph.Stage.EnvironmentPrefilter, after);

            renderer.Render(
                maps == 1 ? Colorful(renderer, TestChains.CamerasFrame) : ColorfulInThreeFrames(renderer),
                1f
            );

            int runs = PrefilterLevels.DownsampleDispatches(PrefilterLevels.RadianceLevels(faceSize));
            int barriers = after.Flushes - before.Flushes;
            runs.Should().Be(3, "the count below assumes three runs of mips");
            barriers.Should().Be(5 * maps + 2, "the prefilter of {0} maps records each barrier once", maps);
        }
        finally
        {
            Gpu.DisposeAndAssertClean(renderer);
        }
    }

    [Fact]
    public void TheOpenSkyShareIsPrefilteredWithTheSameLobeAsTheColor()
    {
        VulkanRenderer renderer = EnvironmentScenes.Renderer();
        try
        {
            MeshId plane = renderer.CreateMesh(Primitives.Plane(1f));
            RenderSnapshot snapshot = EnvironmentScenes.Dark();
            // A white wall of radiance 1 and an open-sky share of 0, against a black sky of share 1.
            snapshot.Instances.Add(
                EnvironmentScenes.Facing(
                    plane: plane,
                    material: EnvironmentScenes.Emissive(renderer, Vector3.One),
                    center: new(-50, 0, 10),
                    normal: -Vector3.UnitZ,
                    halfSize: 50,
                    chainDepth: TestChains.CamerasFrame
                )
            );

            renderer.Render(snapshot, 1f);

            // The five coarser levels of the radiance map and the specular level each round the color and the share to
            // half floats, by up to 2^-12 each below 1, so the sum may stray by 6 × 2^-11.
            const float rounding = 6f / 2048;
            for (int level = 0; level < Levels; level++)
            {
                Half[] faces = AllFaces(renderer: renderer, level: level, image: EnvironmentImage.Specular);
                for (int i = 0; i < faces.Length; i += 4)
                    ((float)faces[i] + (float)faces[i + 3])
                        .Should()
                        .BeApproximately(1f, rounding, "color and share mix by the same weights at level {0}", level);
            }
        }
        finally
        {
            Gpu.DisposeAndAssertClean(renderer);
        }
    }

    /// <summary>A pass that records nothing and keeps the frame's count of barriers at its place in the graph.</summary>
    private sealed class FlushProbe : RenderPass
    {
        public int Flushes { get; private set; }

        public override string Name => "flush probe";

        public override void Record(FrameContext context) => Flushes = context.Barriers.FlushCount;
    }
}
