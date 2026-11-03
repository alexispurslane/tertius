using System.Numerics;
using Tertius.Testing;

namespace Tertius.Rendering.Tests;

/// <summary>
/// The G-buffer's motion target (docs/plans/temporal-renderer.md, section 3): at each pixel, the last frame's place of the
/// surface point that the pixel shows minus this frame's place, in image widths and heights with no jitter, and the ratio
/// of the point's view depth in the last frame to its view depth now. Each test draws two frames and compares the second
/// frame's motion with the place that the last frame's camera gives the point's last position.
/// </summary>
[Collection(GpuCollection.Name)]
public sealed class MotionVectorRenderTests
{
    private const int Width = 320,
        Height = 180;

    /// <summary>The tolerance of a motion, in image widths: a tenth of a pixel, above the half float's rounding.</summary>
    private const float MotionTolerance = 0.1f / Width;

    private static RenderSnapshot WithBox(MeshId box, MaterialId white, Vector3 position, Quaternion? rotation = null)
    {
        RenderSnapshot snapshot = Gpu.Snapshot(new(0.3f, 1f, 0.4f));
        snapshot.Instances.Add(
            Gpu.Instance(
                mesh: box,
                material: white,
                position: position,
                scale: Vector3.One,
                rotation: rotation,
                chainDepth: TestChains.CamerasFrame,
                entity: 5
            )
        );
        return snapshot;
    }

    /// <summary>The camera-relative point that pixel (x, y) shows, for a camera that looks down −Z: on its ray, at its distance.</summary>
    private static Vector3 PointAt(float[] distance, int x, int y)
    {
        float tanY = MathF.Tan(Gpu.Fov / 2);
        float tanX = tanY * Width / Height;
        var ray = new Vector3(((x + 0.5f) / Width * 2 - 1) * tanX, (1 - (y + 0.5f) / Height * 2) * tanY, -1);
        return Vector3.Normalize(ray) * distance[y * Width + x];
    }

    /// <summary>The motion that pixel (x, y) must hold when the point that it shows was at <paramref name="previous"/>.</summary>
    private static Vector2 ExpectedMotion(Vector3 previous, int x, int y)
    {
        Vector2 last = Gpu.Project(point: previous, width: Width, height: Height);
        return new((last.X - (x + 0.5f)) / Width, (last.Y - (y + 0.5f)) / Height);
    }

    private static Vector3 MotionAt(float[] motion, int x, int y)
    {
        int i = 4 * (y * Width + x);
        return new(motion[i], motion[i + 1], motion[i + 2]);
    }

    /// <summary>
    /// Checks the motion of every pixel in the 9 × 9 block around the pixel of <paramref name="center"/> against
    /// <paramref name="previousOf"/>, which gives the last position of the point that a pixel shows now.
    /// </summary>
    private static void ShouldMoveAsAnalytic(
        VulkanRenderer renderer,
        Vector3 center,
        Func<Vector3, Vector3> previousOf,
        string what
    )
    {
        float[] distance = renderer.ReadDistance();
        float[] motion = renderer.ReadMotion();
        Vector2 pixel = Gpu.Project(point: center, width: Width, height: Height);
        for (int dy = -4; dy <= 4; dy++)
        for (int dx = -4; dx <= 4; dx++)
        {
            int x = (int)pixel.X + dx,
                y = (int)pixel.Y + dy;
            Vector3 point = PointAt(distance, x, y);
            Vector3 previous = previousOf(point);
            Vector2 expected = ExpectedMotion(previous, x, y);
            Vector3 actual = MotionAt(motion, x, y);
            actual.X.Should().BeApproximately(expected.X, MotionTolerance, "{0} at ({1}, {2})", what, x, y);
            actual.Y.Should().BeApproximately(expected.Y, MotionTolerance, "{0} at ({1}, {2})", what, x, y);
            actual.Z.Should().BeApproximately(-previous.Z / -point.Z, 2e-3f, "the depth ratio of {0}", what);
        }
    }

    [Fact]
    public void ABoxThatMovesByAKnownStepGivesTheAnalyticMotion()
    {
        VulkanRenderer renderer = Gpu.Headless(width: Width, height: Height);
        try
        {
            MeshId box = renderer.CreateMesh(Primitives.Box(new(0.5f)));
            MaterialId white = renderer.CreateMaterial(MaterialDesc.Color(Vector4.One, roughness: 0.5f));
            var step = new Vector3(0.3f, 0.1f, 0.8f);
            renderer.Render(WithBox(box: box, white: white, position: new(0, 0, -6)), 1f);

            renderer.Render(WithBox(box: box, white: white, position: new Vector3(0, 0, -6) + step), 1f);

            ShouldMoveAsAnalytic(
                renderer: renderer,
                center: new Vector3(0, 0, -5.5f) + step,
                previousOf: point => point - step,
                what: "a box that moved"
            );
        }
        finally
        {
            Gpu.DisposeAndAssertClean(renderer);
        }
    }

    [Fact]
    public void AStillBoxSeenFromACameraThatStepsGivesTheMotionOfTheEyesStep()
    {
        VulkanRenderer renderer = Gpu.Headless(width: Width, height: Height);
        try
        {
            MeshId box = renderer.CreateMesh(Primitives.Box(new(0.5f)));
            MaterialId white = renderer.CreateMaterial(MaterialDesc.Color(Vector4.One, roughness: 0.5f));
            renderer.Render(WithBox(box: box, white: white, position: new(0, 0, -6)), 1f);
            // The eye rose by 0.4 m and moved 0.2 m to the right, so the box stands lower and to the left of it.
            var eyeStep = new Vector3(0.2f, 0.4f, 0);
            RenderSnapshot moved = WithBox(box: box, white: white, position: new Vector3(0, 0, -6) - eyeStep);
            moved.OriginInFrame = eyeStep;

            renderer.Render(moved, 1f);

            ShouldMoveAsAnalytic(
                renderer: renderer,
                center: new Vector3(0, 0, -5.5f) - eyeStep,
                previousOf: point => point + eyeStep,
                what: "a still box seen from a stepping eye"
            );
        }
        finally
        {
            Gpu.DisposeAndAssertClean(renderer);
        }
    }

    [Fact]
    public void ATurningBoxGivesTheAnalyticMotion()
    {
        VulkanRenderer renderer = Gpu.Headless(width: Width, height: Height);
        try
        {
            MeshId box = renderer.CreateMesh(Primitives.Box(new(0.5f)));
            MaterialId white = renderer.CreateMaterial(MaterialDesc.Color(Vector4.One, roughness: 0.5f));
            var center = new Vector3(0, 0, -6);
            var turn = Quaternion.CreateFromAxisAngle(Vector3.UnitZ, 0.2f);
            renderer.Render(WithBox(box: box, white: white, position: center), 1f);

            renderer.Render(WithBox(box: box, white: white, position: center, rotation: turn), 1f);

            ShouldMoveAsAnalytic(
                renderer: renderer,
                center: center + new Vector3(0, 0, 0.5f),
                previousOf: point => center + Vector3.Transform(point - center, Quaternion.Conjugate(turn)),
                what: "a box that turned"
            );
        }
        finally
        {
            Gpu.DisposeAndAssertClean(renderer);
        }
    }

    [Fact]
    public void AGrowingBoxGivesTheAnalyticMotion()
    {
        VulkanRenderer renderer = Gpu.Headless(width: Width, height: Height);
        try
        {
            MeshId box = renderer.CreateMesh(Primitives.Box(new(0.5f)));
            MaterialId white = renderer.CreateMaterial(MaterialDesc.Color(Vector4.One, roughness: 0.5f));
            var center = new Vector3(0, 0, -6);
            renderer.Render(WithBox(box: box, white: white, position: center), 1f);
            RenderSnapshot grown = WithBox(box: box, white: white, position: center);
            RenderInstance growing = grown.Instances[0];
            growing.Scale = new(1.25f);
            growing.PreviousScale = new(1.25f);
            grown.Instances[0] = growing;

            renderer.Render(grown, 1f);

            ShouldMoveAsAnalytic(
                renderer: renderer,
                center: center + new Vector3(0, 0, 0.625f),
                previousOf: point => center + (point - center) / 1.25f,
                what: "a box that grew"
            );
        }
        finally
        {
            Gpu.DisposeAndAssertClean(renderer);
        }
    }

    [Fact]
    public void ADeformedMeshMovesFromItsLastFramesDeformedPositions()
    {
        VulkanRenderer renderer = Gpu.Headless(width: Width, height: Height);
        try
        {
            MeshData cylinder = Primitives.BendingCylinder(radius: 0.4f, length: 2);
            Rig rig = cylinder.Rigging!.Rig;
            MeshId mesh = renderer.CreateMesh(cylinder);
            MaterialId white = renderer.CreateMaterial(MaterialDesc.Color(Vector4.One, roughness: 0.5f));
            var stand = new Vector3(0, -1, -6);
            var shift = new Vector3(0.3f, 0, 0);
            renderer.Render(PosedCylinder(mesh: mesh, material: white, stand: stand, pose: rig.Rest), 1f);
            // The Base joint carries the whole tube, so the deformed copy moves 0.3 m along +X, and the instance does not.
            Pose moved = rig.Rest.Joints[0] with
            {
                Position = shift,
            };
            RigPose shifted = rig.Rest with { Joints = rig.Rest.Joints.SetItem(0, moved) };

            renderer.Render(PosedCylinder(mesh: mesh, material: white, stand: stand, pose: shifted), 1f);

            ShouldMoveAsAnalytic(
                renderer: renderer,
                center: stand + shift + new Vector3(0, 0.5f, 0.4f),
                previousOf: point => point - shift,
                what: "a deformed copy that moved"
            );
        }
        finally
        {
            Gpu.DisposeAndAssertClean(renderer);
        }
    }

    private static RenderSnapshot PosedCylinder(MeshId mesh, MaterialId material, Vector3 stand, RigPose pose)
    {
        RenderSnapshot snapshot = Gpu.Snapshot(new(0.3f, 1f, 0.4f));
        snapshot.Instances.Add(
            Gpu.Instance(
                mesh: mesh,
                material: material,
                position: stand,
                scale: Vector3.One,
                chainDepth: TestChains.CamerasFrame,
                entity: 2
            )
        );
        snapshot.AddRigPose(
            instance: 0,
            joints: pose.Joints.AsSpan(),
            previousJoints: pose.Joints.AsSpan(),
            weights: pose.Weights.AsSpan(),
            previousWeights: pose.Weights.AsSpan()
        );
        return snapshot;
    }

    [Fact]
    public void ANewInstanceGivesAMotionOutsideTheLastFramesImage()
    {
        VulkanRenderer renderer = Gpu.Headless(width: Width, height: Height);
        try
        {
            MeshId box = renderer.CreateMesh(Primitives.Box(new(0.5f)));
            MaterialId white = renderer.CreateMaterial(MaterialDesc.Color(Vector4.One, roughness: 0.5f));
            renderer.Render(Gpu.Snapshot(new(0.3f, 1f, 0.4f)), 1f);

            renderer.Render(WithBox(box: box, white: white, position: new(0, 0, -6)), 1f);

            Vector3 motion = MotionAt(renderer.ReadMotion(), Width / 2, Height / 2);
            new Vector2(motion.X, motion.Y).Should().Be(new Vector2(2, 2));
        }
        finally
        {
            Gpu.DisposeAndAssertClean(renderer);
        }
    }

    [Fact]
    public void TheSkysMotionFollowsATurningRoot()
    {
        VulkanRenderer renderer = Gpu.Headless(width: Width, height: Height);
        try
        {
            var before = Quaternion.CreateFromAxisAngle(Vector3.UnitY, 0.1f);
            var after = Quaternion.CreateFromAxisAngle(Vector3.UnitY, 0.15f);
            renderer.Render(SkyWithRoot(rotation: before, previousRotation: before), 1f);

            renderer.Render(SkyWithRoot(rotation: after, previousRotation: before), 1f);

            float[] motion = renderer.ReadMotion();
            float tanY = MathF.Tan(Gpu.Fov / 2);
            float tanX = tanY * Width / Height;
            Quaternion lastFromNow = before * Quaternion.Conjugate(after);
            foreach ((int x, int y) in new[] { (Width / 2, Height / 2), (40, 30), (280, 150) })
            {
                var ray = new Vector3(((x + 0.5f) / Width * 2 - 1) * tanX, (1 - (y + 0.5f) / Height * 2) * tanY, -1);
                Vector2 expected = ExpectedMotion(Vector3.Transform(ray, lastFromNow), x, y);
                Vector3 actual = MotionAt(motion, x, y);
                actual.X.Should().BeApproximately(expected.X, MotionTolerance, "the sky at ({0}, {1})", x, y);
                actual.Y.Should().BeApproximately(expected.Y, MotionTolerance, "the sky at ({0}, {1})", x, y);
            }
        }
        finally
        {
            Gpu.DisposeAndAssertClean(renderer);
        }
    }

    /// <summary>A snapshot of nothing but sky, whose chain is the camera's frame and a root with the given axes.</summary>
    private static RenderSnapshot SkyWithRoot(Quaternion rotation, Quaternion previousRotation)
    {
        RenderSnapshot snapshot = Gpu.Snapshot(new(0.3f, 1f, 0.4f));
        snapshot.Chain.Add(new(FrameId: 2, Rotation: Quaternion.Identity, PreviousRotation: Quaternion.Identity));
        snapshot.Chain.Add(new(FrameId: 1, Rotation: rotation, PreviousRotation: previousRotation));
        return snapshot;
    }
}
