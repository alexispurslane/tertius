using AwesomeAssertions;

namespace Tertius.Simulation.Tests;

/// <summary>
/// How an entity query is built: what sits inline, what goes into the heap part, and the queries that building refuses.
/// The heap part cannot be built during an entity's run, so the world builds the static query fields of each component
/// type before the type's first run.
/// </summary>
public class EntityQueryBuildTests
{
    private static bool Always(Snapshot snapshot, Entity entity) => true;

    [Fact]
    public void AQueryWhoseWithAndWithoutShareATypeThrowsWhenBuilt()
    {
        Action withThenWithout = () => EntityQuery.With<Flammable>().Without<Flammable>();
        Action withoutThenWith = () => EntityQuery.Without<Wet>().With<Wet>();

        withThenWithout.Should().Throw<InvalidOperationException>().WithMessage("*already needs Flammable*");
        withoutThenWith.Should().Throw<InvalidOperationException>().WithMessage("*already refuses Wet*");
    }

    [Fact]
    public void TheStaticStartsBuildTheSameQueryAsAChainFromTheDefaultQuery()
    {
        EntityQuery.With<Crew>().Should().Be(default(EntityQuery).With<Crew>());
        EntityQuery.Without<Dead>().Should().Be(default(EntityQuery).Without<Dead>());
        EntityQuery.InAny(CollisionGroup.Solid).Should().Be(default(EntityQuery).InAny(CollisionGroup.Solid));
        EntityQuery.Near(5).Should().Be(default(EntityQuery).Near(5));
    }

    [Fact]
    public void ComponentFiltersJoinTheComponentMaskAndCollisionFiltersTheCollisionMask()
    {
        var registry = new CollisionGroupRegistry();
        CollisionGroup wall = registry.Register("Wall");
        CollisionGroup person = registry.Register("Person");

        EntityQuery query = EntityQuery.With<Crew>().Without<Dead>().InAll(CollisionGroup.Solid).InNone(wall)
            .InAny(wall).InAny(person);

        query.Components.With.Should().Be(ComponentFlags.Of([new Crew()]));
        query.Components.Without.Should().Be(ComponentFlags.Of([new Dead()]));
        query.Collision.Should()
            .Be(CollisionMask.Empty.InAll(CollisionGroup.Solid).InNone(wall).InAny(wall).InAny(person));
    }

    [Fact]
    public void OneInOneNearAndOneWhereSitInlineWithNoHeapPart()
    {
        EntityQuery query = EntityQuery.In(new(new(1, 1), 0, 1)).Near(3).Where(static (s, e) => true);

        query.Extras.Should().BeNull();
        query.InFrame.IsNull.Should().BeFalse();
        query.NearFilter.Should().Be(new QueryFilter(new NearAsker(3)));
        query.WherePredicate.Should().NotBeNull();
    }

    [Fact]
    public void ASecondInNearOrWhereGoesIntoTheHeapPartInTheOrderOfTheCalls()
    {
        var frame = new FrameHandle(new(1, 1), 0, 1);

        EntityQuery query = EntityQuery.Where(Always).Near(3).In(frame).Where(Always).Near(new(2, 1), 4).In(frame);

        query.Extras!.Filters.ToArray().Should().Equal(QueryFilter.Where(Always), QueryFilter.Near(new(2, 1), 4),
            QueryFilter.In(frame));
    }

    [Fact]
    public void NearRefusesARadiusThatIsNegativeOrNotFiniteAndACenterThatIsNull()
    {
        Action negative = () => EntityQuery.Near(-1);
        Action infinite = () => EntityQuery.Near(double.PositiveInfinity);
        Action nan = () => EntityQuery.Near(double.NaN);
        Action nullEntity = () => EntityQuery.Near(Entity.Null, 1);
        Action nullFrame = () => EntityQuery.Near(FrameHandle.Null, default, 1);
        Action badPoint = () => EntityQuery.Near(new(new(1, 1), 0, 1), new(double.NaN, 0, 0), 1);

        negative.Should().Throw<ArgumentOutOfRangeException>();
        infinite.Should().Throw<ArgumentOutOfRangeException>();
        nan.Should().Throw<ArgumentOutOfRangeException>();
        nullEntity.Should().Throw<ArgumentException>();
        nullFrame.Should().Throw<ArgumentException>();
        badPoint.Should().Throw<ArgumentException>();
        EntityQuery.Near(0).NearFilter.Should().Be(new QueryFilter(new NearAsker(0)));
    }

    [Fact]
    public void InRefusesTheNullFrameAndWhereANullPredicate()
    {
        Action inNull = () => EntityQuery.In(FrameHandle.Null);
        Action whereNull = () => EntityQuery.Where(null!);

        inNull.Should().Throw<ArgumentException>();
        whereNull.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void QueriesWithEqualFiltersAreEqualHeapPartsIncluded()
    {
        EntityQuery a = EntityQuery.With<Crew>().Where(Always).Where(Always);
        EntityQuery b = EntityQuery.With<Crew>().Where(Always).Where(Always);

        a.Should().Be(b);
        a.Should().NotBe(EntityQuery.With<Crew>().Where(Always));
    }

    // ---- The heap part and the tick ------------------------------------------------------------------------------

    [Fact]
    public void BuildingTheHeapPartDuringAnEntityRunThrowsAndSaysToBuildTheQueryOnceInAStaticField()
    {
        using var t = new TestWorld();
        t.SpawnWith(Lambda.OnTick(static (self, scoped ref events, scoped ref outbox) =>
            _ = EntityQuery.Where(Always).Where(Always)));

        t.World.Tick();

        t.World.Errors.Should().ContainSingle().Which.Should().Contain("static readonly field");
    }

    [Fact]
    public void TheBanEndsWithTheEntityRunSoTheTickingThreadCanBuildTheHeapPartAfterATick()
    {
        // One worker, so the entity runs on this thread.
        using var t = new TestWorld();
        t.SpawnWith(Lambda.OnTick(static (self, scoped ref events, scoped ref outbox) => { }));
        t.World.Tick();

        Action build = () => _ = EntityQuery.Where(Always).Where(Always);

        build.Should().NotThrow();
    }

    /// <summary>A component type whose class constructor fails. Only its Handle call reads the static field.</summary>
    private sealed class BrokenStatics : IComponent
    {
        private static readonly int Broken = int.Parse("not a number");

        public int Priority => 0;
        public EventMask Wants => default;

        public void Handle(Self self, scoped ref EventList events, scoped ref Outbox outbox) => _ = Broken;
    }

    [Fact]
    public void AClassConstructorThatFailsIsReportedAndTheSpawnGoesOn()
    {
        using var t = new TestWorld();

        Entity entity = t.SpawnWith(new BrokenStatics());

        t.World.IsAlive(entity).Should().BeTrue();
        t.World.Errors.Should().ContainSingle().Which.Should().Contain("class constructor").And
            .Contain(nameof(BrokenStatics));
    }

    [Fact]
    public void AnInlineQueryBuiltDuringAnEntityRunIsAllowed()
    {
        using var t = new TestWorld();
        t.SpawnWith(Lambda.OnTick(static (self, scoped ref events, scoped ref outbox) =>
            _ = EntityQuery.With<Crew>().Near(2).Where(Always).In(self.Frame)));

        t.World.Tick();

        t.World.Errors.Should().BeEmpty();
    }

    /// <summary>
    /// A component type whose static field holds a query with a heap part. C# builds the field at the first use of the
    /// class, which would be the first Handle call, during a tick, if the world did not run the class constructor first.
    /// </summary>
    private sealed class CountsCrewTwice : IComponent
    {
        private static readonly EntityQuery CrewTwice = EntityQuery.With<Crew>().Where(Always).Where(Always);

        public int Found = -1;
        public int Priority => 0;
        public EventMask Wants => EventMask.Of<Tick>();

        public void Handle(Self self, scoped ref EventList events, scoped ref Outbox outbox) =>
            Found = self.Snapshot.Find(CrewTwice).Count();
    }

    /// <summary>The same as <see cref="CountsCrewTwice"/>, as a second type whose class constructor has not run yet.</summary>
    private sealed class CountsCrewTwiceWhenAdded : IComponent
    {
        private static readonly EntityQuery CrewTwice = EntityQuery.With<Crew>().Where(Always).Where(Always);

        public int Found = -1;
        public int Priority => 0;
        public EventMask Wants => EventMask.Of<Tick>();

        public void Handle(Self self, scoped ref EventList events, scoped ref Outbox outbox) =>
            Found = self.Snapshot.Find(CrewTwice).Count();
    }

    [Fact]
    public void TheWorldBuildsTheStaticQueryFieldsOfAComponentTypeAtItsSpawnBeforeItsFirstRun()
    {
        using var t = new TestWorld();
        t.SpawnWith(new Crew());
        var counter = new CountsCrewTwice();
        t.SpawnWith(counter);

        t.World.Tick();

        t.World.Errors.Should().BeEmpty();
        counter.Found.Should().Be(1);
    }

    [Fact]
    public void TheWorldBuildsTheStaticQueryFieldsOfAComponentTypeAddedDuringATickAtTheSyncPoint()
    {
        using var t = new TestWorld();
        t.SpawnWith(new Crew());
        var counter = new CountsCrewTwiceWhenAdded();
        bool added = false;
        t.SpawnWith(Lambda.OnTick((self, scoped ref events, scoped ref outbox) =>
        {
            if (!added)
                self.AddComponent(counter);
            added = true;
        }));

        t.World.Run(2);

        t.World.Errors.Should().BeEmpty();
        counter.Found.Should().Be(1);
    }
}
