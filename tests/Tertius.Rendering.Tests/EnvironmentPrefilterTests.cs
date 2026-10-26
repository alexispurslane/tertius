using System.Numerics;

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
    private static RenderSnapshot Colorful(VulkanRenderer renderer)
    {
        MeshId plane = renderer.CreateMesh(Primitives.Plane(1f));
        RenderSnapshot snapshot = Gpu.Snapshot(starDirection: new(0.3f, 0.5f, 1f), starIrradiance: 3f);
        for (int layer = 0; layer < CubeFaces.Count; layer++)
            snapshot.Instances.Add(
                EnvironmentScenes.Square(
                    plane: plane,
                    material: EnvironmentScenes.Emissive(renderer, 2 * EnvironmentScenes.ColorOf(layer)),
                    center: CubeFaces.Basis((CubeFace)layer).Forward * 10 + new Vector3(1, 2, 0.5f),
                    halfSize: 3
                )
            );
        snapshot.Instances.Add(
            EnvironmentScenes.Square(
                plane: plane,
                material: EnvironmentScenes.Matte(renderer),
                center: new(-4, -3, -8),
                halfSize: 3
            )
        );
        return snapshot;
    }

    /// <summary>The six faces of one level, in layer order, each RGBA, rows from the top.</summary>
    private static Half[] AllFaces(VulkanRenderer renderer, int level, EnvironmentImage image)
    {
        var all = new List<Half>();
        for (int layer = 0; layer < CubeFaces.Count; layer++)
            all.AddRange(renderer.ReadEnvironmentFace(map: 0, face: (CubeFace)layer, level: level, image: image));
        return [.. all];
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

    [Fact]
    public void TheMirrorLevelIsTheRadianceMap()
    {
        VulkanRenderer renderer = EnvironmentScenes.Renderer();
        try
        {
            renderer.Render(Colorful(renderer), 1f);

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
                distance: 5
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
                    halfSize: 1
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
                rotation: EnvironmentScenes.FrontToward(-Vector3.UnitZ)
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

    [Fact]
    public void TheIrradianceIsTheProjectionOfTheMapsTexels()
    {
        VulkanRenderer renderer = EnvironmentScenes.Renderer();
        try
        {
            renderer.Render(Colorful(renderer), 1f);

            var expected = Irradiance.Project(
                AllFaces(renderer: renderer, level: 0, image: EnvironmentImage.Radiance),
                Size
            );
            Irradiance read = renderer.ReadEnvironmentIrradiance(map: 0);

            float total = 0;
            for (int i = 0; i < Irradiance.Coefficients; i++)
                total += expected[i].Length();
            total.Should().BeGreaterThan(0.1f, "the map holds light");
            for (int i = 0; i < Irradiance.Coefficients; i++)
                Vector3
                    .Distance(read[i], expected[i])
                    .Should()
                    .BeLessThan(1e-4f * total, "coefficient {0}: the shader projects as Irradiance.Project does", i);
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
                    halfSize: 50
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

    [Fact]
    public void UnderAWhiteSkyAndABlackGroundTheIrradianceIsTheHemispheres()
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
                    halfSize: 5.5f
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
                        halfSize: 5.5f
                    )
                );

            renderer.Render(snapshot, 1f);

            Irradiance irradiance = renderer.ReadEnvironmentIrradiance(map: 0);
            foreach (
                Vector3 normal in (ReadOnlySpan<Vector3>)
                    [Vector3.UnitX, -Vector3.UnitX, Vector3.UnitY, -Vector3.UnitY, Vector3.UnitZ, -Vector3.UnitZ]
            )
            {
                float expected = MathF.PI * (1 + normal.Y) / 2;
                irradiance
                    .At(normal)
                    .X.Should()
                    .BeApproximately(expected, 0.01f * MathF.PI, "E(n) = π (1 + n_y) / 2 at {0}", normal);
            }
        }
        finally
        {
            Gpu.DisposeAndAssertClean(renderer);
        }
    }
}
