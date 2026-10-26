using System.Numerics;
using Silk.NET.Vulkan;
using static Tertius.Rendering.Tests.ReflectionTesting;

namespace Tertius.Rendering.Tests;

/// <summary>
/// The screen-space reflections on the GPU (docs/plans/screen-space-reflections.md, "GPU tests of the reflections"): what
/// a floor shows where its rays hit, blur, fade or miss, read as linear HDR values. Unless a test says otherwise, the
/// floor is white metal and the scene is dark, so the floor shows only what the trace adds. The first three tests are
/// the user's (decision 10). One test checks that the trace may read the BRDF tables after their upload.
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

    /// <summary>Both traces: the behaviors of the reflections hold at either resolution.</summary>
    public static TheoryData<TraceResolution> Resolutions => [TraceResolution.Full, TraceResolution.Half];

    [Theory]
    [MemberData(nameof(Resolutions))]
    public void ABrightObjectAboveAMirrorFloorShowsInTheFloor(TraceResolution resolution)
    {
        (float[] hdr, CameraView camera) = RenderHdr(
            ReflectionsAt(resolution),
            renderer => BoxAbove(renderer, Mirror())
        );

        (int X, int Y) pixel = MirrorPixel(BoxFace);
        Vector3 expected = SpecularWeight(Mirror(), camera, pixel) * Bright;
        BrdfTesting
            .LargestChannelDifference(At(hdr, pixel), expected)
            .Should()
            .BeLessThanOrEqualTo(0.02f * expected.Y, "the mirror shows W · L at {0}: {1}", pixel, At(hdr, pixel));
    }

    [Theory]
    [MemberData(nameof(Resolutions))]
    public void ARougherFloorBlursTheReflection(TraceResolution resolution)
    {
        // About 0.1, 0.2 and 0.3, in steps that the G-buffer's 8 bits hold exactly.
        float[] roughnesses = [26 / 255f, 51 / 255f, 77 / 255f];
        (int X, int Y) center = MirrorPixel(BoxFace);
        int edge = PixelOf(BoxFaceLeft).X;

        float[] widths =
        [
            .. roughnesses.Select(roughness =>
            {
                float[] hdr = RenderHdr(
                    ReflectionsAt(resolution),
                    renderer => BoxAbove(renderer, Mirror(roughness))
                ).Hdr;
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

    [Theory]
    [MemberData(nameof(Resolutions))]
    public void TheReflectionFadesAtTheScreenEdgeWithNoHardEdge(TraceResolution resolution)
    {
        // At 1280 × 720 the fade's band is 72 px, so the smoothstep's steepest step is 2.1 % of the peak per pixel.
        const int width = 1280,
            height = 720;
        var barFront = new Vector3(0, -0.6f, -7.8f);
        int row = MirrorPixel(barFront, width, height).Y;
        // Short of the bar's right end, whose reflected silhouette is a hard edge, as a mirror shows it.
        int last = PixelOf(barFront with { X = 2.5f }, width, height).X;
        float[] with = Row(
            RenderHdr(
                reflections: ReflectionsAt(resolution),
                scene: LongBar,
                view: new(Width: width, Height: height)
            ).Hdr,
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
    [InlineData(0, TraceResolution.Full)]
    [InlineData(2, TraceResolution.Full)]
    [InlineData(0, TraceResolution.Half)]
    [InlineData(2, TraceResolution.Half)]
    public void AHitReplacesTheAmbientSpecularTermUnderTheSameWeight(float exposureStops, TraceResolution resolution)
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
        (float[] with, CameraView camera) = RenderHdr(reflections: ReflectionsAt(resolution), scene: Scene, view: view);
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

    [Theory]
    [MemberData(nameof(Resolutions))]
    public void AFloorWithNothingAboveItShowsTheAmbientSpecularTerm(TraceResolution resolution)
    {
        static RenderSnapshot Scene(VulkanRenderer renderer)
        {
            RenderSnapshot snapshot = Gray(0.2f);
            snapshot.Instances.Add(Floor(renderer, Mirror()));
            return snapshot;
        }

        float[] with = RenderHdr(ReflectionsAt(resolution), Scene).Hdr;
        float[] without = RenderHdr(new NoReflections(), Scene).Hdr;

        ShouldMatch(with, without, "no ray hits its own floor, and a miss keeps the term");
    }

    [Theory]
    [MemberData(nameof(Resolutions))]
    public void AFloorRougherThanTheMaximumKeepsTheAmbientSpecularTerm(TraceResolution resolution)
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

        float[] with = RenderHdr(ReflectionsAt(resolution), Scene).Hdr;
        float[] without = RenderHdr(new NoReflections(), Scene).Hdr;

        ShouldMatch(with, without, "above the maximum roughness SSR has faded out");
    }

    [Theory]
    [MemberData(nameof(Resolutions))]
    public void AMetalFloorReflectsInItsOwnColor(TraceResolution resolution)
    {
        var gold = new MaterialDesc(BaseColor: new(x: 1, y: 0.71f, z: 0.29f, w: 1), Roughness: 0, Metallic: 1);
        float[] hdr = RenderHdr(ReflectionsAt(resolution), renderer => BoxAbove(renderer, gold)).Hdr;

        Vector3 shown = At(hdr, MirrorPixel(BoxFace));
        shown.X.Should().BeGreaterThan(shown.Y, "gold reflects more red than green: {0}", shown);
        shown.Y.Should().BeGreaterThan(shown.Z, "and more green than blue");
        shown.Z.Should().BePositive("the white box shows in it");
    }

    [Theory]
    [MemberData(nameof(Resolutions))]
    public void ARayThatPassesFarBehindAnObjectShowsWhatLiesBehindIt(TraceResolution resolution)
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

        float[] hdr = RenderHdr(ReflectionsAt(resolution), Scene).Hdr;

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

    [Theory]
    [MemberData(nameof(Resolutions))]
    public void ARayThatPointsIntoItsOwnSurfaceAddsNothing(TraceResolution resolution)
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

        float[] with = RenderHdr(ReflectionsAt(resolution), Scene).Hdr;
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

    [Theory]
    [MemberData(nameof(Resolutions))]
    public void AReflectionIsSharpWhereTheObjectTouchesTheFloorAndBlursAwayFromIt(TraceResolution resolution)
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

        float[] hdr = RenderHdr(ReflectionsAt(resolution), Scene).Hdr;

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

    [Theory]
    [MemberData(nameof(Resolutions))]
    public void GlassAndPuffsDoNotShowInReflections(TraceResolution resolution)
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

        float[] with = RenderHdr(ReflectionsAt(resolution), renderer => Scene(renderer, transparent: true)).Hdr;
        float[] without = RenderHdr(ReflectionsAt(resolution), renderer => Scene(renderer, transparent: false)).Hdr;

        At(with, PixelOf(paneCenter)).Z.Should().BeGreaterThan(1, "the pane shows");
        At(with, PixelOf(puffCenter)).X.Should().BeGreaterThan(1, "and the puff");
        foreach (Vector3 center in (Vector3[])[paneCenter, puffCenter])
            At(with, MirrorPixel(center))
                .Should()
                .Be(At(without, MirrorPixel(center)), "the rays read the lit image before the transparent pass");
    }

    [Theory]
    [MemberData(nameof(Resolutions))]
    public void AnImpostorKeepsItsBakedLightAndShowsInReflections(TraceResolution resolution)
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

        (float[] with, CameraView camera) = RenderImpostors(ReflectionsAt(resolution), Scene);
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

    [Theory]
    [MemberData(nameof(Resolutions))]
    public void AFarPlanetShowsInANearMirror(TraceResolution resolution)
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

        (float[] hdr, CameraView camera) = RenderHdr(ReflectionsAt(resolution), Scene);

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

    [Theory]
    [MemberData(nameof(Resolutions))]
    public void AReflectionOnAFloorAtTheCapStaysAtTheCap(TraceResolution resolution)
    {
        // The floor's own light plus the reflection of the box passes MaxRadiance, the surfaces' share of the HDR image.
        // Past the share, the puffs over the pixel could carry its sum past 65,504, which a half float above 65,520 holds
        // only as infinity, and the tone curve turns infinity into NaN.
        const float cap = ShaderInput.MaxRadiance;
        float[] hdr = RenderHdr(
            ReflectionsAt(resolution),
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

    [Theory]
    [MemberData(nameof(Resolutions))]
    public void TwoRendersGiveTheSameBits(TraceResolution resolution)
    {
        float[] first = RenderHdr(ReflectionsAt(resolution), renderer => BoxAbove(renderer, Mirror(51 / 255f))).Hdr;
        float[] second = RenderHdr(ReflectionsAt(resolution), renderer => BoxAbove(renderer, Mirror(51 / 255f))).Hdr;

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
    public void AtHalfResolutionTheRaysTraceBeforeTheReflectionsWhichRunBeforeTheTransparentPass()
    {
        VulkanRenderer renderer = Headless(ReflectionsAt(TraceResolution.Half));
        try
        {
            renderer
                .Graph.Passes.Select(pass => pass.Name)
                .Should()
                .ContainInOrder(["lighting", "radiance pyramid", "reflection trace", "reflections", "transparent"]);
        }
        finally
        {
            Gpu.DisposeAndAssertClean(renderer);
        }
    }

    [Fact]
    public void AtFullResolutionNoPassTracesAtHalfResolution()
    {
        VulkanRenderer renderer = Headless(ReflectionsAt(TraceResolution.Full));
        try
        {
            renderer.Graph.Passes.Select(pass => pass.Name).Should().NotContain("reflection trace");
        }
        finally
        {
            Gpu.DisposeAndAssertClean(renderer);
        }
    }

    [Fact]
    public void AtHalfResolutionAMirrorBoxOnAMirrorFloorKeepsBothReflectionsApartAtItsSides()
    {
        // A metal mirror box stands on the mirror floor 6 m ahead, before a bright wall 15 m ahead. The box's front face
        // reflects the dark space behind the eye and shows nothing, and the floor beside it, 9 m ahead at the box's
        // middle height, reflects the wall. The texels across each side of the box lie on the other surface.
        static RenderSnapshot Scene(VulkanRenderer renderer)
        {
            RenderSnapshot snapshot = Dark();
            snapshot.Instances.Add(Floor(renderer, Mirror()));
            MeshId box = renderer.CreateMesh(Primitives.Box(new(0.5f)));
            snapshot.Instances.Add(
                Block(
                    box: box,
                    material: renderer.CreateMaterial(Mirror()),
                    center: new(0, -1, -6.5f),
                    size: new(1, 1, 1)
                )
            );
            snapshot.Instances.Add(
                Block(box: box, material: Glow(renderer, new(Bright)), center: new(0, 2, -15.5f), size: new(60, 7, 1))
            );
            return snapshot;
        }

        float[] full = RenderHdr(ReflectionsAt(TraceResolution.Full), Scene).Hdr;
        float[] half = RenderHdr(ReflectionsAt(TraceResolution.Half), Scene).Hdr;

        int compared = 0;
        foreach (float height in (float[])[-1.3f, -1.1f, -0.9f, -0.7f])
        foreach (float side in (float[])[-0.5f, 0.5f])
        {
            (int X, int Y) edge = PixelOf(new(side, height, -6));
            for (int dx = -3; dx <= 3; dx++)
            {
                (int X, int Y) pixel = (edge.X + dx, edge.Y);
                BrdfTesting
                    .LargestChannelDifference(At(half, pixel), At(full, pixel))
                    .Should()
                    .BeLessThanOrEqualTo(
                        0.05f * Bright,
                        "pixel {0} beside the box's side shows its own surface's reflection: {1} at half, {2} at full",
                        pixel,
                        At(half, pixel),
                        At(full, pixel)
                    );
                compared++;
            }
        }

        BrdfTesting
            .LargestChannelDifference(At(full, PixelOf(new(-0.8f, -1, -6))), Vector3.Zero)
            .Should()
            .BeGreaterThan(Bright / 4, "the floor beside the box shows the wall");
        At(full, PixelOf(new(0, -1, -6))).Should().Be(Vector3.Zero, "and the box's face shows nothing");
        compared.Should().Be(56);
    }

    [Fact]
    public void AtHalfResolutionEachTexelHoldsTheRayOfTheTopLeftPixelOfItsBlock()
    {
        // A white mirror floor in the dark reflects a wall of red and green stripes 0.3 m wide, 15 m ahead. The white metal
        // weighs each channel alike and the dark adds nothing, so the red share of a pixel's HDR value at full resolution is
        // the red share of its ray. Along the stripes' edges the share changes from one pixel to the next.
        static RenderSnapshot Scene(VulkanRenderer renderer)
        {
            RenderSnapshot snapshot = Dark();
            snapshot.Instances.Add(Floor(renderer, Mirror()));
            MeshId box = renderer.CreateMesh(Primitives.Box(new(0.5f)));
            MaterialId red = Glow(renderer, new(Bright, 0, 0));
            MaterialId green = Glow(renderer, new(0, Bright, 0));
            for (int k = -20; k < 20; k++)
                snapshot.Instances.Add(
                    Block(
                        box: box,
                        material: k % 2 == 0 ? red : green,
                        center: new((k + 0.5f) * 0.3f, 2, -15.5f),
                        size: new(0.3f, 7, 1)
                    )
                );
            return snapshot;
        }

        static float RedShare(Vector3 radiance) => radiance.X / (radiance.X + radiance.Y);

        float[] full = RenderHdr(ReflectionsAt(TraceResolution.Full), Scene).Hdr;
        VulkanRenderer renderer = Headless(ReflectionsAt(TraceResolution.Half));
        try
        {
            renderer.Render(Scene(renderer), 1f);
            (float[] traced, int tracedWidth, int tracedHeight) = renderer.ReadReflectionTrace();

            // The blocks below the row where the wall meets the floor show only the floor.
            int firstFloorRow = PixelOf(new(0, FloorHeight, -15)).Y / 2 + 1;
            int compared = 0,
                atAnEdge = 0;
            for (int j = firstFloorRow; j < tracedHeight; j++)
            for (int i = 0; i < tracedWidth; i++)
            {
                Vector3 representative = At(full, (2 * i, 2 * j));
                Vector3 diagonal = At(full, (2 * i + 1, 2 * j + 1));
                Vector3 ray = At(traced, (i, j), tracedWidth);
                if (representative.X + representative.Y < 0.05f * Bright || ray.X + ray.Y <= 0)
                    continue;
                RedShare(ray)
                    .Should()
                    .BeApproximately(
                        RedShare(representative),
                        0.02f,
                        "texel ({0}, {1}) holds the ray of pixel ({2}, {3}): {4} against {5} at full resolution",
                        i,
                        j,
                        2 * i,
                        2 * j,
                        ray,
                        representative
                    );
                compared++;
                if (Math.Abs(RedShare(representative) - RedShare(diagonal)) > 0.3f)
                    atAnEdge++;
            }

            compared.Should().BeGreaterThan(1000, "the floor below the wall reflects the stripes");
            atAnEdge.Should().BeGreaterThan(50, "the stripes' edges cross the rows of the floor's reflection");
        }
        finally
        {
            Gpu.DisposeAndAssertClean(renderer);
        }
    }

    [Fact]
    public void AtHalfResolutionAMirrorSlabKeepsItsReflectionApartFromTheFloorBehindIt()
    {
        // A mirror slab hangs half a metre above the mirror floor, its top about 1 m below the eye and its far edge 6 m
        // ahead, before a wall red below the eye's height and green above it. At the far edge's row, the slab reflects the
        // green half, and the floor 9 m ahead behind it the red half. Both face up, so only their depths tell them apart.
        // The far edge splits the blocks of 2 × 2 pixels of its row, so a texel of the trace stands for both surfaces.
        float top = ToAnOddPixelBoundary(
            start: -1,
            projected: height => Gpu.Project(point: new(0, height, -6), width: Width, height: Height).Y
        );
        RenderSnapshot Scene(VulkanRenderer renderer)
        {
            RenderSnapshot snapshot = Dark();
            snapshot.Instances.Add(Floor(renderer, Mirror()));
            MeshId box = renderer.CreateMesh(Primitives.Box(new(0.5f)));
            snapshot.Instances.Add(
                Block(
                    box: box,
                    material: renderer.CreateMaterial(Mirror()),
                    center: new(0, top - 0.05f, -5),
                    size: new(2, 0.1f, 2)
                )
            );
            snapshot.Instances.Add(
                Block(
                    box: box,
                    material: Glow(renderer, new(Bright, 0, 0)),
                    center: new(0, -0.75f, -15.5f),
                    size: new(60, 1.5f, 1)
                )
            );
            snapshot.Instances.Add(
                Block(
                    box: box,
                    material: Glow(renderer, new(0, Bright, 0)),
                    center: new(0, 2.5f, -15.5f),
                    size: new(60, 5, 1)
                )
            );
            return snapshot;
        }

        float[] full = RenderHdr(ReflectionsAt(TraceResolution.Full), Scene).Hdr;
        float[] half = RenderHdr(ReflectionsAt(TraceResolution.Half), Scene).Hdr;

        (int X, int Y) edge = PixelOf(new(0, top, -6));
        At(full, (edge.X, edge.Y + 3)).Y.Should().BeGreaterThan(Bright / 4, "the slab shows the green half");
        At(full, (edge.X, edge.Y - 3)).X.Should().BeGreaterThan(Bright / 4, "the floor behind it shows the red half");
        foreach (int dx in (int[])[-1, 0, 1])
            for (int dy = -3; dy <= 3; dy++)
            {
                (int X, int Y) pixel = (edge.X + dx, edge.Y + dy);
                BrdfTesting
                    .LargestChannelDifference(At(half, pixel), At(full, pixel))
                    .Should()
                    .BeLessThanOrEqualTo(
                        0.05f * Bright,
                        "pixel {0} by the slab's far edge shows its own surface's reflection: {1} at half, {2} at full",
                        pixel,
                        At(half, pixel),
                        At(full, pixel)
                    );
            }
    }

    [Fact]
    public void AtHalfResolutionTheTwoFacesOfAMirrorCornerKeepTheirReflectionsApart()
    {
        // A mirror box turned by 45° about the vertical shows the eye a corner 6 m ahead. Its left face reflects the red
        // wall at the left, and its right face the green wall at the right. Texels across the corner lie near the other
        // face's plane, but face another way. The corner splits the blocks of 2 × 2 pixels of its column.
        float x = ToAnOddPixelBoundary(
            start: 0,
            projected: across => Gpu.Project(point: new(across, -0.5f, -6), width: Width, height: Height).X
        );
        RenderSnapshot Scene(VulkanRenderer renderer)
        {
            RenderSnapshot snapshot = Dark();
            MeshId box = renderer.CreateMesh(Primitives.Box(new(0.5f)));
            snapshot.Instances.Add(
                Gpu.Instance(
                    mesh: box,
                    material: renderer.CreateMaterial(Mirror()),
                    position: new(x, -0.5f, -6 - MathF.Sqrt(2) / 2),
                    scale: Vector3.One,
                    rotation: Quaternion.CreateFromAxisAngle(Vector3.UnitY, MathF.PI / 4),
                    radius: 1
                )
            );
            snapshot.Instances.Add(
                Block(
                    box: box,
                    material: Glow(renderer, new(Bright, 0, 0)),
                    center: new(-4.5f, 0, -7),
                    size: new(1, 6, 10)
                )
            );
            snapshot.Instances.Add(
                Block(
                    box: box,
                    material: Glow(renderer, new(0, Bright, 0)),
                    center: new(4.5f, 0, -7),
                    size: new(1, 6, 10)
                )
            );
            return snapshot;
        }

        float[] full = RenderHdr(ReflectionsAt(TraceResolution.Full), Scene).Hdr;
        float[] half = RenderHdr(ReflectionsAt(TraceResolution.Half), Scene).Hdr;

        (int X, int Y) corner = PixelOf(new(x, -0.5f, -6));
        At(full, (corner.X - 4, corner.Y)).X.Should().BeGreaterThan(Bright / 4, "the left face shows the red wall");
        At(full, (corner.X + 4, corner.Y)).Y.Should().BeGreaterThan(Bright / 4, "the right face shows the green wall");
        foreach (int dy in (int[])[-6, -3, 0, 3, 6])
            for (int dx = -3; dx <= 3; dx++)
            {
                (int X, int Y) pixel = (corner.X + dx, corner.Y + dy);
                BrdfTesting
                    .LargestChannelDifference(At(half, pixel), At(full, pixel))
                    .Should()
                    .BeLessThanOrEqualTo(
                        0.05f * Bright,
                        "pixel {0} by the corner shows its own face's reflection: {1} at half, {2} at full",
                        pixel,
                        At(half, pixel),
                        At(full, pixel)
                    );
            }
    }

    [Fact]
    public void AtHalfResolutionAMirrorFloorBesideARoughFloorReflectsAsAtFullResolutionUpToTheirSeam()
    {
        // The floor's left half is a mirror and its right half rougher than the maximum, both at one height, before a
        // bright wall. The texels across the seam lie on the mirror's plane but start no ray.
        static RenderSnapshot Scene(VulkanRenderer renderer)
        {
            RenderSnapshot snapshot = Dark();
            MeshId plane = renderer.CreateMesh(Primitives.Plane(1));
            foreach (
                (float center, MaterialDesc material) in ((float Center, MaterialDesc Material)[])
                    [(Center: -15, Material: Mirror()), (Center: 15, Material: Mirror(179 / 255f))]
            )
                snapshot.Instances.Add(
                    Gpu.Instance(
                        mesh: plane,
                        material: renderer.CreateMaterial(material with { NormalMap = FlatNormalMap(renderer) }),
                        position: new(center, FloorHeight, -20),
                        scale: new(15, 1, 30),
                        radius: 34
                    )
                );
            MeshId box = renderer.CreateMesh(Primitives.Box(new(0.5f)));
            snapshot.Instances.Add(
                Block(box: box, material: Glow(renderer, new(Bright)), center: new(0, 2, -15.5f), size: new(60, 7, 1))
            );
            return snapshot;
        }

        float[] full = RenderHdr(ReflectionsAt(TraceResolution.Full), Scene).Hdr;
        float[] half = RenderHdr(ReflectionsAt(TraceResolution.Half), Scene).Hdr;

        foreach (float ahead in (float[])[-4, -6, -9])
        {
            (int X, int Y) seam = PixelOf(new(0, FloorHeight, ahead));
            BrdfTesting
                .LargestChannelDifference(At(full, (seam.X - 6, seam.Y)), Vector3.Zero)
                .Should()
                .BeGreaterThan(Bright / 4, "the mirror shows the wall {0} m ahead", -ahead);
            for (int dx = -3; dx <= 0; dx++)
            {
                (int X, int Y) pixel = (seam.X + dx, seam.Y);
                BrdfTesting
                    .LargestChannelDifference(At(half, pixel), At(full, pixel))
                    .Should()
                    .BeLessThanOrEqualTo(
                        0.05f * Bright,
                        "mirror pixel {0} at the seam takes only the texels with a ray: {1} at half, {2} at full",
                        pixel,
                        At(half, pixel),
                        At(full, pixel)
                    );
            }
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

    /// <summary>
    /// The coordinate near <paramref name="start"/>, in m, at which <paramref name="projected"/>, an affine map from it to
    /// a pixel coordinate, reaches the next boundary between a pixel 2k and a pixel 2k + 1: the middle of a block of the
    /// half-resolution trace.
    /// </summary>
    private static float ToAnOddPixelBoundary(float start, Func<float, float> projected)
    {
        float at = projected(start);
        float pixelsPerMetre = (projected(start + 0.1f) - at) / 0.1f;
        float boundary = 2 * MathF.Floor(at / 2) + 1;
        return start + (boundary - at) / pixelsPerMetre;
    }
}
