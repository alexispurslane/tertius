using System.Numerics;
using Tertius.Testing;
using static Tertius.Rendering.Tests.BrdfTesting;
using static Tertius.Rendering.Tests.OcclusionScenes;

namespace Tertius.Rendering.Tests;

/// <summary>
/// The tests that pin the bounce light's shaders to the C# copy (docs/plans/screen-space-illumination.md, "GPU tests of
/// the acceptance criteria"), in the corner under the sun and a dim sky and ground of distinct colors: the sector pass
/// gives the estimate that <see cref="SectorSearch"/> gives from the same images, with the environment map off and on,
/// and the ambient pass adds what <see cref="MaterialSample.ReflectedHemisphere"/> gives at the estimate that the
/// estimate target holds.
/// </summary>
[Collection(GpuCollection.Name)]
public sealed class BounceLightShaderTests(ITestOutputHelper output)
{
    /// <summary>102 of 255, which the specular target stores exactly.</summary>
    private const float Reflectance = 0.4f;

    /// <summary>The share of the ambient part of a pixel within which the GPU matches the C# copy: the BRDF tables' sampling.</summary>
    private const float AmbientTolerance = 0.02f;

    /// <summary>The exposure of the ambient pass's test, in stops: 4 times the light, so a bounce read without the division shows.</summary>
    private const float ExposureStops = 2;

    /// <summary>How near a tie a boundary 32 m + ½ may lie before the GPU's single floats may move it: about 30 times their error.</summary>
    private const double TieTolerance = 1e-3;

    /// <summary>The share of the light within which the GPU's single floats and its decoded normals match the C# copy.</summary>
    private const double FloatError = 1e-4;

    /// <summary>
    /// The share of a texel's inverse depth within which the GPU and the C# copy can decide a continuation differently:
    /// each builds the plane with its own rounding, a few units in the last place, and the rule cuts hard at
    /// <see cref="CellBounds.ContinuationShare"/>. A hundred times the rounding, as the pyramid's tests take it.
    /// </summary>
    private const float UndecidedShare = 1e-4f;

    /// <summary>The low box beside the open floor, 0.2 m tall, whose front face the floor beside it sees from behind.</summary>
    private static readonly Vector3 LowBoxCenter = new(0.5f, FloorHeight + 0.1f, -3);

    private static readonly Vector3 LowBoxSize = new(0.3f, 0.2f, 0.3f);

    /// <summary>
    /// The floor 5 cm beside the low box, on its side away from the camera, behind the plane of the box's front face:
    /// its blockers include that face, which faces away from it.
    /// </summary>
    private static readonly Vector3 BesideTheLowBox = new(0.7f, FloorHeight, -3);

    /// <summary>
    /// A platform 5 cm above the floor, whose top's normal map tilts the shading normal 38° toward +x, so its shading
    /// hemisphere reaches below its top. Its bottom lies under the floor.
    /// </summary>
    private static readonly Vector3 PlatformCenter = new(-0.7f, FloorHeight - 0.2f, -2.6f);

    private static readonly Vector3 PlatformSize = new(0.6f, 0.5f, 0.6f);

    /// <summary>The lamp of the map's speck: behind and above the camera, out of its view.</summary>
    private static readonly Vector3 LampCenter = new(0.5f, 1, 3);

    /// <summary>The middle of the platform's top.</summary>
    private static readonly Vector3 OnThePlatform = new(-0.7f, FloorHeight + 0.05f, -2.6f);

    /// <summary>
    /// The normal map of the platform's top: one texel, (128, 200, 220), in the tangent space of the box's top face,
    /// whose tangent is +Z and bitangent +X (<see cref="Primitives.Box"/>).
    /// </summary>
    private static readonly byte[] TiltedMapTexel = [128, 200, 220, 255];

    /// <summary>The world shading normal that material.glsl decodes from <see cref="TiltedMapTexel"/> on the platform's top.</summary>
    private static readonly Vector3 TiltedMapNormal = Vector3.Normalize(
        new(x: 200 / 255f * 2 - 1, y: 220 / 255f * 2 - 1, z: 128 / 255f * 2 - 1)
    );

    /// <summary>The sky and the ground: distinct colors, so a swapped share or a swapped term shows.</summary>
    private static readonly Vector3 Sky = new(0.2f, 0.3f, 0.6f);

    private static readonly Vector3 Ground = new(0.35f, 0.2f, 0.08f);

    /// <summary>Toward the sun: above the open side of the corner, so it lights the floor and both walls at the four points.</summary>
    private static readonly Vector3 SunDirection = new(0.4f, 0.8f, 0.45f);

    /// <summary>The four surface points and their normals: the floor and the back wall, each at the corner and open.</summary>
    private static readonly (Vector3 Point, Double3 Normal)[] Points =
    [
        (Point: FloorAtTheCorner, Normal: Double3.UnitY),
        (Point: OpenFloor, Normal: Double3.UnitY),
        (Point: new(SideWallX + 0.1f, FloorHeight + 0.1f, BackWallZ), Normal: Double3.UnitZ),
        (Point: new(0.8f, FloorHeight + 1.2f, BackWallZ), Normal: Double3.UnitZ),
    ];

    /// <summary>Four points of the corner near its walls: the floor at the corner and 0.4 m from the back wall, and both walls.</summary>
    private static readonly Vector3[] SectorPoints =
    [
        FloorAtTheCorner,
        new(0, FloorHeight, BackWallZ + 0.4f),
        new(SideWallX + 0.1f, FloorHeight + 0.1f, BackWallZ),
        new(SideWallX, FloorHeight + 0.1f, BackWallZ + 0.3f),
    ];

    /// <summary>
    /// A hair card standing on the floor, 0.8 m square, whose strands run up and whose normal is turned 40° about them
    /// from the back wall's: half of its own points lie above the plane of a hair pixel's fiber normal.
    /// </summary>
    private static readonly Vector3 HairCardCenter = new(0.4f, FloorHeight + 0.4f, -3);

    private static readonly Vector3 HairCardNormal = new(MathF.Sin(0.7f), 0, MathF.Cos(0.7f));

    /// <summary>Three points of the hair card: by the floor, at its middle and 0.2 m to its side.</summary>
    private static readonly Vector3[] OnTheHairCard =
    [
        HairCardCenter + new Vector3(0, -0.3f, 0),
        HairCardCenter,
        HairCardCenter + 0.2f * Vector3.Cross(HairCardNormal, Vector3.UnitY),
    ];

    /// <summary>The camera's unit axes in the world: XeGTAO's view space has x right, y up and z forward.</summary>
    private static readonly Vector3 Right = Vector3.Transform(Vector3.UnitX, CornerView);

    private static readonly Vector3 Up = Vector3.Transform(Vector3.UnitY, CornerView);
    private static readonly Vector3 Forward = Vector3.Transform(-Vector3.UnitZ, CornerView);

    [Fact]
    public void TheSectorPassGathersWhatTheCSharpCopyGathers()
    {
        VisibilityBitmaskLight setting = VisibilityBitmaskLight.Default;
        VulkanRenderer renderer = Gpu.Headless(screenSpaceLight: setting);
        try
        {
            // The term that the sector pass leaves, before the blur overwrites it.
            renderer.Graph.Skipped.UnionWith(["occlusion denoise", "occlusion upsample"]);
            renderer.Render(CornerWithBoxes(renderer), 1f);
            (float[] term, int halfWidth, int halfHeight) = renderer.ReadOcclusionTerm();
            var gpu = new SectorPassOutput(
                Term: term,
                OpenShare: renderer.ReadOcclusionTermOpenShare(),
                HalfWidth: halfWidth
            );
            var images = new CornerImages(
                ViewDepth: renderer.ReadOcclusionViewDepth().Depths,
                HalfSize: (Width: halfWidth, Height: halfHeight),
                Source: renderer.ReadDiffuse()
            );
            // The brightest channel of the bounce source, which one sector's light can reach.
            double brightest = images.Source.Where((_, i) => i % 4 != 3).Max();

            AmbientEstimate atTheCorner = images
                .Near(texel: TexelOf(FloorAtTheCorner), setting: setting, everyBlockerFaces: false)
                .Estimate;
            atTheCorner.Bounce.X.Should().BePositive("the sunlit walls bounce their light onto the corner");
            (
                images.Near(texel: TexelOf(BesideTheLowBox), setting: setting, everyBlockerFaces: true).Estimate.Bounce
                - images
                    .Near(texel: TexelOf(BesideTheLowBox), setting: setting, everyBlockerFaces: false)
                    .Estimate.Bounce
            )
                .Length.Should()
                .BePositive("the test needs a blocker that faces away: the box's front face, seen from behind");
            images
                .Near(texel: TexelOf(OnThePlatform), setting: setting, everyBlockerFaces: false)
                .OwnSurface.Sum(sectors => sectors.Count)
                .Should()
                .BePositive("the test needs a shading hemisphere that reaches below its own surface");
            foreach (Vector3 point in (Vector3[])[.. SectorPoints, BesideTheLowBox, OnThePlatform])
            {
                (int X, int Y) texel = TexelOf(point);
                NearTexel expected = images.Near(texel: texel, setting: setting, everyBlockerFaces: false);
                ShouldMatch(gpu: gpu, texel: texel, expected: expected, brightest: brightest, point: point);
            }
        }
        finally
        {
            Gpu.DisposeAndAssertClean(renderer);
        }
    }

    /// <summary>
    /// The sector pass under the environment map, which the faces draw from the camera: the sunlit corner and the dim
    /// sky, so the map's light differs from direction to direction. Its term A holds G, the blockers' light alone, and S,
    /// the open pieces' share of the map's light along every piece: the blocked parts of the near steps and of the
    /// receiver's own surface, and the pieces that they leave open. On the platform, whose normal map tilts its shading
    /// hemisphere below its top, the own surface's part is not empty. The C# copy reads the specular map that the
    /// renderer read back. A GPU read is a trilinear blend of at most eight texels of the two levels around its level of
    /// detail, so it lies between the least and the greatest texel within a texel of its place on those levels
    /// (<see cref="SpecularMap.Range"/>); S by the least open and the most blocked reads, and the reverse, bounds the
    /// GPU's S, before the rounding of the shader's single floats and of term A's half floats. A sector next to a tie
    /// may flip on the GPU and change G by at most the brightest light of the direct image, and S by that light over
    /// the mean read.
    /// </summary>
    [Fact]
    public void UnderTheMapTheSectorPassTakesTheOpenPiecesShareOfTheMapsLightAsTheCSharpCopyDoes() =>
        SectorPassUnderTheMapShouldMatchTheCSharpCopy(CornerWithBoxes);

    [Fact]
    public void TheDepthPassFlagsTheEdgeNeighborsThatContinueEachTexelsPlaneByTheSharedRule()
    {
        // The near steps read the flags in place of their neighbors, so each texel's four bits must be the answers of
        // PixelPlane.ContinuesInto for the copies that the steps read. The floor ends under the sky, and the low box
        // stands on it, so the flags meet neighbors beyond the image, neighbors that see the sky, depth edges and
        // continuing planes.
        VulkanRenderer renderer = Gpu.Headless(screenSpaceLight: VisibilityBitmaskLight.Default);
        try
        {
            renderer.Render(FloorWithTheLowBoxUnderTheSky(renderer), 1f);
            (float[] depths, float[] normals, int width, int height) = renderer.ReadOcclusionDepthAndNormal();
            var copies = new TexelCopies(Depths: depths, Normals: normals, Width: width, Height: height);

            FlagComparison comparison = copies.CompareFlags(renderer.ReadOcclusionContinuation().Flags);

            output.WriteLine(
                $"{comparison.Beyond} neighbors beyond the image, {comparison.Sky} that see the sky, {comparison.Continues} that continue and {comparison.Breaks} that do not"
            );
            comparison
                .Mismatches.Should()
                .BeEmpty("the depth pass answers the shared rule for the copies that the steps read");
            comparison.Beyond.Should().BeGreaterThan(100, "the floor reaches the image's sides and bottom");
            comparison.Sky.Should().BeGreaterThan(100, "the floor's far edge and its sides meet the sky");
            comparison.Breaks.Should().BeGreaterThan(50, "the box's outline and its edges are depth edges");
            comparison.Continues.Should().BeGreaterThan(10_000, "the floor's planes continue each other");
        }
        finally
        {
            Gpu.DisposeAndAssertClean(renderer);
        }
    }

    /// <summary>
    /// The sector pass of <see cref="UnderTheMapTheSectorPassTakesTheOpenPiecesShareOfTheMapsLightAsTheCSharpCopyDoes"/>,
    /// with a small bright lamp behind and above the camera, which the back wall's open directions face. The lamp's glow
    /// makes the map's light change fast with the direction, so an open piece read along other angles than the C# copy's
    /// takes S out of its range: boundaries off by π/4, from one step of their bisection, move S at the corner from 0.54
    /// to 0.75 (sectors.glsl, boundaryAngle).
    /// </summary>
    [Fact]
    public void UnderAMapWithASmallBrightLampTheSectorPassReadsEachOpenPieceAlongTheAnglesThatTheCSharpCopyReads() =>
        SectorPassUnderTheMapShouldMatchTheCSharpCopy(renderer =>
        {
            RenderSnapshot scene = CornerWithBoxes(renderer);
            AddSlab(
                snapshot: scene,
                box: UnitBox(renderer),
                material: Glow(renderer, new Vector3(40, 40, 40)),
                center: LampCenter,
                size: new(0.3f, 0.3f, 0.3f)
            );
            return scene;
        });

    /// <summary>
    /// Pins the sector pass under the environment map in the scene that <paramref name="sceneOf"/> builds to the C#
    /// copy, as <see cref="UnderTheMapTheSectorPassTakesTheOpenPiecesShareOfTheMapsLightAsTheCSharpCopyDoes"/> says.
    /// </summary>
    private void SectorPassUnderTheMapShouldMatchTheCSharpCopy(Func<VulkanRenderer, RenderSnapshot> sceneOf)
    {
        VisibilityBitmaskLight setting = VisibilityBitmaskLight.Default;
        VulkanRenderer renderer = Gpu.Headless(screenSpaceLight: setting, environmentMap: SceneEnvironmentMap.Default);
        try
        {
            // The term that the sector pass leaves, before the blur overwrites it.
            renderer.Graph.Skipped.UnionWith(["occlusion denoise", "occlusion upsample"]);
            // The faces draw the map in the first frame, and the sector pass reads it after them.
            renderer.Render(sceneOf(renderer), 1f);
            (float[] term, int halfWidth, int halfHeight) = renderer.ReadOcclusionTerm();
            var gpu = new SectorPassOutput(
                Term: term,
                OpenShare: renderer.ReadOcclusionTermOpenShare(),
                HalfWidth: halfWidth
            );
            var images = new CornerImages(
                ViewDepth: renderer.ReadOcclusionViewDepth().Depths,
                HalfSize: (Width: halfWidth, Height: halfHeight),
                Source: renderer.ReadDiffuse()
            );
            var map = SpecularMap.Read(renderer, SceneEnvironmentMap.Default.FaceSize);
            double brightest = Math.Max(images.Source.Where((_, i) => i % 4 != 3).Max(), map.Brightest);
            (map.Brightest - map.Darkest)
                .Should()
                .BeGreaterThan(0.1 * map.Brightest, "the test needs a map whose light differs between directions");
            images
                .Near(texel: TexelOf(OnThePlatform), setting: setting, everyBlockerFaces: false)
                .OwnSurface.Sum(sectors => sectors.Count)
                .Should()
                .BePositive("the test needs a shading hemisphere that reaches below its own surface");

            foreach (Vector3 point in (Vector3[])[.. SectorPoints, BesideTheLowBox, OnThePlatform])
            {
                (int X, int Y) texel = TexelOf(point);
                NearTexel leastMap = images.Near(
                    texel: texel,
                    setting: setting,
                    everyBlockerFaces: false,
                    mapLight: (direction, width) => map.Range(direction, width).Low
                );
                NearTexel mostMap = images.Near(
                    texel: texel,
                    setting: setting,
                    everyBlockerFaces: false,
                    mapLight: (direction, width) => map.Range(direction, width).High
                );
                ShouldMatch(gpu: gpu, texel: texel, bounds: (leastMap, mostMap), brightest: brightest, point: point);
            }
        }
        finally
        {
            Gpu.DisposeAndAssertClean(renderer);
        }
    }

    [Fact]
    public void TheSectorPassGathersWhatTheCSharpCopyGathersForAHairReceiver()
    {
        VisibilityBitmaskLight setting = VisibilityBitmaskLight.Default;
        VulkanRenderer renderer = Gpu.Headless(screenSpaceLight: setting);
        try
        {
            renderer.Graph.Skipped.UnionWith(["occlusion denoise", "occlusion upsample"]);
            RenderSnapshot scene = Corner(renderer, Matte(renderer), LitCorner());
            scene.Instances.Add(
                Gpu.Instance(
                    mesh: renderer.CreateMesh(Primitives.Plane(0.4f)),
                    material: renderer.CreateMaterial(
                        MaterialDesc.Color(new(x: 0.8f, y: 0.8f, z: 0.8f, w: 1), roughness: 0.8f) with
                        {
                            Model = new HairShading(StrandDirection.PlusV),
                        }
                    ),
                    position: HairCardCenter,
                    scale: Vector3.One,
                    rotation: PlaneTurnedTo(normal: HairCardNormal, strand: Vector3.UnitY),
                    chainDepth: TestChains.CamerasFrame
                )
            );
            renderer.Render(scene, 1f);
            (float[] term, int halfWidth, int halfHeight) = renderer.ReadOcclusionTerm();
            var gpu = new SectorPassOutput(
                Term: term,
                OpenShare: renderer.ReadOcclusionTermOpenShare(),
                HalfWidth: halfWidth
            );
            byte[] shading = renderer.ReadShading();
            var images = new HairCardImages(
                ViewDepth: renderer.ReadOcclusionViewDepth().Depths,
                HalfSize: (Width: halfWidth, Height: halfHeight),
                Source: renderer.ReadDiffuse(),
                Normals: renderer.ReadNormals(),
                Shading: shading
            );
            double brightest = images.Source.Where((_, i) => i % 4 != 3).Max();

            foreach (Vector3 point in OnTheHairCard)
            {
                (int X, int Y) texel = TexelOf(point);
                CardCode
                    .ModelOf(shading[(2 * texel.Y * Width + 2 * texel.X) * 4 + 3])
                    .Should()
                    .Be(ShadingCode.Hair, "the receiver at {0} is a hair pixel", point);
                (NearTexel expected, int onOwnCard) = images.NearHair(texel, setting);
                onOwnCard.Should().BePositive("the test needs steps that read the receiver's own card at {0}", point);
                expected
                    .Estimate.Occlusion.Should()
                    .BeLessThan(1, "the test needs another surface that blocks the receiver at {0}", point);
                ShouldMatch(gpu: gpu, texel: texel, expected: expected, brightest: brightest, point: point);
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
    public void TheAmbientPassAddsTheBounceAsTheCSharpCopyDoes(bool smoothMetal)
    {
        MaterialDesc desc = MaterialOf(smoothMetal);
        VulkanRenderer renderer = Gpu.Headless(
            screenSpaceLight: VisibilityBitmaskLight.Default,
            exposure: new FixedExposure(ExposureStops)
        );
        try
        {
            renderer.Render(Corner(renderer, renderer.CreateMaterial(desc), LitCorner()), 1f);
            float[] estimate = renderer.ReadEstimate();
            float[] direct = renderer.ReadDirect();
            float[] hdr = renderer.ReadHdr();

            Vector2 corner = PixelOf(FloorAtTheCorner, CornerView);
            if (smoothMetal)
                EstimateAt(estimate, corner)
                    .Bounce.X.Should()
                    .Be(0, "a metal sends no diffuse light, and only the diffuse light bounces");
            else
                EstimateAt(estimate, corner)
                    .Bounce.X.Should()
                    .BePositive("the sunlit walls bounce light onto the corner");
            foreach ((Vector3 point, Double3 normal) in Points)
            {
                Vector2 pixel = PixelOf(point, CornerView);
                Vector3 light = DirectAt(direct, pixel);
                Vector3 expected = ShaderInput.SurfaceLight(
                    radiance: ExpectedAmbientLight(
                        desc: desc,
                        normal: normal,
                        pixel: pixel,
                        estimate: EstimateAt(estimate, pixel)
                    ),
                    preExposedLight: light,
                    exposure: MathF.Pow(2, ExposureStops)
                );
                Vector3 shaded = DirectAt(hdr, pixel);
                output.WriteLine($"{point}: shaded {shaded}, expected {expected}, direct {light}");
                LargestChannelDifference(shaded, expected)
                    .Should()
                    .BeLessThanOrEqualTo(
                        AmbientTolerance * LargestChannel(expected - light) + 1e-3f * LargestChannel(expected),
                        "{0} must show its direct light plus the C# ambient light at its estimate",
                        point
                    );
            }
        }
        finally
        {
            Gpu.DisposeAndAssertClean(renderer);
        }
    }

    /// <summary>
    /// The lit corner of matte walls, with the low box beside the open floor and the platform whose top is
    /// normal-mapped.
    /// </summary>
    private static RenderSnapshot CornerWithBoxes(VulkanRenderer renderer)
    {
        MaterialId matte = Matte(renderer);
        RenderSnapshot scene = Corner(renderer, matte, LitCorner());
        MeshId box = UnitBox(renderer);
        AddSlab(snapshot: scene, box: box, material: matte, center: LowBoxCenter, size: LowBoxSize);
        AddSlab(snapshot: scene, box: box, material: TiltedMatte(renderer), center: PlatformCenter, size: PlatformSize);
        return scene;
    }

    /// <summary>
    /// The corner's floor alone, which ends 10 m ahead under the sky, with the low box on it, under the corner's light and
    /// seen through <see cref="CornerView"/>.
    /// </summary>
    private static RenderSnapshot FloorWithTheLowBoxUnderTheSky(VulkanRenderer renderer)
    {
        MaterialId matte = Matte(renderer);
        RenderSnapshot scene = LitCorner();
        Look(scene, CornerView);
        MeshId box = UnitBox(renderer);
        AddSlab(
            snapshot: scene,
            box: box,
            material: matte,
            center: new(0, FloorHeight - 0.5f, BackWallZ + 5),
            size: new(20, 1, 20)
        );
        AddSlab(snapshot: scene, box: box, material: matte, center: LowBoxCenter, size: LowBoxSize);
        return scene;
    }

    /// <summary>A light gray matte non-metal whose normal map is <see cref="TiltedMapTexel"/>.</summary>
    private static MaterialId TiltedMatte(VulkanRenderer renderer) =>
        renderer.CreateMaterial(
            MaterialDesc.Color(new(x: 0.8f, y: 0.8f, z: 0.8f, w: 1), roughness: 0.8f) with
            {
                NormalMap = renderer.CreateTexture(new(Width: 1, Height: 1, Rgba: TiltedMapTexel, Srgb: false)),
            }
        );

    /// <summary>
    /// The rotation that turns the plane mesh's front, +Y, to the unit <paramref name="normal"/>, and its +Z, along which
    /// its texture's v grows, to the unit <paramref name="strand"/>, which must be at right angles to it.
    /// </summary>
    private static Quaternion PlaneTurnedTo(Vector3 normal, Vector3 strand)
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

    /// <summary>
    /// The world normal that the normals target <paramref name="normals"/> stores at <paramref name="pixel"/> in the two
    /// channels from <paramref name="channel"/>: the C# copy of octDecode in octahedral.glsl.
    /// </summary>
    private static Vector3 StoredNormal(float[] normals, int pixel, int channel)
    {
        var e = new Double3(normals[pixel * 4 + channel], normals[pixel * 4 + channel + 1], 0);
        Double3 n = e with { Z = 1 - Math.Abs(e.X) - Math.Abs(e.Y) };
        if (n.Z < 0)
            n = n with { X = (1 - Math.Abs(e.Y)) * (e.X >= 0 ? 1 : -1), Y = (1 - Math.Abs(e.X)) * (e.Y >= 0 ? 1 : -1) };
        return n.Normalized().ToVector3();
    }

    /// <summary>The half-resolution texel whose representative pixel's block holds the projection of <paramref name="point"/>.</summary>
    private static (int X, int Y) TexelOf(Vector3 point)
    {
        Vector2 pixel = PixelOf(point, CornerView);
        return (X: (int)pixel.X / 2, Y: (int)pixel.Y / 2);
    }

    /// <summary>
    /// Pins the sector pass at <paramref name="texel"/> to the C# copy: the estimate within the half floats of term A and
    /// the share of the sectors next to a boundary within <see cref="TieTolerance"/> of a tie, which the GPU may flip.
    /// </summary>
    private void ShouldMatch(
        SectorPassOutput gpu,
        (int X, int Y) texel,
        NearTexel expected,
        double brightest,
        Vector3 point
    ) => ShouldMatch(gpu: gpu, texel: texel, bounds: (expected, expected), brightest: brightest, point: point);

    /// <summary>
    /// Pins the sector pass at <paramref name="texel"/> to the C# copy as <see cref="ShouldMatch(SectorPassOutput, ValueTuple{int, int}, NearTexel, double, Vector3)"/>
    /// does, with a net bounce between the two estimates of <paramref name="bounds"/>, which share their sectors.
    /// </summary>
    private void ShouldMatch(
        SectorPassOutput gpu,
        (int X, int Y) texel,
        (NearTexel First, NearTexel Second) bounds,
        double brightest,
        Vector3 point
    )
    {
        NearTexel expected = bounds.First;
        double flippedShare = FlippedShare(expected);
        int i = (texel.Y * gpu.HalfWidth + texel.X) * 4;
        var bounce = new Double3(gpu.Term[i], gpu.Term[i + 1], gpu.Term[i + 2]);
        int j = (texel.Y * gpu.HalfWidth + texel.X) * 3;
        var openShare = new Double3(gpu.OpenShare[j], gpu.OpenShare[j + 1], gpu.OpenShare[j + 2]);
        AmbientEstimate estimate = expected.Estimate;
        Double3 other = bounds.Second.Estimate.Bounce;
        output.WriteLine(
            $"{point}: GPU ({bounce}, {gpu.Term[i + 3]:0.000000}, S {openShare}), C# ({estimate.Bounce} to {other}, "
                + $"{estimate.Occlusion:0.000000})"
        );
        OpenShareShouldLieWithin(openShare, OpenShareRange(bounds, flippedShare, brightest), point);
        ((double)gpu.Term[i + 3])
            .Should()
            .BeApproximately(estimate.Occlusion, flippedShare + HalfFloatError(estimate.Occlusion), "V at {0}", point);
        foreach (
            (double actual, double first, double second) in (ReadOnlySpan<(double Actual, double First, double Second)>)
                [
                    (bounce.X, estimate.Bounce.X, other.X),
                    (bounce.Y, estimate.Bounce.Y, other.Y),
                    (bounce.Z, estimate.Bounce.Z, other.Z),
                ]
        )
        {
            double low = Math.Min(first, second);
            double high = Math.Max(first, second);
            double tolerance = (flippedShare + FloatError) * brightest + HalfFloatError(Math.Max(-low, high));
            actual
                .Should()
                .BeInRange(
                    low - tolerance,
                    high + tolerance,
                    "the bounce at {0}: each blocker gives its light to the sectors that it newly blocks if it faces",
                    point
                );
        }
    }

    /// <summary>
    /// The share of the cosine weight in the sectors next to a tie of <paramref name="texel"/>, which single floats can
    /// move to the other side of the tie.
    /// </summary>
    private static double FlippedShare(NearTexel texel)
    {
        double totalWeight = AmbientEstimate.TotalWeight(texel.Slices);
        double flippedShare = 0;
        for (int slice = 0; slice < texel.Slices.Length; slice++)
            flippedShare +=
                texel.Ties[slice].Count * texel.Slices[slice].Sectors.Weight / (SliceSectors.Count * totalWeight);
        return flippedShare;
    }

    /// <summary>Asserts that each channel of the sector pass's S, <paramref name="openShare"/>, lies within <paramref name="range"/>.</summary>
    private static void OpenShareShouldLieWithin(
        Double3 openShare,
        (Double3 Least, Double3 Most, Double3 Tolerance) range,
        Vector3 point
    )
    {
        foreach (
            (double actual, double least, double most, double tolerance) in (ReadOnlySpan<(
                double Actual,
                double Least,
                double Most,
                double Tolerance
            )>)
                [
                    (openShare.X, range.Least.X, range.Most.X, range.Tolerance.X),
                    (openShare.Y, range.Least.Y, range.Most.Y, range.Tolerance.Y),
                    (openShare.Z, range.Least.Z, range.Most.Z, range.Tolerance.Z),
                ]
        )
            actual
                .Should()
                .BeInRange(
                    least - tolerance - HalfFloatError(1),
                    most + tolerance + HalfFloatError(1),
                    "S at {0}: the open pieces' share of the map's light, or V's share where the map holds none",
                    point
                );
    }

    /// <summary>
    /// The least and the most S per channel that the reads of the two estimates of <paramref name="bounds"/> allow: the
    /// least open and the most blocked map light, and the reverse. A channel in which neither holds map light takes V's
    /// share. A sector next to a tie moves its light, at most <paramref name="brightest"/>, between the two sums: the
    /// tolerance is the share of the flipped sectors times that light over the mean read.
    /// </summary>
    private static (Double3 Least, Double3 Most, Double3 Tolerance) OpenShareRange(
        (NearTexel First, NearTexel Second) bounds,
        double flippedShare,
        double brightest
    )
    {
        double scale = SliceSectors.Count * AmbientEstimate.TotalWeight(bounds.First.Slices);
        Double3 fallback = bounds.First.Estimate.OpenShare;
        (Double3 open0, Double3 open1) = (bounds.First.OpenMapLight, bounds.Second.OpenMapLight);
        (Double3 blocked0, Double3 blocked1) = (bounds.First.BlockedMapLight, bounds.Second.BlockedMapLight);
        (double Least, double Most, double Tolerance) Channel(Func<Double3, double> of)
        {
            double leastOpen = Math.Min(of(open0), of(open1));
            double mostOpen = Math.Max(of(open0), of(open1));
            double leastBlocked = Math.Min(of(blocked0), of(blocked1));
            double mostBlocked = Math.Max(of(blocked0), of(blocked1));
            if (mostOpen + mostBlocked <= 0)
                return (Least: of(fallback), Most: of(fallback), Tolerance: flippedShare);
            if (leastOpen + leastBlocked <= 0)
                return (Least: 0, Most: 1, Tolerance: 0);
            return (
                Least: leastOpen / (leastOpen + mostBlocked),
                Most: mostOpen / (mostOpen + leastBlocked),
                Tolerance: flippedShare * brightest * scale / (leastOpen + leastBlocked)
            );
        }
        (double Least, double Most, double Tolerance) x = Channel(v => v.X);
        (double Least, double Most, double Tolerance) y = Channel(v => v.Y);
        (double Least, double Most, double Tolerance) z = Channel(v => v.Z);
        return (
            Least: new(x.Least, y.Least, z.Least),
            Most: new(x.Most, y.Most, z.Most),
            Tolerance: new(x.Tolerance, y.Tolerance, z.Tolerance)
        );
    }

    /// <summary>The rounding of a value of about <paramref name="value"/> into term A's half floats, and of the GPU's single floats.</summary>
    private static double HalfFloatError(double value) => Math.Abs(value) * Math.Pow(2, -11) + FloatError;

    /// <summary>The corner's lights: the sun, which lights the floor and both walls, and a dim sky and ground of distinct colors.</summary>
    private static RenderSnapshot LitCorner()
    {
        RenderSnapshot lit = Gpu.Snapshot(SunDirection, starIrradiance: 1);
        lit.Ambient = new()
        {
            SkyColor = Sky,
            GroundColor = Ground,
            Intensity = 0.5f,
        };
        return lit;
    }

    /// <summary>A smooth metal or a rough non-metal, in decoded sRGB bytes and roughnesses that the G-buffer stores exactly.</summary>
    private static MaterialDesc MaterialOf(bool smoothMetal) =>
        new(
            BaseColor: smoothMetal
                ? new(x: (float)Decoded(250), y: (float)Decoded(210), z: (float)Decoded(140), w: 1)
                : new(x: (float)Decoded(200), y: (float)Decoded(60), z: (float)Decoded(40), w: 1),
            Roughness: smoothMetal ? 0.2f : 0.8f,
            Model: new StandardShading(Metallic: smoothMetal ? 1 : 0),
            Reflectance: Reflectance
        );

    /// <summary>The unit direction from the eye through the center of <paramref name="pixel"/>, for the corner's camera.</summary>
    private static Double3 CornerRay(Vector2 pixel) =>
        Vector3.Transform(Ray(pixel, Width, Height).ToVector3(), CornerView);

    /// <summary>The RGB of <paramref name="pixel"/> in the full-resolution RGBA image <paramref name="rgba"/>.</summary>
    private static Vector3 DirectAt(float[] rgba, Vector2 pixel) =>
        ReflectionTesting.At(rgba, ((int)pixel.X, (int)pixel.Y));

    /// <summary>The estimate of <paramref name="pixel"/> in the estimate target <paramref name="estimate"/>.</summary>
    private static AmbientEstimate EstimateAt(float[] estimate, Vector2 pixel)
    {
        int i = ((int)pixel.Y * Width + (int)pixel.X) * 4;
        return new(
            Bounce: new(estimate[i], estimate[i + 1], estimate[i + 2]),
            Occlusion: estimate[i + 3],
            OpenShare: new Double3(1, 1, 1) * estimate[i + 3]
        );
    }

    private static float LargestChannel(Vector3 color) => MathF.Max(color.X, MathF.Max(color.Y, color.Z));

    /// <summary>
    /// The C# ambient light of the surface that <paramref name="pixel"/>'s center sees, with the sky shares as the
    /// G-buffer stores them and the estimate that the estimate target holds, its pre-exposed bounce over the exposure.
    /// </summary>
    private static Vector3 ExpectedAmbientLight(
        MaterialDesc desc,
        Double3 normal,
        Vector2 pixel,
        AmbientEstimate estimate
    )
    {
        Double3 toView = -CornerRay(pixel);
        // The specular target stores the lobe's sky share in 8 bits, and the emissive target the normal's in a half float.
        double lobeSky =
            Math.Round(LobeSkyShare(normal: normal, toView: toView, roughness: desc.Roughness) * 255) / 255;
        double normalSky = (double)(Half)NormalSkyShare(normal);
        MaterialSample material = TestMaterials.SampleOf(desc);
        return material
            .ReflectedHemisphere(
                normal: normal,
                toView: toView,
                sky: Sky * 0.5f,
                ground: Ground * 0.5f,
                normalSkyShare: normalSky,
                lobeSkyShare: lobeSky,
                occlusion: estimate.Occlusion,
                bounce: estimate.Bounce / Math.Pow(2, ExposureStops),
                tables: BrdfTables.Shared
            )
            .ToVector3();
    }

    /// <summary>
    /// Map 0's specular map as the renderer read it back: <paramref name="Levels"/>[level][face] holds a face's RGBA
    /// halves, row by row from the top, <paramref name="FaceSize"/> texels on a side at level 0.
    /// </summary>
    private sealed record SpecularMap(Half[][][] Levels, int FaceSize)
    {
        /// <summary>How far the GPU's level of detail may lie from the C# copy's: far above its single floats' error.</summary>
        private const double LodTolerance = 1e-3;

        public static SpecularMap Read(VulkanRenderer renderer, int faceSize) =>
            new(
                Levels:
                [
                    .. Enumerable
                        .Range(0, PrefilterLevels.Count(faceSize))
                        .Select(level =>
                            Enumerable
                                .Range(0, CubeFaces.Count)
                                .Select(face =>
                                    renderer.ReadEnvironmentFace(
                                        map: 0,
                                        face: (CubeFace)face,
                                        level: level,
                                        image: EnvironmentImage.Specular
                                    )
                                )
                                .ToArray()
                        ),
                ],
                FaceSize: faceSize
            );

        /// <summary>The brightest channel of any texel of any level.</summary>
        public double Brightest => Channels.Max();

        /// <summary>The darkest channel of any texel of any level.</summary>
        public double Darkest => Channels.Min();

        private IEnumerable<double> Channels =>
            Levels.SelectMany(faces =>
                faces.SelectMany(face => face.Where((_, i) => i % 4 != 3).Select(h => (double)h))
            );

        /// <summary>
        /// The least and the greatest light, per channel, of the texels that a trilinear read along the unit world
        /// direction <paramref name="direction"/> may blend for a part <paramref name="width"/> rad wide: on each level
        /// within one level of its level of detail, every texel within one texel of its place, across a face's edge
        /// too, as seamless cube filtering reads. Steps of half a texel reach every such texel.
        /// </summary>
        public (Double3 Low, Double3 High) Range(Vector3 direction, double width)
        {
            double lod = GgxLobe.RoughnessForWidth(width) * (Levels.Length - 1);
            var low = new Double3(double.MaxValue, double.MaxValue, double.MaxValue);
            var high = new Double3(double.MinValue, double.MinValue, double.MinValue);
            int lowest = Math.Max(0, (int)Math.Floor(lod - LodTolerance));
            int highest = Math.Min(Levels.Length - 1, (int)Math.Ceiling(lod + LodTolerance));
            (CubeFace face, float s, float t) = CubeFaces.Locate(direction);
            for (int level = lowest; level <= highest; level++)
            {
                int size = FaceSize >> level;
                for (int dx = -2; dx <= 2; dx++)
                for (int dy = -2; dy <= 2; dy++)
                {
                    (CubeFace at, float u, float v) = CubeFaces.Locate(
                        CubeFaces.Direction(face, s + 0.5f * dx / size, t + 0.5f * dy / size)
                    );
                    int x = Math.Clamp((int)(u * size), 0, size - 1);
                    int y = Math.Clamp((int)(v * size), 0, size - 1);
                    Half[] texels = Levels[level][(int)at];
                    int i = (y * size + x) * 4;
                    var light = new Double3((double)texels[i], (double)texels[i + 1], (double)texels[i + 2]);
                    low = new(Math.Min(low.X, light.X), Math.Min(low.Y, light.Y), Math.Min(low.Z, light.Z));
                    high = new(Math.Max(high.X, light.X), Math.Max(high.Y, light.Y), Math.Max(high.Z, light.Z));
                }
            }
            return (Low: low, High: high);
        }
    }

    private static double TanHalfX => Math.Tan(Gpu.Fov / 2) * Width / Height;

    /// <summary>
    /// The view position at view depth <paramref name="z"/> on the ray through the place <paramref name="place"/>
    /// in half-resolution texels, where whole numbers are a texel's representative pixel.
    /// </summary>
    private static Double3 ViewPosition((double X, double Y) place, double z)
    {
        double tanHalfY = Math.Tan(Gpu.Fov / 2);
        double screenX = (2 * place.X + 0.5) / Width;
        double screenY = (2 * place.Y + 0.5) / Height;
        return new Double3((2 * screenX - 1) * TanHalfX, (1 - 2 * screenY) * tanHalfY, 1) * z;
    }

    private static Double3 ToView(Vector3 world) =>
        new(Vector3.Dot(world, Right), Vector3.Dot(world, Up), Vector3.Dot(world, Forward));

    /// <summary>
    /// The sectors next to a boundary that lies within <see cref="TieTolerance"/> of a tie: an edge of an own
    /// plane in the slice, or an end of a step within the radius.
    /// </summary>
    private static SectorMask Ties(
        SliceSectors sectors,
        Double3 center,
        OwnSurface ownSurface,
        double radius,
        List<NearStep> steps
    )
    {
        SectorMask ties = SectorMask.None;
        if (ownSurface is OwnPlane(var geometricNormal))
        {
            double planeAngle = Math.Atan2(
                Double3.Dot(geometricNormal, sectors.Slice.TowardPlusSide),
                Double3.Dot(geometricNormal, sectors.Slice.ToEye)
            );
            ties = TiesAt(sectors, planeAngle - Math.PI / 2, planeAngle + Math.PI / 2);
        }
        foreach (NearStep step in steps.Where(step => Double3.Distance(step.Position, center) < radius))
        {
            (double front, double back) = SectorSearch.StepAngles(
                center: center,
                toEye: sectors.Slice.ToEye,
                position: step.Position,
                side: step.Side,
                thicknessShare: CellBounds.ThicknessShare
            );
            ties = ties.Union(TiesAt(sectors, front, back));
        }
        return ties;
    }

    /// <summary>The sectors next to the boundary 32 m + ½ of each angle that lies within <see cref="TieTolerance"/> of a tie.</summary>
    private static SectorMask TiesAt(SliceSectors sectors, params ReadOnlySpan<double> angles)
    {
        SectorMask ties = SectorMask.None;
        foreach (double angle in angles)
        {
            double boundary = SliceSectors.Count * sectors.MeasureFraction(angle) + 0.5;
            int nearest = (int)Math.Round(boundary);
            if (Math.Abs(boundary - nearest) < TieTolerance && nearest >= 1)
                ties = ties.Union(SectorMask.Run(start: nearest - 1, end: nearest));
        }
        return ties;
    }

    /// <summary>
    /// The sector pass's term A, as the renderer read it back, for a half image <paramref name="HalfWidth"/> texels wide:
    /// the bounce and V in RGBA, and S in RGB.
    /// </summary>
    private sealed record SectorPassOutput(float[] Term, float[] OpenShare, int HalfWidth);

    /// <summary>
    /// The C# copy's near slices of a texel, the sectors of each slice below the texel's own surface, and in each slice
    /// the sectors next to a boundary within <see cref="TieTolerance"/> of a tie, which the GPU's single floats may move
    /// by one sector.
    /// </summary>
    private sealed record NearTexel(NearSlice[] Slices, SectorMask[] OwnSurface, SectorMask[] Ties)
    {
        public AmbientEstimate Estimate => AmbientEstimate.OfSlices(Slices);

        /// <summary>The map's light along the open sectors, each slice's by its weight.</summary>
        public Double3 OpenMapLight =>
            Slices.Aggregate(Double3.Zero, (sum, s) => sum + s.OpenMapLight * s.Sectors.Weight);

        /// <summary>The map's light along the blocked sectors, each slice's by its weight.</summary>
        public Double3 BlockedMapLight =>
            Slices.Aggregate(Double3.Zero, (sum, s) => sum + s.BlockedMapLight * s.Sectors.Weight);
    }

    /// <summary>
    /// The depth pass's flags against the C# copy's answers: each mismatch, and how many of the compared neighbors lie
    /// beyond the image, see the sky, continue the plane and do not.
    /// </summary>
    private sealed record FlagComparison(List<string> Mismatches, int Beyond, int Sky, int Continues, int Breaks);

    /// <summary>
    /// The depth pass's copies of the view depth and the octahedral world geometric normal of each half-resolution
    /// texel, as <see cref="VulkanRenderer.ReadOcclusionDepthAndNormal"/> read them back, under <see cref="CornerView"/>.
    /// </summary>
    private sealed record TexelCopies(float[] Depths, float[] Normals, int Width, int Height)
    {
        /// <summary>The edge neighbors in the order of the flags' bits: left, up, right, down.</summary>
        private static readonly (int X, int Y)[] Sides = [(-1, 0), (0, -1), (1, 0), (0, 1)];

        /// <summary>
        /// Compares each texel's flags with <see cref="PixelPlane.ContinuesInto"/> for the copies. A sky texel must hold
        /// no flag. A neighbor whose test lies within <see cref="UndecidedShare"/> of the cut is left out.
        /// </summary>
        public FlagComparison CompareFlags(byte[] flags)
        {
            var comparison = new FlagComparison(Mismatches: [], Beyond: 0, Sky: 0, Continues: 0, Breaks: 0);
            for (int y = 0; y < Height; y++)
            for (int x = 0; x < Width; x++)
            {
                byte texelFlags = flags[y * Width + x];
                if (PlaneAt((x, y)) is { } plane)
                    for (int side = 0; side < 4; side++)
                        comparison = CompareSide(
                            comparison: comparison,
                            texel: (x, y),
                            plane: plane,
                            side: side,
                            flagged: ((texelFlags >> side) & 1) != 0
                        );
                else if (texelFlags != 0)
                    comparison.Mismatches.Add($"({x}, {y}) sees the sky and holds the flags {texelFlags}");
            }
            return comparison;
        }

        /// <summary>Adds the answer toward one side of a texel that sees a surface to <paramref name="comparison"/>.</summary>
        private FlagComparison CompareSide(
            FlagComparison comparison,
            (int X, int Y) texel,
            PixelPlane plane,
            int side,
            bool flagged
        )
        {
            (int X, int Y) neighbor = (texel.X + Sides[side].X, texel.Y + Sides[side].Y);
            bool inside = neighbor.X >= 0 && neighbor.Y >= 0 && neighbor.X < Width && neighbor.Y < Height;
            PixelPlane? other = inside ? PlaneAt(neighbor) : null;
            if (
                other is { } seen
                && (
                    ScreenTesting.NearTheCut(plane, seen, UndecidedShare)
                    || ScreenTesting.NearTheCut(seen, plane, UndecidedShare)
                )
            )
                return comparison;
            bool expected = plane.ContinuesInto(insideImage: inside, neighbor: other);
            if (flagged != expected)
                comparison.Mismatches.Add($"{texel} toward {Sides[side]}: the GPU {flagged}, the C# copy {expected}");
            if (!inside)
                return comparison with { Beyond = comparison.Beyond + 1 };
            if (other is null)
                return comparison with { Sky = comparison.Sky + 1 };
            return expected
                ? comparison with { Continues = comparison.Continues + 1 }
                : comparison with { Breaks = comparison.Breaks + 1 };
        }

        /// <summary>The C# copy's plane of a texel inside the image, from its copies, or null for sky.</summary>
        private PixelPlane? PlaneAt((int X, int Y) texel)
        {
            int index = texel.Y * Width + texel.X;
            if (Depths[index] >= (float)HorizonSearch.SkyDepth)
                return null;
            var read = new StepTexel(
                Position: ViewPosition(texel, Depths[index]),
                GeometricNormal: ToView(OctahedralNormal.Decode(new(Normals[2 * index], Normals[2 * index + 1]))),
                DirectLight: Double3.Zero,
                Card: null
            );
            return SectorSearch.TexelPlane(texel: texel, read: read, rayThrough: place => ViewPosition(place, 1));
        }
    }

    /// <summary>
    /// The images of the corner that the sector pass reads, as the renderer read them back: the view depth of each
    /// half-resolution texel and the diffuse image, the bounce source where nothing glows. The corner's three planes give the normals.
    /// </summary>
    private sealed record CornerImages(float[] ViewDepth, (int Width, int Height) HalfSize, float[] Source)
    {
        /// <summary>
        /// The near slices of <paramref name="texel"/> by the C# copy: the steps of each slice of GTAO, then
        /// <see cref="SectorSearch.Near"/> with each step's view position, geometric normal and direct light. With
        /// <paramref name="everyBlockerFaces"/>, each step's normal faces the center, as if no blocker faced away.
        /// <paramref name="mapLight"/> gives the map's light along a unit world direction for a part of a width in rad,
        /// or is null with the map off.
        /// </summary>
        public NearTexel Near(
            (int X, int Y) texel,
            VisibilityBitmaskLight setting,
            bool everyBlockerFaces,
            Func<Vector3, double, Double3>? mapLight = null
        )
        {
            Func<Double3, double, Double3>? viewMapLight = mapLight is null
                ? null
                : (direction, width) => mapLight(Vector3.Normalize(ToWorld(direction)), width);
            double z = DepthAt(texel);
            Double3 center = ViewPosition(texel, z * HorizonSearch.CenterBias);
            Double3 toEye = (-center).Normalized();
            Double3 normal = ToView(ShadingNormalAt(ViewPosition(texel, z)));
            Double3 geometricNormal = NormalAt(ViewPosition(texel, z));
            (float sliceNoise, float stepNoise) = OcclusionNoise.At((uint)texel.X, (uint)texel.Y);
            double radiusInTexels = setting.RadiusMeters / HorizonSearch.TexelWidth(z, TanHalfX, Width);
            var slices = new NearSlice[setting.Slices];
            var ownSurface = new SectorMask[setting.Slices];
            var ties = new SectorMask[setting.Slices];
            for (int slice = 0; slice < setting.Slices; slice++)
            {
                double angle = HorizonSearch.SliceAngle(slice, setting.Slices, sliceNoise);
                var sectors = new SliceSectors(new HorizonSlice(toEye, normal, angle));
                ownSurface[slice] = sectors.BelowPlane(geometricNormal);
                List<NearStep> steps = SectorSearch.SliceSteps(
                    texel: texel,
                    slice: slice,
                    sliceAngle: angle,
                    stepsPerSide: setting.StepsPerSide,
                    stepNoise: stepNoise,
                    radiusInTexels: radiusInTexels,
                    halfSize: HalfSize,
                    rayThrough: place => ViewPosition(place, 1),
                    texelAt: StepTexelAt,
                    ownSurface: new OwnPlane(geometricNormal)
                );
                if (everyBlockerFaces)
                    steps =
                    [
                        .. steps.Select(step => step with { GeometricNormal = (center - step.Position).Normalized() }),
                    ];
                slices[slice] = SectorSearch.Near(
                    sectors: sectors,
                    center: center,
                    ownSurface: new OwnPlane(geometricNormal),
                    radius: setting.RadiusMeters,
                    thicknessShare: CellBounds.ThicknessShare,
                    steps: [.. steps],
                    mapLight: viewMapLight
                );
                ties[slice] = Ties(
                    sectors: sectors,
                    center: center,
                    ownSurface: new OwnPlane(geometricNormal),
                    radius: setting.RadiusMeters,
                    steps: steps
                );
            }

            return new(Slices: slices, OwnSurface: ownSurface, Ties: ties);
        }

        /// <summary>What a step reads at <paramref name="texel"/>, or null for sky.</summary>
        private StepTexel? StepTexelAt((int X, int Y) texel)
        {
            if (DepthAt(texel) >= HorizonSearch.SkyDepth)
                return null;
            Double3 position = ViewPosition(texel, DepthAt(texel));
            int pixel = (2 * texel.Y * Width + 2 * texel.X) * 4;
            return new StepTexel(
                Position: position,
                GeometricNormal: NormalAt(position),
                DirectLight: new(Source[pixel], Source[pixel + 1], Source[pixel + 2]),
                Card: null
            );
        }

        private double DepthAt((int X, int Y) texel) => ViewDepth[texel.Y * HalfSize.Width + texel.X];

        /// <summary>The view-space geometric normal of the surface at the view position <paramref name="position"/>.</summary>
        private static Double3 NormalAt(Double3 position) => ToView(GeometricNormalAt(ToWorld(position)));

        /// <summary>The world shading normal at the view position <paramref name="position"/>: the platform's top is normal-mapped.</summary>
        private static Vector3 ShadingNormalAt(Double3 position)
        {
            Vector3 world = ToWorld(position);
            return BoxFaceNormal(world, PlatformCenter, PlatformSize) == Vector3.UnitY
                ? TiltedMapNormal
                : GeometricNormalAt(world);
        }

        /// <summary>
        /// The world normal of the face of a box nearest <paramref name="world"/> on the box, and else of the corner's
        /// plane nearest it.
        /// </summary>
        private static Vector3 GeometricNormalAt(Vector3 world) =>
            BoxFaceNormal(world, LowBoxCenter, LowBoxSize)
            ?? BoxFaceNormal(world, PlatformCenter, PlatformSize)
            ?? PlaneNormal(world);

        private static Vector3 ToWorld(Double3 position) =>
            (Right * (float)position.X) + (Up * (float)position.Y) + (Forward * (float)position.Z);

        /// <summary>
        /// The world normal of the face nearest <paramref name="world"/> of the box at <paramref name="center"/> of
        /// <paramref name="size"/>, null off the box by more than 1 mm, well above the error of a read-back position.
        /// </summary>
        private static Vector3? BoxFaceNormal(Vector3 world, Vector3 center, Vector3 size)
        {
            Vector3 local = world - center;
            Vector3 inside = (size / 2) - Vector3.Abs(local);
            if (inside.X < -1e-3f || inside.Y < -1e-3f || inside.Z < -1e-3f)
                return null;
            float x = MathF.Abs(inside.X);
            float y = MathF.Abs(inside.Y);
            float z = MathF.Abs(inside.Z);
            if (y <= x && y <= z)
                return new Vector3(0, MathF.Sign(local.Y), 0);
            return x <= z ? new Vector3(MathF.Sign(local.X), 0, 0) : new Vector3(0, 0, MathF.Sign(local.Z));
        }

        /// <summary>The world normal of the corner's plane nearest <paramref name="world"/>.</summary>
        private static Vector3 PlaneNormal(Vector3 world)
        {
            float toFloor = MathF.Abs(world.Y - FloorHeight);
            float toBackWall = MathF.Abs(world.Z - BackWallZ);
            float toSideWall = MathF.Abs(world.X - SideWallX);
            Vector3 normal = Vector3.UnitX;
            if (toFloor <= toBackWall && toFloor <= toSideWall)
                normal = Vector3.UnitY;
            else if (toBackWall <= toSideWall)
                normal = Vector3.UnitZ;
            return normal;
        }
    }

    /// <summary>
    /// The images of the corner with the hair card that the sector pass reads, as the renderer read them back: the view
    /// depth of each half-resolution texel, the diffuse image, the bounce source where nothing glows, the normals target,
    /// whose normals the C# copy decodes as the pass does, and the shading target, which holds each hair pixel's card.
    /// </summary>
    private sealed record HairCardImages(
        float[] ViewDepth,
        (int Width, int Height) HalfSize,
        float[] Source,
        float[] Normals,
        byte[] Shading
    )
    {
        /// <summary>
        /// The near slices of the hair receiver at <paramref name="texel"/> by the C# copy, whose own surface is its
        /// card, and how many of its steps read a pixel of its card.
        /// </summary>
        public (NearTexel Texel, int OnOwnCard) NearHair((int X, int Y) texel, VisibilityBitmaskLight setting)
        {
            double z = DepthAt(texel);
            Double3 center = ViewPosition(texel, z * HorizonSearch.CenterBias);
            Double3 toEye = (-center).Normalized();
            int pixel = 2 * texel.Y * Width + 2 * texel.X;
            Double3 fiberNormal = ToView(StoredNormal(Normals, pixel, channel: 0));
            Double3 geometricNormal = ToView(StoredNormal(Normals, pixel, channel: 2));
            var card = new OwnCard(CardAt(pixel)!.Value);
            (float sliceNoise, float stepNoise) = OcclusionNoise.At((uint)texel.X, (uint)texel.Y);
            double radiusInTexels = setting.RadiusMeters / HorizonSearch.TexelWidth(z, TanHalfX, Width);
            var slices = new NearSlice[setting.Slices];
            var ties = new SectorMask[setting.Slices];
            int onOwnCard = 0;
            for (int slice = 0; slice < setting.Slices; slice++)
            {
                double angle = HorizonSearch.SliceAngle(slice, setting.Slices, sliceNoise);
                var sectors = new SliceSectors(new HorizonSlice(toEye, fiberNormal, angle));
                List<NearStep> Steps(OwnSurface ownSurface) =>
                    SectorSearch.SliceSteps(
                        texel: texel,
                        slice: slice,
                        sliceAngle: angle,
                        stepsPerSide: setting.StepsPerSide,
                        stepNoise: stepNoise,
                        radiusInTexels: radiusInTexels,
                        halfSize: HalfSize,
                        rayThrough: place => ViewPosition(place, 1),
                        texelAt: StepTexelAt,
                        ownSurface: ownSurface
                    );
                List<NearStep> steps = Steps(card);
                onOwnCard += Steps(new OwnPlane(geometricNormal)).Count - steps.Count;
                slices[slice] = SectorSearch.Near(
                    sectors: sectors,
                    center: center,
                    ownSurface: card,
                    radius: setting.RadiusMeters,
                    thicknessShare: CellBounds.ThicknessShare,
                    steps: [.. steps],
                    mapLight: null
                );
                ties[slice] = Ties(
                    sectors: sectors,
                    center: center,
                    ownSurface: card,
                    radius: setting.RadiusMeters,
                    steps: steps
                );
            }

            // A hair receiver's own surface blocks no sector.
            return (
                Texel: new(Slices: slices, OwnSurface: new SectorMask[setting.Slices], Ties: ties),
                OnOwnCard: onOwnCard
            );
        }

        /// <summary>The card code of the pixel of index <paramref name="pixel"/>, or null where it is not hair.</summary>
        private uint? CardAt(int pixel) => CardCode.Read(b: Shading[pixel * 4 + 2], a: Shading[pixel * 4 + 3]);

        /// <summary>What a step reads at <paramref name="texel"/>, or null for sky: its geometric normal as the normals target stores it.</summary>
        private StepTexel? StepTexelAt((int X, int Y) texel)
        {
            if (DepthAt(texel) >= HorizonSearch.SkyDepth)
                return null;
            int pixel = 2 * texel.Y * Width + 2 * texel.X;
            return new StepTexel(
                Position: ViewPosition(texel, DepthAt(texel)),
                GeometricNormal: ToView(StoredNormal(Normals, pixel, channel: 2)),
                DirectLight: new(Source[pixel * 4], Source[pixel * 4 + 1], Source[pixel * 4 + 2]),
                Card: CardAt(pixel)
            );
        }

        private double DepthAt((int X, int Y) texel) => ViewDepth[texel.Y * HalfSize.Width + texel.X];
    }
}
