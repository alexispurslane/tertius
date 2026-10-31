using System.Numerics;
using Silk.NET.Vulkan;
using Tertius.Testing;
using static Tertius.Rendering.Tests.BrdfTesting;

namespace Tertius.Rendering.Tests;

/// <summary>
/// Hair shaded as fibers on the GPU (docs/plans/skin-and-hair.md, "The hair model", "Hair in each pass"): the light of a
/// hair pixel depends on its strand, the eye and the light, never on its card's plane. The cards are the plane mesh,
/// 2 m square, 3 m ahead, whose strands run along +v. The exposure is fixed at 0 stops, so the HDR image holds radiance.
/// </summary>
[Collection(GpuCollection.Name)]
public sealed class HairRenderTests
{
    private const int Width = 160,
        Height = 90;

    /// <summary>The relative rounding of a half float, with room for the sum of two.</summary>
    private const float HalfRounding = 2f / 1024;

    /// <summary>Values that the specular target stores exactly: 89 and 138 of 255.</summary>
    private const float Roughness = 89f / 255,
        Reflectance = 138f / 255;

    /// <summary>Decoded sRGB bytes, so that the base color target stores them exactly.</summary>
    private static readonly Double3 Brown = new(Decoded(180), Decoded(120), Decoded(70));

    private static readonly HairFiber BrownHair = new(BaseColor: Brown, Roughness: Roughness, Reflectance: Reflectance);

    /// <summary>The middle of the image, which sees the cards' centers.</summary>
    private static readonly Vector2 Ahead = new(Width / 2, Height / 2);

    private static readonly Vector3 CardCenter = new(0, 0, -3);

    /// <summary>The sky and ground of the ambient light: distinct colors, so that a wrong sky share shows.</summary>
    private static readonly Vector3 Sky = new(0.2f, 0.3f, 0.6f);

    private static readonly Vector3 Ground = new(0.35f, 0.2f, 0.08f);

    /// <summary>
    /// The rotation that turns the plane mesh's front, +Y, to the unit <paramref name="normal"/>, and its +Z, along which
    /// its texture's v grows, to the unit <paramref name="strand"/>, which must be at right angles to it.
    /// </summary>
    private static Quaternion CardWith(Vector3 normal, Vector3 strand)
    {
        var alongU = Vector3.Cross(normal, strand);
        var basis = new Matrix4x4(
            m11: alongU.X,
            m12: alongU.Y,
            m13: alongU.Z,
            m14: 0,
            m21: normal.X,
            m22: normal.Y,
            m23: normal.Z,
            m24: 0,
            m31: strand.X,
            m32: strand.Y,
            m33: strand.Z,
            m34: 0,
            m41: 0,
            m42: 0,
            m43: 0,
            m44: 1
        );
        return Quaternion.Normalize(Quaternion.CreateFromRotationMatrix(basis));
    }

    /// <summary>A card of <paramref name="material"/> at <paramref name="center"/>, of half size 1 scaled by <paramref name="scale"/>.</summary>
    private static RenderInstance Card(
        MeshId plane,
        MaterialId material,
        Quaternion rotation,
        Vector3? center = null,
        Vector3? scale = null
    ) =>
        Gpu.Instance(
            mesh: plane,
            material: material,
            position: center ?? CardCenter,
            scale: scale ?? Vector3.One,
            rotation: rotation,
            radius: 1.5f * MathF.Max(MathF.Max((scale ?? Vector3.One).X, (scale ?? Vector3.One).Z), 1),
            chainDepth: TestChains.CamerasFrame
        );

    private static Quaternion FacingTheCamera => CardWith(normal: Vector3.UnitZ, strand: Vector3.UnitY);

    /// <summary>The card of <see cref="FacingTheCamera"/> turned about its strand to face away: the eye sees its back.</summary>
    private static Quaternion TurnedAway => CardWith(normal: -Vector3.UnitZ, strand: Vector3.UnitY);

    /// <summary>A material of the brown hair's colors, of <paramref name="model"/>, drawn as <paramref name="alpha"/>.</summary>
    private static MaterialId Brownish(VulkanRenderer renderer, ShadingModel model, AlphaMode alpha = default) =>
        renderer.CreateMaterial(
            new(
                BaseColor: new(x: (float)Brown.X, y: (float)Brown.Y, z: (float)Brown.Z, w: 1),
                Roughness: Roughness,
                Reflectance: Reflectance,
                Model: model,
                Alpha: alpha
            )
        );

    private static MaterialId Hair(VulkanRenderer renderer, AlphaMode alpha = default) =>
        Brownish(renderer, new HairShading(StrandDirection.PlusV), alpha);

    private static MaterialId Standard(VulkanRenderer renderer) => Brownish(renderer, new StandardShading(0));

    /// <summary>A snapshot with no ambient light and the sun along <paramref name="toSun"/>.</summary>
    private static RenderSnapshot SunOnly(Vector3 toSun, float irradiance = 2)
    {
        RenderSnapshot snapshot = Gpu.Snapshot(Vector3.Normalize(toSun), starIrradiance: irradiance);
        snapshot.Ambient.Intensity = 0;
        return snapshot;
    }

    /// <summary>A snapshot lit by the hemisphere of <see cref="Sky"/> and <see cref="Ground"/> alone.</summary>
    private static RenderSnapshot AmbientOnly()
    {
        RenderSnapshot snapshot = Gpu.Snapshot(Vector3.UnitY, starIrradiance: 0);
        snapshot.Ambient = new()
        {
            SkyColor = Sky,
            GroundColor = Ground,
            Intensity = 1,
        };
        return snapshot;
    }

    /// <summary>The RGB of <paramref name="pixel"/> in an image of RGBA per pixel, <see cref="Width"/> wide.</summary>
    private static Vector3 At(float[] image, Vector2 pixel)
    {
        int i = ((int)pixel.Y * Width + (int)pixel.X) * 4;
        return new(image[i], image[i + 1], image[i + 2]);
    }

    /// <summary>Renders <paramref name="snapshot"/> with <paramref name="instances"/> added and gives the HDR image.</summary>
    private static float[] Rendered(VulkanRenderer renderer, RenderSnapshot snapshot, params RenderInstance[] instances)
    {
        snapshot.Instances.AddRange(instances);
        renderer.Render(snapshot, 1f);
        return renderer.ReadHdr();
    }

    /// <summary>Asserts that each channel of <paramref name="actual"/> is <paramref name="expected"/>'s within <paramref name="relative"/> of the largest.</summary>
    private static void ShouldBeNear(Vector3 actual, Vector3 expected, float relative, string because)
    {
        float tolerance = relative * MathF.Max(MathF.Max(expected.X, expected.Y), MathF.Max(expected.Z, 1e-3f));
        LargestChannelDifference(actual, expected)
            .Should()
            .BeLessThanOrEqualTo(tolerance, "{0}: shown {1}, expected {2}", because, actual, expected);
    }

    /// <summary>
    /// The shading normal that the normal target <paramref name="normals"/> holds at pixel <paramref name="index"/>: the C#
    /// copy of octDecode in octahedral.glsl.
    /// </summary>
    private static Double3 StoredNormal(float[] normals, int index)
    {
        var n = new Double3(normals[index * 4], normals[index * 4 + 1], 0);
        n = n with { Z = 1 - Math.Abs(n.X) - Math.Abs(n.Y) };
        if (n.Z < 0)
            n = new((1 - Math.Abs(n.Y)) * (n.X >= 0 ? 1 : -1), (1 - Math.Abs(n.X)) * (n.Y >= 0 ? 1 : -1), n.Z);
        return n.Normalized();
    }

    /// <summary>What the G-buffer of the last frame holds of a hair pixel.</summary>
    private readonly record struct StoredFiber(Double3 Position, Double3 Strand, Double3 FiberNormal);

    /// <summary>
    /// The position, the strand and the fiber normal of <paramref name="pixel"/> as the direct pass reads them: the ray
    /// times the distance, the 8-bit strand code and the 16-bit shading normal.
    /// </summary>
    private static StoredFiber StoredAt(VulkanRenderer renderer, Vector2 pixel)
    {
        int index = (int)pixel.Y * Width + (int)pixel.X;
        float[] normals = renderer.ReadNormals();
        byte[] shading = renderer.ReadShading();
        double distance = renderer.ReadDistance()[index];
        return new(
            Position: Ray(pixel, Width, Height) * distance,
            Strand: StrandCode.Decode(shading[index * 4], shading[index * 4 + 1]),
            FiberNormal: StoredNormal(normals, index)
        );
    }

    /// <summary>The C# light of <paramref name="fiber"/> toward the eye from a light along <paramref name="toLight"/> of <paramref name="irradiance"/>.</summary>
    private static Vector3 FiberLight(StoredFiber fiber, HairFiber hair, Double3 toLight, Double3 irradiance)
    {
        DirectTerms terms = hair.Reflected(
            strand: fiber.Strand,
            fiberNormal: fiber.FiberNormal,
            toView: -fiber.Position.Normalized(),
            toLight: toLight
        );
        return ((terms.Diffuse + terms.Specular) * irradiance).ToVector3();
    }

    /// <summary>
    /// The direction toward a lamp of radius 0 at <paramref name="lamp"/>, of 5 W/sr and white, from
    /// <paramref name="position"/>, and its irradiance there, with the window to its reach under FixedExposure(0).
    /// </summary>
    private static (Double3 ToLight, Double3 Irradiance) LampAt(Double3 lamp, Double3 position)
    {
        const double intensity = 5;
        double distanceSquared = (lamp - position).LengthSquared;
        double reach = LightUnits.Reach(intensity, LightUnits.CutoffIrradiance(0));
        double ratio = distanceSquared / (reach * reach);
        double window = Math.Pow(Math.Clamp(1 - ratio * ratio, 0, 1), 2);
        return (
            ToLight: (lamp - position).Normalized(),
            Irradiance: new Double3(1, 1, 1) * (intensity * window / distanceSquared)
        );
    }

    [Theory]
    [InlineData(0.3f, 0.6f, 0.75f, false)]
    [InlineData(-0.5f, 0.2f, 0.84f, false)]
    [InlineData(0f, 0.95f, 0.3f, false)]
    [InlineData(0.4f, -0.5f, 0.77f, false)]
    [InlineData(1f, 0.5f, -2f, true)]
    [InlineData(-0.6f, -0.8f, -4.2f, true)]
    public void AHairPixelSendsTheLightThatHairFiberGives(float x, float y, float z, bool lamp)
    {
        VulkanRenderer renderer = Gpu.Headless(width: Width, height: Height);
        try
        {
            MeshId plane = renderer.CreateMesh(Primitives.Plane(1));
            RenderSnapshot snapshot = SunOnly(lamp ? Vector3.UnitY : new(x, y, z), irradiance: lamp ? 0 : 2);
            if (lamp)
                snapshot.Lights.Add(
                    Gpu.Lamp(position: new(x, y, z), color: Vector3.One, intensity: 5, chainDepth: TestChains.Root)
                );

            Vector3 shown = At(
                Rendered(renderer, snapshot, Card(plane: plane, material: Hair(renderer), rotation: FacingTheCamera)),
                Ahead
            );
            StoredFiber fiber = StoredAt(renderer, Ahead);
            (Double3 toLight, Double3 irradiance) = lamp
                ? LampAt(new(x, y, z), fiber.Position)
                : (
                    ToLight: (Double3)Vector3.Normalize(new(x, y, z)),
                    Irradiance: (Double3)Gpu.StarIrradianceRgb(snapshot)
                );

            ShouldBeNear(
                actual: shown,
                expected: FiberLight(fiber: fiber, hair: BrownHair, toLight: toLight, irradiance: irradiance),
                relative: HalfRounding,
                because: "the direct pass shades the stored strand as the C# fiber does"
            );
        }
        finally
        {
            Gpu.DisposeAndAssertClean(renderer);
        }
    }

    [Fact]
    public void APlusVCardStoresAStrandThatRunsTheWayItsTextureVGrows()
    {
        // A quarter turn about +X: the plane's front, +Y, faces the camera, and its +Z, where v grows, turns to -Y.
        var rotation = Quaternion.CreateFromAxisAngle(Vector3.UnitX, MathF.PI / 2);
        Vertex[] vertices = Primitives.Plane(1).Vertices;
        var vGrows = Vector3.Normalize(
            vertices.Single(each => each.TexCoord == new Vector2(0, 1)).Position
                - vertices.Single(each => each.TexCoord == new Vector2(0, 0)).Position
        );
        VulkanRenderer renderer = Gpu.Headless(width: Width, height: Height);
        try
        {
            MeshId plane = renderer.CreateMesh(Primitives.Plane(1));
            Rendered(
                renderer,
                SunOnly(Vector3.UnitZ),
                Card(plane: plane, material: Hair(renderer), rotation: rotation)
            );

            Double3
                .Dot(StoredAt(renderer, Ahead).Strand, Vector3.Transform(vGrows, rotation))
                .Should()
                .BeGreaterThan(0.99, "the strand of a PlusV card runs from the texture's v = 0 toward its v = 1");
        }
        finally
        {
            Gpu.DisposeAndAssertClean(renderer);
        }
    }

    /// <summary>The screen-space light of a test: the renderer's default, or off.</summary>
    private static ScreenSpaceLightSetting ScreenSpaceLight(bool on) =>
        on ? VisibilityBitmaskLight.Default : new NoScreenSpaceLight();

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void TwoCardsWithOneStrandAndDifferentNormalsShadeAlike(bool screenSpaceLight)
    {
        // The same strand, +Y, and a card turned 40° about it.
        Quaternion turned = CardWith(normal: new(MathF.Sin(0.7f), 0, MathF.Cos(0.7f)), strand: Vector3.UnitY);
        Vector3 ShownAhead(bool hair, Quaternion rotation)
        {
            VulkanRenderer renderer = Gpu.Headless(
                width: Width,
                height: Height,
                screenSpaceLight: ScreenSpaceLight(screenSpaceLight)
            );
            try
            {
                MeshId plane = renderer.CreateMesh(Primitives.Plane(1));
                RenderSnapshot snapshot = AmbientOnly();
                Gpu.Star(snapshot: snapshot, irradiance: 2, direction: Vector3.Normalize(new(0.5f, 0.3f, 0.81f)));
                MaterialId material = hair ? Hair(renderer) : Standard(renderer);
                return At(
                    Rendered(renderer, snapshot, Card(plane: plane, material: material, rotation: rotation)),
                    Ahead
                );
            }
            finally
            {
                Gpu.DisposeAndAssertClean(renderer);
            }
        }

        Vector3 facing = ShownAhead(hair: true, FacingTheCamera);
        ShouldBeNear(
            actual: ShownAhead(hair: true, turned),
            expected: facing,
            // Under the screen-space light, 1%: the tolerance of TheHairsAmbientLightIsTheSourcesAtTheFiberNormal.
            relative: screenSpaceLight ? 0.01f : 4 * HalfRounding,
            because: "a fiber's light does not depend on its card's plane"
        );
        Vector3 standardFacing = ShownAhead(hair: false, FacingTheCamera);
        LargestChannelDifference(ShownAhead(hair: false, turned), standardFacing)
            .Should()
            .BeGreaterThan(0.05f * standardFacing.X, "the same cards as standard shade apart, so the test can fail");
    }

    /// <summary>
    /// A card of the plane mesh's size and frame, bent along its strand into two segments side by side, each turned
    /// 15° from the plane mesh's front, so the ridge between them stands toward the front. It is smooth-shaded: the
    /// ridge's vertices take the front, and each outer edge's vertices their segment's normal. Each segment is one card's
    /// half, so the mesh holds one card.
    /// </summary>
    private static MeshData RidgeCard()
    {
        const float halfBend = 15 * MathF.PI / 180;
        float c = MathF.Cos(halfBend),
            s = MathF.Sin(halfBend);
        (Vector3 Position, Vector3 Normal, Vector3 Tangent, float U)[] columns =
        [
            (Position: new(-c, -s, 0), Normal: new(-s, c, 0), Tangent: new(c, s, 0), U: 0),
            (Position: Vector3.Zero, Normal: Vector3.UnitY, Tangent: Vector3.UnitX, U: 0.5f),
            (Position: new(c, -s, 0), Normal: new(s, c, 0), Tangent: new(c, -s, 0), U: 1),
        ];
        // The row at z = +1 has v = 1, as the plane mesh's, and the row at z = -1 v = 0.
        Vertex[] vertices =
        [
            .. new[] { 1f, -1f }.SelectMany(z =>
                columns.Select(column => new Vertex
                {
                    Position = column.Position + new Vector3(0, 0, z),
                    Normal = column.Normal,
                    Tangent = new(column.Tangent, 1),
                    TexCoord = new(column.U, (z + 1) / 2),
                })
            ),
        ];
        // Each segment as the plane mesh's quad: top left, top right, bottom right, then top left, bottom right, bottom left.
        return new(vertices, [0, 1, 4, 0, 4, 3, 1, 2, 5, 1, 5, 4]);
    }

    [Fact]
    public void UnderTheScreenSpaceLightABentSmoothShadedCardShadesAsAFlatCard()
    {
        // The ridge card turned 40° about its strand, +Y, against the flat card that faces the camera: at the ridge and
        // on each segment, their fibers have one strand and one view, so one ambient light.
        Quaternion turned = CardWith(normal: new(MathF.Sin(0.7f), 0, MathF.Cos(0.7f)), strand: Vector3.UnitY);
        Vector2[] pixels = [Ahead, Ahead + new Vector2(-8, 0), Ahead + new Vector2(8, 0)];
        Vector3[] ShownAt(bool bent, Quaternion rotation)
        {
            VulkanRenderer renderer = Gpu.Headless(
                width: Width,
                height: Height,
                screenSpaceLight: VisibilityBitmaskLight.Default
            );
            try
            {
                MeshId mesh = renderer.CreateMesh(bent ? RidgeCard() : Primitives.Plane(1));
                float[] hdr = Rendered(
                    renderer,
                    AmbientOnly(),
                    Card(plane: mesh, material: Hair(renderer), rotation: rotation)
                );
                return [.. pixels.Select(pixel => At(hdr, pixel))];
            }
            finally
            {
                Gpu.DisposeAndAssertClean(renderer);
            }
        }

        Vector3[] flat = ShownAt(bent: false, FacingTheCamera);
        Vector3[] bent = ShownAt(bent: true, turned);
        for (int i = 0; i < pixels.Length; i++)
            ShouldBeNear(
                actual: bent[i],
                expected: flat[i],
                // 1%: the tolerance of TwoCardsWithOneStrandAndDifferentNormalsShadeAlike.
                relative: 0.01f,
                because: $"a bent card's own points block none of its fibers' light at pixel {pixels[i]}"
            );
    }

    [Fact]
    public void UnderTheScreenSpaceLightOneCardOfAHairMeshDarkensAnotherCardOfTheSameMeshBehindIt()
    {
        // One mesh of two cards: the plane mesh, and a copy of half its size 0.2 m in front of it and beside the line
        // of sight ahead, from 0.1 m to its right, as in UnderTheScreenSpaceLightAHairCardDarkensAHairCardBehindIt. The
        // plane mesh's +X turns to the world's -X.
        static MeshData TwoCards(bool withCardInFront)
        {
            MeshData plane = Primitives.Plane(1);
            if (!withCardInFront)
                return plane;
            Vertex[] front =
            [
                .. plane.Vertices.Select(vertex =>
                    vertex with
                    {
                        Position = vertex.Position * 0.5f + new Vector3(-0.6f, 0.2f, 0),
                    }
                ),
            ];
            return new(
                [.. plane.Vertices, .. front],
                [.. plane.Indices, .. plane.Indices.Select(index => index + (uint)plane.Vertices.Length)]
            );
        }

        static Vector3 ShownAhead(bool withCardInFront)
        {
            VulkanRenderer renderer = Gpu.Headless(
                width: Width,
                height: Height,
                screenSpaceLight: VisibilityBitmaskLight.Default
            );
            try
            {
                MeshId mesh = renderer.CreateMesh(TwoCards(withCardInFront));
                return At(
                    Rendered(
                        renderer,
                        AmbientOnly(),
                        Card(plane: mesh, material: Hair(renderer), rotation: FacingTheCamera)
                    ),
                    Ahead
                );
            }
            finally
            {
                Gpu.DisposeAndAssertClean(renderer);
            }
        }

        Vector3 alone = ShownAhead(withCardInFront: false);
        ShownAhead(withCardInFront: true)
            .X.Should()
            .BeLessThan(
                0.95f * alone.X,
                "the mesh's card in front blocks a part of the ambient light of its card behind"
            );
    }

    [Fact]
    public void UnderTheScreenSpaceLightTheShadingTargetIsVisibleToTheComputeReadsOfTheOcclusionSectorsPass()
    {
        // No reflections, whose compute passes read the shading target too: the sectors pass reads it alone.
        VulkanRenderer renderer = Gpu.Headless(
            width: Width,
            height: Height,
            screenSpaceLight: VisibilityBitmaskLight.Default
        );
        try
        {
            MeshId plane = renderer.CreateMesh(Primitives.Plane(1));
            Rendered(renderer, AmbientOnly(), Card(plane: plane, material: Hair(renderer), rotation: FacingTheCamera));

            renderer
                .ShadingTargetState.Stages.HasFlag(PipelineStageFlags2.ComputeShaderBit)
                .Should()
                .BeTrue(
                    "the sectors pass reads a hair receiver's model code in a compute shader (Vulkan spec, memory dependencies)"
                );
        }
        finally
        {
            Gpu.DisposeAndAssertClean(renderer);
        }
    }

    [Fact]
    public void UnderTheScreenSpaceLightAHairCardDarkensAHairCardBehindIt()
    {
        // A card of half size 0.5, 0.2 m in front of the other and beside the line of sight ahead, from 0.1 m to its
        // right: within the default radius of 1 m.
        static Vector3 ShownAhead(bool withCardInFront)
        {
            VulkanRenderer renderer = Gpu.Headless(
                width: Width,
                height: Height,
                screenSpaceLight: VisibilityBitmaskLight.Default
            );
            try
            {
                MeshId plane = renderer.CreateMesh(Primitives.Plane(1));
                MaterialId hair = Hair(renderer);
                RenderInstance behind = Card(plane: plane, material: hair, rotation: FacingTheCamera);
                RenderInstance[] cards = withCardInFront
                    ?
                    [
                        behind,
                        Card(
                            plane: plane,
                            material: hair,
                            rotation: FacingTheCamera,
                            center: new(0.6f, 0, -2.8f),
                            scale: new(0.5f)
                        ),
                    ]
                    : [behind];
                return At(Rendered(renderer, AmbientOnly(), cards), Ahead);
            }
            finally
            {
                Gpu.DisposeAndAssertClean(renderer);
            }
        }

        Vector3 alone = ShownAhead(withCardInFront: false);
        ShownAhead(withCardInFront: true)
            .X.Should()
            .BeLessThan(0.95f * alone.X, "the card in front blocks a part of the ambient light of the card behind it");
    }

    /// <summary>A dark, smooth hair: its primary highlight stands out along a card's middle column.</summary>
    private static readonly HairFiber DarkSmoothHair = new(
        BaseColor: new(Decoded(30), Decoded(30), Decoded(30)),
        Roughness: 0.2,
        Reflectance: 1
    );

    /// <summary>The rows of the middle column on the tall card, which spans ±28° of the field's ±30°.</summary>
    private static readonly IEnumerable<int> TallCardRows = Enumerable.Range(8, Height - 16);

    /// <summary>The brightest row of the tall card of <see cref="DarkSmoothHair"/> under a sun at <paramref name="elevation"/> rad.</summary>
    private static int ShownPeakRow(float elevation)
    {
        VulkanRenderer renderer = Gpu.Headless(width: Width, height: Height);
        try
        {
            MeshId plane = renderer.CreateMesh(Primitives.Plane(1));
            Double3 color = DarkSmoothHair.BaseColor;
            MaterialId material = renderer.CreateMaterial(
                new(
                    BaseColor: new(x: (float)color.X, y: (float)color.Y, z: (float)color.Z, w: 1),
                    Roughness: (float)DarkSmoothHair.Roughness,
                    Reflectance: (float)DarkSmoothHair.Reflectance,
                    Model: new HairShading(StrandDirection.PlusV)
                )
            );
            float[] hdr = Rendered(
                renderer,
                SunOnly(new(0, MathF.Sin(elevation), MathF.Cos(elevation))),
                Card(plane: plane, material: material, rotation: FacingTheCamera, scale: new(0.3f, 1, 1.6f))
            );
            return TallCardRows.MaxBy(row => At(hdr, new(Width / 2, row)).X);
        }
        finally
        {
            Gpu.DisposeAndAssertClean(renderer);
        }
    }

    /// <summary>The row where the C# fiber's highlight peaks, for <see cref="ShownPeakRow"/>.</summary>
    private static int ExpectedPeakRow(float elevation)
    {
        var toLight = new Double3(0, Math.Sin(elevation), Math.Cos(elevation));
        return TallCardRows.MaxBy(row =>
        {
            Double3 point = OnPlane(Ray(new(Width / 2, row), Width, Height), CardCenter, Double3.UnitZ);
            Double3 toView = -point.Normalized();
            Double3 fiberNormal = HairFiber.FiberNormal(Double3.UnitY, toView, Double3.UnitZ);
            return DarkSmoothHair
                .Reflected(strand: Double3.UnitY, fiberNormal: fiberNormal, toView: toView, toLight: toLight)
                .Specular.X;
        });
    }

    [Fact]
    public void TheHighlightMovesAlongTheStrandAsTheLightMoves()
    {
        float level = 0,
            low = -20 * MathF.PI / 180;
        int levelRow = ShownPeakRow(level);
        int lowRow = ShownPeakRow(low);

        levelRow.Should().BeCloseTo(ExpectedPeakRow(level), 1, "the highlight lies where the C# fiber's peaks");
        lowRow.Should().BeCloseTo(ExpectedPeakRow(low), 1);
        (lowRow - levelRow)
            .Should()
            .BeGreaterThan(20, "a light from lower down moves the highlight down the strand, toward its root");
    }

    [Fact]
    public void AHairPixelTakesLightFromBehindItsCard()
    {
        var toSun = Vector3.Normalize(new(0.2f, 0.3f, -0.93f));
        Vector3 ShownAhead(bool hair, out StoredFiber fiber, out Vector3 irradiance)
        {
            VulkanRenderer renderer = Gpu.Headless(width: Width, height: Height);
            try
            {
                MeshId plane = renderer.CreateMesh(Primitives.Plane(1));
                RenderSnapshot snapshot = SunOnly(toSun);
                irradiance = Gpu.StarIrradianceRgb(snapshot);
                MaterialId material = hair ? Hair(renderer) : Standard(renderer);
                Vector3 shown = At(
                    Rendered(renderer, snapshot, Card(plane: plane, material: material, rotation: FacingTheCamera)),
                    Ahead
                );
                fiber = StoredAt(renderer, Ahead);
                return shown;
            }
            finally
            {
                Gpu.DisposeAndAssertClean(renderer);
            }
        }

        Vector3 hairLight = ShownAhead(hair: true, out StoredFiber fiber, out Vector3 irradiance);
        Vector3 expected = FiberLight(fiber: fiber, hair: BrownHair, toLight: toSun, irradiance: irradiance);

        expected.X.Should().BeGreaterThan(0.05f, "the C# fiber takes the light from behind");
        ShouldBeNear(
            actual: hairLight,
            expected: expected,
            relative: HalfRounding,
            because: "a fiber is lit from every side"
        );
        ShownAhead(hair: false, out _, out _)
            .Should()
            .Be(Vector3.Zero, "a standard card takes no light from behind, so the test can fail");
    }

    [Fact]
    public void AHairCardLitFromBehindDoesNotShadowItself()
    {
        // The card casts into the sun's cascades. Its shadow lookups offset the point toward the light, off the card's
        // side that the light sees (docs/plans/skin-and-hair.md, decision 27).
        var toSun = Vector3.Normalize(new(0.2f, 0.3f, -0.93f));
        VulkanRenderer renderer = Gpu.Headless(width: Width, height: Height);
        try
        {
            MeshId plane = renderer.CreateMesh(Primitives.Plane(1));
            RenderSnapshot snapshot = SunOnly(toSun);
            Vector3 irradiance = Gpu.StarIrradianceRgb(snapshot);
            RenderInstance card = Card(plane: plane, material: Hair(renderer), rotation: FacingTheCamera) with
            {
                Flags = RenderFlags.Visible | RenderFlags.CastsShadow,
            };

            Vector3 shown = At(Rendered(renderer, snapshot, card), Ahead);
            Vector3 expected = FiberLight(
                fiber: StoredAt(renderer, Ahead),
                hair: BrownHair,
                toLight: toSun,
                irradiance: irradiance
            );

            ShouldBeNear(
                actual: shown,
                expected: expected,
                relative: 0.02f,
                because: "the card's own shadow map does not hide the side of it that the light sees"
            );
        }
        finally
        {
            Gpu.DisposeAndAssertClean(renderer);
        }
    }

    /// <summary>
    /// The world side of a texel of the sun's first cascade, which holds every card of these tests: the texel that
    /// <see cref="ShadowCascades.Fit"/> gives this camera's slice from the near plane to the first split.
    /// </summary>
    private static float FirstCascadeTexel()
    {
        float tanHalfY = MathF.Tan(Gpu.Fov / 2);
        float radius = ShadowCascades
            .BoundingSphere(
                near: DepthRanges.NearPlane,
                far: ShadowCascades.Split(0),
                tanHalfX: tanHalfY * Width / Height,
                tanHalfY: tanHalfY
            )
            .Radius;
        return 2 * radius / (ShadowMap.DefaultSize - 2 * ShadowCascades.MarginTexels);
    }

    [Fact]
    public void AHairCardLitFromBehindKeepsTheShadowOfACasterCloseBehindIt()
    {
        // The arithmetic, in texels of the first cascade (5.85 cm: a sphere of radius 59.6 m over 2,040 texels) and in
        // depth along the light. The shadow lookups take the card's normal turned toward the sun, at cosine 0.93 with
        // it: seen from the sun, the card's plane rises tan θ = 0.39 per texel along û = (−0.58, 0.81) of the light's
        // axes. A tap of the 4 × 4 kernel lies up to 1.5 texels off along each axis, so the receiver-plane term,
        // max(dot(slope, offset), 0), reaches 1.5 · (0.58 + 0.81) · 0.39 = 0.81 with that cosine, and 7.6 with the
        // floor 0.1 in its place (sampleCascade in shadows.glsl).
        // The caster stands parallel to the card, GapTexels in front of the lookup point, which the normal offset moves
        // sin θ = 0.36 texel off the card toward it. With the cosine 0.93, a tap's reference follows the card's plane
        // uphill and keeps the pixel's own depth downhill, so every tap sees the caster when the gap is above the
        // downhill drop, 0.81, the spread of a tap's 2 × 2 footprint, 0.54, the constant bias, 0.5, and the caster's
        // slope bias, at most 1.5 · 0.39 = 0.58: 2.4 in all. With the floor, a tap's reference rises above the plane by
        // 3.2 texels per texel of dot(û, offset), so the 4 taps with dot(û, offset) of 0.93 to 2.1 pass over the caster:
        // the card takes about a quarter of the sun.
        const float GapTexels = 3;
        var toSun = Vector3.Normalize(new(0.2f, 0.3f, -0.93f));
        Vector3 towardSun = -Vector3.UnitZ;
        float cosine = Vector3.Dot(towardSun, toSun),
            sine = MathF.Sqrt(1 - cosine * cosine);
        LightBasis light = LightBasis.FromDirection(toSun);
        float across = MathF.Abs(Vector3.Dot(towardSun, light.X)) + MathF.Abs(Vector3.Dot(towardSun, light.Y));
        float Reach(float receiverCosine) =>
            1.5f * across / MathF.Max(receiverCosine, ShadowPass.MinimumReceiverCosine);
        float trueNeed =
            Reach(cosine) + across / cosine + ShadowPass.ConstantBiasTexels + ShadowPass.SlopeBias * sine / cosine;
        trueNeed.Should().BeLessThan(GapTexels - 0.5f, "every tap sees the caster with the cosine turned to the sun");
        Reach(-cosine)
            .Should()
            .BeGreaterThan(GapTexels + 4, "the floored cosine lifts the uphill taps over the caster");
        CardCenter.Z.Should().BeGreaterThan(-ShadowCascades.BlendStart(0), "the card lies in the first cascade alone");

        VulkanRenderer renderer = Gpu.Headless(width: Width, height: Height);
        try
        {
            MeshId plane = renderer.CreateMesh(Primitives.Plane(1));
            RenderSnapshot snapshot = SunOnly(toSun);
            float gap = FirstCascadeTexel() * (GapTexels * cosine + ShadowPass.NormalOffsetTexels * sine);
            RenderInstance caster = Card(
                plane: plane,
                material: Standard(renderer),
                rotation: FacingTheCamera,
                center: CardCenter + towardSun * gap
            ) with
            {
                Flags = RenderFlags.Visible | RenderFlags.CastsShadow,
            };

            Vector3 shown = At(
                Rendered(
                    renderer,
                    snapshot,
                    Card(plane: plane, material: Hair(renderer), rotation: FacingTheCamera),
                    caster
                ),
                Ahead
            );
            Vector3 lit = FiberLight(
                fiber: StoredAt(renderer, Ahead),
                hair: BrownHair,
                toLight: toSun,
                irradiance: Gpu.StarIrradianceRgb(snapshot)
            );

            lit.X.Should().BeGreaterThan(0.05f, "the sun lights the card from behind where nothing stands in its way");
            // Every tap sees the caster, so the card shows 0. A quarter of the sun, the floored cosine's leak, is 12 times
            // the tolerance.
            LargestChannelDifference(shown, Vector3.Zero)
                .Should()
                .BeLessThanOrEqualTo(0.02f * lit.X, "the caster's shadow covers the card: shown {0}", shown);
        }
        finally
        {
            Gpu.DisposeAndAssertClean(renderer);
        }
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    [InlineData(true, true)]
    public void TheHairsAmbientLightIsTheSourcesAtTheFiberNormal(bool environmentMap, bool screenSpaceLight)
    {
        // A strand tilted 45° toward the eye, on a card turned 37° about it: its fiber normal looks down, its card's
        // normal sideways and down by other amounts.
        var strand = Vector3.Normalize(new(0, 1, 1));
        var downTowardTheEye = Vector3.Normalize(new(0, -1, 1));
        Quaternion hairCard = CardWith(normal: 0.6f * Vector3.UnitX + 0.8f * downTowardTheEye, strand: strand);
        Double3 toView = -Ray(Ahead, Width, Height);
        var fiberNormal = HairFiber.FiberNormal(strand, toView, Double3.UnitZ).ToVector3();
        Quaternion standardCard = CardWith(
            normal: fiberNormal,
            strand: Vector3.Normalize(Vector3.Cross(fiberNormal, Vector3.UnitX))
        );
        Vector3 ShownAhead(bool hair)
        {
            VulkanRenderer renderer = environmentMap
                ? EnvironmentScenes.Renderer(screenSpaceLight: ScreenSpaceLight(screenSpaceLight))
                : Gpu.Headless(width: Width, height: Height, screenSpaceLight: ScreenSpaceLight(screenSpaceLight));
            try
            {
                MeshId plane = renderer.CreateMesh(Primitives.Plane(1));
                RenderSnapshot snapshot = AmbientOnly();
                if (environmentMap)
                    for (int face = 0; face < CubeFaces.Count; face++)
                        snapshot.Instances.Add(
                            EnvironmentScenes.Square(
                                plane: plane,
                                material: Glow(renderer, EnvironmentScenes.ColorOf(face)),
                                center: CubeFaces.Basis((CubeFace)face).Forward * 20,
                                halfSize: 22,
                                chainDepth: TestChains.CamerasFrame
                            )
                        );
                // Small, so that the card hides little of the faces of the map around the eye.
                snapshot.Instances.Add(
                    Card(
                        plane: plane,
                        material: hair ? Hair(renderer) : Standard(renderer),
                        rotation: hair ? hairCard : standardCard,
                        scale: new(0.2f)
                    )
                );
                // The second frame reads the faces that the first drew.
                renderer.Render(snapshot, 1f);
                renderer.Render(snapshot, 1f);
                return At(renderer.ReadHdr(), Ahead);
            }
            finally
            {
                Gpu.DisposeAndAssertClean(renderer);
            }
        }

        Vector3 atTheFiberNormal = ShownAhead(hair: false);
        atTheFiberNormal.X.Should().BePositive("the ambient light reaches the card");
        ShouldBeNear(
            actual: ShownAhead(hair: true),
            expected: atTheFiberNormal,
            relative: 0.01f,
            because: "a hair pixel takes the ambient light of a surface whose normal is its fiber normal"
        );
    }

    /// <summary>The coverage of the fringe card of <see cref="TwoCardsShown"/>.</summary>
    private const float FringeCardAlpha = 0.4f;

    /// <summary>The centers of the two cards of <see cref="TwoCardsShown"/>, in mirror places about the middle of the image.</summary>
    private static readonly Vector3 CoreCardCenter = new(-0.375f, 0, -3),
        FringeCardCenter = new(0.375f, 0, -3);

    /// <summary>
    /// The HDR image of the sun and the hemisphere, with or without two small fringed hair cards of reflectance 0, whose
    /// highlights, and with them the rounding of the strand's code, vanish: a core card of alpha 1 that faces the camera
    /// at <see cref="CoreCardCenter"/>, and a fringe card of alpha <see cref="FringeCardAlpha"/> turned 40° about its
    /// strand at <see cref="FringeCardCenter"/>.
    /// </summary>
    private static float[] TwoCardsShown(bool withCards)
    {
        VulkanRenderer renderer = Gpu.Headless(width: Width, height: Height);
        try
        {
            MeshId plane = renderer.CreateMesh(Primitives.Plane(1));
            RenderSnapshot snapshot = AmbientOnly();
            Gpu.Star(snapshot: snapshot, irradiance: 2, direction: Vector3.Normalize(new(0, 0.6f, 0.8f)));
            if (!withCards)
                return Rendered(renderer, snapshot);
            MaterialId Material(float alpha) =>
                renderer.CreateMaterial(
                    new(
                        BaseColor: new(x: (float)Brown.X, y: (float)Brown.Y, z: (float)Brown.Z, w: alpha),
                        Roughness: Roughness,
                        Reflectance: 0,
                        Model: new HairShading(StrandDirection.PlusV),
                        Alpha: new FringedAlpha(0.5f)
                    )
                );
            Quaternion turned = CardWith(normal: new(MathF.Sin(0.7f), 0, MathF.Cos(0.7f)), strand: Vector3.UnitY);
            return Rendered(
                renderer,
                snapshot,
                Card(
                    plane: plane,
                    material: Material(1),
                    rotation: FacingTheCamera,
                    center: CoreCardCenter,
                    scale: new(0.3f)
                ),
                Card(
                    plane: plane,
                    material: Material(FringeCardAlpha),
                    rotation: turned,
                    center: FringeCardCenter,
                    scale: new(0.3f)
                )
            );
        }
        finally
        {
            Gpu.DisposeAndAssertClean(renderer);
        }
    }

    [Fact]
    public void TheFringeShadesAsTheCoreBesideIt()
    {
        // The eye sees both pixels alike. Their fibers shade alike, and their planes, which a standard surface would show,
        // differ.
        Vector2 core = Holding(Gpu.Project(point: CoreCardCenter, width: Width, height: Height));
        Vector2 fringe = Holding(Gpu.Project(point: FringeCardCenter, width: Width, height: Height));
        (fringe.X + core.X).Should().Be(Width - 1, "the two pixels mirror each other");

        float[] withCards = TwoCardsShown(withCards: true);
        Vector3 behind = At(TwoCardsShown(withCards: false), fringe);
        Vector3 fringeLight = (At(withCards, fringe) - (1 - FringeCardAlpha) * behind) / FringeCardAlpha;

        ShouldBeNear(
            actual: fringeLight,
            expected: At(withCards, core),
            relative: 0.01f,
            because: "the fringe shades forward as the core does deferred, by its strand"
        );
    }

    /// <summary>The forward views of an opaque pass that shade hair.</summary>
    public enum ForwardView
    {
        EnvironmentFace,
        ImpostorCapture,
    }

    /// <summary>How a card seen from behind draws its alpha, so that each pipeline variant that shades it is tested.</summary>
    public enum CardAlpha
    {
        Opaque,

        /// <summary>Cutout, with every texel of alpha 1 above the cutoff: the core draws.</summary>
        Cutout,
    }

    private static AlphaMode AlphaOf(CardAlpha alpha) =>
        alpha switch
        {
            CardAlpha.Opaque => new OpaqueAlpha(),
            CardAlpha.Cutout => new CutoutAlpha(Cutoff: 0.5f),
        };

    /// <summary>
    /// A matte material of base color (0.8, 0.6, 0.4), alpha 1 and reflectance 0, of <paramref name="model"/>, drawn as
    /// <paramref name="alpha"/>.
    /// </summary>
    private static MaterialId MatteOf(VulkanRenderer renderer, ShadingModel model, AlphaMode alpha = default) =>
        renderer.CreateMaterial(
            new(
                BaseColor: new(x: 0.8f, y: 0.6f, z: 0.4f, w: 1),
                Roughness: 1,
                Reflectance: 0,
                Model: model,
                Alpha: alpha
            )
        );

    /// <summary>
    /// The middle texel of the +X face of the map, which sees a matte card of <paramref name="model"/> 5 m away, facing
    /// the eye or <paramref name="turnedAway"/> from it, drawn as <paramref name="alpha"/>, under a sun behind it of
    /// <paramref name="irradiance"/>.
    /// </summary>
    private static Vector3 FaceLitFromBehind(
        ShadingModel model,
        out Vector3 irradiance,
        bool turnedAway = false,
        AlphaMode alpha = default
    )
    {
        VulkanRenderer renderer = EnvironmentScenes.Renderer();
        try
        {
            MeshId plane = renderer.CreateMesh(Primitives.Plane(1));
            RenderSnapshot snapshot = SunOnly(Vector3.UnitX);
            irradiance = Gpu.StarIrradianceRgb(snapshot);
            snapshot.Instances.Add(
                EnvironmentScenes.Facing(
                    plane: plane,
                    material: MatteOf(renderer: renderer, model: model, alpha: alpha),
                    center: new(5, 0, 0),
                    normal: turnedAway ? Vector3.UnitX : -Vector3.UnitX,
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

    /// <summary>
    /// The middle texel of the impostor capture of a matte card of <paramref name="model"/> 6 m ahead, facing the eye or
    /// <paramref name="turnedAway"/> from it, drawn as <paramref name="alpha"/>, under a sun behind it of
    /// <paramref name="irradiance"/>.
    /// </summary>
    private static Vector3 CaptureLitFromBehind(
        ShadingModel model,
        out Vector3 irradiance,
        bool turnedAway = false,
        AlphaMode alpha = default
    )
    {
        VulkanRenderer renderer = Gpu.Headless(width: 320, height: 180, impostors: true);
        try
        {
            MeshId plane = renderer.CreateMesh(Primitives.Plane(1));
            MaterialId material = MatteOf(renderer: renderer, model: model, alpha: alpha);
            RenderSnapshot snapshot = SunOnly(-Vector3.UnitZ);
            irradiance = Gpu.StarIrradianceRgb(snapshot);
            snapshot.Instances.Add(
                Card(
                    plane: plane,
                    material: material,
                    rotation: turnedAway ? TurnedAway : FacingTheCamera,
                    center: new(0, 0, -6)
                )
            );
            renderer.Render(snapshot, 1f);
            renderer.LastFrameStats.ImpostorCaptures.Should().Be(1, "a 2 m card 6 m away is an impostor");
            int tile = renderer
                .Impostors!.Status(
                    new(EntityIndex: 0, Part: 0, Mesh: plane.Value, Material: MaterialStore.Slot(material))
                )!
                .Value.Tile;
            (float[] rgba, _) = renderer.ReadImpostorTile(tile);
            const int size = ImpostorAtlas.TileSize;
            int middle = (size / 2 * size + size / 2) * 4;
            return new(rgba[middle], rgba[middle + 1], rgba[middle + 2]);
        }
        finally
        {
            Gpu.DisposeAndAssertClean(renderer);
        }
    }

    [Theory]
    [InlineData(ForwardView.EnvironmentFace)]
    [InlineData(ForwardView.ImpostorCapture)]
    public void AnEnvironmentFaceAndACaptureShadeHairAsFibers(ForwardView view)
    {
        Vector3 Shown(ShadingModel model, out Vector3 irradiance) =>
            view switch
            {
                ForwardView.EnvironmentFace => FaceLitFromBehind(model: model, irradiance: out irradiance),
                ForwardView.ImpostorCapture => CaptureLitFromBehind(model: model, irradiance: out irradiance),
            };

        Vector3 hairLight = Shown(new HairShading(StrandDirection.PlusV), out Vector3 irradiance);
        // Matte and black in its highlights: the light from behind the card is the fiber's diffuse part alone, b E / π².
        var expected = (new Double3(0.8, 0.6, 0.4) * (Double3)irradiance / (Math.PI * Math.PI)).ToVector3();

        ShouldBeNear(
            actual: hairLight,
            expected: expected,
            relative: 0.03f,
            because: "the fiber takes the light from behind its card"
        );
        Shown(new StandardShading(0), out _)
            .Should()
            .Be(Vector3.Zero, "a standard card takes none, so the test can fail");
    }

    [Theory]
    [InlineData(ForwardView.EnvironmentFace, CardAlpha.Opaque)]
    [InlineData(ForwardView.EnvironmentFace, CardAlpha.Cutout)]
    [InlineData(ForwardView.ImpostorCapture, CardAlpha.Opaque)]
    [InlineData(ForwardView.ImpostorCapture, CardAlpha.Cutout)]
    public void AnEnvironmentFaceAndACaptureShadeAHairCardSeenFromBehindAsSeenFromTheFront(
        ForwardView view,
        CardAlpha alpha
    )
    {
        var hair = new HairShading(StrandDirection.PlusV);
        Vector3 Shown(bool turnedAway) =>
            view switch
            {
                ForwardView.EnvironmentFace => FaceLitFromBehind(
                    model: hair,
                    irradiance: out _,
                    turnedAway: turnedAway,
                    alpha: AlphaOf(alpha)
                ),
                ForwardView.ImpostorCapture => CaptureLitFromBehind(
                    model: hair,
                    irradiance: out _,
                    turnedAway: turnedAway,
                    alpha: AlphaOf(alpha)
                ),
            };

        Vector3 front = Shown(turnedAway: false);
        front.X.Should().BePositive("the fiber takes the light from behind its card");
        ShouldBeNear(
            actual: Shown(turnedAway: true),
            expected: front,
            relative: 4 * HalfRounding,
            because: "a hair card is a sheet of strands with no back"
        );
    }

    /// <summary>
    /// The middle pixel of a card of <paramref name="material"/>, facing the camera or <paramref name="turnedAway"/> from
    /// it, under the hemisphere and a sun from the front.
    /// </summary>
    private static Vector3 CardShownAhead(Func<VulkanRenderer, MaterialId> material, bool turnedAway)
    {
        VulkanRenderer renderer = Gpu.Headless(width: Width, height: Height);
        try
        {
            MeshId plane = renderer.CreateMesh(Primitives.Plane(1));
            RenderSnapshot snapshot = AmbientOnly();
            Gpu.Star(snapshot: snapshot, irradiance: 2, direction: Vector3.Normalize(new(0.5f, 0.3f, 0.81f)));
            RenderInstance card = Card(
                plane: plane,
                material: material(renderer),
                rotation: turnedAway ? TurnedAway : FacingTheCamera
            );
            return At(Rendered(renderer, snapshot, card), Ahead);
        }
        finally
        {
            Gpu.DisposeAndAssertClean(renderer);
        }
    }

    [Theory]
    [InlineData(CardAlpha.Opaque)]
    [InlineData(CardAlpha.Cutout)]
    public void AHairCardSeenFromBehindShadesAsTheSameCardSeenFromTheFront(CardAlpha alpha)
    {
        Vector3 front = CardShownAhead(renderer => Hair(renderer, AlphaOf(alpha)), turnedAway: false);

        front.X.Should().BePositive("the light reaches the card's front");
        ShouldBeNear(
            actual: CardShownAhead(renderer => Hair(renderer, AlphaOf(alpha)), turnedAway: true),
            expected: front,
            relative: 4 * HalfRounding,
            because: "a hair card is a sheet of strands with no back"
        );
    }

    [Theory]
    [InlineData(CardAlpha.Opaque)]
    [InlineData(CardAlpha.Cutout)]
    public void AHairCardSeenFromBehindStoresTheNormalsAndTheStrandOfTheCardSeenFromTheFront(CardAlpha alpha)
    {
        (float[] Normals, byte[] Shading) StoredAhead(Quaternion rotation)
        {
            VulkanRenderer renderer = Gpu.Headless(width: Width, height: Height);
            try
            {
                MeshId plane = renderer.CreateMesh(Primitives.Plane(1));
                MaterialId material = Hair(renderer, AlphaOf(alpha));
                Rendered(renderer, AmbientOnly(), Card(plane: plane, material: material, rotation: rotation));
                int index = (int)Ahead.Y * Width + (int)Ahead.X;
                return (
                    Normals: renderer.ReadNormals()[(4 * index)..(4 * index + 4)],
                    Shading: renderer.ReadShading()[(4 * index)..(4 * index + 4)]
                );
            }
            finally
            {
                Gpu.DisposeAndAssertClean(renderer);
            }
        }

        (float[] Normals, byte[] Shading) behind = StoredAhead(TurnedAway);
        (float[] Normals, byte[] Shading) front = StoredAhead(FacingTheCamera);

        // The two rotations may round their normals apart by a step of the SNORM16 target.
        const float snormStep = 1f / 32767;
        behind
            .Normals.Should()
            .Equal(
                front.Normals,
                (seenBehind, seenFront) => Math.Abs(seenBehind - seenFront) <= 2 * snormStep,
                "a hair card's normals turn toward the eye: its fiber normal and its card's normal, which the shadow lookups read"
            );
        // The strand +Y codes as 128 of 255 across, at the rounding's edge, so the two rotations may code it a step apart.
        behind
            .Shading.Should()
            .Equal(
                front.Shading,
                (seenBehind, seenFront) => Math.Abs(seenBehind - seenFront) <= 1,
                "a hair card stores the strand of its mesh's frame and the hair model, seen from either side"
            );
        CardCode
            .ModelOf(front.Shading[3])
            .Should()
            .Be(ShadingCode.Hair, "the model's bits name hair, so the test can fail");
    }

    [Fact]
    public void AFringedHairCardSeenFromBehindShadesAsTheSameCardSeenFromTheFront()
    {
        // Alpha 0.4 under a cutoff of 0.5: the whole card is fringe, and the transparent pass alone draws it.
        static MaterialId Fringe(VulkanRenderer renderer) =>
            renderer.CreateMaterial(
                new(
                    BaseColor: new(x: (float)Brown.X, y: (float)Brown.Y, z: (float)Brown.Z, w: FringeCardAlpha),
                    Roughness: Roughness,
                    Reflectance: Reflectance,
                    Model: new HairShading(StrandDirection.PlusV),
                    Alpha: new FringedAlpha(0.5f)
                )
            );
        Vector3 front = CardShownAhead(Fringe, turnedAway: false);

        front.X.Should().BePositive("the fringe covers 0.4 of the pixel");
        ShouldBeNear(
            actual: CardShownAhead(Fringe, turnedAway: true),
            expected: front,
            relative: 4 * HalfRounding,
            because: "a hair card's fringe has no back either"
        );
    }

    /// <summary>
    /// The middle pixel of a standard backdrop 6 m ahead, 6 m square, behind a glass hair card of opacity 0.5 in the
    /// rotation <paramref name="glass"/>, or behind nothing for null, under the hemisphere and a sun from the front.
    /// </summary>
    private static Vector3 BackdropBehindGlassHair(Quaternion? glass)
    {
        VulkanRenderer renderer = Gpu.Headless(width: Width, height: Height);
        try
        {
            MeshId plane = renderer.CreateMesh(Primitives.Plane(1));
            RenderSnapshot snapshot = AmbientOnly();
            Gpu.Star(snapshot: snapshot, irradiance: 2, direction: Vector3.Normalize(new(0.5f, 0.3f, 0.81f)));
            snapshot.Instances.Add(
                Card(
                    plane: plane,
                    material: Standard(renderer),
                    rotation: FacingTheCamera,
                    center: new(0, 0, -6),
                    scale: new(3, 1, 3)
                )
            );
            if (glass is { } rotation)
            {
                MaterialId glassHair = renderer.CreateMaterial(
                    new(
                        BaseColor: new(x: (float)Brown.X, y: (float)Brown.Y, z: (float)Brown.Z, w: 0.5f),
                        Roughness: Roughness,
                        Reflectance: Reflectance,
                        Model: new HairShading(StrandDirection.PlusV),
                        Alpha: new GlassAlpha()
                    )
                );
                snapshot.Instances.Add(Card(plane: plane, material: glassHair, rotation: rotation));
            }
            return At(Rendered(renderer, snapshot), Ahead);
        }
        finally
        {
            Gpu.DisposeAndAssertClean(renderer);
        }
    }

    [Fact]
    public void AGlassHairCardSeenFromBehindDimsWhatLiesBehindItAsSeenFromTheFront()
    {
        Vector3 bare = BackdropBehindGlassHair(glass: null);
        Vector3 front = BackdropBehindGlassHair(glass: FacingTheCamera);

        LargestChannelDifference(front, bare)
            .Should()
            .BeGreaterThan(0.1f * bare.X, "the glass covers half of the backdrop, so the test can fail");
        ShouldBeNear(
            actual: BackdropBehindGlassHair(glass: TurnedAway),
            expected: front,
            relative: 4 * HalfRounding,
            because: "a glass hair material is two-sided by the same rule"
        );
    }

    [Fact]
    public void AStandardCardSeenFromBehindDrawsNothing() =>
        CardShownAhead(Standard, turnedAway: true)
            .Should()
            .Be(
                Vector3.Zero,
                "only a hair card is a sheet with no back, and the opaque passes cull every other back face"
            );

    [Fact]
    public void StandardPixelsKeepTheirLight()
    {
        VulkanRenderer renderer = Gpu.Headless(width: Width, height: Height);
        try
        {
            MeshId plane = renderer.CreateMesh(Primitives.Plane(1));
            var toSun = Vector3.Normalize(new(0.3f, 0.6f, 0.75f));
            RenderSnapshot snapshot = SunOnly(toSun);
            Vector3 irradiance = Gpu.StarIrradianceRgb(snapshot);

            Vector3 shown = At(
                Rendered(
                    renderer,
                    snapshot,
                    Card(plane: plane, material: Standard(renderer), rotation: FacingTheCamera)
                ),
                Ahead
            );
            float[] normals = renderer.ReadNormals();
            int index = (int)Ahead.Y * Width + (int)Ahead.X;
            Double3 normal = StoredNormal(normals, index);
            var material = new MaterialSample(
                BaseColor: Brown,
                Metallic: 0,
                Roughness: Roughness,
                Reflectance: Reflectance
            );
            Double3 reflected = material.Reflected(
                normal: normal,
                toView: -Ray(Ahead, Width, Height),
                toLight: toSun,
                tables: BrdfTables.Shared
            );

            ShouldBeNear(
                actual: shown,
                expected: (reflected * (Double3)irradiance).ToVector3(),
                relative: HalfRounding,
                because: "a standard pixel sends the light of the BRDF, the sum of its two parts"
            );
        }
        finally
        {
            Gpu.DisposeAndAssertClean(renderer);
        }
    }
}
