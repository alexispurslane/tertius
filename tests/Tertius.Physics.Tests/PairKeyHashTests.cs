using JoltPhysicsSharp;

namespace Tertius.Physics.Tests;

/// <summary>
/// The pair tables of a system hash a pair key so that the pairs of neighbors spread over the buckets (ter-9sri). The
/// default hash of a <see cref="ulong"/> XORs its two halves, the two body IDs, and the pairs of neighbors in a grid of
/// bodies with consecutive IDs share a few values, so a lookup walked long chains.
/// </summary>
public sealed class PairKeyHashTests
{
    /// <summary>
    /// A floor with ID 0 and 2,000 cubes with IDs 1 to 2,000, in 40 rows of 50, each cube against the next in its row and
    /// in its column: the 5,910 pairs of the scene of ContactForgetBenchmarks.
    /// </summary>
    [Fact]
    public void ThePairsOfAGridOfNeighborsGetNearlyAllDifferentHashes()
    {
        const int Columns = 50;
        const int Rows = 40;
        var keys = new List<ulong>();
        for (uint i = 1; i <= Columns * Rows; i++)
        {
            keys.Add(FrameWorld.PairKey(new BodyID(0), new BodyID(i)));
            if (i % Columns != 0)
                keys.Add(FrameWorld.PairKey(new BodyID(i), new BodyID(i + 1)));
            if (i + Columns <= Columns * Rows)
                keys.Add(FrameWorld.PairKey(new BodyID(i), new BodyID(i + Columns)));
        }

        int hashes = keys.Select(FrameWorld.PairKeyHash.Instance.GetHashCode).Distinct().Count();

        keys.Should().HaveCount(5910);
        hashes.Should().BeGreaterThan(keys.Count * 99 / 100, "pairs that share a hash share a chain of a hash table");
    }
}
