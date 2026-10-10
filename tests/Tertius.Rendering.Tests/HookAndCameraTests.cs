using System.Numerics;
using Silk.NET.Vulkan;
using Tertius.Rendering;

namespace Tertius.Rendering.Tests;

[Collection(GpuCollection.Name)]
public sealed class HookAndCameraTests
{
    [Fact]
    public void RotatedCameraSeesAlongItsOwnForwardAxisWithCorrectLighting()
    {
        const int width = 480, height = 270;
        VulkanRenderer renderer = Gpu.Headless(width, height);
        try
        {
            MeshId sphere = renderer.CreateMesh(Primitives.Sphere(1f, 48, 24));
            MaterialId white = renderer.CreateMaterial(MaterialDesc.Color(new Vector4(0.8f, 0.8f, 0.8f, 1), gloss: 0.1f));
            MaterialId red = renderer.CreateMaterial(MaterialDesc.Color(new Vector4(0.8f, 0.1f, 0.1f, 1), gloss: 0.1f));
            // Turned 90 degrees to the left, the camera looks down −X. The sun shines from +Y, so the upper half of
            // the sphere in front is lit and the lower half is not, whatever the camera's rotation.
            RenderSnapshot snapshot = Gpu.Snapshot(Vector3.UnitY);
            Quaternion left = Quaternion.CreateFromAxisAngle(Vector3.UnitY, MathF.PI / 2);
            snapshot.Camera.Rotation = left;
            snapshot.Camera.PreviousRotation = left;
            snapshot.Instances.Add(Gpu.Instance(sphere, white, new Vector3(-6, 0, 0), Vector3.One));
            snapshot.Instances.Add(Gpu.Instance(sphere, red, new Vector3(0, 0, -6), Vector3.One));

            Capture image = Gpu.RenderAndCapture(renderer, snapshot, "rotated-camera");
            Assert.Equal(1, renderer.LastFrameStats.VisibleInstances);
            // In camera space the white sphere is straight ahead: project it as (0, 0, -6).
            Vector2 top = Gpu.Project(new Vector3(0, 0.7f, -5.4f), width, height);
            Vector2 bottom = Gpu.Project(new Vector3(0, -0.7f, -5.4f), width, height);
            Assert.True(image.Luma(top) > 0.5f, $"lit top {image.Luma(top)}");
            Assert.True(image.Luma(bottom) < 0.25f, $"unlit bottom {image.Luma(bottom)}");
            Vector3 center = image.Color(width / 2, height / 2);
            Assert.True(MathF.Abs(center.X - center.Y) < 0.05f, $"the white sphere is in front, not the red one: {center}");
        }
        finally
        {
            Gpu.DisposeAndAssertClean(renderer);
        }
    }

    [Fact]
    public void SteadyStateFramesDoNotAllocate()
    {
        VulkanRenderer renderer = Gpu.Headless(320, 180);
        try
        {
            var scene = TestScene.Create(renderer);
            RenderSnapshot snapshot = scene.Snapshot();
            for (int i = 0; i < 5; i++)
                renderer.Render(snapshot, 0.5f);
            long before = GC.GetAllocatedBytesForCurrentThread();
            for (int i = 0; i < 20; i++)
                renderer.Render(snapshot, i / 20f);
            long allocated = GC.GetAllocatedBytesForCurrentThread() - before;
            Assert.Equal(0, allocated);
        }
        finally
        {
            Gpu.DisposeAndAssertClean(renderer);
        }
    }

    [Fact]
    public void PassesPrepareOnTheCpuBeforeTheFrameIsWrittenAndRecordInStageOrder()
    {
        VulkanRenderer renderer = Gpu.Headless(160, 90);
        try
        {
            MeshId box = renderer.CreateMesh(Primitives.Box(new Vector3(0.5f)));
            MaterialId white = renderer.CreateMaterial(MaterialDesc.Color(Vector4.One));
            var log = new List<string>();
            var probe = new ProbePass(log);
            renderer.Graph.Add(FrameGraph.Stage.ShadowMaps, probe);
            RenderSnapshot snapshot = Gpu.Snapshot(Vector3.UnitY);
            snapshot.Instances.Add(Gpu.Instance(box, white, new Vector3(0, 0, -5), Vector3.One));
            renderer.Render(snapshot, 1f);

            Assert.Equal(["prepare", "record"], log);
            // The appended instance and command reached the frame's buffers after the batched ones.
            Assert.Equal(2, renderer.LastScene.InstanceCount);
            Assert.Equal(1, probe.AppendedInstance);
            Assert.Equal(1, probe.AppendedCommand);
            Assert.Same(snapshot, probe.SeenSnapshot);
            Assert.Equal(1, renderer.LastFrameStats.NearCommands);
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
            AppendedInstance = context.Scene.AppendInstance(GpuInstance.From(new Vector3(0, 0, -3), Quaternion.Identity, Vector3.One, 0));
            AppendedCommand = context.Scene.AppendCommand(new DrawIndexedIndirectCommand(36, 1, 0, 0, (uint)AppendedInstance));
        }

        public override void Record(FrameContext context) => log.Add("record");
    }
}
