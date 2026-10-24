using System.Numerics;

namespace Tertius.Core.Tests;

public class PoseTreeTests
{
    private static Pose At(float x, float y = 0, float z = 0, float yaw = 0) =>
        new(new(x, y, z), Quaternion.CreateFromAxisAngle(Vector3.UnitY, yaw));

    private static void Near(Vector3 expected, Vector3 actual, float eps = 1e-4f) =>
        Assert.True(Vector3.Distance(expected, actual) < eps, $"expected {expected}, got {actual}");

    [Fact]
    public void WorldPosesComposeDownTheTree()
    {
        var tree = new PoseTree<Pose>();
        PoseNode root = tree.Add(PoseNode.Null, At(x: 10));
        PoseNode child = tree.Add(root, At(x: 1, yaw: MathF.PI / 2));
        PoseNode grandchild = tree.Add(child, At(x: 1));
        tree.UpdateWorld();
        Near(new(10, 0, 0), tree.World(root).Position);
        Near(new(11, 0, 0), tree.World(child).Position);
        Near(new(11, 0, -1), tree.World(grandchild).Position);
    }

    [Fact]
    public void ParentAlwaysPrecedesChildAfterReparent()
    {
        var tree = new PoseTree<Pose>();
        PoseNode a = tree.Add(PoseNode.Null, At(x: 0));
        PoseNode b = tree.Add(a, At(x: 1));
        PoseNode c = tree.Add(PoseNode.Null, At(x: 100));
        tree.UpdateWorld();
        Vector3 bWorld = tree.World(b).Position;

        // c was added after b, so making c the parent of b breaks the order.
        tree.Reparent(b, c, keepWorldPose: true);
        tree.UpdateWorld();

        ReadOnlySpan<int> parents = tree.ParentsDense;
        for (int i = 0; i < parents.Length; i++)
            Assert.True(parents[i] < i, $"node {i} has parent {parents[i]}");
        Near(bWorld, tree.World(b).Position);
        Assert.Equal(c, tree.Parent(b));
    }

    [Fact]
    public void RemoveKeepsChildrenWorldPoses()
    {
        var tree = new PoseTree<Pose>();
        PoseNode a = tree.Add(PoseNode.Null, At(x: 5));
        PoseNode b = tree.Add(a, At(x: 2, yaw: 1));
        PoseNode c = tree.Add(b, At(x: 3));
        tree.UpdateWorld();
        Vector3 before = tree.World(c).Position;

        tree.Remove(b);
        tree.UpdateWorld();

        Assert.False(tree.IsAlive(b));
        Assert.Equal(a, tree.Parent(c));
        Near(before, tree.World(c).Position);
        Assert.Equal(2, tree.Count);
    }

    [Fact]
    public void StaleHandlesAreRejectedAfterSlotReuse()
    {
        var tree = new PoseTree<Pose>();
        PoseNode a = tree.Add(PoseNode.Null, At(x: 0));
        tree.Remove(a);
        PoseNode b = tree.Add(PoseNode.Null, At(x: 1));
        Assert.Equal(a.Id, b.Id);
        Assert.NotEqual(a.Generation, b.Generation);
        Assert.False(tree.IsAlive(a));
        Assert.Throws<InvalidOperationException>(() => tree.World(a));
    }

    [Fact]
    public void RelativeGoesThroughCommonAncestor()
    {
        var tree = new PoseTree<DPose>();
        PoseNode sun = tree.Add(PoseNode.Null, DPose.Identity);
        PoseNode planet = tree.Add(sun, new(new(1.5e11, 0, 0), DQuaternion.Identity));
        PoseNode shipA = tree.Add(planet, new(new(7e6, 0, 0), DQuaternion.Identity));
        PoseNode shipB = tree.Add(planet, new(new(7e6, 0.001, 0), DQuaternion.Identity));
        tree.UpdateWorld();

        DPose bInA = tree.Relative(shipB, shipA);
        Assert.Equal(0.001, bInA.Position.Y, 9);
        Assert.Equal(0, bInA.Position.X, 9);
    }

    [Fact]
    public void ComputeRelativeToMatchesPairwiseRelative()
    {
        var tree = new PoseTree<DPose>();
        PoseNode r = tree.Add(PoseNode.Null, new(new(1, 2, 3), DQuaternion.FromAxisAngle(Double3.UnitZ, 0.4)));
        PoseNode a = tree.Add(r, new(new(10, 0, 0), DQuaternion.FromAxisAngle(Double3.UnitY, 1.2)));
        PoseNode b = tree.Add(a, new(new(0, 5, 0), DQuaternion.Identity));
        PoseNode c = tree.Add(r, new(new(-3, 0, 7), DQuaternion.FromAxisAngle(Double3.UnitX, -0.7)));
        PoseNode other = tree.Add(PoseNode.Null, new(new(100, 0, 0), DQuaternion.Identity));
        tree.UpdateWorld();

        var result = new DPose[tree.Count];
        tree.ComputeRelativeTo(b, result);
        foreach (PoseNode n in new[] { r, a, b, c, other })
        {
            DPose expected = tree.Relative(n, b);
            DPose actual = result[tree.DenseIndex(n)];
            Assert.True((expected.Position - actual.Position).Length < 1e-9,
                $"{n}: {expected.Position} vs {actual.Position}");
        }
    }

    [Fact]
    public void ReparentUnderOwnDescendantThrows()
    {
        var tree = new PoseTree<Pose>();
        PoseNode a = tree.Add(PoseNode.Null, Pose.Identity);
        PoseNode b = tree.Add(a, Pose.Identity);
        Assert.Throws<InvalidOperationException>(() => tree.Reparent(a, b));
    }

    [Fact]
    public void ManyNodesStayPacked()
    {
        var tree = new PoseTree<Pose>(2);
        var nodes = new List<PoseNode> { tree.Add(PoseNode.Null, At(x: 0)) };
        for (int i = 1; i < 500; i++)
            nodes.Add(tree.Add(nodes[i / 2], At(x: 1)));
        for (int i = 499; i > 0; i -= 7)
            tree.Remove(nodes[i]);
        tree.UpdateWorld();
        ReadOnlySpan<int> parents = tree.ParentsDense;
        Assert.Equal(tree.Count, parents.Length);
        for (int i = 0; i < parents.Length; i++)
            Assert.True(parents[i] < i);
    }
}
