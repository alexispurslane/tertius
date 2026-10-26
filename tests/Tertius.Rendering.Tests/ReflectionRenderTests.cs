using System.Numerics;
using Silk.NET.Vulkan;
using static Tertius.Rendering.Tests.ReflectionTesting;

namespace Tertius.Rendering.Tests;

/// <summary>
/// The screen-space reflections on the GPU (docs/plans/screen-space-reflections.md, "GPU tests of the reflections"): what
/// a floor shows where its rays hit, blur, fade or miss, read as linear HDR values. Unless a test says otherwise, the
/// floor is white metal and the scene is dark, so the floor shows only what the trace adds. The first three tests are
/// the user's (decision 10). One test checks that the trace may read the BRDF tables after their upload, and one that
/// its fallback is the environment map's specular term while the map is on.
/// </summary>
[Collection(GpuCollection.Name)]
public sealed class ReflectionRenderTests
{
    /// <summary>The radiance of the emissive objects that the floors reflect.</summary>
    private const float Bright = 4;

    /// <summary>The middle of the front face of <see cref="BoxAbove"/>, which the floor below it reflects.</summary>
    private static readonly Vector3 BoxFace = new(0, -0.25f, -7.5f);

    /// <summary>The left edge of that face at its middle height.</summary>
    private static readonly Vector3 BoxFaceLeft = new(-3, -0.25f, -7.5f);

    [Fact]
    public void ABrightObjectAboveAMirrorFloorShowsInTheFloor()
    {
        (float[] hdr, CameraView camera) = RenderHdr(Reflections, renderer => BoxAbove(renderer, Mirror()));

        (int X, int Y) pixel = MirrorPixel(BoxFace);
        Vector3 expected = SpecularWeight(Mirror(), camera, pixel) * Bright;
        BrdfTesting
            .LargestChannelDifference(At(hdr, pixel), expected)
            .Should()
            .BeLessThanOrEqualTo(0.02f * expected.Y, "the mirror shows W · L at {0}: {1}", pixel, At(hdr, pixel));
    }

    [Fact]
    public void ARougherFloorBlursTheReflection()
    {
        // About 0.1, 0.2 and 0.3, in steps that the G-buffer's 8 bits hold exactly.
        float[] roughnesses = [26 / 255f, 51 / 255f, 77 / 255f];
        (int X, int Y) center = MirrorPixel(BoxFace);
        int edge = PixelOf(BoxFaceLeft).X;

        float[] widths =
        [
            .. roughnesses.Select(roughness =>
            {
                float[] hdr = RenderHdr(Reflections, renderer => BoxAbove(renderer, Mirror(roughness))).Hdr;
                return RiseWidth(Row(hdr, center.Y), outside: edge - 70, plateau: center.X);
            }),
        ];

        widths.Should().BeInAscendingOrder("a rougher floor reflects through a wider cone");
        widths[0].Should().BeLessThan(widths[2] / 2, "the width grows with the roughness: {0}", widths);
        float diameter = 2 * ConeRadiusPixels(roughnesses[2], floorPixel: (edge, center.Y), hit: BoxFaceLeft);
        widths[2]
            .Should()
            .BeInRange(diameter / 2, diameter * 2, "the blur is the size of the cone, {0} px across", diameter);
    }

    [Fact]
    public void TheReflectionFadesAtTheScreenEdgeWithNoHardEdge()
    {
        // At 1280 × 720 the fade's band is 72 px, so the smoothstep's steepest step is 2.1 % of the peak per pixel.
        const int width = 1280,
            height = 720;
        var barFront = new Vector3(0, -0.6f, -7.8f);
        int row = MirrorPixel(barFront, width, height).Y;
        // Short of the bar's right end, whose reflected silhouette is a hard edge, as a mirror shows it.
        int last = PixelOf(barFront with { X = 2.5f }, width, height).X;
        float[] with = Row(
            RenderHdr(reflections: Reflections, scene: LongBar, view: new(Width: width, Height: height)).Hdr,
            row,
            width
        );
        float[] without = Row(
            RenderHdr(reflections: new NoReflections(), scene: LongBar, view: new(Width: width, Height: height)).Hdr,
            row,
            width
        );

        float peak = with.Take(last).Max();
        peak.Should().BeGreaterThan(Bright / 2, "the bar shows in the mirror inside the band");
        for (int x = 0; x + 1 < last; x++)
            MathF
                .Abs(with[x + 1] - with[x])
                .Should()
                .BeLessThanOrEqualTo(0.03f * peak, "the reflection fades smoothly at pixel {0}", x);
        MathF
            .Abs(with[0] - without[0])
            .Should()
            .BeLessThanOrEqualTo(peak / 255, "the outermost pixel shows the floor without the reflection");
    }

    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    public void AHitReplacesTheAmbientSpecularTermUnderTheSameWeight(float exposureStops)
    {
        const float gray = 0.2f;
        var floor = new MaterialDesc(
            BaseColor: new(x: 0.5f, y: 0.5f, z: 0.5f, w: 1),
            Roughness: 26 / 255f,
            Reflectance: 153 / 255f
        );
        RenderSnapshot Scene(VulkanRenderer renderer)
        {
            RenderSnapshot snapshot = Gray(gray);
            snapshot.Instances.Add(Floor(renderer, floor));
            MeshId box = renderer.CreateMesh(Primitives.Box(new(0.5f)));
            snapshot.Instances.Add(
                Block(box: box, material: Glow(renderer, new(3)), center: new(0, 3, -10), size: new(40, 9, 1))
            );
            return snapshot;
        }

        var view = new TestView(ExposureStops: exposureStops);
        (float[] with, CameraView camera) = RenderHdr(reflections: Reflections, scene: Scene, view: view);
        float[] without = RenderHdr(reflections: new NoReflections(), scene: Scene, view: view).Hdr;

        foreach (float x in (float[])[-2, 0, 2])
        foreach (float y in (float[])[0, 1, 2])
        {
            var onWall = new Vector3(x, y, -9.5f);
            (int X, int Y) pixel = MirrorPixel(onWall);
            Vector3 weight = SpecularWeight(floor, camera, pixel);
            // The ambient specular term k · T_s: W times the gray, which the sky and the ground share, pre-exposed.
            Vector3 ambientSpecular = weight * gray * MathF.Pow(2, exposureStops);
            Vector3 expected = At(without, pixel) - ambientSpecular + weight * At(without, PixelOf(onWall));
            BrdfTesting
                .LargestChannelDifference(At(with, pixel), expected)
                .Should()
                .BeLessThanOrEqualTo(0.01f * expected.Y, "{0} reflects the wall: {1}", pixel, At(with, pixel));
        }
    }

    [Fact]
    public void UnderAmbientOcclusionAHitReplacesTheOccludedAmbientSpecularTerm()
    {
        // The floor within half a metre of the wall's base, where GTAO's V and so V_s lie well below 1. A gray of 1 makes
        // the occluded term k · V_s · T_s large beside the 1 % tolerance, so a trace that took the unoccluded term fails.
        const float gray = 1;
        var floor = new MaterialDesc(
            BaseColor: new(x: 0.5f, y: 0.5f, z: 0.5f, w: 1),
            Roughness: 26 / 255f,
            Reflectance: 153 / 255f
        );
        RenderSnapshot Scene(VulkanRenderer renderer)
        {
            RenderSnapshot snapshot = Gray(gray);
            snapshot.Instances.Add(Floor(renderer, floor));
            MeshId box = renderer.CreateMesh(Primitives.Box(new(0.5f)));
            snapshot.Instances.Add(
                Block(box: box, material: Glow(renderer, new(3)), center: new(0, 3, -10), size: new(40, 9, 1))
            );
            return snapshot;
        }

        (float[] with, float[] occlusion, CameraView camera) = RenderUnderOcclusion(Reflections, Scene);
        float[] without = RenderUnderOcclusion(new NoReflections(), Scene).Hdr;

        foreach (float x in (float[])[-2, 0, 2])
        foreach (float height in (float[])[0.02f, 0.04f, 0.08f])
        {
            var onWall = new Vector3(x, FloorHeight + height, -9.5f);
            (int X, int Y) pixel = MirrorPixel(onWall);
            Vector3 toView = -Vector3.Normalize(camera.ViewRayThrough(new(pixel.X + 0.5f, pixel.Y + 0.5f)));
            float nDotV = Math.Clamp(Vector3.Dot(FlatMapNormal, toView), 1e-4f, 1);
            float specularOcclusion = (float)
                new GgxLobe(floor.Roughness).SpecularOcclusion(
                    nDotV: nDotV,
                    occlusion: occlusion[pixel.Y * Width + pixel.X]
                );
            specularOcclusion.Should().BeLessThan(0.9f, "the floor at {0} lies in the wall's occlusion", pixel);
            Vector3 weight = SpecularWeight(floor, camera, pixel);
            Vector3 occludedSpecular = specularOcclusion * weight * gray;
            Vector3 expected = At(without, pixel) - occludedSpecular + weight * At(without, PixelOf(onWall));
            BrdfTesting
                .LargestChannelDifference(At(with, pixel), expected)
                .Should()
                .BeLessThanOrEqualTo(0.01f * expected.Y, "{0} reflects the wall: {1}", pixel, At(with, pixel));
        }
    }

    // No pass declares the tables after their upload, so the barrier after the copy is the only one that can make the
    // copy visible to the trace, which reads the DFG table in a compute shader through lobesOf.
    [Fact]
    public void AfterTheFirstFrameTheBrdfTablesAreVisibleToTheComputeShaderOfTheTrace()
    {
        VulkanRenderer renderer = Headless(Reflections);
        try
        {
            renderer.Render(BoxAbove(renderer, Mirror()), 1f);

            foreach (GpuImage table in (GpuImage[])[renderer.BrdfTableImages.Dfg, renderer.BrdfTableImages.LobeSky])
            {
                table.State.Stages.Should().HaveFlag(PipelineStageFlags2.ComputeShaderBit, "the trace reads it");
                table.State.Stages.Should().HaveFlag(PipelineStageFlags2.FragmentShaderBit, "and so does the lighting");
                table.State.Access.Should().Be(AccessFlags2.ShaderSampledReadBit);
            }
        }
        finally
        {
            Gpu.DisposeAndAssertClean(renderer);
        }
    }

    [Fact]
    public void AFloorWithNothingAboveItShowsTheAmbientSpecularTerm()
    {
        static RenderSnapshot Scene(VulkanRenderer renderer)
        {
            RenderSnapshot snapshot = Gray(0.2f);
            snapshot.Instances.Add(Floor(renderer, Mirror()));
            return snapshot;
        }

        float[] with = RenderHdr(Reflections, Scene).Hdr;
        float[] without = RenderHdr(new NoReflections(), Scene).Hdr;

        ShouldMatch(with, without, "no ray hits its own floor, and a miss keeps the term");
    }

    [Fact]
    public void AFloorRougherThanTheMaximumKeepsTheAmbientSpecularTerm()
    {
        // About 0.7, above the maximum of 0.6; the box is rough too, so that no pixel of the frame reflects.
        static RenderSnapshot Scene(VulkanRenderer renderer)
        {
            RenderSnapshot snapshot = Gray(0.2f);
            snapshot.Instances.Add(Floor(renderer, Mirror(179 / 255f)));
            MaterialId glow = renderer.CreateMaterial(
                new(BaseColor: new(x: 0, y: 0, z: 0, w: 1), Roughness: 1, Reflectance: 0, Emissive: new(Bright))
            );
            snapshot.Instances.Add(Box(renderer, glow));
            return snapshot;
        }

        float[] with = RenderHdr(Reflections, Scene).Hdr;
        float[] without = RenderHdr(new NoReflections(), Scene).Hdr;

        ShouldMatch(with, without, "above the maximum roughness SSR has faded out");
    }

    [Fact]
    public void AMetalFloorReflectsInItsOwnColor()
    {
        var gold = new MaterialDesc(BaseColor: new(x: 1, y: 0.71f, z: 0.29f, w: 1), Roughness: 0, Metallic: 1);
        float[] hdr = RenderHdr(Reflections, renderer => BoxAbove(renderer, gold)).Hdr;

        Vector3 shown = At(hdr, MirrorPixel(BoxFace));
        shown.X.Should().BeGreaterThan(shown.Y, "gold reflects more red than green: {0}", shown);
        shown.Y.Should().BeGreaterThan(shown.Z, "and more green than blue");
        shown.Z.Should().BePositive("the white box shows in it");
    }

    [Fact]
    public void ARayThatPassesFarBehindAnObjectShowsWhatLiesBehindIt()
    {
        // The floor point 4 m ahead reflects the green wall 20 m ahead. On the screen its ray crosses a red plate 6 m
        // ahead, which hangs above the ray: where the ray's image crosses the plate's, the ray is 1.4 m to 3 m behind it,
        // past the plate's slab of 0.6 m, though it started nearer the eye than the plate.
        static RenderSnapshot Scene(VulkanRenderer renderer)
        {
            RenderSnapshot snapshot = Dark();
            snapshot.Instances.Add(Floor(renderer, Mirror()));
            MeshId box = renderer.CreateMesh(Primitives.Box(new(0.5f)));
            snapshot.Instances.Add(
                Block(
                    box: box,
                    material: Glow(renderer, new(Bright, 0, 0)),
                    center: new(0, 0.06f, -6),
                    size: new(2, 0.48f, 0.05f)
                )
            );
            snapshot.Instances.Add(
                Block(
                    box: box,
                    material: Glow(renderer, new(0, Bright, 0)),
                    center: new(0, 3, -20.5f),
                    size: new(60, 9, 1)
                )
            );
            return snapshot;
        }

        float[] hdr = RenderHdr(Reflections, Scene).Hdr;

        Vector3 shown = At(hdr, PixelOf(new(0, FloorHeight, -4)));
        shown.Y.Should().BeGreaterThan(Bright / 2, "the floor shows the green wall behind the plate: {0}", shown);
        shown.X.Should().BeLessThan(shown.Y / 100, "and nothing of the red plate");
    }

    [Fact]
    public void AMirrorGivesNoWeightToASurfaceThatItsRayMeetsFromBehind()
    {
        // A mirror wall 8 m ahead faces the eye, and a red box hangs 5 m ahead. The rays of the mirror just left of the
        // box come back toward the eye and pass behind the box's front face, into its slab from behind, where q = 1.
        static RenderSnapshot Scene(VulkanRenderer renderer)
        {
            RenderSnapshot snapshot = Dark();
            snapshot.Instances.Add(
                Gpu.Instance(
                    mesh: renderer.CreateMesh(Primitives.Plane(1)),
                    material: renderer.CreateMaterial(Mirror() with { NormalMap = FlatNormalMap(renderer) }),
                    position: new(0, 0, -8),
                    scale: new(10),
                    rotation: Quaternion.CreateFromAxisAngle(Vector3.UnitX, MathF.PI / 2),
                    radius: 15
                )
            );
            snapshot.Instances.Add(
                Block(
                    box: renderer.CreateMesh(Primitives.Box(new(0.5f))),
                    material: Glow(renderer, new(Bright, 0, 0)),
                    center: new(0.6f, 0, -5),
                    size: new(0.5f)
                )
            );
            return snapshot;
        }

        float[] with = RenderHdr(Reflections, Scene).Hdr;
        float[] without = RenderHdr(new NoReflections(), Scene).Hdr;

        foreach (float x in (float[])[0.41f, 0.44f, 0.47f])
        {
            (int X, int Y) pixel = PixelOf(new(x, 0, -8));
            At(with, pixel).Should().Be(At(without, pixel), "the ray at {0} meets the box from behind", pixel);
        }
    }

    [Fact]
    public void ARayThatPointsIntoItsOwnSurfaceAddsNothing()
    {
        // The floor's normal map leans its shading normal 45° away from the eye, so every mirror ray points down into
        // the floor, where the surface fade is 0.
        static RenderSnapshot Scene(VulkanRenderer renderer)
        {
            RenderSnapshot snapshot = Gray(0.2f);
            TextureId leaning = renderer.CreateTexture(
                new(Width: 1, Height: 1, Rgba: [128, 218, 218, 255], Srgb: false)
            );
            MaterialDesc floor = new MaterialDesc(BaseColor: new(x: 0.5f, y: 0.5f, z: 0.5f, w: 1), Roughness: 0) with
            {
                NormalMap = leaning,
            };
            snapshot.Instances.Add(
                Gpu.Instance(
                    mesh: renderer.CreateMesh(Primitives.Plane(1)),
                    material: renderer.CreateMaterial(floor),
                    position: new(0, FloorHeight, -20),
                    scale: new(30, 1, 30),
                    radius: 43
                )
            );
            return snapshot;
        }

        float[] with = RenderHdr(Reflections, Scene).Hdr;
        float[] without = RenderHdr(new NoReflections(), Scene).Hdr;

        ShouldMatch(with, without, "a ray into its own surface fades out before it starts");
    }

    [Theory]
    // A pixel footprint of half the view depth in a window 4 px tall at 90°, and of 0.64 view depths at 179° in
    // 640 × 360: two footprints along the normal of a surface that faces the eye reach the eye's plane or pass it.
    [InlineData(640, 4, 90f)]
    [InlineData(640, 360, 179f)]
    public void AMirrorThatFacesTheEyeReflectsNothingWhenTwoPixelFootprintsReachTheEye(
        int width,
        int height,
        float fovDegrees
    )
    {
        // A gray metal mirror 3 m ahead that fills the screen: no ray from a flat mirror meets the mirror again.
        RenderSnapshot Scene(VulkanRenderer renderer)
        {
            RenderSnapshot snapshot = Gray(0.2f);
            snapshot.Camera.VerticalFov = fovDegrees * MathF.PI / 180;
            var mirror = new MaterialDesc(BaseColor: new(x: 0.5f, y: 0.5f, z: 0.5f, w: 1), Roughness: 0, Metallic: 1);
            snapshot.Instances.Add(
                Gpu.Instance(
                    mesh: renderer.CreateMesh(Primitives.Plane(1)),
                    material: renderer.CreateMaterial(mirror),
                    position: new(0, 0, -3),
                    rotation: Quaternion.CreateFromAxisAngle(Vector3.UnitX, MathF.PI / 2),
                    scale: new(1_000, 1, 1_000),
                    radius: 1_500
                )
            );
            return snapshot;
        }

        var view = new TestView(Width: width, Height: height);
        float[] with = RenderHdr(reflections: Reflections, scene: Scene, view: view).Hdr;
        float[] without = RenderHdr(reflections: new NoReflections(), scene: Scene, view: view).Hdr;

        ShouldMatch(with, without, "every origin stays in front of the eye, and no ray meets its own flat mirror");
    }

    [Fact]
    public void AReflectionIsSharpWhereTheObjectTouchesTheFloorAndBlursAwayFromIt()
    {
        // About 0.2. The bar stands on the floor 7.7 m ahead, 3 m wide and 2 m tall.
        const float roughness = 51 / 255f;
        RenderSnapshot Scene(VulkanRenderer renderer)
        {
            RenderSnapshot snapshot = Dark();
            snapshot.Instances.Add(Floor(renderer, Mirror(roughness)));
            MeshId box = renderer.CreateMesh(Primitives.Box(new(0.5f)));
            snapshot.Instances.Add(
                Block(box: box, material: Glow(renderer, new(Bright)), center: new(0, -0.5f, -8), size: new(3, 2, 0.6f))
            );
            return snapshot;
        }

        float[] hdr = RenderHdr(Reflections, Scene).Hdr;

        int edge = PixelOf(new(-1.5f, 0, -7.7f)).X;
        int center = Width / 2;
        float WidthAt(float height) =>
            RiseWidth(Row(hdr, MirrorPixel(new(0, height, -7.7f)).Y), outside: edge - 50, plateau: center);
        float nearContact = WidthAt(FloorHeight + 0.15f);
        float nearTop = WidthAt(0.3f);
        nearContact
            .Should()
            .BeLessThan(nearTop / 2, "a reflected ray is short near the contact, so its cone is narrow there");
    }

    [Fact]
    public void GlassAndPuffsDoNotShowInReflections()
    {
        var paneCenter = new Vector3(-1, -0.5f, -6);
        var puffCenter = new Vector3(1, -0.5f, -6);
        RenderSnapshot Scene(VulkanRenderer renderer, bool transparent)
        {
            RenderSnapshot snapshot = Dark();
            snapshot.Instances.Add(Floor(renderer, Mirror()));
            if (!transparent)
                return snapshot;
            MaterialId pane = renderer.CreateMaterial(
                new(
                    BaseColor: new(x: 0, y: 0, z: 0, w: 0.5f),
                    Roughness: 0.5f,
                    Reflectance: 0,
                    Emissive: new(0, 0, Bright)
                )
            );
            snapshot.Instances.Add(
                Gpu.Instance(
                    mesh: renderer.CreateMesh(Primitives.Plane(1)),
                    material: pane,
                    position: paneCenter,
                    scale: new(0.5f),
                    rotation: Quaternion.CreateFromAxisAngle(Vector3.UnitX, MathF.PI / 2),
                    flags: RenderFlags.Transparent,
                    radius: 0.75f
                )
            );
            snapshot.PuffLooks.Add(
                new(
                    Life: 2,
                    StartRadius: 0.4f,
                    EndRadius: 0.4f,
                    TemperatureRatio: 1,
                    Shading: PuffShading.Glow(new(Bright, Bright / 2, 0))
                )
            );
            snapshot.Puffs.Add(new() { Position = puffCenter, PreviousPosition = puffCenter });
            return snapshot;
        }

        float[] with = RenderHdr(Reflections, renderer => Scene(renderer, transparent: true)).Hdr;
        float[] without = RenderHdr(Reflections, renderer => Scene(renderer, transparent: false)).Hdr;

        At(with, PixelOf(paneCenter)).Z.Should().BeGreaterThan(1, "the pane shows");
        At(with, PixelOf(puffCenter)).X.Should().BeGreaterThan(1, "and the puff");
        foreach (Vector3 center in (Vector3[])[paneCenter, puffCenter])
            At(with, MirrorPixel(center))
                .Should()
                .Be(At(without, MirrorPixel(center)), "the rays read the lit image before the transparent pass");
    }

    [Fact]
    public void AnImpostorKeepsItsBakedLightAndShowsInReflections()
    {
        var center = new Vector3(0, -0.5f, -6);
        RenderSnapshot Scene(VulkanRenderer renderer)
        {
            RenderSnapshot snapshot = Dark();
            snapshot.Instances.Add(Floor(renderer, Mirror()));
            MaterialId glossy = renderer.CreateMaterial(
                new(
                    BaseColor: new(x: 0.8f, y: 0.1f, z: 0.1f, w: 1),
                    Roughness: 0.1f,
                    Reflectance: 1,
                    Emissive: new(3, 1, 0.5f)
                )
            );
            snapshot.Instances.Add(
                Gpu.Instance(
                    mesh: renderer.CreateMesh(Primitives.Sphere(1)),
                    material: glossy,
                    position: center,
                    scale: new(0.5f),
                    radius: 0.5f,
                    entity: 1
                )
            );
            return snapshot;
        }

        (float[] with, CameraView camera) = RenderImpostors(Reflections, Scene);
        float[] without = RenderImpostors(new NoReflections(), Scene).Hdr;

        (int X, int Y) middle = PixelOf(center);
        for (int dy = -8; dy <= 8; dy++)
        for (int dx = -8; dx <= 8; dx++)
            At(with, (middle.X + dx, middle.Y + dy))
                .Should()
                .Be(At(without, (middle.X + dx, middle.Y + dy)), "an impostor starts no ray");
        (int X, int Y) mirror = MirrorPixel(center);
        Vector3 expected = SpecularWeight(Mirror(), camera, mirror) * At(with, middle);
        BrdfTesting
            .LargestChannelDifference(At(with, mirror), expected)
            .Should()
            .BeLessThanOrEqualTo(0.05f * expected.X, "the mirror shows the billboard: {0}", At(with, mirror));
    }

    [Fact]
    public void AFarPlanetShowsInANearMirror()
    {
        // A planet of 3,000 km radius 10,000 km ahead, above the horizon from 5.6° to 38°.
        var radiance = new Vector3(2, 1.5f, 1);
        RenderSnapshot Scene(VulkanRenderer renderer)
        {
            RenderSnapshot snapshot = Dark();
            snapshot.Instances.Add(Floor(renderer, Mirror()));
            snapshot.Instances.Add(
                Gpu.Instance(
                    mesh: renderer.CreateMesh(Primitives.Sphere(1)),
                    material: Glow(renderer, radiance),
                    position: new(0, 4e6f, -1e7f),
                    scale: new(3e6f),
                    radius: 3e6f
                )
            );
            return snapshot;
        }

        (float[] hdr, CameraView camera) = RenderHdr(Reflections, Scene);

        foreach (float degrees in (float[])[10, 15, 20])
        {
            // The floor point that far below the horizon reflects a ray that far above it.
            float below = MathF.Tan(degrees * MathF.PI / 180);
            (int X, int Y) pixel = PixelOf(new(0, FloorHeight, FloorHeight / below));
            Vector3 expected = SpecularWeight(Mirror(), camera, pixel) * radiance;
            BrdfTesting
                .LargestChannelDifference(At(hdr, pixel), expected)
                .Should()
                .BeLessThanOrEqualTo(0.02f * expected.X, "{0} shows the planet: {1}", pixel, At(hdr, pixel));
        }
    }

    [Fact]
    public void AReflectionOnAFloorAtTheCapStaysAtTheCap()
    {
        // The floor's own light plus the reflection of the box passes MaxRadiance, the surfaces' share of the HDR image.
        // Past the share, the puffs over the pixel could carry its sum past 65,504, which a half float above 65,520 holds
        // only as infinity, and the tone curve turns infinity into NaN.
        const float cap = ShaderInput.MaxRadiance;
        float[] hdr = RenderHdr(
            Reflections,
            renderer =>
            {
                RenderSnapshot snapshot = Dark();
                snapshot.Instances.Add(Floor(renderer, Mirror() with { Emissive = new(cap) }));
                snapshot.Instances.Add(Box(renderer, Glow(renderer, new(cap))));
                return snapshot;
            }
        ).Hdr;

        At(hdr, MirrorPixel(BoxFace)).Should().Be(new Vector3(cap), "the trace caps its sum at MaxRadiance");
        hdr.Should().OnlyContain(value => float.IsFinite(value), "no value of the frame is infinite");
    }

    [Fact]
    public void TwoRendersGiveTheSameBits()
    {
        float[] first = RenderHdr(Reflections, renderer => BoxAbove(renderer, Mirror(51 / 255f))).Hdr;
        float[] second = RenderHdr(Reflections, renderer => BoxAbove(renderer, Mirror(51 / 255f))).Hdr;

        second.Should().Equal(first, "the trace has no noise and no history");
    }

    [Fact]
    public void TheTraceRunsAfterTheRadiancePyramidAndBeforeTheTransparentPass()
    {
        VulkanRenderer renderer = Headless(Reflections);
        try
        {
            renderer
                .Graph.Passes.Select(pass => pass.Name)
                .Should()
                .ContainInOrder(["lighting", "radiance pyramid", "reflections", "transparent"]);
        }
        finally
        {
            Gpu.DisposeAndAssertClean(renderer);
        }
    }

    [Fact]
    public void WithTheEnvironmentMapOnAHitReplacesTheMapsSpecularTerm()
    {
        // A wide wall that glows 3 fills the floor's lobe on the screen and in the map alike, so the ray's radiance and
        // the map's specular term are both 3: a hit that replaces the term leaves the floor as it was, and a fallback
        // that read the hemisphere, which is black while the map is on, would add the wall a second time.
        static RenderSnapshot Scene(VulkanRenderer renderer)
        {
            RenderSnapshot snapshot = Dark();
            snapshot.Instances.Add(Floor(renderer, Mirror()));
            MeshId box = renderer.CreateMesh(Primitives.Box(new(0.5f)));
            snapshot.Instances.Add(
                Block(box: box, material: Glow(renderer, new(3)), center: new(0, 3, -10), size: new(40, 9, 1))
            );
            return snapshot;
        }

        (float[] with, CameraView camera) = RenderUnderMap(reflections: Reflections, scene: Scene);
        float[] without = RenderUnderMap(reflections: new NoReflections(), scene: Scene).Hdr;

        foreach (float x in (float[])[-2, 0, 2])
        foreach (float y in (float[])[0, 1, 2])
        {
            (int X, int Y) pixel = MirrorPixel(new(x, y, -9.5f));
            Vector3 mapped = At(without, pixel);
            mapped
                .Y.Should()
                .BeGreaterThan(SpecularWeight(Mirror(), camera, pixel).Y * 2.5f, "the map lights the floor");
            BrdfTesting
                .LargestChannelDifference(At(with, pixel), mapped)
                .Should()
                .BeLessThanOrEqualTo(
                    0.05f * mapped.Y,
                    "{0} reflects the wall once, and the map's 32 texels hold the wall's light about 3 % low: {1}",
                    pixel,
                    At(with, pixel)
                );
        }
    }

    /// <summary>
    /// Renders the scene that <paramref name="scene"/> builds once at 640 × 360 and 0 stops, with
    /// <paramref name="reflections"/>, under a map of 32 texels that draws all six faces in the frame.
    /// </summary>
    /// <returns>The frame's HDR image, and its camera.</returns>
    private static (float[] Hdr, CameraView Camera) RenderUnderMap(
        ReflectionSetting reflections,
        Func<VulkanRenderer, RenderSnapshot> scene
    )
    {
        VulkanRenderer renderer = Gpu.Headless(
            reflections: reflections,
            environmentMap: new SceneEnvironmentMap(FaceSize: 32, FacesPerFrame: 6)
        );
        try
        {
            renderer.Render(scene(renderer), 1f);
            return (renderer.ReadHdr(), renderer.LastScene.Camera);
        }
        finally
        {
            Gpu.DisposeAndAssertClean(renderer);
        }
    }

    /// <summary>Renders the scene that <paramref name="scene"/> builds once at 640 × 360 and 0 stops, under GTAO's default.</summary>
    /// <returns>The frame's HDR image, its occlusion target and its camera.</returns>
    private static (float[] Hdr, float[] Occlusion, CameraView Camera) RenderUnderOcclusion(
        ReflectionSetting reflections,
        Func<VulkanRenderer, RenderSnapshot> scene
    )
    {
        VulkanRenderer renderer = Gpu.Headless(
            ambientOcclusion: GroundTruthOcclusion.Default,
            exposure: new FixedExposure(0),
            reflections: reflections
        );
        try
        {
            renderer.Render(scene(renderer), 1f);
            return (renderer.ReadHdr(), renderer.ReadOcclusion(), renderer.LastScene.Camera);
        }
        finally
        {
            Gpu.DisposeAndAssertClean(renderer);
        }
    }

    /// <summary>
    /// A floor of <paramref name="floor"/>, and above it an emissive box of <see cref="Bright"/>, 6 m wide and 1.5 m tall,
    /// whose front face stands 7.5 m ahead from 0.5 m to 2 m above the floor.
    /// </summary>
    private static RenderSnapshot BoxAbove(VulkanRenderer renderer, MaterialDesc floor)
    {
        RenderSnapshot snapshot = Dark();
        snapshot.Instances.Add(Floor(renderer, floor));
        snapshot.Instances.Add(Box(renderer, Glow(renderer, new(Bright))));
        return snapshot;
    }

    /// <summary>The box of <see cref="BoxAbove"/> in <paramref name="material"/>.</summary>
    private static RenderInstance Box(VulkanRenderer renderer, MaterialId material) =>
        Block(
            box: renderer.CreateMesh(Primitives.Box(new(0.5f))),
            material: material,
            center: new(0, -0.25f, -8),
            size: new(6, 1.5f, 1)
        );

    /// <summary>
    /// A bright bar 0.4 m tall whose front face stands 7.8 m ahead, from 27 m to the left, past the screen's edge, to
    /// 3 m to the right.
    /// </summary>
    private static RenderSnapshot LongBar(VulkanRenderer renderer)
    {
        RenderSnapshot snapshot = Dark();
        snapshot.Instances.Add(Floor(renderer, Mirror()));
        snapshot.Instances.Add(
            Block(
                box: renderer.CreateMesh(Primitives.Box(new(0.5f))),
                material: Glow(renderer, new(Bright)),
                center: new(-12, -0.6f, -8),
                size: new(30, 0.4f, 0.4f)
            )
        );
        return snapshot;
    }

    /// <summary>
    /// The radius in pixels of the cone of <paramref name="roughness"/> at <paramref name="hit"/>, for the ray from the
    /// floor point that <paramref name="floorPixel"/> sees.
    /// </summary>
    private static float ConeRadiusPixels(float roughness, (int X, int Y) floorPixel, Vector3 hit)
    {
        Double3 ray = BrdfTesting.Ray(new(floorPixel.X, floorPixel.Y), Width, Height);
        var floorPoint = BrdfTesting
            .OnPlane(ray: ray, point: new(0, FloorHeight, 0), normal: Double3.UnitY)
            .ToVector3();
        float pixelAngle = 2 * MathF.Tan(Gpu.Fov / 2) / Height;
        return (hit - floorPoint).Length()
            * ReflectionCone.OfRoughness(roughness).TanHalfAngle
            / (hit.Length() * pixelAngle);
    }

    /// <summary>Renders the scene with impostors on until its object is an impostor whose capture is done.</summary>
    private static (float[] Hdr, CameraView Camera) RenderImpostors(
        ReflectionSetting reflections,
        Func<VulkanRenderer, RenderSnapshot> scene
    )
    {
        VulkanRenderer renderer = Headless(reflections: reflections, impostors: true);
        try
        {
            RenderSnapshot snapshot = scene(renderer);
            renderer.Render(snapshot, 1f);
            renderer.Render(snapshot, 1f);
            renderer.LastFrameStats.Impostors.Should().Be(1, "the sphere is small enough on the screen");
            renderer.LastFrameStats.ImpostorCaptures.Should().Be(0, "its capture is done");
            return (renderer.ReadHdr(), renderer.LastScene.Camera);
        }
        finally
        {
            Gpu.DisposeAndAssertClean(renderer);
        }
    }

    /// <summary>Every value of two HDR images agrees within the rounding of a half float.</summary>
    private static void ShouldMatch(float[] actual, float[] expected, string because)
    {
        int[] differing =
        [
            .. Enumerable
                .Range(0, actual.Length)
                .Where(i => MathF.Abs(actual[i] - expected[i]) > MathF.Abs(expected[i]) / 1024),
        ];
        differing.Should().BeEmpty("{0}; the first is value {1}", because, differing.FirstOrDefault());
    }
}
