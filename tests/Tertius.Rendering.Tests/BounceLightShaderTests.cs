using System.Numerics;
using static Tertius.Rendering.Tests.BrdfTesting;
using static Tertius.Rendering.Tests.OcclusionScenes;

namespace Tertius.Rendering.Tests;

/// <summary>
/// The tests that pin the bounce light's shaders to the C# copy (docs/plans/screen-space-illumination.md, "GPU tests of
/// the acceptance criteria"), in the corner under the sun and a dim sky and ground of distinct colors: the sector pass
/// blocks the sectors and gathers the light that <see cref="SectorSearch"/> does from the same images, and the ambient
/// pass adds what <see cref="MaterialSample.ReflectedHemisphere"/> gives at the estimate that the estimate target holds.
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

    /// <summary>The low box beside the open floor, 0.2 m tall, whose top the floor beside it sees from below.</summary>
    private static readonly Vector3 LowBoxCenter = new(0.5f, FloorHeight + 0.1f, -3);

    private static readonly Vector3 LowBoxSize = new(0.3f, 0.2f, 0.3f);

    /// <summary>
    /// The floor beside the low box, on its side away from the camera, which sees the box's top past it: its
    /// blockers include that top, which faces away from it.
    /// </summary>
    private static readonly Vector3 BesideTheLowBox = new(0.85f, FloorHeight, -3);

    /// <summary>
    /// A platform 5 cm above the floor, whose top's normal map tilts the shading normal 38° toward +x, so its shading
    /// hemisphere reaches below its top. Its bottom lies under the floor.
    /// </summary>
    private static readonly Vector3 PlatformCenter = new(-0.7f, FloorHeight - 0.2f, -2.6f);

    private static readonly Vector3 PlatformSize = new(0.6f, 0.5f, 0.6f);

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

    [Fact]
    public void TheSectorPassGathersWhatTheCSharpCopyGathers()
    {
        VisibilityBitmaskLight setting = VisibilityBitmaskLight.Default;
        VulkanRenderer renderer = Gpu.Headless(screenSpaceLight: setting);
        try
        {
            // The term that the sector pass leaves, before the blur overwrites it.
            renderer.Graph.Skipped.UnionWith(["occlusion far rays", "occlusion denoise", "occlusion upsample"]);
            MaterialId matte = Matte(renderer);
            RenderSnapshot scene = Corner(renderer, matte, LitCorner());
            MeshId box = UnitBox(renderer);
            AddSlab(snapshot: scene, box: box, material: matte, center: LowBoxCenter, size: LowBoxSize);
            AddSlab(
                snapshot: scene,
                box: box,
                material: TiltedMatte(renderer),
                center: PlatformCenter,
                size: PlatformSize
            );
            renderer.Render(scene, 1f);
            (float[] term, int halfWidth, int halfHeight) = renderer.ReadOcclusionTerm();
            var gpu = new SectorPassOutput(Term: term, Masks: renderer.ReadSectorMasks(), HalfWidth: halfWidth);
            var images = new CornerImages(
                ViewDepth: renderer.ReadOcclusionViewDepth().Depths,
                HalfSize: (Width: halfWidth, Height: halfHeight),
                Direct: renderer.ReadDirect()
            );
            // The brightest channel of the direct light, which one sector's light can reach.
            double brightest = images.Direct.Where((_, i) => i % 4 != 3).Max();

            AmbientEstimate atTheCorner = images
                .Near(TexelOf(FloorAtTheCorner), setting, everyBlockerFaces: false)
                .Estimate;
            atTheCorner.Bounce.X.Should().BePositive("the sunlit walls bounce their light onto the corner");
            (
                images.Near(TexelOf(BesideTheLowBox), setting, everyBlockerFaces: true).Estimate.Bounce
                - images.Near(TexelOf(BesideTheLowBox), setting, everyBlockerFaces: false).Estimate.Bounce
            )
                .Length.Should()
                .BePositive("the test needs a blocker that faces away: the box's top, seen from below");
            images
                .Near(TexelOf(OnThePlatform), setting, everyBlockerFaces: false)
                .OwnSurface.Sum(sectors => sectors.Count)
                .Should()
                .BePositive("the test needs a shading hemisphere that reaches below its own surface");
            foreach (Vector3 point in (Vector3[])[.. SectorPoints, BesideTheLowBox, OnThePlatform])
            {
                (int X, int Y) texel = TexelOf(point);
                NearTexel expected = images.Near(texel, setting, everyBlockerFaces: false);
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
            EstimateAt(estimate, corner).Bounce.X.Should().BePositive("the sunlit walls bounce light onto the corner");
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

    /// <summary>A light gray matte non-metal whose normal map is <see cref="TiltedMapTexel"/>.</summary>
    private static MaterialId TiltedMatte(VulkanRenderer renderer) =>
        renderer.CreateMaterial(
            MaterialDesc.Color(new(x: 0.8f, y: 0.8f, z: 0.8f, w: 1), roughness: 0.8f) with
            {
                NormalMap = renderer.CreateTexture(new(Width: 1, Height: 1, Rgba: TiltedMapTexel, Srgb: false)),
            }
        );

    /// <summary>The half-resolution texel whose representative pixel's block holds the projection of <paramref name="point"/>.</summary>
    private static (int X, int Y) TexelOf(Vector3 point)
    {
        Vector2 pixel = PixelOf(point, CornerView);
        return (X: (int)pixel.X / 2, Y: (int)pixel.Y / 2);
    }

    /// <summary>
    /// Pins the sector pass at <paramref name="texel"/> to the C# copy: each slice's mask bit for bit, but for a
    /// sector next to a boundary within <see cref="TieTolerance"/> of a tie, and the near estimate within the half
    /// floats of term A and the share of those flipped sectors.
    /// </summary>
    private void ShouldMatch(
        SectorPassOutput gpu,
        (int X, int Y) texel,
        NearTexel expected,
        double brightest,
        Vector3 point
    )
    {
        double totalWeight = AmbientEstimate.TotalWeight(expected.Slices);
        double flippedShare = 0;
        for (int slice = 0; slice < expected.Slices.Length; slice++)
        {
            uint mask = gpu.Masks[(texel.Y * gpu.HalfWidth + texel.X) * expected.Slices.Length + slice];
            uint flips = mask ^ expected.Slices[slice].Blocked.Bits;
            output.WriteLine($"{point}, slice {slice}: GPU {mask:B32}, C# {expected.Slices[slice].Blocked.Bits:B32}");
            (flips & ~expected.Ties[slice].Bits)
                .Should()
                .Be(
                    0u,
                    "slice {0} at {1} blocks the C# copy's sectors, but where a boundary lies at a tie",
                    slice,
                    point
                );
            flippedShare +=
                BitOperations.PopCount(flips)
                * expected.Slices[slice].Sectors.Weight
                / (SliceSectors.Count * totalWeight);
        }

        int i = (texel.Y * gpu.HalfWidth + texel.X) * 4;
        var bounce = new Double3(gpu.Term[i], gpu.Term[i + 1], gpu.Term[i + 2]);
        AmbientEstimate estimate = expected.Estimate;
        output.WriteLine(
            $"{point}: GPU ({bounce}, {gpu.Term[i + 3]:0.000000}), C# ({estimate.Bounce}, {estimate.Occlusion:0.000000})"
        );
        ((double)gpu.Term[i + 3])
            .Should()
            .BeApproximately(estimate.Occlusion, flippedShare + HalfFloatError(estimate.Occlusion), "V at {0}", point);
        foreach (
            (double actual, double wanted) in (ReadOnlySpan<(double Actual, double Wanted)>)
                [(bounce.X, estimate.Bounce.X), (bounce.Y, estimate.Bounce.Y), (bounce.Z, estimate.Bounce.Z)]
        )
            actual
                .Should()
                .BeApproximately(
                    wanted,
                    (flippedShare + FloatError) * brightest + HalfFloatError(wanted),
                    "the bounce at {0}: each blocker gives its light to the sectors that it newly blocks if it faces",
                    point
                );
    }

    /// <summary>The rounding of a value of about <paramref name="value"/> into term A's half floats, and of the GPU's single floats.</summary>
    private static double HalfFloatError(double value) => Math.Abs(value) * Math.Pow(2, -11) + FloatError;

    /// <summary>The corner's lights: the sun, which lights the floor and both walls, and a dim sky and ground of distinct colors.</summary>
    private static RenderSnapshot LitCorner()
    {
        RenderSnapshot lit = Gpu.Snapshot(SunDirection, sunIntensity: 1);
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
            Metallic: smoothMetal ? 1 : 0,
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
        return new(Bounce: new(estimate[i], estimate[i + 1], estimate[i + 2]), Occlusion: estimate[i + 3]);
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
        var material = new MaterialSample(
            BaseColor: new(desc.BaseColor.X, desc.BaseColor.Y, desc.BaseColor.Z),
            Metallic: desc.Metallic,
            Roughness: desc.Roughness,
            Reflectance: desc.Reflectance
        );
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

    /// <summary>The sector pass's term A and sector masks, as the renderer read them back, for a half image <paramref name="HalfWidth"/> texels wide.</summary>
    private sealed record SectorPassOutput(float[] Term, uint[] Masks, int HalfWidth);

    /// <summary>
    /// The C# copy's near slices of a texel, the sectors of each slice below the texel's own surface, and in each slice
    /// the sectors next to a boundary within <see cref="TieTolerance"/> of a tie, which the GPU's single floats may move
    /// by one sector.
    /// </summary>
    private sealed record NearTexel(NearSlice[] Slices, SectorMask[] OwnSurface, SectorMask[] Ties)
    {
        public AmbientEstimate Estimate => AmbientEstimate.OfSlices(Slices);
    }

    /// <summary>
    /// The images of the corner that the sector pass reads, as the renderer read them back: the view depth of each
    /// half-resolution texel and the direct image. The corner's three planes give the normals.
    /// </summary>
    private sealed record CornerImages(float[] ViewDepth, (int Width, int Height) HalfSize, float[] Direct)
    {
        /// <summary>The camera's unit axes in the world: XeGTAO's view space has x right, y up and z forward.</summary>
        private static readonly Vector3 Right = Vector3.Transform(Vector3.UnitX, CornerView);

        private static readonly Vector3 Up = Vector3.Transform(Vector3.UnitY, CornerView);
        private static readonly Vector3 Forward = Vector3.Transform(-Vector3.UnitZ, CornerView);

        /// <summary>
        /// The near slices of <paramref name="texel"/> by the C# copy: the steps of each slice of GTAO, then
        /// <see cref="SectorSearch.Near"/> with each step's view position, geometric normal and direct light. With
        /// <paramref name="everyBlockerFaces"/>, each step's normal faces the center, as if no blocker faced away.
        /// </summary>
        public NearTexel Near((int X, int Y) texel, VisibilityBitmaskLight setting, bool everyBlockerFaces)
        {
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
                List<NearStep> steps = Steps(
                    texel: texel,
                    slice: slice,
                    sliceAngle: angle,
                    setting: setting,
                    stepNoise: stepNoise,
                    radiusInTexels: radiusInTexels,
                    facing: everyBlockerFaces ? center : null
                );
                slices[slice] = SectorSearch.Near(
                    sectors: sectors,
                    center: center,
                    geometricNormal: geometricNormal,
                    radius: setting.RadiusMeters,
                    thicknessShare: CellBounds.ThicknessShare,
                    steps: [.. steps]
                );
                ties[slice] = Ties(
                    sectors: sectors,
                    center: center,
                    geometricNormal: geometricNormal,
                    radius: setting.RadiusMeters,
                    steps: steps
                );
            }

            return new(Slices: slices, OwnSurface: ownSurface, Ties: ties);
        }

        /// <summary>
        /// GTAO's steps of one slice of <paramref name="texel"/>, nearest first and plus side first, with each normal
        /// toward <paramref name="facing"/> if one is given.
        /// </summary>
        private List<NearStep> Steps(
            (int X, int Y) texel,
            int slice,
            double sliceAngle,
            VisibilityBitmaskLight setting,
            float stepNoise,
            double radiusInTexels,
            Double3? facing
        )
        {
            var steps = new List<NearStep>();
            for (int step = 0; HorizonSearch.NearRangeHoldsSteps(radiusInTexels) && step < setting.StepsPerSide; step++)
            {
                double fraction = HorizonSearch.StepFraction(
                    step: step,
                    slice: slice,
                    stepsPerSide: setting.StepsPerSide,
                    stepNoise: stepNoise,
                    radiusInTexels: radiusInTexels
                );
                (double x, double y) exact = HorizonSearch.ExactStepOffset(
                    stepFraction: fraction,
                    sliceAngle: sliceAngle,
                    radiusInTexels: radiusInTexels,
                    halfSize: HalfSize
                );
                (int x, int y) snapped = HorizonSearch.StepOffset(
                    stepFraction: fraction,
                    sliceAngle: sliceAngle,
                    radiusInTexels: radiusInTexels,
                    halfSize: HalfSize
                );
                AddStep(
                    steps: steps,
                    texel: (X: texel.X + snapped.x, Y: texel.Y + snapped.y),
                    exact: (X: texel.X + exact.x, Y: texel.Y + exact.y),
                    side: SliceSide.Plus,
                    facing: facing
                );
                AddStep(
                    steps: steps,
                    texel: (X: texel.X - snapped.x, Y: texel.Y - snapped.y),
                    exact: (X: texel.X - exact.x, Y: texel.Y - exact.y),
                    side: SliceSide.Minus,
                    facing: facing
                );
            }
            return steps;
        }

        /// <summary>
        /// The sectors next to a boundary that lies within <see cref="TieTolerance"/> of a tie: an edge of the own
        /// surface's plane in the slice, or an end of a step within the radius.
        /// </summary>
        private static SectorMask Ties(
            SliceSectors sectors,
            Double3 center,
            Double3 geometricNormal,
            double radius,
            List<NearStep> steps
        )
        {
            double planeAngle = Math.Atan2(
                Double3.Dot(geometricNormal, sectors.Slice.TowardPlusSide),
                Double3.Dot(geometricNormal, sectors.Slice.ToEye)
            );
            SectorMask ties = TiesAt(sectors, planeAngle - Math.PI / 2, planeAngle + Math.PI / 2);
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

        private static double TanHalfX => Math.Tan(Gpu.Fov / 2) * Width / Height;

        /// <summary>
        /// Adds the step from <paramref name="texel"/> at its exact place <paramref name="exact"/>, in half-resolution
        /// texels, unless the texel lies outside the image or on sky, or its surface does not reach the place, with its
        /// normal toward <paramref name="facing"/> if one is given.
        /// </summary>
        private void AddStep(
            List<NearStep> steps,
            (int X, int Y) texel,
            (double X, double Y) exact,
            SliceSide side,
            Double3? facing
        )
        {
            bool inside = texel.X >= 0 && texel.Y >= 0 && texel.X < HalfSize.Width && texel.Y < HalfSize.Height;
            if (!inside || DepthAt(texel) >= HorizonSearch.SkyDepth)
                return;
            Double3 texelPosition = ViewPosition(texel, DepthAt(texel));
            Double3 texelNormal = NormalAt(texelPosition);
            if (
                SectorSearch.SurfacePoint(
                    exactRay: ViewPosition(exact, 1),
                    texelPosition: texelPosition,
                    texelNormal: texelNormal
                )
                is not { } position
            )
                return;
            int pixel = (2 * texel.Y * Width + 2 * texel.X) * 4;
            steps.Add(
                new(
                    Position: position,
                    GeometricNormal: facing is { } center ? (center - position).Normalized() : texelNormal,
                    DirectLight: new(Direct[pixel], Direct[pixel + 1], Direct[pixel + 2]),
                    Side: side
                )
            );
        }

        private double DepthAt((int X, int Y) texel) => ViewDepth[texel.Y * HalfSize.Width + texel.X];

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

        private static Double3 ToView(Vector3 world) =>
            new(Vector3.Dot(world, Right), Vector3.Dot(world, Up), Vector3.Dot(world, Forward));

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
}
