namespace Tertius.Simulation.Tests;

/// <summary>The spatial index on its own: releasing an entity takes it out of its grid cell.</summary>
public class SpatialIndexSubsystemTests
{
    [Fact]
    public void ReleasingASlotTakesItOutOfItsCell()
    {
        EntityTable entities = TableSupport.NewTable();
        var frames = new FrameTree();
        FrameHandle root = frames.Create(new Entity(60, 1), FrameHandle.Null, DPose.Identity, FrameBounds.Sphere(1000), MotionDriver.Static);
        var spatial = new SpatialIndex(entities, frames);
        Entity near = entities.SpawnLive(root, x: 1);
        Entity far = entities.SpawnLive(root, x: 500);
        Entity alsoNear = entities.SpawnLive(root, x: 2);
        spatial.Update();
        spatial.AssertInvariants();

        var found = new List<Entity>();
        spatial.QueryRadius(root, Double3.Zero, 10, found);
        Assert.Equal([near, alsoNear], found);

        entities.Release(near);
        spatial.AssertInvariants();
        spatial.QueryRadius(root, Double3.Zero, 10, found);
        Assert.Equal([alsoNear], found);

        // A new entity in the released slot is placed by the next update, and only then.
        Entity successor = entities.SpawnLive(root, x: 3);
        Assert.Equal(near.Index, successor.Index);
        spatial.QueryRadius(root, Double3.Zero, 10, found);
        Assert.Equal([alsoNear], found);
        spatial.Update();
        spatial.QueryRadius(root, Double3.Zero, 1000, found);
        Assert.Equal([successor, far, alsoNear], found);
        spatial.AssertInvariants();
    }
}
