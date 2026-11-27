using System.Numerics;
using Tertius.Testing;

namespace Tertius.Rendering.Tests;

[Collection(GpuCollection.Name)]
public sealed class LightVolumeRenderTests
{
    private readonly record struct Panel(Vector3 Position, Vector3 Scale, uint Entity);

    private static VulkanRenderer CreateVolumeRenderer(bool screenTrace = false, float exposureStops = 0) =>
        VulkanRenderer.CreateHeadless(
            new RendererOptions
            {
                Width = 64,
                Height = 64,
                WarmUpFrames = 0,
                Validation = true,
                Impostors = false,
                Upscaling = new NoUpscaling(),
                Exposure = new FixedExposure(exposureStops),
                EnvironmentMap = new NoEnvironmentMap(),
                ScreenTrace = screenTrace ? ScreenTrace.Default : new NoScreenTrace(),
                LightVolume = true,
            }
        );

    private static RenderInstance BoxPanel(MeshId box, MaterialId material, Panel panel) =>
        Gpu.Instance(
            mesh: box,
            material: material,
            position: panel.Position,
            scale: panel.Scale,
            chainDepth: TestChains.CamerasFrame,
            entity: panel.Entity
        );

    private static RenderSnapshot DarkSnapshot(Vector3 lightDirection)
    {
        RenderSnapshot scene = Gpu.Snapshot(lightDirection);
        scene.Lights.Clear();
        scene.Ambient = default;
        return scene;
    }

    private static float RenderCenterRed(VulkanRenderer renderer, RenderSnapshot scene)
    {
        scene.Time += 1.0 / 60;
        renderer.Render(scene, 1f);
        return renderer.ReadHdr()[4 * (32 * 64 + 32)];
    }

    [Theory]
    [InlineData(1.0f, true)]
    [InlineData(0.1f, false)]
    public void AnOffscreenSunlitRedSurfaceAppearsInBroadReflections(float roughness, bool broad)
    {
        VulkanRenderer renderer = Gpu.Headless(width: 64, height: 64, warmUpFrames: 0, lightVolume: true);
        try
        {
            MeshId box = renderer.CreateMesh(Primitives.Box(new(0.5f)));
            MaterialId receiver = renderer.CreateMaterial(
                new(BaseColor: Vector4.UnitW, Roughness: roughness, Reflectance: 1)
            );
            MaterialId red = renderer.CreateMaterial(MaterialDesc.Color(Vector4.UnitX + Vector4.UnitW, 1));
            RenderSnapshot scene = Gpu.Snapshot(-Vector3.UnitZ, starIrradiance: 20);
            scene.Ambient = default;
            scene.Instances.Add(BoxPanel(box, receiver, new(new(0, 0, -1.5f), new(3, 3, 0.08f), 1)));
            scene.Instances.Add(BoxPanel(box, red, new(new(0, 0, 0.75f), new(2, 2, 0.08f), 2)));
            renderer.Render(scene, 1f);
            float[] hdr = renderer.ReadHdr();
            int center = 4 * (32 * 64 + 32);
            renderer.ReadVolumeLightEvaluations().Should().BeInRange(1u, 200u);
            if (broad)
                hdr[center].Should().BeGreaterThan(hdr[center + 1] + 0.005f);
            else
                hdr[center].Should().BeLessThan(0.001f);
        }
        finally
        {
            Gpu.DisposeAndAssertClean(renderer);
        }
    }

    [Fact]
    public void AnOffscreenEmitterLightsTheReceiverAndOpaqueSheetsReduceItByProjectedArea()
    {
        VulkanRenderer renderer = CreateVolumeRenderer();
        try
        {
            MeshId box = renderer.CreateMesh(Primitives.Box(new(0.5f)));
            MaterialId matte = renderer.CreateMaterial(MaterialDesc.Color(Vector4.One, 1));
            MaterialId red = renderer.CreateMaterial(
                new(BaseColor: Vector4.Zero with { W = 1 }, Roughness: 1, Emissive: new(8, 0, 0))
            );
            RenderSnapshot scene = DarkSnapshot(Vector3.UnitZ);
            scene.Instances.Add(BoxPanel(box, matte, new(new(0, 0, -2), new(4, 4, 0.08f), 1)));
            scene.Instances.Add(BoxPanel(box, red, new(new(0, 0, 1.5f), new(2, 2, 0.08f), 2)));

            float open = RenderCenterRed(renderer, scene);
            open.Should().BeGreaterThan(0.01f, "default lighting includes the offscreen emitter");
            renderer.ReadVolumeLightEvaluations().Should().Be(0, "there are no direct lights to evaluate");

            scene.Instances.Add(BoxPanel(box, matte, new(new(0, 0, 0.6f), new(6, 6, 0.08f), 3)));
            RenderCenterRed(renderer, scene).Should().BeLessThan(open * 0.25f);

            scene.Instances[2] = BoxPanel(box, matte, new(new(0, 0, 0.6f), new(0.6f, 0.6f, 0.08f), 3));
            float partial = RenderCenterRed(renderer, scene);
            partial.Should().BeInRange(open * 0.05f, open * 0.98f);

            RenderInstance edgeOn = scene.Instances[2];
            edgeOn.Rotation = Quaternion.CreateFromAxisAngle(Vector3.UnitY, MathF.PI / 2);
            edgeOn.PreviousRotation = edgeOn.Rotation;
            scene.Instances[2] = edgeOn;
            RenderCenterRed(renderer, scene).Should().BeGreaterThan(partial, "an edge-on sheet intercepts less light");

            scene.Instances.RemoveAt(2);
            RenderCenterRed(renderer, scene).Should().BeApproximately(open, open * 0.02f);
        }
        finally
        {
            Gpu.DisposeAndAssertClean(renderer);
        }
    }

    [Fact]
    public void RebasingTheSnapshotOriginAcrossACameraCutPreservesVolumeLighting()
    {
        VulkanRenderer renderer = CreateVolumeRenderer();
        try
        {
            MeshId box = renderer.CreateMesh(Primitives.Box(new(0.5f)));
            MaterialId matte = renderer.CreateMaterial(MaterialDesc.Color(Vector4.One, 1));
            MaterialId red = renderer.CreateMaterial(
                new(BaseColor: Vector4.Zero with { W = 1 }, Roughness: 1, Emissive: new(8, 0, 0))
            );
            RenderSnapshot scene = DarkSnapshot(Vector3.UnitZ);
            scene.Instances.Add(BoxPanel(box, matte, new(new(0, 0, -2), new(4, 4, 0.08f), 1)));
            scene.Instances.Add(BoxPanel(box, red, new(new(0, 0, 1.5f), new(2, 2, 0.08f), 2)));

            float beforeCut = RenderCenterRed(renderer, scene);
            Vector3 shift = new(0.19f, -0.1f, 0.31f);
            scene.OriginInFrame += (Double3)shift;
            scene.Camera.Position -= shift;
            scene.Camera.PreviousPosition -= shift;
            for (int i = 0; i < scene.Instances.Count; i++)
            {
                RenderInstance instance = scene.Instances[i];
                instance.Position -= shift;
                instance.PreviousPosition -= shift;
                scene.Instances[i] = instance;
            }
            scene.CameraCuts++;

            RenderCenterRed(renderer, scene).Should().BeApproximately(beforeCut, beforeCut * 0.02f);
        }
        finally
        {
            Gpu.DisposeAndAssertClean(renderer);
        }
    }

    [Fact]
    public void ASourceOutsideTheFineGridStillLightsTheReceiverThroughTheCoarseGrid()
    {
        VulkanRenderer renderer = CreateVolumeRenderer();
        try
        {
            MeshId box = renderer.CreateMesh(Primitives.Box(new(0.5f)));
            MaterialId matte = renderer.CreateMaterial(MaterialDesc.Color(Vector4.One, 1));
            MaterialId red = renderer.CreateMaterial(
                new(BaseColor: Vector4.Zero with { W = 1 }, Roughness: 1, Emissive: new(8, 0, 0))
            );
            RenderSnapshot scene = DarkSnapshot(Vector3.UnitZ);
            scene.Instances.Add(BoxPanel(box, matte, new(new(0, 0, -2), new(4, 4, 0.08f), 1)));
            scene.Instances.Add(BoxPanel(box, red, new(new(0, 0, 1.5f), new(2, 2, 0.08f), 2)));
            float near = RenderCenterRed(renderer, scene);

            scene.Instances[1] = BoxPanel(box, red, new(new(0, 0, 6.5f), new(2, 2, 0.08f), 2));
            RenderCenterRed(renderer, scene)
                .Should()
                .BeInRange(near * 0.001f, near * 0.5f, "a source outside the fine grid subtends a smaller solid angle");
        }
        finally
        {
            Gpu.DisposeAndAssertClean(renderer);
        }
    }

    [Fact]
    public void ANeutralOffscreenReflectorAddsFarLessIndirectLightThanAColoredOneOfTheSamePeakAlbedo()
    {
        float colored = MeasureOffscreenReflectorRed(baseColor: new(x: 1, y: 0, z: 0, w: 1), exposureStops: 0);
        float neutral = MeasureOffscreenReflectorRed(baseColor: Vector4.One, exposureStops: 0);

        colored.Should().BeGreaterThan(0.01f, "a saturated red reflector keeps its full indirect bounce");
        neutral.Should().BeGreaterThan(0.0001f, "neutral bounce is restrained rather than removed");
        neutral.Should().BeLessThan(colored * 0.1f, "low-saturation diffuse fill is restrained relative to color");
    }

    [Fact]
    public void ANeutralOffscreenEmitterAddsFarLessIndirectLightThanAColoredOneOfTheSamePeakRadiance()
    {
        float colored = MeasureOffscreenEmitterRed(emissive: new(8, 0, 0), exposureStops: 0);
        float neutral = MeasureOffscreenEmitterRed(emissive: new(8, 8, 8), exposureStops: 0);

        colored.Should().BeGreaterThan(0.01f, "a saturated red emitter keeps its full indirect contribution");
        neutral.Should().BeGreaterThan(0.0001f, "neutral emission is restrained rather than removed");
        neutral.Should().BeLessThan(colored * 0.1f, "low-saturation emission is restrained relative to color");
    }

    [Fact]
    public void ChangingExposureScalesTheVolumesIndirectLightWithoutChangingItsWorldRadiance()
    {
        float baseReflector = MeasureOffscreenReflectorRed(baseColor: Vector4.One, exposureStops: 0);
        float boostedReflector = MeasureOffscreenReflectorRed(baseColor: Vector4.One, exposureStops: 4);
        float baseEmitter = MeasureOffscreenEmitterRed(emissive: new(8, 8, 8), exposureStops: 0);
        float boostedEmitter = MeasureOffscreenEmitterRed(emissive: new(8, 8, 8), exposureStops: 4);

        (boostedReflector / baseReflector)
            .Should()
            .BeApproximately(16f, 0.4f, "four stops of exposure scale neutral diffuse bounce by 16x");
        (boostedEmitter / baseEmitter)
            .Should()
            .BeApproximately(16f, 0.4f, "four stops of exposure scale neutral emission bounce by 16x");
    }

    [Fact]
    public void AGlossySurfaceOccludesOffscreenEnvironmentMissesBehindAnOpaqueWall()
    {
        VulkanRenderer renderer = CreateVolumeRenderer(screenTrace: true);
        try
        {
            MeshId box = renderer.CreateMesh(Primitives.Box(new(0.5f)));
            MaterialId glossy = renderer.CreateMaterial(new(BaseColor: Vector4.UnitW, Roughness: 0.2f, Reflectance: 1));
            MaterialId black = renderer.CreateMaterial(new(BaseColor: Vector4.UnitW, Roughness: 1, Reflectance: 0));
            RenderSnapshot scene = DarkSnapshot(Vector3.UnitZ);
            scene.Ambient = new AmbientLight
            {
                SkyColor = new(10),
                GroundColor = new(10),
                Intensity = 1,
            };
            scene.Instances.Add(BoxPanel(box, glossy, new(new(0, 0, -1.5f), new(3, 3, 0.08f), 1)));
            float open = RenderCenterRed(renderer, scene);

            scene.Instances.Add(BoxPanel(box, black, new(new(0, 0, 1.0f), new(8, 8, 0.08f), 2)));
            scene.CameraCuts++;
            float occluded = RenderCenterRed(renderer, scene);

            open.Should().BeGreaterThan(0.1f, "an open room reflects the offscreen environment on a miss");
            occluded
                .Should()
                .BeLessThan(open * 0.25f, "an opaque wall behind the camera occludes offscreen environment misses");
        }
        finally
        {
            Gpu.DisposeAndAssertClean(renderer);
        }
    }

    [Fact]
    public void AGlossyHitSurfaceWithoutSpecularHistoryOccludesEnvironmentReflectionsOnAResetFrame()
    {
        VulkanRenderer renderer = CreateVolumeRenderer(screenTrace: true);
        try
        {
            MeshId box = renderer.CreateMesh(Primitives.Box(new(0.5f)));
            MaterialId floorMat = renderer.CreateMaterial(
                new(BaseColor: Vector4.One, Roughness: 0.05f, Model: new StandardShading(Metallic: 1))
            );
            MaterialId hitMat = renderer.CreateMaterial(
                new(BaseColor: Vector4.One, Roughness: 0.2f, Model: new StandardShading(Metallic: 1))
            );
            MaterialId black = renderer.CreateMaterial(new(BaseColor: Vector4.UnitW, Roughness: 1, Reflectance: 0));
            RenderSnapshot scene = DarkSnapshot(Vector3.UnitZ);
            scene.Ambient = new AmbientLight
            {
                SkyColor = new(10),
                GroundColor = new(10),
                Intensity = 1,
            };
            scene.Instances.Add(BoxPanel(box, floorMat, new(new(0, -0.5f, -2.0f), new(6, 0.08f, 6), 1)));
            scene.Instances.Add(BoxPanel(box, hitMat, new(new(0, 0, -3.2f), new(6, 4, 0.08f), 2)));
            renderer.Render(scene, 1f);
            float open = renderer.ReadHdr()[4 * (54 * 64 + 32)];

            scene.Instances.Add(BoxPanel(box, black, new(new(0, 0, 1.0f), new(8, 6, 0.08f), 3)));
            scene.Instances.Add(BoxPanel(box, black, new(new(0, 2.0f, -1.0f), new(8, 0.08f, 8), 4)));
            scene.Instances.Add(BoxPanel(box, black, new(new(-2.8f, 0, -1.0f), new(0.08f, 6, 8), 5)));
            scene.Instances.Add(BoxPanel(box, black, new(new(2.8f, 0, -1.0f), new(0.08f, 6, 8), 6)));
            scene.CameraCuts++;
            scene.Time += 1.0 / 60;
            renderer.Render(scene, 1f);
            float closed = renderer.ReadHdr()[4 * (54 * 64 + 32)];

            open.Should().BeGreaterThan(1f, "an open room reflects the bright environment via the glossy wall");
            closed
                .Should()
                .BeLessThan(
                    open * 0.25f,
                    "a glossy hit surface with no specular history must obey volume occlusion inside a closed room"
                );
        }
        finally
        {
            Gpu.DisposeAndAssertClean(renderer);
        }
    }

    /// <summary>
    /// The camera walks along -z in steps of 2 cm, from z = -0.45 m to -0.55 m. The step from -0.49 m to -0.51 m crosses
    /// the place where the 1 m grid moves by a whole cell, and the other steps stay within a cell. A matte wall faces the
    /// camera about <paramref name="distance"/> m ahead, inside the outer grid's fade. A wide red emitter behind the
    /// camera lights it through the grid alone. The grid carries light along 32 fixed directions, so a small emitter's
    /// beams would miss a wall this far away.
    /// </summary>
    [Theory]
    [InlineData(6.7f)]
    [InlineData(7.0f)]
    [InlineData(7.3f)]
    public void AWallInTheOuterFadeChangesNoMoreWhenTheCameraStepsAcrossAWholeCellThanWithinOne(float distance)
    {
        VulkanRenderer renderer = CreateVolumeRenderer();
        try
        {
            RenderSnapshot scene = WallBeforeAWideEmitter(renderer, wall: -0.5f - distance, emitter: 1.5f);
            float[] lights =
            [
                .. new[] { -0.45f, -0.47f, -0.49f, -0.51f, -0.53f, -0.55f }.Select(z =>
                    RenderCenterRedFrom(renderer, scene, new(0, 0, z))
                ),
            ];
            float[] changes = [.. lights.Zip(lights.Skip(1), (before, after) => after - before)];
            float within = (changes[0] + changes[1] + changes[3] + changes[4]) / 4;

            lights.Min().Should().BeGreaterThan(1e-3f, "the emitter lights the wall through the outer grid");
            changes[2]
                .Should()
                .BeApproximately(within, lights[2] * 0.01f, "the fade follows the camera, so the snap adds no step");
        }
        finally
        {
            Gpu.DisposeAndAssertClean(renderer);
        }
    }

    /// <summary>
    /// The scene moves with the camera, so the wall stays <paramref name="distance"/> m ahead in the outer grid's fade,
    /// while the camera's place within its 1 m cell, and so the cell that the grid snaps to, takes five values across
    /// the cell.
    /// </summary>
    [Theory]
    [InlineData(6.8f)]
    [InlineData(7.2f)]
    public void AWallAtAFixedDistanceInTheOuterFadeGetsTheSameLightWhereverTheGridSnaps(float distance)
    {
        VulkanRenderer renderer = CreateVolumeRenderer();
        try
        {
            var lights = new List<float>();
            foreach (float place in new[] { -0.45f, -0.2f, 0f, 0.2f, 0.45f })
            {
                RenderSnapshot scene = WallBeforeAWideEmitter(renderer, wall: place - distance, emitter: place + 2);
                lights.Add(RenderCenterRedFrom(renderer, scene, new(0, 0, place)));
            }

            lights.Min().Should().BeGreaterThan(1e-3f, "the emitter lights the wall through the outer grid");
            lights
                .Max()
                .Should()
                .BeLessThan(lights.Min() * 1.1f, "the fade follows the camera, not the cell that the grid snapped to");
        }
        finally
        {
            Gpu.DisposeAndAssertClean(renderer);
        }
    }

    /// <summary>
    /// The outer grid's fade ends 7.5 cells from the camera, wherever the grid snaps. A wall 7.6 m ahead gets none of the
    /// grid's light at any of five places of the camera within its 1 m cell, and a wall 7.4 m ahead gets some at each.
    /// </summary>
    [Fact]
    public void TheOuterFadeEndsSevenAndAHalfCellsFromTheCameraWhereverTheGridSnaps()
    {
        VulkanRenderer renderer = CreateVolumeRenderer();
        try
        {
            var beyond = new List<float>();
            var within = new List<float>();
            foreach (float place in new[] { -0.45f, -0.2f, 0f, 0.2f, 0.45f })
            {
                // The wall's face toward the camera lies 0.04 m nearer than its center.
                RenderSnapshot far = WallBeforeAWideEmitter(renderer, wall: place - 7.64f, emitter: place + 2);
                beyond.Add(RenderCenterRedFrom(renderer, far, new(0, 0, place)));
                RenderSnapshot near = WallBeforeAWideEmitter(renderer, wall: place - 7.44f, emitter: place + 2);
                within.Add(RenderCenterRedFrom(renderer, near, new(0, 0, place)));
            }

            beyond
                .Should()
                .OnlyContain(red => red == 0, "the dark scene's only light reaches the wall through the grid");
            within
                .Should()
                .OnlyContain(red => red > 1e-3f, "the outer grid still gives a twentieth of the wall's light");
        }
        finally
        {
            Gpu.DisposeAndAssertClean(renderer);
        }
    }

    /// <summary>
    /// The camera reaches z = -0.51 m, in the 1 m grid's cell past a snap, once walking along -z from -0.41 m and once
    /// walking back along +z from -0.61 m, without a cut. A wall 7 m ahead lies in the outer grid's fade.
    /// </summary>
    [Fact]
    public void AWallInTheOuterFadeGetsTheSameLightWhicheverWayTheCameraCame()
    {
        VulkanRenderer renderer = CreateVolumeRenderer();
        try
        {
            RenderSnapshot scene = WallBeforeAWideEmitter(renderer, wall: -7.5f, emitter: 1.5f);
            float forward = WalkTo(renderer: renderer, scene: scene, from: -0.41f, to: -0.51f);
            float backward = WalkTo(renderer: renderer, scene: scene, from: -0.61f, to: -0.51f);

            forward.Should().BeGreaterThan(1e-3f, "the emitter lights the wall through the outer grid");
            backward.Should().Be(forward, "the volume keeps no state from earlier frames");
        }
        finally
        {
            Gpu.DisposeAndAssertClean(renderer);
        }
    }

    /// <summary>
    /// Six black walls close a cube of 17 m around the camera, which stands at the center of its 1 m cell. So each wall
    /// lies in the outer grid's shell, the cells from 8 to 9 m from the camera, and the walls meet in the shell's edges
    /// and corners. Bright sky light reaches the receiver only where the shell lets it through. The corner cell with
    /// the highest index lies in the last, partial work group of each pass over all cells.
    /// </summary>
    [Fact]
    public void WallsInTheOuterGridsShellCloseTheSkyFromAReceiverNearTheCamera()
    {
        VulkanRenderer renderer = CreateVolumeRenderer();
        try
        {
            MeshId box = renderer.CreateMesh(Primitives.Box(new(0.5f)));
            MaterialId matte = renderer.CreateMaterial(MaterialDesc.Color(Vector4.One, 1));
            MaterialId black = renderer.CreateMaterial(new(BaseColor: Vector4.UnitW, Roughness: 1, Reflectance: 0));
            RenderSnapshot scene = DarkSnapshot(Vector3.UnitZ);
            scene.Ambient = new AmbientLight
            {
                SkyColor = new(10),
                GroundColor = new(10),
                Intensity = 1,
            };
            scene.Instances.Add(BoxPanel(box, matte, new(new(0, 0, -2), new(4, 4, 0.08f), 1)));
            float open = RenderCenterRed(renderer, scene);

            uint entity = 2;
            foreach (int axis in new[] { 0, 1, 2 })
            foreach (float side in new[] { -8.5f, 8.5f })
            {
                Vector3 place = Vector3.Zero,
                    scale = new(17);
                place[axis] = side;
                scale[axis] = 0.08f;
                scene.Instances.Add(BoxPanel(box, black, new(place, scale, entity++)));
            }
            scene.CameraCuts++;
            float closed = RenderCenterRed(renderer, scene);

            open.Should().BeGreaterThan(0.1f, "an open receiver takes the sky's light");
            closed.Should().BeLessThan(open * 0.1f, "the shell's walls, edges and corners hold the sky out");
        }
        finally
        {
            Gpu.DisposeAndAssertClean(renderer);
        }
    }

    /// <summary>
    /// A wide red reflector, lit by the sun on its side that faces the receiver, stands in the outer grid's shell behind
    /// the camera, in the cells from 8 to 9 m. Those cells form groups of one cell's depth that share one evaluation of
    /// the lights.
    /// </summary>
    [Fact]
    public void ASunlitReflectorInTheOuterGridsShellLightsAReceiverNearTheCamera()
    {
        float shell = MeasureWideReflectorRed(8.5f);
        float inside = MeasureWideReflectorRed(7.5f);

        inside.Should().BeGreaterThan(1e-4f, "a sunlit reflector inside the old outer cube lights the receiver");
        shell.Should().BeGreaterThan(inside * 0.5f, "the shell's groups evaluate the sun like any other group");
    }

    [Fact]
    public void ALightThatReachesOnlyTheOuterGridsShellCanLightTheVolume()
    {
        LightVolumePass.Intersects(center: new(0, 0, 9.3f), radius: 0).Should().BeTrue();
        LightVolumePass.Intersects(center: new(9.3f, -9.3f, 9.3f), radius: 0).Should().BeTrue();
        LightVolumePass.Intersects(center: new(0, 0, 9.6f), radius: 0).Should().BeFalse();
    }

    [Theory]
    [InlineData(4.25f)]
    [InlineData(4.75f)]
    public void AnEmitterJustOutsideTheMiddleGridLightsAReceiverThroughTheOuterGrid(float distance)
    {
        VulkanRenderer renderer = CreateVolumeRenderer();
        try
        {
            MeshId box = renderer.CreateMesh(Primitives.Box(new(0.5f)));
            MaterialId matte = renderer.CreateMaterial(MaterialDesc.Color(Vector4.One, 1));
            MaterialId red = renderer.CreateMaterial(
                new(BaseColor: Vector4.UnitW, Roughness: 1, Emissive: new(8, 0, 0))
            );
            RenderSnapshot scene = DarkSnapshot(Vector3.UnitZ);
            scene.Instances.Add(BoxPanel(box, matte, new(new(0, 0, -2), new(4, 4, 0.08f), 1)));
            scene.Instances.Add(BoxPanel(box, red, new(new(0, 0, distance), new(6, 6, 0.08f), 2)));

            RenderCenterRed(renderer, scene)
                .Should()
                .BeGreaterThan(1e-3f, "the middle grid ends at 4 m, so the outer grid must capture the emitter once");
        }
        finally
        {
            Gpu.DisposeAndAssertClean(renderer);
        }
    }

    [Fact]
    public void OppositeFacesOfTheOuterFadeReceiveTheSameLightFromMirroredScenes()
    {
        VulkanRenderer renderer = CreateVolumeRenderer();
        try
        {
            MeshId box = renderer.CreateMesh(Primitives.Box(new(0.5f)));
            MaterialId matte = renderer.CreateMaterial(MaterialDesc.Color(Vector4.One, 1));
            MaterialId red = renderer.CreateMaterial(
                new(BaseColor: Vector4.UnitW, Roughness: 1, Emissive: new(8, 0, 0))
            );
            var lights = new List<float>();
            foreach (float sign in new[] { -1f, 1f })
            {
                RenderSnapshot scene = DarkSnapshot(Vector3.UnitZ);
                scene.Camera.Rotation =
                    sign < 0 ? Quaternion.Identity : Quaternion.CreateFromAxisAngle(Vector3.UnitY, MathF.PI);
                scene.Camera.PreviousRotation = scene.Camera.Rotation;
                scene.Instances.Add(BoxPanel(box, matte, new(new(0, 0, sign * 7.3f), new(6, 6, 0.08f), 1)));
                scene.Instances.Add(BoxPanel(box, red, new(new(0, 0, -sign * 2), new(6, 6, 0.08f), 2)));
                lights.Add(RenderCenterRedFrom(renderer, scene, Vector3.Zero));
            }

            lights[0].Should().BeGreaterThan(1e-3f, "the finite emitter lights the wall through the outer grid");
            lights[1]
                .Should()
                .BeApproximately(
                    lights[0],
                    lights[0] * 0.02f,
                    "opposite outer faces have the same interpolation support, including cells beyond index 15"
                );
        }
        finally
        {
            Gpu.DisposeAndAssertClean(renderer);
        }
    }

    private static RenderSnapshot WallBeforeAWideEmitter(VulkanRenderer renderer, float wall, float emitter)
    {
        MeshId box = renderer.CreateMesh(Primitives.Box(new(0.5f)));
        MaterialId matte = renderer.CreateMaterial(MaterialDesc.Color(Vector4.One, 1));
        MaterialId red = renderer.CreateMaterial(
            new(BaseColor: Vector4.Zero with { W = 1 }, Roughness: 1, Emissive: new(8, 0, 0))
        );
        RenderSnapshot scene = DarkSnapshot(Vector3.UnitZ);
        scene.Instances.Add(BoxPanel(box, matte, new(new(0, 0, wall), new(6, 6, 0.08f), 1)));
        scene.Instances.Add(BoxPanel(box, red, new(new(0, 0, emitter), new(12, 12, 0.08f), 2)));
        return scene;
    }

    // Walks the camera along z in steps of 2 cm with no cut, then gives the red of the center pixel at the end.
    private static float WalkTo(VulkanRenderer renderer, RenderSnapshot scene, float from, float to)
    {
        RenderCenterRedFrom(renderer, scene, new(0, 0, from));
        int steps = (int)MathF.Round(MathF.Abs(to - from) / 0.02f);
        float red = 0;
        for (int i = 1; i <= steps; i++)
        {
            scene.Camera.PreviousPosition = scene.Camera.Position;
            scene.Camera.Position = new(0, 0, from + (to - from) * i / steps);
            red = RenderCenterRed(renderer, scene);
        }
        return red;
    }

    private static float RenderCenterRedFrom(VulkanRenderer renderer, RenderSnapshot scene, Vector3 eye)
    {
        scene.Camera.Position = eye;
        scene.Camera.PreviousPosition = eye;
        scene.CameraCuts++;
        return RenderCenterRed(renderer, scene);
    }

    private static float MeasureWideReflectorRed(float distance)
    {
        VulkanRenderer renderer = CreateVolumeRenderer();
        try
        {
            MeshId box = renderer.CreateMesh(Primitives.Box(new(0.5f)));
            MaterialId receiver = renderer.CreateMaterial(MaterialDesc.Color(Vector4.One, 1));
            MaterialId red = renderer.CreateMaterial(MaterialDesc.Color(Vector4.UnitX + Vector4.UnitW, 1));
            RenderSnapshot scene = Gpu.Snapshot(-Vector3.UnitZ, starIrradiance: 20);
            scene.Ambient = default;
            scene.Instances.Add(BoxPanel(box, receiver, new(new(0, 0, -1.5f), new(3, 3, 0.08f), 1)));
            scene.Instances.Add(BoxPanel(box, red, new(new(0, 0, distance), new(12, 12, 0.08f), 2)));
            return RenderCenterRed(renderer, scene);
        }
        finally
        {
            Gpu.DisposeAndAssertClean(renderer);
        }
    }

    private static float MeasureOffscreenReflectorRed(Vector4 baseColor, float exposureStops)
    {
        VulkanRenderer renderer = CreateVolumeRenderer(exposureStops: exposureStops);
        try
        {
            MeshId box = renderer.CreateMesh(Primitives.Box(new(0.5f)));
            MaterialId receiver = renderer.CreateMaterial(MaterialDesc.Color(Vector4.One, 1));
            MaterialId source = renderer.CreateMaterial(MaterialDesc.Color(baseColor, 1));
            RenderSnapshot scene = Gpu.Snapshot(-Vector3.UnitZ, starIrradiance: 20);
            scene.Ambient = default;
            scene.Instances.Add(BoxPanel(box, receiver, new(new(0, 0, -1.5f), new(3, 3, 0.08f), 1)));
            scene.Instances.Add(BoxPanel(box, source, new(new(0, 0, 0.75f), new(2, 2, 0.08f), 2)));
            return RenderCenterRed(renderer, scene);
        }
        finally
        {
            Gpu.DisposeAndAssertClean(renderer);
        }
    }

    private static float MeasureOffscreenEmitterRed(Vector3 emissive, float exposureStops)
    {
        VulkanRenderer renderer = CreateVolumeRenderer(exposureStops: exposureStops);
        try
        {
            MeshId box = renderer.CreateMesh(Primitives.Box(new(0.5f)));
            MaterialId receiver = renderer.CreateMaterial(MaterialDesc.Color(Vector4.One, 1));
            MaterialId source = renderer.CreateMaterial(
                new(BaseColor: Vector4.Zero with { W = 1 }, Roughness: 1, Emissive: emissive)
            );
            RenderSnapshot scene = DarkSnapshot(Vector3.UnitZ);
            scene.Instances.Add(BoxPanel(box, receiver, new(new(0, 0, -2), new(4, 4, 0.08f), 1)));
            scene.Instances.Add(BoxPanel(box, source, new(new(0, 0, 1.5f), new(2, 2, 0.08f), 2)));
            return RenderCenterRed(renderer, scene);
        }
        finally
        {
            Gpu.DisposeAndAssertClean(renderer);
        }
    }
}
