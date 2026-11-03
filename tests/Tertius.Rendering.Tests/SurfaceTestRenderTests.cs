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

    [Theory]
    [InlineData(100_000f)]
    [InlineData(10_000_000f)]
    public void AStillSurfaceFarBeyondAHalfFloatsRangeFindsItsOwnSurfaceInTheLastFrame(float distance)
    {
        VulkanRenderer renderer = Gpu.Headless(width: Width, height: Height);
        try
        {
            MeshId cube = renderer.CreateMesh(Primitives.Box(new(0.5f)));
            MaterialId white = renderer.CreateMaterial(MaterialDesc.Color(Vector4.One, roughness: 0.5f));
            // A planet's ground seen from orbit: a face that fills the view, past a half float's 65,504 m.
            RenderSnapshot scene = Gpu.Snapshot(new(0.3f, 1f, 0.4f));
            scene.Instances.Add(
                Gpu.Instance(
                    mesh: cube,
                    material: white,
                    position: new(0, 0, -distance),
                    scale: new(3 * distance, 2 * distance, 1),
                    chainDepth: TestChains.CamerasFrame,
                    flags: RenderFlags.Celestial,
                    radius: 2 * distance,
                    entity: 1
                )
            );
            renderer.Render(scene, 1f);
            scene.Time += Tick;

            renderer.Render(scene, 1f);

            byte[] bits = renderer.ReadSurfaceTest();
            float[] eye = renderer.ReadDistance();
            eye.Should().OnlyContain(value => value > 0, "the face fills the view");
            for (int i = 0; i < bits.Length; i++)
                (bits[i] & 1).Should().Be(1, "pixel {0} lies at its own last place, {1} m away", i, distance);
        }
        finally
        {
            Gpu.DisposeAndAssertClean(renderer);
        }
    }

    [Fact]
    public void EveryPixelOfAStillWallTurnedAwayFromTheCameraFindsItsOwnSurfaceInTheLastFrame()
    {
        VulkanRenderer renderer = Gpu.Headless(width: Width, height: Height);
        try
        {
            MeshId cube = renderer.CreateMesh(Primitives.Box(new(0.5f)));
            MaterialId white = renderer.CreateMaterial(MaterialDesc.Color(Vector4.One, roughness: 0.5f));
            // Its normal leans 40° away from the view axis, toward both right and up, so every pixel's point and normal
            // depend on both axes of the last frame's view ray.
            RenderSnapshot scene = Gpu.Snapshot(new(0.3f, 1f, 0.4f));
            scene.Instances.Add(
                Gpu.Instance(
                    mesh: cube,
                    material: white,
                    position: new(0, 0, -20),
                    scale: new(80, 80, 1),
                    rotation: Quaternion.CreateFromAxisAngle(Vector3.Normalize(new(1, -1, 0)), 40 * MathF.PI / 180),
                    chainDepth: TestChains.CamerasFrame,
                    entity: 1
                )
            );
            renderer.Render(scene, 1f);
            scene.Time += Tick;

            renderer.Render(scene, 1f);

            byte[] bits = renderer.ReadSurfaceTest();
            float[] distance = renderer.ReadDistance();
            for (int i = 0; i < bits.Length; i++)
                if (distance[i] > 0)
                    (bits[i] & 1).Should().Be(1, "pixel {0} lies at its own last place on the turned wall", i);
        }
        finally
        {
            Gpu.DisposeAndAssertClean(renderer);
        }
    }

    [Fact]
    public void AStillSurfaceOfTheSameRoughnessPassesTheSpecularTest()
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

            byte bits = renderer.ReadSurfaceTest()[Height / 2 * Width + Width / 2];
            (bits & 0x11).Should().Be(0x11, "the box's first tap passes the surface test and keeps its roughness");
        }
        finally
        {
            Gpu.DisposeAndAssertClean(renderer);
        }
    }

    /// <summary>
    /// The surface histories alternate by the parity of the drawn frames, so the third frame tests against the second
    /// frame's surfaces, not the first frame's.
    /// </summary>
    [Fact]
    public void TheThirdFrameFindsTheSurfacesOfTheSecondFrameNotOfTheFirst()
    {
        VulkanRenderer renderer = Gpu.Headless(width: Width, height: Height);
        try
        {
            MeshId cube = renderer.CreateMesh(Primitives.Box(new(0.5f)));
            MaterialId white = renderer.CreateMaterial(MaterialDesc.Color(Vector4.One, roughness: 0.5f));
            renderer.Render(Scene(cube: cube, wall: white, paint: white, box: new(0, 0, -6)), 1f);
            RenderSnapshot moved = Scene(cube: cube, wall: white, paint: white, box: new(1.5f, 0, -6), later: Tick);
            renderer.Render(moved, 1f);
            moved.Time += Tick;

            renderer.Render(moved, 1f);

            // The wall where the box stood in the first frame: the second frame drew the wall there.
            Vector2 uncovered = Gpu.Project(point: new(0, 0, -20), width: Width, height: Height);
            (renderer.ReadSurfaceTest()[(int)uncovered.Y * Width + (int)uncovered.X] & 1).Should().Be(1);
        }
        finally
        {
            Gpu.DisposeAndAssertClean(renderer);
        }
    }

    /// <summary>
    /// A wall 20 m ahead that fills the view, seen in a first frame by a camera that looks down −Z and in a second frame
    /// by one turned about +Y so that the pixel in column <paramref name="column"/> of the middle row finds its point at
    /// <paramref name="lastPlace"/> pixels from the left of the last frame's image. Returns that pixel's pass bits.
    /// </summary>
    private static byte BitsAfterATurn(VulkanRenderer renderer, int column, float lastPlace)
    {
        MeshId cube = renderer.CreateMesh(Primitives.Box(new(0.5f)));
        MaterialId white = renderer.CreateMaterial(MaterialDesc.Color(Vector4.One, roughness: 0.5f));
        RenderSnapshot scene = Scene(cube: cube, wall: white, paint: white, box: new(0, 0, 10));
        renderer.Render(scene, 1f);
        float tanX = MathF.Tan(Gpu.Fov / 2) * Width / Height;
        float now = MathF.Atan(((column + 0.5f) / Width * 2 - 1) * tanX);
        float last = MathF.Atan((lastPlace / Width * 2 - 1) * tanX);
        var turned = Quaternion.CreateFromAxisAngle(Vector3.UnitY, now - last);
        scene.Camera.Rotation = turned;
        scene.Camera.PreviousRotation = turned;
        scene.Time += Tick;

        renderer.Render(scene, 1f);

        int pixel = Height / 2 * Width + column;
        float motion = renderer.ReadMotion()[4 * pixel] * Width;
        motion.Should().BeApproximately(lastPlace - (column + 0.5f), 0.05f, "the turn moves the pixel's point there");
        return renderer.ReadSurfaceTest()[pixel];
    }

    [Fact]
    public void APixelWhoseLastPlaceLiesInTheFirstHalfTexelOfTheLastImageTestsOnlyTheTexelsInsideIt()
    {
        VulkanRenderer renderer = Gpu.Headless(width: Width, height: Height);
        try
        {
            byte bits = BitsAfterATurn(renderer: renderer, column: 0, lastPlace: 0.25f);

            (bits & SurfaceBits)
                .Should()
                .Be(0b1010, "the 2 × 2 texels around the place are columns −1 and 0, and only column 0 lies inside");
        }
        finally
        {
            Gpu.DisposeAndAssertClean(renderer);
        }
    }

    [Theory]
    [InlineData(0, -0.25f)]
    [InlineData(Width - 1, Width + 0.25f)]
    public void APixelWhoseLastPlaceLiesJustOutsideTheLastImageFindsNoSurface(int column, float lastPlace)
    {
        VulkanRenderer renderer = Gpu.Headless(width: Width, height: Height);
        try
        {
            byte bits = BitsAfterATurn(renderer: renderer, column: column, lastPlace: lastPlace);

            bits.Should().Be(0, "the last frame never saw the point, though two of the four texels lie inside");
        }
        finally
        {
            Gpu.DisposeAndAssertClean(renderer);
        }
    }

    [Fact]
    public void ABoxThatComesNearerFindsItsOwnSurfaceInTheLastFrame()
    {
        VulkanRenderer renderer = Gpu.Headless(width: Width, height: Height);
        try
        {
            MeshId cube = renderer.CreateMesh(Primitives.Box(new(0.5f)));
            MaterialId white = renderer.CreateMaterial(MaterialDesc.Color(Vector4.One, roughness: 0.5f));
            renderer.Render(Scene(cube: cube, wall: white, paint: white, box: new(0, 0, -7)), 1f);

            renderer.Render(Scene(cube: cube, wall: white, paint: white, box: new(0, 0, -6), later: Tick), 1f);

            (renderer.ReadSurfaceTest()[Height / 2 * Width + Width / 2] & 1)
                .Should()
                .Be(1, "the motion's depth ratio puts the point 1 m farther in the last frame, where the box was");
        }
        finally
        {
            Gpu.DisposeAndAssertClean(renderer);
        }
    }

    [Fact]
    public void EverySkyPixelFindsNoSurfaceInTheLastFrame()
    {
        VulkanRenderer renderer = Gpu.Headless(width: Width, height: Height);
        try
        {
            MeshId cube = renderer.CreateMesh(Primitives.Box(new(0.5f)));
            MaterialId white = renderer.CreateMaterial(MaterialDesc.Color(Vector4.One, roughness: 0.5f));
            RenderSnapshot scene = Gpu.Snapshot(new(0.3f, 1f, 0.4f));
            scene.Instances.Add(
                Gpu.Instance(
                    mesh: cube,
                    material: white,
                    position: new(0, 0, -6),
                    scale: Vector3.One,
                    chainDepth: TestChains.CamerasFrame,
                    entity: 2
                )
            );
            renderer.Render(scene, 1f);
            scene.Time += Tick;

            renderer.Render(scene, 1f);

            byte[] bits = renderer.ReadSurfaceTest();
            float[] distance = renderer.ReadDistance();
            distance[0].Should().Be(0, "the corner shows the sky");
            for (int i = 0; i < bits.Length; i++)
                if (distance[i] <= 0)
                    bits[i].Should().Be(0, "sky pixel {0} has no surface to find", i);
        }
        finally
        {
            Gpu.DisposeAndAssertClean(renderer);
        }
    }

    [Fact]
    public void TheRightmostColumnOfAStillBoxFindsNoSurfaceInTheFartherWallBesideIt()
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

            float[] distance = renderer.ReadDistance();
            int row = Height / 2 * Width;
            int column = Enumerable.Range(0, Width - 1).Last(x => distance[row + x] < 10 && distance[row + x + 1] > 10);
            (renderer.ReadSurfaceTest()[row + column] & SurfaceBits)
                .Should()
                .Be(0b0101, "taps 1 and 3 hold the wall, 14 m behind the box's plane");
        }
        finally
        {
            Gpu.DisposeAndAssertClean(renderer);
        }
    }

    [Fact]
    public void ATapAcrossAFoldOfThirtyDegreesFailsTheNormalTest()
    {
        VulkanRenderer renderer = Gpu.Headless(width: Width, height: Height);
        try
        {
            MeshId cube = renderer.CreateMesh(Primitives.Box(new(0.5f)));
            MaterialId white = renderer.CreateMaterial(MaterialDesc.Color(Vector4.One, roughness: 0.5f));
            // Two walls that meet in a fold at x = 0, each turned 15° toward the camera, so their normals differ by 30°,
            // more than the normal test's 25°. Each reaches 2 m past the fold, behind the other.
            RenderSnapshot scene = Gpu.Snapshot(new(0.3f, 1f, 0.4f));
            var fold = new Vector3(0, 0, -20);
            foreach ((float angle, uint entity) in (ReadOnlySpan<(float Angle, uint Entity)>)[(15f, 1u), (-15f, 2u)])
            {
                var turn = Quaternion.CreateFromAxisAngle(Vector3.UnitY, angle * MathF.PI / 180);
                Vector3 along = Vector3.Transform(Vector3.UnitX, turn) * MathF.Sign(angle);
                scene.Instances.Add(
                    Gpu.Instance(
                        mesh: cube,
                        material: white,
                        position: fold - along * 28,
                        scale: new(60, 40, 1),
                        rotation: turn,
                        chainDepth: TestChains.CamerasFrame,
                        entity: entity
                    )
                );
            }
            renderer.Render(scene, 1f);
            scene.Time += Tick;

            renderer.Render(scene, 1f);

            // Pixel 159's center lies half a pixel left of the fold, and its tap 1, column 160, half a pixel right of it.
            (renderer.ReadSurfaceTest()[Height / 2 * Width + Width / 2 - 1] & SurfaceBits)
                .Should()
                .Be(0b0101, "taps 1 and 3 lie on the other wall, near the pixel's plane but 30° off its normal");
        }
        finally
        {
            Gpu.DisposeAndAssertClean(renderer);
        }
    }

    [Theory]
    [InlineData(1f, 0f)]
    [InlineData(0f, 1f)]
    public void EveryTapOfAStillWallSeenAtAGrazingAngleThatLiesOnTheWallPasses(float axisX, float axisY)
    {
        VulkanRenderer renderer = Gpu.Headless(width: Width, height: Height);
        try
        {
            MeshId cube = renderer.CreateMesh(Primitives.Box(new(0.5f)));
            MaterialId white = renderer.CreateMaterial(MaterialDesc.Color(Vector4.One, roughness: 0.5f));
            // Turned 80° about the axis, so the view depth changes fast from texel to texel, and a tap's point depends
            // on the last frame's view ray through it. A slab of 1 cm shows no side face.
            RenderSnapshot scene = Gpu.Snapshot(new(0.3f, 1f, 0.4f));
            scene.Instances.Add(
                Gpu.Instance(
                    mesh: cube,
                    material: white,
                    position: new(0, 0, -20),
                    scale: new(200, 200, 0.01f),
                    rotation: Quaternion.CreateFromAxisAngle(new(axisX, axisY, 0), 80 * MathF.PI / 180),
                    chainDepth: TestChains.CamerasFrame,
                    entity: 1
                )
            );
            renderer.Render(scene, 1f);
            scene.Time += Tick;

            renderer.Render(scene, 1f);

            byte[] bits = renderer.ReadSurfaceTest();
            float[] distance = renderer.ReadDistance();
            int checkedPixels = 0;
            for (int y = 0; y < Height - 1; y++)
            for (int x = 0; x < Width - 1; x++)
            {
                int i = y * Width + x;
                if (
                    distance[i] <= 0
                    || distance[i + 1] <= 0
                    || distance[i + Width] <= 0
                    || distance[i + Width + 1] <= 0
                )
                    continue;
                (bits[i] & SurfaceBits)
                    .Should()
                    .Be(SurfaceBits, "all four taps of pixel ({0}, {1}) lie on the wall", x, y);
                checkedPixels++;
            }
            checkedPixels.Should().BeGreaterThan(Width * Height / 4, "the wall covers much of the view");
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
    public void TheSurfaceHistoryHoldsEachPixelsNormalAndRoughness()
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
            // The box's front face looks along +Z, whose octahedral code is (0, 0).
            history[center + 1].Should().BeApproximately(0, 1f / 1024);
            history[center + 2].Should().BeApproximately(0, 1f / 1024);
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
