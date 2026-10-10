using System.Numerics;

namespace Tertius.Simulation.Tests;

/// <summary>Attachments on their own: releasing an entity takes it out of the chains.</summary>
public class AttachmentsSubsystemTests
{
    private static Pose Offset(float x, float y) => new(new Vector3(x, y, 0), Quaternion.Identity);

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ReleasingAChainDetachesEveryLinkAndTheRootLeavesTheTree(bool parentFirst)
    {
        EntityTable entities = TableSupport.NewTable();
        var attachments = new Attachments(entities);
        Entity root = entities.SpawnLive(x: 10);
        Entity child = entities.SpawnLive();
        Entity grandchild = entities.SpawnLive();
        attachments.Attach(child, root, Offset(1, 0));
        attachments.Attach(grandchild, child, Offset(0, 1));
        attachments.Derive();
        Assert.Equal([child, grandchild], attachments.Attached.ToArray());
        Assert.Equal(new Double3(11, 1, 0), entities.BackRow(grandchild).Pose.Position);
        attachments.AssertInvariants();

        // The world releases an attached entity together with its children, in either order.
        Entity[] order = parentFirst ? [child, grandchild] : [grandchild, child];
        foreach (Entity entity in order)
            entities.Release(entity);

        Assert.Empty(attachments.Attached.ToArray());
        Assert.True(attachments.ChildrenOf(root).IsEmpty);
        // The released links are clear: the table checks every column entry of a slot that is not live.
        entities.AssertInvariants();
        attachments.AssertInvariants();

        // The root can take a new child, which starts a new chain.
        Entity next = entities.SpawnLive();
        attachments.Attach(next, root, Offset(2, 0));
        attachments.Derive();
        Assert.Equal(new Double3(12, 0, 0), entities.BackRow(next).Pose.Position);
        attachments.AssertInvariants();
    }
}
