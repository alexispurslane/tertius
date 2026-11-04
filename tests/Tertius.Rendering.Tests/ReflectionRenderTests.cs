using System.Numerics;
using Silk.NET.Vulkan;
using Tertius.Testing;
using static Tertius.Rendering.Tests.ReflectionTesting;

namespace Tertius.Rendering.Tests;

/// <summary>
/// The screen-space reflections on the GPU (docs/plans/screen-space-reflections.md, "GPU tests of the reflections"): what
/// a floor shows where its rays hit, blur, fade or miss, read as linear HDR values. Unless a test says otherwise, the
/// floor is white metal and the scene is dark, so the floor shows only what the trace adds. The first three tests are
/// the user's (decision 10). One test checks that the trace may read the BRDF tables after their upload, one that its
/// fallback is the environment map's specular term while the map is on, and one that a hit takes away the very term
/// that the ambient pass added, reflected stars included.
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

    /// <summary>The center of the mirror ball of <see cref="MirrorBesideGlow"/>, 4 m ahead.</summary>
    private static readonly Vector3 MirrorBall = new(0, 0, -4);

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
                    UnfadedReflectionsAt(resolution),
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
    public void AFloorOfThePaintsRoughnessKeepsOnlyTheAmbientSpecularTerm(TraceResolution resolution)
    {
        // About 0.38, the roughness of the demo's paint, just above the default's maximum; the box is rough too, so
        // that no pixel of the frame reflects.
        static RenderSnapshot Scene(VulkanRenderer renderer)
        {
            RenderSnapshot snapshot = Gray(0.2f);
            snapshot.Instances.Add(Floor(renderer, Mirror(97 / 255f)));
            MaterialId glow = renderer.CreateMaterial(
                new(BaseColor: new(x: 0, y: 0, z: 0, w: 1), Roughness: 1, Reflectance: 0, Emissive: new(Bright))
            );
            snapshot.Instances.Add(Box(renderer, glow));
            return snapshot;
        }

        float[] with = RenderHdr(ReflectionsAt(resolution), Scene).Hdr;
        float[] without = RenderHdr(new NoReflections(), Scene).Hdr;

        ShouldMatch(with, without, "at the paint's roughness SSR has faded out");
    }

    [Theory]
    [MemberData(nameof(Resolutions))]
    public void AFloorOfRoughness018ReflectsInFull(TraceResolution resolution)
    {
        // About 0.18, just below half the default's maximum. A maximum of 1 reflects every roughness up to 0.5 in full.
        static RenderSnapshot Scene(VulkanRenderer renderer) => BoxAbove(renderer, Mirror(46 / 255f));

        float[] shown = RenderHdr(ReflectionsAt(resolution), Scene).Hdr;
        float[] full = RenderHdr(UnfadedReflectionsAt(resolution), Scene).Hdr;

        ShouldMatch(shown, full, "the roughness fade is 1 up to half the maximum");
    }

    [Theory]
    [MemberData(nameof(Resolutions))]
    public void AFloorOfRoughness028ShowsHalfItsReflection(TraceResolution resolution)
    {
        // About 0.28, halfway between half the default's maximum and the maximum, where the fade is 0.495.
        static RenderSnapshot Scene(VulkanRenderer renderer) => BoxAbove(renderer, Mirror(71 / 255f));

        float[] shown = RenderHdr(ReflectionsAt(resolution), Scene).Hdr;
        float[] full = RenderHdr(UnfadedReflectionsAt(resolution), Scene).Hdr;
        float[] without = RenderHdr(new NoReflections(), Scene).Hdr;

        ShareOf(shown: shown, full: full, without: without)
            .Should()
            .BeApproximately(0.495f, 0.01f, "the fade falls linearly from 1 at half the maximum to 0 at it");
    }

    [Theory]
    [MemberData(nameof(Resolutions))]
    public void AMetalFloorReflectsInItsOwnColor(TraceResolution resolution)
    {
        var gold = new MaterialDesc(
            BaseColor: new(x: 1, y: 0.71f, z: 0.29f, w: 1),
            Roughness: 0,
            Model: new StandardShading(Metallic: 1)
        );
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
                    radius: 15,
                    chainDepth: TestChains.CamerasFrame
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
                    radius: 43,
                    chainDepth: TestChains.CamerasFrame
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
            var mirror = new MaterialDesc(
                BaseColor: new(x: 0.5f, y: 0.5f, z: 0.5f, w: 1),
                Roughness: 0,
                Model: new StandardShading(Metallic: 1)
            );
            snapshot.Instances.Add(
                Gpu.Instance(
                    mesh: renderer.CreateMesh(Primitives.Plane(1)),
                    material: renderer.CreateMaterial(mirror),
                    position: new(0, 0, -3),
                    rotation: Quaternion.CreateFromAxisAngle(Vector3.UnitX, MathF.PI / 2),
                    scale: new(1_000, 1, 1_000),
                    radius: 1_500,
                    chainDepth: TestChains.CamerasFrame
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

        float[] hdr = RenderHdr(UnfadedReflectionsAt(resolution), Scene).Hdr;

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
                    Emissive: new(0, 0, Bright),
                    Alpha: new GlassAlpha()
                )
            );
            snapshot.Instances.Add(
                Gpu.Instance(
                    mesh: renderer.CreateMesh(Primitives.Plane(1)),
                    material: pane,
                    position: paneCenter,
                    scale: new(0.5f),
                    rotation: Quaternion.CreateFromAxisAngle(Vector3.UnitX, MathF.PI / 2),
                    radius: 0.75f,
                    chainDepth: TestChains.CamerasFrame
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
                    entity: 1,
                    chainDepth: TestChains.CamerasFrame
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
                    radius: 3e6f,
                    chainDepth: TestChains.CamerasFrame
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
    public void NoPixelOfAFloorAtTheCapPassesTheCapAtTheOutlineOfItsReflection(TraceResolution resolution)
    {
        // At half resolution the box's reflected outline, a hit beside a miss, traces its own rays in a dispatch of its own,
        // which must cap its sum as the trace and the upsample do.
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

        hdr.Should()
            .OnlyContain(value => value <= cap, "every pass that adds a reflection caps its sum at MaxRadiance");
    }

    [Theory]
    [MemberData(nameof(Resolutions))]
    public void TwoRendersGiveTheSameBits(TraceResolution resolution)
    {
        float[] first = RenderHdr(
            UnfadedReflectionsAt(resolution),
            renderer => BoxAbove(renderer, Mirror(51 / 255f))
        ).Hdr;
        float[] second = RenderHdr(
            UnfadedReflectionsAt(resolution),
            renderer => BoxAbove(renderer, Mirror(51 / 255f))
        ).Hdr;

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
                .ContainInOrder(["composite", "radiance pyramid", "reflections", "transparent"]);
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
                .ContainInOrder(["composite", "radiance pyramid", "reflection trace", "reflections", "transparent"]);
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
    public void AtHalfResolutionEachTexelHoldsTheRayOfTheTopLeftPixelOfItsBlock()
    {
        float[] full = RenderHdr(ReflectionsAt(TraceResolution.Full), StripedWall).Hdr;
        (float[] traced, int tracedWidth, int tracedHeight) = RenderTrace(StripedWall);

        // The blocks below the row where the wall meets the floor show only the floor. From there to 9 m ahead, the rays
        // run less than half as far as the wall's distance from the eye, so the half trace's cone stays under one pixel
        // and reads level 0, as the full trace does.
        int firstFloorRow = PixelOf(new(0, FloorHeight, -15)).Y / 2 + 1,
            lastFloorRow = PixelOf(new(0, FloorHeight, -9)).Y / 2;
        int compared = 0,
            atAnEdge = 0;
        for (int j = firstFloorRow; j <= lastFloorRow; j++)
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

    [Fact]
    public void AtHalfResolutionAMirrorSlabKeepsItsReflectionApartFromTheFloorBehindIt()
    {
        // The far edge of the slab splits the blocks of 2 × 2 pixels of its row, so a texel of the trace stands for both
        // the slab and the floor behind it. Both face up, so only their depths tell them apart.
        float top = ToAnOddPixelBoundary(
            start: -1,
            projected: height => Gpu.Project(point: new(0, height, -6), width: Width, height: Height).Y
        );
        (float[] Full, float[] Half) renders = RenderAtBothResolutions(renderer => MirrorSlab(renderer, top));

        (int X, int Y) edge = PixelOf(new(0, top, -6));
        At(renders.Full, (edge.X, edge.Y + 3)).Y.Should().BeGreaterThan(Bright / 4, "the slab shows the green half");
        At(renders.Full, (edge.X, edge.Y - 3))
            .X.Should()
            .BeGreaterThan(Bright / 4, "the floor behind it shows the red half");
        ShouldMatchAtHalfResolution(
            renders: renders,
            pixels: Around(center: edge, dxs: [-1, 0, 1], dys: [-3, -2, -1, 0, 1, 2, 3]),
            shows: "by the slab's far edge shows its own surface's reflection"
        );
    }

    [Fact]
    public void AtHalfResolutionTheTwoFacesOfAMirrorCornerKeepTheirReflectionsApart()
    {
        // The corner splits the blocks of 2 × 2 pixels of its column. Texels across it lie near the other face's plane,
        // but face another way.
        float x = ToAnOddPixelBoundary(
            start: 0,
            projected: across => Gpu.Project(point: new(across, -0.5f, -6), width: Width, height: Height).X
        );
        (float[] Full, float[] Half) renders = RenderAtBothResolutions(renderer => MirrorCorner(renderer, x));

        (int X, int Y) corner = PixelOf(new(x, -0.5f, -6));
        At(renders.Full, (corner.X - 4, corner.Y))
            .X.Should()
            .BeGreaterThan(Bright / 4, "the left face shows the red wall");
        At(renders.Full, (corner.X + 4, corner.Y))
            .Y.Should()
            .BeGreaterThan(Bright / 4, "the right face shows the green wall");
        ShouldMatchAtHalfResolution(
            renders: renders,
            pixels: Around(center: corner, dxs: [-3, -2, -1, 0, 1, 2, 3], dys: [-6, -3, 0, 3, 6]),
            shows: "by the corner shows its own face's reflection"
        );
    }

    [Fact]
    public void AMirrorFloorReflectsAsSharplyInItsLastPixelBesideARoughFloorAsInsideIt()
    {
        // The last mirror pixel's next pixel lies on the rough floor, whose d leans far toward the normal. Its footprint
        // takes the step back onto the mirror, so its cone stays the mirror's own.
        float[] hdr = RenderHdr(ReflectionsAt(TraceResolution.Full), MirrorBesideRoughFloor).Hdr;

        foreach (float ahead in (float[])[-4, -6, -9])
        {
            (int X, int Y) seam = PixelOf(new(0, FloorHeight, ahead));
            (int X, int Y) last = (seam.X - 1, seam.Y),
                inside = (seam.X - 4, seam.Y);
            BrdfTesting
                .LargestChannelDifference(At(hdr, last), At(hdr, inside))
                .Should()
                .BeLessThanOrEqualTo(
                    0.05f * Bright,
                    "the last mirror pixel {0} shows what the mirror shows at {1}: {2} and {3}",
                    last,
                    inside,
                    At(hdr, last),
                    At(hdr, inside)
                );
        }
    }

    [Fact]
    public void AtHalfResolutionAMirrorFloorBesideARoughFloorReflectsAsAtFullResolutionUpToTheirSeam()
    {
        // The texels across the seam lie on the mirror's plane but start no ray.
        (float[] Full, float[] Half) renders = RenderAtBothResolutions(MirrorBesideRoughFloor);

        foreach (float ahead in (float[])[-4, -6, -9])
        {
            (int X, int Y) seam = PixelOf(new(0, FloorHeight, ahead));
            BrdfTesting
                .LargestChannelDifference(At(renders.Full, (seam.X - 6, seam.Y)), Vector3.Zero)
                .Should()
                .BeGreaterThan(Bright / 4, "the mirror shows the wall {0} m ahead", -ahead);
            ShouldMatchAtHalfResolution(
                renders: renders,
                pixels: Around(center: seam, dxs: [-3, -2, -1, 0], dys: [0]),
                shows: "of the mirror at the seam takes only the texels with a ray"
            );
        }
    }

    [Fact]
    public void AtHalfResolutionAMirrorFloorReflectsASlantedEdgeAlongAStraightLineWithNoSteps()
    {
        float[] hdr = RenderHdr(ReflectionsAt(TraceResolution.Half), SlantedEdgeFarAhead).Hdr;

        // The floor from 4 m to 9 m ahead reflects the slab's edge 5 m to 14 m up, where the rays run far, so the gap
        // between neighboring rays at the hit is the largest that a flat mirror has.
        int farRow = PixelOf(new(0, FloorHeight, -9)).Y,
            nearRow = PixelOf(new(0, FloorHeight, -4)).Y;
        int dark = PixelOf(new(-15, 0, -40)).X,
            lit = PixelOf(new(5, 0, -40)).X;
        (float Y, float X)[] crossings =
        [
            .. Enumerable
                .Range(farRow, nearRow - farRow + 1)
                .Select(y => (Y: (float)y, X: HalfCrossing(Row(hdr, y), outside: dark, plateau: lit))),
        ];

        LargestDistanceFromTheirLine(crossings)
            .Should()
            .BeLessThan(
                0.4f,
                "the reflected edge is a ramp along a straight line, not steps, at x = {0}",
                string.Join(", ", crossings.Select(crossing => $"{crossing.X:F2}"))
            );
    }

    [Fact]
    public void AtHalfResolutionTheReflectedOutlineOfABoxBeforeAFartherWallMatchesTheFullResolutionTrace()
    {
        // The box stands 3 m before the wall, far past the trace's slab of a tenth of the depth, so the rays on either side
        // of its reflected outline hit different surfaces. The floor from 9.5 m to 11.5 m ahead reflects the box's lower
        // half, and its rays run less than half as far as their hits lie from the eye, so both traces' cones read level 0.
        (float[] Full, float[] Half) renders = RenderAtBothResolutions(renderer =>
            BoxBeforeAFartherWall(renderer, split: 6)
        );

        int farRow = PixelOf(new(0, FloorHeight, -11.5f)).Y,
            nearRow = PixelOf(new(0, FloorHeight, -9.5f)).Y;
        int left = MirrorPixel(new(-1, -1.3f, -12)).X,
            right = MirrorPixel(new(1, -1.3f, -12)).X;
        int middle = (farRow + nearRow) / 2;
        At(renders.Full, (left + 3, middle)).X.Should().BeGreaterThan(Bright / 4, "the floor shows the red box");
        At(renders.Full, (left - 3, middle)).Y.Should().BeGreaterThan(Bright / 4, "beside it, the green wall");
        int[] rows = [.. Enumerable.Range(farRow, nearRow - farRow + 1)];
        int[] across = [-3, -2, -1, 0, 1, 2, 3];
        ShouldMatchAtHalfResolution(
            renders: renders,
            pixels: Around(center: (left, 0), dxs: across, dys: rows)
                .Concat(Around(center: (right, 0), dxs: across, dys: rows)),
            shows: "on the box's reflected outline shows the box or the wall as the full-resolution trace does"
        );
    }

    [Fact]
    public void AtHalfResolutionAColorEdgeOnOneReflectedSurfaceKeepsTheMeanOfItsTexelsRays()
    {
        // The wall turns from green to blue at an odd pixel boundary of the reflection, so the odd pixel right of it lies
        // between a texel whose ray hits green and one whose ray hits blue. Both hit the wall at one depth: they agree, and
        // the pixel takes their mean, where the full-resolution trace shows blue.
        float split = ToAnOddPixelBoundary(
            start: 4,
            projected: across => Gpu.Project(point: new(across, 0, -15), width: Width, height: Height).X
        );
        (float[] Full, float[] Half) renders = RenderAtBothResolutions(renderer =>
            BoxBeforeAFartherWall(renderer, split)
        );

        int edge = (int)MathF.Round(Gpu.Project(point: new(split, 0, -15), width: Width, height: Height).X);
        foreach (float ahead in (float[])[-9.5f, -10.5f, -11.5f])
        {
            (int X, int Y) between = (edge, PixelOf(new(0, FloorHeight, ahead)).Y);
            Vector3 full = At(renders.Full, between),
                mean = At(renders.Half, between);
            full.Z.Should().BeGreaterThan(Bright / 4, "the full trace shows the blue half at {0}", between);
            mean.Z.Should().BeGreaterThan(Bright / 4, "pixel {0} takes the blue texel's half: {1}", between, mean);
            mean.Y.Should()
                .BeGreaterThan(
                    full.Y + Bright / 4,
                    "pixel {0} takes the green texel's half, which its own ray does not see: {1} against {2}",
                    between,
                    mean,
                    full
                );
        }
    }

    /// <summary>
    /// A white mirror floor in the dark before a red box 2 m wide, 2 m tall and 0.5 m deep, standing on the floor with its
    /// front 12 m ahead, and a wall 15 m ahead, green left of <paramref name="split"/> and blue right of it.
    /// </summary>
    private static RenderSnapshot BoxBeforeAFartherWall(VulkanRenderer renderer, float split)
    {
        RenderSnapshot snapshot = Dark();
        snapshot.Instances.Add(Floor(renderer, Mirror()));
        MeshId box = renderer.CreateMesh(Primitives.Box(new(0.5f)));
        snapshot.Instances.Add(
            Block(
                box: box,
                material: Glow(renderer, new(Bright, 0, 0)),
                center: new(0, FloorHeight + 1, -12.25f),
                size: new(2, 2, 0.5f)
            )
        );
        snapshot.Instances.Add(
            Block(
                box: box,
                material: Glow(renderer, new(0, Bright, 0)),
                center: new((split - 30) / 2, 2, -15.5f),
                size: new(split + 30, 7, 1)
            )
        );
        snapshot.Instances.Add(
            Block(
                box: box,
                material: Glow(renderer, new(0, 0, Bright)),
                center: new((split + 30) / 2, 2, -15.5f),
                size: new(30 - split, 7, 1)
            )
        );
        return snapshot;
    }

    /// <summary>
    /// A white mirror floor in the dark before a wall of red and green stripes 0.3 m wide, 15 m ahead. The white metal
    /// weighs each channel alike and the dark adds nothing, so the red share of a floor pixel's HDR value is the red
    /// share of its ray. Along the stripes' edges, the share changes from one pixel to the next.
    /// </summary>
    private static RenderSnapshot StripedWall(VulkanRenderer renderer)
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

    /// <summary>
    /// A white mirror floor in the dark before a bright slab 40 m ahead, 20 m wide, 40 m tall and 10 cm thick, turned by 20° about the
    /// view axis, so that its left edge leans to the left as it rises, before a black wall.
    /// </summary>
    private static RenderSnapshot SlantedEdgeFarAhead(VulkanRenderer renderer)
    {
        RenderSnapshot snapshot = Dark();
        snapshot.Instances.Add(Floor(renderer, Mirror()));
        MeshId box = renderer.CreateMesh(Primitives.Box(new(0.5f)));
        snapshot.Instances.Add(
            Gpu.Instance(
                mesh: box,
                material: Glow(renderer, new(Bright)),
                position: new(8, 8, -40.05f),
                scale: new(20, 40, 0.1f),
                rotation: Quaternion.CreateFromAxisAngle(Vector3.UnitZ, 20 * MathF.PI / 180),
                radius: 23,
                chainDepth: TestChains.CamerasFrame
            )
        );
        snapshot.Instances.Add(
            Block(box: box, material: Glow(renderer, Vector3.Zero), center: new(0, 20, -40.6f), size: new(200, 60, 1))
        );
        return snapshot;
    }

    /// <summary>The largest horizontal distance of <paramref name="points"/> from their least-squares line x = a + b · y, in pixels.</summary>
    private static float LargestDistanceFromTheirLine(IReadOnlyList<(float Y, float X)> points)
    {
        double meanY = points.Average(point => point.Y),
            meanX = points.Average(point => point.X);
        double slope =
            points.Sum(point => (point.Y - meanY) * (point.X - meanX))
            / points.Sum(point => (point.Y - meanY) * (point.Y - meanY));
        return (float)points.Max(point => Math.Abs(point.X - (meanX + slope * (point.Y - meanY))));
    }

    /// <summary>
    /// Where <paramref name="values"/> first reaches half its value at <paramref name="plateau"/>, scanning from
    /// <paramref name="outside"/> toward it, in pixels with linear interpolation between pixel centers.
    /// </summary>
    private static float HalfCrossing(IReadOnlyList<float> values, int outside, int plateau)
    {
        float half = values[plateau] / 2;
        int direction = Math.Sign(plateau - outside);
        for (int x = outside + direction; x != plateau + direction; x += direction)
            if (values[x] >= half)
            {
                float before = values[x - direction];
                return x - direction + direction * (half - before) / (values[x] - before);
            }

        throw new InvalidOperationException($"The values never reach half of {values[plateau]}.");
    }

    /// <summary>The share of red in the red and green of <paramref name="radiance"/>.</summary>
    private static float RedShare(Vector3 radiance) => radiance.X / (radiance.X + radiance.Y);

    /// <summary>
    /// A mirror slab half a metre above the mirror floor, its top at <paramref name="top"/> and its far edge 6 m ahead,
    /// before a wall red below the eye's height and green above it. At the far edge's row, the slab reflects the green
    /// half, and the floor 9 m ahead behind it the red half.
    /// </summary>
    private static RenderSnapshot MirrorSlab(VulkanRenderer renderer, float top)
    {
        RenderSnapshot snapshot = Dark();
        snapshot.Instances.Add(Floor(renderer, Mirror()));
        MeshId box = renderer.CreateMesh(Primitives.Box(new(0.5f)));
        MaterialId red = Glow(renderer, new(Bright, 0, 0));
        MaterialId green = Glow(renderer, new(0, Bright, 0));
        snapshot.Instances.Add(
            Block(
                box: box,
                material: renderer.CreateMaterial(Mirror()),
                center: new(0, top - 0.05f, -5),
                size: new(2, 0.1f, 2)
            )
        );
        snapshot.Instances.Add(Block(box: box, material: red, center: new(0, -0.75f, -15.5f), size: new(60, 1.5f, 1)));
        snapshot.Instances.Add(Block(box: box, material: green, center: new(0, 2.5f, -15.5f), size: new(60, 5, 1)));
        return snapshot;
    }

    /// <summary>
    /// A mirror box turned by 45° about the vertical, whose corner nearest the eye stands at <paramref name="x"/>, 6 m
    /// ahead, between a red wall at the left and a green wall at the right. Its left face reflects the red wall, and its
    /// right face the green wall.
    /// </summary>
    private static RenderSnapshot MirrorCorner(VulkanRenderer renderer, float x)
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
                radius: 1,
                chainDepth: TestChains.CamerasFrame
            )
        );
        MaterialId red = Glow(renderer, new(Bright, 0, 0));
        MaterialId green = Glow(renderer, new(0, Bright, 0));
        snapshot.Instances.Add(Block(box: box, material: red, center: new(-4.5f, 0, -7), size: new(1, 6, 10)));
        snapshot.Instances.Add(Block(box: box, material: green, center: new(4.5f, 0, -7), size: new(1, 6, 10)));
        return snapshot;
    }

    /// <summary>
    /// The floor's left half a mirror and its right half of roughness 1, at or above every valid maximum, both at one
    /// height, before a bright wall.
    /// </summary>
    private static RenderSnapshot MirrorBesideRoughFloor(VulkanRenderer renderer)
    {
        RenderSnapshot snapshot = Dark();
        MeshId plane = renderer.CreateMesh(Primitives.Plane(1));
        foreach (
            (float center, MaterialDesc material) in ((float Center, MaterialDesc Material)[])
                [(Center: -15, Material: Mirror()), (Center: 15, Material: Mirror(1))]
        )
            snapshot.Instances.Add(
                Gpu.Instance(
                    mesh: plane,
                    material: renderer.CreateMaterial(material with { NormalMap = FlatNormalMap(renderer) }),
                    position: new(center, FloorHeight, -20),
                    scale: new(15, 1, 30),
                    radius: 34,
                    chainDepth: TestChains.CamerasFrame
                )
            );
        MeshId box = renderer.CreateMesh(Primitives.Box(new(0.5f)));
        snapshot.Instances.Add(
            Block(box: box, material: Glow(renderer, new(Bright)), center: new(0, 2, -15.5f), size: new(60, 7, 1))
        );
        return snapshot;
    }

    /// <summary>The HDR images of the scene that <paramref name="scene"/> builds, traced at full and at half resolution.</summary>
    private static (float[] Full, float[] Half) RenderAtBothResolutions(Func<VulkanRenderer, RenderSnapshot> scene) =>
        (
            Full: RenderHdr(ReflectionsAt(TraceResolution.Full), scene).Hdr,
            Half: RenderHdr(ReflectionsAt(TraceResolution.Half), scene).Hdr
        );

    /// <summary>The half-resolution trace of the scene that <paramref name="scene"/> builds, read after one render.</summary>
    private static (float[] Rgba, int Width, int Height) RenderTrace(Func<VulkanRenderer, RenderSnapshot> scene)
    {
        VulkanRenderer renderer = Headless(ReflectionsAt(TraceResolution.Half));
        try
        {
            renderer.Render(scene(renderer), 1f);
            return renderer.ReadReflectionTrace();
        }
        finally
        {
            Gpu.DisposeAndAssertClean(renderer);
        }
    }

    [Theory]
    [MemberData(nameof(Resolutions))]
    public void WithTheEnvironmentMapOnAHitReplacesTheMapsSpecularTerm(TraceResolution resolution)
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

        (float[] with, CameraView camera) = RenderUnderMap(reflections: ReflectionsAt(resolution), scene: Scene);
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

    [Theory]
    [MemberData(nameof(Resolutions))]
    public void ACurvedMirrorThatReflectsItsNeighborDoesNotChangeWhenTheStarsTurn(TraceResolution resolution)
    {
        // The rays of the mirror ball's right side hit the glowing ball on the screen, but the map, seen from the eye,
        // sees open sky along them. The mirror's d moves by several star cells per pixel, so the ambient pass adds the
        // stars' mean, which a quarter turn of the root axes keeps. A hit that took away single stars in its place would
        // leave their dips in the reflection, and the dips would move with the stars.
        float[] lit = RenderUnderMap(
            reflections: new NoReflections(),
            scene: MirrorBesideGlow(Quaternion.Identity)
        ).Hdr;
        float[] reflected = RenderUnderMap(
            reflections: ReflectionsAt(resolution),
            scene: MirrorBesideGlow(Quaternion.Identity)
        ).Hdr;
        (int X, int Y)[] mirror = MirrorBallPixels();
        mirror
            .Count(pixel => At(reflected, pixel).Y > At(lit, pixel).Y + 0.5f)
            .Should()
            .BeGreaterThan(200, "the mirror's right side reflects the glowing ball through its rays' hits");

        foreach (
            Quaternion turn in (Quaternion[])
                [
                    Quaternion.CreateFromAxisAngle(Vector3.UnitY, MathF.PI / 2),
                    Quaternion.CreateFromAxisAngle(Vector3.UnitX, MathF.PI / 2),
                ]
        )
        {
            float[] turned = RenderUnderMap(reflections: ReflectionsAt(resolution), scene: MirrorBesideGlow(turn)).Hdr;
            (int X, int Y)[] differing =
            [
                .. mirror.Where(pixel =>
                    BrdfTesting.LargestChannelDifference(At(turned, pixel), At(reflected, pixel))
                    > At(reflected, pixel).Y / 1024
                ),
            ];
            differing
                .Should()
                .BeEmpty(
                    "the trace takes away the term that the ambient pass added, whatever the stars along d; first {0}: {1} and {2}",
                    differing.FirstOrDefault(),
                    At(turned, differing.FirstOrDefault()),
                    At(reflected, differing.FirstOrDefault())
                );
        }
    }

    [Theory]
    [MemberData(nameof(Resolutions))]
    public void ACurvedMirrorThatReflectsABlackNeighborDoesNotChangeWhenTheStarsTurn(TraceResolution resolution)
    {
        // The neighbor adds no light, so where the mirror ball's rays hit it, the mirror shows the stars' mean that the
        // ambient pass added, less the share that the hits take away. That share must be the very term that the ambient
        // pass added, with the mirror's footprint f: a term of single stars, as with the footprint p, would leave their
        // dips, which move with the stars, and no bright reflection would hide them.
        float[] lit = RenderUnderMap(
            reflections: new NoReflections(),
            scene: MirrorBesideGlow(Quaternion.Identity, glow: 0)
        ).Hdr;
        float[] reflected = RenderUnderMap(
            reflections: ReflectionsAt(resolution),
            scene: MirrorBesideGlow(Quaternion.Identity, glow: 0)
        ).Hdr;
        (int X, int Y)[] mirror = MirrorBallPixels();
        mirror
            .Count(pixel => At(reflected, pixel).Y < 0.5f * At(lit, pixel).Y)
            .Should()
            .BeGreaterThan(200, "the mirror's right side reflects the black ball through its rays' hits");

        foreach (
            Quaternion turn in (Quaternion[])
                [
                    Quaternion.CreateFromAxisAngle(Vector3.UnitY, MathF.PI / 2),
                    Quaternion.CreateFromAxisAngle(Vector3.UnitX, MathF.PI / 2),
                ]
        )
        {
            float[] turned = RenderUnderMap(
                reflections: ReflectionsAt(resolution),
                scene: MirrorBesideGlow(turn, glow: 0)
            ).Hdr;
            (int X, int Y)[] differing =
            [
                .. mirror.Where(pixel =>
                    BrdfTesting.LargestChannelDifference(At(turned, pixel), At(reflected, pixel))
                    > At(lit, pixel).Y / 1024
                ),
            ];
            differing
                .Should()
                .BeEmpty(
                    "the trace takes away the term that the ambient pass added, whatever the stars along d; first {0}: {1} and {2}",
                    differing.FirstOrDefault(),
                    At(turned, differing.FirstOrDefault()),
                    At(reflected, differing.FirstOrDefault())
                );
        }
    }

    [Theory]
    [MemberData(nameof(Resolutions))]
    public void ACurvedMirrorReflectsAFineGratingAsItsMeanWhereItsFootprintSpansTheBars(TraceResolution resolution)
    {
        // The mirror ball's d turns by many pixel angles to the next pixel, so each pixel's cone reads the pyramid at the
        // level of its footprint f, where the grating's bars, about 2 px apart on the screen, have blended into their
        // mean. A cone of the footprint p would read single bars, a bright or a dim one from pixel to pixel.
        float[] hdr = RenderHdr(ReflectionsAt(resolution), MirrorBesideGrating).Hdr;
        HashSet<(int X, int Y)> reflecting = [.. MirrorBallPixels().Where(pixel => At(hdr, pixel).Y > 0.05f * Bright)];
        var steps = new List<float>();
        foreach ((int X, int Y) pixel in reflecting)
        {
            (int X, int Y) right = (X: pixel.X + 1, pixel.Y);
            if (!reflecting.Contains(right))
                continue;
            float here = At(hdr, pixel).Y,
                there = At(hdr, right).Y;
            steps.Add(2 * Math.Abs(here - there) / (here + there));
        }

        steps.Count.Should().BeGreaterThan(1000, "the mirror's right side reflects the grating");
        // Measured on 2026-11-01: 0.05 at full and 0.09 at half resolution, and 0.41 and 1.07 with the cone of p.
        steps
            .Average()
            .Should()
            .BeLessThan(
                0.2f,
                "the step between neighbors is the slow change of the grating's blurred image, not a bar's"
            );
    }

    /// <summary>
    /// A white mirror ball of 1 m radius at <see cref="MirrorBall"/> in the dark, and to its right a grating 2 m tall
    /// that runs 4 m along the view, from 2 m to 6 m ahead: 80 bars 5 cm wide, which glow <see cref="Bright"/> and a tenth
    /// of it in turn and face the ball.
    /// </summary>
    private static RenderSnapshot MirrorBesideGrating(VulkanRenderer renderer)
    {
        RenderSnapshot snapshot = Dark();
        snapshot.Instances.Add(
            Gpu.Instance(
                mesh: renderer.CreateMesh(Primitives.Sphere(1f, 64, 32)),
                material: renderer.CreateMaterial(Mirror()),
                position: MirrorBall,
                scale: Vector3.One,
                chainDepth: TestChains.CamerasFrame
            )
        );
        MeshId box = renderer.CreateMesh(Primitives.Box(new(0.5f)));
        MaterialId bright = Glow(renderer, new(Bright));
        MaterialId dim = Glow(renderer, new(Bright / 10));
        const float bar = 0.05f;
        for (int i = 0; i < 80; i++)
            snapshot.Instances.Add(
                Block(
                    box: box,
                    material: i % 2 == 0 ? bright : dim,
                    center: new(2.05f, 0, -6 + bar * (i + 0.5f)),
                    size: new(0.1f, 2, bar)
                )
            );
        return snapshot;
    }

    /// <summary>
    /// A white mirror ball of 1 m radius at <see cref="MirrorBall"/> and a ball of the same size that glows
    /// <paramref name="glow"/> 2.5 m to its right, in the dark, with the root axes turned by <paramref name="root"/>.
    /// </summary>
    private static Func<VulkanRenderer, RenderSnapshot> MirrorBesideGlow(Quaternion root, float glow = Bright) =>
        renderer =>
        {
            RenderSnapshot snapshot = Dark();
            TestChains.UnderTurnedRoot(snapshot, root);
            MeshId sphere = renderer.CreateMesh(Primitives.Sphere(1f, 64, 32));
            snapshot.Instances.Add(
                Gpu.Instance(
                    mesh: sphere,
                    material: renderer.CreateMaterial(Mirror()),
                    position: MirrorBall,
                    scale: Vector3.One,
                    chainDepth: TestChains.CamerasFrame
                )
            );
            snapshot.Instances.Add(
                Gpu.Instance(
                    mesh: sphere,
                    material: Glow(renderer, new(glow)),
                    position: MirrorBall + new Vector3(2.5f, 0, 0),
                    scale: Vector3.One,
                    chainDepth: TestChains.CamerasFrame
                )
            );
            return snapshot;
        };

    /// <summary>
    /// The pixels of the mirror ball of <see cref="MirrorBesideGlow"/>. The ball spans asin(1 / 4) from its center, and the
    /// inner 90 % of that lies on its mesh.
    /// </summary>
    private static (int X, int Y)[] MirrorBallPixels()
    {
        (int X, int Y) center = PixelOf(MirrorBall);
        float radius = 0.9f * MathF.Tan(MathF.Asin(0.25f)) * (Height / 2f) / MathF.Tan(Gpu.Fov / 2);
        return
        [
            .. from y in Enumerable.Range(center.Y - (int)radius, 2 * (int)radius + 1)
            from x in Enumerable.Range(center.X - (int)radius, 2 * (int)radius + 1)
            where (x - center.X) * (x - center.X) + (y - center.Y) * (y - center.Y) <= radius * radius
            select (x, y),
        ];
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

    /// <summary>
    /// Each of <paramref name="pixels"/> of the half-resolution render shows what the full-resolution render shows there,
    /// within 5 % of <see cref="Bright"/>.
    /// </summary>
    private static void ShouldMatchAtHalfResolution(
        (float[] Full, float[] Half) renders,
        IEnumerable<(int X, int Y)> pixels,
        string shows
    )
    {
        foreach ((int X, int Y) pixel in pixels)
            BrdfTesting
                .LargestChannelDifference(At(renders.Half, pixel), At(renders.Full, pixel))
                .Should()
                .BeLessThanOrEqualTo(
                    0.05f * Bright,
                    "pixel {0} {1}: {2} at half, {3} at full",
                    pixel,
                    shows,
                    At(renders.Half, pixel),
                    At(renders.Full, pixel)
                );
    }

    /// <summary>The pixels at the offsets <paramref name="dxs"/> × <paramref name="dys"/> from <paramref name="center"/>.</summary>
    private static IEnumerable<(int X, int Y)> Around((int X, int Y) center, int[] dxs, int[] dys) =>
        from dy in dys
        from dx in dxs
        select (center.X + dx, center.Y + dy);

    /// <summary>Renders the scene that <paramref name="scene"/> builds once at 640 × 360 and 0 stops, under GTAO's default.</summary>
    /// <returns>The frame's HDR image, the occlusion V of each pixel and its camera.</returns>
    private static (float[] Hdr, float[] Occlusion, CameraView Camera) RenderUnderOcclusion(
        ReflectionSetting reflections,
        Func<VulkanRenderer, RenderSnapshot> scene
    )
    {
        VulkanRenderer renderer = Gpu.Headless(
            screenSpaceLight: VisibilityBitmaskLight.Default,
            exposure: new FixedExposure(0),
            reflections: reflections
        );
        try
        {
            renderer.Render(scene(renderer), 1f);
            return (renderer.ReadHdr(), OcclusionScenes.ReadOcclusion(renderer), renderer.LastScene.Camera);
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
    /// The share of the full reflection that <paramref name="shown"/> adds: the least-squares scale s of
    /// shown − without ≈ s · (full − without) over every value of the images.
    /// </summary>
    private static float ShareOf(float[] shown, float[] full, float[] without)
    {
        double product = 0,
            square = 0;
        for (int i = 0; i < shown.Length; i++)
        {
            double reflection = full[i] - without[i];
            product += (shown[i] - without[i]) * reflection;
            square += reflection * reflection;
        }

        square.Should().BePositive("the full reflection adds light");
        return (float)(product / square);
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
