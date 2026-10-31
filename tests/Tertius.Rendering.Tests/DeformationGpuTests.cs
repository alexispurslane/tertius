using System.Numerics;
using Tertius.Testing;

namespace Tertius.Rendering.Tests;

/// <summary>
/// The deform pass on the GPU: its copies match the C# deformation, every pass draws them, and the region grows
/// (docs/plans/skinning-and-morphs.md, "The deformation on the GPU"). The cylinders are 2 m long and 0.2 m wide, with
/// Base at the origin and Tip 1 m up, and stand 6 m in front of the camera.
/// </summary>
[Collection(GpuCollection.Name)]
public sealed class DeformationGpuTests
{
    private const float Length = 2;
    private const float Radius = 0.2f;
    private const int Width = 640;
    private const int Height = 360;

    /// <summary>Where the posed cylinders stand: Base 1 m below the eye, 6 m ahead.</summary>
    private static readonly Vector3 Stand = new(0, -1, -6);

    private static MeshData Cylinder() => Primitives.BendingCylinder(radius: Radius, length: Length);

    /// <summary><paramref name="rest"/> with Tip turned by <paramref name="angle"/> about +Z: a positive angle bends the tip toward −X.</summary>
    private static RigPose BentTip(RigPose rest, Rig rig, float angle)
    {
        int tip = rig.JointNamed("Tip")!.Value;
        Pose turned = rest.Joints[tip] with { Rotation = Quaternion.CreateFromAxisAngle(Vector3.UnitZ, angle) };
        return rest with { Joints = rest.Joints.SetItem(tip, turned) };
    }

    /// <summary>A still rig pose of instance <paramref name="instance"/>: the same pose at both ends.</summary>
    private static void AddStillPose(RenderSnapshot snapshot, int instance, RigPose pose) =>
        snapshot.AddRigPose(
            instance: instance,
            joints: pose.Joints.AsSpan(),
            previousJoints: pose.Joints.AsSpan(),
            weights: pose.Weights.AsSpan(),
            previousWeights: pose.Weights.AsSpan()
        );

    private static RenderInstance Posed(MeshId mesh, MaterialId material, Vector3 position) =>
        Gpu.Instance(
            mesh: mesh,
            material: material,
            position: position,
            scale: Vector3.One,
            flags: RenderFlags.CastsShadow,
            chainDepth: TestChains.CamerasFrame
        );

    /// <summary>Every vertex of <paramref name="mesh"/> deformed in C# by the still <paramref name="pose"/>.</summary>
    private static GpuVertex[] Expected(MeshData mesh, RigPose pose)
    {
        PackedDeform packed = MeshDeform.Pack(mesh, MeshCards.Number(mesh.Vertices, mesh.Indices))!;
        var rows = new Vector4[packed.Deform.PoseRows];
        packed.Deform.PoseInto(
            previousJoints: pose.Joints.AsSpan(),
            joints: pose.Joints.AsSpan(),
            previousWeights: pose.Weights.AsSpan(),
            weights: pose.Weights.AsSpan(),
            alpha: 1,
            rows: rows
        );
        return
        [
            .. packed.Vertices.Select(source =>
                MeshDeform.Deformed(
                    source: source,
                    deltas: packed.Deltas.AsSpan((int)source.MorphStart, (int)source.MorphCount),
                    rows: rows,
                    paletteJoints: packed.Deform.PaletteJoints
                )
            ),
        ];
    }

    /// <summary>
    /// Every number of <paramref name="actual"/> lies within 10^−5 × (1 + |expected|) of <paramref name="expected"/>: the GPU
    /// may fuse a multiply and an add (docs/plans/skinning-and-morphs.md, "The math, and its C# copy").
    /// </summary>
    private static void ShouldMatch(ReadOnlySpan<GpuVertex> actual, GpuVertex[] expected, string copy)
    {
        actual.Length.Should().Be(expected.Length, "{0} has one vertex per vertex of its mesh", copy);
        for (int i = 0; i < expected.Length; i++)
        {
            float[] shown = Numbers(actual[i]);
            float[] wanted = Numbers(expected[i]);
            for (int c = 0; c < wanted.Length; c++)
                if (MathF.Abs(shown[c] - wanted[c]) > 1e-5f * (1 + MathF.Abs(wanted[c])))
                    shown[c]
                        .Should()
                        .BeApproximately(
                            wanted[c],
                            1e-5f * (1 + MathF.Abs(wanted[c])),
                            "number {0} of vertex {1} of {2}",
                            c,
                            i,
                            copy
                        );
        }
    }

    private static float[] Numbers(in GpuVertex v) =>
        [
            v.Position.X,
            v.Position.Y,
            v.Position.Z,
            v.Normal.X,
            v.Normal.Y,
            v.Normal.Z,
            v.Tangent.X,
            v.Tangent.Y,
            v.Tangent.Z,
            v.Tangent.W,
            v.TexCoord.X,
            v.TexCoord.Y,
        ];

    /// <summary>The G-buffer distance of the pixel that <paramref name="point"/>, camera-relative, projects to: 0 for sky.</summary>
    private static float DistanceAt(float[] distance, Vector3 point)
    {
        Vector2 pixel = Gpu.Project(point: point, width: Width, height: Height);
        return distance[(int)pixel.Y * Width + (int)pixel.X];
    }

    /// <summary>Renders one still frame of a single posed mesh and gives its copy and the C# copy.</summary>
    private static (GpuVertex[] Shown, GpuVertex[] Expected) DeformOne(MeshData mesh, RigPose pose)
    {
        VulkanRenderer renderer = Gpu.Headless(width: Width, height: Height);
        try
        {
            MeshId id = renderer.CreateMesh(mesh);
            MaterialId white = renderer.CreateMaterial(MaterialDesc.Color(Vector4.One, roughness: 0.5f));
            RenderSnapshot snapshot = Gpu.Snapshot(new(0.3f, 1f, 0.2f));
            snapshot.Instances.Add(Posed(id, white, Stand));
            AddStillPose(snapshot, instance: 0, pose: pose);
            renderer.Render(snapshot, 1f);
            renderer.LastFrameStats.DeformedInstances.Should().Be(1, "the posed instance fits its mesh");
            return (renderer.ReadDeformedVertices(), Expected(mesh, pose));
        }
        finally
        {
            Gpu.DisposeAndAssertClean(renderer);
        }
    }

    [Fact]
    public void DeformedCylinderMatchesTheCSharpDeformation()
    {
        MeshData cylinder = Cylinder();
        Rig rig = cylinder.Rigging!.Rig;
        (GpuVertex[] shown, GpuVertex[] expected) = DeformOne(cylinder, BentTip(rig.Rest, rig, angle: 1.1f));

        ShouldMatch(shown, expected, "the bent cylinder");
    }

    [Fact]
    public void DeformedBoxMatchesTheCSharpDeformation()
    {
        MeshData box = Primitives.MorphingBox(new(0.5f, 0.3f, 0.4f));
        RigPose pose = box.Rigging!.Rig.Rest with { Weights = [0.5f, -0.75f] };
        (GpuVertex[] shown, GpuVertex[] expected) = DeformOne(box, pose);

        ShouldMatch(shown, expected, "the stretched, leaning box");
    }

    [Fact]
    public void DeformedTwistingCylinderMatchesTheCSharpDeformation()
    {
        MeshData cylinder = Primitives.TwistingBendingCylinder(radius: Radius, length: Length);
        Rig rig = cylinder.Rigging!.Rig;
        RigPose pose = BentTip(rig.Rest, rig, angle: -0.8f) with { Weights = [0.6f] };
        (GpuVertex[] shown, GpuVertex[] expected) = DeformOne(cylinder, pose);

        ShouldMatch(shown, expected, "the twisted, bent cylinder");
    }

    [Fact]
    public void DeformedCopyKeepsTheBitangentSign()
    {
        MeshData cylinder = Cylinder();
        // A mirrored UV layout: every bitangent points the other way.
        MeshData mirrored = cylinder with
        {
            Vertices = [.. cylinder.Vertices.Select(v => v with { Tangent = v.Tangent with { W = -1 } })],
        };
        Rig rig = mirrored.Rigging!.Rig;
        (GpuVertex[] shown, GpuVertex[] expected) = DeformOne(mirrored, BentTip(rig.Rest, rig, angle: 0.5f));

        ShouldMatch(shown, expected, "the mirrored cylinder");
        shown.Should().OnlyContain(v => v.Tangent.W == -1, "a morph or a skin never flips the sign");
    }

    [Fact]
    public void ADeformedCopyCarriesTheCardNumbersOfItsMesh()
    {
        // Two cylinders 0.5 m apart in one mesh, with one skin: two cards.
        MeshData one = Cylinder();
        MeshSkin skin = one.Rigging!.Skin!;
        var two = new MeshData(
            [.. one.Vertices, .. one.Vertices.Select(v => v with { Position = v.Position + new Vector3(0.5f, 0, 0) })],
            [.. one.Indices, .. one.Indices.Select(index => index + (uint)one.Vertices.Length)]
        )
        {
            Rigging = one.Rigging with { Skin = skin with { Vertices = [.. skin.Vertices, .. skin.Vertices] } },
        };
        Rig rig = two.Rigging.Rig;
        uint[] cards = MeshCards.Number(two.Vertices, two.Indices);
        cards.Distinct().Should().HaveCount(2, "the test needs a mesh of two cards");

        (GpuVertex[] shown, _) = DeformOne(two, BentTip(rig.Rest, rig, angle: 0.7f));

        shown.Select(v => v.Card).Should().Equal(cards, "a morph or a skin never moves a vertex to another card");
    }

    [Fact]
    public void TwoInstancesOfOneMeshDeformApart()
    {
        VulkanRenderer renderer = Gpu.Headless(width: Width, height: Height);
        try
        {
            MeshData cylinder = Cylinder();
            Rig rig = cylinder.Rigging!.Rig;
            MeshId mesh = renderer.CreateMesh(cylinder);
            MaterialId white = renderer.CreateMaterial(MaterialDesc.Color(Vector4.One, roughness: 0.5f));
            RenderSnapshot snapshot = Gpu.Snapshot(new(0.3f, 1f, 0.2f));
            RigPose left = BentTip(rig.Rest, rig, angle: 0.9f),
                right = BentTip(rig.Rest, rig, angle: -0.9f);
            snapshot.Instances.Add(Posed(mesh, white, Stand - Vector3.UnitX));
            snapshot.Instances.Add(Posed(mesh, white, Stand + Vector3.UnitX));
            AddStillPose(snapshot, instance: 0, pose: left);
            AddStillPose(snapshot, instance: 1, pose: right);

            renderer.Render(snapshot, 1f);
            GpuVertex[] shown = renderer.ReadDeformedVertices();

            int count = cylinder.Vertices.Length;
            shown.Should().HaveCount(2 * count, "each posed instance has its own copy");
            ShouldMatch(shown.AsSpan(0, count), Expected(cylinder, left), "the first copy");
            ShouldMatch(shown.AsSpan(count, count), Expected(cylinder, right), "the second copy");
        }
        finally
        {
            Gpu.DisposeAndAssertClean(renderer);
        }
    }

    [Fact]
    public void OneDispatchDeformsEveryJobOfTheFrame()
    {
        VulkanRenderer renderer = Gpu.Headless(width: Width, height: Height);
        try
        {
            MaterialId white = renderer.CreateMaterial(MaterialDesc.Color(Vector4.One, roughness: 0.5f));
            MeshData cylinder = Cylinder(),
                box = Primitives.MorphingBox(new(0.5f, 0.3f, 0.4f)),
                twisting = Primitives.TwistingBendingCylinder(radius: Radius, length: Length);
            Rig rig = cylinder.Rigging!.Rig,
                twistingRig = twisting.Rigging!.Rig;
            // Copies of 477, 24 and 477 vertices: 8, 1 and 8 workgroups, so a job's groups start mid-dispatch.
            (MeshData Mesh, RigPose Pose)[] copies =
            [
                (cylinder, BentTip(rig.Rest, rig, angle: 1.1f)),
                (box, box.Rigging!.Rig.Rest with { Weights = [0.5f, -0.75f] }),
                (twisting, BentTip(twistingRig.Rest, twistingRig, angle: -0.8f) with { Weights = [0.6f] }),
                (cylinder, BentTip(rig.Rest, rig, angle: -0.4f)),
            ];
            MeshId cylinderId = renderer.CreateMesh(cylinder),
                boxId = renderer.CreateMesh(box),
                twistingId = renderer.CreateMesh(twisting);
            MeshId[] ids = [cylinderId, boxId, twistingId, cylinderId];
            RenderSnapshot snapshot = Gpu.Snapshot(new(0.3f, 1f, 0.2f));
            for (int i = 0; i < copies.Length; i++)
            {
                snapshot.Instances.Add(Posed(ids[i], white, Stand + new Vector3(i - 1.5f, 0, 0)));
                AddStillPose(snapshot, instance: i, pose: copies[i].Pose);
            }

            renderer.Render(snapshot, 1f);
            GpuVertex[] shown = renderer.ReadDeformedVertices();

            renderer
                .Graph.Passes.OfType<DeformPass>()
                .Single()
                .Dispatches.Should()
                .Be(1, "one dispatch covers every job");
            renderer
                .Graph.Passes.OfType<DeformPass>()
                .Single()
                .Barriers.Should()
                .Be(2, "one batch before the dispatch and one after it, and none between its jobs");
            int read = 0;
            for (int i = 0; i < copies.Length; i++)
            {
                int count = copies[i].Mesh.Vertices.Length;
                ShouldMatch(shown.AsSpan(read, count), Expected(copies[i].Mesh, copies[i].Pose), $"copy {i}");
                read += count;
            }

            read.Should().Be(shown.Length, "the copies follow one another in job order");
        }
        finally
        {
            Gpu.DisposeAndAssertClean(renderer);
        }
    }

    [Fact]
    public void BentCylinderDrawsItsTipWhereThePoseMovedIt()
    {
        VulkanRenderer renderer = Gpu.Headless(width: Width, height: Height);
        try
        {
            MeshData cylinder = Cylinder();
            Rig rig = cylinder.Rigging!.Rig;
            MeshId mesh = renderer.CreateMesh(cylinder);
            MaterialId white = renderer.CreateMaterial(MaterialDesc.Color(Vector4.One, roughness: 0.5f));
            RenderSnapshot snapshot = Gpu.Snapshot(new(0.3f, 1f, 0.2f));
            snapshot.Instances.Add(Posed(mesh, white, Stand));
            // A quarter turn lays the upper half along −X, 1 m up.
            AddStillPose(snapshot, instance: 0, pose: BentTip(rig.Rest, rig, angle: MathF.PI / 2));

            renderer.Render(snapshot, 1f);
            float[] distance = renderer.ReadDistance();

            DistanceAt(distance, Stand + new Vector3(-0.7f, 1, 0))
                .Should()
                .BeGreaterThan(0, "the bent tip lies 0.7 m to the left of the base, 1 m up");
            DistanceAt(distance, Stand + new Vector3(0, 1.8f, 0))
                .Should()
                .Be(0, "the tip no longer stands where it rests: the sky shows there");
        }
        finally
        {
            Gpu.DisposeAndAssertClean(renderer);
        }
    }

    [Fact]
    public void BentCylinderShadowFollowsItsTip()
    {
        VulkanRenderer renderer = Gpu.Headless(width: Width, height: Height);
        try
        {
            MeshData cylinder = Cylinder();
            Rig rig = cylinder.Rigging!.Rig;
            MeshId mesh = renderer.CreateMesh(cylinder);
            MeshId plane = renderer.CreateMesh(Primitives.Plane(1f));
            MaterialId white = renderer.CreateMaterial(MaterialDesc.Color(Vector4.One, roughness: 0.5f));
            // The sun stands straight overhead, so a caster's shadow lies right below it.
            RenderSnapshot snapshot = Gpu.Snapshot(Vector3.UnitY);
            const float ground = -1.5f;
            snapshot.Instances.Add(Posed(mesh, white, Stand));
            snapshot.Instances.Add(
                Gpu.Instance(
                    mesh: plane,
                    material: white,
                    position: new(0, ground, -6),
                    scale: new(10, 1, 10),
                    flags: RenderFlags.CastsShadow,
                    chainDepth: TestChains.CamerasFrame
                )
            );
            AddStillPose(snapshot, instance: 0, pose: BentTip(rig.Rest, rig, angle: MathF.PI / 2));

            renderer.Render(snapshot, 1f);

            Vector3 underTip = Stand + new Vector3(-0.7f, 0, 0);
            Vector3 mirrored = Stand + new Vector3(0.7f, 0, 0);
            // Halfway between the ground and the bent tip's underside, 1 − 0.2 m above the base.
            float between = (ground + Stand.Y + 0.8f) / 2;
            (float tipDepth, float betweenDepth) = CascadeDepths(renderer, underTip, between);
            (float mirroredDepth, _) = CascadeDepths(renderer, mirrored, between);
            tipDepth
                .Should()
                .BeGreaterThan(betweenDepth, "the bent tip stands between the sun and the ground below it");
            mirroredDepth.Should().BeLessThan(betweenDepth, "nothing stands over the ground on the side the tip left");
        }
        finally
        {
            Gpu.DisposeAndAssertClean(renderer);
        }
    }

    /// <summary>
    /// The depth that the first cascade holding <paramref name="point"/> stores above it, and the depth that a point at
    /// height <paramref name="height"/> above it would have: reversed, so a larger depth is nearer the sun.
    /// </summary>
    private static (float Stored, float AtHeight) CascadeDepths(VulkanRenderer renderer, Vector3 point, float height)
    {
        PreparedScene scene = renderer.LastScene;
        for (int cascade = 0; cascade < ShadowCascades.Count; cascade++)
        {
            Matrix4x4 viewProjection = scene.CascadeSets[0][cascade].ViewProjection;
            var clip = Vector4.Transform(new Vector4(point, 1), viewProjection);
            if (MathF.Abs(clip.X) >= 0.99f || MathF.Abs(clip.Y) >= 0.99f)
                continue;
            int size = renderer.ShadowMap.Size;
            float[] depths = renderer.ReadShadowCascade(set: 0, cascade: cascade);
            int column = (int)((clip.X * 0.5f + 0.5f) * size);
            int row = (int)((clip.Y * 0.5f + 0.5f) * size);
            var atHeight = Vector4.Transform(new Vector4(point with { Y = height }, 1), viewProjection);
            return (depths[row * size + column], atHeight.Z);
        }

        throw new InvalidOperationException($"No cascade holds {point}.");
    }

    [Fact]
    public void RegionGrowsForMorePosedInstances()
    {
        VulkanRenderer renderer = Gpu.Headless(width: Width, height: Height);
        try
        {
            MeshData cylinder = Cylinder();
            Rig rig = cylinder.Rigging!.Rig;
            MeshId mesh = renderer.CreateMesh(cylinder);
            MaterialId white = renderer.CreateMaterial(MaterialDesc.Color(Vector4.One, roughness: 0.5f));
            RenderSnapshot one = Gpu.Snapshot(new(0.3f, 1f, 0.2f));
            one.Instances.Add(Posed(mesh, white, Stand));
            AddStillPose(one, instance: 0, pose: rig.Rest);
            renderer.Render(one, 1f);

            const int many = 64;
            RenderSnapshot Many(float bend)
            {
                RenderSnapshot snapshot = Gpu.Snapshot(new(0.3f, 1f, 0.2f));
                for (int i = 0; i < many; i++)
                {
                    snapshot.Instances.Add(Posed(mesh, white, Stand + new Vector3(i % 8 - 3.5f, 0, -i / 8)));
                    AddStillPose(snapshot, instance: i, pose: BentTip(rig.Rest, rig, angle: bend * i));
                }

                return snapshot;
            }

            // Then a frame in the other slot, which drew the one copy before the growth and must follow the grown region.
            renderer.Render(Many(bend: 0.01f), 1f);
            renderer.Render(Many(bend: 0.02f), 1f);

            renderer.LastFrameStats.DeformedInstances.Should().Be(many, "the region grew to hold every copy");
            renderer.LastFrameStats.UndeformedPosedInstances.Should().Be(0);
            int count = cylinder.Vertices.Length;
            ShouldMatch(
                renderer.ReadDeformedVertices().AsSpan((many - 1) * count, count),
                Expected(cylinder, BentTip(rig.Rest, rig, angle: 0.02f * (many - 1))),
                "the last copy"
            );
        }
        finally
        {
            Gpu.DisposeAndAssertClean(renderer);
        }
    }

    [Fact]
    public void MeshesUploadedLaterDeformFromTheirOwnSources()
    {
        VulkanRenderer renderer = Gpu.Headless(width: Width, height: Height);
        try
        {
            MaterialId white = renderer.CreateMaterial(MaterialDesc.Color(Vector4.One, roughness: 0.5f));
            MeshData box = Primitives.MorphingBox(new(0.5f));
            MeshId first = renderer.CreateMesh(box);
            RenderSnapshot snapshot = Gpu.Snapshot(new(0.3f, 1f, 0.2f));
            snapshot.Instances.Add(Posed(first, white, Stand));
            AddStillPose(snapshot, instance: 0, pose: box.Rigging!.Rig.Rest with { Weights = [1, 0] });
            renderer.Render(snapshot, 1f);
            // Twelve cylinders of 477 vertices pass the deform store's first 4,096 deform vertices, so it grows and
            // keeps the box's sources.
            MeshData twisting = Primitives.TwistingBendingCylinder(radius: Radius, length: Length);
            Rig rig = twisting.Rigging!.Rig;
            RigPose pose = BentTip(rig.Rest, rig, angle: 0.7f) with { Weights = [0.5f] };
            MeshId last = default;
            for (int i = 0; i < 12; i++)
                last = renderer.CreateMesh(twisting);
            snapshot.Instances.Add(Posed(last, white, Stand + Vector3.UnitX));
            AddStillPose(snapshot, instance: 1, pose: pose);

            // Once in each frame slot: the slot that drew the box before the growth must follow the grown store too.
            renderer.Render(snapshot, 1f);
            renderer.Render(snapshot, 1f);
            GpuVertex[] shown = renderer.ReadDeformedVertices();

            int boxVertices = box.Vertices.Length;
            ShouldMatch(
                shown.AsSpan(0, boxVertices),
                Expected(box, box.Rigging.Rig.Rest with { Weights = [1, 0] }),
                "the box"
            );
            ShouldMatch(shown.AsSpan(boxVertices), Expected(twisting, pose), "the last cylinder");
        }
        finally
        {
            Gpu.DisposeAndAssertClean(renderer);
        }
    }

    [Fact]
    public void StaticMeshesStillDrawAfterTheRegionGrows()
    {
        VulkanRenderer renderer = Gpu.Headless(width: Width, height: Height);
        try
        {
            MeshData cylinder = Cylinder();
            Rig rig = cylinder.Rigging!.Rig;
            MeshId mesh = renderer.CreateMesh(cylinder);
            MeshId box = renderer.CreateMesh(Primitives.Box(new(0.5f)));
            MaterialId white = renderer.CreateMaterial(MaterialDesc.Color(Vector4.One, roughness: 0.5f));
            var boxAt = new Vector3(1.5f, 0, -6);
            RenderSnapshot snapshot = Gpu.Snapshot(new(0.3f, 1f, 0.2f));
            snapshot.Instances.Add(
                Gpu.Instance(
                    mesh: box,
                    material: white,
                    position: boxAt,
                    scale: Vector3.One,
                    chainDepth: TestChains.CamerasFrame
                )
            );
            renderer.Render(snapshot, 1f);
            // Behind the camera, so that no copy covers the box; the batcher deforms them all the same.
            const int many = 64;
            for (int i = 0; i < many; i++)
            {
                snapshot.Instances.Add(Posed(mesh, white, new(i, 0, 10)));
                AddStillPose(snapshot, instance: i + 1, pose: rig.Rest);
            }

            renderer.Render(snapshot, 1f);

            renderer.LastFrameStats.DeformedInstances.Should().Be(many);
            DistanceAt(renderer.ReadDistance(), boxAt)
                .Should()
                .BeGreaterThan(0, "the grown vertex buffer still holds the box's vertices");
        }
        finally
        {
            Gpu.DisposeAndAssertClean(renderer);
        }
    }

    [Fact]
    public void FrameWithoutAPosedInstanceRecordsNothingInTheDeformPass()
    {
        VulkanRenderer renderer = Gpu.Headless(width: Width, height: Height);
        try
        {
            MeshId mesh = renderer.CreateMesh(Cylinder());
            MaterialId white = renderer.CreateMaterial(MaterialDesc.Color(Vector4.One, roughness: 0.5f));
            RenderSnapshot snapshot = Gpu.Snapshot(new(0.3f, 1f, 0.2f));
            snapshot.Instances.Add(Posed(mesh, white, Stand));

            renderer.Render(snapshot, 1f);

            DeformPass pass = renderer.Graph.Passes.OfType<DeformPass>().Single();
            pass.Dispatches.Should().Be(0);
            pass.Barriers.Should()
                .Be(0, "a scene without a posed instance keeps the cost it had before the deformation");
        }
        finally
        {
            Gpu.DisposeAndAssertClean(renderer);
        }
    }

    [Fact]
    public void DeformationOffDrawsTheStaticMesh()
    {
        VulkanRenderer renderer = Gpu.Headless(width: Width, height: Height, deformation: false);
        try
        {
            MeshData cylinder = Cylinder();
            Rig rig = cylinder.Rigging!.Rig;
            MeshId mesh = renderer.CreateMesh(cylinder);
            MaterialId white = renderer.CreateMaterial(MaterialDesc.Color(Vector4.One, roughness: 0.5f));
            RenderSnapshot snapshot = Gpu.Snapshot(new(0.3f, 1f, 0.2f));
            snapshot.Instances.Add(Posed(mesh, white, Stand));
            AddStillPose(snapshot, instance: 0, pose: BentTip(rig.Rest, rig, angle: MathF.PI / 2));

            renderer.Render(snapshot, 1f);
            float[] distance = renderer.ReadDistance();

            renderer.LastFrameStats.DeformedInstances.Should().Be(0);
            renderer.LastFrameStats.UndeformedPosedInstances.Should().Be(1, "the posed instance draws as uploaded");
            DistanceAt(distance, Stand + new Vector3(0, 1.8f, 0))
                .Should()
                .BeGreaterThan(0, "the tip stands where it rests");
            DistanceAt(distance, Stand + new Vector3(-0.7f, 1, 0)).Should().Be(0, "nothing bends to the left");
        }
        finally
        {
            Gpu.DisposeAndAssertClean(renderer);
        }
    }
}
