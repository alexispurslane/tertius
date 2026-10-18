using AwesomeAssertions;

namespace Tertius.Simulation.Tests;

/// <summary>
/// Runs of entity queries on the snapshot: what each filter keeps, the endings, the shorthand chain, and the index that
/// a run starts from. The world here has no bodies; the collision filters are in <see cref="EntityQueryCollisionTests"/>.
/// </summary>
public class EntityQueryRunTests
{
    private static List<Entity> Found(FoundEntities run)
    {
        var found = new List<Entity>();
        foreach (Entity entity in run)
            found.Add(entity);
        return found;
    }

    /// <summary>The x of an entity's position in its frame, from the snapshot, as a predicate reads it. NaN for an entity in no frame.</summary>
    private static double X(Snapshot snapshot, Entity entity) =>
        snapshot.TryGetPoseIn(entity, snapshot.FrameOf(entity), out DPose pose) ? pose.Position.X : double.NaN;

    private static (QueryIndex Index, int Count) PlanOf(FoundEntities run)
    {
        FoundEntities.Enumerator enumerator = run.GetEnumerator();
        try
        {
            return (enumerator.Index, enumerator.CandidateCount);
        }
        finally
        {
            enumerator.Dispose();
        }
    }

    // ---- Component filters --------------------------------------------------------------------------------------

    [Fact]
    public void FindReturnsTheEntitiesWithEveryWithTypeAndNoWithoutTypeInEntityIndexOrder()
    {
        using var t = new TestWorld();
        Entity a = t.SpawnWith(new Flammable(), new Metal());
        t.SpawnWith(new Flammable());
        Entity c = t.SpawnWith(new Metal(), new Flammable(), new Crew());
        t.SpawnWith(new Flammable(), new Metal(), new Wet());

        List<Entity> found = Found(t.World.Snapshot.Find(EntityQuery.With<Metal>().With<Flammable>().Without<Wet>()));

        found.Should().Equal(a, c);
    }

    [Fact]
    public void TheDefaultQueryFindsEveryLiveEntityInEntityIndexOrder()
    {
        using var t = new TestWorld();
        Entity a = t.SpawnWith(new Flammable());
        Entity b = t.SpawnWith();
        var all = new List<Entity>();
        t.World.CopyLiveEntities(all);

        Found(t.World.Snapshot.Find(default)).Should().Equal(all).And.Contain([a, b]);
    }

    [Fact]
    public void AQueryNoLongerFindsAnEntityThatWasDestroyedOrLostItsComponent()
    {
        using var t = new TestWorld();
        Entity doomed = t.SpawnWith(new Crew());
        var crew = new Crew();
        Entity changed = t.SpawnWith(crew);
        Entity kept = t.SpawnWith(new Crew());

        t.World.Destroy(doomed);
        t.World.RemoveComponent(changed, crew);

        Found(t.World.Snapshot.Find(EntityQuery.With<Crew>())).Should().Equal(kept);
    }

    [Fact]
    public void MatchesSaysWhetherARunOfTheQueryWouldFindTheEntity()
    {
        using var t = new TestWorld();
        Entity crew = t.SpawnAt(new Double3(9, 0, 0), new Crew());
        Entity near = t.SpawnAt(new Double3(1, 0, 0), new Crew());
        Entity cargo = t.SpawnAt(new Double3(9, 0, 0));
        EntityQuery query = EntityQuery.With<Crew>().Where(static (snapshot, entity) => X(snapshot, entity) > 5);

        Snapshot snapshot = t.World.Snapshot;

        snapshot.Matches(query, crew).Should().BeTrue();
        snapshot.Matches(query, near).Should().BeFalse("the predicate drops it");
        snapshot.Matches(query, cargo).Should().BeFalse("it is not crew");
    }

    [Fact]
    public void NoQueryMatchesAnEntityThatIsNotLive()
    {
        using var t = new TestWorld();
        Entity doomed = t.SpawnWith(new Crew());
        t.World.Destroy(doomed);

        t.World.Snapshot.Matches(default, doomed).Should().BeFalse();
        t.World.Snapshot.Matches(default, Entity.Null).Should().BeFalse();
    }

    // ---- Places --------------------------------------------------------------------------------------------------

    [Fact]
    public void InKeepsOnlyTheEntitiesOfTheFrame()
    {
        using var t = new TestWorld();
        FrameHandle ship = t.SpawnStaticFrame(TestWorld.At(100));
        Entity aboard = t.World.SpawnWith(ship, new Crew());
        t.SpawnWith(new Crew());

        Found(t.World.Snapshot.Find(EntityQuery.With<Crew>().In(ship))).Should().Equal(aboard);
    }

    [Fact]
    public void InAlsoFiltersTheCandidatesThatAnotherIndexGave()
    {
        using var t = new TestWorld();
        FrameHandle ship = t.SpawnStaticFrame(TestWorld.At(100));
        t.World.SpawnWith(ship, new Crew());
        t.World.SpawnWith(ship, new Crew());
        t.SpawnWith(new Dead());

        // One Dead entity against two members of the ship: the run starts from the Dead one, which is not aboard.
        FoundEntities run = t.World.Snapshot.Find(EntityQuery.With<Dead>().In(ship));

        PlanOf(run).Index.Should().Be(QueryIndex.Components);
        run.Any().Should().BeFalse();
    }

    [Fact]
    public void NearAroundTheAskerKeepsTheEntitiesWithinTheRadiusOfIt()
    {
        using var t = new TestWorld();
        Entity close = t.SpawnAt(new Double3(3, 0, 0), new Flammable());
        t.SpawnAt(new Double3(6, 0, 0), new Flammable());
        Entity edge = t.SpawnAt(new Double3(0, 0, -5), new Flammable());
        var found = new List<Entity>();
        Entity asker = t.SpawnAt(default, Lambda.OnTick((self, scoped ref events, scoped ref outbox) =>
            self.Snapshot.Find(EntityQuery.With<Flammable>().Near(5)).CopyTo(found)));

        t.World.Tick();

        found.Should().Equal(close, edge);
    }

    [Fact]
    public void NearAroundTheAskerOnTheSnapshotOfTheWorldThrowsBecauseThatSnapshotHasNoAsker()
    {
        using var t = new TestWorld();
        t.SpawnWith(new Flammable());

        Action run = () => t.World.Snapshot.Find(EntityQuery.Near(5)).Count();

        run.Should().Throw<InvalidOperationException>().WithMessage("*Self.Snapshot*");
    }

    [Fact]
    public void NearAroundAnotherEntityKeepsTheEntitiesWithinTheRadiusOfItsSnapshotPosition()
    {
        using var t = new TestWorld();
        Entity center = t.SpawnAt(new Double3(50, 0, 0));
        Entity close = t.SpawnAt(new Double3(52, 0, 0), new Metal());
        t.SpawnAt(new Double3(0, 0, 0), new Metal());

        Found(t.World.Snapshot.Find(EntityQuery.With<Metal>().Near(center, 3))).Should().Equal(close);
    }

    [Fact]
    public void NearAroundAnEntityThatIsNotLiveMatchesNothing()
    {
        using var t = new TestWorld();
        Entity center = t.SpawnAt(new Double3(50, 0, 0));
        t.SpawnAt(new Double3(50, 0, 0), new Metal());
        t.World.Destroy(center);

        t.World.Snapshot.Find(EntityQuery.Near(center, 100)).Any().Should().BeFalse();
    }

    [Fact]
    public void NearAroundAFixedPointKeepsTheEntitiesWithinTheRadiusOfIt()
    {
        using var t = new TestWorld();
        Entity inside = t.SpawnAt(new Double3(10, 10, 0), new Metal());
        t.SpawnAt(new Double3(10, 13, 0), new Metal());

        Found(t.World.Snapshot.Find(EntityQuery.Near(t.Root, new Double3(10, 10, 1), 2))).Should().Equal(inside);
    }

    [Fact]
    public void NearAroundAPointOfAFrameThatIsGoneMatchesNothing()
    {
        using var t = new TestWorld();
        FrameHandle ship = t.SpawnStaticFrame(TestWorld.At(100));
        t.World.SpawnWith(ship, new Crew());
        t.World.Destroy(ship.Entity);

        FoundEntities run = t.World.Snapshot.Find(EntityQuery.Near(ship, default, 1e6));

        run.Any().Should().BeFalse();
        PlanOf(run).Should().Be((QueryIndex.Nothing, 0), "a center that is gone ends the plan at once");
    }

    [Fact]
    public void NearKeepsOnlyTheEntitiesInTheFrameOfItsCenter()
    {
        using var t = new TestWorld();
        // A ship at the origin of the root: an entity at its origin is at the center, but in another frame.
        FrameHandle ship = t.SpawnStaticFrame(TestWorld.At(0));
        t.World.SpawnWith(ship, new Crew());
        Entity outside = t.SpawnAt(new Double3(1, 0, 0), new Crew());

        Found(t.World.Snapshot.Find(EntityQuery.With<Crew>().Near(t.Root, default, 10))).Should().Equal(outside);
    }

    [Fact]
    public void TwoNearsBothNarrow()
    {
        using var t = new TestWorld();
        t.SpawnAt(new Double3(-4, 0, 0), new Metal());
        Entity between = t.SpawnAt(new Double3(5, 0, 0), new Metal());
        t.SpawnAt(new Double3(14, 0, 0), new Metal());

        EntityQuery query = EntityQuery.With<Metal>().Near(t.Root, default, 6).Near(t.Root, new Double3(10, 0, 0), 6);

        Found(t.World.Snapshot.Find(query)).Should().Equal(between);
    }

    // ---- Predicates ----------------------------------------------------------------------------------------------

    [Fact]
    public void WhereKeepsTheEntitiesThatPassThePredicate()
    {
        using var t = new TestWorld();
        t.SpawnAt(new Double3(1, 0, 0), new Metal());
        Entity far = t.SpawnAt(new Double3(9, 0, 0), new Metal());

        EntityQuery query = EntityQuery.With<Metal>().Where(static (snapshot, entity) => X(snapshot, entity) > 5);

        Found(t.World.Snapshot.Find(query)).Should().Equal(far);
    }

    [Fact]
    public void TwoWheresBothNarrow()
    {
        using var t = new TestWorld();
        t.SpawnAt(new Double3(1, 0, 0), new Metal());
        Entity middle = t.SpawnAt(new Double3(5, 0, 0), new Metal());
        t.SpawnAt(new Double3(9, 0, 0), new Metal());

        EntityQuery query = EntityQuery
            .Where(static (snapshot, entity) => X(snapshot, entity) > 2)
            .Where(static (snapshot, entity) => X(snapshot, entity) < 8);

        Found(t.World.Snapshot.Find(query)).Should().Equal(middle);
    }

    [Fact]
    public void TwoInsWithDifferentFramesMatchNothing()
    {
        using var t = new TestWorld();
        FrameHandle ship = t.SpawnStaticFrame(TestWorld.At(100));
        t.World.SpawnWith(ship, new Crew());

        t.World.Snapshot.Find(EntityQuery.In(ship).In(t.Root)).Any().Should().BeFalse();
    }

    // ---- Endings -------------------------------------------------------------------------------------------------

    [Fact]
    public void TheEndingsAgreeWithForeach()
    {
        using var t = new TestWorld();
        t.SpawnWith(new Wet());
        Entity first = t.SpawnWith(new Metal());
        Entity second = t.SpawnWith(new Metal());
        FoundEntities metal = t.World.Snapshot.Find(EntityQuery.With<Metal>());
        var copied = new List<Entity> { Entity.Null };

        metal.CopyTo(copied);

        metal.Count().Should().Be(2);
        metal.Any().Should().BeTrue();
        metal.TryFirst(out Entity found).Should().BeTrue();
        found.Should().Be(first);
        copied.Should().Equal(Entity.Null, first, second);
    }

    [Fact]
    public void ARunThatFindsNothingHasNoFirst()
    {
        using var t = new TestWorld();
        t.SpawnWith(new Wet());
        FoundEntities none = t.World.Snapshot.Find(EntityQuery.With<Metal>());

        none.Count().Should().Be(0);
        none.Any().Should().BeFalse();
        none.TryFirst(out Entity found).Should().BeFalse();
        found.Should().Be(Entity.Null);
    }

    // ---- The shorthand chain -------------------------------------------------------------------------------------

    [Fact]
    public void TheShorthandChainFindsWhatTheStoredQueryFinds()
    {
        using var t = new TestWorld();
        for (int i = 0; i < 20; i++)
            t.SpawnAt(new Double3(i, 0, 0), i % 2 == 0 ? new Metal() : new Wet());
        Snapshot snapshot = t.World.Snapshot;
        EntityQuery stored = EntityQuery.With<Metal>().Without<Wet>().Near(t.Root, new Double3(10, 0, 0), 5).In(t.Root);

        List<Entity> chain = Found(snapshot.Entities.With<Metal>().Without<Wet>().Near(t.Root, new Double3(10, 0, 0), 5).In(t.Root));

        chain.Should().Equal(Found(snapshot.Find(stored))).And.HaveCount(5);
    }

    [Fact]
    public void TheShorthandChainKeepsTheFiltersPastItsInlineOnesOnTheStack()
    {
        using var t = new TestWorld();
        for (int i = 0; i < 10; i++)
            t.SpawnAt(new Double3(i, 0, 0), new Metal());
        FoundEntities chain = t.World.Snapshot.Entities
            .Near(t.Root, default, 8).Near(t.Root, new Double3(9, 0, 0), 8)
            .Where(static (s, e) => X(s, e) != 4)
            .Where(static (s, e) => X(s, e) != 5)
            .In(t.Root).In(t.Root);

        chain.Count().Should().Be(6, "the entities at x 1 to 8, but not those at 4 and 5");
    }

    [Fact]
    public void TheShorthandChainBuildsNoHeapPartSoAComponentCanUseItDuringItsRun()
    {
        using var t = new TestWorld();
        t.SpawnAt(new Double3(1, 0, 0), new Metal());
        int count = -1;
        t.SpawnWith(Lambda.OnTick((self, scoped ref events, scoped ref outbox) =>
            count = self.Snapshot.Entities.With<Metal>().Near(5).Near(6).Where(static (s, e) => true).Where(static (s, e) => true)
                .In(self.Frame).In(self.Frame).Count()));

        t.World.Tick();

        t.World.Errors.Should().BeEmpty();
        count.Should().Be(1);
    }

    [Fact]
    public void TheShorthandChainHoldsAtMostEightFiltersPastItsInlineOnes()
    {
        using var t = new TestWorld();
        FoundEntities chain = t.World.Snapshot.Entities.Where(static (s, e) => true);
        for (int i = 0; i < StackFilters.Capacity; i++)
            chain = chain.Where(static (s, e) => true);

        InvalidOperationException? refused = null;
        try
        {
            chain.Where(static (s, e) => true);
        }
        catch (InvalidOperationException ex)
        {
            refused = ex;
        }

        refused.Should().NotBeNull();
        refused!.Message.Should().Match("*at most 8*static readonly EntityQuery*");
    }

    [Fact]
    public void TheShorthandChainKeepsItsFirstNearInlineSoNineNearsFitAndATenthIsRefused()
    {
        using var t = new TestWorld();
        FoundEntities chain = t.World.Snapshot.Entities.Near(t.Root, default, 1e6);
        for (int i = 0; i < StackFilters.Capacity; i++)
            chain = chain.Near(t.Root, default, 1e6);

        chain.Count().Should().Be(0, "the world has no entities, and nine Nears are not refused");
        InvalidOperationException? refused = null;
        try
        {
            chain.Near(t.Root, default, 1e6);
        }
        catch (InvalidOperationException ex)
        {
            refused = ex;
        }

        refused.Should().NotBeNull();
        refused!.Message.Should().Match("*at most 8*");
    }

    [Fact]
    public void FiltersAddedToTheRunOfAStoredQueryNarrowIt()
    {
        using var t = new TestWorld();
        t.SpawnAt(new Double3(1, 0, 0), new Metal(), new Wet());
        Entity dry = t.SpawnAt(new Double3(2, 0, 0), new Metal());

        Found(t.World.Snapshot.Find(EntityQuery.With<Metal>()).Without<Wet>()).Should().Equal(dry);
    }

    // ---- The index that a run starts from ------------------------------------------------------------------------

    [Fact]
    public void ARunStartsFromTheEntitiesOfItsRarestWithType()
    {
        using var t = new TestWorld();
        for (int i = 0; i < 20; i++)
            t.SpawnWith(new Metal());
        t.SpawnWith(new Metal(), new Crew());
        t.SpawnWith(new Metal(), new Crew());

        FoundEntities run = t.World.Snapshot.Find(EntityQuery.With<Metal>().With<Crew>());

        PlanOf(run).Should().Be((QueryIndex.Components, 2));
        run.Count().Should().Be(2);
    }

    [Fact]
    public void ARunStartsFromTheMembersOfAFrameWhenThatIsTheSmallerIndex()
    {
        using var t = new TestWorld();
        FrameHandle ship = t.SpawnStaticFrame(TestWorld.At(100));
        for (int i = 0; i < 10; i++)
            t.SpawnWith(new Crew());
        t.World.SpawnWith(ship, new Crew());

        PlanOf(t.World.Snapshot.Find(EntityQuery.With<Crew>().In(ship))).Should().Be((QueryIndex.Frame, 1));
    }

    [Fact]
    public void ARunWithASmallNearStartsFromTheGridCellsAroundTheCenterAndYieldsInEntityIndexOrder()
    {
        using var t = new TestWorld();
        Entity early = t.SpawnAt(new Double3(5000, 0, 0), new Metal());
        // Far apart along x, so that a small sphere touches few cells.
        for (int i = 0; i < 30; i++)
            t.SpawnAt(new Double3(1000 + i * 100, 0, 0), new Metal());
        Entity a = t.SpawnAt(new Double3(300, 0, 0), new Metal());
        Entity b = t.SpawnAt(new Double3(301, 0, 0), new Metal());
        // The new entity takes the slot of the early one, the lowest index, but its cell lists it last.
        t.World.Destroy(early);
        Entity c = t.SpawnAt(new Double3(302, 0, 0), new Metal());
        c.Index.Should().BeLessThan(a.Index);

        FoundEntities run = t.World.Snapshot.Find(EntityQuery.With<Metal>().Near(t.Root, new Double3(301, 0, 0), 2));

        PlanOf(run).Should().Be((QueryIndex.Grid, 3));
        Found(run).Should().Equal(c, a, b);
    }

    /// <summary>
    /// Metal entities far apart along x, and three close together at x = 300, 301 and 302. The entity at 302 takes the
    /// lowest slot index. Returns the three, in the order of their positions.
    /// </summary>
    private static (Entity At300, Entity At301, Entity At302) SpawnMetalClusterAmongFarEntities(TestWorld t)
    {
        Entity early = t.SpawnAt(new Double3(5000, 0, 0), new Metal());
        for (int i = 0; i < 30; i++)
            t.SpawnAt(new Double3(1000 + i * 100, 0, 0), new Metal());
        Entity at300 = t.SpawnAt(new Double3(300, 0, 0), new Metal());
        Entity at301 = t.SpawnAt(new Double3(301, 0, 0), new Metal());
        t.World.Destroy(early);
        Entity at302 = t.SpawnAt(new Double3(302, 0, 0), new Metal());
        return (at300, at301, at302);
    }

    [Fact]
    public void ARunWithASmallNearAroundAnEntityStartsFromTheGridCellsAroundItAndFindsEveryEntityWithinTheRadius()
    {
        using var t = new TestWorld();
        (Entity at300, Entity at301, Entity at302) = SpawnMetalClusterAmongFarEntities(t);

        FoundEntities run = t.World.Snapshot.Find(EntityQuery.With<Metal>().Near(at301, 2));

        PlanOf(run).Should().Be((QueryIndex.Grid, 3));
        Found(run).Should().Equal(at302, at300, at301);
    }

    [Fact]
    public void ARunWithASmallNearAroundTheAskerStartsFromTheGridCellsAroundItAndFindsEveryEntityWithinTheRadius()
    {
        using var t = new TestWorld();
        (Entity at300, Entity at301, Entity at302) = SpawnMetalClusterAmongFarEntities(t);
        var snapshot = new Snapshot(t.World, at301);

        FoundEntities run = snapshot.Find(EntityQuery.With<Metal>().Near(2));

        PlanOf(run).Should().Be((QueryIndex.Grid, 3));
        Found(run).Should().Equal(at302, at300, at301);
    }

    [Fact]
    public void ARunWithANearLargerThanTheOccupiedGridStartsFromTheMembersOfTheFrame()
    {
        using var t = new TestWorld();
        for (int i = 0; i < 4; i++)
            t.SpawnAt(new Double3(i, 0, 0), new Metal());
        int members = t.World.Snapshot.Find(EntityQuery.In(t.Root)).Count();

        PlanOf(t.World.Snapshot.Find(EntityQuery.Near(t.Root, default, 1e6))).Should().Be((QueryIndex.Frame, members));
    }

    [Fact]
    public void ARunConsidersTheIndexesOfTheFiltersInTheHeapPartAndOnTheStack()
    {
        using var t = new TestWorld();
        FrameHandle ship = t.SpawnStaticFrame(TestWorld.At(100));
        for (int i = 0; i < 6; i++)
            t.SpawnWith(new Crew());
        Entity aboard = t.World.SpawnWith(ship, new Crew());

        // The second In, the smallest index, sits in the heap part of the stored query and on the stack of the chain.
        FoundEntities stored = t.World.Snapshot.Find(EntityQuery.With<Crew>().In(t.Root).In(ship));
        FoundEntities chain = t.World.Snapshot.Entities.With<Crew>().In(t.Root).In(ship);

        PlanOf(stored).Should().Be((QueryIndex.Frame, 1));
        PlanOf(chain).Should().Be((QueryIndex.Frame, 1));
        stored.Any().Should().BeFalse("no entity is in both frames");
        aboard.IsNull.Should().BeFalse();
    }

    [Fact]
    public void ARunWhoseFiltersHaveNoIndexScansEveryLiveEntity()
    {
        using var t = new TestWorld();
        t.SpawnWith(new Metal());
        t.SpawnWith(new Wet());

        PlanOf(t.World.Snapshot.Find(EntityQuery.Without<Wet>().Where(static (s, e) => true))).Should().Be((QueryIndex.All, t.World.Stats.LiveEntities));
    }

    [Fact]
    public void ARunWithANearAroundAnEntityThatIsNotLiveHasNoCandidates()
    {
        using var t = new TestWorld();
        Entity center = t.SpawnWith(new Metal());
        t.World.Destroy(center);

        PlanOf(t.World.Snapshot.Find(EntityQuery.Near(center, 10))).Should().Be((QueryIndex.Nothing, 0));
    }

    [Fact]
    public void ARunTakesTheSmallestOfEveryIndexThatItsFiltersOffer()
    {
        using var t = new TestWorld();
        FrameHandle ship = t.SpawnStaticFrame(TestWorld.At(1e5));
        for (int i = 0; i < 5; i++)
            t.World.SpawnWith(ship, new Crew());
        for (int i = 0; i < 3; i++)
            t.SpawnWith(new Dead());
        t.World.SpawnWith(ship, new Dead());

        // Dead: 4 entities, the ship's members: 6, so the run starts from the Dead entities.
        FoundEntities run = t.World.Snapshot.Find(EntityQuery.With<Dead>().In(ship).Where(static (s, e) => true));

        PlanOf(run).Should().Be((QueryIndex.Components, 4));
        run.Count().Should().Be(1);
    }

    // ---- Many workers --------------------------------------------------------------------------------------------

    [Fact]
    public void ManyWorkersRunQueriesAtOnceWhileTheIndexesChangeAndGetTheResultsOfOneWorker()
    {
        // 300 entities on a line. Each tick, each counts the Flammable entities near it with a stored query and a chain,
        // and some toggle their own Flammable and move, so that the component index and the grid change every tick.
        List<long> Run(int workers)
        {
            using var t = new TestWorld(maxDegreeOfParallelism: workers, chunkSize: 4);
            var results = new List<long>();
            for (int i = 0; i < 300; i++)
            {
                int me = i;
                t.SpawnAt(new Double3(i * 0.5, 0, 0), Lambda.OnTick((self, scoped ref events, scoped ref outbox) =>
                {
                    int stored = self.Snapshot.Find(NearbyFlammable).Count();
                    int chain = self.Snapshot.Entities.With<Flammable>().Near(4).Where(static (s, e) => true).Count();
                    lock (results)
                        results.Add(self.Tick * 10_000_000 + me * 10_000 + stored * 100 + chain);
                    if ((self.Tick + me) % 4 == 0)
                    {
                        if (self.GetComponent<Flammable>() is { } flammable)
                            self.RemoveComponent(flammable);
                        else
                            self.AddComponent(new Flammable());
                        self.SetTransform(self.PoseIn(self.Frame) with { Position = self.PoseIn(self.Frame).Position + new Double3(0, 0.7, 0) });
                    }
                }));
            }
            t.World.Run(10);
            t.World.Errors.Should().BeEmpty();
            results.Sort();
            return results;
        }

        List<long> one = Run(1);
        one.Should().Contain(r => r % 100 > 0, "some entity found Flammable neighbors, so the test measures something");
        Run(8).Should().Equal(one);
    }

    private static readonly EntityQuery NearbyFlammable = EntityQuery.With<Flammable>().Near(3);
}
