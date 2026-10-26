using System.Numerics;
using Tertius.Testing;

namespace Tertius.Rendering.Tests;

[Collection(GpuCollection.Name)]
public sealed class HookAndCameraTests
{
    [Fact]
    public void RotatedCameraSeesAlongItsOwnForwardAxisWithCorrectLighting()
    {
        const int width = 480,
            height = 270;
        VulkanRenderer renderer = Gpu.Headless(width: width, height: height);
        try
        {
            MeshId sphere = renderer.CreateMesh(Primitives.Sphere(1f, 48, 24));
            MaterialId white = renderer.CreateMaterial(
                MaterialDesc.Color(new(x: 0.8f, y: 0.8f, z: 0.8f, w: 1), roughness: 0.75f)
            );
            MaterialId red = renderer.CreateMaterial(
                MaterialDesc.Color(new(x: 0.8f, y: 0.1f, z: 0.1f, w: 1), roughness: 0.75f)
            );
            // Turned 90 degrees to the left, the camera looks down −X. The sun shines from +Y, so the upper half of
            // the sphere in front is lit and the lower half is not, whatever the camera's rotation.
            RenderSnapshot snapshot = Gpu.Snapshot(Vector3.UnitY);
            var left = Quaternion.CreateFromAxisAngle(Vector3.UnitY, MathF.PI / 2);
            snapshot.Camera.Rotation = left;
            snapshot.Camera.PreviousRotation = left;
            snapshot.Instances.Add(
                Gpu.Instance(mesh: sphere, material: white, position: new(-6, 0, 0), scale: Vector3.One)
            );
            snapshot.Instances.Add(
                Gpu.Instance(mesh: sphere, material: red, position: new(0, 0, -6), scale: Vector3.One)
            );

            Capture image = Gpu.RenderAndCapture(renderer: renderer, snapshot: snapshot, name: "rotated-camera");
            renderer.LastFrameStats.VisibleInstances.Should().Be(1, "the red sphere is behind the turned camera");
            // In camera space the white sphere is straight ahead: project it as (0, 0, -6).
            Vector2 top = Gpu.Project(point: new(0, 0.7f, -5.4f), width: width, height: height);
            Vector2 bottom = Gpu.Project(point: new(0, -0.7f, -5.4f), width: width, height: height);
            image.Luma(top).Should().BeGreaterThan(0.5f, "the sun from +Y lights the top");
            image.Luma(bottom).Should().BeLessThan(0.25f, "the bottom faces away from the sun");
            Vector3 center = image.Color(width / 2, height / 2);
            MathF
                .Abs(center.X - center.Y)
                .Should()
                .BeLessThan(0.05f, "the white sphere is in front, not the red one: {0}", center);
        }
        finally
        {
            Gpu.DisposeAndAssertClean(renderer);
        }
    }

    [Fact]
    public void SteadyStateFramesDoNotAllocate()
    {
        VulkanRenderer renderer = Gpu.Headless(width: 320, height: 180);
        try
        {
            var scene = TestScene.Create(renderer);
            RenderSnapshot snapshot = scene.Snapshot();
            for (int i = 0; i < 5; i++)
                renderer.Render(snapshot, 0.5f);
            long allocated = Allocations.Fewest(() =>
            {
                for (int i = 0; i < 20; i++)
                    renderer.Render(snapshot, i / 20f);
            });
            allocated.Should().Be(0, "a steady state allocates nothing");
        }
        finally
        {
            Gpu.DisposeAndAssertClean(renderer);
        }
    }

    [Fact]
    public void PassesPrepareOnTheCpuBeforeTheFrameIsWrittenAndRecordInStageOrder()
    {
        VulkanRenderer renderer = Gpu.Headless(width: 160, height: 90);
        try
        {
            MeshId box = renderer.CreateMesh(Primitives.Box(new(0.5f)));
            MaterialId white = renderer.CreateMaterial(MaterialDesc.Color(Vector4.One, roughness: 0.38f));
            var log = new List<string>();
            var probe = new ProbePass(log);
            renderer.Graph.Add(FrameGraph.Stage.ShadowMaps, probe);
            RenderSnapshot snapshot = Gpu.Snapshot(Vector3.UnitY);
            snapshot.Instances.Add(
                Gpu.Instance(mesh: box, material: white, position: new(0, 0, -5), scale: Vector3.One)
            );
            renderer.Render(snapshot, 1f);

            log.Should().Equal("prepare", "record");
            // The appended instance and command reached the frame's buffers after the batched ones.
            renderer.LastScene.InstanceCount.Should().Be(2);
            probe.AppendedInstance.Should().Be(1);
            probe.AppendedCommand.Should().Be(1);
            probe.SeenSnapshot.Should().BeSameAs(snapshot);
            renderer.LastFrameStats.NearCommands.Should().Be(1);
        }
        finally
        {
            Gpu.DisposeAndAssertClean(renderer);
        }
    }

    private sealed class ProbePass(List<string> log) : RenderPass
    {
        public int AppendedInstance { get; private set; } = -1;
        public int AppendedCommand { get; private set; } = -1;
        public RenderSnapshot? SeenSnapshot { get; private set; }

        public override string Name => "probe";

        public override void Prepare(FrameContext context)
        {
            log.Add("prepare");
            SeenSnapshot = context.Snapshot;
            AppendedInstance = context.Scene.AppendInstance(
                GpuInstance.From(
                    position: new(0, 0, -3),
                    rotation: Quaternion.Identity,
                    scale: Vector3.One,
                    material: 0,
                    map: 0
                )
            );
            AppendedCommand = context.Scene.AppendCommand(
                new(
                    indexCount: 36,
                    instanceCount: 1,
                    firstIndex: 0,
                    vertexOffset: 0,
                    firstInstance: (uint)AppendedInstance
                )
            );
        }

        public override void Record(FrameContext context) => log.Add("record");
    }
}
