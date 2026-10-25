using System.Numerics;

namespace Tertius.Rendering.Tests;

/// <summary>
/// The overlay pass on the GPU: the rings of frame outlines in real headless captures. The camera is at the render origin
/// with identity rotation, so it looks down −Z with +Y up. Each test renders its scene twice, without and with its
/// outlines, so that a pixel that a ring covers can be told from the scene behind it exactly.
/// </summary>
[Collection(GpuCollection.Name)]
public sealed class OutlineRenderTests
{
    private const int Width = 640,
        Height = 360;

    /// <summary>The display color of a packed outline color, in [0, 1].</summary>
    private static Vector3 Display(uint packed) =>
        new Vector3(packed & 0xFF, (packed >> 8) & 0xFF, (packed >> 16) & 0xFF) / 255f;

    private static Vector2 Pixel(Vector3 point) => Gpu.Project(point: point, width: Width, height: Height);

    /// <summary>
    /// The pixels within <paramref name="radius"/> of <paramref name="around"/> that a ring covers: those whose color differs
    /// from the render without outlines. Each gives its color with the outlines and without.
    /// </summary>
    private static List<(Vector3 With, Vector3 Without)> Covered(
        Capture with,
        Capture without,
        Vector2 around,
        int radius = 2
    )
    {
        var covered = new List<(Vector3 With, Vector3 Without)>();
        for (int y = (int)around.Y - radius; y <= (int)around.Y + radius; y++)
        for (int x = (int)around.X - radius; x <= (int)around.X + radius; x++)
        {
            Vector3 a = with.Color(x, y),
                b = without.Color(x, y);
            if (Vector3.Distance(a, b) > 0.02f)
                covered.Add((a, b));
        }

        return covered;
    }

    /// <summary>Renders <paramref name="snapshot"/> once without its outlines and once with them.</summary>
    private static (Capture Without, Capture With) RenderBoth(
        VulkanRenderer renderer,
        RenderSnapshot snapshot,
        string name
    )
    {
        List<FrameOutline> outlines = [.. snapshot.Outlines];
        snapshot.Outlines.Clear();
        Capture without = Gpu.RenderAndCapture(renderer: renderer, snapshot: snapshot, name: name + "-without");
        snapshot.Outlines.AddRange(outlines);
        Capture with = Gpu.RenderAndCapture(renderer: renderer, snapshot: snapshot, name: name);
        return (without, with);
    }

    [Fact]
    public void ASphereOutlineInFrontOfTheCameraPutsItsColorAtThePixelsOfItsProjectedRim()
    {
        // A resting frame's contents sphere 6 m ahead, radius 2.4 m, against the sky. Seen from the eye, its rim is the
        // circle of tangent points: 6 · (1 − 2.4² / 6²) = 5.04 m ahead, of radius 2.4 · √(1 − 2.4² / 6²) = 2.1996 m. It
        // projects 8 px outside the ring in the sphere's XY plane, which is 6 m ahead with radius 2.4 m. The test reads a
        // capture, so it also shows that a capture includes the outlines.
        VulkanRenderer renderer = Gpu.Headless(width: Width, height: Height);
        try
        {
            RenderSnapshot snapshot = Gpu.Snapshot(new(0.4f, 0.8f, 0.5f));
            snapshot.Outlines.Add(new RestingOutline(new(0, 0, -6), 2.4f));

            (Capture without, Capture with) = RenderBoth(renderer, snapshot, "outline-sphere-rim");

            float rim = 2.4f * MathF.Sqrt(1 - 2.4f * 2.4f / 36f) / MathF.Sqrt(2);
            Vector2 upperRight = Pixel(new(rim, rim, -5.04f));
            Vector2 lowerLeft = Pixel(new(-rim, -rim, -5.04f));
            foreach (Vector2 pixel in new[] { upperRight, lowerLeft })
            {
                List<(Vector3 With, Vector3 Without)> covered = Covered(
                    with: with,
                    without: without,
                    around: pixel,
                    radius: 1
                );
                covered.Should().NotBeEmpty("the rim as seen from the eye passes through pixel {0}", pixel);
                covered
                    .Should()
                    .AllSatisfy(p =>
                        Vector3
                            .Distance(p.With, Display(OutlineColors.Resting))
                            .Should()
                            .BeLessThan(0.02f, "a ring against the sky draws its color at full alpha: {0}", p.With)
                    );
            }

            // Halfway between the rim and the ring of the XY plane, 4 px inside the rim, is sky.
            float between = (rim + 2.4f / 6f * 5.04f / MathF.Sqrt(2)) / 2;
            Covered(with: with, without: without, around: Pixel(new(between, between, -5.04f)), radius: 0)
                .Should()
                .BeEmpty();
        }
        finally
        {
            Gpu.DisposeAndAssertClean(renderer);
        }
    }

    /// <summary>
    /// A semantic bubble of radius 3 m, 10 m ahead, whose ring in the XY plane passes behind a white box 6 m ahead at the
    /// upper right, in front of a white wall 20 m ahead at the lower left, and against the sky at the upper left.
    /// </summary>
    private static RenderSnapshot RingAmongGeometry(VulkanRenderer renderer)
    {
        MeshId box = renderer.CreateMesh(Primitives.Box(new(0.5f)));
        MaterialId white = renderer.CreateMaterial(
            MaterialDesc.Color(new(x: 0.8f, y: 0.8f, z: 0.8f, w: 1), roughness: 0.75f) with
            {
                Reflectance = 0f,
            }
        );
        RenderSnapshot snapshot = Gpu.Snapshot(new(0.3f, 0.5f, 0.8f));
        snapshot.Instances.Add(
            Gpu.Instance(mesh: box, material: white, position: new(2, 2, -6), scale: new(2.5f, 2.5f, 0.5f))
        );
        snapshot.Instances.Add(
            Gpu.Instance(mesh: box, material: white, position: new(-4, -4, -20), scale: new(6, 6, 0.5f))
        );
        snapshot.Outlines.Add(new SemanticOutline(new(0, 0, -10), Quaternion.Identity, new(3)));
        return snapshot;
    }

    /// <summary>The point of the bubble's XY ring at 45° from +X toward the quadrant (sx, sy).</summary>
    private static Vector2 RingPixel(float sx, float sy) =>
        Pixel(new(sx * 3 / MathF.Sqrt(2), sy * 3 / MathF.Sqrt(2), -10));

    [Fact]
    public void ARingBehindAnOpaqueBoxDrawsAtThirtyPercentAlphaThere()
    {
        VulkanRenderer renderer = Gpu.Headless(width: Width, height: Height);
        try
        {
            (Capture without, Capture with) = RenderBoth(renderer, RingAmongGeometry(renderer), "outline-behind-box");

            // The arc from 30° to 60° passes behind the box. It holds the corners of segments 6 to 10, where two quads meet,
            // and each pixel must blend once.
            Vector3 ring = Measure.ToLinear(Display(OutlineColors.Semantic));
            for (int degrees = 30; degrees <= 60; degrees++)
            {
                float angle = degrees * MathF.PI / 180;
                Vector2 pixel = Pixel(new(3 * MathF.Cos(angle), 3 * MathF.Sin(angle), -10));
                List<(Vector3 With, Vector3 Without)> covered = Covered(
                    with: with,
                    without: without,
                    around: pixel,
                    radius: 1
                );
                covered.Should().NotBeEmpty("the ring passes behind the box at {0}°", degrees);
                foreach ((Vector3 shown, Vector3 box) in covered)
                {
                    // The output blends in linear light: 30 % of the ring over 70 % of the box.
                    Vector3 expected = 0.3f * ring + 0.7f * Measure.ToLinear(box);
                    Vector3
                        .Distance(Measure.ToLinear(shown), expected)
                        .Should()
                        .BeLessThan(0.02f, "the box {0} shows through the ring at {1}°: {2}", box, degrees, shown);
                }
            }
        }
        finally
        {
            Gpu.DisposeAndAssertClean(renderer);
        }
    }

    [Fact]
    public void ARingInFrontOfGeometryOrAgainstTheSkyDrawsAtFullAlpha()
    {
        VulkanRenderer renderer = Gpu.Headless(width: Width, height: Height);
        try
        {
            (Capture without, Capture with) = RenderBoth(renderer, RingAmongGeometry(renderer), "outline-in-front");

            without
                .Luma(RingPixel(-1, -1))
                .Should()
                .BeGreaterThan(0.3f, "the wall is behind the ring at the lower left");
            foreach (Vector2 pixel in new[] { RingPixel(-1, -1), RingPixel(-1, 1) })
            {
                List<(Vector3 With, Vector3 Without)> covered = Covered(
                    with: with,
                    without: without,
                    around: pixel,
                    radius: 1
                );
                covered.Should().NotBeEmpty("the ring passes through pixel {0}", pixel);
                covered
                    .Should()
                    .AllSatisfy(p =>
                        Vector3
                            .Distance(p.With, Display(OutlineColors.Semantic))
                            .Should()
                            .BeLessThan(0.02f, "nothing is in front of the ring at {0}: {1}", pixel, p.With)
                    );
            }
        }
        finally
        {
            Gpu.DisposeAndAssertClean(renderer);
        }
    }

    [Fact]
    public void ACameraInsideABubbleSeesItsRings()
    {
        // The bubble's center is 5 m behind the eye, with radii 9, 7 and 8 m, so the eye is inside it. Its rings in the YZ
        // and ZX planes pass in front of the eye, about 3 m ahead. The two points are corners of ring segments: on the YZ
        // ring (0, 7 cos a, 8 sin a) at a = 47 / 64 of a turn, and on the ZX ring (9 sin a, 0, 8 cos a) at a = 34 / 64.
        VulkanRenderer renderer = Gpu.Headless(width: Width, height: Height);
        try
        {
            RenderSnapshot snapshot = Gpu.Snapshot(new(0.4f, 0.8f, 0.5f));
            Vector3 center = new(1, 0.5f, 5);
            snapshot.Outlines.Add(new SemanticOutline(center, Quaternion.Identity, new(9, 7, 8)));

            (Capture without, Capture with) = RenderBoth(renderer, snapshot, "outline-camera-inside");

            float yz = 2 * MathF.PI * 47 / 64,
                zx = 2 * MathF.PI * 34 / 64;
            Vector3 onYz = center + new Vector3(0, 7 * MathF.Cos(yz), 8 * MathF.Sin(yz));
            Vector3 onZx = center + new Vector3(9 * MathF.Sin(zx), 0, 8 * MathF.Cos(zx));
            foreach (Vector3 point in new[] { onYz, onZx })
            {
                point.Z.Should().BeLessThan(-2, "the point is ahead of the eye");
                List<(Vector3 With, Vector3 Without)> covered = Covered(
                    with: with,
                    without: without,
                    around: Pixel(point),
                    radius: 1
                );
                covered.Should().NotBeEmpty("the ring passes through {0}", point);
                covered
                    .Should()
                    .AllSatisfy(p =>
                        Vector3
                            .Distance(p.With, Display(OutlineColors.Semantic))
                            .Should()
                            .BeLessThan(0.02f, "the ring at {0} draws its color: {1}", point, p.With)
                    );
            }
        }
        finally
        {
            Gpu.DisposeAndAssertClean(renderer);
        }
    }

    [Fact]
    public void ARingIsCutAtTheNearPlaneAsGeometryIs()
    {
        // A bubble of radius 3 m centered 3 m to the right of the eye and 1 cm below it. Its ring in the ZX plane passes
        // 1 cm under the eye: point 48 of the ring, (3 sin a, 0, 3 cos a) at a = 48 / 64 of a turn, is at (0, -0.01, 0).
        // Segment 47 runs from 29 cm ahead to there, so it crosses the near plane, 5 cm ahead, low in the middle of the view.
        VulkanRenderer renderer = Gpu.Headless(width: Width, height: Height);
        try
        {
            RenderSnapshot snapshot = Gpu.Snapshot(new(0.4f, 0.8f, 0.5f));
            Vector3 center = new(3, -0.01f, 0);
            snapshot.Outlines.Add(new SemanticOutline(center, Quaternion.Identity, new(3)));

            (Capture without, Capture with) = RenderBoth(renderer, snapshot, "outline-near-plane");

            float a = 2 * MathF.PI * 47 / 64;
            Vector3 start = center + new Vector3(3 * MathF.Sin(a), 0, 3 * MathF.Cos(a));
            Vector3 end = new(0, -0.01f, 0);
            Vector3 AtDepth(float depth) => Vector3.Lerp(start, end, (-start.Z - depth) / -start.Z);
            List<(Vector3 With, Vector3 Without)> ahead = Covered(
                with: with,
                without: without,
                around: Pixel(AtDepth(0.1f)),
                radius: 1
            );
            ahead.Should().NotBeEmpty("the segment draws 10 cm ahead, in front of the near plane");
            ahead
                .Should()
                .AllSatisfy(p => Vector3.Distance(p.With, Display(OutlineColors.Semantic)).Should().BeLessThan(0.02f));
            Covered(with: with, without: without, around: Pixel(AtDepth(0.025f)), radius: 1)
                .Should()
                .BeEmpty("2.5 cm ahead is nearer than the near plane, where geometry would be cut off too");
        }
        finally
        {
            Gpu.DisposeAndAssertClean(renderer);
        }
    }

    [Fact]
    public void ARingSeenAlmostEdgeOnEndsAtItsTipWithoutASpike()
    {
        // A bubble of radius 2 m, 5 m ahead and 1 cm to the right, so the plane of its YZ ring passes 1 cm beside the eye.
        // That ring projects to a loop less than a pixel wide, and it turns back at the top, 2 / √(5² − 2²) = 0.436 of the
        // half-height of the view up. A corner that bends by almost 180° must not reach out far past the tip.
        VulkanRenderer renderer = Gpu.Headless(width: Width, height: Height);
        try
        {
            RenderSnapshot snapshot = Gpu.Snapshot(new(0.4f, 0.8f, 0.5f));
            snapshot.Outlines.Add(new SemanticOutline(new(0.01f, 0, -5), Quaternion.Identity, new(2)));

            (Capture without, Capture with) = RenderBoth(renderer, snapshot, "outline-edge-on");

            float tip = 2 / MathF.Sqrt(21);
            Covered(with: with, without: without, around: Pixel(new(0, tip - 0.02f, -1)), radius: 1)
                .Should()
                .NotBeEmpty("the ring reaches up to its tip");
            for (float above = 4; above <= 40; above += 4)
            {
                Vector2 pixel = Pixel(new(0, tip, -1)) - new Vector2(0, above);
                Covered(with: with, without: without, around: pixel, radius: 1)
                    .Should()
                    .BeEmpty("nothing is drawn {0} px above the tip", above);
            }
        }
        finally
        {
            Gpu.DisposeAndAssertClean(renderer);
        }
    }

    [Fact]
    public void RenderStatsCountTheOutlinesThatAFrameDrew()
    {
        VulkanRenderer renderer = Gpu.Headless(width: Width, height: Height);
        try
        {
            RenderSnapshot snapshot = Gpu.Snapshot(new(0.4f, 0.8f, 0.5f));
            snapshot.Outlines.Add(new FallingOutline(new(0, 0, -10), 2));
            snapshot.Outlines.Add(new FollowingOutline(new(0, 0, -10), 3));
            snapshot.Outlines.Add(new FallingOutline(new(0, 0, 30), 2)); // behind the eye
            snapshot.Outlines.Add(new RestingOutline(new(0, 0, -10), 0)); // holds nothing

            renderer.Render(snapshot, 1f);
            renderer.LastFrameStats.Outlines.Should().Be(2);

            snapshot.Outlines.Clear();
            renderer.Render(snapshot, 1f);
            renderer.LastFrameStats.Outlines.Should().Be(0);
        }
        finally
        {
            Gpu.DisposeAndAssertClean(renderer);
        }
    }

    /// <summary>
    /// The scene of the reference image frame-outlines.png, 640 × 360, at alpha 1, on gray ground 2 m below the eye, lit by
    /// the sun from the upper right behind the camera:
    /// <list type="bullet">
    /// <item>Left of center, 11 m ahead: a gray box, turned, inside a blue semantic bubble that has the box's axes. The
    /// bubble's rings dim where they pass behind the box.</item>
    /// <item>Right of center, 14 m ahead: a pink falling frame's sphere, partly behind a gray wall 9 m ahead that hides its
    /// lower right quarter, where its rings dim. Its lowest point dips just below the ground.</item>
    /// <item>Above the center, 14 m ahead: a small teal resting frame's sphere, against the sky.</item>
    /// <item>Below the center, 16 m ahead: a purple nesting frame's sphere, half under the ground, so its lower half is
    /// dimmed.</item>
    /// <item>At the upper right, 18 m ahead: a gold following frame's sphere, against the sky.</item>
    /// </list>
    /// Each sphere shows three rings in the axes of the camera's frame, and its rim as seen from the eye.
    /// </summary>
    private static RenderSnapshot OutlineScene(VulkanRenderer renderer)
    {
        MeshId box = renderer.CreateMesh(Primitives.Box(new(0.5f)));
        MeshId plane = renderer.CreateMesh(Primitives.Plane(1f));
        MaterialId gray = renderer.CreateMaterial(
            MaterialDesc.Color(new(x: 0.55f, y: 0.55f, z: 0.55f, w: 1), roughness: 0.55f)
        );
        MaterialId ground = renderer.CreateMaterial(
            MaterialDesc.Color(new(x: 0.35f, y: 0.35f, z: 0.35f, w: 1), roughness: 0.65f)
        );
        RenderSnapshot snapshot = Gpu.Snapshot(new(0.4f, 0.8f, 0.5f));
        snapshot.Instances.Add(
            Gpu.Instance(mesh: plane, material: ground, position: new(0, -2, -30), scale: new(30, 1, 30))
        );

        var turned = Quaternion.CreateFromYawPitchRoll(0.5f, 0, 0);
        Vector3 boxCenter = new(-3, -0.5f, -11);
        snapshot.Instances.Add(
            Gpu.Instance(mesh: box, material: gray, position: boxCenter, scale: new(1.2f), rotation: turned)
        );
        snapshot.Outlines.Add(new SemanticOutline(boxCenter, turned, new(1.8f, 1.3f, 1.6f)));

        snapshot.Instances.Add(
            Gpu.Instance(mesh: box, material: gray, position: new(3, -0.8f, -9), scale: new(2.8f, 2.5f, 0.4f))
        );
        snapshot.Outlines.Add(new FallingOutline(new(2.5f, 0, -14), 2.2f));
        snapshot.Outlines.Add(new RestingOutline(new(-0.5f, 2.6f, -14), 1f));
        snapshot.Outlines.Add(new NestingOutline(new(-1, -2.6f, -16), 1.4f));
        snapshot.Outlines.Add(new FollowingOutline(new(7, 3.2f, -18), 1.6f));
        return snapshot;
    }

    [Fact]
    public void TheOverlayDrawsTheOutlineOfEachKindOfFrameOverTheScene()
    {
        VulkanRenderer renderer = Gpu.Headless(width: Width, height: Height);
        try
        {
            ReferenceImages.Verify(renderer: renderer, snapshot: OutlineScene(renderer), name: "frame-outlines");
            renderer.LastFrameStats.Outlines.Should().Be(5);
        }
        finally
        {
            Gpu.DisposeAndAssertClean(renderer);
        }
    }
}
