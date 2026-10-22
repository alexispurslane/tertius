using AwesomeAssertions;

namespace Tertius.Simulation.Tests;

/// <summary>
/// <see cref="QueryFilter"/> as a custom C# 15 union of five cases: <see cref="InFilter"/>, <see cref="NearAsker"/>,
/// <see cref="NearEntity"/>, <see cref="NearPoint"/> and <see cref="WhereFilter"/>. The default value holds no case and
/// passes every entity. These tests pin what a union promises: each case goes in through its constructor and comes out
/// through its <c>TryGetValue</c>, no other <c>TryGetValue</c> answers true, equality needs the same case and the same
/// fields, and a <c>switch</c> over the filter takes the arm of the case that it holds. The tests of <c>Passes</c> pin
/// what each case keeps; the query tests in <see cref="EntityQueryRunTests"/> pin the same through whole queries.
/// </summary>
public class QueryFilterUnionTests
{
    private static readonly FrameHandle Ship = new(new(1, 1), 0, 1);
    private static readonly FrameHandle Station = new(new(2, 1), 1, 1);
    private static readonly Entity Rock = new(7, 1);
    private static readonly Entity Moon = new(8, 1);
    private static readonly Double3 Here = new(1, 2, 3);
    private static readonly Double3 There = new(1, 2, 4);

    private static bool Always(Snapshot snapshot, Entity entity) => true;

    private static bool Never(Snapshot snapshot, Entity entity) => false;

    /// <summary>The names of the cases whose <c>TryGetValue</c> answers true for the filter.</summary>
    private static string[] CasesHeld(QueryFilter filter)
    {
        var held = new List<string>();
        if (filter.TryGetValue(out InFilter _))
            held.Add(nameof(InFilter));
        if (filter.TryGetValue(out NearAsker _))
            held.Add(nameof(NearAsker));
        if (filter.TryGetValue(out NearEntity _))
            held.Add(nameof(NearEntity));
        if (filter.TryGetValue(out NearPoint _))
            held.Add(nameof(NearPoint));
        if (filter.TryGetValue(out WhereFilter _))
            held.Add(nameof(WhereFilter));
        return [.. held];
    }

    /// <summary>The value that each <c>TryGetValue</c> hands out, whatever it answers.</summary>
    private static (InFilter In, NearAsker Asker, NearEntity Entity, NearPoint Point, WhereFilter Where) Views(
        QueryFilter filter)
    {
        filter.TryGetValue(out InFilter inFilter);
        filter.TryGetValue(out NearAsker asker);
        filter.TryGetValue(out NearEntity entity);
        filter.TryGetValue(out NearPoint point);
        filter.TryGetValue(out WhereFilter where);
        return (inFilter, asker, entity, point, where);
    }

    private static string ArmOf(QueryFilter filter) => filter switch
    {
        InFilter(var frame) => $"in {frame.Index}",
        NearAsker(var radius) => $"asker {radius}",
        NearEntity(var other, var radius) => $"entity {other.Index} {radius}",
        NearPoint(var frame, var point, var radius) => $"point {frame.Index} {point.Z} {radius}",
        WhereFilter(var predicate) => $"where {predicate(default, default)}",
        null => "none"
    };

    /// <summary>One filter of each case, each with fields that no other filter shares.</summary>
    private static QueryFilter[] OneOfEachCase() =>
    [
        new(new InFilter(Ship)),
        new(new NearAsker(5)),
        new(new NearEntity(Rock, 6)),
        new(new NearPoint(Station, Here, 7)),
        new(new WhereFilter(Always))
    ];

    // ---- The default value ---------------------------------------------------------------------------------------

    [Fact]
    public void TheDefaultFilterHoldsNoCase()
    {
        QueryFilter filter = default;

        filter.HasValue.Should().BeFalse();
        filter.Value.Should().BeNull();
        CasesHeld(filter).Should().BeEmpty();
    }

    [Fact]
    public void TheDefaultFilterPassesEveryEntity()
    {
        using var t = new TestWorld();
        FrameHandle ship = t.SpawnStaticFrame(TestWorld.At(100));
        Entity inRoot = t.SpawnAt(new(1e6, 0, 0));
        Entity inShip = t.World.SpawnAt(ship, new(0, 5, 0));
        Snapshot snapshot = t.World.Snapshot;

        foreach (Entity entity in new[] { inRoot, inShip })
            default(QueryFilter).Passes(snapshot, entity, t.World.Entities.FrontRow(entity)).Should().BeTrue();
    }

    // ---- One constructor and one TryGetValue per case ------------------------------------------------------------

    [Fact]
    public void AnInFilterComesBackThroughItsTryGetValueAndNoOtherCaseAnswers()
    {
        var filter = new QueryFilter(new InFilter(Ship));

        filter.HasValue.Should().BeTrue();
        filter.TryGetValue(out InFilter held).Should().BeTrue();
        held.Should().Be(new InFilter(Ship));
        CasesHeld(filter).Should().Equal(nameof(InFilter));
    }

    [Fact]
    public void ANearAskerComesBackThroughItsTryGetValueAndNoOtherCaseAnswers()
    {
        var filter = new QueryFilter(new NearAsker(5));

        filter.HasValue.Should().BeTrue();
        filter.TryGetValue(out NearAsker held).Should().BeTrue();
        held.Should().Be(new NearAsker(5));
        CasesHeld(filter).Should().Equal(nameof(NearAsker));
    }

    [Fact]
    public void ANearEntityComesBackThroughItsTryGetValueAndNoOtherCaseAnswers()
    {
        var filter = new QueryFilter(new NearEntity(Rock, 6));

        filter.HasValue.Should().BeTrue();
        filter.TryGetValue(out NearEntity held).Should().BeTrue();
        held.Should().Be(new NearEntity(Rock, 6));
        CasesHeld(filter).Should().Equal(nameof(NearEntity));
    }

    [Fact]
    public void ANearPointComesBackThroughItsTryGetValueAndNoOtherCaseAnswers()
    {
        var filter = new QueryFilter(new NearPoint(Station, Here, 7));

        filter.HasValue.Should().BeTrue();
        filter.TryGetValue(out NearPoint held).Should().BeTrue();
        held.Should().Be(new NearPoint(Station, Here, 7));
        CasesHeld(filter).Should().Equal(nameof(NearPoint));
    }

    [Fact]
    public void AWhereFilterComesBackThroughItsTryGetValueAndNoOtherCaseAnswers()
    {
        var filter = new QueryFilter(new WhereFilter(Always));

        filter.HasValue.Should().BeTrue();
        filter.TryGetValue(out WhereFilter held).Should().BeTrue();
        held.Predicate.Should().Be((EntityPredicate)Always);
        CasesHeld(filter).Should().Equal(nameof(WhereFilter));
    }

    [Fact]
    public void ATryGetValueThatAnswersFalseHandsOutTheDefaultValueNotTheFieldsOfAnotherCase()
    {
        // Every field of the point filter is set, so a case that read a shared field without checking would show it.
        Views(new(new NearPoint(Station, Here, 7))).Should().Be(
            (default, default, default, new(Station, Here, 7), default));
        Views(new(new NearEntity(Rock, 6))).Should().Be(
            (default, default, new(Rock, 6), default, default));
        Views(new(new InFilter(Ship))).Should().Be(
            (new(Ship), default, default, default, default));
        Views(default).Should().Be(
            (default, default, default, default, default));
    }

    [Fact]
    public void ValueBoxesTheCaseThatTheFilterHolds()
    {
        new QueryFilter(new InFilter(Ship)).Value.Should().Be(new InFilter(Ship));
        new QueryFilter(new NearAsker(5)).Value.Should().Be(new NearAsker(5));
        new QueryFilter(new NearEntity(Rock, 6)).Value.Should().Be(new NearEntity(Rock, 6));
        new QueryFilter(new NearPoint(Station, Here, 7)).Value.Should().Be(new NearPoint(Station, Here, 7));
        new QueryFilter(new WhereFilter(Always)).Value.Should().Be(new WhereFilter(Always));
    }

    [Fact]
    public void EveryCaseConvertsImplicitlyToTheFilterThatItsConstructorBuilds()
    {
        QueryFilter fromIn = new InFilter(Ship);
        QueryFilter fromAsker = new NearAsker(5);
        QueryFilter fromEntity = new NearEntity(Rock, 6);
        QueryFilter fromPoint = new NearPoint(Station, Here, 7);
        QueryFilter fromWhere = new WhereFilter(Always);

        fromIn.Value.Should().Be(new InFilter(Ship));
        fromAsker.Value.Should().Be(new NearAsker(5));
        fromEntity.Value.Should().Be(new NearEntity(Rock, 6));
        fromPoint.Value.Should().Be(new NearPoint(Station, Here, 7));
        fromWhere.Value.Should().Be(new WhereFilter(Always));
        fromIn.Should().Be(new QueryFilter(new InFilter(Ship)));
        fromAsker.Should().Be(new QueryFilter(new NearAsker(5)));
        fromEntity.Should().Be(new QueryFilter(new NearEntity(Rock, 6)));
        fromPoint.Should().Be(new QueryFilter(new NearPoint(Station, Here, 7)));
        fromWhere.Should().Be(new QueryFilter(new WhereFilter(Always)));
    }

    [Fact]
    public void TheStaticFactoriesBuildTheCaseThatTheirNameSays()
    {
        QueryFilter.In(Ship).Should().Be(new QueryFilter(new InFilter(Ship)));
        QueryFilter.Near(5).Should().Be(new QueryFilter(new NearAsker(5)));
        QueryFilter.Near(Rock, 6).Should().Be(new QueryFilter(new NearEntity(Rock, 6)));
        QueryFilter.Near(Station, Here, 7).Should().Be(new QueryFilter(new NearPoint(Station, Here, 7)));
        QueryFilter.Where(Always).Should().Be(new QueryFilter(new WhereFilter(Always)));
    }

    // ---- The constructors check the invariants of the cases ------------------------------------------------------

    [Fact]
    public void EveryConstructorRefusesACaseThatBreaksTheInvariantsOfItsRecord()
    {
        var refused = new (string Why, Action Build, Type Exception)[]
        {
            ("In with the null frame", () => _ = new QueryFilter(new InFilter(FrameHandle.Null)),
                typeof(ArgumentException)),
            ("NearAsker with a negative radius", () => _ = new QueryFilter(new NearAsker(-1)),
                typeof(ArgumentOutOfRangeException)),
            ("NearAsker with a NaN radius", () => _ = new QueryFilter(new NearAsker(double.NaN)),
                typeof(ArgumentOutOfRangeException)),
            ("NearAsker with an infinite radius", () => _ = new QueryFilter(new NearAsker(double.PositiveInfinity)),
                typeof(ArgumentOutOfRangeException)),
            ("NearEntity with the null center", () => _ = new QueryFilter(new NearEntity(Entity.Null, 1)),
                typeof(ArgumentException)),
            ("NearEntity with a negative radius", () => _ = new QueryFilter(new NearEntity(Rock, -1)),
                typeof(ArgumentOutOfRangeException)),
            ("NearPoint with the null frame", () => _ = new QueryFilter(new NearPoint(FrameHandle.Null, Here, 1)),
                typeof(ArgumentException)),
            ("NearPoint with a NaN x", () => _ = new QueryFilter(new NearPoint(Station, new(double.NaN, 0, 0), 1)),
                typeof(ArgumentException)),
            ("NearPoint with an infinite y",
                () => _ = new QueryFilter(new NearPoint(Station, new(0, double.PositiveInfinity, 0), 1)),
                typeof(ArgumentException)),
            ("NearPoint with an infinite z",
                () => _ = new QueryFilter(new NearPoint(Station, new(0, 0, double.NegativeInfinity), 1)),
                typeof(ArgumentException)),
            ("NearPoint with a negative radius", () => _ = new QueryFilter(new NearPoint(Station, Here, -1)),
                typeof(ArgumentOutOfRangeException)),
            ("Where with a null predicate", () => _ = new QueryFilter(new WhereFilter(null!)),
                typeof(ArgumentNullException))
        };

        foreach ((string why, Action build, Type exception) in refused)
            build.Should().Throw<ArgumentException>(why).Which.Should().BeOfType(exception, why);
    }

    [Fact]
    public void EveryImplicitConversionRefusesWhatItsConstructorRefuses()
    {
        // The default value of each case record breaks its invariants, except NearAsker, whose radius 0 is valid.
        Action fromIn = () =>
        {
            QueryFilter filter = default(InFilter);
        };
        Action fromAsker = () =>
        {
            QueryFilter filter = new NearAsker(double.NaN);
        };
        Action fromEntity = () =>
        {
            QueryFilter filter = default(NearEntity);
        };
        Action fromPoint = () =>
        {
            QueryFilter filter = default(NearPoint);
        };
        Action fromWhere = () =>
        {
            QueryFilter filter = default(WhereFilter);
        };

        fromIn.Should().Throw<ArgumentException>();
        fromAsker.Should().Throw<ArgumentOutOfRangeException>();
        fromEntity.Should().Throw<ArgumentException>();
        fromPoint.Should().Throw<ArgumentException>();
        fromWhere.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void EveryConstructorAcceptsTheBoundaryOfItsInvariants()
    {
        // A radius of exactly 0 and a point at the origin are valid.
        Action build = () =>
        {
            _ = new QueryFilter(new NearAsker(0));
            _ = new QueryFilter(new NearEntity(Rock, 0));
            _ = new QueryFilter(new NearPoint(Station, Double3.Zero, 0));
        };

        build.Should().NotThrow();
    }

    [Fact]
    public void TheFactoriesNameTheArgumentThatTheCallerGotWrong()
    {
        // EntityQuery and FoundEntities pass their arguments straight to these factories, so their callers see these names.
        ((Action)(() => QueryFilter.In(FrameHandle.Null))).Should().Throw<ArgumentException>()
            .WithParameterName("frame");
        ((Action)(() => QueryFilter.Near(-1))).Should().Throw<ArgumentOutOfRangeException>()
            .WithParameterName("radius");
        ((Action)(() => QueryFilter.Near(Entity.Null, 1))).Should().Throw<ArgumentException>()
            .WithParameterName("other");
        ((Action)(() => QueryFilter.Near(Rock, -1))).Should().Throw<ArgumentOutOfRangeException>()
            .WithParameterName("radius");
        ((Action)(() => QueryFilter.Near(FrameHandle.Null, Here, 1))).Should().Throw<ArgumentException>()
            .WithParameterName("frame");
        ((Action)(() => QueryFilter.Near(Station, new(double.NaN, 0, 0), 1))).Should().Throw<ArgumentException>()
            .WithParameterName("point");
        ((Action)(() => QueryFilter.Near(Station, Here, -1))).Should().Throw<ArgumentOutOfRangeException>()
            .WithParameterName("radius");
        ((Action)(() => QueryFilter.Where(null!))).Should().Throw<ArgumentNullException>()
            .WithParameterName("predicate");
    }

    // ---- Pattern matching ----------------------------------------------------------------------------------------

    [Fact]
    public void ASwitchOverAFilterTakesTheArmOfTheCaseItHoldsAndTheLastArmForTheDefault()
    {
        ArmOf(new(new InFilter(Ship))).Should().Be("in 0");
        ArmOf(new(new NearAsker(5))).Should().Be("asker 5");
        ArmOf(new(new NearEntity(Rock, 6))).Should().Be("entity 7 6");
        ArmOf(new(new NearPoint(Station, Here, 7))).Should().Be("point 1 3 7");
        ArmOf(new(new WhereFilter(Never))).Should().Be("where False");
        ArmOf(default).Should().Be("none");
    }

    // ---- Equality ------------------------------------------------------------------------------------------------

    [Fact]
    public void TwoFiltersOfTheSameCaseWithTheSameFieldsAreEqualAndHashAlike()
    {
        QueryFilter[] first = OneOfEachCase();
        QueryFilter[] second = OneOfEachCase();

        for (int i = 0; i < first.Length; i++)
        {
            first[i].Equals(second[i]).Should().BeTrue($"case {i}");
            first[i].Equals((object)second[i]).Should().BeTrue($"case {i} as an object");
            (first[i] == second[i]).Should().BeTrue($"case {i} by ==");
            (first[i] != second[i]).Should().BeFalse($"case {i} by !=");
            first[i].GetHashCode().Should().Be(second[i].GetHashCode(), $"case {i}");
        }

        default(QueryFilter).Should().Be(default(QueryFilter));
        default(QueryFilter).GetHashCode().Should().Be(default(QueryFilter).GetHashCode());
    }

    [Fact]
    public void TwoFiltersOfTheSameCaseThatDifferInOneFieldAreNotEqual()
    {
        var inShip = new QueryFilter(new InFilter(Ship));
        var asker = new QueryFilter(new NearAsker(5));
        var entity = new QueryFilter(new NearEntity(Rock, 6));
        var point = new QueryFilter(new NearPoint(Station, Here, 7));
        var where = new QueryFilter(new WhereFilter(Always));

        inShip.Should().NotBe(new QueryFilter(new InFilter(Station)));
        asker.Should().NotBe(new QueryFilter(new NearAsker(5.5)));
        entity.Should().NotBe(new QueryFilter(new NearEntity(Moon, 6)), "another center");
        entity.Should().NotBe(new QueryFilter(new NearEntity(Rock, 6.5)), "another radius");
        point.Should().NotBe(new QueryFilter(new NearPoint(Ship, Here, 7)), "another frame");
        point.Should().NotBe(new QueryFilter(new NearPoint(Station, There, 7)), "another point");
        point.Should().NotBe(new QueryFilter(new NearPoint(Station, Here, 7.5)), "another radius");
        where.Should().NotBe(new QueryFilter(new WhereFilter(Never)));
        (inShip != new QueryFilter(new InFilter(Station))).Should().BeTrue();
    }

    [Fact]
    public void FiltersOfDifferentCasesAreNotEqualEvenWhenTheirFieldsMatch()
    {
        // Each pair shares every field that both cases have, so only the case can tell them apart.
        new QueryFilter(new InFilter(Ship)).Should().NotBe(new QueryFilter(new NearPoint(Ship, default, 0)));
        new QueryFilter(new NearAsker(5)).Should().NotBe(new QueryFilter(new NearEntity(Rock, 5)));
        new QueryFilter(new NearEntity(Rock, 5)).Should().NotBe(new QueryFilter(new NearPoint(Station, Here, 5)));
        new QueryFilter(new NearAsker(5)).Should().NotBe(new QueryFilter(new NearPoint(Station, Here, 5)));
    }

    [Fact]
    public void AFilterWithACaseIsNeverEqualToTheEmptyFilterEvenWhenEveryFieldIsZero()
    {
        // NearAsker is the one case whose fields can all be zero. The other cases would need the null frame, the null
        // center or the null predicate for that, and their constructors refuse those.
        QueryFilter empty = default;

        new QueryFilter(new NearAsker(0)).Should().NotBe(empty);
        (empty != new QueryFilter(new NearAsker(0))).Should().BeTrue();
    }

    [Fact]
    public void AFilterIsNotEqualToAnObjectThatIsNotAFilter()
    {
        new QueryFilter(new NearAsker(5)).Equals((object)new NearAsker(5)).Should().BeFalse();
        new QueryFilter(new NearAsker(5)).Equals(null).Should().BeFalse();
    }

    // ---- ToString ------------------------------------------------------------------------------------------------

    [Fact]
    public void ToStringNamesTheCaseAndItsFieldsAndSaysNoneForTheDefault()
    {
        default(QueryFilter).ToString().Should().Be("QueryFilter(none)");
        new QueryFilter(new InFilter(Ship)).ToString().Should().StartWith("QueryFilter(").And.Contain(nameof(InFilter));
        new QueryFilter(new NearAsker(5)).ToString().Should().Contain(nameof(NearAsker)).And.Contain("5");
        new QueryFilter(new NearEntity(Rock, 6)).ToString().Should().Contain(nameof(NearEntity)).And.Contain("6");
        new QueryFilter(new NearPoint(Station, Here, 7)).ToString().Should().Contain(nameof(NearPoint)).And
            .Contain("7");
        new QueryFilter(new WhereFilter(Always)).ToString().Should().Contain(nameof(WhereFilter));
    }

    // ---- What Passes keeps, case by case -------------------------------------------------------------------------

    [Fact]
    public void AnInFilterPassesTheEntitiesOfItsFrameOnly()
    {
        using var t = new TestWorld();
        FrameHandle ship = t.SpawnStaticFrame(TestWorld.At(100));
        Entity aboard = t.World.SpawnAt(ship, new(1, 0, 0));
        Entity outside = t.SpawnAt(new(1, 0, 0));
        QueryFilter filter = QueryFilter.In(ship);

        PassesOf(t, filter, aboard).Should().BeTrue();
        PassesOf(t, filter, outside).Should().BeFalse();
    }

    [Fact]
    public void ANearAskerPassesTheEntitiesOfTheAskersFrameWithinTheRadiusOfTheAskerAndTheBoundaryCounts()
    {
        using var t = new TestWorld();
        FrameHandle ship = t.SpawnStaticFrame(TestWorld.At(100));
        Entity asker = t.SpawnAt(new(10, 0, 0));
        Entity close = t.SpawnAt(new(13, 0, 0));
        Entity onTheEdge = t.SpawnAt(new(15, 0, 0));
        Entity far = t.SpawnAt(new(16, 0, 0));
        Entity otherFrame = t.World.SpawnAt(ship, new(11, 0, 0));
        QueryFilter filter = QueryFilter.Near(5);

        PassesOf(t, filter, close, asker).Should().BeTrue();
        PassesOf(t, filter, asker, asker).Should().BeTrue("the asker is at distance 0 from itself");
        PassesOf(t, filter, onTheEdge, asker).Should().BeTrue();
        PassesOf(t, filter, far, asker).Should().BeFalse();
        PassesOf(t, filter, otherFrame, asker).Should().BeFalse("it is close in numbers but in another frame");
    }

    [Fact]
    public void ANearAskerOnASnapshotWithoutAnAskerPassesNothing()
    {
        using var t = new TestWorld();
        Entity entity = t.SpawnAt(new(1, 0, 0));

        PassesOf(t, QueryFilter.Near(5), entity).Should().BeFalse();
    }

    [Fact]
    public void ANearEntityPassesTheEntitiesWithinTheRadiusOfThatEntityInItsFrameAndNothingWhenItIsDestroyed()
    {
        using var t = new TestWorld();
        Entity center = t.SpawnAt(new(10, 0, 0));
        Entity close = t.SpawnAt(new(12, 0, 0));
        Entity far = t.SpawnAt(new(20, 0, 0));
        QueryFilter filter = QueryFilter.Near(center, 3);

        PassesOf(t, filter, close).Should().BeTrue();
        PassesOf(t, filter, far).Should().BeFalse();

        t.World.Destroy(center);

        PassesOf(t, filter, close).Should().BeFalse("a center that is not live matches nothing");
    }

    [Fact]
    public void ANearPointPassesTheEntitiesWithinTheRadiusOfThePointInThatFrame()
    {
        using var t = new TestWorld();
        FrameHandle ship = t.SpawnStaticFrame(TestWorld.At(100));
        Entity close = t.World.SpawnAt(ship, new(11, 0, 0));
        Entity far = t.World.SpawnAt(ship, new(15, 0, 0));
        Entity inRoot = t.SpawnAt(new(11, 0, 0));
        QueryFilter filter = QueryFilter.Near(ship, new(10, 0, 0), 2);

        PassesOf(t, filter, close).Should().BeTrue();
        PassesOf(t, filter, far).Should().BeFalse();
        PassesOf(t, filter, inRoot).Should().BeFalse("the point is in the ship's frame");
    }

    [Fact]
    public void AWhereFilterPassesWhatItsPredicateAccepts()
    {
        using var t = new TestWorld();
        Entity entity = t.SpawnWith();

        PassesOf(t, QueryFilter.Where(Always), entity).Should().BeTrue();
        PassesOf(t, QueryFilter.Where(Never), entity).Should().BeFalse();
    }

    // ---- TryCenter -----------------------------------------------------------------------------------------------

    [Fact]
    public void TryCenterGivesTheFrameAndPositionOfTheCenterOfEachNearCase()
    {
        using var t = new TestWorld();
        FrameHandle ship = t.SpawnStaticFrame(TestWorld.At(100));
        Entity asker = t.World.SpawnAt(ship, new(1, 2, 3));
        Entity center = t.SpawnAt(new(4, 5, 6));
        var snapshot = new Snapshot(t.World, asker);

        QueryFilter.Near(5).TryCenter(snapshot, out FrameHandle askerFrame, out Double3 askerPoint).Should().BeTrue();
        QueryFilter.Near(center, 5).TryCenter(snapshot, out FrameHandle entityFrame, out Double3 entityPoint).Should()
            .BeTrue();
        QueryFilter.Near(ship, new(7, 8, 9), 5).TryCenter(snapshot, out FrameHandle pointFrame, out Double3 pointPoint)
            .Should().BeTrue();

        (askerFrame, askerPoint).Should().Be((ship, new(1, 2, 3)), "the asker's own frame and position");
        (entityFrame, entityPoint).Should().Be((t.Root, new(4, 5, 6)), "the other entity's frame and position");
        (pointFrame, pointPoint).Should().Be((ship, new(7, 8, 9)), "the frame and point of the filter");
    }

    [Fact]
    public void TryCenterIsFalseForACenterThatIsInNoFrameAndForAFilterThatIsNotANearFilter()
    {
        using var t = new TestWorld();
        Entity asker = t.SpawnAt(new(1, 0, 0));
        var snapshot = new Snapshot(t.World, asker);

        FrameHandle gone = t.SpawnStaticFrame(TestWorld.At(100));
        t.World.Destroy(gone.Entity);

        QueryFilter.Near(t.Root.Entity, 5).TryCenter(snapshot, out _, out _).Should()
            .BeFalse("the owner of a root frame is in no frame");
        QueryFilter.Near(gone, default, 5).TryCenter(snapshot, out _, out _).Should()
            .BeFalse("the frame of the point is gone");
        QueryFilter.Near(5).TryCenter(t.World.Snapshot, out _, out _).Should()
            .BeFalse("a snapshot without an asker has no center for Near(radius)");
        QueryFilter.In(t.Root).TryCenter(snapshot, out _, out _).Should().BeFalse();
        QueryFilter.Where(Always).TryCenter(snapshot, out _, out _).Should().BeFalse();
        default(QueryFilter).TryCenter(snapshot, out _, out _).Should().BeFalse();
    }

    private static bool PassesOf(TestWorld t, QueryFilter filter, Entity entity, Entity asker = default)
    {
        Snapshot snapshot = asker.IsNull ? t.World.Snapshot : new(t.World, asker);
        return filter.Passes(snapshot, entity, t.World.Entities.FrontRow(entity));
    }
}
