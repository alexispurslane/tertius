using AwesomeAssertions;

namespace Tertius.Core.Tests;

/// <summary>
/// A move under a node that comes later in the dense order leaves the order to be fixed at the next access, which replaces
/// the tree's arrays. A read or write right after the move still finds its own node.
/// </summary>
public sealed class PoseTreeReorderTests
{
    private static DPose At(double x) => new(new(x, 0, 0), DQuaternion.Identity);

    private static (PoseTree<DPose> Tree, PoseNode Older, PoseNode Newer) MovedUnderNewer()
    {
        var tree = new PoseTree<DPose>(4);
        PoseNode root = tree.Add(PoseNode.Null, DPose.Identity);
        PoseNode older = tree.Add(root, At(1));
        PoseNode newer = tree.Add(root, At(2));
        tree.Reparent(older, newer, keepWorldPose: false);
        return (tree, older, newer);
    }

    [Fact]
    public void A_read_after_a_move_under_a_newer_node_finds_its_node()
    {
        (PoseTree<DPose> tree, PoseNode older, PoseNode newer) = MovedUnderNewer();
        tree.GetLocal(older).Should().Be(At(1));
        tree.GetLocal(newer).Should().Be(At(2));
    }

    [Fact]
    public void A_write_after_a_move_under_a_newer_node_lands_on_its_node()
    {
        (PoseTree<DPose> tree, PoseNode older, PoseNode newer) = MovedUnderNewer();
        tree.Local(older) = At(5);
        tree.GetLocal(older).Should().Be(At(5));
        tree.GetLocal(newer).Should().Be(At(2));
    }

    [Fact]
    public void The_parent_after_a_move_under_a_newer_node_is_the_newer_node()
    {
        (PoseTree<DPose> tree, PoseNode older, PoseNode newer) = MovedUnderNewer();
        tree.Parent(older).Should().Be(newer);
    }
}
