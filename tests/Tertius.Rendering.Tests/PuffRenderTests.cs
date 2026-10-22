using System.Numerics;

namespace Tertius.Rendering.Tests;

/// <summary>
/// Glow puffs on the GPU: snapshot tests of the transparent pass's puff sprites, in real headless captures at alpha 1. The
/// camera is at the render origin with identity rotation, so it looks down −Z with +Y up. Each test renders its scene
/// twice, without and with its puffs, so that it can measure what the puffs add, and checks the image with its puffs
/// against its reference image.
/// </summary>
[Collection(GpuCollection.Name)]
public sealed class PuffRenderTests
{
    private const int Width = 640, Height = 360;

    /// <summary>The camera of puffs-soft.png: turned down by 0.3 rad, so that it sees the floor at a fair angle.</summary>
    private static readonly Quaternion SoftCamera = Gpu.PitchDown(0.3f);

    private static Vector2 Pixel(Vector3 point) => Gpu.Project(point, Width, Height);

    /// <summary>A still puff of look <paramref name="look"/>.</summary>
    private static RenderPuff Puff(Vector3 position, float age, int look = 0) =>
        new() { Position = position, PreviousPosition = position, Age = age, Look = look };

    /// <summary>Renders <paramref name="snapshot"/> once without its puffs and once with them.</summary>
    private static (Capture Without, Capture With) RenderBoth(VulkanRenderer renderer, RenderSnapshot snapshot,
        string name)
    {
        List<RenderPuff> puffs = [.. snapshot.Puffs];
        snapshot.Puffs.Clear();
        Capture without = Gpu.RenderAndCapture(renderer, snapshot, name + "-without");
        snapshot.Puffs.AddRange(puffs);
        Capture with = Gpu.RenderAndCapture(renderer, snapshot, name);
        return (without, with);
    }

    /// <summary>Checks the image with the puffs against the reference image <paramref name="name"/>, after the measurements.</summary>
    private static void Verify(Capture with, string name) =>
        ReferenceImages.Verify(with, Path.Combine(ReferenceImages.Folder, name + ".png"));

    /// <summary>The linear light that the puffs added at a pixel, as luma: the render with them less the render without.</summary>
    private static float Added(Capture with, Capture without, Vector2 pixel) =>
        LinearLuma(with.Color(pixel)) - LinearLuma(without.Color(pixel));

    private static float LinearLuma(Vector3 display)
    {
        Vector3 c = Measure.ToLinear(display);
        return 0.2126f * c.X + 0.7152f * c.Y + 0.0722f * c.Z;
    }

    /// <summary>
    /// The scene of puffs-glow.png: a dark ground 2 m below the eye at dusk, under a weak sun from behind the camera, and
    /// a trail of nine orange glow puffs that rises from the lower left, 8 m ahead and in front of the ground, to the upper
    /// right, 14 m ahead and against the sky. The puffs at the lower left are young and small, and those at the upper right
    /// are old, large and faint.
    /// </summary>
    private static RenderSnapshot GlowScene(VulkanRenderer renderer)
    {
        MeshId plane = renderer.CreateMesh(Primitives.Plane(1f));
        MaterialId ground = renderer.CreateMaterial(MaterialDesc.Color(new(0.08f, 0.08f, 0.08f, 1), gloss: 0.1f) with
        {
            SpecularIntensity = 0f
        });
        RenderSnapshot snapshot = Gpu.Snapshot(new(0.3f, 0.4f, 0.8f), sunIntensity: 0.5f);
        snapshot.Instances.Add(Gpu.Instance(plane, ground, new(0, -2, -30), new(40, 1, 40)));
        snapshot.PuffLooks.Add(new(Life: 2f, StartRadius: 0.4f, EndRadius: 1.2f, TemperatureRatio: 3f,
            Shading: PuffShading.Glow(new(3f, 1.2f, 0.3f))));
        for (int i = 0; i < 9; i++)
        {
            float share = i / 8f;
            snapshot.Puffs.Add(Puff(new(-4.5f + 9 * share, -1.3f + 4.6f * share, -8 - 6 * share),
                age: 0.1f + 1.7f * share));
        }

        return snapshot;
    }

    [Fact]
    public void GlowPuffsOverADarkGroundAndAgainstTheSkyOnlyBrighten()
    {
        VulkanRenderer renderer = Gpu.Headless(Width, Height);
        try
        {
            RenderSnapshot snapshot = GlowScene(renderer);
            (Capture without, Capture with) = RenderBoth(renderer, snapshot, "puffs-glow");

            renderer.LastFrameStats.Puffs.Should().Be(9);
            for (int y = 0; y < Height; y++)
            for (int x = 0; x < Width; x++)
            {
                Vector3 added = with.Color(x, y) - without.Color(x, y);
                // One level of the 8-bit output for the rounding.
                MathF.Min(added.X, MathF.Min(added.Y, added.Z)).Should().BeGreaterThanOrEqualTo(-1.5f / 255,
                    "additive puffs only brighten, at pixel ({0}, {1})", x, y);
            }

            Vector3 first = snapshot.Puffs[0].Position, last = snapshot.Puffs[8].Position;
            Added(with, without, Pixel(first)).Should()
                .BeGreaterThan(0.05f, "the young puff in front of the ground glows");
            Added(with, without, Pixel(last)).Should()
                .BeGreaterThan(0.005f, "the old puff against the sky still glows, faintly");
            Verify(with, "puffs-glow");
        }
        finally
        {
            Gpu.DisposeAndAssertClean(renderer);
        }
    }

    /// <summary>
    /// The scene of puffs-glass.png: a gray ground 2 m below the eye under a weak sun, and a pane of glass of alpha 0.2, 8 m
    /// ahead, that covers the left half of the view. Two puffs 12 m ahead, one behind the pane and one to its right in the
    /// open, and two puffs 5 m ahead, one in front of the pane and one to its right. The four have one look, and the two of
    /// each pair have the same size on the screen.
    /// </summary>
    private static RenderSnapshot GlassScene(VulkanRenderer renderer, out Vector3[] behind, out Vector3[] inFront)
    {
        MeshId box = renderer.CreateMesh(Primitives.Box(new(0.5f)));
        MeshId plane = renderer.CreateMesh(Primitives.Plane(1f));
        MaterialId ground = renderer.CreateMaterial(MaterialDesc.Color(new(0.3f, 0.3f, 0.3f, 1), gloss: 0.2f) with
        {
            SpecularIntensity = 0f
        });
        MaterialId glass = renderer.CreateMaterial(MaterialDesc.Color(new(0.6f, 0.7f, 0.8f, 0.2f), gloss: 0.2f) with
        {
            SpecularIntensity = 0f
        });
        RenderSnapshot snapshot = Gpu.Snapshot(new(0.3f, 0.6f, 0.8f), sunIntensity: 0.6f);
        snapshot.Instances.Add(Gpu.Instance(plane, ground, new(0, -2, -30), new(40, 1, 40)));
        snapshot.Instances.Add(Gpu.Instance(box, glass, new(-3.5f, 0.5f, -8), new(7, 5, 0.05f),
            flags: RenderFlags.Transparent));
        snapshot.PuffLooks.Add(new(Life: 2f, StartRadius: 0.6f, EndRadius: 0.6f, TemperatureRatio: 3f,
            Shading: PuffShading.Glow(new(0.5f, 0.25f, 0.08f))));
        behind = [new(-2.4f, 1.2f, -12), new(2.4f, 1.2f, -12)];
        inFront = [new(-1f, -0.5f, -5), new(1f, -0.5f, -5)];
        snapshot.Puffs.Add(Puff(behind[0], age: 0f));
        snapshot.Puffs.Add(Puff(behind[1], age: 0f));
        // The same size on the screen as those behind: radius 0.25 at 5 m.
        snapshot.PuffLooks.Add(snapshot.PuffLooks[0] with { StartRadius = 0.25f, EndRadius = 0.25f });
        snapshot.Puffs.Add(Puff(inFront[0], age: 0f, look: 1));
        snapshot.Puffs.Add(Puff(inFront[1], age: 0f, look: 1));
        return snapshot;
    }

    [Fact]
    public void GlassDimsThePuffsBehindItAndLeavesThoseInFrontAsTheyAre()
    {
        VulkanRenderer renderer = Gpu.Headless(Width, Height);
        try
        {
            RenderSnapshot snapshot = GlassScene(renderer, out Vector3[] behind, out Vector3[] inFront);
            (Capture without, Capture with) = RenderBoth(renderer, snapshot, "puffs-glass");

            renderer.LastFrameStats.Puffs.Should().Be(4);
            float behindPane = Added(with, without, Pixel(behind[0])),
                behindOpen = Added(with, without, Pixel(behind[1]));
            float frontPane = Added(with, without, Pixel(inFront[0])),
                frontOpen = Added(with, without, Pixel(inFront[1]));
            behindOpen.Should().BeGreaterThan(0.02f);
            behindPane.Should().BeLessThan(0.9f * behindOpen,
                "the pane of alpha 0.2 lets 80 % of a puff behind it through");
            frontPane.Should().BeApproximately(frontOpen, 0.15f * frontOpen,
                "a puff in front of the pane adds to it in full");
            Verify(with, "puffs-glass");
        }
        finally
        {
            Gpu.DisposeAndAssertClean(renderer);
        }
    }

    private static Vector2 SoftPixel(Vector3 point) => Gpu.Project(point, SoftCamera, Width, Height);

    /// <summary>
    /// The scene of puffs-soft.png, seen by <see cref="SoftCamera"/>: a gray floor 3 m below the eye and a gray wall whose
    /// face is 10 m ahead, under the sun. A glow puff of radius 1.2 m, 0.5 m in front of the wall and 0.6 m above the floor,
    /// whose sphere cuts into both, at the left, and a faint glow puff of radius 1 m whose sphere holds the eye, to the lower
    /// right of the eye.
    /// </summary>
    private static RenderSnapshot SoftScene(VulkanRenderer renderer, out Vector3 cutting)
    {
        MeshId box = renderer.CreateMesh(Primitives.Box(new(0.5f)));
        MeshId plane = renderer.CreateMesh(Primitives.Plane(1f));
        MaterialId gray = renderer.CreateMaterial(MaterialDesc.Color(new(0.35f, 0.35f, 0.35f, 1), gloss: 0.2f) with
        {
            SpecularIntensity = 0f
        });
        RenderSnapshot snapshot = Gpu.Snapshot(new(0.3f, 0.7f, 0.6f), sunIntensity: 1.5f);
        snapshot.Camera.Rotation = SoftCamera;
        snapshot.Camera.PreviousRotation = SoftCamera;
        snapshot.Instances.Add(Gpu.Instance(plane, gray, new(0, -3, -10), new(20, 1, 20)));
        snapshot.Instances.Add(Gpu.Instance(box, gray, new(0, 0, -10.2f), new(20, 6, 0.4f)));
        snapshot.PuffLooks.Add(new(Life: 2f, StartRadius: 1.2f, EndRadius: 1.2f, TemperatureRatio: 3f,
            Shading: PuffShading.Glow(new(2f, 0.9f, 0.25f))));
        snapshot.PuffLooks.Add(new(Life: 2f, StartRadius: 1f, EndRadius: 1f, TemperatureRatio: 3f,
            Shading: PuffShading.Glow(new(0.3f, 0.2f, 0.1f))));
        cutting = new(-2f, -2.4f, -9.5f);
        snapshot.Puffs.Add(Puff(cutting, age: 0f));
        snapshot.Puffs.Add(Puff(new(0.4f, -0.5f, -0.3f), age: 0f, look: 1));
        return snapshot;
    }

    [Fact]
    public void APuffThatCutsIntoAWallAndAFloorAndOneAroundTheEyeShowNoHardLine()
    {
        VulkanRenderer renderer = Gpu.Headless(Width, Height);
        try
        {
            RenderSnapshot snapshot = SoftScene(renderer, out Vector3 cutting);
            (Capture without, Capture with) = RenderBoth(renderer, snapshot, "puffs-soft");

            renderer.LastFrameStats.Puffs.Should().Be(2);
            // Down the column through the cutting puff's center, across the line where the floor meets its sphere, and along
            // the row through its center, the light that the puffs add changes by small steps from pixel to pixel. The floor
            // is seen at 17°, so the fade within one radius of it spans about 0.3 of the radius on the screen: 5 px to 8 px.
            // A hard cut would drop the whole light of the puff in one pixel.
            Vector2 center = SoftPixel(cutting);
            float largestStep = 0f, brightest = 0f;
            for (int y = 1; y < Height; y++)
            {
                float added = Added(with, without, new(center.X, y));
                brightest = MathF.Max(brightest, added);
                largestStep = MathF.Max(largestStep, MathF.Abs(added - Added(with, without, new(center.X, y - 1))));
            }

            for (int x = 1; x < Width; x++)
                largestStep = MathF.Max(largestStep,
                    MathF.Abs(Added(with, without, new(x, center.Y)) - Added(with, without, new(x - 1, center.Y))));
            brightest.Should().BeGreaterThan(0.1f);
            largestStep.Should().BeLessThan(0.25f * brightest,
                "a puff fades within one radius of a surface and of the eye");
            Added(with, without, center).Should()
                .BeGreaterThan(0.02f, "the puff 0.5 m in front of the wall still shows");
            Added(with, without, SoftPixel(new(-2f, -3.4f, -9.5f))).Should().BeLessThan(0.002f,
                "the part of the puff's square below the floor is behind the floor, so it draws nothing");
            Verify(with, "puffs-soft");
        }
        finally
        {
            Gpu.DisposeAndAssertClean(renderer);
        }
    }

    /// <summary>
    /// The scene of puffs-far.png: a trail of twelve glow puffs 2.5 km ahead, in the middle depth range, above a ground
    /// 300 m below the eye that reaches to the horizon. The trail starts at the center of the view, just above the horizon,
    /// runs to the right and curls up and back to the left, and its puffs grow from a radius of 20 m to 64 m and fade with
    /// their age, one second apart. The first puff, of radius 20 m at the center of the view, covers
    /// 2.5 px of the 360 px of the view's height of 60°.
    /// </summary>
    private static RenderSnapshot FarScene(VulkanRenderer renderer, out Vector3 first, out float firstRadius)
    {
        MeshId plane = renderer.CreateMesh(Primitives.Plane(1f));
        MaterialId ground = renderer.CreateMaterial(MaterialDesc.Color(new(0.25f, 0.27f, 0.22f, 1), gloss: 0.1f) with
        {
            SpecularIntensity = 0f
        });
        RenderSnapshot snapshot = Gpu.Snapshot(new(0.3f, 0.5f, 0.8f), sunIntensity: 2f);
        snapshot.Instances.Add(Gpu.Instance(plane, ground, new(0, -300, -20_000), new(30_000, 1, 30_000)));
        snapshot.PuffLooks.Add(new(Life: 12f, StartRadius: 20f, EndRadius: 68f, TemperatureRatio: 3f,
            Shading: PuffShading.Glow(new(4f, 1.8f, 0.5f))));
        first = new(0, 0, -2_500);
        firstRadius = 20f;
        for (int i = 0; i < 12; i++)
        {
            float angle = i * 0.25f;
            var place = new Vector3(700 * MathF.Sin(angle), 250 * (1 - MathF.Cos(angle)), -2_500);
            snapshot.Puffs.Add(Puff(place, age: i));
        }

        return snapshot;
    }

    [Fact]
    public void ATrailTwoAndAHalfKilometersAwayDrawsInTheMiddleRangeAtItsTrueSize()
    {
        VulkanRenderer renderer = Gpu.Headless(Width, Height);
        try
        {
            RenderSnapshot snapshot = FarScene(renderer, out Vector3 first, out float firstRadius);
            (Capture without, Capture with) = RenderBoth(renderer, snapshot, "puffs-far");

            renderer.LastFrameStats.Puffs.Should().Be(12);
            // The first puff projects to a circle of firstRadius / 2,500 / tan(30°) · 180 px = 2.5 px.
            float pixels = firstRadius / 2_500f / MathF.Tan(Gpu.Fov / 2) * (Height / 2f);
            Vector2 center = Pixel(first);
            Added(with, without, center).Should().BeGreaterThan(0.05f, "the center of the far puff glows");
            foreach (Vector2 direction in new[] { Vector2.UnitY, -Vector2.UnitY, -Vector2.UnitX })
                Added(with, without, center + direction * (pixels + 1.5f)).Should().BeLessThan(0.003f,
                    "a puff is drawn at its true size, with no least size on the screen, so nothing shows beyond its radius");
            Verify(with, "puffs-far");
        }
        finally
        {
            Gpu.DisposeAndAssertClean(renderer);
        }
    }
}
