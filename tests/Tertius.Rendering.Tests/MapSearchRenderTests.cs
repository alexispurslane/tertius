using System.Numerics;
using Silk.NET.Vulkan;
using Tertius.Testing;

namespace Tertius.Rendering.Tests;

/// <summary>
/// The corrected read (docs/plans/environment-map-distances.md, "The corrected read, on the GPU"): every reader of the
/// ambient specular term reads its map where the lobe's central ray ends, and a ray that meets no stored surface reads
/// the map as before. The camera sits at the origin and looks down −Z, a floor lies 1 m below it, and a wall stands 4 m
/// ahead, facing it. Only emission and the map light the scenes. The screen tracer is off unless a test names it.
/// </summary>
[Collection(GpuCollection.Name)]
public sealed class MapSearchRenderTests
{
    private const int Width = 320,
        Height = 180;

    /// <summary>The map of every test: small, so a level-0 texel spans <see cref="TexelAngle"/>.</summary>
    private static readonly SceneEnvironmentMap SmallMap = SceneEnvironmentMap.Default with { FaceSize = 32 };

    /// <summary>2 / W: the angle of a level-0 texel of <see cref="SmallMap"/> at a face's center, in rad.</summary>
    private const float TexelAngle = 2f / 32;

    /// <summary>The height of the floor's top, in m.</summary>
    private const float FloorHeight = -1;

    /// <summary>Where the wall's face stands, in m along −Z.</summary>
    private const float WallZ = -4;

    /// <summary>A square of the plane primitive, which faces +Y, turned to face +Z, toward the camera from ahead of it.</summary>
    private static readonly Quaternion FacingPlusZ = Quaternion.CreateFromAxisAngle(Vector3.UnitX, MathF.PI / 2);

    /// <summary>The columns where the tests compare a floor's reflection with a ray trace: the middle and both sides.</summary>
    private static readonly int[] Columns = [100, 160, 220];

    public static TheoryData<string> OpenSpaceViews => ["floor", "panel"];

    /// <param name="view">"floor", a mirror floor lit by the sun, or "panel", a turned rough metal panel lit by the sun.</param>
    [Theory]
    [MemberData(nameof(OpenSpaceViews))]
    public void AMapAtInfinityGivesTodaysImageExactly(string view)
    {
        float[] corrected = RenderOpenSpace(view, build: null);
        float[] directionOnly = RenderOpenSpace(view, build: "direction only");

        (int X, int Y)[] differing =
        [
            .. Enumerable
                .Range(0, corrected.Length)
                .Where(i =>
                    BitConverter.SingleToInt32Bits(corrected[i]) != BitConverter.SingleToInt32Bits(directionOnly[i])
                )
                .Select(i => (X: i / 4 % Width, Y: i / 4 / Width))
                .Distinct(),
        ];
        differing.Should().BeEmpty("no ray meets a stored surface, so each reads its map at d with r, bit for bit");
        corrected.Distinct().Count().Should().BeGreaterThan(1000, "the view reflects the stars, not one flat color");
    }

    [Fact]
    public void AMirrorFloorShowsANearWallsStripeWhereARayTraceFindsIt()
    {
        (float[] hdr, CameraView camera) = Render(scene: renderer => StripeAboveAFloor(renderer, glass: false));

        foreach (int column in Columns)
            StripeAngle(hdr, camera, column)
                .Should()
                .BeLessThan(
                    TexelAngle,
                    "the floor reflects the stripe where the floor's rays meet it, in column {0}",
                    column
                );
    }

    [Fact]
    public void AGlassPaneReflectsTheNearWallWhereARayTraceFindsIt()
    {
        (float[] hdr, CameraView camera) = Render(scene: renderer => StripeAboveAFloor(renderer, glass: true));

        foreach (int column in Columns)
            StripeAngle(hdr, camera, column)
                .Should()
                .BeLessThan(
                    TexelAngle,
                    "the pane reflects the stripe where the pane's rays meet it, in column {0}",
                    column
                );
    }

    [Fact]
    public void AReflectionThatLeavesTheScreenFindsTheStripeBehindTheCamera()
    {
        VulkanRenderer renderer = Renderer(ScreenTrace.Default, new FixedExposure(0));
        try
        {
            RenderSnapshot snapshot = MirrorFacingTheCamera(renderer);
            for (int frame = 0; frame < 8; frame++)
            {
                snapshot.Time += 1.0 / 60;
                renderer.Render(snapshot, 1f);
            }
            float[] hdr = renderer.ReadHdr();
            foreach (int column in Columns)
            {
                int row = Enumerable.Range(0, Height).MaxBy(row => ReflectionTesting.At(hdr, (column, row), Width).X);
                ReflectionTesting.At(hdr, (column, row), Width).X.Should().BeGreaterThan(0.5f);
                Vector3 ray = renderer.LastScene.Camera.ViewRayThrough(new(column + 0.5f, row + 0.5f));
                Vector3 point = ray * (WallZ / ray.Z);
                var direction = Vector3.Reflect(Vector3.Normalize(ray), Vector3.UnitZ);
                Vector3 met = point + direction * ((-WallZ - point.Z) / direction.Z);
                var stripe = new Vector3(met.X, Math.Clamp(met.Y, 1.35f, 1.65f), -WallZ);
                float angle = MathF.Acos(
                    Math.Clamp(Vector3.Dot(Vector3.Normalize(met), Vector3.Normalize(stripe)), -1, 1)
                );
                angle.Should().BeLessThan(TexelAngle, "the off-screen stripe is placed by the receiving point's ray");
            }
        }
        finally
        {
            Gpu.DisposeAndAssertClean(renderer);
        }
    }

    [Fact]
    public void AStillAdvancesEveryWarmUpDrawAndContinuesItsReflectionHistoryOnTheNextTick()
    {
        VulkanRenderer renderer = Renderer(ScreenTrace.Default, new FixedExposure(0));
        try
        {
            var draws = new DrawHistoryPass();
            renderer.Graph.Add(FrameGraph.Stage.Composite, draws);
            RenderSnapshot snapshot = MirrorFacingTheCamera(renderer);
            renderer.Render(snapshot, 1f);
            float[] first = renderer.ReadImage(renderer.ScreenTraceSignals.Specular.Slow(0));
            draws.Indices.Should().Equal(Enumerable.Range(0, 33).Select(index => (long)index));
            draws.Resets.Count(reset => reset).Should().Be(1);
            first.Where((_, index) => index % 4 == 3).Max().Should().BeGreaterThan(1, "reflection history accumulated");

            snapshot.Time += 1.0 / 60;
            renderer.Render(snapshot, 1f);

            draws.Indices[^1].Should().Be(33);
            draws.Resets[^1].Should().BeFalse();
        }
        finally
        {
            Gpu.DisposeAndAssertClean(renderer);
        }
    }

    [Fact]
    public void ADiffuseMissKeepsItsDirectionalReadBesideANearWall()
    {
        VulkanRenderer renderer = Renderer(ScreenTrace.Default, new FixedExposure(0));
        try
        {
            var probe = ScreenTraceProbe.On(renderer, "map_diffuse_miss_probe.comp");
            RenderSnapshot snapshot = StripeAboveAFloor(renderer, glass: false);
            renderer.Render(snapshot, 1f);

            float[] actual = probe.Read();
            float[] directional = renderer.ReadImage(renderer.ScreenTraceSignals.Diffuse.Sample(0));
            actual.Should().Equal(directional, "a diffuse miss keeps the old direction and roughness 0.5");
            actual.Where((_, index) => index % 4 != 3).Max().Should().BeGreaterThan(0.1f);
        }
        finally
        {
            Gpu.DisposeAndAssertClean(renderer);
        }
    }

    [Fact]
    public void ARoughFloorBelowABrightOpeningIsBrightestWhereItsCentralRayMeetsTheOpening()
    {
        // The demo's paint, 0.38, which the G-buffer holds as 97 / 255. The opening's middle stands 2 m above the eye.
        const float roughness = 97 / 255f,
            openingHeight = 2,
            openingHalfHeight = 0.4f,
            openingHalfWidth = 1;
        (float[] hdr, CameraView camera) = Render(scene: renderer =>
        {
            RenderSnapshot snapshot = Dark(verticalFovDegrees: 90, pitchDown: 20 * MathF.PI / 180);
            AddFloor(renderer, snapshot, Metal(renderer, roughness));
            AddWall(renderer, snapshot);
            AddPanel(
                renderer: renderer,
                snapshot: snapshot,
                material: BrdfTesting.Glow(renderer, new(8)),
                center: new(0, openingHeight, WallZ + 0.01f),
                halfWidth: openingHalfWidth,
                halfHeight: openingHalfHeight
            );
            return snapshot;
        });

        int row = BrightestFloorRow(hdr, camera, Width / 2);
        Vector3 met = WallPointOf(camera, (Width / 2, row), roughness)!.Value;
        MathF
            .Abs(met.Y - openingHeight)
            .Should()
            .BeLessThan(openingHalfHeight, "the brightest floor pixel's central ray meets the opening, at {0}", met);
    }

    private static VulkanRenderer Renderer(ScreenTraceSetting screenTrace, ExposureSetting exposure) =>
        Gpu.Headless(
            width: Width,
            height: Height,
            exposure: exposure,
            screenTrace: screenTrace,
            environmentMap: SmallMap
        );

    private static RenderSnapshot MirrorFacingTheCamera(VulkanRenderer renderer)
    {
        RenderSnapshot snapshot = Dark(verticalFovDegrees: 70, pitchDown: 0);
        snapshot.Instances.Add(
            Gpu.Instance(
                mesh: renderer.CreateMesh(Primitives.Plane(1f)),
                material: BrdfTesting.Glow(renderer, Vector3.Zero),
                position: new(0, 0, -WallZ + 0.01f),
                scale: new(20, 1, 20),
                rotation: Quaternion.CreateFromAxisAngle(Vector3.UnitX, -MathF.PI / 2),
                chainDepth: TestChains.CamerasFrame
            )
        );
        AddPanel(
            renderer: renderer,
            snapshot: snapshot,
            material: Metal(renderer, roughness: 0),
            center: new(0, 0, WallZ),
            halfWidth: 6,
            halfHeight: 4
        );
        snapshot.Instances.Add(
            Gpu.Instance(
                mesh: renderer.CreateMesh(Primitives.Plane(1f)),
                material: BrdfTesting.Glow(renderer, new(8)),
                position: new(0, 1.5f, -WallZ),
                scale: new(6, 1, 0.15f),
                rotation: Quaternion.CreateFromAxisAngle(Vector3.UnitX, -MathF.PI / 2),
                chainDepth: TestChains.CamerasFrame
            )
        );
        return snapshot;
    }

    /// <summary>
    /// The HDR image of the scene's second draw, by the engine's ambient pass or, with <paramref name="build"/>, by
    /// that test build's, and the camera of the draw.
    /// </summary>
    private static (float[] Hdr, CameraView Camera) Render(
        Func<VulkanRenderer, RenderSnapshot> scene,
        ScreenTraceSetting screenTrace = default,
        ExposureSetting exposure = default,
        string? build = null
    )
    {
        VulkanRenderer renderer = Renderer(screenTrace, exposure);
        try
        {
            if (build is not null)
                DirectionOnlyComposite.Replace(renderer);
            RenderSnapshot snapshot = scene(renderer);
            snapshot.Time += 1.0 / 60;
            renderer.Render(snapshot, 1f);
            snapshot.Time += 1.0 / 60;
            renderer.Render(snapshot, 1f);
            return (renderer.ReadHdr(), renderer.LastScene.Camera);
        }
        finally
        {
            Gpu.DisposeAndAssertClean(renderer);
        }
    }

    /// <summary>
    /// A view with the map on in which no ray meets a stored surface: a mirror floor under the open, starry sky, or a
    /// rough metal panel in open space, turned 0.5 rad to the side, both lit by the test star. The exposure shows the
    /// stars. Each ray leaves its flat surface on the eye's side, so no ray passes behind a stored surface, nor through
    /// the skin that the filter gives a silhouette: a sphere's grazing rays do ("Known limits").
    /// </summary>
    private static float[] RenderOpenSpace(string view, string? build) =>
        Render(
            scene: renderer =>
            {
                RenderSnapshot snapshot = Gpu.Snapshot(Vector3.Normalize(new(0.3f, 0.6f, 0.5f)), starIrradiance: 2e-7f);
                snapshot.Ambient.Intensity = 0;
                if (view == "floor")
                {
                    snapshot.Camera.Rotation = Gpu.PitchDown(0.2f);
                    snapshot.Camera.PreviousRotation = snapshot.Camera.Rotation;
                    AddFloor(renderer, snapshot, Metal(renderer, roughness: 0));
                }
                else
                    snapshot.Instances.Add(
                        Gpu.Instance(
                            mesh: renderer.CreateMesh(Primitives.Plane(1.5f)),
                            material: Metal(renderer, roughness: 77 / 255f),
                            position: new(0, 0, -5),
                            scale: Vector3.One,
                            rotation: Quaternion.CreateFromAxisAngle(Vector3.UnitY, 0.5f) * FacingPlusZ,
                            chainDepth: TestChains.CamerasFrame
                        )
                    );
                return snapshot;
            },
            exposure: new FixedExposure(-MathF.Log2(StarField.UnitRadiance) + 6),
            build: build
        ).Hdr;

    /// <summary>
    /// A black wall with a bright horizontal stripe 1.5 m above the eye, beyond a mirror floor, or beyond a clear glass
    /// pane over a black floor. The direction-only read shows the stripe about 20° lower on the floor than the ray trace.
    /// </summary>
    private static RenderSnapshot StripeAboveAFloor(VulkanRenderer renderer, bool glass)
    {
        RenderSnapshot snapshot = Dark(verticalFovDegrees: 70, pitchDown: 15 * MathF.PI / 180);
        MaterialId black = BrdfTesting.Glow(renderer, Vector3.Zero);
        if (glass)
        {
            MaterialId pane = renderer.CreateMaterial(
                new(BaseColor: new(x: 1, y: 1, z: 1, w: 0), Roughness: 0, Reflectance: 0.5f, Alpha: new GlassAlpha())
            );
            AddFloor(renderer, snapshot, pane);
            snapshot.Instances.Add(
                Gpu.Instance(
                    mesh: renderer.CreateMesh(Primitives.Plane(20f)),
                    material: black,
                    position: new(0, FloorHeight - 0.05f, 0),
                    scale: Vector3.One,
                    chainDepth: TestChains.CamerasFrame
                )
            );
        }
        else
            AddFloor(renderer, snapshot, Metal(renderer, roughness: 0));
        AddWall(renderer, snapshot);
        AddPanel(
            renderer: renderer,
            snapshot: snapshot,
            material: BrdfTesting.Glow(renderer, new(8)),
            center: new(0, 1.5f, WallZ + 0.01f),
            halfWidth: 6,
            // 4° as the eye sees it, more than a texel of the map, so that the face draws it into a row of texels.
            halfHeight: 0.15f
        );
        return snapshot;
    }

    /// <summary>A snapshot with no light and no hemisphere light, from a camera turned down by <paramref name="pitchDown"/> rad.</summary>
    private static RenderSnapshot Dark(float verticalFovDegrees, float pitchDown)
    {
        RenderSnapshot snapshot = Gpu.Snapshot(Vector3.UnitY, starIrradiance: 0);
        snapshot.Ambient.Intensity = 0;
        snapshot.Camera.VerticalFov = verticalFovDegrees * MathF.PI / 180;
        snapshot.Camera.Rotation = Gpu.PitchDown(pitchDown);
        snapshot.Camera.PreviousRotation = snapshot.Camera.Rotation;
        return snapshot;
    }

    private static MaterialId Metal(VulkanRenderer renderer, float roughness) =>
        renderer.CreateMaterial(
            new(BaseColor: Vector4.One, Roughness: roughness, Model: new StandardShading(Metallic: 1))
        );

    /// <summary>A floor of <paramref name="material"/> 40 m wide at <see cref="FloorHeight"/>.</summary>
    private static void AddFloor(VulkanRenderer renderer, RenderSnapshot snapshot, MaterialId material) =>
        snapshot.Instances.Add(
            Gpu.Instance(
                mesh: renderer.CreateMesh(Primitives.Plane(20f)),
                material: material,
                position: new(0, FloorHeight, 0),
                scale: Vector3.One,
                chainDepth: TestChains.CamerasFrame
            )
        );

    /// <summary>A black wall 12 m wide from the floor to 4 m above the eye, at <see cref="WallZ"/>.</summary>
    private static void AddWall(VulkanRenderer renderer, RenderSnapshot snapshot) =>
        AddPanel(
            renderer: renderer,
            snapshot: snapshot,
            material: BrdfTesting.Glow(renderer, Vector3.Zero),
            center: new(0, 1.5f, WallZ),
            halfWidth: 6,
            halfHeight: 2.5f
        );

    /// <summary>A rectangle of <paramref name="material"/> around <paramref name="center"/> that faces the camera.</summary>
    private static void AddPanel(
        VulkanRenderer renderer,
        RenderSnapshot snapshot,
        MaterialId material,
        Vector3 center,
        float halfWidth,
        float halfHeight
    ) =>
        // The plane's local z becomes the world's −y once turned to face +Z.
        snapshot.Instances.Add(
            Gpu.Instance(
                mesh: renderer.CreateMesh(Primitives.Plane(1f)),
                material: material,
                position: center,
                scale: new(halfWidth, 1, halfHeight),
                rotation: FacingPlusZ,
                chainDepth: TestChains.CamerasFrame
            )
        );

    /// <summary>
    /// The floor point that a pixel shows in front of the wall, and the central ray of its lobe of
    /// <paramref name="roughness"/>, the specular dominant direction about the floor's normal; null off the floor.
    /// </summary>
    private static (Vector3 Point, Vector3 Central)? FloorRayOf(
        CameraView camera,
        (int X, int Y) pixel,
        float roughness
    )
    {
        Vector3 ray = camera.ViewRayThrough(new(pixel.X + 0.5f, pixel.Y + 0.5f));
        if (ray.Y >= 0)
            return null;
        Vector3 point = ray * (FloorHeight / ray.Y);
        if (point.Z <= WallZ)
            return null;
        var view = Vector3.Normalize(ray);
        Double3 mirror = new(view.X, -view.Y, view.Z);
        Double3 central = new GgxLobe(roughness).DominantDirection(normal: new(0, 1, 0), mirror: mirror);
        return (point, new((float)central.X, (float)central.Y, (float)central.Z));
    }

    /// <summary>Where a floor point's central ray meets the wall's plane.</summary>
    private static Vector3 WallPointOf((Vector3 Point, Vector3 Central) ray) =>
        ray.Point + ray.Central * ((WallZ - ray.Point.Z) / ray.Central.Z);

    /// <summary>Where the central ray of the floor point that a pixel shows meets the wall's plane; null off the floor.</summary>
    private static Vector3? WallPointOf(CameraView camera, (int X, int Y) pixel, float roughness) =>
        FloorRayOf(camera, pixel, roughness) is { } ray ? WallPointOf(ray) : null;

    /// <summary>The row of the brightest floor pixel of a column, at least half a meter in front of the wall.</summary>
    private static int BrightestFloorRow(float[] hdr, CameraView camera, int column)
    {
        int brightest = -1;
        float most = 0;
        for (int row = 0; row < Height; row++)
        {
            if (FloorRayOf(camera, (column, row), roughness: 0) is not { } ray || ray.Point.Z < WallZ + 0.5f)
                continue;
            Vector3 color = ReflectionTesting.At(hdr, (column, row), Width);
            float luma = 0.2126f * color.X + 0.7152f * color.Y + 0.0722f * color.Z;
            if (luma > most)
                (brightest, most) = (row, luma);
        }
        most.Should().BeGreaterThan(0.05f, "the floor reflects something bright in column {0}", column);
        return brightest;
    }

    /// <summary>
    /// The angle, as the eye sees it, from the stripe 1.5 m above the eye to where the brightest floor pixel of a column
    /// reflects, by a ray trace of the floor's mirror rays.
    /// </summary>
    private static float StripeAngle(float[] hdr, CameraView camera, int column)
    {
        Vector3 seen = WallPointOf(camera, (column, BrightestFloorRow(hdr, camera, column)), roughness: 0)!.Value;
        var stripe = new Vector3(seen.X, 1.5f, WallZ);
        return MathF.Acos(Math.Clamp(Vector3.Dot(Vector3.Normalize(seen), Vector3.Normalize(stripe)), -1, 1));
    }

    private sealed class DrawHistoryPass : RenderPass
    {
        public override string Name => "test map draw history";
        public List<long> Indices { get; } = [];
        public List<bool> Resets { get; } = [];

        public override void Record(FrameContext context)
        {
            Indices.Add(context.History.DrawIndex);
            Resets.Add(context.History.Reset);
        }
    }

    private sealed unsafe class DirectionOnlyComposite : RenderPass
    {
        private VulkanDevice? device;
        private Pipeline pipeline;
        private DescriptorSet lightSet;

        public override string Name => "composite, direction only";

        public static void Replace(VulkanRenderer renderer)
        {
            renderer.Graph.Skipped.Add("composite");
            renderer.Graph.Add(FrameGraph.Stage.Composite, new DirectionOnlyComposite());
        }

        public override void Prepare(FrameContext context)
        {
            if (device is not null)
                return;
            device = context.Device;
            using var shaders = new ShaderLibrary(context.Device, typeof(DirectionOnlyComposite).Assembly);
            pipeline = new GraphicsPipelineDesc
            {
                Name = Name,
                VertexShader = "direction_only_lighting.vert",
                FragmentShader = "direction_only_composite.frag",
                Layout = context.Layouts.CompositeLayout,
                ColorFormats = [RenderTargets.HdrFormat],
                CullMode = CullModeFlags.None,
                Constants = new ShaderConstants(EnvironmentMap: true),
            }.Create(device, shaders);
            lightSet = context.Layouts.AllocateLightSet();
            for (uint binding = 0; binding < 4; binding++)
                context.Layouts.WriteStorageImage(
                    set: lightSet,
                    binding: binding,
                    image: binding % 2 == 0 ? context.Targets.DirectDiffuse : context.Targets.DirectSpecular
                );
        }

        public override void Record(FrameContext context)
        {
            foreach (GpuImage image in context.Targets.GBuffer)
                context.Barriers.Image(image, ImageUse.SampledFragment);
            context.Barriers.Image(context.Targets.DirectDiffuse, ImageUse.StorageFragment);
            context.Barriers.Image(context.Targets.DirectSpecular, ImageUse.StorageFragment);
            context.Barriers.Image(context.Targets.Hdr, ImageUse.ColorAttachment, discard: true);
            context.Barriers.Buffer(context.Exposure.Buffer, BufferUse.ShaderRead);
            context.Barriers.Flush(context.Vk, context.CommandBuffer);
            context.DrawSceneFullScreen(
                pipeline: pipeline,
                layout: context.Layouts.CompositeLayout,
                passSet: lightSet,
                targets: [(context.Targets.Hdr, AttachmentLoadOp.DontCare)]
            );
        }

        public override void Dispose()
        {
            device?.Vk.DestroyPipeline(device.Device, pipeline, null);
            base.Dispose();
        }
    }
}
