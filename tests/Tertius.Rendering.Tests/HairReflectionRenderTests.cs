using System.Numerics;
using Tertius.Testing;
using static Tertius.Rendering.Tests.ReflectionTesting;

namespace Tertius.Rendering.Tests;

/// <summary>
/// The screen-space reflections of hair on the GPU (docs/plans/skin-and-hair.md, "The reflections of hair"): a hair
/// pixel whose dominant direction points below its card, as a normal map that tilts the strand off the card can make it,
/// traces no ray and keeps the reflection that the ambient pass gave it. The card is a hair floor, the reflection tests'
/// floor of 60 m by 60 m 1.5 m below the eye, whose strands run along +v, toward the eye. A wall that glows 3 stands on
/// it 9.5 m ahead. The floor's roughness of 26 / 255 makes its dominant direction lie within 1° of the eye's direction
/// mirrored along the strand.
/// </summary>
[Collection(GpuCollection.Name)]
public sealed class HairReflectionRenderTests
{
    /// <summary>A value that the specular target stores exactly, about 0.1.</summary>
    private const float Roughness = 26 / 255f;

    /// <summary>A value that the specular target stores exactly, about 0.5.</summary>
    private const float Reflectance = 128 / 255f;

    /// <summary>The radiance of the wall.</summary>
    private const float WallGlow = 3;

    /// <summary>The ambient light's gray, from the sky and the ground alike, while the environment map is off.</summary>
    private const float Gray = 0.2f;

    /// <summary>
    /// The cosine with the card's normal below which a pixel's ray surely points below its card: the rounding of the
    /// 16-bit normals in the G-buffer moves the C# copy's cosine by far less.
    /// </summary>
    private const double SurelyBelow = -0.01;

    /// <summary>Both traces: the fallback holds at either resolution.</summary>
    public static TheoryData<int, TraceResolution> TiltsAndResolutions =>
        new()
        {
            { 10, TraceResolution.Full },
            { 10, TraceResolution.Half },
            { 45, TraceResolution.Full },
            { 45, TraceResolution.Half },
        };

    [Theory]
    [MemberData(nameof(TiltsAndResolutions))]
    public void AHairPixelWhoseRayPointsBelowItsCardKeepsTheEnvironmentMapsReflection(
        int tiltDegrees,
        TraceResolution resolution
    )
    {
        // A tilt of 10° puts the rays of the near floor above the card and those of the far floor below it, with the
        // surface fade's range between them. A tilt of 45° puts every ray of the floor below its card.
        Rendered with = Render(reflections: ReflectionsAt(resolution), tiltDegrees: tiltDegrees, environmentMap: true);
        Rendered without = Render(reflections: new NoReflections(), tiltDegrees: tiltDegrees, environmentMap: true);

        (int X, int Y)[] below = [.. FloorPixels(with).Where(pixel => CosineWithCard(with, pixel) < SurelyBelow)];
        below.Length.Should().BeGreaterThan(20_000, "the tilt of {0}° puts the far floor's rays below the card", tiltDegrees);
        foreach ((int X, int Y) pixel in below)
        {
            Vector3 kept = At(without.Hdr, pixel);
            kept.Y.Should().BeGreaterThan(0, "the ambient pass lights the hair at {0} from the map", pixel);
            BrdfTesting
                .LargestChannelDifference(At(with.Hdr, pixel), kept)
                .Should()
                .BeLessThanOrEqualTo(
                    kept.Y / 1024,
                    "{0}, whose ray points below its card, traces nothing and keeps the map's reflection: {1}",
                    pixel,
                    At(with.Hdr, pixel)
                );
        }
    }

    [Theory]
    [InlineData(TraceResolution.Full)]
    [InlineData(TraceResolution.Half)]
    public void AHairPixelWhoseRayGrazesItsCardTakesTheScreensReflectionOnlyInTheShareOfTheSurfaceFade(
        TraceResolution resolution
    )
    {
        // The tilt of 10° puts a band of the floor's rays within a cosine of SurfaceFadeCosine above the card. Their
        // delta is f · (W · C − a · k · T_s), and C and k · T_s each lie between 0 and the wall's glow, so the delta of a
        // pixel of fade f is at most f · W times the glow: the map's reflection keeps the share 1 − f.
        Rendered with = Render(reflections: ReflectionsAt(resolution), tiltDegrees: 10, environmentMap: true);
        Rendered without = Render(reflections: new NoReflections(), tiltDegrees: 10, environmentMap: true);

        (int X, int Y)[] grazing =
        [
            .. FloorPixels(with)
                .Where(pixel => CosineWithCard(with, pixel) is > 0.002 and < ScreenRay.SurfaceFadeCosine - 0.002),
        ];
        grazing.Length.Should().BeGreaterThan(1_000, "the far floor's rays cross the card's plane");
        foreach ((int X, int Y) pixel in grazing)
        {
            float fade = (float)(CosineWithCard(with, pixel) / ScreenRay.SurfaceFadeCosine);
            float most = (fade + 0.05f) * SpecularWeight(with, pixel).Y * WallGlow;
            BrdfTesting
                .LargestChannelDifference(At(with.Hdr, pixel), At(without.Hdr, pixel))
                .Should()
                .BeLessThanOrEqualTo(
                    most,
                    "{0} of fade {1} takes that share of the screen's reflection: {2} with, {3} without",
                    pixel,
                    fade,
                    At(with.Hdr, pixel),
                    At(without.Hdr, pixel)
                );
        }
    }

    [Theory]
    [InlineData(TraceResolution.Full)]
    [InlineData(TraceResolution.Half)]
    public void AHairCardWhoseStrandsLieInItsPlaneReflectsTheWallThroughItsRays(TraceResolution resolution)
    {
        Rendered with = Render(reflections: ReflectionsAt(resolution), tiltDegrees: 0, environmentMap: false);
        Rendered without = Render(reflections: new NoReflections(), tiltDegrees: 0, environmentMap: false);

        FloorPixels(with)
            .Min(pixel => CosineWithCard(with, pixel))
            .Should()
            .BeGreaterThan(ScreenRay.SurfaceFadeCosine, "a strand in its card mirrors the eye above the card");
        foreach (float y in (float[])[0, 1, 2])
        {
            var onWall = new Vector3(0, y, -9.5f);
            (int X, int Y) pixel = MirrorPixel(onWall);
            Vector3 weight = SpecularWeight(with, pixel);
            // The hit replaces the ambient specular term W · gray with the wall's light W · C.
            Vector3 expected = At(without.Hdr, pixel) + weight * (At(without.Hdr, PixelOf(onWall)) - new Vector3(Gray));
            BrdfTesting
                .LargestChannelDifference(At(with.Hdr, pixel), expected)
                .Should()
                .BeLessThanOrEqualTo(0.02f * expected.Y, "{0} reflects the wall: {1}", pixel, At(with.Hdr, pixel));
        }
    }

    /// <summary>The HDR image, the normal target and the distance target of one frame, and its camera.</summary>
    private readonly record struct Rendered(float[] Hdr, float[] Normals, float[] Distances, CameraView Camera);

    /// <summary>
    /// Renders the hair floor, whose normal map leans the shading normal <paramref name="tiltDegrees"/> away from the eye
    /// and so tilts the strand as far up off the card toward the eye, and the wall, at 640 × 360 and 0 stops. With
    /// <paramref name="environmentMap"/>, under a map of 32 texels that draws all six faces in the frame; without, under
    /// the ambient light of <see cref="Gray"/>.
    /// </summary>
    private static Rendered Render(ReflectionSetting reflections, int tiltDegrees, bool environmentMap)
    {
        VulkanRenderer renderer = Gpu.Headless(
            exposure: new FixedExposure(0),
            reflections: reflections,
            environmentMap: environmentMap ? new SceneEnvironmentMap(FaceSize: 32, FacesPerFrame: 6) : null
        );
        try
        {
            RenderSnapshot snapshot = ReflectionTesting.Gray(Gray);
            snapshot.Instances.Add(HairFloor(renderer, tiltDegrees));
            snapshot.Instances.Add(
                Block(
                    box: renderer.CreateMesh(Primitives.Box(new(0.5f))),
                    material: Glow(renderer, new(WallGlow)),
                    center: new(0, 3, -10),
                    size: new(40, 9, 1)
                )
            );
            renderer.Render(snapshot, 1f);
            return new(
                Hdr: renderer.ReadHdr(),
                Normals: renderer.ReadNormals(),
                Distances: renderer.ReadDistance(),
                Camera: renderer.LastScene.Camera
            );
        }
        finally
        {
            Gpu.DisposeAndAssertClean(renderer);
        }
    }

    /// <summary>
    /// The floor of <see cref="ReflectionTesting.Floor"/> as gray hair of <see cref="Roughness"/>, with a normal map of
    /// one texel that leans the normal toward the bitangent, −Z, by <paramref name="tiltDegrees"/>.
    /// </summary>
    private static RenderInstance HairFloor(VulkanRenderer renderer, int tiltDegrees)
    {
        float tilt = tiltDegrees * MathF.PI / 180;
        static byte Code(float value) => (byte)MathF.Round((value + 1) / 2 * 255);
        TextureId map = renderer.CreateTexture(
            new(Width: 1, Height: 1, Rgba: [128, Code(MathF.Sin(tilt)), Code(MathF.Cos(tilt)), 255], Srgb: false)
        );
        var hair = new MaterialDesc(
            BaseColor: new(x: 0.5f, y: 0.5f, z: 0.5f, w: 1),
            Roughness: Roughness,
            Reflectance: Reflectance,
            Model: new HairShading(StrandDirection.PlusV)
        ) with
        {
            NormalMap = map,
        };
        return Gpu.Instance(
            mesh: renderer.CreateMesh(Primitives.Plane(1)),
            material: renderer.CreateMaterial(hair),
            position: new(0, FloorHeight, -20),
            scale: new(30, 1, 30),
            radius: 43,
            chainDepth: TestChains.CamerasFrame
        );
    }

    /// <summary>The pixels that see the floor: a surface whose geometric normal is +Y.</summary>
    private static IEnumerable<(int X, int Y)> FloorPixels(Rendered frame) =>
        from y in Enumerable.Range(0, Height)
        from x in Enumerable.Range(0, Width)
        let index = y * Width + x
        where frame.Distances[index] > 0 && Decoded(frame.Normals, index, channel: 2).Y > 0.999
        select (x, y);

    /// <summary>
    /// The cosine of the pixel's dominant direction with its card's normal: the C# copy of the direction that
    /// reflectingPixel in reflection_pixel.glsl gives, from the fiber normal that the G-buffer holds.
    /// </summary>
    private static double CosineWithCard(Rendered frame, (int X, int Y) pixel)
    {
        int index = pixel.Y * Width + pixel.X;
        Double3 fiberNormal = Decoded(frame.Normals, index, channel: 0);
        Double3 toView = ToView(frame, pixel);
        Double3 mirror = 2 * Double3.Dot(toView, fiberNormal) * fiberNormal - toView;
        Double3 direction = new GgxLobe(Roughness).DominantDirection(normal: fiberNormal, mirror: mirror);
        return Double3.Dot(direction, Decoded(frame.Normals, index, channel: 2));
    }

    /// <summary>W at a floor pixel: the compensated specular albedo of the hair toward the eye, about its fiber normal.</summary>
    private static Vector3 SpecularWeight(Rendered frame, (int X, int Y) pixel)
    {
        var sample = new MaterialSample(
            BaseColor: new(0.5, 0.5, 0.5),
            Metallic: 0,
            Roughness: Roughness,
            Reflectance: Reflectance
        );
        return sample
            .CompensatedSpecularAlbedo(
                normal: Decoded(frame.Normals, pixel.Y * Width + pixel.X, channel: 0),
                toView: ToView(frame, pixel),
                tables: BrdfTables.Shared
            )
            .ToVector3();
    }

    private static Double3 ToView(Rendered frame, (int X, int Y) pixel) =>
        -Vector3.Normalize(frame.Camera.ViewRayThrough(new(pixel.X + 0.5f, pixel.Y + 0.5f)));

    /// <summary>
    /// The unit normal that the normal target holds at pixel <paramref name="index"/> from <paramref name="channel"/>: 0
    /// for the shading normal, 2 for the geometric normal. The C# copy of octDecode in octahedral.glsl.
    /// </summary>
    private static Double3 Decoded(float[] normals, int index, int channel)
    {
        var n = new Double3(normals[index * 4 + channel], normals[index * 4 + channel + 1], 0);
        n = n with { Z = 1 - Math.Abs(n.X) - Math.Abs(n.Y) };
        if (n.Z < 0)
            n = new((1 - Math.Abs(n.Y)) * (n.X >= 0 ? 1 : -1), (1 - Math.Abs(n.X)) * (n.Y >= 0 ? 1 : -1), n.Z);
        return n.Normalized();
    }
}
