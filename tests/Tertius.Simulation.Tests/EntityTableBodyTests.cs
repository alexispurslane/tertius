using System.Numerics;
using AwesomeAssertions;

namespace Tertius.Simulation.Tests;

/// <summary>
/// What the entity table keeps about bodies so that the sync point never searches for them: the set of the live entities
/// with a body, in slot order, which the numeric update walks instead of every live entity, and the bounding radii of each
/// body's shapes, which the update reads instead of asking physics about the shape every tick. A body is fixed from the
/// spawn to the release, so both follow the spawns and the releases, also when a slot is reused.
/// </summary>
public sealed class EntityTableBodyTests
{
    private static readonly BodyDesc Ball = TestWorld.Ball;

    private static List<Entity> BodyEntities(EntityTable table)
    {
        var list = new List<Entity>();
        foreach (Entity entity in table.BodyEntities)
            list.Add(entity);
        return list;
    }

    [Fact]
    public void TheBodyEntitiesAreTheLiveEntitiesWithABodyInSlotOrder()
    {
        EntityTable table = TableSupport.NewTable();
        Entity first = table.SpawnLiveWith(Ball);
        table.SpawnLiveWith(null);
        Entity third = table.SpawnLiveWith(Ball);
        table.SpawnLiveWith(null);

        BodyEntities(table).Should().Equal(first, third);
        table.AssertInvariants();
    }

    [Fact]
    public void AReservationIsNoBodyEntityUntilItIsLive()
    {
        EntityTable table = TableSupport.NewTable();
        Entity reserved = table.Reserve();

        BodyEntities(table).Should().BeEmpty();
        table.Activate(
            entity: reserved,
            row: new(),
            render: new(),
            components: [],
            wants: [],
            body: Ball,
            bodyCenterOfMass: Vector3.Zero,
            ownedFrame: FrameHandle.Null
        );
        BodyEntities(table).Should().Equal(reserved);
    }

    [Fact]
    public void AReleasedBodyIsNoLongerABodyEntity()
    {
        EntityTable table = TableSupport.NewTable();
        Entity first = table.SpawnLiveWith(Ball);
        Entity second = table.SpawnLiveWith(Ball);

        table.Release(first);

        BodyEntities(table).Should().Equal(second);
        table.AssertInvariants();
    }

    [Fact]
    public void ABodyThatTakesAFreedSlotIsListedInTheOrderOfThatSlot()
    {
        EntityTable table = TableSupport.NewTable();
        Entity first = table.SpawnLiveWith(Ball);
        Entity second = table.SpawnLiveWith(Ball);
        table.Release(first);

        Entity successor = table.SpawnLiveWith(Ball);

        successor.Index.Should().Be(first.Index);
        BodyEntities(table).Should().Equal(successor, second);
        table.AssertInvariants();
    }

    [Fact]
    public void AnEntityWithoutABodyThatTakesTheSlotOfABodyIsNoBodyEntity()
    {
        EntityTable table = TableSupport.NewTable();
        Entity first = table.SpawnLiveWith(Ball);
        Entity second = table.SpawnLiveWith(Ball);
        table.Release(first);

        Entity successor = table.SpawnLiveWith(null);

        successor.Index.Should().Be(first.Index);
        BodyEntities(table).Should().Equal(second);
        table.AssertInvariants();
    }

    [Fact]
    public void TheBodyEntitiesReachEverySlotWhenTheTableGrowsPastItsFirstCapacity()
    {
        EntityTable table = TableSupport.NewTable();
        var expected = new List<Entity>();
        for (int i = 0; i < 200; i++)
        {
            Entity entity = table.SpawnLiveWith(i % 3 == 0 ? Ball : null);
            if (i % 3 == 0)
                expected.Add(entity);
        }

        BodyEntities(table).Should().Equal(expected);
        table.AssertInvariants();
    }

    [Fact]
    public void TheInteriorRadiusOfABodyIsTheBoundingRadiusOfItsInteriorShape()
    {
        EntityTable table = TableSupport.NewTable();
        var hull = new BoxShapeDesc(new(1));
        var interior = new BoxShapeDesc(new(3, 4, 12));

        Entity entity = table.SpawnLiveWith(new(Shape: hull, InteriorShape: interior));

        table.BodyBoundingRadius(entity).Should().Be(hull.BoundingRadius());
        table.BodyInteriorBoundingRadius(entity).Should().Be(interior.BoundingRadius());
        table.BodyInteriorBoundingRadius(entity).Should().NotBe(table.BodyBoundingRadius(entity));
    }

    [Fact]
    public void ABodyWithoutAnInteriorShapeHasTheRadiusOfItsShapeInside()
    {
        EntityTable table = TableSupport.NewTable();
        var shape = new BoxShapeDesc(new(2));

        Entity entity = table.SpawnLiveWith(new(Shape: shape));

        table.BodyInteriorBoundingRadius(entity).Should().Be(shape.BoundingRadius());
    }

    [Fact]
    public void AnEntityWithoutABodyHasNoRadii()
    {
        EntityTable table = TableSupport.NewTable();

        Entity entity = table.SpawnLiveWith(null);

        table.BodyBoundingRadius(entity).Should().Be(0);
        table.BodyInteriorBoundingRadius(entity).Should().Be(0);
    }

    [Fact]
    public void TheRadiiOfAReusedSlotAreThoseOfTheNewBody()
    {
        EntityTable table = TableSupport.NewTable();
        var small = new BoxShapeDesc(new(1));
        var large = new BoxShapeDesc(new(5));
        Entity first = table.SpawnLiveWith(new(Shape: small, InteriorShape: small));
        table.Release(first);

        Entity successor = table.SpawnLiveWith(new(Shape: large, InteriorShape: large));

        successor.Index.Should().Be(first.Index);
        table.BodyBoundingRadius(successor).Should().Be(large.BoundingRadius());
        table.BodyInteriorBoundingRadius(successor).Should().Be(large.BoundingRadius());
        table.AssertInvariants();
    }
}
