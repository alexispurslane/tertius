using System.Numerics;
using AwesomeAssertions;

namespace Tertius.Core.Tests;

/// <summary>
/// The test meshes with rigging (docs/plans/skinning-and-morphs.md, "Part 1: the seam"): the closed tube of both bending
/// cylinders, the bending cylinder's skin, the morphing box's two morphs, and the twist of the twisting, bending cylinder.
/// </summary>
public sealed class PrimitivesRigTests
{
    private const float Radius = 0.25f;
    private const float Length = 2;
    private const int Segments = 24;

    // Each half extent differs from the others, so a swap of two axes shows.
    private static readonly Vector3 HalfExtents = new(0.5f, 1, 0.75f);

    public static TheoryData<string> Meshes =>
        [
            nameof(Primitives.BendingCylinder),
            nameof(Primitives.MorphingBox),
            nameof(Primitives.TwistingBendingCylinder),
        ];

    public static TheoryData<string> Cylinders =>
        [nameof(Primitives.BendingCylinder), nameof(Primitives.TwistingBendingCylinder)];

    public static TheoryData<string, float> CylinderEnds =>
        new()
        {
            { nameof(Primitives.BendingCylinder), 0 },
            { nameof(Primitives.BendingCylinder), Length },
            { nameof(Primitives.TwistingBendingCylinder), 0 },
            { nameof(Primitives.TwistingBendingCylinder), Length },
        };

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

    /// <summary>The outward normal of the flat cap at <paramref name="height"/>: -Y at the bottom, +Y at the top.</summary>
    private static Vector3 CapNormalAt(float height) => height == 0 ? -Vector3.UnitY : Vector3.UnitY;

    /// <summary>The indices of the vertices at <paramref name="height"/> whose normal is the cap's flat normal there.</summary>
    private static int[] CapAt(MeshData cylinder, float height) =>
        [
            .. cylinder
                .Vertices.Index()
                .Where(each => each.Item.Position.Y == height && each.Item.Normal == CapNormalAt(height))
                .Select(each => each.Index),
        ];

    /// <summary>The unnormalized normal of a triangle, along which its corners wind counterclockwise.</summary>
    private static Vector3 FaceNormal(Vector3 a, Vector3 b, Vector3 c) => Vector3.Cross(b - a, c - a);

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

    [Theory]
    [MemberData(nameof(Cylinders))]
    public void EachBendingCylinderIsAClosedTubeWhoseEveryEdgeTwoTrianglesShare(string mesh)
    {
        // The wall's seam and the caps' rims repeat a position in vertices of their own, so the edges are counted
        // between welded positions: each vertex stands for the first vertex at its position.
        MeshData cylinder = Named(mesh);
        Vertex[] vertices = [.. cylinder.Vertices];
        int[] welded =
        [
            .. vertices.Select(vertex =>
                Array.FindIndex(vertices, other => Vector3.Distance(other.Position, vertex.Position) < 1e-5f)
            ),
        ];
        var sharing = new Dictionary<(int low, int high), int>();
        for (int corner = 0; corner < cylinder.Indices.Length; corner += 3)
        for (int side = 0; side < 3; side++)
        {
            int from = welded[cylinder.Indices[corner + side]],
                to = welded[cylinder.Indices[corner + (side + 1) % 3]];
            (int low, int high) edge = (Math.Min(from, to), Math.Max(from, to));
            sharing[edge] = sharing.GetValueOrDefault(edge) + 1;
        }

        sharing.Values.Should().AllBeEquivalentTo(2, "a closed tube has no rim that only one triangle borders");
    }

    [Theory]
    [MemberData(nameof(CylinderEnds))]
    public void EachEndOfABendingCylinderIsAFlatCapWhoseFacesFaceOut(string mesh, float height)
    {
        MeshData cylinder = Named(mesh);
        Vector3[] capFaceNormals =
        [
            .. cylinder
                .Indices.Chunk(3)
                .Select(corners => corners.Select(index => cylinder.Vertices[(int)index].Position).ToArray())
                .Where(corners => corners.All(corner => corner.Y == height))
                .Select(corners => Vector3.Normalize(FaceNormal(corners[0], corners[1], corners[2]))),
        ];

        capFaceNormals
            .Should()
            .HaveCount(Segments, "the cap is a fan of one triangle for each segment")
            .And.AllSatisfy(normal =>
                Vector3
                    .Distance(normal, CapNormalAt(height))
                    .Should()
                    .BeLessThan(1e-5f, "the cap faces out of the tube")
            );
    }

    [Theory]
    [MemberData(nameof(CylinderEnds))]
    public void EachCapHasVerticesOfItsOwnSoItsEdgeWithTheWallStaysHard(string mesh, float height)
    {
        MeshData cylinder = Named(mesh);
        Vertex[] wallRing = [.. cylinder.Vertices.Where(vertex => vertex.Position.Y == height && vertex.Normal.Y == 0)];

        CapAt(cylinder, height)
            .Should()
            .HaveCount(Segments + 2, "a center and a rim of one vertex per seam of the wall");
        wallRing.Should().HaveCount(Segments + 1, "the wall keeps its own ring with radial normals");
    }

    [Theory]
    [MemberData(nameof(CylinderEnds))]
    public void EachCapsTangentLiesInItsPlaneAndItsTextureFollowsItsTangentFrame(string mesh, float height)
    {
        // As on the wall and the plane, u grows along the tangent and v shrinks along the bitangent, which is
        // w × (normal × tangent). The cap's disc spans [0, 1] in u and v.
        MeshData cylinder = Named(mesh);
        int[] cap = CapAt(cylinder, height);
        Vertex center = cylinder.Vertices[cap.Single(index => cylinder.Vertices[index].Position is { X: 0, Z: 0 })];

        var misses = new List<float>();
        foreach (int index in cap)
        {
            Vertex vertex = cylinder.Vertices[index];
            var tangent = new Vector3(vertex.Tangent.X, vertex.Tangent.Y, vertex.Tangent.Z);
            Vector3 bitangent = vertex.Tangent.W * Vector3.Cross(vertex.Normal, tangent);
            Vector3 offset = vertex.Position - center.Position;
            Vector2 expected =
                new Vector2(Vector3.Dot(offset, tangent), -Vector3.Dot(offset, bitangent)) / (2 * Radius);
            vertex.Tangent.W.Should().Be(1);
            tangent.Y.Should().Be(0, "the tangent lies in the cap's plane");
            misses.Add(MathF.Abs(tangent.Length() - 1));
            misses.Add(Vector2.Distance(vertex.TexCoord - center.TexCoord, expected));
        }

        center.TexCoord.Should().Be(new Vector2(0.5f, 0.5f), "the center of the disc is the center of the texture");
        misses
            .Should()
            .HaveCount(2 * (Segments + 2))
            .And.AllSatisfy(miss => miss.Should().BeLessThan(1e-5f, "the texture follows the unit tangent frame"));
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
    public void BendingCylinderBindsItsLowestRingAndItsBottomCapToTheBaseOnly() =>
        WeightsOfTheRingAt(0)
            .Should()
            .HaveCount(Segments + 1 + Segments + 2, "the wall's ring and the cap's own vertices")
            .And.AllBeEquivalentTo((onBase: 1f, onTip: 0f));

    [Fact]
    public void BendingCylinderBindsItsHighestRingAndItsTopCapToTheTipOnly() =>
        WeightsOfTheRingAt(Length)
            .Should()
            .HaveCount(Segments + 1 + Segments + 2, "the wall's ring and the cap's own vertices")
            .And.AllBeEquivalentTo((onBase: 0f, onTip: 1f));

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
            .HaveCount(3 * (17 * (Segments + 1) + 2 * (Segments + 2)), "every vertex of the wall and the caps")
            .And.AllSatisfy(miss =>
                miss.Should().BeLessThan(1e-5f, "the twist turns the position, normal and tangent")
            );
    }

    [Fact]
    public void TwistTurnsTheTopCapWithTheTopRingAndKeepsItFacingUp()
    {
        MeshData cylinder = Primitives.TwistingBendingCylinder(radius: Radius, length: Length);
        MeshRigging rigging = cylinder.Rigging!;
        MorphTarget twist = rigging.Morphs.Single(target => target.Morph == rigging.Rig.MorphNamed("Twist"));
        var quarterTurn = Quaternion.CreateFromAxisAngle(Vector3.UnitY, MathF.PI / 2);

        var misses = new List<float>();
        foreach (int index in CapAt(cylinder, Length))
        {
            Vertex rest = cylinder.Vertices[index];
            var tangent = new Vector3(rest.Tangent.X, rest.Tangent.Y, rest.Tangent.Z);
            misses.Add(
                Vector3.Distance(rest.Position + twist.Positions[index], Vector3.Transform(rest.Position, quarterTurn))
            );
            misses.Add(Vector3.Distance(tangent + twist.Tangents[index], Vector3.Transform(tangent, quarterTurn)));
            misses.Add(Vector3.Distance(rest.Normal + twist.Normals[index], Vector3.UnitY));
        }

        misses
            .Should()
            .HaveCount(3 * (Segments + 2))
            .And.AllSatisfy(miss =>
                miss.Should().BeLessThan(1e-5f, "the top cap turns by a quarter turn and still faces up")
            );
    }
}
