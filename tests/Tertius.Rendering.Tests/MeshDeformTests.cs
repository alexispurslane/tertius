using System.Numerics;
using System.Runtime.InteropServices;

namespace Tertius.Rendering.Tests;

/// <summary>
/// The deformation in C#: the packing of a deforming mesh, the pose rows and the posed bound of a rig pose, and the copy
/// of deform.comp for one vertex (docs/plans/skinning-and-morphs.md, "The math, and its C# copy"). No GPU needed. The
/// cylinders are 2 m long, with Base at the origin and Tip 1 m up.
/// </summary>
public sealed class MeshDeformTests
{
    private const float Length = 2;
    private const float Radius = 0.2f;
    private const int Segments = 24;
    private const int Rings = 16;
    private const int Tip = 1;

    private static readonly Quaternion QuarterTurnAboutZ = Quaternion.CreateFromAxisAngle(Vector3.UnitZ, MathF.PI / 2);

    private static MeshData Cylinder() => Primitives.BendingCylinder(radius: Radius, length: Length);

    private static MeshData TwistingCylinder() => Primitives.TwistingBendingCylinder(radius: Radius, length: Length);

    private static PackedDeform Packed(MeshData mesh) => MeshDeform.Pack(mesh)!;

    /// <summary>The pose rows of <paramref name="previous"/> and <paramref name="current"/> at <paramref name="alpha"/>, and the posed bound.</summary>
    private static (Vector4[] Rows, float Bound) Rows(MeshDeform deform, RigPose previous, RigPose current, float alpha)
    {
        var rows = new Vector4[deform.PoseRows];
        float bound = deform.PoseInto(
            previousJoints: previous.Joints.AsSpan(),
            joints: current.Joints.AsSpan(),
            previousWeights: previous.Weights.AsSpan(),
            weights: current.Weights.AsSpan(),
            alpha: alpha,
            rows: rows
        );
        return (rows, bound);
    }

    /// <summary>Every vertex of <paramref name="mesh"/> deformed by the rig pose between two ends, and the posed bound.</summary>
    private static (Vertex[] Vertices, float Bound) Deform(
        MeshData mesh,
        RigPose previous,
        RigPose current,
        float alpha
    )
    {
        PackedDeform packed = Packed(mesh);
        (Vector4[] rows, float bound) = Rows(deform: packed.Deform, previous: previous, current: current, alpha: alpha);
        Vertex[] vertices =
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
        return (vertices, bound);
    }

    private static (Vertex[] Vertices, float Bound) Deform(MeshData mesh, RigPose pose) =>
        Deform(mesh: mesh, previous: pose, current: pose, alpha: 1);

    private static RigPose WithJoint(RigPose pose, int joint, Pose value) =>
        pose with
        {
            Joints = pose.Joints.SetItem(joint, value),
        };

    private static RigPose WithWeights(RigPose pose, params float[] weights) => pose with { Weights = [.. weights] };

    /// <summary>The cylinder's rest pose with Tip turned a quarter about +Z.</summary>
    private static RigPose BentTip(MeshData mesh) =>
        WithJoint(mesh.Rigging!.Rig.Rest, Tip, new(new(0, Length / 2, 0), QuarterTurnAboutZ));

    /// <summary>The tip's skin pose for a tip joint pose turned by <paramref name="turn"/>: it turns a point about (0, 1, 0).</summary>
    private static Vector3 AboutTip(Vector3 point, Quaternion turn)
    {
        var joint = new Vector3(0, Length / 2, 0);
        return joint + Vector3.Transform(point - joint, turn);
    }

    /// <summary>The point that the skin pose of <paramref name="joint"/> in <paramref name="rows"/> makes of <paramref name="point"/>.</summary>
    private static Vector3 Apply(Vector4[] rows, int joint, Vector3 point)
    {
        var p = new Vector4(point, 1);
        return new(
            Vector4.Dot(rows[3 * joint], p),
            Vector4.Dot(rows[3 * joint + 1], p),
            Vector4.Dot(rows[3 * joint + 2], p)
        );
    }

    private static void ShouldBeClose(Vector3 actual, Vector3 expected, string because = "")
    {
        actual.X.Should().BeApproximately(expected.X, 1e-5f * (1 + MathF.Abs(expected.X)), because);
        actual.Y.Should().BeApproximately(expected.Y, 1e-5f * (1 + MathF.Abs(expected.Y)), because);
        actual.Z.Should().BeApproximately(expected.Z, 1e-5f * (1 + MathF.Abs(expected.Z)), because);
    }

    private static Vector3 Xyz(Vector4 v) => new(v.X, v.Y, v.Z);

    [Fact]
    public void PackGivesOneDeformVertexPerVertex()
    {
        MeshData mesh = Cylinder();

        PackedDeform packed = Packed(mesh);

        packed.Vertices.Should().HaveCount(mesh.Vertices.Length);
        packed.Deform.VertexCount.Should().Be(mesh.Vertices.Length);
        for (int index = 0; index < mesh.Vertices.Length; index++)
        {
            packed.Vertices[index].Position.Should().Be(mesh.Vertices[index].Position);
            packed.Vertices[index].TexCoord.Should().Be(mesh.Vertices[index].TexCoord);
        }
    }

    [Fact]
    public void PackListsEachVertexsDeltasInMorphOrder()
    {
        MeshData box = Primitives.MorphingBox(new(1, 0.5f, 0.25f));
        MeshRigging rigging = box.Rigging!;
        // Lean before Stretch: the order of the targets must not matter.
        MeshData reversed = box with
        {
            Rigging = rigging with { Morphs = [rigging.Morphs[1], rigging.Morphs[0]] },
        };

        PackedDeform packed = Packed(reversed);

        int expectedStart = 0;
        for (int index = 0; index < packed.Vertices.Length; index++)
        {
            GpuDeformVertex vertex = packed.Vertices[index];
            vertex.MorphStart.Should().Be((uint)expectedStart, "each vertex's deltas follow the one before");
            GpuMorphDelta[] deltas = packed.Deltas.AsSpan(expectedStart, (int)vertex.MorphCount).ToArray();
            deltas.Select(delta => delta.Morph).Should().BeInAscendingOrder();
            foreach (GpuMorphDelta delta in deltas)
                delta.Position.Should().Be(rigging.Morphs[(int)delta.Morph].Positions[index]);
            expectedStart += (int)vertex.MorphCount;
        }

        expectedStart.Should().Be(packed.Deltas.Length);
        packed.Deltas.Select(delta => delta.Morph).Should().Contain([0u, 1u]);
    }

    [Fact]
    public void PackLeavesOutDeltasThatAreExactlyZero()
    {
        MeshData box = Primitives.MorphingBox(new(1, 0.5f, 0.25f));
        MeshRigging rigging = box.Rigging!;

        PackedDeform packed = Packed(box);

        for (int index = 0; index < box.Vertices.Length; index++)
        {
            int moving = rigging.Morphs.Count(target =>
                At(target.Positions, index) != Vector3.Zero
                || At(target.Normals, index) != Vector3.Zero
                || At(target.Tangents, index) != Vector3.Zero
            );
            packed.Vertices[index].MorphCount.Should().Be((uint)moving);
        }

        packed.Vertices.Should().Contain(vertex => vertex.MorphCount == 0, "the bottom face moves in no morph");
        return;

        static Vector3 At(Vector3[] deltas, int index) => deltas.Length == 0 ? Vector3.Zero : deltas[index];
    }

    [Fact]
    public void PackScalesTheWeightsOfAVertexToSumToOne()
    {
        MeshData mesh = Cylinder();
        MeshSkin skin = mesh.Rigging!.Skin!;
        MeshSkin heavy = skin with
        {
            Vertices = [.. skin.Vertices.Select(each => each with { Weights = each.Weights * 4 })],
        };

        PackedDeform packed = Packed(mesh with { Rigging = mesh.Rigging with { Skin = heavy } });

        for (int index = 0; index < packed.Vertices.Length; index++)
        {
            Vector4 weights = packed.Vertices[index].Weights;
            (weights.X + weights.Y + weights.Z + weights.W).Should().BeApproximately(1, 1e-6f);
            weights.X.Should().BeApproximately(skin.Vertices[index].Weights.X, 1e-6f);
            weights.Y.Should().BeApproximately(skin.Vertices[index].Weights.Y, 1e-6f);
        }
    }

    [Fact]
    public void PackGivesAMeshWithoutASkinOneIdentityJoint()
    {
        MeshData box = Primitives.MorphingBox(new(1, 0.5f, 0.25f));

        PackedDeform packed = Packed(box);
        (Vector4[] rows, _) = Rows(
            deform: packed.Deform,
            previous: box.Rigging!.Rig.Rest,
            current: box.Rigging.Rig.Rest,
            alpha: 1
        );

        packed.Deform.PaletteJoints.Should().Be(1);
        packed
            .Vertices.Should()
            .AllSatisfy(vertex =>
            {
                vertex.Joints01.Should().Be(0u);
                vertex.Joints23.Should().Be(0u);
                vertex.Weights.Should().Be(new Vector4(x: 1, y: 0, z: 0, w: 0));
            });
        rows[..3]
            .Should()
            .Equal(new Vector4(x: 1, y: 0, z: 0, w: 0), new(x: 0, y: 1, z: 0, w: 0), new(x: 0, y: 0, z: 1, w: 0));
    }

    [Fact]
    public void PackGivesNothingForAMeshWithoutRigging() =>
        MeshDeform.Pack(Primitives.Box(Vector3.One)).Should().BeNull();

    [Fact]
    public void RestPoseLeavesEveryVertexInPlace()
    {
        MeshData mesh = TwistingCylinder();

        (Vertex[] deformed, _) = Deform(mesh, mesh.Rigging!.Rig.Rest);

        for (int index = 0; index < mesh.Vertices.Length; index++)
        {
            ShouldBeClose(deformed[index].Position, mesh.Vertices[index].Position);
            ShouldBeClose(deformed[index].Normal, mesh.Vertices[index].Normal);
            ShouldBeClose(Xyz(deformed[index].Tangent), Xyz(mesh.Vertices[index].Tangent));
        }
    }

    [Fact]
    public void BentTipTurnsTheTipRingsAboutTheTipJoint()
    {
        MeshData mesh = Cylinder();

        (Vertex[] deformed, _) = Deform(mesh, BentTip(mesh));

        int checkedVertices = 0;
        for (int index = 0; index < mesh.Vertices.Length; index++)
        {
            Vertex vertex = mesh.Vertices[index];
            // Above 1.25 m the skin puts the whole weight on Tip.
            if (vertex.Position.Y < 1.25f)
                continue;
            ShouldBeClose(deformed[index].Position, AboutTip(vertex.Position, QuarterTurnAboutZ));
            ShouldBeClose(deformed[index].Normal, Vector3.Transform(vertex.Normal, QuarterTurnAboutZ));
            checkedVertices++;
        }

        checkedVertices.Should().Be(7 * (Segments + 1) + Segments + 2, "the rings at 1.25 m to 2 m and the top cap");
    }

    [Fact]
    public void RingBetweenTheJointsBlendsBothJoints()
    {
        MeshData mesh = Cylinder();

        (Vertex[] deformed, _) = Deform(mesh, BentTip(mesh));

        int middle = Rings / 2 * (Segments + 1);
        for (int index = middle; index < middle + Segments + 1; index++)
        {
            Vector3 position = mesh.Vertices[index].Position;
            Vector3 expected = 0.5f * position + 0.5f * AboutTip(position, QuarterTurnAboutZ);
            ShouldBeClose(deformed[index].Position, expected, "the middle ring has half its weight on each joint");
        }
    }

    [Fact]
    public void MorphAtWeightOneAddsItsWholeDelta()
    {
        var halfExtents = new Vector3(1, 0.5f, 0.25f);
        MeshData box = Primitives.MorphingBox(halfExtents);

        (Vertex[] deformed, _) = Deform(box, WithWeights(box.Rigging!.Rig.Rest, 1, 0));

        for (int index = 0; index < box.Vertices.Length; index++)
        {
            Vector3 position = box.Vertices[index].Position;
            Vector3 lift = position.Y > 0 ? new(0, 2 * halfExtents.Y, 0) : Vector3.Zero;
            ShouldBeClose(deformed[index].Position, position + lift);
        }
    }

    [Fact]
    public void MorphAtWeightHalfAddsHalfItsDelta()
    {
        var halfExtents = new Vector3(1, 0.5f, 0.25f);
        MeshData box = Primitives.MorphingBox(halfExtents);

        (Vertex[] deformed, _) = Deform(box, WithWeights(box.Rigging!.Rig.Rest, 0, 0.5f));

        for (int index = 0; index < box.Vertices.Length; index++)
        {
            Vector3 position = box.Vertices[index].Position;
            Vector3 lean = position.Y > 0 ? new(halfExtents.X / 2, 0, 0) : Vector3.Zero;
            ShouldBeClose(deformed[index].Position, position + lean);
        }
    }

    [Fact]
    public void MorphAddsItsTangentDeltaAndKeepsTheSign()
    {
        MeshData twisting = TwistingCylinder();
        MeshData mesh = twisting with
        {
            Vertices =
            [
                .. twisting.Vertices.Select(vertex => vertex with { Tangent = vertex.Tangent with { W = -1 } }),
            ],
        };

        (Vertex[] deformed, _) = Deform(mesh, WithWeights(mesh.Rigging!.Rig.Rest, 1));

        for (int index = 0; index < mesh.Vertices.Length; index++)
        {
            Vertex vertex = mesh.Vertices[index];
            var turn = Quaternion.CreateFromAxisAngle(Vector3.UnitY, MathF.PI / 2 * vertex.Position.Y / Length);
            ShouldBeClose(Xyz(deformed[index].Tangent), Vector3.Transform(Xyz(vertex.Tangent), turn));
            deformed[index].Tangent.W.Should().Be(-1, "a morph moves the tangent and never its sign");
        }
    }

    [Fact]
    public void MorphsApplyBeforeTheSkin()
    {
        MeshData mesh = TwistingCylinder();
        RigPose pose = WithWeights(BentTip(mesh), 1);
        int top = Rings * (Segments + 1);
        Vector3 position = mesh.Vertices[top].Position;
        Vector3 delta = mesh.Rigging!.Morphs[0].Positions[top];

        (Vertex[] deformed, _) = Deform(mesh, pose);

        ShouldBeClose(
            deformed[top].Position,
            AboutTip(position + delta, QuarterTurnAboutZ),
            "the skin turns the delta"
        );
        Vector3 skinFirst = AboutTip(position, QuarterTurnAboutZ) + delta;
        Vector3.Distance(deformed[top].Position, skinFirst).Should().BeGreaterThan(0.1f);
    }

    [Fact]
    public void DeformedNormalsAndTangentsAreUnitOrZero()
    {
        MeshData mesh = TwistingCylinder();
        var random = new Random(41);
        // A box whose one morph cancels every normal and tangent.
        MeshData box = Primitives.Box(Vector3.One);
        var flatten = new MorphTarget(
            Morph: 0,
            Positions: [],
            Normals: [.. box.Vertices.Select(vertex => -vertex.Normal)],
            Tangents: [.. box.Vertices.Select(vertex => -Xyz(vertex.Tangent))]
        );
        MeshData flat = box with { Rigging = new(new([], [new(Name: "Flatten", Rest: 1)]), Skin: null, [flatten]) };

        (Vertex[] cancelled, _) = Deform(flat, flat.Rigging!.Rig.Rest);

        cancelled
            .Should()
            .AllSatisfy(vertex =>
            {
                vertex.Normal.Should().Be(Vector3.Zero);
                Xyz(vertex.Tangent).Should().Be(Vector3.Zero);
            });
        for (int trial = 0; trial < 8; trial++)
        {
            RigPose pose = WithWeights(
                WithJoint(mesh.Rigging!.Rig.Rest, Tip, new(new(0, 1, 0), RandomRotation(random))),
                (float)(random.NextDouble() * 4 - 2)
            );
            (Vertex[] deformed, _) = Deform(mesh, pose);
            deformed
                .Should()
                .AllSatisfy(vertex =>
                {
                    UnitOrZero(vertex.Normal).Should().BeTrue();
                    UnitOrZero(Xyz(vertex.Tangent)).Should().BeTrue();
                });
        }

        return;

        static bool UnitOrZero(Vector3 v) => v == Vector3.Zero || MathF.Abs(v.Length() - 1) < 1e-5f;
    }

    [Fact]
    public void PoseInterpolatesJointRotationsBySlerp()
    {
        MeshData mesh = Cylinder();
        PackedDeform packed = Packed(mesh);

        (Vector4[] rows, _) = Rows(
            deform: packed.Deform,
            previous: mesh.Rigging!.Rig.Rest,
            current: BentTip(mesh),
            alpha: 0.5f
        );

        var eighthTurn = Quaternion.CreateFromAxisAngle(Vector3.UnitZ, MathF.PI / 4);
        foreach (Vector3 point in (Vector3[])[new(0.2f, 1.5f, 0), new(0, 2, 0.2f), new(-0.2f, 1, 0)])
            ShouldBeClose(Apply(rows, Tip, point), AboutTip(point, eighthTurn), "halfway turns half the angle");
    }

    [Fact]
    public void PoseLerpsJointPositionsAndWeights()
    {
        MeshData mesh = TwistingCylinder();
        PackedDeform packed = Packed(mesh);
        RigPose previous = mesh.Rigging!.Rig.Rest;
        RigPose current = WithWeights(WithJoint(previous, Tip, new(new(0, 2, 0), Quaternion.Identity)), 1);

        (Vector4[] rows, _) = Rows(deform: packed.Deform, previous: previous, current: current, alpha: 0.25f);

        ShouldBeClose(Apply(rows, Tip, new(0, 1, 0)), new(0, 1.25f, 0), "the bind's tip point follows the joint");
        rows[3 * packed.Deform.PaletteJoints].Should().Be(new Vector4(x: 0.25f, y: 0, z: 0, w: 0));
    }

    [Fact]
    public void PoseTakesTheCurrentValueWhereBothEndsAreEqual()
    {
        MeshData mesh = TwistingCylinder();
        PackedDeform packed = Packed(mesh);
        RigPose rest = mesh.Rigging!.Rig.Rest;
        var random = new Random(11);

        for (int trial = 0; trial < 64; trial++)
        {
            // Rotations off unit length and positions far from the origin, where a lerp or a slerp rounds.
            RigPose pose = new(
                [
                    .. rest.Joints.Select(_ => new Pose(
                        RandomPosition(random) * 1000,
                        RandomRotation(random) * (float)(0.5 + random.NextDouble())
                    )),
                ],
                [.. rest.Weights.Select(_ => (float)random.NextDouble() * 4 - 2)]
            );
            float alpha = (float)random.NextDouble();

            (Vector4[] between, _) = Rows(deform: packed.Deform, previous: pose, current: pose, alpha: alpha);
            (Vector4[] end, _) = Rows(deform: packed.Deform, previous: pose, current: pose, alpha: 1);

            MemoryMarshal
                .AsBytes(between.AsSpan())
                .SequenceEqual(MemoryMarshal.AsBytes(end.AsSpan()))
                .Should()
                .BeTrue($"trial {trial} at alpha {alpha}");
        }
    }

    [Fact]
    public void AWeightHeldAtInfinityReadsAsTheLargestMorphWeight()
    {
        MeshData box = Primitives.MorphingBox(new(1, 0.5f, 0.25f));
        PackedDeform packed = Packed(box);
        RigPose pose = WithWeights(box.Rigging!.Rig.Rest, float.PositiveInfinity, 0);

        (Vector4[] rows, _) = Rows(deform: packed.Deform, previous: pose, current: pose, alpha: 0.5f);

        rows[3].X.Should().Be(ShaderInput.LargestMorphWeight, "both ends are equal, so the weight is the current one");
    }

    [Fact]
    public void PosedBoundHoldsEveryDeformedVertex()
    {
        MeshData mesh = TwistingCylinder();
        RigPose rest = mesh.Rigging!.Rig.Rest;
        var random = new Random(7);

        for (int trial = 0; trial < 64; trial++)
        {
            RigPose previous = RandomPose(rest, random);
            RigPose current = RandomPose(rest, random);
            float alpha = (float)random.NextDouble();

            (Vertex[] deformed, float bound) = Deform(mesh: mesh, previous: previous, current: current, alpha: alpha);

            float farthest = deformed.Max(vertex => vertex.Position.Length());
            farthest.Should().BeLessThanOrEqualTo(bound * (1 + 1e-5f), $"trial {trial}");
        }
    }

    [Fact]
    public void PosedBoundGrowsByEachWeightTimesItsLongestDelta()
    {
        MeshData twisting = TwistingCylinder();
        MorphTarget twist = twisting.Rigging!.Morphs[0];
        // The vertices and deltas in reverse, so that the top ring's longest delta comes first.
        MeshData mesh = twisting with
        {
            Vertices = [.. twisting.Vertices.Reverse()],
            Rigging = twisting.Rigging with
            {
                Skin = twisting.Rigging.Skin! with { Vertices = [.. twisting.Rigging.Skin.Vertices.Reverse()] },
                Morphs =
                [
                    twist with
                    {
                        Positions = [.. twist.Positions.Reverse()],
                        Normals = [.. twist.Normals.Reverse()],
                        Tangents = [.. twist.Tangents.Reverse()],
                    },
                ],
            },
        };
        PackedDeform packed = Packed(mesh);
        RigPose rest = mesh.Rigging.Rig.Rest;
        float longest = twist.Positions.Max(delta => delta.Length());

        (_, float still) = Rows(deform: packed.Deform, previous: rest, current: rest, alpha: 1);
        (_, float forward) = Rows(deform: packed.Deform, previous: rest, current: WithWeights(rest, 0.7f), alpha: 1);
        (_, float backward) = Rows(deform: packed.Deform, previous: rest, current: WithWeights(rest, -1.5f), alpha: 1);

        (forward - still).Should().BeApproximately(0.7f * longest, 1e-5f);
        (backward - still).Should().BeApproximately(1.5f * longest, 1e-5f, "a negative weight moves as far");
    }

    [Fact]
    public void PosedBoundOfAMeshWithoutASkinIsItsRadiusPlusTheMorphs()
    {
        var halfExtents = new Vector3(1, 0.5f, 0.25f);
        MeshData box = Primitives.MorphingBox(halfExtents);
        PackedDeform packed = Packed(box);
        RigPose rest = box.Rigging!.Rig.Rest;

        (_, float still) = Rows(deform: packed.Deform, previous: rest, current: rest, alpha: 1);
        (_, float morphed) = Rows(
            deform: packed.Deform,
            previous: rest,
            current: WithWeights(rest, 0.5f, -1),
            alpha: 1
        );

        still.Should().BeApproximately(halfExtents.Length(), 1e-6f);
        // Stretch moves the top by 2 h.y and Lean by h.x.
        morphed.Should().BeApproximately(halfExtents.Length() + 0.5f * 2 * halfExtents.Y + halfExtents.X, 1e-5f);
    }

    [Theory]
    [InlineData(float.NaN)]
    [InlineData(float.PositiveInfinity)]
    public void PosedBoundIsNotFiniteForAJointPositionThatIsNot(float x)
    {
        MeshData mesh = Cylinder();
        PackedDeform packed = Packed(mesh);
        RigPose pose = WithJoint(mesh.Rigging!.Rig.Rest, Tip, new(new(x, 1, 0), Quaternion.Identity));

        (_, float bound) = Rows(deform: packed.Deform, previous: pose, current: pose, alpha: 1);

        float.IsFinite(bound).Should().BeFalse();
    }

    [Fact]
    public void PosedBoundIsNotFiniteForAJointThatMovesNoVertex()
    {
        // Every vertex on Base, with Tip in slot 1 at weight 0: the shader still multiplies Tip's rows by 0.
        MeshData cylinder = Cylinder();
        MeshSkin skin = cylinder.Rigging!.Skin!;
        MeshSkin onBase = skin with
        {
            Vertices = [.. skin.Vertices.Select(each => each with { Weights = new(x: 1, y: 0, z: 0, w: 0) })],
        };
        MeshData mesh = cylinder with { Rigging = cylinder.Rigging with { Skin = onBase } };
        PackedDeform packed = Packed(mesh);
        RigPose pose = WithJoint(mesh.Rigging!.Rig.Rest, Tip, new(new(float.NaN, 1, 0), Quaternion.Identity));

        (_, float bound) = Rows(deform: packed.Deform, previous: pose, current: pose, alpha: 1);

        float.IsFinite(bound).Should().BeFalse("0 times a NaN is a NaN");
    }

    [Theory]
    [InlineData(2)]
    [InlineData(3)]
    public void PosedBoundReachesAVertexThatTheThirdOrTheFourthJointMoves(int joint)
    {
        // Four root joints at the origin, a box on Joint 0 except its first vertex, which has a quarter on each joint.
        var rig = new Rig(
            [.. Enumerable.Range(0, 4).Select(each => new RigJoint($"J{each}", null, Pose.Identity))],
            []
        );
        MeshData box = Primitives.Box(Vector3.One);
        VertexSkin[] vertices =
        [
            .. box.Vertices.Select(
                (_, index) =>
                    index == 0
                        ? new VertexSkin(Joint0: 0, Joint1: 1, Joint2: 2, Joint3: 3, Weights: new(0.25f))
                        : new VertexSkin(
                            Joint0: 0,
                            Joint1: 0,
                            Joint2: 0,
                            Joint3: 0,
                            Weights: new(x: 1, y: 0, z: 0, w: 0)
                        )
            ),
        ];
        var skin = new MeshSkin(vertices, [.. Enumerable.Repeat(Pose.Identity, 4)]);
        MeshData mesh = box with { Rigging = new(rig, skin, []) };
        PackedDeform packed = Packed(mesh);
        RigPose moved = WithJoint(rig.Rest, joint, new(new(50, 0, 0), Quaternion.Identity));

        (_, float still) = Rows(deform: packed.Deform, previous: rig.Rest, current: rig.Rest, alpha: 1);
        (_, float bound) = Rows(deform: packed.Deform, previous: moved, current: moved, alpha: 1);

        still.Should().BeLessThan(2, "the box is 1 m wide, and nothing moves");
        bound
            .Should()
            .BeGreaterThan(49, "the joint's sphere holds the vertex, so the bound follows the joint out to 50 m");
    }

    [Fact]
    public void PoseReadsEachWeightAsTheShadersReadIt()
    {
        MeshData box = Primitives.MorphingBox(new(1, 0.5f, 0.25f));
        PackedDeform packed = Packed(box);
        RigPose pose = WithWeights(box.Rigging!.Rig.Rest, float.NaN, 2000);

        (Vector4[] rows, float bound) = Rows(deform: packed.Deform, previous: pose, current: pose, alpha: 1);

        rows[3].Should().Be(new Vector4(x: 0, y: ShaderInput.LargestMorphWeight, z: 0, w: 0));
        float.IsFinite(bound).Should().BeTrue();
    }

    [Fact]
    public void TurnedBaseCarriesTheWholeCylinder()
    {
        MeshData mesh = Cylinder();
        var basePose = new Pose(new(1, 2, 3), Quaternion.CreateFromAxisAngle(Vector3.UnitX, 0.7f));

        (Vertex[] deformed, _) = Deform(mesh, WithJoint(mesh.Rigging!.Rig.Rest, 0, basePose));

        for (int index = 0; index < mesh.Vertices.Length; index++)
            ShouldBeClose(
                deformed[index].Position,
                basePose.TransformPoint(mesh.Vertices[index].Position),
                "Tip follows its parent"
            );
    }

    [Fact]
    public void ARigOfManyJointsPosesItsLastJointThroughEveryParent()
    {
        // A chain of 300 joints, each 1 cm above its parent, with a box skinned to the last.
        const int count = 300;
        var step = new Pose(new(0, 0.01f, 0), Quaternion.Identity);
        var rig = new Rig(
            [
                .. Enumerable
                    .Range(0, count)
                    .Select(joint => new RigJoint($"J{joint}", joint == 0 ? null : joint - 1, step)),
            ],
            []
        );
        MeshData box = Primitives.Box(Vector3.One);
        var skin = new MeshSkin(
            [
                .. box.Vertices.Select(_ => new VertexSkin(
                    Joint0: count - 1,
                    Joint1: 0,
                    Joint2: 0,
                    Joint3: 0,
                    Weights: new(x: 1, y: 0, z: 0, w: 0)
                )),
            ],
            [.. Enumerable.Range(1, count).Select(depth => new Pose(new(0, -0.01f * depth, 0), Quaternion.Identity))]
        );
        MeshData mesh = box with { Rigging = new(rig, skin, []) };

        (Vertex[] deformed, _) = Deform(mesh, WithJoint(rig.Rest, 0, step with { Rotation = QuarterTurnAboutZ }));

        for (int index = 0; index < box.Vertices.Length; index++)
            ShouldBeClose(
                deformed[index].Position,
                new Vector3(0, 0.01f, 0)
                    + Vector3.Transform(box.Vertices[index].Position - new Vector3(0, 0.01f, 0), QuarterTurnAboutZ),
                "the turn of the root carries the whole chain"
            );
    }

    [Fact]
    public void FitsNeedsTheRigsJointAndMorphCounts()
    {
        MeshDeform deform = Packed(TwistingCylinder()).Deform;

        deform.Fits(joints: 2, weights: 1).Should().BeTrue();
        deform.Fits(joints: 1, weights: 1).Should().BeFalse();
        deform.Fits(joints: 3, weights: 1).Should().BeFalse();
        deform.Fits(joints: 2, weights: 0).Should().BeFalse();
        deform.Fits(joints: 2, weights: 2).Should().BeFalse();
    }

    [Fact]
    public void PoseWritesNoRowAfterItsPoseRows()
    {
        // The rest of a shared pose buffer: the next job's rows follow this job's.
        MeshData mesh = TwistingCylinder();
        MeshDeform deform = Packed(mesh).Deform;
        RigPose rest = mesh.Rigging!.Rig.Rest;
        var next = new Vector4(x: 7, y: 7, z: 7, w: 7);
        var buffer = new Vector4[deform.PoseRows + 4];
        buffer.AsSpan(deform.PoseRows).Fill(next);

        deform.PoseInto(
            previousJoints: rest.Joints.AsSpan(),
            joints: rest.Joints.AsSpan(),
            previousWeights: rest.Weights.AsSpan(),
            weights: rest.Weights.AsSpan(),
            alpha: 1,
            rows: buffer
        );

        buffer[deform.PoseRows..].Should().AllBeEquivalentTo(next, "those rows belong to the next job");
    }

    private static Quaternion RandomRotation(Random random) =>
        Quaternion.Normalize(
            new(
                x: (float)random.NextDouble() * 2 - 1,
                y: (float)random.NextDouble() * 2 - 1,
                z: (float)random.NextDouble() * 2 - 1,
                w: (float)random.NextDouble() * 2 - 1
            )
        );

    private static Vector3 RandomPosition(Random random) =>
        new((float)random.NextDouble() * 2 - 1, (float)random.NextDouble() * 2 - 1, (float)random.NextDouble() * 2 - 1);

    private static RigPose RandomPose(RigPose rest, Random random) =>
        new(
            [.. rest.Joints.Select(_ => new Pose(RandomPosition(random), RandomRotation(random)))],
            [.. rest.Weights.Select(_ => (float)random.NextDouble() * 4 - 2)]
        );
}
