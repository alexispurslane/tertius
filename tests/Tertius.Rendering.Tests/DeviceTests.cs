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
        Assert.False(string.IsNullOrEmpty(renderer.DeviceName));
        Assert.Equal((320, 180), renderer.OutputSize);
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
            Assert.Equal(320, image.Width);
            Assert.Equal(180, image.Height);
            Assert.True(renderer.LastFrameStats.Rendered);
            Assert.Equal(0, renderer.LastFrameStats.IndirectDrawCalls);

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
            Assert.True(sum / (image.Width * image.Height) < 0.05f, $"mean luma {sum / (image.Width * image.Height)}");
            Assert.InRange(bright, 10, image.Width * image.Height / 50);
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
                Assert.Equal(1, renderer.LastFrameStats.NearCommands);
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
        Assert.Throws<ArgumentOutOfRangeException>(() => VulkanRenderer.CreateHeadless(new RendererOptions { Width = 64, Height = 64, ShadowMapSize = 100 }));
        VulkanRenderer renderer = VulkanRenderer.CreateHeadless(new RendererOptions { Validation = true, Width = 160, Height = 90, ShadowMapSize = 1024 });
        try
        {
            Assert.Equal(1024, renderer.ShadowMap.Size);
            Assert.Equal((uint)ShadowCascades.Count, renderer.ShadowMap.Image.ArrayLayers);
            MeshId box = renderer.CreateMesh(Primitives.Box(new Vector3(0.5f)));
            MaterialId white = renderer.CreateMaterial(MaterialDesc.Color(Vector4.One));
            RenderSnapshot snapshot = Gpu.Snapshot(new Vector3(0.3f, 1f, 0.2f));
            snapshot.Instances.Add(Gpu.Instance(box, white, new Vector3(0, 0, -5), Vector3.One, flags: RenderFlags.CastsShadow));
            renderer.Render(snapshot, 1f);
            Assert.Equal(4, renderer.LastFrameStats.ShadowCascades);
            Assert.Equal(1024 * 1024, renderer.ReadShadowCascade(0).Length);
        }
        finally
        {
            Gpu.DisposeAndAssertClean(renderer);
        }
    }
}
