using System.Numerics;
using AwesomeAssertions;

namespace Tertius.Core.Tests;

/// <summary>
/// The test meshes with rigging (docs/plans/skinning-and-morphs.md, "Part 1: the seam"): the bending cylinder's skin, the
/// morphing box's two morphs, and the twist of the twisting, bending cylinder.
/// </summary>
public sealed class PrimitivesRigTests
{
    private const float Radius = 0.25f;
    private const float Length = 2;

    // Each half extent differs from the others, so a swap of two axes shows.
    private static readonly Vector3 HalfExtents = new(0.5f, 1, 0.75f);

    public static TheoryData<string> Meshes =>
        [
            nameof(Primitives.BendingCylinder),
            nameof(Primitives.MorphingBox),
            nameof(Primitives.TwistingBendingCylinder),
        ];

    private static MeshData Named(string name) =>
        name switch
        {
            nameof(Primitives.BendingCylinder) => Primitives.BendingCylinder(radius: Radius, length: Length),
            nameof(Primitives.MorphingBox) => Primitives.MorphingBox(HalfExtents),
            nameof(Primitives.TwistingBendingCylinder) => Primitives.TwistingBendingCylinder(
                radius: Radius,
                length: Length
            ),
            _ => throw new ArgumentException($"No test mesh is named {name}.", nameof(name)),
        };

    /// <summary>The sum of a vertex's weights on <paramref name="joint"/>, over its four slots.</summary>
    private static float WeightOn(VertexSkin skin, int joint)
    {
        float weight = 0;
        if (skin.Joint0 == joint)
            weight += skin.Weights.X;
        if (skin.Joint1 == joint)
            weight += skin.Weights.Y;
        if (skin.Joint2 == joint)
            weight += skin.Weights.Z;
        if (skin.Joint3 == joint)
            weight += skin.Weights.W;
        return weight;
    }

    /// <summary>The skins of the vertices at <paramref name="height"/>, with each one's weights on the base and the tip.</summary>
    private static (float onBase, float onTip)[] WeightsOfTheRingAt(float height)
    {
        MeshData cylinder = Primitives.BendingCylinder(radius: Radius, length: Length);
        MeshRigging rigging = cylinder.Rigging!;
        int baseJoint = rigging.Rig.JointNamed("Base")!.Value;
        int tipJoint = rigging.Rig.JointNamed("Tip")!.Value;
        return
        [
            .. cylinder
                .Vertices.Index()
                .Where(each => each.Item.Position.Y == height)
                .Select(each => rigging.Skin!.Vertices[each.Index])
                .Select(skin => (onBase: WeightOn(skin, baseJoint), onTip: WeightOn(skin, tipJoint))),
        ];
    }

    [Theory]
    [MemberData(nameof(Meshes))]
    public void RiggingHasOneEntryPerVertex(string mesh)
    {
        MeshData data = Named(mesh);
        MeshRigging rigging = data.Rigging!;
        int[] skinLengths = rigging.Skin is { } skin ? [skin.Vertices.Length] : [];
        int[] deltaLengths =
        [
            .. rigging
                .Morphs.SelectMany(target =>
                    new[] { target.Positions.Length, target.Normals.Length, target.Tangents.Length }
                )
                .Where(length => length > 0),
        ];

        skinLengths.Concat(deltaLengths).Should().NotBeEmpty().And.AllBeEquivalentTo(data.Vertices.Length);
    }

    [Fact]
    public void BendingCylinderWeightsSumToOneAtEveryVertex()
    {
        MeshData cylinder = Primitives.BendingCylinder(radius: Radius, length: Length);

        cylinder
            .Rigging!.Skin!.Vertices.Select(skin => skin.Weights.X + skin.Weights.Y + skin.Weights.Z + skin.Weights.W)
            .Should()
            .AllSatisfy(sum => sum.Should().BeApproximately(1, 1e-6f));
    }

    [Fact]
    public void BendingCylinderBindsItsLowestRingToTheBaseOnly() =>
        WeightsOfTheRingAt(0).Should().HaveCount(25).And.AllBeEquivalentTo((onBase: 1f, onTip: 0f));

    [Fact]
    public void BendingCylinderBindsItsHighestRingToTheTipOnly() =>
        WeightsOfTheRingAt(Length).Should().HaveCount(25).And.AllBeEquivalentTo((onBase: 0f, onTip: 1f));

    [Fact]
    public void BendingCylinderInverseBindsUndoTheRestModelPoses()
    {
        MeshRigging rigging = Primitives.BendingCylinder(radius: Radius, length: Length).Rigging!;
        var modelPoses = new Pose[rigging.Rig.Joints.Length];
        for (int joint = 0; joint < modelPoses.Length; joint++)
        {
            RigJoint each = rigging.Rig.Joints[joint];
            modelPoses[joint] = each.Parent is { } parent ? Pose.Compose(modelPoses[parent], each.Rest) : each.Rest;
        }

        // A point away from every joint shows a wrong turn as well as a wrong shift.
        var point = new Vector3(0.3f, 0.7f, -0.2f);
        modelPoses
            .Zip(rigging.Skin!.InverseBinds, (model, inverseBind) => Pose.Compose(model, inverseBind))
            .Select(skinPose => Vector3.Distance(skinPose.TransformPoint(point), point))
            .Should()
            .HaveCount(2)
            .And.AllSatisfy(miss => miss.Should().BeLessThan(1e-6f, "every skin pose is the identity at the bind"));
    }

    [Fact]
    public void MorphingBoxStretchMovesOnlyTheTopVertices()
    {
        MeshData box = Primitives.MorphingBox(HalfExtents);
        MeshRigging rigging = box.Rigging!;
        MorphTarget stretch = rigging.Morphs.Single(target => target.Morph == rigging.Rig.MorphNamed("Stretch"));

        Vector3[] expected =
        [
            .. box.Vertices.Select(vertex =>
                vertex.Position.Y > 0 ? new Vector3(0, 2 * HalfExtents.Y, 0) : Vector3.Zero
            ),
        ];
        stretch.Positions.Should().Equal(expected);
    }

    [Fact]
    public void MorphingBoxLeanTiltsTheNormalsOfTheFacesThatItShears()
    {
        // Each face of the box is four vertices in a row, in the order of Primitives.Box.
        MeshData box = Primitives.MorphingBox(HalfExtents);
        MeshRigging rigging = box.Rigging!;
        MorphTarget lean = rigging.Morphs.Single(target => target.Morph == rigging.Rig.MorphNamed("Lean"));
        Vector3[] leaned = [.. box.Vertices.Select((vertex, index) => vertex.Position + lean.Positions[index])];

        var misses = new List<float>();
        for (int face = 0; face < box.Vertices.Length; face += 4)
        {
            var faceNormal = Vector3.Normalize(
                Vector3.Cross(leaned[face + 1] - leaned[face], leaned[face + 3] - leaned[face])
            );
            for (int vertex = face; vertex < face + 4; vertex++)
                misses.Add(Vector3.Distance(box.Vertices[vertex].Normal + lean.Normals[vertex], faceNormal));
        }

        misses
            .Should()
            .HaveCount(24)
            .And.AllSatisfy(miss => miss.Should().BeLessThan(1e-6f, "each leaned normal is its leaned face's normal"));
    }

    [Fact]
    public void TwistTurnsEachRingByItsHeight()
    {
        MeshData cylinder = Primitives.TwistingBendingCylinder(radius: Radius, length: Length);
        MeshRigging rigging = cylinder.Rigging!;
        MorphTarget twist = rigging.Morphs.Single(target => target.Morph == rigging.Rig.MorphNamed("Twist"));

        var misses = new List<float>();
        for (int vertex = 0; vertex < cylinder.Vertices.Length; vertex++)
        {
            Vertex rest = cylinder.Vertices[vertex];
            var turn = Quaternion.CreateFromAxisAngle(Vector3.UnitY, MathF.PI / 2 * rest.Position.Y / Length);
            var tangent = new Vector3(rest.Tangent.X, rest.Tangent.Y, rest.Tangent.Z);
            misses.Add(
                Vector3.Distance(rest.Position + twist.Positions[vertex], Vector3.Transform(rest.Position, turn))
            );
            misses.Add(Vector3.Distance(rest.Normal + twist.Normals[vertex], Vector3.Transform(rest.Normal, turn)));
            misses.Add(Vector3.Distance(tangent + twist.Tangents[vertex], Vector3.Transform(tangent, turn)));
        }

        misses
            .Should()
            .HaveCount(3 * 17 * 25)
            .And.AllSatisfy(miss =>
                miss.Should().BeLessThan(1e-5f, "the twist turns the position, normal and tangent")
            );
    }
}
