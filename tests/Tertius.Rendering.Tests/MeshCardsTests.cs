using System.Numerics;

namespace Tertius.Rendering.Tests;

/// <summary>
/// The card numbers of a mesh (docs/plans/skin-and-hair.md, "The card number"): each connected strip of triangles is
/// one card, and every vertex of a triangle carries the number of the triangle's card.
/// </summary>
public sealed class MeshCardsTests
{
    private static Vertex At(Vector3 position, Vector3 normal) =>
        new()
        {
            Position = position,
            Normal = normal,
            Tangent = new(x: 1, y: 0, z: 0, w: 1),
        };

    /// <summary>The card number of each triangle: the number of its first vertex, after a check that its three agree.</summary>
    private static uint[] CardOfEachTriangle(uint[] indices, uint[] cards) =>
        [
            .. Enumerable
                .Range(0, indices.Length / 3)
                .Select(triangle =>
                {
                    uint[] each = [.. indices.Skip(3 * triangle).Take(3).Select(index => cards[index])];
                    each.Should()
                        .AllBeEquivalentTo(each[0], "the three vertices of triangle {0} carry one card", triangle);
                    return each[0];
                }),
        ];

    [Fact]
    public void EachConnectedStripIsOneCardAndEachTriangleTakesItsStripsNumber()
    {
        // Two strips 1 m apart along X, of two quads and of one, whose quads share the vertices between them.
        Vertex[] vertices =
        [
            At(new(0, 0, 0), Vector3.UnitY),
            At(new(0.1f, 0, 0), Vector3.UnitY),
            At(new(0, 0, 1), Vector3.UnitY),
            At(new(0.1f, 0, 1), Vector3.UnitY),
            At(new(0, 0, 2), Vector3.UnitY),
            At(new(0.1f, 0, 2), Vector3.UnitY),
            At(new(1, 0, 0), Vector3.UnitY),
            At(new(1.1f, 0, 0), Vector3.UnitY),
            At(new(1, 0, 1), Vector3.UnitY),
            At(new(1.1f, 0, 1), Vector3.UnitY),
        ];
        uint[] indices = [0, 3, 1, 0, 2, 3, 2, 5, 3, 2, 4, 5, 6, 9, 7, 6, 8, 9];

        uint[] cards = MeshCards.Number(vertices, indices);

        CardOfEachTriangle(indices, cards).Should().Equal([0, 0, 0, 0, 1, 1], "the mesh holds two strips");
    }

    [Fact]
    public void TrianglesThatMeetOnlyAtVerticesOfOnePositionAreOneCard()
    {
        // A strip with a sharp crease at z = 1: each quad has its own vertices there, with its own normal, as an exporter
        // splits a hard edge, but the two copies of each crease vertex have one position.
        var tilted = Vector3.Normalize(new(0, 1, 1));
        Vertex[] vertices =
        [
            At(new(0, 0, 0), Vector3.UnitY),
            At(new(0.1f, 0, 0), Vector3.UnitY),
            At(new(0, 0, 1), Vector3.UnitY),
            At(new(0.1f, 0, 1), Vector3.UnitY),
            At(new(0, 0, 1), tilted),
            At(new(0.1f, 0, 1), tilted),
            At(new(0, -1, 1), tilted),
            At(new(0.1f, -1, 1), tilted),
        ];
        uint[] indices = [0, 3, 1, 0, 2, 3, 4, 7, 5, 4, 6, 7];

        uint[] cards = MeshCards.Number(vertices, indices);

        CardOfEachTriangle(indices, cards)
            .Should()
            .AllBeEquivalentTo(0u, "a card that bends past the smooth angle stays one strip");
    }

    [Fact]
    public void CardsAreNumberedInTheOrderOfTheirFirstTriangles()
    {
        // The first triangle uses the last vertices: its strip is card 0 all the same.
        Vertex[] vertices =
        [
            At(new(0, 0, 0), Vector3.UnitY),
            At(new(1, 0, 0), Vector3.UnitY),
            At(new(0, 0, 1), Vector3.UnitY),
            At(new(5, 0, 0), Vector3.UnitY),
            At(new(6, 0, 0), Vector3.UnitY),
            At(new(5, 0, 1), Vector3.UnitY),
        ];
        uint[] indices = [3, 5, 4, 0, 2, 1];

        uint[] cards = MeshCards.Number(vertices, indices);

        CardOfEachTriangle(indices, cards).Should().Equal([0, 1]);
    }

    [Fact]
    public void TwoStripsThatTouchAtOnePositionAreOneCard()
    {
        // Two strips that fan out from one root at the origin, as an exporter can snap two cards' roots to one scalp
        // vertex. Each strip has its own copy of the root. The cost of the join by position: the two never shade each
        // other under the screen-space light (docs/plans/skin-and-hair.md, "Known limits").
        Vertex[] vertices =
        [
            At(new(0, 0, 0), Vector3.UnitY),
            At(new(-0.1f, 0, 1), Vector3.UnitY),
            At(new(-0.2f, 0, 1), Vector3.UnitY),
            At(new(0, 0, 0), Vector3.UnitY),
            At(new(0.2f, 0, 1), Vector3.UnitY),
            At(new(0.1f, 0, 1), Vector3.UnitY),
        ];
        uint[] indices = [0, 1, 2, 3, 4, 5];

        uint[] cards = MeshCards.Number(vertices, indices);

        CardOfEachTriangle(indices, cards).Should().Equal([0, 0], "the strips share the position of their root");
    }

    [Fact]
    public void AVertexOfNoTriangleTakesANumberAfterTheCards()
    {
        // Vertex 1 belongs to no triangle. It draws nothing, so its number only has to name no card of a triangle.
        Vertex[] vertices =
        [
            At(new(0, 0, 0), Vector3.UnitY),
            At(new(3, 0, 0), Vector3.UnitY),
            At(new(1, 0, 0), Vector3.UnitY),
            At(new(0, 0, 1), Vector3.UnitY),
        ];
        uint[] indices = [0, 3, 2];

        uint[] cards = MeshCards.Number(vertices, indices);

        cards.Should().Equal([0, 1, 0, 0], "the triangle's strip is card 0, and the lone vertex comes after it");
    }
}
