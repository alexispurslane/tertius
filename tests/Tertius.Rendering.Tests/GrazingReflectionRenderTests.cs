using System.Numerics;
using Tertius.Testing;
using static Tertius.Rendering.Tests.ReflectionTesting;

namespace Tertius.Rendering.Tests;

/// <summary>
/// The reflections of smooth surfaces where a ray meets a surface that the eye sees at a grazing angle
/// (docs/plans/screen-space-reflections.md, "The pixel's surface"; ticket ter-eqtb): a mirror ball shows a glowing floor
/// with no bands, its reflections of curved meshes show no dotted lines, and a mirror floor shows the outline of a ball
/// with no streak. The eye stands 0.15 m above each floor, as in the review view pbr-spheres low, and each scene is dark
/// apart from its glowing objects, so a mirror shows only what its rays hit. Each test compares the image with the
/// analytic image of the scene, whose rays meet true spheres and planes.
/// </summary>
[Collection(GpuCollection.Name)]
public sealed class GrazingReflectionRenderTests
{
    /// <summary>The height of the floor's top, in m: 0.15 m below the eye.</summary>
    private const float LowFloor = -0.15f;

    /// <summary>The radiance of the glowing floor of <see cref="MirrorBallOverAGlowingFloor"/>.</summary>
    private const float FloorGlow = 1;

    /// <summary>The radiance of the glowing ball of <see cref="MirrorFloorUnderAGlowingBall"/>.</summary>
    private const float BallGlow = 1;

    /// <summary>The center of the mirror ball of <see cref="MirrorBallOverAGlowingFloor"/>, 1 m in radius, 5 cm above
    /// the floor and 5 m ahead.</summary>
    private static readonly Sphere BallOverFloor = new(new(0, LowFloor + 1.05f, -5), 1);

    /// <summary>The mirror ball of <see cref="MirrorBallOverRedBalls"/>, 0.4 m in radius, 1.4 m above the floor and 8 m
    /// ahead, as gold-0 of pbr-spheres stands.</summary>
    private static readonly Sphere MirrorAboveRedBalls = new(new(-0.5f, LowFloor + 1.4f, -8), 0.4f);

    /// <summary>The three glowing red balls of <see cref="MirrorBallOverRedBalls"/>, 0.4 m in radius, on the floor
    /// below the mirror ball and to each side of it, as the red row of pbr-spheres stands.</summary>
    private static readonly Sphere[] RedBalls =
    [
        new(new(-0.5f, LowFloor + 0.4f, -8), 0.4f),
        new(new(0.5f, LowFloor + 0.4f, -8), 0.4f),
        new(new(-1.5f, LowFloor + 0.4f, -8), 0.4f),
    ];

    /// <summary>The glowing ball of <see cref="MirrorFloorUnderAGlowingBall"/>, 0.5 m in radius, 0.3 m above the floor
    /// and 4 m ahead, against the dark sky.</summary>
    private static readonly Sphere BallOverMirror = new(new(0, LowFloor + 0.8f, -4), 0.5f);

    /// <summary>Both traces.</summary>
    public static TheoryData<TraceResolution> Resolutions => [TraceResolution.Full, TraceResolution.Half];

    [Fact]
    public void AMirrorBallReflectsAGlowingFloorThatTheEyeSeesAtAGrazingAngleWithNoBands()
    {
        // The ball's rays come down onto the floor, whose pixels each span a long stretch of depth. A pixel that held
        // one depth over its whole square met those rays through the back of its slab, with weights that rise and fall
        // in bands from row to row (ter-y8z0's artifact 1). The floor's plane meets them at its front, with weight 1.
        (float[] hdr, CameraView camera) = RenderHdr(Reflections, MirrorBallOverAGlowingFloor);
        var wrong = new List<string>();
        int judged = 0;

        for (int y = 0; y < Height; y++)
        for (int x = 0; x < Width; x++)
        {
            if (FloorSeenInTheBall(camera, (x, y)) is not { } weight)
                continue;
            judged++;
            float shown = At(hdr, (x, y)).Y;
            if (MathF.Abs(shown - weight * FloorGlow) > 0.1f * weight * FloorGlow)
                wrong.Add($"({x}, {y}): {shown}, not {weight * FloorGlow}");
        }

        judged.Should().BeGreaterThan(3_000, "the lower half of the ball reflects the floor");
        wrong
            .Should()
            .BeEmpty("the ball shows W · L of the floor wherever its ray meets the floor; {0} of {1} differ", wrong.Count, judged);
    }

    [Fact]
    public void AMirrorBallReflectsBallsOnAFloorThatTheEyeSeesAtAGrazingAngleWithNoDottedLines()
    {
        // The red balls' pixels near their outlines see the balls at a grazing angle, so each pixel's plane stands off
        // its neighbors' surfaces. A plane that continued to a neighbor 10 % off it (κ) left steps between the two
        // surfaces, and the mirror's rays slipped through them: dark dots in lines on the reflected balls.
        const int width = 1280,
            height = 720;
        (float[] hdr, CameraView camera) = RenderHdr(
            reflections: Reflections,
            scene: MirrorBallOverRedBalls,
            view: new TestView(Width: width, Height: height)
        );
        var dots = new List<string>();

        for (int y = 1; y < height - 1; y++)
        for (int x = 1; x < width - 1; x++)
        {
            Vector3 viewRay = camera.ViewRayThrough(new(x + 0.5f, y + 0.5f));
            if (InnerMirrorPoint(MirrorAboveRedBalls, viewRay) is null)
                continue;
            if (IsDarkDot(hdr, width, (x, y)))
                dots.Add($"({x}, {y})");
        }

        dots.Should().HaveCountLessThanOrEqualTo(5, "the mirror's reflections show no dotted lines: {0}", string.Join(", ", dots));
    }

    [Theory]
    [MemberData(nameof(Resolutions))]
    public void AMirrorFloorThatTheEyeSeesAtAGrazingAngleReflectsTheOutlineOfABallAgainstTheSkyWithNoStreak(
        TraceResolution resolution
    )
    {
        // The pixels at the ball's outline see it at a grazing angle, so their planes run steeply in depth. A plane that
        // reached the pixel's edge toward the sky stood far behind the ball, and the floor's rays that pass beside the
        // ball hit it: streaks of the ball's glow above its reflection (ter-y8z0's streaks of the unbounded planes).
        (float[] hdr, CameraView camera) = RenderHdr(ReflectionsAt(resolution), MirrorFloorUnderAGlowingBall);
        bool?[] meetsTheBall = new bool?[Width * Height];
        for (int y = 0; y < Height; y++)
        for (int x = 0; x < Width; x++)
            meetsTheBall[y * Width + x] = FloorRayMeetsTheBall(camera, (x, y));
        var streaks = new List<string>();
        int judged = 0;

        for (int y = 2; y < Height - 2; y++)
        for (int x = 2; x < Width - 2; x++)
        {
            // Two pixels away from the outline of the reflection, where its edge blurs over a pixel or two.
            bool besideTheBall = Enumerable
                .Range(-2, 5)
                .All(dy => Enumerable.Range(-2, 5).All(dx => meetsTheBall[(y + dy) * Width + x + dx] == false));
            if (!besideTheBall)
                continue;
            judged++;
            float weight = SpecularWeight(Mirror(), camera, (x, y)).Y;
            float shown = At(hdr, (x, y)).Y;
            if (shown > 0.25f * weight * BallGlow)
                streaks.Add($"({x}, {y}): {shown}");
        }

        judged.Should().BeGreaterThan(50_000, "most of the floor's rays pass the ball into the dark sky");
        streaks.Should().BeEmpty("a floor ray that passes beside the ball shows the dark sky");
    }

    /// <summary>A true sphere: its center, camera-relative, and its radius, in m.</summary>
    private readonly record struct Sphere(Vector3 Center, float Radius)
    {
        /// <summary>The distance along a unit direction from an origin outside the sphere to its near side, or null when
        /// the line misses it or meets it behind the origin.</summary>
        public float? DistanceAlong(Vector3 origin, Vector3 direction)
        {
            Vector3 offset = origin - Center;
            float half = Vector3.Dot(offset, direction);
            float discriminant = half * half - offset.LengthSquared() + Radius * Radius;
            if (discriminant < 0)
                return null;
            float distance = -half - MathF.Sqrt(discriminant);
            return distance > 1e-4f ? distance : null;
        }
    }

    /// <summary>
    /// A white mirror ball 1 m in radius at <see cref="BallOverFloor"/> above a floor that glows <see cref="FloorGlow"/>
    /// and reflects nothing.
    /// </summary>
    private static RenderSnapshot MirrorBallOverAGlowingFloor(VulkanRenderer renderer)
    {
        RenderSnapshot snapshot = Dark();
        snapshot.Instances.Add(LowFloorOf(renderer, Glow(renderer, new(FloorGlow))));
        snapshot.Instances.Add(Ball(renderer, BallOverFloor, renderer.CreateMaterial(Mirror())));
        return snapshot;
    }

    /// <summary>
    /// A white mirror ball at <see cref="MirrorAboveRedBalls"/> above three balls that glow red at
    /// <see cref="RedBalls"/>, on a floor that glows green, under a vertical field of view of 35°, as pbr-spheres' low
    /// view looks at its gold and red rows.
    /// </summary>
    private static RenderSnapshot MirrorBallOverRedBalls(VulkanRenderer renderer)
    {
        RenderSnapshot snapshot = Dark();
        snapshot.Camera.VerticalFov = 35 * MathF.PI / 180;
        snapshot.Instances.Add(LowFloorOf(renderer, Glow(renderer, new(0.1f, 0.5f, 0.1f))));
        snapshot.Instances.Add(Ball(renderer, MirrorAboveRedBalls, renderer.CreateMaterial(Mirror())));
        MaterialId red = Glow(renderer, new(1, 0.1f, 0.1f));
        foreach (Sphere ball in RedBalls)
            snapshot.Instances.Add(Ball(renderer, ball, red));
        return snapshot;
    }

    /// <summary>
    /// A white mirror floor, with <see cref="FlatNormalMap"/>, below a ball that glows <see cref="BallGlow"/> at
    /// <see cref="BallOverMirror"/>, against the dark sky.
    /// </summary>
    private static RenderSnapshot MirrorFloorUnderAGlowingBall(VulkanRenderer renderer)
    {
        RenderSnapshot snapshot = Dark();
        snapshot.Instances.Add(
            LowFloorOf(renderer, renderer.CreateMaterial(Mirror() with { NormalMap = FlatNormalMap(renderer) }))
        );
        snapshot.Instances.Add(Ball(renderer, BallOverMirror, Glow(renderer, new(BallGlow))));
        return snapshot;
    }

    /// <summary>A floor of 60 m by 60 m whose top lies at <see cref="LowFloor"/>, from 10 m behind the eye to 50 m ahead.
    /// </summary>
    private static RenderInstance LowFloorOf(VulkanRenderer renderer, MaterialId material) =>
        Gpu.Instance(
            mesh: renderer.CreateMesh(Primitives.Plane(1)),
            material: material,
            position: new(0, LowFloor, -20),
            scale: new(30, 1, 30),
            radius: 43,
            chainDepth: TestChains.CamerasFrame
        );

    /// <summary>A ball mesh of 96 segments and 48 rings, as pbr-spheres' spheres have.</summary>
    private static RenderInstance Ball(VulkanRenderer renderer, Sphere ball, MaterialId material) =>
        Gpu.Instance(
            mesh: renderer.CreateMesh(Primitives.Sphere(ball.Radius, segments: 96, rings: 48)),
            material: material,
            position: ball.Center,
            scale: Vector3.One,
            radius: ball.Radius,
            chainDepth: TestChains.CamerasFrame
        );

    /// <summary>
    /// The point of a mirror ball that a view ray sees, with its unit normal, where the ball faces the eye by a cosine of
    /// 0.2 or more, or null elsewhere: the inner part of the ball, away from its rim, where its mesh and the true sphere
    /// agree to a fraction of a pixel.
    /// </summary>
    private static (Vector3 Point, Vector3 Normal)? InnerMirrorPoint(Sphere ball, Vector3 viewRay)
    {
        var toPoint = Vector3.Normalize(viewRay);
        if (ball.DistanceAlong(Vector3.Zero, toPoint) is not { } distance)
            return null;
        Vector3 point = toPoint * distance;
        var normal = Vector3.Normalize(point - ball.Center);
        return Vector3.Dot(normal, -toPoint) >= 0.2f ? (point, normal) : null;
    }

    /// <summary>
    /// W at a pixel of the mirror ball of <see cref="MirrorBallOverAGlowingFloor"/> whose mirrored ray meets the floor
    /// within 5 m of the eye, at a point that the eye sees a tenth of the screen's height or more inside its edges, past
    /// the edge fade; or null for any other pixel. Within 5 m, the cone at the hit covers floor only.
    /// </summary>
    private static float? FloorSeenInTheBall(in CameraView camera, (int X, int Y) pixel)
    {
        Vector3 viewRay = camera.ViewRayThrough(new(pixel.X + 0.5f, pixel.Y + 0.5f));
        if (InnerMirrorPoint(BallOverFloor, viewRay) is not { } mirror)
            return null;
        var toPoint = Vector3.Normalize(viewRay);
        Vector3 reflected = Vector3.Reflect(toPoint, mirror.Normal);
        if (reflected.Y >= 0)
            return null;
        Vector3 hit = mirror.Point + reflected * ((LowFloor - mirror.Point.Y) / reflected.Y);
        // In front of the eye, and not hidden from it behind the ball.
        if (hit.Z > -0.5f || hit.Length() > 5 || BallOverFloor.DistanceAlong(Vector3.Zero, Vector3.Normalize(hit)) is not null)
            return null;
        if (!InsideTheEdgeFade(camera, camera.PixelOf(hit)))
            return null;
        return TestMaterials
            .SampleOf(Mirror())
            .CompensatedSpecularAlbedo(normal: mirror.Normal, toView: -toPoint, tables: BrdfTables.Shared)
            .ToVector3()
            .Y;
    }

    /// <summary>
    /// Whether the mirrored ray of a floor pixel of <see cref="MirrorFloorUnderAGlowingBall"/> meets the ball at a point
    /// that the eye sees, inside the edge fade; null for a pixel that is not floor, or whose ray meets the ball where
    /// the eye does not see it, or near the screen's edges.
    /// </summary>
    private static bool? FloorRayMeetsTheBall(in CameraView camera, (int X, int Y) pixel)
    {
        var toPoint = Vector3.Normalize(camera.ViewRayThrough(new(pixel.X + 0.5f, pixel.Y + 0.5f)));
        if (toPoint.Y >= 0)
            return null;
        float distance = LowFloor / toPoint.Y;
        if (BallOverMirror.DistanceAlong(Vector3.Zero, toPoint) < distance || distance > 40)
            return null;
        Vector3 point = toPoint * distance;
        Vector3 reflected = Vector3.Reflect(toPoint, FlatMapNormal);
        if (BallOverMirror.DistanceAlong(point, reflected) is not { } along)
            return false;
        Vector3 hit = point + reflected * along;
        bool seen = Vector3.Dot(Vector3.Normalize(hit - BallOverMirror.Center), -Vector3.Normalize(hit)) > 0;
        return seen && InsideTheEdgeFade(camera, camera.PixelOf(hit)) ? true : null;
    }

    /// <summary>Whether a screen position lies a tenth of the screen's shorter side inside its edges, plus 4 pixels,
    /// where the edge fade is 1.</summary>
    private static bool InsideTheEdgeFade(in CameraView camera, Vector2 position)
    {
        float margin = CameraView.EdgeFadeShare * Math.Min(camera.Width, camera.Height) + 4;
        return position.X >= margin
            && position.Y >= margin
            && position.X <= camera.Width - margin
            && position.Y <= camera.Height - margin;
    }

    /// <summary>
    /// Whether a pixel is a dark dot: on some line through it, across, down or along a diagonal, both neighbors show
    /// about the same light (their sums of RGB within a quarter of the larger, which is at least 0.25), and the pixel
    /// shows less than half of either.
    /// </summary>
    private static bool IsDarkDot(float[] hdr, int width, (int X, int Y) pixel)
    {
        float Sum((int X, int Y) at) => Vector3.Dot(At(hdr, at, width), Vector3.One);
        float center = Sum(pixel);
        foreach ((int dx, int dy) in ((int Dx, int Dy)[])[(1, 0), (0, 1), (1, 1), (1, -1)])
        {
            float a = Sum((pixel.X + dx, pixel.Y + dy));
            float b = Sum((pixel.X - dx, pixel.Y - dy));
            bool alike = MathF.Abs(a - b) < 0.25f * MathF.Max(a, b);
            if (alike && MathF.Min(a, b) > 0.25f && center < 0.5f * MathF.Min(a, b))
                return true;
        }
        return false;
    }
}
