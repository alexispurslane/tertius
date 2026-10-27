using System.Numerics;
using Tertius.Testing;

namespace Tertius.Rendering.Tests;

/// <summary>
/// Scenes and reads for the GPU tests of the environment map (docs/plans/environment-map.md, "GPU tests of the map
/// itself"). The camera sits at the origin with identity rotation and frame 0.
/// </summary>
internal static class EnvironmentScenes
{
    /// <summary>The face size of the tests: small, so that a refresh draws quickly.</summary>
    public const int FaceSize = 32;

    /// <summary>A headless renderer with the map on, 160 × 90.</summary>
    public static VulkanRenderer Renderer(int facesPerFrame = 1, ExposureSetting exposure = default) =>
        Gpu.Headless(
            width: 160,
            height: 90,
            exposure: exposure,
            environmentMap: SceneEnvironmentMap.Default with
            {
                FaceSize = FaceSize,
                FacesPerFrame = facesPerFrame,
            }
        );

    /// <summary>A snapshot with no sunlight: only emission and lamps light the faces.</summary>
    public static RenderSnapshot Dark() => Gpu.Snapshot(starDirection: Vector3.UnitY, starIrradiance: 0f);

    /// <summary>A black material that emits <paramref name="radiance"/> and reflects nothing.</summary>
    public static MaterialId Emissive(VulkanRenderer renderer, Vector3 radiance) =>
        renderer.CreateMaterial(
            MaterialDesc.Color(new(x: 0, y: 0, z: 0, w: 1), roughness: 1f) with
            {
                Emissive = radiance,
                Reflectance = 0f,
            }
        );

    /// <summary>A white matte material.</summary>
    public static MaterialId Matte(VulkanRenderer renderer) =>
        renderer.CreateMaterial(MaterialDesc.Color(new(x: 0.8f, y: 0.8f, z: 0.8f, w: 1), roughness: 0.9f));

    /// <summary>The rotation that turns the plane mesh's front, +Y, toward the unit <paramref name="normal"/>.</summary>
    public static Quaternion FrontToward(Vector3 normal)
    {
        float cosine = Vector3.Dot(Vector3.UnitY, normal);
        if (cosine < -0.9999f)
            return Quaternion.CreateFromAxisAngle(Vector3.UnitX, MathF.PI);
        var axis = Vector3.Cross(Vector3.UnitY, normal);
        if (axis.LengthSquared() < 1e-12f)
            return Quaternion.Identity;
        return Quaternion.CreateFromAxisAngle(Vector3.Normalize(axis), MathF.Acos(Math.Clamp(cosine, -1f, 1f)));
    }

    /// <summary>A square of <paramref name="plane"/>, a plane mesh of half size 1, centered at <paramref name="center"/> and facing the eye.</summary>
    public static RenderInstance Square(
        MeshId plane,
        MaterialId material,
        Vector3 center,
        float halfSize,
        ushort chainDepth
    ) =>
        Gpu.Instance(
            mesh: plane,
            material: material,
            position: center,
            scale: new(halfSize, 1, halfSize),
            rotation: FrontToward(-Vector3.Normalize(center)),
            chainDepth: chainDepth
        );

    /// <summary>A square of half size <paramref name="halfSize"/> at <paramref name="center"/> whose front faces along <paramref name="normal"/>.</summary>
    public static RenderInstance Facing(
        MeshId plane,
        MaterialId material,
        Vector3 center,
        Vector3 normal,
        float halfSize,
        ushort chainDepth,
        RenderFlags flags = RenderFlags.None
    ) =>
        Gpu.Instance(
            mesh: plane,
            material: material,
            position: center,
            scale: new(halfSize, 1, halfSize),
            rotation: FrontToward(normal),
            flags: flags,
            chainDepth: chainDepth
        );

    /// <summary>Six squares facing inward around the eye, <paramref name="distance"/> away: a box that fills every direction.</summary>
    public static void AddEnclosure(
        RenderSnapshot snapshot,
        MeshId plane,
        MaterialId material,
        float distance,
        ushort chainDepth
    )
    {
        for (int layer = 0; layer < CubeFaces.Count; layer++)
            snapshot.Instances.Add(
                Square(
                    plane: plane,
                    material: material,
                    center: CubeFaces.Basis((CubeFace)layer).Forward * distance,
                    halfSize: distance * 1.1f,
                    chainDepth: chainDepth
                )
            );
    }

    /// <summary>Renders <paramref name="snapshot"/> at <paramref name="time"/> s.</summary>
    public static void Still(VulkanRenderer renderer, RenderSnapshot snapshot, double time)
    {
        snapshot.Time = time;
        renderer.Render(snapshot, 1f);
    }

    /// <summary>Level 0 of a face of the radiance map.</summary>
    public static Half[] Face(VulkanRenderer renderer, CubeFace face) =>
        renderer.ReadEnvironmentFace(map: 0, face: face, level: 0, image: EnvironmentImage.Radiance);

    /// <summary>The RGBA of texel (<paramref name="x"/>, <paramref name="y"/>) of a face of <paramref name="size"/> texels.</summary>
    public static Vector4 Texel(Half[] face, int size, int x, int y)
    {
        int i = (y * size + x) * 4;
        return new(x: (float)face[i], y: (float)face[i + 1], z: (float)face[i + 2], w: (float)face[i + 3]);
    }

    /// <summary>The RGB of the texel at the center of a face of level 0.</summary>
    public static Vector3 Center(Half[] face) =>
        AsColor(Texel(face: face, size: FaceSize, x: FaceSize / 2, y: FaceSize / 2));

    public static Vector3 AsColor(Vector4 texel) => new(texel.X, texel.Y, texel.Z);

    /// <summary>The level-0 center of every face, in layer order.</summary>
    public static Vector3[] Centers(VulkanRenderer renderer)
    {
        var centers = new Vector3[CubeFaces.Count];
        for (int layer = 0; layer < centers.Length; layer++)
            centers[layer] = Center(Face(renderer, (CubeFace)layer));
        return centers;
    }

    /// <summary>
    /// Asserts that <paramref name="actual"/> is <paramref name="expected"/> within the rounding of a half float: the map
    /// stores RGBA16F.
    /// </summary>
    public static void ShouldBeNear(Vector3 actual, Vector3 expected, string because) =>
        Vector3
            .Distance(actual, expected)
            .Should()
            .BeLessThan(1e-3f, $"{because}: expected {expected}, found {actual}");

    /// <summary>A color that differs for every <paramref name="index"/> from 0 to 9.</summary>
    public static Vector3 ColorOf(int index) => new(0.1f * (index + 1), 0.05f * index + 0.02f, 1f - 0.08f * index);
}

/// <summary>
/// The faces of the environment map on the GPU (docs/plans/environment-map.md, "The capture" and "Which faces a frame
/// draws"): what a face holds, how it is lit, and which faces a frame draws.
/// </summary>
[Collection(GpuCollection.Name)]
public sealed class EnvironmentFaceTests
{
    private const int Size = EnvironmentScenes.FaceSize;

    [Fact]
    public void EachFaceHoldsWhatLiesInItsDirection()
    {
        VulkanRenderer renderer = EnvironmentScenes.Renderer();
        try
        {
            MeshId plane = renderer.CreateMesh(Primitives.Plane(1f));
            RenderSnapshot snapshot = EnvironmentScenes.Dark();
            for (int layer = 0; layer < CubeFaces.Count; layer++)
                snapshot.Instances.Add(
                    EnvironmentScenes.Square(
                        plane: plane,
                        material: EnvironmentScenes.Emissive(renderer, EnvironmentScenes.ColorOf(layer)),
                        center: CubeFaces.Basis((CubeFace)layer).Forward * 10,
                        halfSize: 2,
                        chainDepth: TestChains.CamerasFrame
                    )
                );
            var marker = new Vector3(4);
            // Toward +X, up along +Y and right along −Z: s = 0.75, t = 0.25 of face +X.
            snapshot.Instances.Add(
                EnvironmentScenes.Square(
                    plane: plane,
                    material: EnvironmentScenes.Emissive(renderer, marker),
                    center: new(10, 5, -5),
                    halfSize: 0.5f,
                    chainDepth: TestChains.CamerasFrame
                )
            );

            renderer.Render(snapshot, 1f);

            Vector3[] centers = EnvironmentScenes.Centers(renderer);
            for (int layer = 0; layer < CubeFaces.Count; layer++)
                EnvironmentScenes.ShouldBeNear(
                    centers[layer],
                    EnvironmentScenes.ColorOf(layer),
                    $"face {(CubeFace)layer} looks at the square along its own axis"
                );
            Half[] positiveX = EnvironmentScenes.Face(renderer, CubeFace.PositiveX);
            EnvironmentScenes
                .AsColor(EnvironmentScenes.Texel(face: positiveX, size: Size, x: 24, y: 8))
                .Should()
                .Be(marker, "a square up and toward −Z of +X lies in the upper right of face +X");
            EnvironmentScenes
                .AsColor(EnvironmentScenes.Texel(face: positiveX, size: Size, x: 7, y: 8))
                .Should()
                .Be(Vector3.Zero, "the right of face +X is −Z, not +Z");
            EnvironmentScenes
                .AsColor(EnvironmentScenes.Texel(face: positiveX, size: Size, x: 24, y: 23))
                .Should()
                .Be(Vector3.Zero, "row 0 of a face is its top");
        }
        finally
        {
            Gpu.DisposeAndAssertClean(renderer);
        }
    }

    [Fact]
    public void AFaceDrawsTheFarMiddleAndNearRanges()
    {
        VulkanRenderer renderer = EnvironmentScenes.Renderer();
        try
        {
            MeshId plane = renderer.CreateMesh(Primitives.Plane(1f));
            RenderSnapshot snapshot = EnvironmentScenes.Dark();
            (CubeFace Face, float Distance, int Color)[] squares =
            [
                (CubeFace.PositiveX, 100f, 0),
                (CubeFace.NegativeX, 100e3f, 1),
                (CubeFace.PositiveY, 10e6f, 2),
                (CubeFace.PositiveZ, 10e6f, 3),
                (CubeFace.PositiveZ, 100f, 4),
            ];
            foreach ((CubeFace face, float distance, int color) in squares)
                snapshot.Instances.Add(
                    EnvironmentScenes.Square(
                        plane: plane,
                        material: EnvironmentScenes.Emissive(renderer, EnvironmentScenes.ColorOf(color)),
                        center: CubeFaces.Basis(face).Forward * distance,
                        halfSize: 0.2f * distance,
                        chainDepth: TestChains.CamerasFrame
                    )
                );

            renderer.Render(snapshot, 1f);

            Vector3[] centers = EnvironmentScenes.Centers(renderer);
            EnvironmentScenes.ShouldBeNear(
                centers[(int)CubeFace.PositiveX],
                EnvironmentScenes.ColorOf(0),
                "the near range draws at 100 m"
            );
            EnvironmentScenes.ShouldBeNear(
                centers[(int)CubeFace.NegativeX],
                EnvironmentScenes.ColorOf(1),
                "the middle range draws at 100 km"
            );
            EnvironmentScenes.ShouldBeNear(
                centers[(int)CubeFace.PositiveY],
                EnvironmentScenes.ColorOf(2),
                "the far range draws at 10,000 km"
            );
            EnvironmentScenes.ShouldBeNear(
                centers[(int)CubeFace.PositiveZ],
                EnvironmentScenes.ColorOf(4),
                "a nearer range draws over a farther one"
            );
        }
        finally
        {
            Gpu.DisposeAndAssertClean(renderer);
        }
    }

    [Fact]
    public void AFaceShowsTheSunsMapShadowBehindTheCamera()
    {
        VulkanRenderer renderer = EnvironmentScenes.Renderer();
        try
        {
            MeshId plane = renderer.CreateMesh(Primitives.Plane(1f));
            MeshId box = renderer.CreateMesh(Primitives.Box(new(0.5f)));
            MaterialId matte = EnvironmentScenes.Matte(renderer);
            RenderSnapshot snapshot = Gpu.Snapshot(starDirection: new(1, 1, 0), starIrradiance: 3f);
            snapshot.Instances.Add(
                Gpu.Instance(
                    mesh: plane,
                    material: matte,
                    position: new(0, -10, 0),
                    scale: new(200, 1, 200),
                    flags: RenderFlags.CastsShadow,
                    chainDepth: TestChains.CamerasFrame
                )
            );
            // 30 m behind the camera, where the main view's rule picks cascade 0, which does not hold it. Its shadow falls
            // toward −X, over x from −15 to −5.
            snapshot.Instances.Add(
                Gpu.Instance(
                    mesh: box,
                    material: matte,
                    position: new(0, -5, 30),
                    scale: new(10),
                    flags: RenderFlags.CastsShadow,
                    chainDepth: TestChains.CamerasFrame
                )
            );

            renderer.Render(snapshot, 1f);

            Half[] behind = EnvironmentScenes.Face(renderer, CubeFace.PositiveZ);
            // The ground at (−10, −10, 29) and at (10, −10, 29).
            float shadowed = EnvironmentScenes.Texel(face: behind, size: Size, x: 10, y: 21).X;
            float lit = EnvironmentScenes.Texel(face: behind, size: Size, x: 21, y: 21).X;
            // The ground at (−10, −10, 24.6): deep in the far shadow of the box's bounding sphere, which the cascades
            // replace, but short of the box's own shadow, which begins at z = 25. The filter of the map's shadow edge,
            // 0.4 m away, still darkens it by about a quarter.
            float besideTheShadow = EnvironmentScenes.Texel(face: behind, size: Size, x: 9, y: 22).X;
            lit.Should().BeGreaterThan(0.2f, "the sun lights the ground beside the box");
            shadowed.Should().BeLessThan(0.1f * lit, "the box shadows the ground on its far side from the sun");
            besideTheShadow
                .Should()
                .BeGreaterThan(0.5f * lit, "a face takes the sun's shadow from the cascade that holds the point");
        }
        finally
        {
            Gpu.DisposeAndAssertClean(renderer);
        }
    }

    [Fact]
    public void AFaceIsLitByALampThatTheMainViewDoesNotSee()
    {
        // At −10 stops the cutoff irradiance is π · 2^−13 · 2^10 ≈ 0.39 W/m², so a lamp of 50 W/sr reaches
        // √(50 / 0.39) ≈ 11.3 m. The faces store radiance, which the exposure does not scale.
        VulkanRenderer renderer = EnvironmentScenes.Renderer(exposure: new FixedExposure(-10));
        try
        {
            MeshId plane = renderer.CreateMesh(Primitives.Plane(1f));
            RenderSnapshot snapshot = EnvironmentScenes.Dark();
            snapshot.Instances.Add(
                EnvironmentScenes.Square(
                    plane: plane,
                    material: EnvironmentScenes.Matte(renderer),
                    center: new(0, 0, 22),
                    halfSize: 5,
                    chainDepth: TestChains.CamerasFrame
                )
            );
            // Its reach, from z ≈ 8.7 to 31.3, lies wholly behind the camera.
            snapshot.Lights.Add(
                Gpu.Lamp(
                    position: new(0, 0, 20),
                    color: Vector3.One,
                    intensity: 50,
                    chainDepth: TestChains.CamerasFrame
                )
            );

            renderer.Render(snapshot, 1f);

            EnvironmentScenes
                .Center(EnvironmentScenes.Face(renderer, CubeFace.PositiveZ))
                .X.Should()
                .BeGreaterThan(0.5f, "the lamp lights the wall behind the camera");
        }
        finally
        {
            Gpu.DisposeAndAssertClean(renderer);
        }
    }

    [Fact]
    public void AFaceTakesNoAmbientLight()
    {
        VulkanRenderer renderer = EnvironmentScenes.Renderer();
        try
        {
            MeshId plane = renderer.CreateMesh(Primitives.Plane(1f));
            RenderSnapshot snapshot = EnvironmentScenes.Dark();
            snapshot.Ambient = snapshot.Ambient with { Intensity = 20f };
            snapshot.Instances.Add(
                EnvironmentScenes.Square(
                    plane: plane,
                    material: EnvironmentScenes.Matte(renderer),
                    center: new(10, 0, 0),
                    halfSize: 5,
                    chainDepth: TestChains.CamerasFrame
                )
            );
            // A bright wall behind the camera fills the map with light, which an ambient term would read from the frame
            // before.
            snapshot.Instances.Add(
                EnvironmentScenes.Square(
                    plane: plane,
                    material: EnvironmentScenes.Emissive(renderer, new(5)),
                    center: new(-10, 0, 0),
                    halfSize: 5,
                    chainDepth: TestChains.CamerasFrame
                )
            );

            renderer.Render(snapshot, 1f);
            renderer.Render(snapshot, 1f);

            EnvironmentScenes
                .Center(EnvironmentScenes.Face(renderer, CubeFace.PositiveX))
                .Should()
                .Be(Vector3.Zero, "no sun, lamp or emission reaches the wall, and a face takes no ambient light");
        }
        finally
        {
            Gpu.DisposeAndAssertClean(renderer);
        }
    }

    [Fact]
    public void TheSkyMarksOpenSkyAndASurfaceMarksNone()
    {
        VulkanRenderer renderer = EnvironmentScenes.Renderer();
        try
        {
            MeshId plane = renderer.CreateMesh(Primitives.Plane(1f));
            RenderSnapshot snapshot = EnvironmentScenes.Dark();
            // Behind the camera, over x from −100 to 0: the left half of face +Z, whose right is +X.
            snapshot.Instances.Add(
                EnvironmentScenes.Facing(
                    plane: plane,
                    material: EnvironmentScenes.Matte(renderer),
                    center: new(-50, 0, 10),
                    normal: -Vector3.UnitZ,
                    halfSize: 50,
                    chainDepth: TestChains.CamerasFrame
                )
            );

            renderer.Render(snapshot, 1f);

            Half[] behind = EnvironmentScenes.Face(renderer, CubeFace.PositiveZ);
            for (int y = 0; y < Size; y++)
            for (int x = 0; x < Size; x++)
                EnvironmentScenes
                    .Texel(face: behind, size: Size, x: x, y: y)
                    .W.Should()
                    .Be(x < Size / 2 ? 0f : 1f, "texel ({0}, {1}) of face +Z shows the wall left of the middle", x, y);
            foreach (CubeFace open in (ReadOnlySpan<CubeFace>)[CubeFace.PositiveX, CubeFace.NegativeZ])
                EnvironmentScenes
                    .Face(renderer, open)
                    .Where((_, i) => i % 4 == 3)
                    .Should()
                    .AllSatisfy(alpha => ((float)alpha).Should().Be(1f), "face {0} sees only sky", open);
        }
        finally
        {
            Gpu.DisposeAndAssertClean(renderer);
        }
    }

    [Fact]
    public void AFaceHoldsNoStars()
    {
        VulkanRenderer renderer = EnvironmentScenes.Renderer();
        try
        {
            renderer.Render(EnvironmentScenes.Dark(), 1f);

            for (int layer = 0; layer < CubeFaces.Count; layer++)
                EnvironmentScenes
                    .Face(renderer, (CubeFace)layer)
                    .Where((_, i) => i % 4 != 3)
                    .Should()
                    .AllSatisfy(
                        channel => ((float)channel).Should().Be(0f),
                        "the sky of face {0} is black: the readers add the stars",
                        (CubeFace)layer
                    );
        }
        finally
        {
            Gpu.DisposeAndAssertClean(renderer);
        }
    }

    [Fact]
    public void AFaceHoldsPlainRadianceWhateverTheExposure()
    {
        static Half[] Faces(float stops)
        {
            VulkanRenderer renderer = EnvironmentScenes.Renderer(exposure: new FixedExposure(stops));
            try
            {
                MeshId plane = renderer.CreateMesh(Primitives.Plane(1f));
                RenderSnapshot snapshot = Gpu.Snapshot(starDirection: -Vector3.UnitX, starIrradiance: 3f);
                snapshot.Instances.Add(
                    EnvironmentScenes.Square(
                        plane: plane,
                        material: EnvironmentScenes.Emissive(renderer, new(1, 0.5f, 0.25f)),
                        center: new(10, 0, 0),
                        halfSize: 4,
                        chainDepth: TestChains.CamerasFrame
                    )
                );
                snapshot.Instances.Add(
                    EnvironmentScenes.Square(
                        plane: plane,
                        material: EnvironmentScenes.Matte(renderer),
                        center: new(-10, 0, 0),
                        halfSize: 4,
                        chainDepth: TestChains.CamerasFrame
                    )
                );
                renderer.Render(snapshot, 1f);
                return
                [
                    .. EnvironmentScenes.Face(renderer, CubeFace.PositiveX),
                    .. EnvironmentScenes.Face(renderer, CubeFace.NegativeX),
                ];
            }
            finally
            {
                Gpu.DisposeAndAssertClean(renderer);
            }
        }

        Half[] bright = Faces(3);
        Half[] plain = Faces(0);

        plain.Should().Contain(texel => (float)texel > 0.1f, "the faces hold the emission and the sunlit wall");
        bright.Should().Equal(plain, "the map stores light that no exposure multiplied");
    }

    [Fact]
    public void AFaceLeavesOutGlassAndPuffs()
    {
        VulkanRenderer renderer = EnvironmentScenes.Renderer();
        try
        {
            MeshId plane = renderer.CreateMesh(Primitives.Plane(1f));
            var radiance = new Vector3(1, 0.5f, 0.25f);
            RenderSnapshot snapshot = EnvironmentScenes.Dark();
            snapshot.Instances.Add(
                EnvironmentScenes.Square(
                    plane: plane,
                    material: EnvironmentScenes.Emissive(renderer, radiance),
                    center: new(10, 0, 0),
                    halfSize: 5,
                    chainDepth: TestChains.CamerasFrame
                )
            );
            MaterialId glass = renderer.CreateMaterial(
                MaterialDesc.Color(new(x: 0.2f, y: 0.9f, z: 0.3f, w: 0.5f), roughness: 0.1f) with
                {
                    Emissive = new(0, 3, 0),
                }
            );
            snapshot.Instances.Add(
                EnvironmentScenes.Facing(
                    plane: plane,
                    material: glass,
                    center: new(5, 0, 0),
                    normal: -Vector3.UnitX,
                    halfSize: 3,
                    flags: RenderFlags.Transparent,
                    chainDepth: TestChains.CamerasFrame
                )
            );
            snapshot.PuffLooks.Add(
                new(
                    Life: 10f,
                    StartRadius: 1f,
                    EndRadius: 1f,
                    TemperatureRatio: 1f,
                    Shading: PuffShading.Glow(new(5f, 5f, 5f))
                )
            );
            var puff = new Vector3(7, 0, 0);
            snapshot.Puffs.Add(
                new()
                {
                    Position = puff,
                    PreviousPosition = puff,
                    Age = 0,
                    Look = 0,
                }
            );

            renderer.Render(snapshot, 1f);

            EnvironmentScenes.ShouldBeNear(
                EnvironmentScenes.Center(EnvironmentScenes.Face(renderer, CubeFace.PositiveX)),
                radiance,
                "the wall shows through the pane and the puff"
            );
        }
        finally
        {
            Gpu.DisposeAndAssertClean(renderer);
        }
    }

    [Fact]
    public void OneFaceUpdatesPerFrameInLayerOrder()
    {
        VulkanRenderer renderer = EnvironmentScenes.Renderer();
        try
        {
            MeshId plane = renderer.CreateMesh(Primitives.Plane(1f));
            RenderSnapshot snapshot = EnvironmentScenes.Dark();
            MaterialId[] materials =
            [
                .. Enumerable
                    .Range(0, 7)
                    .Select(i => EnvironmentScenes.Emissive(renderer, EnvironmentScenes.ColorOf(i))),
            ];
            EnvironmentScenes.AddEnclosure(
                snapshot: snapshot,
                plane: plane,
                material: materials[0],
                distance: 5,
                chainDepth: TestChains.CamerasFrame
            );
            EnvironmentScenes.Still(renderer: renderer, snapshot: snapshot, time: 0);
            Vector3[] before = EnvironmentScenes.Centers(renderer);
            foreach (Vector3 center in before)
                EnvironmentScenes.ShouldBeNear(
                    center,
                    EnvironmentScenes.ColorOf(0),
                    "the first drawn frame draws every face"
                );

            for (int frame = 1; frame <= 6; frame++)
            {
                snapshot.Instances.Clear();
                EnvironmentScenes.AddEnclosure(
                    snapshot: snapshot,
                    plane: plane,
                    material: materials[frame],
                    distance: 5,
                    chainDepth: TestChains.CamerasFrame
                );
                EnvironmentScenes.Still(renderer: renderer, snapshot: snapshot, time: frame / 60.0);

                Vector3[] after = EnvironmentScenes.Centers(renderer);
                for (int layer = 0; layer < CubeFaces.Count; layer++)
                    EnvironmentScenes.ShouldBeNear(
                        after[layer],
                        layer == frame % CubeFaces.Count ? EnvironmentScenes.ColorOf(frame) : before[layer],
                        $"frame {frame} draws face {frame % CubeFaces.Count} alone, and face {layer} keeps its old color"
                    );
                before = after;
            }
        }
        finally
        {
            Gpu.DisposeAndAssertClean(renderer);
        }
    }

    [Fact]
    public void TwoFacesPerFrameUpdateTwoFacesInTurn()
    {
        VulkanRenderer renderer = EnvironmentScenes.Renderer(facesPerFrame: 2);
        try
        {
            MeshId plane = renderer.CreateMesh(Primitives.Plane(1f));
            RenderSnapshot snapshot = EnvironmentScenes.Dark();
            MaterialId[] materials =
            [
                .. Enumerable
                    .Range(0, 4)
                    .Select(i => EnvironmentScenes.Emissive(renderer, EnvironmentScenes.ColorOf(i))),
            ];
            EnvironmentScenes.AddEnclosure(
                snapshot: snapshot,
                plane: plane,
                material: materials[0],
                distance: 5,
                chainDepth: TestChains.CamerasFrame
            );
            EnvironmentScenes.Still(renderer: renderer, snapshot: snapshot, time: 0);
            Vector3[] before = EnvironmentScenes.Centers(renderer);

            for (int frame = 1; frame <= 3; frame++)
            {
                snapshot.Instances.Clear();
                EnvironmentScenes.AddEnclosure(
                    snapshot: snapshot,
                    plane: plane,
                    material: materials[frame],
                    distance: 5,
                    chainDepth: TestChains.CamerasFrame
                );
                EnvironmentScenes.Still(renderer: renderer, snapshot: snapshot, time: frame / 60.0);

                Vector3[] after = EnvironmentScenes.Centers(renderer);
                int first = 2 * frame % CubeFaces.Count;
                for (int layer = 0; layer < CubeFaces.Count; layer++)
                    EnvironmentScenes.ShouldBeNear(
                        after[layer],
                        layer == first || layer == first + 1 ? EnvironmentScenes.ColorOf(frame) : before[layer],
                        $"frame {frame} draws faces {first} and {first + 1}, and face {layer} keeps its old color otherwise"
                    );
                before = after;
            }
        }
        finally
        {
            Gpu.DisposeAndAssertClean(renderer);
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void AHeadlessStillAfterALongStepRefreshesEveryFace(bool autoExposure)
    {
        ExposureSetting exposure = autoExposure ? AutoExposure.Default : new FixedExposure(0);
        VulkanRenderer renderer = EnvironmentScenes.Renderer(exposure: exposure);
        try
        {
            Vector3[] centers = CentersAfterThreeStills(renderer: renderer, thirdTime: 1 / 60.0 + 1, thirdFrame: 0);

            foreach (Vector3 center in centers)
                EnvironmentScenes.ShouldBeNear(
                    center,
                    EnvironmentScenes.ColorOf(2),
                    "a still after a step of 1 s refreshes the map"
                );
        }
        finally
        {
            Gpu.DisposeAndAssertClean(renderer);
        }
    }

    [Fact]
    public void AHeadlessStillAtTheTickOfTheFrameBeforeRefreshesEveryFace()
    {
        VulkanRenderer renderer = EnvironmentScenes.Renderer();
        try
        {
            Vector3[] centers = CentersAfterThreeStills(renderer: renderer, thirdTime: 1 / 60.0, thirdFrame: 0);

            foreach (Vector3 center in centers)
                EnvironmentScenes.ShouldBeNear(
                    center,
                    EnvironmentScenes.ColorOf(2),
                    "a still at the same tick may follow a camera cut"
                );
        }
        finally
        {
            Gpu.DisposeAndAssertClean(renderer);
        }
    }

    [Fact]
    public void TheShownDrawAfterAMeteringDrawDrawsItsFacesInTurn()
    {
        VulkanRenderer renderer = EnvironmentScenes.Renderer(exposure: AutoExposure.Default);
        try
        {
            CentersAfterThreeStills(renderer: renderer, thirdTime: 1 / 60.0 + 1, thirdFrame: 0);

            renderer
                .LastScene.EnvironmentFaces.Should()
                .ContainSingle("the metering draw refreshed the map, and the shown draw does not refresh it again");
        }
        finally
        {
            Gpu.DisposeAndAssertClean(renderer);
        }
    }

    [Fact]
    public void AChangeOfTheCamerasFrameRefreshesEveryFace()
    {
        VulkanRenderer renderer = EnvironmentScenes.Renderer();
        try
        {
            Vector3[] centers = CentersAfterThreeStills(renderer: renderer, thirdTime: 2 / 60.0, thirdFrame: 1);

            foreach (Vector3 center in centers)
                EnvironmentScenes.ShouldBeNear(
                    center,
                    EnvironmentScenes.ColorOf(2),
                    "the faces of the old frame lie in the wrong axes"
                );
        }
        finally
        {
            Gpu.DisposeAndAssertClean(renderer);
        }
    }

    /// <summary>
    /// Three stills of an enclosure of a new color each: at 0 s, at 1/60 s, and at <paramref name="thirdTime"/> in the
    /// camera frame <paramref name="thirdFrame"/>. Returns the face centers after the third.
    /// </summary>
    private static Vector3[] CentersAfterThreeStills(VulkanRenderer renderer, double thirdTime, long thirdFrame)
    {
        MeshId plane = renderer.CreateMesh(Primitives.Plane(1f));
        RenderSnapshot snapshot = EnvironmentScenes.Dark();
        double[] times = [0, 1 / 60.0, thirdTime];
        for (int still = 0; still < times.Length; still++)
        {
            snapshot.Instances.Clear();
            EnvironmentScenes.AddEnclosure(
                snapshot: snapshot,
                plane: plane,
                material: EnvironmentScenes.Emissive(renderer, EnvironmentScenes.ColorOf(still)),
                distance: 5,
                chainDepth: TestChains.CamerasFrame
            );
            TestChains.Of(snapshot, (still == 2 ? thirdFrame : 0, Quaternion.Identity));
            EnvironmentScenes.Still(renderer: renderer, snapshot: snapshot, time: times[still]);
        }

        return EnvironmentScenes.Centers(renderer);
    }
}
