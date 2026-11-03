using System.Numerics;
using Tertius.Testing;

namespace Tertius.Rendering.Tests;

/// <summary>
/// The surface test (docs/plans/temporal-renderer.md, section 8, "The surface test"): once per frame, each pixel finds
/// its surface's previous place and tests the 2 × 2 texels of the last surface history around it. The same pass writes
/// this frame's surface history and the device depth that FSR reads. Each test reads the pass's images after a second
/// frame, one tick after the first, so that it continues the first frame: a second draw of the same time is a still, which
/// resets every history (docs/plans/temporal-renderer.md, "The still rule").
/// </summary>
[Collection(GpuCollection.Name)]
public sealed class SurfaceTestRenderTests
{
    private const int Width = 320,
        Height = 180;

    /// <summary>The step of the second frame: one tick, which continues the first frame.</summary>
    private const double Tick = 1.0 / 60.0;

    /// <summary>The bits of the surface test image that hold the four taps of the plane and normal test.</summary>
    private const byte SurfaceBits = 0x0F;

    /// <summary>
    /// A wall 20 m ahead that fills the view, and a box in front of it at <paramref name="box"/>, at the snapshot time of
    /// <see cref="Gpu.Snapshot"/> plus <paramref name="later"/>, in s.
    /// </summary>
    private static RenderSnapshot Scene(MeshId cube, MaterialId wall, MaterialId paint, Vector3 box, double later = 0)
    {
        RenderSnapshot snapshot = Gpu.Snapshot(new(0.3f, 1f, 0.4f));
        snapshot.Time += later;
        snapshot.Instances.Add(
            Gpu.Instance(
                mesh: cube,
                material: wall,
                position: new(0, 0, -20),
                scale: new(60, 40, 1),
                chainDepth: TestChains.CamerasFrame,
                entity: 1
            )
        );
        snapshot.Instances.Add(
            Gpu.Instance(
                mesh: cube,
                material: paint,
                position: box,
                scale: Vector3.One,
                chainDepth: TestChains.CamerasFrame,
                entity: 2
            )
        );
        return snapshot;
    }

    [Fact]
    public void EveryPixelOfAStillSceneFindsItsOwnSurfaceInTheLastFrame()
    {
        VulkanRenderer renderer = Gpu.Headless(width: Width, height: Height);
        try
        {
            MeshId cube = renderer.CreateMesh(Primitives.Box(new(0.5f)));
            MaterialId white = renderer.CreateMaterial(MaterialDesc.Color(Vector4.One, roughness: 0.5f));
            RenderSnapshot scene = Scene(cube: cube, wall: white, paint: white, box: new(0, 0, -6));
            renderer.Render(scene, 1f);
            scene.Time += Tick;

            renderer.Render(scene, 1f);

            byte[] bits = renderer.ReadSurfaceTest();
            float[] distance = renderer.ReadDistance();
            for (int i = 0; i < bits.Length; i++)
                if (distance[i] > 0)
                    (bits[i] & 1)
                        .Should()
                        .Be(1, "pixel {0} lies at its own last place, whose texel is the first tap", i);
        }
        finally
        {
            Gpu.DisposeAndAssertClean(renderer);
        }
    }

    [Fact]
    public void AFrameThatResetsFindsNoSurfaceInTheLastFrame()
    {
        VulkanRenderer renderer = Gpu.Headless(width: Width, height: Height);
        try
        {
            MeshId cube = renderer.CreateMesh(Primitives.Box(new(0.5f)));
            MaterialId white = renderer.CreateMaterial(MaterialDesc.Color(Vector4.One, roughness: 0.5f));
            RenderSnapshot scene = Scene(cube: cube, wall: white, paint: white, box: new(0, 0, -6));
            renderer.Render(scene, 1f);
            scene.CameraCuts++;

            renderer.Render(scene, 1f);

            renderer.ReadSurfaceTest().Should().AllBeEquivalentTo((byte)0, "a cut resets every history");
        }
        finally
        {
            Gpu.DisposeAndAssertClean(renderer);
        }
    }

    [Fact]
    public void ABoxThatMovesUncoversPixelsThatFindNoSurfaceInTheLastFrame()
    {
        VulkanRenderer renderer = Gpu.Headless(width: Width, height: Height);
        try
        {
            MeshId cube = renderer.CreateMesh(Primitives.Box(new(0.5f)));
            MaterialId white = renderer.CreateMaterial(MaterialDesc.Color(Vector4.One, roughness: 0.5f));
            renderer.Render(Scene(cube: cube, wall: white, paint: white, box: new(0, 0, -6)), 1f);

            renderer.Render(Scene(cube: cube, wall: white, paint: white, box: new(1.5f, 0, -6), later: Tick), 1f);

            byte[] bits = renderer.ReadSurfaceTest();
            // The wall where the box stood: its last texel there held the box, 14 m nearer.
            Vector2 uncovered = Gpu.Project(point: new(0, 0, -20), width: Width, height: Height);
            (bits[(int)uncovered.Y * Width + (int)uncovered.X] & SurfaceBits).Should().Be(0);
            // The wall far from both places of the box keeps its history.
            Vector2 open = Gpu.Project(point: new(-6, 3, -20), width: Width, height: Height);
            (bits[(int)open.Y * Width + (int)open.X] & 1).Should().Be(1);
            // The box itself moved with its motion vector to its own last place.
            Vector2 box = Gpu.Project(point: new(1.5f, 0, -5.5f), width: Width, height: Height);
            (bits[(int)box.Y * Width + (int)box.X] & SurfaceBits).Should().NotBe(0);
        }
        finally
        {
            Gpu.DisposeAndAssertClean(renderer);
        }
    }

    [Fact]
    public void ASurfaceWhoseRoughnessChangedPassesTheSurfaceTestAndFailsTheSpecularTest()
    {
        VulkanRenderer renderer = Gpu.Headless(width: Width, height: Height);
        try
        {
            MeshId cube = renderer.CreateMesh(Primitives.Box(new(0.5f)));
            MaterialId wall = renderer.CreateMaterial(MaterialDesc.Color(Vector4.One, roughness: 0.5f));
            MaterialId smooth = renderer.CreateMaterial(MaterialDesc.Color(Vector4.One, roughness: 0.2f));
            MaterialId rough = renderer.CreateMaterial(MaterialDesc.Color(Vector4.One, roughness: 0.6f));
            renderer.Render(Scene(cube: cube, wall: wall, paint: smooth, box: new(0, 0, -6)), 1f);

            renderer.Render(Scene(cube: cube, wall: wall, paint: rough, box: new(0, 0, -6), later: Tick), 1f);

            byte bits = renderer.ReadSurfaceTest()[Height / 2 * Width + Width / 2];
            (bits & 1).Should().Be(1, "the box stood still");
            (bits & 0x10).Should().Be(0, "its roughness rose by 0.4, beyond the specular test's 0.1");
        }
        finally
        {
            Gpu.DisposeAndAssertClean(renderer);
        }
    }

    [Fact]
    public void TheSurfaceHistoryHoldsEachPixelsViewDepthAndRoughness()
    {
        VulkanRenderer renderer = Gpu.Headless(width: Width, height: Height);
        try
        {
            MeshId cube = renderer.CreateMesh(Primitives.Box(new(0.5f)));
            MaterialId wall = renderer.CreateMaterial(MaterialDesc.Color(Vector4.One, roughness: 0.5f));
            MaterialId paint = renderer.CreateMaterial(MaterialDesc.Color(Vector4.One, roughness: 0.3f));

            renderer.Render(Scene(cube: cube, wall: wall, paint: paint, box: new(0, 0, -6)), 1f);

            float[] history = renderer.ReadSurfaceHistory();
            int center = 4 * (Height / 2 * Width + Width / 2);
            history[center].Should().BeApproximately(5.5f, 5.5f / 1024, "the box's front face is 5.5 m ahead");
            // The specular target keeps the roughness in 8 bits, and the history as a half float.
            history[center + 3].Should().BeApproximately(0.3f, 0.5f / 255 + 1f / 2048);
        }
        finally
        {
            Gpu.DisposeAndAssertClean(renderer);
        }
    }

    [Fact]
    public void TheDeviceDepthIsReversedAndInfiniteAndZeroInTheSky()
    {
        VulkanRenderer renderer = Gpu.Headless(width: Width, height: Height);
        try
        {
            MeshId cube = renderer.CreateMesh(Primitives.Box(new(0.5f)));
            MaterialId white = renderer.CreateMaterial(MaterialDesc.Color(Vector4.One, roughness: 0.5f));
            RenderSnapshot snapshot = Gpu.Snapshot(new(0.3f, 1f, 0.4f));
            snapshot.Instances.Add(
                Gpu.Instance(
                    mesh: cube,
                    material: white,
                    position: new(0, 0, -6),
                    scale: Vector3.One,
                    chainDepth: TestChains.CamerasFrame,
                    entity: 2
                )
            );

            renderer.Render(snapshot, 1f);

            float[] depth = renderer.ReadDeviceDepth();
            depth[Height / 2 * Width + Width / 2]
                .Should()
                .BeApproximately(SurfaceTestPass.DeviceDepthNear / 5.5f, 1e-6f);
            depth[0].Should().Be(0, "the corner shows the sky");
        }
        finally
        {
            Gpu.DisposeAndAssertClean(renderer);
        }
    }
}
