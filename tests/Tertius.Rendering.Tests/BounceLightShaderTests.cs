using System.Numerics;
using static Tertius.Rendering.Tests.BrdfTesting;
using static Tertius.Rendering.Tests.OcclusionScenes;

namespace Tertius.Rendering.Tests;

/// <summary>
/// The tests that pin the bounce light's shaders to the C# copy (docs/plans/screen-space-illumination.md, "GPU tests of
/// the acceptance criteria"), in the corner under the sun and a dim sky and ground of distinct colors: the sector pass
/// gathers what <see cref="SectorSearch"/> gathers from the same images, and the ambient pass adds what
/// <see cref="MaterialSample.ReflectedHemisphere"/> gives at the estimate that the estimate target holds.
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
            renderer.Graph.Skipped.UnionWith(["occlusion denoise", "occlusion upsample"]);
            renderer.Render(Corner(renderer, Matte(renderer), LitCorner()), 1f);
            (float[] term, int halfWidth, int halfHeight) = renderer.ReadOcclusionTerm();
            var images = new CornerImages(
                ViewDepth: renderer.ReadOcclusionViewDepth().Depths,
                HalfSize: (Width: halfWidth, Height: halfHeight),
                Direct: renderer.ReadDirect()
            );
            // The brightest channel of the direct light, which one sector's light can reach.
            double brightest = images.Direct.Where((_, i) => i % 4 != 3).Max();

            foreach (Vector3 point in SectorPoints)
            {
                Vector2 pixel = PixelOf(point, CornerView);
                (int X, int Y) texel = (X: (int)pixel.X / 2, Y: (int)pixel.Y / 2);
                AmbientEstimate expected = images.NearEstimate(texel, setting);
                int i = (texel.Y * halfWidth + texel.X) * 4;
                var bounce = new Double3(term[i], term[i + 1], term[i + 2]);
                output.WriteLine(
                    $"{point}: GPU ({bounce}, {term[i + 3]:0.0000}), C# ({expected.Bounce}, {expected.Occlusion:0.0000})"
                );
                expected.Bounce.X.Should().BePositive("the sunlit walls bounce their light onto {0}", point);
                ((double)term[i + 3])
                    .Should()
                    .BeApproximately(
                        expected.Occlusion,
                        1.0 / 32 + 1e-3,
                        "one sector of each slice at most at {0}",
                        point
                    );
                (bounce - expected.Bounce)
                    .Length.Should()
                    .BeLessThanOrEqualTo(
                        brightest * Math.Sqrt(3) / 32 + 1e-3,
                        "one sector of each slice at most at {0}",
                        point
                    );
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
        /// The near estimate of <paramref name="texel"/> by the C# copy: the steps of each slice of GTAO, then
        /// <see cref="SectorSearch.Near"/> with each step's view position, geometric normal and direct light.
        /// </summary>
        public AmbientEstimate NearEstimate((int X, int Y) texel, VisibilityBitmaskLight setting)
        {
            double z = DepthAt(texel);
            Double3 center = ViewPosition(texel, z * HorizonSearch.CenterBias);
            Double3 toEye = (-center).Normalized();
            Double3 normal = NormalAt(ViewPosition(texel, z));
            (float sliceNoise, float stepNoise) = OcclusionNoise.At((uint)texel.X, (uint)texel.Y);
            double radiusInTexels = setting.RadiusMeters / HorizonSearch.TexelWidth(z, TanHalfX, Width);
            var slices = new NearSlice[setting.Slices];
            for (int slice = 0; slice < setting.Slices; slice++)
            {
                double angle = HorizonSearch.SliceAngle(slice, setting.Slices, sliceNoise);
                var sectors = new SliceSectors(new HorizonSlice(toEye, normal, angle));
                var steps = new List<NearStep>();
                for (
                    int step = 0;
                    HorizonSearch.NearRangeHoldsSteps(radiusInTexels) && step < setting.StepsPerSide;
                    step++
                )
                {
                    double fraction = HorizonSearch.StepFraction(
                        step: step,
                        slice: slice,
                        stepsPerSide: setting.StepsPerSide,
                        stepNoise: stepNoise,
                        radiusInTexels: radiusInTexels
                    );
                    (int x, int y) = HorizonSearch.StepOffset(
                        stepFraction: fraction,
                        sliceAngle: angle,
                        radiusInTexels: radiusInTexels,
                        halfSize: HalfSize
                    );
                    AddStep(steps, (X: texel.X + x, Y: texel.Y + y), SliceSide.Plus);
                    AddStep(steps, (X: texel.X - x, Y: texel.Y - y), SliceSide.Minus);
                }

                slices[slice] = SectorSearch.Near(
                    sectors: sectors,
                    center: center,
                    radius: setting.RadiusMeters,
                    thicknessShare: ScreenRay.ThicknessShare,
                    steps: [.. steps]
                );
            }

            return AmbientEstimate.OfSlices(slices);
        }

        private static double TanHalfX => Math.Tan(Gpu.Fov / 2) * Width / Height;

        /// <summary>Adds the step at <paramref name="texel"/> unless it lies outside the image or on sky.</summary>
        private void AddStep(List<NearStep> steps, (int X, int Y) texel, SliceSide side)
        {
            bool inside = texel.X >= 0 && texel.Y >= 0 && texel.X < HalfSize.Width && texel.Y < HalfSize.Height;
            if (!inside || DepthAt(texel) >= HorizonSearch.SkyDepth)
                return;
            Double3 position = ViewPosition(texel, DepthAt(texel));
            int pixel = (2 * texel.Y * Width + 2 * texel.X) * 4;
            steps.Add(
                new(
                    Position: position,
                    GeometricNormal: NormalAt(position),
                    DirectLight: new(Direct[pixel], Direct[pixel + 1], Direct[pixel + 2]),
                    Side: side
                )
            );
        }

        private double DepthAt((int X, int Y) texel) => ViewDepth[texel.Y * HalfSize.Width + texel.X];

        /// <summary>The view position at view depth <paramref name="z"/> on the ray through the texel's representative pixel.</summary>
        private static Double3 ViewPosition((int X, int Y) texel, double z)
        {
            double tanHalfY = Math.Tan(Gpu.Fov / 2);
            double screenX = (2 * texel.X + 0.5) / Width;
            double screenY = (2 * texel.Y + 0.5) / Height;
            return new Double3((2 * screenX - 1) * TanHalfX, (1 - 2 * screenY) * tanHalfY, 1) * z;
        }

        /// <summary>The view-space normal of the corner's plane nearest the view position <paramref name="position"/>.</summary>
        private static Double3 NormalAt(Double3 position)
        {
            Vector3 world = (Right * (float)position.X) + (Up * (float)position.Y) + (Forward * (float)position.Z);
            float toFloor = MathF.Abs(world.Y - FloorHeight);
            float toBackWall = MathF.Abs(world.Z - BackWallZ);
            float toSideWall = MathF.Abs(world.X - SideWallX);
            Vector3 normal = Vector3.UnitX;
            if (toFloor <= toBackWall && toFloor <= toSideWall)
                normal = Vector3.UnitY;
            else if (toBackWall <= toSideWall)
                normal = Vector3.UnitZ;
            return new(Vector3.Dot(normal, Right), Vector3.Dot(normal, Up), Vector3.Dot(normal, Forward));
        }
    }
}
