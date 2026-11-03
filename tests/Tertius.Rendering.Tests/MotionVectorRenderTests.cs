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

    /// <summary>
    /// The direction of the ray through the center of pixel (x, y), in the axes of the camera's frame, for a camera with
    /// <paramref name="camera"/> as its axes. Its length is 1 along the camera's view axis.
    /// </summary>
    private static Vector3 RayThrough(int x, int y, Quaternion camera)
    {
        float tanY = MathF.Tan(Gpu.Fov / 2);
        float tanX = tanY * Width / Height;
        var ray = new Vector3(((x + 0.5f) / Width * 2 - 1) * tanX, (1 - (y + 0.5f) / Height * 2) * tanY, -1);
        return Vector3.Transform(ray, camera);
    }

    /// <summary>
    /// The camera-relative point that pixel (x, y) shows, on its ray at its distance, for a camera with
    /// <paramref name="camera"/> as its axes, or one that looks down −Z.
    /// </summary>
    private static Vector3 PointAt(float[] distance, int x, int y, Quaternion? camera = null) =>
        Vector3.Normalize(RayThrough(x, y, camera ?? Quaternion.Identity)) * distance[y * Width + x];

    /// <summary>
    /// The motion that pixel (x, y) must hold when the point that it shows was at <paramref name="previous"/>, relative to
    /// the eye, and the last frame's camera had <paramref name="lastCamera"/> as its axes, or looked down −Z.
    /// </summary>
    private static Vector2 ExpectedMotion(Vector3 previous, int x, int y, Quaternion? lastCamera = null)
    {
        Vector2 last = Gpu.Project(
            point: previous,
            cameraRotation: lastCamera ?? Quaternion.Identity,
            width: Width,
            height: Height
        );
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
            Vector3 point = PointAt(distance: distance, x: x, y: y);
            Vector3 previous = previousOf(point);
            Vector2 expected = ExpectedMotion(previous: previous, x: x, y: y);
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
    public void ACutoutBoxThatMovesGivesTheAnalyticMotion()
    {
        VulkanRenderer renderer = Gpu.Headless(width: Width, height: Height);
        try
        {
            MeshId box = renderer.CreateMesh(Primitives.Box(new(0.5f)));
            // Opaque everywhere, but drawn by the G-buffer's cutout pipeline.
            MaterialId cutout = renderer.CreateMaterial(
                MaterialDesc.Color(Vector4.One, roughness: 0.5f) with
                {
                    Alpha = new CutoutAlpha(0.5f),
                }
            );
            var step = new Vector3(0.3f, 0.1f, 0.8f);
            renderer.Render(WithBox(box: box, white: cutout, position: new(0, 0, -6)), 1f);

            renderer.Render(WithBox(box: box, white: cutout, position: new Vector3(0, 0, -6) + step), 1f);

            ShouldMoveAsAnalytic(
                renderer: renderer,
                center: new Vector3(0, 0, -5.5f) + step,
                previousOf: point => point - step,
                what: "a cutout box that moved"
            );
        }
        finally
        {
            Gpu.DisposeAndAssertClean(renderer);
        }
    }

    [Fact]
    public void AnInstancePlacedAfterTheFirstThousandGivesItsMotion()
    {
        VulkanRenderer renderer = Gpu.Headless(width: Width, height: Height);
        try
        {
            MeshId box = renderer.CreateMesh(Primitives.Box(new(0.5f)));
            MaterialId white = renderer.CreateMaterial(MaterialDesc.Color(Vector4.One, roughness: 0.5f));
            var step = new Vector3(0.3f, 0.1f, 0.8f);
            RenderSnapshot Crowd(Vector3 position)
            {
                // 1,100 boxes behind the camera come first, so the moving box's motion row lies past the frame's first
                // 1,024, the motion buffer's first size.
                RenderSnapshot snapshot = Gpu.Snapshot(new(0.3f, 1f, 0.4f));
                for (uint i = 0; i < 1_100; i++)
                    snapshot.Instances.Add(
                        Gpu.Instance(
                            mesh: box,
                            material: white,
                            position: new(i % 40 - 20f, i / 40 - 15f, 30),
                            scale: Vector3.One,
                            chainDepth: TestChains.CamerasFrame,
                            entity: 10 + i
                        )
                    );
                snapshot.Instances.Add(
                    Gpu.Instance(
                        mesh: box,
                        material: white,
                        position: position,
                        scale: Vector3.One,
                        chainDepth: TestChains.CamerasFrame,
                        entity: 5_000
                    )
                );
                return snapshot;
            }
            renderer.Render(Crowd(new(0, 0, -6)), 1f);

            renderer.Render(Crowd(new Vector3(0, 0, -6) + step), 1f);

            ShouldMoveAsAnalytic(
                renderer: renderer,
                center: new Vector3(0, 0, -5.5f) + step,
                previousOf: point => point - step,
                what: "the last of 1,101 boxes"
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

    /// <summary>
    /// The kept positions alternate by the parity of the drawn frames, so the third frame reads the second frame's buffer,
    /// not the first frame's, which its own deform pass then overwrites.
    /// </summary>
    [Fact]
    public void AThirdFrameOfADeformedMeshMovesFromTheSecondFramesPositions()
    {
        VulkanRenderer renderer = Gpu.Headless(width: Width, height: Height);
        try
        {
            MeshData cylinder = Primitives.BendingCylinder(radius: 0.4f, length: 2);
            Rig rig = cylinder.Rigging!.Rig;
            MeshId mesh = renderer.CreateMesh(cylinder);
            MaterialId white = renderer.CreateMaterial(MaterialDesc.Color(Vector4.One, roughness: 0.5f));
            var stand = new Vector3(0, -1, -6);
            var step = new Vector3(0.3f, 0, 0);
            RigPose Shifted(Vector3 shift) =>
                rig.Rest with
                {
                    Joints = rig.Rest.Joints.SetItem(0, rig.Rest.Joints[0] with { Position = shift }),
                };
            renderer.Render(PosedCylinder(mesh: mesh, material: white, stand: stand, pose: rig.Rest), 1f);
            renderer.Render(PosedCylinder(mesh: mesh, material: white, stand: stand, pose: Shifted(step)), 1f);

            renderer.Render(PosedCylinder(mesh: mesh, material: white, stand: stand, pose: Shifted(2 * step)), 1f);

            ShouldMoveAsAnalytic(
                renderer: renderer,
                center: stand + 2 * step + new Vector3(0, 0.5f, 0.4f),
                previousOf: point => point - step,
                what: "a deformed copy in its third frame"
            );
        }
        finally
        {
            Gpu.DisposeAndAssertClean(renderer);
        }
    }

    [Fact]
    public void EachOfTwoDeformedMeshesMovesFromItsOwnLastPositions()
    {
        VulkanRenderer renderer = Gpu.Headless(width: Width, height: Height);
        try
        {
            MeshData cylinder = Primitives.BendingCylinder(radius: 0.4f, length: 2);
            Rig rig = cylinder.Rigging!.Rig;
            MeshId mesh = renderer.CreateMesh(cylinder);
            MaterialId white = renderer.CreateMaterial(MaterialDesc.Color(Vector4.One, roughness: 0.5f));
            var left = new Vector3(-1.5f, -1, -6);
            var right = new Vector3(1.5f, -1, -6);
            var shift = new Vector3(0.3f, 0, 0);
            RigPose shifted = rig.Rest with
            {
                Joints = rig.Rest.Joints.SetItem(0, rig.Rest.Joints[0] with { Position = shift }),
            };
            renderer.Render(
                PosedCylinders(mesh: mesh, material: white, cylinders: [(left, rig.Rest), (right, rig.Rest)]),
                1f
            );

            // Only the second copy, whose kept positions follow the first's, moves.
            renderer.Render(
                PosedCylinders(mesh: mesh, material: white, cylinders: [(left, rig.Rest), (right, shifted)]),
                1f
            );

            ShouldMoveAsAnalytic(
                renderer: renderer,
                center: right + shift + new Vector3(0, 0.5f, 0.4f),
                previousOf: point => point - shift,
                what: "the second deformed copy"
            );
        }
        finally
        {
            Gpu.DisposeAndAssertClean(renderer);
        }
    }

    [Fact]
    public void ADeformedMeshWhoseCopyMovedInTheRegionMovesFromItsLastPositions()
    {
        VulkanRenderer renderer = Gpu.Headless(width: Width, height: Height);
        try
        {
            MeshData cylinder = Primitives.BendingCylinder(radius: 0.4f, length: 2);
            Rig rig = cylinder.Rigging!.Rig;
            MeshId mesh = renderer.CreateMesh(cylinder);
            MaterialId white = renderer.CreateMaterial(MaterialDesc.Color(Vector4.One, roughness: 0.5f));
            var left = new Vector3(-1.5f, -1, -6);
            var right = new Vector3(1.5f, -1, -6);
            var shift = new Vector3(0.3f, 0, 0);
            RigPose shifted = rig.Rest with
            {
                Joints = rig.Rest.Joints.SetItem(0, rig.Rest.Joints[0] with { Position = shift }),
            };
            renderer.Render(
                PosedCylinders(mesh: mesh, material: white, cylinders: [(right, rig.Rest)], firstEntity: 3),
                1f
            );

            // A new copy with a lower key takes the region's first place, so the right copy's frame mesh changes too.
            renderer.Render(
                PosedCylinders(mesh: mesh, material: white, cylinders: [(left, rig.Rest), (right, shifted)]),
                1f
            );

            ShouldMoveAsAnalytic(
                renderer: renderer,
                center: right + shift + new Vector3(0, 0.5f, 0.4f),
                previousOf: point => point - shift,
                what: "a deformed copy whose place in the region moved"
            );
        }
        finally
        {
            Gpu.DisposeAndAssertClean(renderer);
        }
    }

    /// <summary>
    /// A frame whose deform region grows copies the last drawn frame's kept positions into the new buffers once: the
    /// frame after it reads what the growing frame wrote.
    /// </summary>
    [Fact]
    public void ADeformedMeshInTheFrameAfterTheRegionGrewMovesFromTheGrowingFramesPositions()
    {
        VulkanRenderer renderer = Gpu.Headless(width: Width, height: Height);
        try
        {
            MeshData cylinder = Primitives.BendingCylinder(radius: 0.4f, length: 2);
            Rig rig = cylinder.Rigging!.Rig;
            MeshId mesh = renderer.CreateMesh(cylinder);
            MaterialId white = renderer.CreateMaterial(MaterialDesc.Color(Vector4.One, roughness: 0.5f));
            var stand = new Vector3(0, -1, -6);
            var step = new Vector3(0.3f, 0, 0);
            RigPose Shifted(Vector3 shift) =>
                rig.Rest with
                {
                    Joints = rig.Rest.Joints.SetItem(0, rig.Rest.Joints[0] with { Position = shift }),
                };
            // Two more copies triple the vertices of the region, past its power of two.
            (Vector3 Stand, RigPose Pose)[] Frame(Vector3 shift) =>
                [(stand, Shifted(shift)), (new(-2.5f, -1, -6), rig.Rest), (new(2.5f, -1, -6), rig.Rest)];
            renderer.Render(PosedCylinders(mesh: mesh, material: white, cylinders: [(stand, rig.Rest)]), 1f);
            renderer.Render(PosedCylinders(mesh: mesh, material: white, cylinders: Frame(step)), 1f);

            renderer.Render(PosedCylinders(mesh: mesh, material: white, cylinders: Frame(2 * step)), 1f);

            ShouldMoveAsAnalytic(
                renderer: renderer,
                center: stand + 2 * step + new Vector3(0, 0.5f, 0.4f),
                previousOf: point => point - step,
                what: "a deformed copy after the region grew"
            );
        }
        finally
        {
            Gpu.DisposeAndAssertClean(renderer);
        }
    }

    /// <summary>One posed cylinder per stand, each with its own pose, entities <paramref name="firstEntity"/> upward in order.</summary>
    private static RenderSnapshot PosedCylinders(
        MeshId mesh,
        MaterialId material,
        ReadOnlySpan<(Vector3 Stand, RigPose Pose)> cylinders,
        uint firstEntity = 2
    )
    {
        RenderSnapshot snapshot = Gpu.Snapshot(new(0.3f, 1f, 0.4f));
        for (int i = 0; i < cylinders.Length; i++)
        {
            snapshot.Instances.Add(
                Gpu.Instance(
                    mesh: mesh,
                    material: material,
                    position: cylinders[i].Stand,
                    scale: Vector3.One,
                    chainDepth: TestChains.CamerasFrame,
                    entity: firstEntity + (uint)i
                )
            );
            RigPose pose = cylinders[i].Pose;
            snapshot.AddRigPose(
                instance: i,
                joints: pose.Joints.AsSpan(),
                previousJoints: pose.Joints.AsSpan(),
                weights: pose.Weights.AsSpan(),
                previousWeights: pose.Weights.AsSpan()
            );
        }
        return snapshot;
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
                Vector2 expected = ExpectedMotion(previous: Vector3.Transform(ray, lastFromNow), x: x, y: y);
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

    [Fact]
    public void AStillBoxAndTheSkySeenFromATurningCameraGiveTheMotionOfTheTurn()
    {
        VulkanRenderer renderer = Gpu.Headless(width: Width, height: Height);
        try
        {
            MeshId box = renderer.CreateMesh(Primitives.Box(new(0.5f)));
            MaterialId white = renderer.CreateMaterial(MaterialDesc.Color(Vector4.One, roughness: 0.5f));
            var before = Quaternion.CreateFromYawPitchRoll(0.05f, 0.02f, 0f);
            var after = Quaternion.CreateFromYawPitchRoll(0.15f, 0.02f, 0f);
            // The box stands 6 m ahead of the turned camera, in the axes of the camera's frame, in both frames.
            var center = Vector3.Transform(new Vector3(0, 0, -6), after);
            renderer.Render(Turned(WithBox(box: box, white: white, position: center), before), 1f);

            renderer.Render(Turned(WithBox(box: box, white: white, position: center), after), 1f);

            float[] distance = renderer.ReadDistance();
            float[] motion = renderer.ReadMotion();
            var front = Vector3.Transform(new Vector3(0, 0, -5.5f), after);
            Vector2 pixel = Gpu.Project(point: front, cameraRotation: after, width: Width, height: Height);
            for (int dy = -4; dy <= 4; dy++)
            for (int dx = -4; dx <= 4; dx++)
            {
                int x = (int)pixel.X + dx,
                    y = (int)pixel.Y + dy;
                Vector3 point = PointAt(distance: distance, x: x, y: y, camera: after);
                ShouldHoldMotion(
                    motion: motion,
                    x: x,
                    y: y,
                    expected: ExpectedMotion(previous: point, x: x, y: y, lastCamera: before),
                    what: "the still box"
                );
            }

            // The sky has no distance: its point is the direction of the pixel's ray, which a turn of the camera alone
            // moves across the image.
            foreach ((int x, int y) in new[] { (12, 10), (300, 20), (20, 165) })
                ShouldHoldMotion(
                    motion: motion,
                    x: x,
                    y: y,
                    expected: ExpectedMotion(previous: RayThrough(x, y, after), x: x, y: y, lastCamera: before),
                    what: "the sky"
                );
        }
        finally
        {
            Gpu.DisposeAndAssertClean(renderer);
        }
    }

    [Fact]
    public void ABillboardMovesWithItsObjectsCenter()
    {
        VulkanRenderer renderer = Gpu.Headless(width: Width, height: Height, impostors: true);
        try
        {
            MeshId sphere = renderer.CreateMesh(Primitives.Sphere(1f, 48, 24));
            MaterialId white = renderer.CreateMaterial(MaterialDesc.Color(Vector4.One, roughness: 0.5f));
            // About 60 px tall, below the 200 px under which an object draws as a billboard.
            var center = new Vector3(0, 0, -11);
            var step = new Vector3(0.4f, 0.2f, 0);
            renderer.Render(WithBox(box: sphere, white: white, position: center), 1f);
            renderer.Render(WithBox(box: sphere, white: white, position: center), 1f);

            renderer.Render(WithBox(box: sphere, white: white, position: center + step), 1f);

            renderer.LastFrameStats.Impostors.Should().Be(1, "the test needs the sphere drawn as a billboard");
            // The distance of a billboard's pixel is the distance of its quad's point, whose last place is the same point
            // moved back by the object's step.
            ShouldMoveAsAnalytic(
                renderer: renderer,
                center: center + step,
                previousOf: point => point - step,
                what: "a billboard whose object moved"
            );
        }
        finally
        {
            Gpu.DisposeAndAssertClean(renderer);
        }
    }

    [Fact]
    public void ABillboardMovesWithItsOwnObjectsCenterNotWithAnotherObjects()
    {
        VulkanRenderer renderer = Gpu.Headless(width: Width, height: Height, impostors: true);
        try
        {
            MeshId sphere = renderer.CreateMesh(Primitives.Sphere(1f, 48, 24));
            MaterialId white = renderer.CreateMaterial(MaterialDesc.Color(Vector4.One, roughness: 0.5f));
            var still = new Vector3(-4, 0, -11);
            var center = new Vector3(2, 0, -11);
            var step = new Vector3(0.4f, 0.2f, 0);
            RenderSnapshot Spheres(Vector3 moving)
            {
                // The still sphere's entity comes first, so the moving sphere's billboard reads the second motion row.
                RenderSnapshot snapshot = Gpu.Snapshot(new(0.3f, 1f, 0.4f));
                foreach (
                    (Vector3 position, uint entity) in (ReadOnlySpan<(Vector3 Position, uint Entity)>)
                        [(still, 4u), (moving, 5u)]
                )
                    snapshot.Instances.Add(
                        Gpu.Instance(
                            mesh: sphere,
                            material: white,
                            position: position,
                            scale: Vector3.One,
                            chainDepth: TestChains.CamerasFrame,
                            entity: entity
                        )
                    );
                return snapshot;
            }
            renderer.Render(Spheres(center), 1f);
            renderer.Render(Spheres(center), 1f);

            renderer.Render(Spheres(center + step), 1f);

            renderer.LastFrameStats.Impostors.Should().Be(2, "the test needs both spheres drawn as billboards");
            ShouldMoveAsAnalytic(
                renderer: renderer,
                center: center + step,
                previousOf: point => point - step,
                what: "the second billboard"
            );
        }
        finally
        {
            Gpu.DisposeAndAssertClean(renderer);
        }
    }

    [Fact]
    public void ANewBillboardGivesAMotionOutsideTheLastFramesImage()
    {
        VulkanRenderer renderer = Gpu.Headless(width: Width, height: Height, impostors: true);
        try
        {
            MeshId sphere = renderer.CreateMesh(Primitives.Sphere(1f, 48, 24));
            MaterialId white = renderer.CreateMaterial(MaterialDesc.Color(Vector4.One, roughness: 0.5f));
            renderer.Render(Gpu.Snapshot(new(0.3f, 1f, 0.4f)), 1f);

            renderer.Render(WithBox(box: sphere, white: white, position: new(0, 0, -11)), 1f);

            renderer.LastFrameStats.Impostors.Should().Be(1, "the test needs the sphere drawn as a billboard");
            Vector3 motion = MotionAt(renderer.ReadMotion(), Width / 2, Height / 2);
            new Vector2(motion.X, motion.Y).Should().Be(new Vector2(2, 2));
        }
        finally
        {
            Gpu.DisposeAndAssertClean(renderer);
        }
    }

    private static void ShouldHoldMotion(float[] motion, int x, int y, Vector2 expected, string what)
    {
        Vector3 actual = MotionAt(motion, x, y);
        actual.X.Should().BeApproximately(expected.X, MotionTolerance, "{0} at ({1}, {2})", what, x, y);
        actual.Y.Should().BeApproximately(expected.Y, MotionTolerance, "{0} at ({1}, {2})", what, x, y);
    }

    /// <summary><paramref name="snapshot"/> seen from a camera with <paramref name="camera"/> as its axes in both builds.</summary>
    private static RenderSnapshot Turned(RenderSnapshot snapshot, Quaternion camera)
    {
        snapshot.Camera.Rotation = camera;
        snapshot.Camera.PreviousRotation = camera;
        return snapshot;
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
