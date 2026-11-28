using System.Numerics;
using Tertius.Testing;

namespace Tertius.Rendering.Tests;

/// <summary>Diffuse environment light on a wall outside all local light grids during a camera walk.</summary>
[Collection(GpuCollection.Name)]
public sealed class EnvironmentMotionTests
{
    [Fact]
    public void ANewCaptureStartsLightingBothAffectedNormalsOnTheSameFrame()
    {
        VulkanRenderer renderer = CaptureRenderer();
        try
        {
            RenderSnapshot snapshot = EnvironmentScenes.Dark();
            for (int frame = 0; frame < 4; frame++)
                Draw(renderer, snapshot);
            (float forward, float oblique) = DiffuseAtTwoNormals(renderer);
            AddRearPanel(renderer, snapshot);

            Draw(renderer, snapshot);
            (float firstForward, float firstOblique) = DiffuseAtTwoNormals(renderer);

            firstOblique.Should().BeGreaterThan(oblique + 0.005f, "the oblique direction sees the same capture update");
            firstForward.Should().BeGreaterThan(forward + 0.005f, "the rear-facing direction starts on that draw too");
            for (int frame = 0; frame < 5; frame++)
                Draw(renderer, snapshot);
            (float settledForward, float settledOblique) = DiffuseAtTwoNormals(renderer);
            settledForward.Should().BeGreaterThan(firstForward + 0.01f, "the light eases toward its captured value");
            settledOblique.Should().BeGreaterThan(firstOblique + 0.01f);
        }
        finally
        {
            Gpu.DisposeAndAssertClean(renderer);
        }
    }

    [Fact]
    public void CuttingBehindAnOpaquePanelUsesTheDestinationLightOnTheFirstDraw()
    {
        VulkanRenderer renderer = CaptureRenderer();
        try
        {
            RenderSnapshot snapshot = EnvironmentScenes.Dark();
            snapshot.Instances.Add(
                EnvironmentScenes.Facing(
                    plane: renderer.CreateMesh(Primitives.Plane(1f)),
                    material: EnvironmentScenes.Emissive(renderer, Vector3.Zero),
                    center: new(0.6f, 0, 0.5f),
                    normal: -Vector3.UnitZ,
                    halfSize: 0.25f,
                    chainDepth: TestChains.CamerasFrame
                )
            );
            for (int frame = 0; frame < 4; frame++)
                Draw(renderer, snapshot);
            AddRearPanel(renderer, snapshot);
            Draw(renderer, snapshot);
            DiffuseAtTwoNormals(renderer).Oblique.Should().BeGreaterThan(0.005f, "the old capture is still blending");

            // The same two panels, seen from 1.2 m to the right. The opaque panel now hides the emitter.
            snapshot.CameraCuts++;
            snapshot.OriginInFrame = new(1.2, 0, 0);
            for (int i = 0; i < snapshot.Instances.Count; i++)
            {
                RenderInstance instance = snapshot.Instances[i];
                instance.Position -= 1.2f * Vector3.UnitX;
                instance.PreviousPosition = instance.Position;
                snapshot.Instances[i] = instance;
            }
            Draw(renderer, snapshot);

            DiffuseAtTwoNormals(renderer).Should().Be((0f, 0f), "the new view's occluder hides all panel radiance");
        }
        finally
        {
            Gpu.DisposeAndAssertClean(renderer);
        }
    }

    [Fact]
    public void CapturingAWholeMapOnItsOrdinaryTurnStillEasesTheLight()
    {
        VulkanRenderer renderer = CaptureRenderer(facesPerFrame: 6);
        try
        {
            RenderSnapshot snapshot = EnvironmentScenes.Dark();
            TestChains.Of(snapshot, (1, Quaternion.Identity), (0, Quaternion.Identity));
            Draw(renderer, snapshot);
            AddRearPanel(renderer, snapshot);

            // The cabin map has a turn on draw 1. On draw 2 the root map draws and the cabin blend reaches its target.
            Draw(renderer, snapshot);
            float first = DiffuseAtTwoNormals(renderer, map: 1).Oblique;
            Draw(renderer, snapshot);
            float settled = DiffuseAtTwoNormals(renderer, map: 1).Oblique;

            first
                .Should()
                .BeGreaterThan(0.005f, "the cabin wall starts to brighten on the draw that updates its capture");
            settled.Should().BeApproximately(2 * first, 0.001f, "its two-draw blend finishes while another map draws");
        }
        finally
        {
            Gpu.DisposeAndAssertClean(renderer);
        }
    }

    [Fact]
    public void AWrapInTheCaptureOrderLetsAnUntouchedFaceFinishItsBlend()
    {
        VulkanRenderer renderer = CaptureRenderer(facesPerFrame: 4);
        try
        {
            RenderSnapshot snapshot = EnvironmentScenes.Dark();
            for (int frame = 0; frame < 3; frame++)
                Draw(renderer, snapshot);
            snapshot.Instances.Add(
                EnvironmentScenes.Facing(
                    plane: renderer.CreateMesh(Primitives.Plane(1f)),
                    material: EnvironmentScenes.Emissive(renderer, new Vector3(2)),
                    center: Vector3.UnitY,
                    normal: -Vector3.UnitY,
                    halfSize: 0.4f,
                    chainDepth: TestChains.CamerasFrame
                )
            );

            // The +Y capture updates on draw 3. Draw 4 wraps, updating +Z, -Z, +X and -X.
            Draw(renderer, snapshot);
            float first = DiffuseTexel(renderer: renderer, map: 0, face: CubeFace.PositiveY, x: 8);
            Draw(renderer, snapshot);
            float settled = DiffuseTexel(renderer: renderer, map: 0, face: CubeFace.PositiveY, x: 8);

            first.Should().BeGreaterThan(0.01f, "the top capture begins lighting the receiver");
            settled.Should().BeApproximately(2 * first, 0.001f, "the wrap does not restart the top face's blend");
        }
        finally
        {
            Gpu.DisposeAndAssertClean(renderer);
        }
    }

    [Fact]
    public void ASecondCaptureStartsFromWhatTheWallAlreadyReceived()
    {
        VulkanRenderer renderer = CaptureRenderer(facesPerFrame: 4);
        try
        {
            RenderSnapshot snapshot = EnvironmentScenes.Dark();
            Draw(renderer, snapshot);
            AddRearPanel(renderer, snapshot);

            // Draws 1 and 2 both capture +Z. The panel goes dark before the first blend can finish.
            Draw(renderer, snapshot);
            float first = DiffuseAtTwoNormals(renderer).Rear;
            snapshot.Instances.Clear();
            Draw(renderer, snapshot);
            float second = DiffuseAtTwoNormals(renderer).Rear;
            Draw(renderer, snapshot);
            float settled = DiffuseAtTwoNormals(renderer).Rear;

            first.Should().BeGreaterThan(0.01f);
            second.Should().BeApproximately(first / 2, 0.001f, "the second blend starts from the light already shown");
            settled.Should().Be(0, "the latest capture has no panel light");
        }
        finally
        {
            Gpu.DisposeAndAssertClean(renderer);
        }
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void WalkingPastAnOffscreenPanelDoesNotStepTheLightOnADistantWall(bool towardPanel)
    {
        const int width = 96;
        const int height = 64;
        VulkanRenderer renderer = Gpu.Headless(
            width: width,
            height: height,
            environmentMap: SceneEnvironmentMap.Default with
            {
                FaceSize = 32,
            },
            warmUpFrames: 0,
            outputDither: false,
            lightVolume: true
        );
        try
        {
            MeshId plane = renderer.CreateMesh(Primitives.Plane(1f));
            MaterialId wall = renderer.CreateMaterial(MaterialDesc.Color(Vector4.One, 1f) with { Reflectance = 0 });
            MaterialId panel = EnvironmentScenes.Emissive(renderer, new Vector3(0.5f));
            const int travel = 22;
            const int hold = 12;
            float[] luma = new float[travel + hold + 1];

            for (int frame = 0; frame < luma.Length; frame++)
            {
                int moved = Math.Min(frame, travel);
                float eyeZ = -0.4f + 0.05f * (towardPanel ? moved : travel - moved);
                RenderSnapshot snapshot = EnvironmentScenes.Dark();
                snapshot.Time = frame / 60.0;
                snapshot.OriginInFrame = new(0, 0, eyeZ);
                snapshot.Instances.Add(
                    EnvironmentScenes.Facing(
                        plane: plane,
                        material: wall,
                        center: new(0, 0, -14 - eyeZ),
                        normal: Vector3.UnitZ,
                        halfSize: 4,
                        chainDepth: TestChains.CamerasFrame
                    )
                );
                snapshot.Instances.Add(
                    EnvironmentScenes.Facing(
                        plane: plane,
                        material: panel,
                        center: new(0, 0, 1 - eyeZ),
                        normal: -Vector3.UnitZ,
                        halfSize: 0.2f,
                        chainDepth: TestChains.CamerasFrame
                    )
                );
                Capture capture = Gpu.RenderAndCapture(renderer: renderer, snapshot: snapshot, name: $"walk {frame}");
                luma[frame] = 255 * capture.MeanLuma(new(width / 2, height / 2), radius: 4);
            }

            float totalChange = Math.Abs(luma[^1] - luma[0]);
            totalChange.Should().BeGreaterThan(10, "the wall receives more light when the panel fills more of the map");
            float largestChange = luma.Zip(luma.Skip(1), (a, b) => Math.Abs(b - a)).Max();
            largestChange
                .Should()
                .BeLessThan(totalChange / 3, "one frame should not account for a third of the walk's change");
        }
        finally
        {
            Gpu.DisposeAndAssertClean(renderer);
        }
    }

    private static VulkanRenderer CaptureRenderer(int facesPerFrame = 1) =>
        Gpu.Headless(
            width: 96,
            height: 64,
            environmentMap: new SceneEnvironmentMap(FaceSize: 32, FacesPerFrame: facesPerFrame),
            warmUpFrames: 0
        );

    private static void Draw(VulkanRenderer renderer, RenderSnapshot snapshot)
    {
        snapshot.Time += 1.0 / 60;
        renderer.Render(snapshot, 1f);
    }

    private static void AddRearPanel(VulkanRenderer renderer, RenderSnapshot snapshot) =>
        snapshot.Instances.Add(
            EnvironmentScenes.Facing(
                plane: renderer.CreateMesh(Primitives.Plane(1f)),
                material: EnvironmentScenes.Emissive(renderer, new Vector3(2)),
                center: Vector3.UnitZ,
                normal: -Vector3.UnitZ,
                halfSize: 0.4f,
                chainDepth: TestChains.CamerasFrame
            )
        );

    // Both directions face the rear panel. Their texels lie on different faces of the diffuse cube.
    private static (float Rear, float Oblique) DiffuseAtTwoNormals(VulkanRenderer renderer, int map = 0) =>
        (
            DiffuseTexel(renderer: renderer, map: map, face: CubeFace.PositiveZ, x: 8),
            DiffuseTexel(renderer: renderer, map: map, face: CubeFace.PositiveX, x: 3)
        );

    private static float DiffuseTexel(VulkanRenderer renderer, int map, CubeFace face, int x)
    {
        Half[] irradiance = renderer.ReadEnvironmentFace(
            map: map,
            face: face,
            level: 0,
            image: EnvironmentImage.Irradiance
        );
        return EnvironmentScenes.Texel(face: irradiance, size: IrradianceCube.FaceSize, x: x, y: 8).X;
    }
}
