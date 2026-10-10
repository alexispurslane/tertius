using System.Numerics;
using Tertius.Rendering;

namespace Tertius.Rendering.Tests;

[Collection(GpuCollection.Name)]
public sealed class DeviceTests
{
    [Fact]
    public void HeadlessRendererCreatesAndDisposesCleanly()
    {
        VulkanRenderer renderer = Gpu.Headless(320, 180);
        renderer.DeviceName.Should().NotBeNullOrEmpty();
        renderer.OutputSize.Should().Be((320, 180));
        Gpu.DisposeAndAssertClean(renderer);
    }

    [Fact]
    public void EmptySceneRendersStarsOnBlack()
    {
        VulkanRenderer renderer = Gpu.Headless(320, 180);
        try
        {
            RenderSnapshot snapshot = Gpu.Snapshot(new Vector3(0, 1, 0));
            Capture image = Gpu.RenderAndCapture(renderer, snapshot, "empty");
            image.Width.Should().Be(320);
            image.Height.Should().Be(180);
            renderer.LastFrameStats.Rendered.Should().BeTrue();
            renderer.LastFrameStats.IndirectDrawCalls.Should().Be(0);

            // Mostly black space with a few stars.
            int bright = 0;
            float sum = 0;
            for (int y = 0; y < image.Height; y++)
            {
                for (int x = 0; x < image.Width; x++)
                {
                    float luma = image.Luma(x, y);
                    sum += luma;
                    if (luma > 0.1f)
                        bright++;
                }
            }
            (sum / (image.Width * image.Height)).Should().BeLessThan(0.05f, "space is mostly black");
            bright.Should().BeInRange(10, image.Width * image.Height / 50, "a few stars show");
        }
        finally
        {
            Gpu.DisposeAndAssertClean(renderer);
        }
    }

    [Fact]
    public void RenderingManyFramesWithEveryFramesInFlightSettingIsClean()
    {
        foreach (int framesInFlight in new[] { 1, 2, 3 })
        {
            VulkanRenderer renderer = Gpu.Headless(160, 90, framesInFlight);
            try
            {
                MeshId box = renderer.CreateMesh(Primitives.Box(new Vector3(0.5f)));
                MaterialId material = renderer.CreateMaterial(MaterialDesc.Color(new Vector4(0.8f, 0.3f, 0.2f, 1)));
                RenderSnapshot snapshot = Gpu.Snapshot(new Vector3(0.3f, 1, 0.5f));
                snapshot.Instances.Add(Gpu.Instance(box, material, new Vector3(0, 0, -4), Vector3.One));
                for (int i = 0; i < 8; i++)
                    renderer.Render(snapshot, 1f);
                renderer.LastFrameStats.NearCommands.Should().Be(1, "with {0} frames in flight", framesInFlight);
            }
            finally
            {
                Gpu.DisposeAndAssertClean(renderer);
            }
        }
    }

    [Fact]
    public void TheShadowMapSizeIsAnOptionWithALowerBound()
    {
        FluentActions.Invoking(() => VulkanRenderer.CreateHeadless(new RendererOptions { Width = 64, Height = 64, ShadowMapSize = 100 }))
            .Should().ThrowExactly<ArgumentOutOfRangeException>();
        VulkanRenderer renderer = VulkanRenderer.CreateHeadless(new RendererOptions { Validation = true, Width = 160, Height = 90, ShadowMapSize = 1024 });
        try
        {
            renderer.ShadowMap.Size.Should().Be(1024);
            renderer.ShadowMap.Image.ArrayLayers.Should().Be((uint)ShadowCascades.Count);
            MeshId box = renderer.CreateMesh(Primitives.Box(new Vector3(0.5f)));
            MaterialId white = renderer.CreateMaterial(MaterialDesc.Color(Vector4.One));
            RenderSnapshot snapshot = Gpu.Snapshot(new Vector3(0.3f, 1f, 0.2f));
            snapshot.Instances.Add(Gpu.Instance(box, white, new Vector3(0, 0, -5), Vector3.One, flags: RenderFlags.CastsShadow));
            renderer.Render(snapshot, 1f);
            renderer.LastFrameStats.ShadowCascades.Should().Be(4);
            renderer.ReadShadowCascade(0).Should().HaveCount(1024 * 1024);
        }
        finally
        {
            Gpu.DisposeAndAssertClean(renderer);
        }
    }
}
