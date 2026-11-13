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

    /// <summary>
    /// The vector irradiance of a rectangle light's face per unit of its face radiance, ½ Σ θᵢ n̂ᵢ over its four edges seen
    /// from the point, in sr: each edge's angle from an arctangent, independent of the shader's fit. The corners are
    /// relative to the point, in the order −w −h, −w +h, +w +h, +w −h.
    /// </summary>
    private static Double3 VectorIrradiance(Double3[] corners)
    {
        Double3 sum = Double3.Zero;
        for (int i = 0; i < 4; i++)
        {
            Double3 a = corners[i],
                b = corners[(i + 1) & 3];
            var normal = Double3.Cross(a, b);
            sum += normal * (Math.Atan2(normal.Length, Double3.Dot(a, b)) / normal.Length);
        }
        return 0.5 * sum;
    }

    /// <summary>
    /// The face of <paramref name="panel"/> seen from <paramref name="position"/>: its vector irradiance per unit of face
    /// radiance (<see cref="VectorIrradiance"/>), and its face radiance times the window of its reach from the face's
    /// nearest point under FixedExposure(0).
    /// </summary>
    private static (Double3 Vector, double FaceRadiance) FaceSeenFrom(RectRenderLight panel, Double3 position)
    {
        Double3 halfWidth = Vector3.Transform(Vector3.UnitX, panel.Rotation) * (panel.Width / 2),
            halfHeight = Vector3.Transform(Vector3.UnitY, panel.Rotation) * (panel.Height / 2);
        Double3 toCenter = (Double3)panel.Position - position;
        Double3 vector = VectorIrradiance([
            toCenter - halfWidth - halfHeight,
            toCenter - halfWidth + halfHeight,
            toCenter + halfWidth + halfHeight,
            toCenter + halfWidth - halfHeight,
        ]);
        double intensity = LightUnits.RectIntensity(panel.Power);
        double reach = LightUnits.Reach(intensity, LightUnits.CutoffIrradiance(0));
        double across = Math.Clamp(-Double3.Dot(toCenter, halfWidth) / halfWidth.LengthSquared, -1, 1),
            up = Math.Clamp(-Double3.Dot(toCenter, halfHeight) / halfHeight.LengthSquared, -1, 1);
        double ratio = (-toCenter - across * halfWidth - up * halfHeight).LengthSquared / (reach * reach);
        return (vector, intensity / ((double)panel.Width * panel.Height) * Math.Pow(1 - ratio * ratio, 2));
    }

    /// <summary>A white rectangle light of 6 W, 0.6 m by 0.4 m, that casts no shadow, at <paramref name="center"/>.</summary>
    private static RectRenderLight PanelAt(Vector3 center, Quaternion rotation) =>
        new(
            EntityIndex: Gpu.LampEntity,
            EntityGeneration: 0,
            Position: center,
            PreviousPosition: center,
            Rotation: rotation,
            PreviousRotation: rotation,
            Color: Vector3.One,
            Power: 6,
            Width: 0.6f,
            Height: 0.4f,
            CastsShadow: false,
            ChainDepth: TestChains.Root
        );

    [Fact]
    public void AHairPixelTakesARectangleLightAsHairFiberTakesALightAlongTheFacesVectorIrradiance()
    {
        // A panel 0.6 m by 0.4 m, 1 m to the right of the card, 0.5 m up and 1 m nearer the eye, turned to face the card's
        // center, of 6 W and white.
        var center = new Vector3(1f, 0.5f, -2f);
        var front = Vector3.Normalize(CardCenter - center);
        var axis = Vector3.Normalize(Vector3.Cross(Vector3.UnitZ, front));
        RectRenderLight panel = PanelAt(center, Quaternion.CreateFromAxisAngle(axis, MathF.Acos(front.Z)));
        VulkanRenderer renderer = Gpu.Headless(width: Width, height: Height);
        try
        {
            MeshId plane = renderer.CreateMesh(Primitives.Plane(1));
            RenderSnapshot snapshot = SunOnly(Vector3.UnitY, irradiance: 0);
            snapshot.Lights.Add(panel);

            Vector3 shown = At(
                Rendered(renderer, snapshot, Card(plane: plane, material: Hair(renderer), rotation: FacingTheCamera)),
                Ahead
            );
            StoredFiber fiber = StoredAt(renderer, Ahead);
            (Double3 vector, double faceRadiance) = FaceSeenFrom(panel, fiber.Position);

            ShouldBeNear(
                actual: shown,
                expected: FiberLight(
                    fiber: fiber,
                    hair: BrownHair,
                    toLight: vector.Normalized(),
                    irradiance: new Double3(1, 1, 1) * (faceRadiance * vector.Length)
                ),
                relative: HalfRounding,
                because: "a fiber takes the face as a light along its vector irradiance, of the face radiance times its length"
            );
        }
        finally
        {
            Gpu.DisposeAndAssertClean(renderer);
        }
    }

    [Fact]
    public void AHairPixelBehindARectangleLightTakesNoneOfItsLight()
    {
        // The panel of the test above turned to face away from the card: the card sees its back.
        var center = new Vector3(1f, 0.5f, -2f);
        var away = Vector3.Normalize(center - CardCenter);
        var axis = Vector3.Normalize(Vector3.Cross(Vector3.UnitZ, away));
        var rotation = Quaternion.CreateFromAxisAngle(axis, MathF.Acos(away.Z));
        VulkanRenderer renderer = Gpu.Headless(width: Width, height: Height);
        try
        {
            MeshId plane = renderer.CreateMesh(Primitives.Plane(1));
            RenderSnapshot snapshot = SunOnly(Vector3.UnitY, irradiance: 0);
            snapshot.Lights.Add(PanelAt(center, rotation));

            Vector3 shown = At(
                Rendered(renderer, snapshot, Card(plane: plane, material: Hair(renderer), rotation: FacingTheCamera)),
                Ahead
            );

            shown.Should().Be(Vector3.Zero, "a fiber has no horizon, but the face sends no light from its back");
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

    [Fact]
    public void TwoCardsWithOneStrandAndDifferentNormalsShadeAlike()
    {
        // The same strand, +Y, and a card turned 40° about it.
        Quaternion turned = CardWith(normal: new(MathF.Sin(0.7f), 0, MathF.Cos(0.7f)), strand: Vector3.UnitY);
        static Vector3 ShownAhead(bool hair, Quaternion rotation)
        {
            VulkanRenderer renderer = Gpu.Headless(width: Width, height: Height);
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
            relative: 4 * HalfRounding,
            because: "a fiber's light does not depend on its card's plane"
        );
        Vector3 standardFacing = ShownAhead(hair: false, FacingTheCamera);
        LargestChannelDifference(ShownAhead(hair: false, turned), standardFacing)
            .Should()
            .BeGreaterThan(0.05f * standardFacing.X, "the same cards as standard shade apart, so the test can fail");
    }

    [Fact]
    public void UnderTheScreenTracerTheShadingTargetIsVisibleToTheComputeReadsOfTheTracer()
    {
        VulkanRenderer renderer = Gpu.Headless(width: Width, height: Height, screenTrace: ScreenTrace.Default);
        try
        {
            MeshId plane = renderer.CreateMesh(Primitives.Plane(1));
            Rendered(renderer, AmbientOnly(), Card(plane: plane, material: Hair(renderer), rotation: FacingTheCamera));

            renderer
                .ShadingTargetState.Stages.HasFlag(PipelineStageFlags2.ComputeShaderBit)
                .Should()
                .BeTrue(
                    "the tracer reads a hair pixel's model code in a compute shader (Vulkan spec, memory dependencies)"
                );
        }
        finally
        {
            Gpu.DisposeAndAssertClean(renderer);
        }
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

    /// <remarks>
    /// The arithmetic, in texels of the first cascade (5.85 cm: a sphere of radius 59.6 m over 2,040 texels) and in
    /// depth along the light. The shadow lookups take the card's normal turned toward the sun, at cosine 0.93 with
    /// it: seen from the sun, the card's plane rises tan θ = 0.39 per texel along û = (−0.58, 0.81) of the light's
    /// axes. The caster stands parallel to the card, <c>GapTexels</c> in front of the lookup point, which the normal
    /// offset moves sin θ = 0.36 texel off the card toward it. Every tap of the 4 × 4 kernel compares against the lookup
    /// point's own depth (sampleCascade in shadows.glsl). A tap lies up to 1.5 texels off along each axis, so at the most
    /// downhill tap the caster's plane comes 1.5 · (0.58 + 0.81) · 0.39 = 0.81 nearer that depth. Every tap sees the
    /// caster when the gap is above that drop, the spread of a tap's 2 × 2 footprint, 0.54, the constant bias, 0.5, and
    /// the caster's slope bias, at most 2.5 · 0.39 = 0.98: 2.8 in all. The premise keeps half a texel spare, four times
    /// the depth step of the 16-bit fallback map.
    /// </remarks>
    [Fact]
    public void AHairCardLitFromBehindKeepsTheShadowOfACasterCloseBehindIt()
    {
        const float GapTexels = 3.5f,
            SpareTexels = 0.5f;
        var toSun = Vector3.Normalize(new(0.2f, 0.3f, -0.93f));
        Vector3 towardSun = -Vector3.UnitZ;
        float cosine = Vector3.Dot(towardSun, toSun),
            sine = MathF.Sqrt(1 - cosine * cosine);
        var light = LightBasis.FromDirection(toSun);
        float across = MathF.Abs(Vector3.Dot(towardSun, light.X)) + MathF.Abs(Vector3.Dot(towardSun, light.Y));
        float need =
            1.5f * across / cosine
            + across / cosine
            + ShadowBias.ConstantBiasTexels
            + ShadowBias.SlopeBias * sine / cosine;
        need.Should().BeLessThan(GapTexels - SpareTexels, "every tap sees the caster");
        CardCenter.Z.Should().BeGreaterThan(-ShadowCascades.BlendStart(0), "the card lies in the first cascade alone");

        VulkanRenderer renderer = Gpu.Headless(width: Width, height: Height);
        try
        {
            MeshId plane = renderer.CreateMesh(Primitives.Plane(1));
            RenderSnapshot snapshot = SunOnly(toSun);
            float texel = Gpu.FirstCascadeTexel(width: Width, height: Height);
            float gap = texel * (GapTexels * cosine + ShadowBias.NormalOffsetTexels * sine);
            RenderInstance caster = Card(
                plane: plane,
                material: Standard(renderer),
                rotation: FacingTheCamera,
                center: CardCenter + towardSun * gap
            ) with
            {
                Flags = RenderFlags.Visible | RenderFlags.CastsShadow,
            };
            RenderInstance card = Card(plane: plane, material: Hair(renderer), rotation: FacingTheCamera);

            Vector3 shown = At(Rendered(renderer, snapshot, card, caster), Ahead);
            float rendererTexel = renderer.LastScene.CascadeSets[0][0].TexelSize;
            rendererTexel.Should().BeApproximately(texel, 1e-5f * texel, "the gap is in the renderer's texels");
            Vector3 lit = FiberLight(
                fiber: StoredAt(renderer, Ahead),
                hair: BrownHair,
                toLight: toSun,
                irradiance: Gpu.StarIrradianceRgb(snapshot)
            );

            lit.X.Should().BeGreaterThan(0.05f, "the sun lights the card from behind where nothing stands in its way");
            float leak = LargestChannelDifference(shown, Vector3.Zero);
            leak.Should().BeLessThanOrEqualTo(0.02f * lit.X, "the caster's shadow covers the card: shown {0}", shown);
        }
        finally
        {
            Gpu.DisposeAndAssertClean(renderer);
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void TheHairsAmbientLightIsTheSourcesAtTheFiberNormal(bool environmentMap)
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
                ? EnvironmentScenes.Renderer()
                : Gpu.Headless(width: Width, height: Height);
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
                    new(
                        Entity: new(Index: 0, Generation: 0),
                        Part: 0,
                        Mesh: plane.Value,
                        Material: MaterialStore.Slot(material)
                    )
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

    /// <summary>
    /// A face lights a hair card from its surface record, which holds the strand beside the fiber's normal
    /// (Shaders/face_surface.glsl). The sun stands 45° above the line from the card to the eye. Strands along Z lie across
    /// both directions, so the eye is near the highlights' cone. Strands along Y lean toward the sun, and their cone
    /// points far below the eye.
    /// </summary>
    [Theory]
    [InlineData(0, 0, 1)]
    [InlineData(0, 1, 0)]
    public void AnEnvironmentFaceLightsAHairCardByItsStrandsAsTheFiberModelDoes(float x, float y, float z)
    {
        var strand = new Vector3(x, y, z);
        var toSun = Vector3.Normalize(new(x: -1, y: 1, z: 0));
        const float distance = 5;
        VulkanRenderer renderer = EnvironmentScenes.Renderer();
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
            RenderSnapshot snapshot = SunOnly(toSun);
            snapshot.Instances.Add(
                Card(
                    plane: plane,
                    material: material,
                    rotation: CardWith(normal: -Vector3.UnitX, strand: strand),
                    center: new(distance, 0, 0),
                    scale: new(3, 1, 3)
                )
            );
            renderer.Render(snapshot, 1f);
            Vector3 shown = EnvironmentScenes.Center(EnvironmentScenes.Face(renderer, CubeFace.PositiveX));

            // The middle texel's center lies half a texel off the face's axis, along its right and against its up.
            FaceBasis basis = CubeFaces.Basis(CubeFace.PositiveX);
            float offset = 1f / EnvironmentScenes.FaceSize;
            Vector3 direction = basis.Forward + offset * basis.Right - offset * basis.Up;
            var toView = -((Double3)direction).Normalized();
            DirectTerms terms = DarkSmoothHair.Reflected(
                strand: (Double3)strand,
                fiberNormal: HairFiber.FiberNormal((Double3)strand, toView, -Double3.UnitX),
                toView: toView,
                toLight: (Double3)toSun
            );
            ShouldBeNear(
                actual: shown,
                expected: ((terms.Diffuse + terms.Specular) * (Double3)Gpu.StarIrradianceRgb(snapshot)).ToVector3(),
                relative: 0.03f,
                because: "the face's light pass reads the card's strand from the record"
            );
        }
        finally
        {
            Gpu.DisposeAndAssertClean(renderer);
        }
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
                tables: BrdfTables.Shared,
                lightSine: Gpu.StarSine
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
