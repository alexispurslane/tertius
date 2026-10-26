using System.Numerics;
using Tertius.Testing;

namespace Tertius.Rendering.Tests;

[Collection(GpuCollection.Name)]
public sealed class DeviceTests
{
    [Fact]
    public void HeadlessRendererCreatesAndDisposesCleanly()
    {
        VulkanRenderer renderer = Gpu.Headless(width: 320, height: 180);
        renderer.DeviceName.Should().NotBeNullOrEmpty();
        renderer.OutputSize.Should().Be((320, 180));
        Gpu.DisposeAndAssertClean(renderer);
    }

    [Fact]
    public void EmptySceneRendersStarsOnBlack()
    {
        // The stars are physical, at most about 3 · 2⁻¹⁰ W/(m²·sr) at their peaks, so 10 stops show them.
        VulkanRenderer renderer = Gpu.Headless(width: 320, height: 180, exposure: new FixedExposure(10));
        try
        {
            RenderSnapshot snapshot = Gpu.Snapshot(new(0, 1, 0));
            Capture image = Gpu.RenderAndCapture(renderer: renderer, snapshot: snapshot, name: "empty");
            image.Width.Should().Be(320);
            image.Height.Should().Be(180);
            renderer.LastFrameStats.Rendered.Should().BeTrue();
            renderer.LastFrameStats.IndirectDrawCalls.Should().Be(0);

            // Mostly black space with a few stars.
            int bright = 0;
            float sum = 0;
            for (int y = 0; y < image.Height; y++)
            for (int x = 0; x < image.Width; x++)
            {
                float luma = image.Luma(x, y);
                sum += luma;
                if (luma > 0.1f)
                    bright++;
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
    public void TheStarsStayFixedToTheRootFramesAxesWhateverFrameTheCameraRidesIn()
    {
        // 10 stops show the physical stars.
        VulkanRenderer renderer = Gpu.Headless(width: 320, height: 180, exposure: new FixedExposure(10));
        try
        {
            // The same view of the sky, from a camera in the root frame and from one in a frame turned by `turn`: in the
            // turned frame's axes, the camera's rotation and the root's axes are both turned back by it.
            Quaternion view =
                Quaternion.CreateFromAxisAngle(Vector3.UnitY, 0.5f)
                * Quaternion.CreateFromAxisAngle(Vector3.UnitX, 0.2f);
            var turn = Quaternion.CreateFromAxisAngle(Vector3.Normalize(new(1, 1, 0)), 0.7f);
            RenderSnapshot inRoot = Gpu.Snapshot(Vector3.UnitY, starIrradiance: 0);
            inRoot.Camera.Rotation = inRoot.Camera.PreviousRotation = view;
            RenderSnapshot inTurned = Gpu.Snapshot(Vector3.UnitY, starIrradiance: 0);
            inTurned.Camera.Rotation = inTurned.Camera.PreviousRotation = Quaternion.Conjugate(turn) * view;
            TestChains.UnderTurnedRoot(inTurned, Quaternion.Conjugate(turn));

            HashSet<(int X, int Y)> rootStars = Stars(
                Gpu.RenderAndCapture(renderer: renderer, snapshot: inRoot, name: "stars-root")
            );
            HashSet<(int X, int Y)> turnedStars = Stars(
                Gpu.RenderAndCapture(renderer: renderer, snapshot: inTurned, name: "stars-turned")
            );

            rootStars.Count.Should().BeGreaterThan(10);
            // Rounding can move a star's edge by a pixel.
            rootStars.Intersect(turnedStars).Count().Should().BeGreaterThan(rootStars.Count * 9 / 10);
            turnedStars.Count.Should().BeInRange(rootStars.Count * 9 / 10, rootStars.Count * 11 / 10);
        }
        finally
        {
            Gpu.DisposeAndAssertClean(renderer);
        }
    }

    private static HashSet<(int X, int Y)> Stars(Capture image)
    {
        var stars = new HashSet<(int X, int Y)>();
        for (int y = 0; y < image.Height; y++)
        for (int x = 0; x < image.Width; x++)
            if (image.Luma(x, y) > 0.1f)
                stars.Add((x, y));
        return stars;
    }

    [Fact]
    public void RenderingManyFramesWithEveryFramesInFlightSettingIsClean()
    {
        foreach (int framesInFlight in new[] { 1, 2, 3 })
        {
            VulkanRenderer renderer = Gpu.Headless(width: 160, height: 90, framesInFlight: framesInFlight);
            try
            {
                MeshId box = renderer.CreateMesh(Primitives.Box(new(0.5f)));
                MaterialId material = renderer.CreateMaterial(
                    MaterialDesc.Color(new(x: 0.8f, y: 0.3f, z: 0.2f, w: 1), roughness: 0.38f)
                );
                RenderSnapshot snapshot = Gpu.Snapshot(new(0.3f, 1, 0.5f));
                snapshot.Instances.Add(
                    Gpu.Instance(mesh: box, material: material, position: new(0, 0, -4), scale: Vector3.One)
                );
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
        FluentActions
            .Invoking(() =>
                VulkanRenderer.CreateHeadless(
                    new()
                    {
                        Width = 64,
                        Height = 64,
                        ShadowMapSize = 100,
                        AntiAliasing = AntiAliasing.None,
                    }
                )
            )
            .Should()
            .ThrowExactly<ArgumentOutOfRangeException>();
        var renderer = VulkanRenderer.CreateHeadless(
            new()
            {
                Validation = true,
                Width = 160,
                Height = 90,
                ShadowMapSize = 1024,
                Bloom = new NoBloom(),
                AntiAliasing = AntiAliasing.None,
            }
        );
        try
        {
            renderer.ShadowMap.Size.Should().Be(1024);
            renderer.ShadowMap.Image.ArrayLayers.Should().Be(ShadowCascades.Count);
            MeshId box = renderer.CreateMesh(Primitives.Box(new(0.5f)));
            MaterialId white = renderer.CreateMaterial(MaterialDesc.Color(Vector4.One, roughness: 0.38f));
            RenderSnapshot snapshot = Gpu.Snapshot(new(0.3f, 1f, 0.2f));
            snapshot.Instances.Add(
                Gpu.Instance(
                    mesh: box,
                    material: white,
                    position: new(0, 0, -5),
                    scale: Vector3.One,
                    flags: RenderFlags.CastsShadow
                )
            );
            renderer.Render(snapshot, 1f);
            renderer.LastFrameStats.ShadowCascades.Should().Be(4);
            renderer.ReadShadowCascade(set: 0, cascade: 0).Should().HaveCount(1024 * 1024);
        }
        finally
        {
            Gpu.DisposeAndAssertClean(renderer);
        }
    }
}
