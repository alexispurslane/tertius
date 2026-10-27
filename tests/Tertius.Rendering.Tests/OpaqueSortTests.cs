namespace Tertius.Rendering.Tests;

/// <summary>
/// The order of a view's opaque draws: by depth range, then by run, the solid run before the cutout run, then by mesh,
/// and at equal keys in the order of the visible instances, so one scene always gives the same commands.
/// </summary>
public sealed class OpaqueSortTests
{
    /// <summary>
    /// Items that a view appends in the order of its instances, each in up to three ranges, solid or cutout, of meshes 0
    /// to <paramref name="meshes"/> − 1, come out in the order of (key, index), the order that sorting by both gives.
    /// </summary>
    [Theory]
    [InlineData(1, 1)]
    [InlineData(1, 500)]
    [InlineData(7, 2_000)]
    [InlineData(300, 5_000)]
    public void OpaqueItemsSortByRangeThenRunThenMeshThenTheOrderOfTheirInstances(int meshes, int instances)
    {
        var random = new Random(meshes * 31 + instances);
        var items = new List<SceneBatcher.DrawItem>();
        for (int index = 0; index < instances; index++)
        {
            int mesh = random.Next(meshes);
            ulong run = random.Next(2) == 0 ? SceneBatcher.CutoutKeyBit : 0;
            for (int range = 0; range < DepthRanges.Count; range++)
                if (random.Next(3) == 0 || range == DepthRanges.Count - 1 && items.Count == 0)
                    items.Add(new(((ulong)range << 32) | run | (uint)mesh, index));
        }

        SceneBatcher.DrawItem[] sorted = [.. items];
        var sorter = new SceneBatcher.OpaqueSorter();
        sorter.Sort(sorted, meshes);

        sorted
            .Should()
            .Equal(
                items.OrderBy(item => item.Key).ThenBy(item => item.Index),
                "the sort keeps the order of the instances at equal keys"
            );
    }

    [Fact]
    public void ASorterServesViewsOfMoreMeshesAndMoreItemsThanBefore()
    {
        var sorter = new SceneBatcher.OpaqueSorter();
        SceneBatcher.DrawItem[] few = [new((1UL << 32) | 0, 0), new(0, 1)];
        SceneBatcher.DrawItem[] many = [new((2UL << 32) | 9, 0), new((2UL << 32) | 3, 1), new(9, 2), new(3, 3)];

        sorter.Sort(few, meshCount: 1);
        sorter.Sort(many, meshCount: 10);

        few.Select(item => item.Index).Should().Equal(1, 0);
        many.Select(item => item.Index).Should().Equal(3, 2, 1, 0);
    }
}
