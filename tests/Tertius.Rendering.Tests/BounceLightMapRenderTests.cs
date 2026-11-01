using System.Numerics;
using static Tertius.Rendering.Tests.OcclusionScenes;

namespace Tertius.Rendering.Tests;

/// <summary>
/// The bounce light under the environment map on the GPU (docs/plans/screen-space-illumination.md, decision 41): each
/// direction around a floor point is blocked nearby, and brings the light of its blocker, or open, and brings the map's
/// light from that direction, so a wall that the map also holds lights the point once, and the open directions bring
/// the light of the part of the map that they face. The scenes have no sun and no hemisphere light: only the map, which
/// the faces draw from the camera, lights the floor's ambient light. A wall stands along the left of the view, so high
/// and long that from every floor point it fills the half of the sky toward it. At 640 × 360.
/// </summary>
[Collection(GpuCollection.Name)]
public sealed class BounceLightMapRenderTests(ITestOutputHelper output)
{
    /// <summary>Where the near wall's face stands, facing +X, 1 m left of the eye.</summary>
    private const float WallX = -1;

    /// <summary>
    /// The largest share by which a floor point within the radius of the wall may differ from one beyond it. See
    /// <see cref="AFloorPointBesideAGlowingWallThatTheMapAlsoHoldsGetsTheWallsLightOnce"/> for where it comes from.
    /// </summary>
    private const double OnceTolerance = 0.03;

    /// <summary>A small map: no test here depends on the face size.</summary>
    private static readonly SceneEnvironmentMap SmallMap = SceneEnvironmentMap.Default with { FaceSize = 32 };

    /// <summary>The camera: turned down 30° from looking along −Z, so the floor beside the wall and away from it shows.</summary>
    private static readonly Quaternion View = Gpu.PitchDown(MathF.PI / 6);

    /// <summary>Floor points 0.15 m from the wall, well within the radius of 1 m, where the near steps find the wall.</summary>
    private static readonly Vector3[] BesideTheWall =
    [
        new(WallX + 0.15f, FloorHeight, -3.5f),
        new(WallX + 0.15f, FloorHeight, -4f),
        new(WallX + 0.15f, FloorHeight, -4.5f),
    ];

    /// <summary>Floor points 2.5 m from the wall, beyond the radius, where nothing blocks a direction nearby.</summary>
    private static readonly Vector3[] AwayFromTheWall =
    [
        new(WallX + 2.5f, FloorHeight, -3.5f),
        new(WallX + 2.5f, FloorHeight, -4f),
        new(WallX + 2.5f, FloorHeight, -4.5f),
    ];

    /// <summary>
    /// The wall glows, and the rest of the map is black. From every floor point the wall fills the half of the sky
    /// toward it, so every floor point gets half the wall's radiance L, beside the wall as away from it. Beside it, the
    /// near steps block a share b of the hemisphere with the wall's own light, and before decision 41 the open share
    /// 1 − b took the map's mean, L / 2, so the point got (b + (1 − b) / 2) / (1 / 2) = 1 + b times the light away from
    /// the wall: 1.21 on this Mac, so b ≈ 0.21 after the blurs. Read in its own directions, the open share takes the
    /// map's light where the wall lies beyond the radius and nothing elsewhere, and what remains is the blur of each read
    /// and the quadrature of the parts. A read puts at most 5 % of its lobe's weight beyond an end of its part
    /// (GgxLobe.RoughnessForWidth), and a part that reaches the horizon, below which the map holds the black floor, so
    /// misses at most 5 % of its light: 2 × 0.05 × 0.21 ≈ 0.02 of the light away from the wall. Sharp reads, at level 0,
    /// left 0.002 for the quadrature, so 0.03 holds both with a margin.
    /// </summary>
    [Fact]
    public void AFloorPointBesideAGlowingWallThatTheMapAlsoHoldsGetsTheWallsLightOnce()
    {
        float[] hdr = Rendered(
            VisibilityBitmaskLight.Default,
            (renderer, snapshot) =>
                AddWall(
                    snapshot: snapshot,
                    renderer: renderer,
                    material: BrdfTesting.Glow(renderer, Vector3.One),
                    x: WallX,
                    faceToward: 1
                )
        );

        double beside = MeanLight(hdr, BesideTheWall);
        double away = MeanLight(hdr, AwayFromTheWall);
        output.WriteLine($"floor beside the glowing wall {beside:0.0000}, away from it {away:0.0000}");
        beside
            .Should()
            .BeApproximately(
                away,
                OnceTolerance * away,
                "the wall fills half the sky of both points, and the light of each direction counts once"
            );
    }

    /// <summary>
    /// The near wall is black, and a glowing wall 41 m away fills the other half of the sky up to 84° above the floor.
    /// Beside the black wall, the near steps block a share b of the hemisphere with no light, and the open share faces
    /// the glowing wall alone, so the point gets the light of a point away from the wall, which sees the same two halves.
    /// Before decision 41, the open share took the map's mean over the hemisphere, half the glowing wall's light, and the
    /// point got 1 − b times the light away from the wall: 0.79 on this Mac. The tolerance is the one of
    /// <see cref="AFloorPointBesideAGlowingWallThatTheMapAlsoHoldsGetsTheWallsLightOnce"/>, with the sides swapped: a
    /// read of the black wall takes in at most 5 % of the glowing wall's light beyond its part.
    /// </summary>
    [Fact]
    public void AFloorPointBesideADarkWallGetsTheLightOfTheBrightPartOfTheMapThatItsOpenDirectionsFace()
    {
        float[] hdr = Rendered(VisibilityBitmaskLight.Default, AddDarkWallAndFarGlow);

        double beside = MeanLight(hdr, BesideTheWall);
        double away = MeanLight(hdr, AwayFromTheWall);
        output.WriteLine($"floor beside the dark wall {beside:0.0000}, away from it {away:0.0000}");
        beside
            .Should()
            .BeApproximately(
                away,
                OnceTolerance * away,
                "the open directions of both points face the glowing wall, not the mean of the map"
            );
    }

    /// <summary>
    /// A floor point beyond the radius of every blocker has every sector open: the estimate holds no bounce, and its
    /// open share S is exactly 1, because the map's light along its blocked sectors is exactly 0 and the images store
    /// 1 − S, whose zeros the blurs and the upsample keep. So its diffuse light is the map's irradiance at its normal,
    /// exactly as with the screen-space light off. The passes' half floats can store V of an open texel as 1 − 2⁻¹¹, the
    /// largest half below 1, where a device rounds a store toward zero, as this Mac's does: the blurs' sums of ones come
    /// to just below 1. Only the specular occlusion reads V, and it moves the pixel by less than one step of the HDR
    /// image's half floats, 2⁻¹¹ of its value.
    /// </summary>
    [Fact]
    public void AFloorPointWithEverySectorOpenTakesTheMapsIrradianceAsWithTheScreenSpaceLightOff()
    {
        (float[] on, float[] estimate, float[] openShare) = RenderedWithEstimate(
            VisibilityBitmaskLight.Default,
            AddDarkWallAndFarGlow
        );
        float[] off = Rendered(new NoScreenSpaceLight(), AddDarkWallAndFarGlow);

        foreach (Vector3 point in AwayFromTheWall)
        {
            (int X, int Y) pixel = Pixel(point);
            Vector3 withLight = ReflectionTesting.At(on, pixel);
            Vector3 without = ReflectionTesting.At(off, pixel);
            int i = 4 * (pixel.Y * Width + pixel.X);
            output.WriteLine(
                $"{point}: with the screen-space light {withLight}, without {without}, estimate "
                    + $"({estimate[i]}, {estimate[i + 1]}, {estimate[i + 2]}, {estimate[i + 3]})"
            );
            estimate[i..(i + 3)].Should().Equal([0f, 0f, 0f], "every sector of {0} is open", point);
            int j = 3 * (pixel.Y * Width + pixel.X);
            openShare[j..(j + 3)]
                .Should()
                .Equal([1f, 1f, 1f], "an open point keeps exactly the map's irradiance at {0}", point);
            estimate[i + 3]
                .Should()
                .BeGreaterThanOrEqualTo(1 - MathF.Pow(2, -11), "every sector of {0} is open", point);
            BrdfTesting
                .LargestChannelDifference(withLight, without)
                .Should()
                .BeLessThan(
                    MathF.Pow(2, -11) * without.X,
                    "an open point takes the map's irradiance at S = 1, and only the specular occlusion reads V"
                );
        }
    }

    /// <summary>
    /// The crease of a dimly glowing near wall and the floor, under the map of the far glowing wall, which glows twice
    /// as bright, and of a small bright lamp high up and ahead, which the open directions of the crease face. The map
    /// holds the near wall too, so the crease's blocked directions hold much of the map's light, and S is well below 1.
    /// The crease's light at the default 3 slices lies within 10 % of a reference at 16 slices, the most that the
    /// setting allows, in the mean over ten floor points along the crease, 5 cm from the wall: the slices' noise averages
    /// out along the crease, as the denoise averages it, and the open share S is a ratio of reads of one store, so no
    /// read's error takes the crease past the open directions' light.
    /// </summary>
    [Fact]
    public void ACreaseBesideASmallBrightLightInTheMapGetsTheMapsLightOverItsOpenDirectionsWithinATenthOfAManySliceReference()
    {
        Vector3[] crease =
        [
            .. Enumerable.Range(0, 10).Select(k => new Vector3(WallX + 0.05f, FloorHeight, -3f - 0.25f * k)),
        ];

        (float[] hdr, _, float[] openShare) = RenderedWithEstimate(VisibilityBitmaskLight.Default, AddDimWallFarGlowAndLamp);
        double light = MeanLight(hdr, crease);
        double reference = MeanLight(
            Rendered(
                VisibilityBitmaskLight.Default with { Slices = ScreenSpaceLightSetting.MaxSlices },
                AddDimWallFarGlowAndLamp
            ),
            crease
        );
        double share = crease.Average(point =>
        {
            (int X, int Y) pixel = Pixel(point);
            int j = 3 * (pixel.Y * Width + pixel.X);
            return (openShare[j] + openShare[j + 1] + openShare[j + 2]) / 3.0;
        });
        output.WriteLine($"the crease at 3 slices {light:0.0000}, at 16 slices {reference:0.0000}, S {share:0.0000}");

        share.Should().BeLessThan(0.85, "the test needs a crease whose blocked directions hold much of the map's light");
        light
            .Should()
            .BeApproximately(
                reference,
                0.1 * reference,
                "the crease takes the map's light over its open directions, as many slices find it"
            );
    }

    /// <summary>
    /// A near wall that glows at half the far wall's radiance, the glowing far wall 41 m right of the eye, and a lamp
    /// 0.5 m wide, 20 times as bright as the far wall, high up and ahead.
    /// </summary>
    private static void AddDimWallFarGlowAndLamp(VulkanRenderer renderer, RenderSnapshot snapshot)
    {
        AddWall(
            snapshot: snapshot,
            renderer: renderer,
            material: BrdfTesting.Glow(renderer, new Vector3(0.5f, 0.5f, 0.5f)),
            x: WallX,
            faceToward: 1
        );
        AddWall(
            snapshot: snapshot,
            renderer: renderer,
            material: BrdfTesting.Glow(renderer, Vector3.One),
            x: 40,
            faceToward: -1
        );
        AddSlab(
            snapshot: snapshot,
            box: UnitBox(renderer),
            material: BrdfTesting.Glow(renderer, new Vector3(20, 20, 20)),
            center: new(4, FloorHeight + 5, -12),
            size: new(0.5f, 0.5f, 0.5f)
        );
    }

    /// <summary>The black near wall, and the glowing far wall 41 m right of the eye, facing −X.</summary>
    private static void AddDarkWallAndFarGlow(VulkanRenderer renderer, RenderSnapshot snapshot)
    {
        AddWall(
            snapshot: snapshot,
            renderer: renderer,
            material: renderer.CreateMaterial(MaterialDesc.Color(new(x: 0, y: 0, z: 0, w: 1), roughness: 1)),
            x: WallX,
            faceToward: 1
        );
        AddWall(
            snapshot: snapshot,
            renderer: renderer,
            material: BrdfTesting.Glow(renderer, Vector3.One),
            x: 40,
            faceToward: -1
        );
    }

    /// <summary>
    /// The HDR image of a white floor with no specular lobe under the map, with the walls that <paramref name="addWalls"/>
    /// adds, rendered by a renderer of <paramref name="setting"/>.
    /// </summary>
    private static float[] Rendered(ScreenSpaceLightSetting setting, Action<VulkanRenderer, RenderSnapshot> addWalls) =>
        RenderedWithEstimate(setting, addWalls).Hdr;

    /// <summary>
    /// The HDR image of <see cref="Rendered"/>, and the estimate target of the same frame: its bounce and V in RGBA, and
    /// its open share S in RGB.
    /// </summary>
    private static (float[] Hdr, float[] Estimate, float[] OpenShare) RenderedWithEstimate(
        ScreenSpaceLightSetting setting,
        Action<VulkanRenderer, RenderSnapshot> addWalls
    )
    {
        VulkanRenderer renderer = Gpu.Headless(screenSpaceLight: setting, environmentMap: SmallMap);
        try
        {
            RenderSnapshot snapshot = Gpu.Snapshot(Vector3.UnitY, starIrradiance: 0);
            snapshot.Ambient.Intensity = 0;
            Look(snapshot, View);
            MaterialId floor = renderer.CreateMaterial(
                new(BaseColor: new(x: 1, y: 1, z: 1, w: 1), Roughness: 1, Reflectance: 0)
            );
            AddSlab(
                snapshot: snapshot,
                box: UnitBox(renderer),
                material: floor,
                center: new(0, FloorHeight - 0.5f, 0),
                size: new(800, 1, 800)
            );
            addWalls(renderer, snapshot);
            // The faces draw the map in the first frame, and every reader reads it after them.
            renderer.Render(snapshot, 1f);
            return (
                Hdr: renderer.ReadHdr(),
                Estimate: renderer.ReadEstimate(),
                OpenShare: renderer.ReadEstimateOpenShare()
            );
        }
        finally
        {
            Gpu.DisposeAndAssertClean(renderer);
        }
    }

    /// <summary>
    /// Adds a wall 400 m high and 800 m long whose face stands at <paramref name="x"/>, facing +X for
    /// <paramref name="faceToward"/> 1 and −X for −1.
    /// </summary>
    private static void AddWall(
        RenderSnapshot snapshot,
        VulkanRenderer renderer,
        MaterialId material,
        float x,
        float faceToward
    ) =>
        AddSlab(
            snapshot: snapshot,
            box: UnitBox(renderer),
            material: material,
            center: new(x - 0.5f * faceToward, FloorHeight + 200, 0),
            size: new(1, 400, 800)
        );

    /// <summary>The mean over <paramref name="points"/> of the mean channel of their pixels in <paramref name="hdr"/>.</summary>
    private static double MeanLight(float[] hdr, Vector3[] points) =>
        points.Average(point =>
        {
            Vector3 color = ReflectionTesting.At(hdr, Pixel(point));
            return (color.X + color.Y + color.Z) / 3.0;
        });

    private static (int X, int Y) Pixel(Vector3 point)
    {
        Vector2 pixel = PixelOf(point, View);
        return ((int)pixel.X, (int)pixel.Y);
    }
}
