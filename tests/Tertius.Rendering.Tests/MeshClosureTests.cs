using System.Numerics;

namespace Tertius.Rendering.Tests;

/// <summary>
/// Whether a mesh bounds a volume (docs/plans/back-face-depth-layer.md, decision 5): the weld of its positions, the
/// pairing rule on its directed edges, the triangles that do not count, and the flag of a staged mesh. No GPU needed.
/// </summary>
public sealed class MeshClosureTests
{
    private static bool IsClosed(MeshData mesh) => MeshClosure.IsClosed(mesh.Indices, Positions(mesh));

    private static Vector3[] Positions(MeshData mesh) => [.. mesh.Vertices.Select(vertex => vertex.Position)];

    /// <summary>
    /// A box from the origin to (2, 2, 2), whose three vertices at the origin, one per face that meets there, move by
    /// <paramref name="moves"/> in turn.
    /// </summary>
    private static (uint[] Indices, Vector3[] Positions) CornerBox(params Vector3[] moves)
    {
        MeshData box = Primitives.Box(Vector3.One);
        Vector3[] positions = [.. Positions(box).Select(position => position + Vector3.One)];
        int moved = 0;
        for (int i = 0; i < positions.Length; i++)
            if (positions[i] == Vector3.Zero)
                positions[i] = moves[moved++];
        moved.Should().Be(3, "three faces of the box meet at each corner");
        return (box.Indices, positions);
    }

    [Fact]
    public void ABoxOf24VerticesIsClosed()
    {
        MeshData box = Primitives.Box(new(1, 2, 3));

        box.Vertices.Should().HaveCount(24, "each face has its own four vertices");
        IsClosed(box).Should().BeTrue();
    }

    [Fact]
    public void ASingleQuadIsOpen() => IsClosed(Primitives.Plane(1)).Should().BeFalse();

    [Fact]
    public void ABoxLessOneFaceIsOpen()
    {
        MeshData box = Primitives.Box(new(1, 2, 3));

        // The first six indices are the box's first face.
        IsClosed(box with { Indices = box.Indices[6..] }).Should().BeFalse();
    }

    [Fact]
    public void AUvSphereWithSplitPoleVerticesIsClosed()
    {
        MeshData sphere = Primitives.Sphere(1);

        Positions(sphere)
            .Distinct()
            .Count()
            .Should()
            .BeLessThan(sphere.Vertices.Length, "the seam and the poles repeat positions");
        IsClosed(sphere).Should().BeTrue();
    }

    [Fact]
    public void TwoBoxesThatShareAnEdgeAreClosedThoughFourTrianglesHoldThatEdge()
    {
        // The second box touches the first along the edge x = y = 1 of both, so four triangles hold that edge, two in each
        // direction, as on the station's spokes.
        MeshData box = Primitives.Box(new(0.5f));
        Vector3[] first = [.. Positions(box).Select(position => position + new Vector3(0.5f, 0.5f, 0.5f))];
        Vector3[] second = [.. Positions(box).Select(position => position + new Vector3(1.5f, 1.5f, 0.5f))];
        uint[] indices = [.. box.Indices, .. box.Indices.Select(index => index + (uint)first.Length)];

        MeshClosure.IsClosed(indices, [.. first, .. second]).Should().BeTrue();
    }

    [Fact]
    public void AMeshOfOnlyDegenerateTrianglesIsOpen()
    {
        // Each triangle has two corners at one position. Their edges pair, so only the rule that such a triangle does not
        // count keeps the mesh open.
        Vector3[] positions = [new(0, 0, 0), new(0, 0, 0), new(1, 0, 0)];
        uint[] indices = [0, 1, 2, 1, 0, 2];

        MeshClosure.IsClosed(indices, positions).Should().BeFalse();
    }

    [Fact]
    public void ATriangleWithTwoCornersAtOneWeldedPositionDoesNotCountInAClosedMesh()
    {
        // One degenerate triangle of each form: the first, the second and the third corner repeat another.
        MeshData box = Primitives.Box(Vector3.One);
        uint[] indices = [.. box.Indices, 0, 0, 1, 1, 0, 0, 0, 1, 0];

        MeshClosure.IsClosed(indices, Positions(box)).Should().BeTrue();
    }

    [Fact]
    public void AMeshWithNoTrianglesIsOpen() => MeshClosure.IsClosed([], []).Should().BeFalse();

    [Fact]
    public void AStagedBoxIsClosed() => StagedMesh.Of(Primitives.Box(Vector3.One)).Closed.Should().BeTrue();

    [Fact]
    public void AStagedQuadIsOpen() => StagedMesh.Of(Primitives.Plane(1)).Closed.Should().BeFalse();

    [Fact]
    public void TwoPositionsWithinTheToleranceWeldAcrossAGridLine()
    {
        // The origin lies on a line of every grid that the weld can use, so these moves put the three vertices of the
        // corner on both sides of it in every axis.
        float near = 0.4f * MeshClosure.WeldTolerance([Vector3.Zero, new(2)]);
        (uint[] indices, Vector3[] positions) = CornerBox(new(-near), new(near), new(near, -near, near));

        MeshClosure.IsClosed(indices, positions).Should().BeTrue();
    }

    [Fact]
    public void ACornerSplitByEightFloatStepsOfTheLargestCoordinateWelds()
    {
        // A float near 2 has a step of 2^−22. A generator's sines and an exporter's transforms leave a few such steps.
        float noise = 8 * MathF.Pow(2, -22);
        (uint[] indices, Vector3[] positions) = CornerBox(new(-noise), new(noise), new(noise, -noise, noise));

        MeshClosure.IsClosed(indices, positions).Should().BeTrue();
    }

    [Fact]
    public void ACornerSplitByATenThousandthOfTheMeshsSizeDoesNotWeld()
    {
        (uint[] indices, Vector3[] positions) = CornerBox(new(-0.0002f), new(0), new(0.0002f));

        MeshClosure.IsClosed(indices, positions).Should().BeFalse("a gap of 0.2 mm in a box of 2 m is a real hole");
    }

    [Fact]
    public void TwoPositionsFartherApartThanTheToleranceDoNotWeld()
    {
        float far = 0.6f * MeshClosure.WeldTolerance([Vector3.Zero, new(2)]);
        (uint[] indices, Vector3[] positions) = CornerBox(new(-far), new(far), new(far, -far, far));

        MeshClosure.IsClosed(indices, positions).Should().BeFalse("the corner splits, so its edges do not pair");
    }
}
