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

    /// <summary>
    /// Level <paramref name="level"/> of a face of map 0's distance cube: one inverse distance per texel, in km⁻¹, rows from
    /// the top.
    /// </summary>
    public static Half[] Distances(VulkanRenderer renderer, CubeFace face, int level = 0) =>
        renderer.ReadEnvironmentFace(map: 0, face: face, level: level, image: EnvironmentImage.Distance);

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

    /// <summary>The entity of the black glowing panel of the glow tests, which no other instance holds.</summary>
    private const uint PanelEntity = 7;

    /// <summary>The glow of the black panel of the glow tests, in W/(m² sr) per channel.</summary>
    private const float PanelGlow = 5;

    /// <summary>The entity of the glowing wall behind the panel, which holds no light.</summary>
    private const uint WallEntity = 8;

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

    /// <summary>
    /// A face stores 1000 m over the distance from the eye to the surface in each texel, along the texel's own direction:
    /// the radial distance, not the depth along the face's axis (docs/plans/environment-map-distances.md, "The distance
    /// store"). A wall 4 m ahead fills face +X, a square 100 km up, in the middle range, covers the center of face +Y, and
    /// face −X sees only the sky.
    /// </summary>
    [Fact]
    public void AFaceStoresTheInverseDistanceOfTheSurfaceInEachTexel()
    {
        VulkanRenderer renderer = EnvironmentScenes.Renderer();
        try
        {
            MeshId plane = renderer.CreateMesh(Primitives.Plane(1f));
            MaterialId matte = EnvironmentScenes.Matte(renderer);
            RenderSnapshot snapshot = EnvironmentScenes.Dark();
            snapshot.Instances.Add(
                EnvironmentScenes.Square(
                    plane: plane,
                    material: matte,
                    center: new(4, 0, 0),
                    halfSize: 5,
                    chainDepth: TestChains.CamerasFrame
                )
            );
            snapshot.Instances.Add(
                EnvironmentScenes.Square(
                    plane: plane,
                    material: matte,
                    center: new(0, 100e3f, 0),
                    halfSize: 20e3f,
                    chainDepth: TestChains.CamerasFrame
                )
            );

            renderer.Render(snapshot, 1f);

            Half[] wall = EnvironmentScenes.Distances(renderer, CubeFace.PositiveX);
            wall.Should().HaveCount(Size * Size, "the distance cube holds one inverse distance per texel");
            int center = Size / 2 * Size + Size / 2;
            ((float)wall[center])
                .Should()
                .BeApproximately(250f, 0.5f, "the wall is 4 m ahead at the face's center, 1000 m over 4 m");
            // One rounding to a half float, within one unit in the last place, 2⁻¹⁰ of the value at most: Vulkan lets a
            // device round toward zero.
            WorstErrorOfAWallAhead(face: wall, distance: 4f)
                .Should()
                .BeLessThan(1e-3f, "each texel holds 1000 m over its own radial distance, within a half float");
            Vector3 up = CubeFaces.Direction(CubeFace.PositiveY, (Size / 2 + 0.5f) / Size, (Size / 2 + 0.5f) / Size);
            ((float)EnvironmentScenes.Distances(renderer, CubeFace.PositiveY)[center])
                .Should()
                .BeApproximately(1000f * up.Y / 100e3f, 1e-5f, "the middle range stores its surface too");
            EnvironmentScenes
                .Distances(renderer, CubeFace.NegativeX)
                .Should()
                .OnlyContain(distance => (float)distance == 0f, "the sky is infinitely far, an inverse distance of 0");
        }
        finally
        {
            Gpu.DisposeAndAssertClean(renderer);
        }
    }

    /// <summary>
    /// A surface 10 cm from the eye, in the near range, stores its own inverse distance, up to 10,000 km⁻¹: the
    /// floor of the distance in environment_face.frag, the near plane of 5 cm, lies below every distance that a face
    /// draws, so it only keeps a division by 0 out.
    /// </summary>
    [Fact]
    public void ASurfaceTenCentimetersFromTheEyeStoresItsTrueInverseDistance()
    {
        VulkanRenderer renderer = EnvironmentScenes.Renderer();
        try
        {
            RenderSnapshot snapshot = EnvironmentScenes.Dark();
            snapshot.Instances.Add(
                EnvironmentScenes.Square(
                    plane: renderer.CreateMesh(Primitives.Plane(1f)),
                    material: EnvironmentScenes.Matte(renderer),
                    center: new(0.1f, 0, 0),
                    halfSize: 1,
                    chainDepth: TestChains.CamerasFrame
                )
            );

            renderer.Render(snapshot, 1f);

            // One rounding to a half float, within one unit in the last place, 2⁻¹⁰ of the value at most.
            WorstErrorOfAWallAhead(face: EnvironmentScenes.Distances(renderer, CubeFace.PositiveX), distance: 0.1f)
                .Should()
                .BeLessThan(1e-3f, "each texel holds 1000 m over its own radial distance, however near the surface");
        }
        finally
        {
            Gpu.DisposeAndAssertClean(renderer);
        }
    }

    /// <summary>
    /// The largest relative error of the inverse distances of face +X, <paramref name="face"/>, against a wall on the
    /// plane x = <paramref name="distance"/> m that fills the face: a texel's direction meets it distance / x away.
    /// </summary>
    private static float WorstErrorOfAWallAhead(Half[] face, float distance)
    {
        float worst = 0;
        for (int y = 0; y < Size; y++)
        for (int x = 0; x < Size; x++)
        {
            Vector3 direction = CubeFaces.Direction(CubeFace.PositiveX, (x + 0.5f) / Size, (y + 0.5f) / Size);
            float expected = 1000f * direction.X / distance;
            worst = MathF.Max(worst, MathF.Abs((float)face[y * Size + x] - expected) / expected);
        }

        return worst;
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
    public void AGlowOfAnEntityThatHoldsALightAddsNothingToTheMap()
    {
        // The entity sends its power once, through its light, and its glow is that light's face (docs/plans/environment-
        // map.md, "How a face is lit"). Else the map's irradiance lights the room with the lamp's power a second time.
        VulkanRenderer renderer = EnvironmentScenes.Renderer();
        try
        {
            RenderSnapshot snapshot = EnvironmentScenes.Dark();
            RenderInstance panel = EnvironmentScenes.Square(
                plane: renderer.CreateMesh(Primitives.Plane(1f)),
                material: EnvironmentScenes.Emissive(renderer, new(PanelGlow)),
                center: new(10, 0, 0),
                halfSize: 5,
                chainDepth: TestChains.CamerasFrame
            );
            panel.EntityIndex = PanelEntity;
            snapshot.Instances.Add(panel);
            // Behind the panel, the lamp's light reaches only the panel's back.
            snapshot.Lights.Add(
                Gpu.Lamp(
                    position: new(12, 0, 0),
                    color: Vector3.One,
                    intensity: 1,
                    chainDepth: TestChains.CamerasFrame
                ) with
                {
                    EntityIndex = PanelEntity,
                }
            );
            // A glowing wall behind the lamp fills the face. Where the panel covers it, the face reads 0 only if it draws
            // the panel dark, not if it leaves the panel out.
            RenderInstance wall = EnvironmentScenes.Square(
                plane: renderer.CreateMesh(Primitives.Plane(1f)),
                material: EnvironmentScenes.Emissive(renderer, new(PanelGlow)),
                center: new(20, 0, 0),
                halfSize: 10,
                chainDepth: TestChains.CamerasFrame
            );
            wall.EntityIndex = WallEntity;
            snapshot.Instances.Add(wall);

            renderer.Render(snapshot, 1f);

            EnvironmentScenes
                .Center(EnvironmentScenes.Face(renderer, CubeFace.PositiveX))
                .Should()
                .Be(
                    Vector3.Zero,
                    "the black panel hides the wall and reflects nothing, and its glow is its light's face"
                );
        }
        finally
        {
            Gpu.DisposeAndAssertClean(renderer);
        }
    }

    [Fact]
    public void AGlowWithoutALightOfItsOwnStaysInTheMap()
    {
        VulkanRenderer renderer = EnvironmentScenes.Renderer();
        try
        {
            RenderSnapshot snapshot = EnvironmentScenes.Dark();
            RenderInstance panel = EnvironmentScenes.Square(
                plane: renderer.CreateMesh(Primitives.Plane(1f)),
                material: EnvironmentScenes.Emissive(renderer, new(PanelGlow)),
                center: new(10, 0, 0),
                halfSize: 5,
                chainDepth: TestChains.CamerasFrame
            );
            panel.EntityIndex = PanelEntity;
            snapshot.Instances.Add(panel);
            // Behind the panel, the lamp's light reaches only the panel's back. The lamp is another entity's.
            snapshot.Lights.Add(
                Gpu.Lamp(position: new(12, 0, 0), color: Vector3.One, intensity: 1, chainDepth: TestChains.CamerasFrame)
            );

            renderer.Render(snapshot, 1f);

            EnvironmentScenes.ShouldBeNear(
                EnvironmentScenes.Center(EnvironmentScenes.Face(renderer, CubeFace.PositiveX)),
                new(PanelGlow),
                "a glow with no light of its own, such as a screen's, is a source of its own"
            );
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

    /// <summary>
    /// The faces share one surface record (<see cref="FaceSurface"/>), and each face starts it anew. So a texel that the
    /// face before covered with a surface shows the sky in a face that sees none there.
    /// </summary>
    [Fact]
    public void AFaceShowsTheSkyWhereTheFaceDrawnBeforeItSawASurface()
    {
        VulkanRenderer renderer = EnvironmentScenes.Renderer(facesPerFrame: CubeFaces.Count);
        try
        {
            MeshId plane = renderer.CreateMesh(Primitives.Plane(1f));
            RenderSnapshot snapshot = EnvironmentScenes.Dark();
            snapshot.Instances.Add(
                EnvironmentScenes.Square(
                    plane: plane,
                    material: EnvironmentScenes.Emissive(renderer, new(1, 0.5f, 0.25f)),
                    center: new(10, 0, 0),
                    halfSize: 4,
                    chainDepth: TestChains.CamerasFrame
                )
            );

            // Two frames of six faces each: every face but +X follows a face in some frame, whatever the order.
            EnvironmentScenes.Still(renderer: renderer, snapshot: snapshot, time: 0);
            EnvironmentScenes.Still(renderer: renderer, snapshot: snapshot, time: 1);

            Middle(CubeFace.PositiveX).W.Should().Be(0f, "face +X sees the square, so the test can fail");
            for (int layer = 1; layer < CubeFaces.Count; layer++)
                Middle((CubeFace)layer)
                    .Should()
                    .Be(new Vector4(x: 0, y: 0, z: 0, w: 1), "face {0} sees only the dark sky", (CubeFace)layer);

            Vector4 Middle(CubeFace face) =>
                EnvironmentScenes.Texel(
                    face: EnvironmentScenes.Face(renderer, face),
                    size: Size,
                    x: Size / 2,
                    y: Size / 2
                );
        }
        finally
        {
            Gpu.DisposeAndAssertClean(renderer);
        }
    }

    /// <summary>
    /// The middle texel of face +X, which sees a wall of <paramref name="material"/> 5 m away that faces the eye, under a
    /// sun of <paramref name="irradiance"/> 45° off the wall's normal.
    /// </summary>
    private static Vector3 WallUnderASlantedSun(Func<VulkanRenderer, MaterialId> material, out Vector3 irradiance)
    {
        VulkanRenderer renderer = EnvironmentScenes.Renderer();
        try
        {
            MeshId plane = renderer.CreateMesh(Primitives.Plane(1f));
            RenderSnapshot snapshot = Gpu.Snapshot(
                starDirection: Vector3.Normalize(new(x: -1, y: 1, z: 0)),
                starIrradiance: 2f
            );
            snapshot.Ambient.Intensity = 0;
            irradiance = Gpu.StarIrradianceRgb(snapshot);
            snapshot.Instances.Add(
                EnvironmentScenes.Facing(
                    plane: plane,
                    material: material(renderer),
                    center: new(5, 0, 0),
                    normal: -Vector3.UnitX,
                    halfSize: 3,
                    chainDepth: TestChains.CamerasFrame
                )
            );
            renderer.Render(snapshot, 1f);
            return EnvironmentScenes.Center(EnvironmentScenes.Face(renderer, CubeFace.PositiveX));
        }
        finally
        {
            Gpu.DisposeAndAssertClean(renderer);
        }
    }

    /// <summary>A material of base color (0.8, 0.6, 0.4) with <paramref name="roughness"/> and <paramref name="reflectance"/>, of metallic share <paramref name="metallic"/>.</summary>
    private static MaterialId Brownish(VulkanRenderer renderer, float roughness, float reflectance, float metallic) =>
        renderer.CreateMaterial(
            new(
                BaseColor: new(x: 0.8f, y: 0.6f, z: 0.4f, w: 1),
                Roughness: roughness,
                Reflectance: reflectance,
                Model: new StandardShading(metallic)
            )
        );

    [Fact]
    public void AMatteWallInAFaceSendsItsBaseColorTimesTheSunsIrradiance()
    {
        Vector3 shown = WallUnderASlantedSun(
            renderer => Brownish(renderer: renderer, roughness: 1, reflectance: 0, metallic: 0),
            out Vector3 irradiance
        );

        // Lambert: b E cos 45° / π.
        Vector3 expected = new Vector3(x: 0.8f, y: 0.6f, z: 0.4f) * irradiance * (MathF.Sqrt(0.5f) / MathF.PI);
        Vector3
            .Distance(shown, expected)
            .Should()
            .BeLessThan(0.03f * expected.Length(), "the wall shows {0}, and Lambert gives {1}", shown, expected);
    }

    [Fact]
    public void APolishedMetalInAFaceSendsNoDiffuseLight()
    {
        Vector3 matte = WallUnderASlantedSun(
            renderer => Brownish(renderer: renderer, roughness: 1, reflectance: 0, metallic: 0),
            out _
        );
        // The eye stands 45° off the sun's mirror direction, far outside the lobe of roughness 0.2.
        Vector3 metal = WallUnderASlantedSun(
            renderer => Brownish(renderer: renderer, roughness: 0.2f, reflectance: 0.5f, metallic: 1),
            out _
        );

        matte.X.Should().BeGreaterThan(0.1f, "the matte wall is lit, so the test can fail");
        metal.X.Should().BeLessThan(0.1f * matte.X, "a metal has no diffuse part, and its highlight points elsewhere");
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void AFaceIsLitByATubeAndByARectangleLight(bool rectangle)
    {
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
            // Behind the camera, 2 m before the wall. The rectangle's front, its local +Z, faces the wall.
            var position = new Vector3(x: 0, y: 0, z: 20);
            snapshot.Lights.Add(
                rectangle
                    ? new RectRenderLight(
                        EntityIndex: Gpu.LampEntity,
                        EntityGeneration: 0,
                        Position: position,
                        PreviousPosition: position,
                        Rotation: Quaternion.Identity,
                        PreviousRotation: Quaternion.Identity,
                        Color: Vector3.One,
                        Power: 600,
                        Width: 1,
                        Height: 1,
                        CastsShadow: false,
                        ChainDepth: TestChains.CamerasFrame
                    )
                    : new TubeRenderLight(
                        EntityIndex: Gpu.LampEntity,
                        EntityGeneration: 0,
                        Position: position,
                        PreviousPosition: position,
                        Axis: Vector3.UnitX,
                        PreviousAxis: Vector3.UnitX,
                        Color: Vector3.One,
                        Power: 600,
                        Radius: 0.05f,
                        Length: 1,
                        CastsShadow: false,
                        ChainDepth: TestChains.CamerasFrame
                    )
            );

            renderer.Render(snapshot, 1f);

            EnvironmentScenes
                .Center(EnvironmentScenes.Face(renderer, CubeFace.PositiveZ))
                .X.Should()
                .BeGreaterThan(0.5f, "the light lights the wall behind the camera");
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
                    Alpha = new GlassAlpha(),
                }
            );
            snapshot.Instances.Add(
                EnvironmentScenes.Facing(
                    plane: plane,
                    material: glass,
                    center: new(5, 0, 0),
                    normal: -Vector3.UnitX,
                    halfSize: 3,
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
