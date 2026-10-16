using AwesomeAssertions;
using static Tertius.Simulation.Tests.EntityTableSupport;

namespace Tertius.Simulation.Tests;

/// <summary>
/// The component flags in the entity table: a back copy that component changes write, a front copy that the dirty-row
/// copy brings up to date, and the index of the entities by each type of their front flags.
/// </summary>
public class EntityTableComponentFlagsTests
{
    private static int Id<T>() where T : IComponent => ComponentTypes.IdOf(typeof(T));

    [Fact]
    public void ASpawnWritesBothCopiesOfTheFlagsAndListsTheEntityByEachType()
    {
        EntityTable table = TableSupport.NewTable();

        Entity entity = table.SpawnLiveWith(null, new Flammable(), new Metal());

        table.FrontComponents(entity).Should().Be(ComponentFlags.Of([new Flammable(), new Metal()]));
        table.WithComponent(Id<Flammable>()).ToArray().Should().Equal(entity);
        table.WithComponent(Id<Metal>()).ToArray().Should().Equal(entity);
        table.WithComponent(Id<Wet>()).ToArray().Should().BeEmpty();
        table.AssertInvariants();
    }

    [Fact]
    public void AComponentAddedDuringGameplayReachesTheFrontFlagsAndTheIndexOnlyAtTheCopy()
    {
        EntityTable table = TableSupport.NewTable();
        Entity entity = table.SpawnLiveWith(null, new Metal());
        table.BeginTick(spawnPoolSize: 0);

        table.AddComponent(entity, new Flammable());
        bool frontDuringTick = table.FrontComponents(entity).Contains<Flammable>();
        table.EndGameplay();
        table.CopyDirtyRows();

        frontDuringTick.Should().BeFalse("the front flags show the end of the previous tick");
        table.FrontComponents(entity).Contains<Flammable>().Should().BeTrue();
        table.WithComponent(Id<Flammable>()).ToArray().Should().Equal(entity);
        table.AssertInvariants();
    }

    [Fact]
    public void AComponentRemovedOutsideGameplayLeavesTheFrontFlagsAndTheIndexAtTheCopy()
    {
        EntityTable table = TableSupport.NewTable();
        var wet = new Wet();
        Entity entity = table.SpawnLiveWith(null, wet, new Metal());

        table.RemoveComponent(entity, wet).Should().BeTrue();
        table.FrontComponents(entity).Contains<Wet>().Should().BeTrue("nothing was copied yet");
        table.CopyDirtyRows();

        table.FrontComponents(entity).Contains<Wet>().Should().BeFalse();
        table.WithComponent(Id<Wet>()).ToArray().Should().BeEmpty();
        table.WithComponent(Id<Metal>()).ToArray().Should().Equal(entity);
        table.AssertInvariants();
    }

    [Fact]
    public void ReleasingAnEntityTakesItOutOfTheIndexOfEveryTypeItHad()
    {
        EntityTable table = TableSupport.NewTable();
        Entity doomed = table.SpawnLiveWith(null, new Crew(), new Dead());
        Entity kept = table.SpawnLiveWith(null, new Crew());

        table.Release(doomed);

        table.WithComponent(Id<Crew>()).ToArray().Should().Equal(kept);
        table.WithComponent(Id<Dead>()).ToArray().Should().BeEmpty();
        table.AssertInvariants();
    }

    [Fact]
    public void AnEntityWithoutABodyHasDefaultCollisionRowsAndOneWithABodyHasItsSpawnGroups()
    {
        EntityTable table = TableSupport.NewTable();
        BodyDesc body = TestWorld.Ball with { CollisionGroups = CollisionGroups.Of(CollisionGroup.Solid) };

        Entity plain = table.SpawnLiveWith(null);
        Entity ball = table.SpawnLiveWith(body);

        table.FrontCollision(plain).Should().Be(default(BodyCollision));
        table.FrontCollision(ball).Should().Be(new BodyCollision(body.CollisionGroups, body.CollisionMask));
        table.AssertInvariants();
    }
}
