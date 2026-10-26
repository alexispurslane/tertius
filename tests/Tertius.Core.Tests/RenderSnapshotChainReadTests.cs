using System.Numerics;
using AwesomeAssertions;

namespace Tertius.Core.Tests;

/// <summary>
/// The reads of a render snapshot that derive from its chain (docs/plans/environment-maps-per-frame.md, decision 15): the
/// camera's frame's id is the first chain frame's, and the root's rotations are the last chain frame's.
/// </summary>
public sealed class RenderSnapshotChainReadTests
{
    private static readonly Quaternion Tilt = Quaternion.CreateFromAxisAngle(Vector3.UnitX, 0.4f);
    private static readonly Quaternion Turn = Quaternion.CreateFromAxisAngle(Vector3.UnitY, 1.1f);

    [Fact]
    public void TheRootsRotationsAreThoseOfTheLastFrameOfTheChain()
    {
        var snapshot = new RenderSnapshot();
        snapshot.Chain.Add(new(FrameId: 7, Rotation: Quaternion.Identity, PreviousRotation: Quaternion.Identity));
        snapshot.Chain.Add(new(FrameId: 3, Rotation: Turn, PreviousRotation: Tilt));

        (snapshot.RootRotation, snapshot.PreviousRootRotation).Should().Be((Turn, Tilt));
    }

    [Fact]
    public void TheOriginFrameIdIsTheIdOfTheFirstFrameOfTheChain()
    {
        var snapshot = new RenderSnapshot();
        snapshot.Chain.Add(new(FrameId: 7, Rotation: Quaternion.Identity, PreviousRotation: Quaternion.Identity));
        snapshot.Chain.Add(new(FrameId: 3, Rotation: Turn, PreviousRotation: Tilt));

        snapshot.OriginFrameId.Should().Be(7);
    }

    [Fact]
    public void ClearEmptiesTheChainSoTheReadsFallBackToNoCamera()
    {
        var snapshot = new RenderSnapshot();
        snapshot.Chain.Add(new(FrameId: 3, Rotation: Turn, PreviousRotation: Tilt));

        snapshot.Clear();

        snapshot.Chain.Should().BeEmpty();
        (snapshot.OriginFrameId, snapshot.RootRotation, snapshot.PreviousRootRotation)
            .Should()
            .Be((0L, Quaternion.Identity, Quaternion.Identity));
    }
}
