using System.Numerics;
using Tertius.Simulation;
using static Tertius.Testing.WorldTesting;

namespace Tertius.Physics.Tests;

/// <summary>
/// Shape casts and overlap queries from components, on the real World and Jolt. A component asks with
/// <see cref="Self.Physics"/> during a tick, the sync point runs the query against Jolt and the snapshot of the end of that
/// tick, and the answers, a <see cref="ShapeHit"/> or one <see cref="OverlapHit"/> for each entity, arrive in the next
/// tick. Every expected distance, point and normal comes from the geometry of the scene, worked out in the test.
/// </summary>
public class WorldShapeQueryTests
{
    /// <summary>Jolt finds the touch of a moving shape to within its collision tolerance of 0.1 mm, in floats.</summary>
    private const double Tolerance = 1e-3;

    /// <summary>A crew member, for the queries that filter by component.</summary>
    private sealed class Crew : IComponent
    {
        public int Priority => 0;
        public EventMask Wants => default;

        public void Handle(Self self, scoped ref EventList events, scoped ref Outbox outbox)
        {
        }
    }

    /// <summary>One shape cast that a component asks for, on tick <see cref="OnTick"/>.</summary>
    private sealed record Cast(
        QueryShape Shape,
        DPose Start,
        Double3 Direction,
        double Length,
        EntityQuery Query,
        int Tag,
        long OnTick = 0);

    /// <summary>One overlap query that a component asks for, on tick <see cref="OnTick"/>.</summary>
    private sealed record Overlap(QueryShape Shape, DPose Pose, EntityQuery Query, int Tag, long OnTick = 0);

    /// <summary>Asks for its casts and overlaps on their ticks, and keeps every answer that it hears, with the tick it heard it on.</summary>
    private sealed class Probe : IComponent
    {
        public Cast[] Casts { get; init; } = [];
        public Overlap[] Overlaps { get; init; } = [];

        /// <summary>When set, the component asks for every query on every tick.</summary>
        public bool EveryTick { get; init; }

        /// <summary>When cleared, the component counts the answers but keeps none, so that it allocates nothing itself.</summary>
        public bool Keeps { get; init; } = true;

        public List<(long Tick, ShapeHit Hit)> ShapeAnswers { get; } = [];
        public List<(long Tick, OverlapHit Hit)> OverlapAnswers { get; } = [];

        /// <summary>How many answers the component heard.</summary>
        public int Heard { get; private set; }

        public int Priority => 0;
        public EventMask Wants => EventMask.Of<Tick>().And<ShapeHit>().And<OverlapHit>();

        public void Handle(Self self, scoped ref EventList events, scoped ref Outbox outbox)
        {
            foreach (ref ShapeHit hit in events.Of<ShapeHit>())
            {
                Heard++;
                if (Keeps)
                    ShapeAnswers.Add((self.Tick, hit));
            }

            foreach (ref OverlapHit hit in events.Of<OverlapHit>())
            {
                Heard++;
                if (Keeps)
                    OverlapAnswers.Add((self.Tick, hit));
            }

            foreach (Cast cast in Casts)
                if (EveryTick || cast.OnTick == self.Tick)
                    self.Physics.CastShape(cast.Shape, cast.Start, cast.Direction, cast.Length, cast.Query, cast.Tag);
            foreach (Overlap overlap in Overlaps)
                if (EveryTick || overlap.OnTick == self.Tick)
                    self.Physics.Overlap(overlap.Shape, overlap.Pose, overlap.Query, overlap.Tag);
        }

        public ShapeHit[] CastAnswersTo(int tag) =>
            [.. ShapeAnswers.Where(a => a.Hit.Tag == tag).Select(a => a.Hit)];

        public Entity[] OverlapAnswersTo(int tag) =>
            [.. OverlapAnswers.Where(a => a.Hit.Tag == tag).Select(a => a.Hit.Entity)];
    }

    private static BodyDesc StaticBox(float half = 0.5f) => new(new BoxShapeDesc(new(half)), BodyMotion.Static);

    private static Entity Spawn(World world, FrameHandle frame, Double3 at, BodyDesc? body,
        params IComponent[] components) =>
        world.Spawn(new()
            { Frame = frame, Pose = new(at, DQuaternion.Identity), Body = body, Components = components });

    private static DPose Turned(Double3 at, Double3 axis, double angle) =>
        new(at, DQuaternion.FromAxisAngle(axis, angle));

    private static void ShouldBeAt(ShapeHit hit, Entity entity, Double3 point, Vector3 normal, double distance)
    {
        hit.Entity.Should().Be(entity);
        (hit.Point - point).Length.Should().BeLessThan(Tolerance, $"the hit point {hit.Point} should be {point}");
        Vector3.Distance(hit.Normal, normal).Should().BeLessThan(1e-3f, $"the normal {hit.Normal} should be {normal}");
        ((double)hit.Distance).Should().BeApproximately(distance, Tolerance);
    }

    /// <summary>
    /// A static frame at (100, 0, 0) in <paramref name="parent"/>, turned by 90° about +Y, so that its +Z is the parent's
    /// +X, with a bubble of 20 m. Its owner has no body, so the parent's system holds nothing of it.
    /// </summary>
    private static FrameHandle TurnedChildFrame(World world, FrameHandle parent) => world.GetOwnedFrame(world.Spawn(
        new()
        {
            Frame = parent,
            Pose = Turned(new(100, 0, 0), Double3.UnitY, Math.PI / 2),
            AsFrame = new(FrameBounds.Sphere(20))
        }));

    /// <summary>
    /// A static frame at <paramref name="at"/> in <paramref name="parent"/>, not turned, with a bubble of
    /// <paramref name="radius"/> m. Its owner has no body, so the parent's system holds nothing of it.
    /// </summary>
    private static FrameHandle Bubble(World world, FrameHandle parent, Double3 at, float radius) =>
        world.GetOwnedFrame(world.Spawn(new()
        {
            Frame = parent,
            Pose = new(at, DQuaternion.Identity),
            AsFrame = new(FrameBounds.Sphere(radius))
        }));

    /// <summary>
    /// A predicate for <see cref="EntityQuery.Where"/> that accepts every entity and writes down each entity that it was
    /// asked about, in order. Jolt asks it about the bodies that a cast reaches, so the list shows which frames the cast
    /// looked in.
    /// </summary>
    private static EntityQuery Recording(List<Entity> asked) => EntityQuery.Where((_, entity) =>
    {
        asked.Add(entity);
        return true;
    });

    // ---- Shape casts ----

    [Fact]
    public void ASphereCastAnswersInTheNextTickWithTheFirstBodyItTouchesWhereItTouchesAndHowFarItMoved()
    {
        using World world = NewWorld(out _);
        FrameHandle ground = Ground(world, 0);
        var probe = new Probe { Casts = [new(QueryShape.Sphere(0.5f), At(0), Double3.UnitX, 50, default, Tag: 4)] };
        Spawn(world, ground, Double3.Zero, null, probe);
        Spawn(world, ground, new(20, 0, 0), StaticBox());
        Entity near = Spawn(world, ground, new(10, 0, 0), StaticBox());

        Tick(world, 3);

        probe.ShapeAnswers.Should().ContainSingle();
        probe.ShapeAnswers[0].Tick.Should()
            .Be(1, "the cast asked on tick 0, and its answer arrives with the next tick");
        // The sphere's front reaches the face at x = 9.5 when its center is at x = 9.
        ShouldBeAt(probe.ShapeAnswers[0].Hit, near, new(9.5, 0, 0), -Vector3.UnitX, 9);
        probe.ShapeAnswers[0].Hit.Tag.Should().Be(4);
        world.Errors.Should().BeEmpty();
    }

    [Fact]
    public void AShapeCastWithADirectionThatIsNotUnitLengthMovesTheShapeInMeters()
    {
        // The direction (6, 0, 0) is six meters long. The sphere still moves 9 m before it touches the box, not 9 m over 6.
        using World world = NewWorld(out _);
        FrameHandle ground = Ground(world, 0);
        var probe = new Probe { Casts = [new(QueryShape.Sphere(0.5f), At(0), new(6, 0, 0), 50, default, Tag: 4)] };
        Spawn(world, ground, Double3.Zero, null, probe);
        Entity near = Spawn(world, ground, new(10, 0, 0), StaticBox());

        Tick(world, 2);

        ShouldBeAt(probe.CastAnswersTo(4).Should().ContainSingle().Subject, near, new(9.5, 0, 0), -Vector3.UnitX, 9);
        world.Errors.Should().BeEmpty();
    }

    [Fact]
    public void AShapeCastWithADirectionThatIsNotUnitLengthMissesAtTheEndOfItsLengthInMeters()
    {
        using World world = NewWorld(out _);
        FrameHandle ground = Ground(world, 0);
        var probe = new Probe
            { Casts = [new(QueryShape.Sphere(0.5f), At(1, 2, 3), new(0, 0, 3), 50, default, Tag: 5)] };
        Spawn(world, ground, new(1, 2, 3), null, probe);

        Tick(world, 2);

        ShapeHit miss = probe.CastAnswersTo(5).Should().ContainSingle().Subject;
        miss.IsHit.Should().BeFalse();
        (miss.Point - new Double3(1, 2, 53)).Length.Should().BeLessThan(1e-9);
        miss.Distance.Should().Be(50);
    }

    [Fact]
    public void ABoxCastMovesTheBoxTurnedByItsStartPose()
    {
        // A box 2 m long along its own Z, turned by 90° about +Y, so that its length lies along the ground's X: its front
        // face reaches the face at x = 9.5 when its center is at x = 8.5. Unturned, it would travel 9.25 m.
        using World world = NewWorld(out _);
        FrameHandle ground = Ground(world, 0);
        DPose start = Turned(Double3.Zero, Double3.UnitY, Math.PI / 2);
        var probe = new Probe
            { Casts = [new(QueryShape.Box(new(0.25f, 0.25f, 1)), start, Double3.UnitX, 50, default, Tag: 1)] };
        Spawn(world, ground, Double3.Zero, null, probe);
        Entity target = Spawn(world, ground, new(10, 0, 0), StaticBox());

        Tick(world, 2);

        ShapeHit hit = probe.CastAnswersTo(1).Single();
        hit.Entity.Should().Be(target);
        ((double)hit.Distance).Should().BeApproximately(8.5, Tolerance);
        // Two faces touch, so Jolt may name any point of the square where they meet.
        hit.Point.X.Should().BeApproximately(9.5, Tolerance);
        Math.Abs(hit.Point.Y).Should().BeLessThan(0.25 + Tolerance);
        Math.Abs(hit.Point.Z).Should().BeLessThan(0.25 + Tolerance);
        Vector3.Distance(hit.Normal, -Vector3.UnitX).Should().BeLessThan(1e-3f);
    }

    [Fact]
    public void ACapsuleCastMovesTheCapsuleTurnedByItsStartPose()
    {
        // A capsule along its own +Y, with 1 m of cylinder on each side of its center and a radius of 0.5 m, turned by 90°
        // about +Z, so that it lies along the ground's X: its tip reaches the face at x = 9.5 when its center is at x = 8.
        // Upright, it would travel 9 m.
        using World world = NewWorld(out _);
        FrameHandle ground = Ground(world, 0);
        DPose start = Turned(Double3.Zero, Double3.UnitZ, Math.PI / 2);
        var probe = new Probe { Casts = [new(QueryShape.Capsule(1, 0.5f), start, Double3.UnitX, 50, default, Tag: 1)] };
        Spawn(world, ground, Double3.Zero, null, probe);
        Entity target = Spawn(world, ground, new(10, 0, 0), StaticBox());

        Tick(world, 2);

        ShouldBeAt(probe.CastAnswersTo(1).Single(), target, new(9.5, 0, 0), -Vector3.UnitX, 8);
    }

    [Fact]
    public void AShapeCastThatTouchesNothingAnswersWithAMissAtTheEndOfTheCast()
    {
        using World world = NewWorld(out _);
        FrameHandle ground = Ground(world, 0);
        var probe = new Probe { Casts = [new(QueryShape.Sphere(1), At(1, 2, 3), Double3.UnitY, 50, default, Tag: 9)] };
        Spawn(world, ground, new(1, 2, 3), null, probe);
        Spawn(world, ground, new(10, 0, 0), StaticBox());

        Tick(world, 2);

        ShapeHit miss = probe.CastAnswersTo(9).Should().ContainSingle().Subject;
        miss.IsHit.Should().BeFalse();
        miss.Entity.Should().Be(Entity.Null);
        (miss.Point - new Double3(1, 52, 3)).Length.Should().BeLessThan(1e-9);
        miss.Distance.Should().Be(50);
        miss.Normal.Should().Be(Vector3.Zero);
    }

    [Fact]
    public void AShapeCastByComponentSkipsTheBodiesOfEntitiesWithoutTheComponent()
    {
        using World world = NewWorld(out _);
        FrameHandle ground = Ground(world, 0);
        var probe = new Probe
            { Casts = [new(QueryShape.Sphere(0.5f), At(0), Double3.UnitX, 50, EntityQuery.With<Crew>(), Tag: 1)] };
        Spawn(world, ground, Double3.Zero, null, probe);
        Spawn(world, ground, new(10, 0, 0), StaticBox());
        Entity crew = Spawn(world, ground, new(20, 0, 0), StaticBox(), new Crew());

        Tick(world, 2);

        ShouldBeAt(probe.CastAnswersTo(1).Single(), crew, new(19.5, 0, 0), -Vector3.UnitX, 19);
    }

    [Fact]
    public void AShapeCastByCollisionGroupSkipsTheBodiesOutsideTheGroup()
    {
        var registry = new CollisionGroupRegistry();
        CollisionGroup glass = registry.Register("Glass");
        using World world = NewWorld(out _);
        FrameHandle ground = Ground(world, 0);
        var probe = new Probe
        {
            Casts =
            [
                new(QueryShape.Sphere(0.5f), At(0), Double3.UnitX, 50, EntityQuery.InAny(CollisionGroup.Solid), Tag: 1)
            ]
        };
        Spawn(world, ground, Double3.Zero, null, probe);
        Spawn(world, ground, new(10, 0, 0), StaticBox() with { CollisionGroups = CollisionGroups.Of(glass) });
        Entity wall = Spawn(world, ground, new(20, 0, 0), StaticBox());

        Tick(world, 2);

        ShouldBeAt(probe.CastAnswersTo(1).Single(), wall, new(19.5, 0, 0), -Vector3.UnitX, 19);
    }

    [Fact]
    public void AShapeCastSkipsTheBodyOfTheEntityThatAsked()
    {
        // The sphere starts inside the asker's own box, which Jolt treats as solid, so without the skip it would touch the
        // asker at once.
        using World world = NewWorld(out _);
        FrameHandle ground = Ground(world, 0);
        var probe = new Probe { Casts = [new(QueryShape.Sphere(0.5f), At(0), Double3.UnitX, 50, default, Tag: 1)] };
        Spawn(world, ground, Double3.Zero, StaticBox(2), probe);
        Entity target = Spawn(world, ground, new(10, 0, 0), StaticBox());

        Tick(world, 2);

        ShouldBeAt(probe.CastAnswersTo(1).Single(), target, new(9.5, 0, 0), -Vector3.UnitX, 9);
    }

    [Fact]
    public void AShapeCastThatStartsOverlappingABodyTouchesItAtOnceEvenMovingAwayFromIt()
    {
        // The box from x = 0.5 to x = 1.5 overlaps the sphere at its start, and the sphere moves away along -X, toward the
        // box at x = -10. A convex body is solid, as it is for a ray.
        using World world = NewWorld(out _);
        FrameHandle ground = Ground(world, 0);
        var probe = new Probe { Casts = [new(QueryShape.Sphere(1), At(0), -Double3.UnitX, 50, default, Tag: 1)] };
        Spawn(world, ground, Double3.Zero, null, probe);
        Entity overlapped = Spawn(world, ground, new(1, 0, 0), StaticBox());
        Spawn(world, ground, new(-10, 0, 0), StaticBox());

        Tick(world, 2);

        ShapeHit hit = probe.CastAnswersTo(1).Single();
        hit.Entity.Should().Be(overlapped);
        hit.Distance.Should().Be(0);
    }

    [Fact]
    public void AShapeCastThatCrossesIntoTheBubbleOfAChildFrameHitsABodyInThatFrameWithTheShapeTurnedAsItIsInTheAsker()
    {
        // A capsule that lies along the ground's X, as in the capsule test, reaches 1.5 m ahead of its center. In the
        // child, which is turned, it must lie along the child's Z.
        using World world = NewWorld(out _);
        FrameHandle ground = Ground(world, 0);
        FrameHandle child = TurnedChildFrame(world, ground);
        DPose start = Turned(Double3.Zero, Double3.UnitZ, Math.PI / 2);
        var probe = new Probe
            { Casts = [new(QueryShape.Capsule(1, 0.5f), start, Double3.UnitX, 200, default, Tag: 1)] };
        Spawn(world, ground, Double3.Zero, null, probe);
        // At (0, 0, 5) in the child, which is (105, 0, 0) in the ground. Its face toward the cast is the child's -Z face.
        Entity inside = Spawn(world, child, new(0, 0, 5), StaticBox());

        Tick(world, 2);

        ShouldBeAt(probe.CastAnswersTo(1).Single(), inside, new(104.5, 0, 0), -Vector3.UnitX, 103);
        world.Errors.Should().BeEmpty();
    }

    [Fact]
    public void AShapeCastThatTouchesAFarBodyInTheParentAndANearerBodyInAChildFrameNamesTheNearerBody()
    {
        // The ground's system answers first, with the far box. The child's system then answers with a nearer box, and the
        // nearer one is the cast's hit.
        using World world = NewWorld(out _);
        FrameHandle ground = Ground(world, 0);
        FrameHandle child = TurnedChildFrame(world, ground);
        var probe = new Probe { Casts = [new(QueryShape.Sphere(0.5f), At(0), Double3.UnitX, 200, default, Tag: 1)] };
        Spawn(world, ground, Double3.Zero, null, probe);
        Spawn(world, ground, new(150, 0, 0), StaticBox());
        // At (0, 0, 5) in the child, which is (105, 0, 0) in the ground: the sphere touches its face at x = 104.5 with its
        // center at x = 104.
        Entity nearer = Spawn(world, child, new(0, 0, 5), StaticBox());

        Tick(world, 2);

        ShouldBeAt(probe.CastAnswersTo(1).Single(), nearer, new(104.5, 0, 0), -Vector3.UnitX, 104);
        world.Errors.Should().BeEmpty();
    }

    [Fact]
    public void AShapeCastDoesNotLookInAChildFrameThatLiesBeyondTheNearestHitItHasAlreadyFound()
    {
        // Three places along +X: the ground's box at x = 150, a near frame around x = 40, and a far frame around x = 120.
        // The far frame is made first, so the cast's walk visits it after the near frame. The ground's system answers with
        // the box at x = 150, the near frame's system then answers with a nearer hit, and the far frame's part of the cast,
        // from about x = 109, starts beyond that hit. So Jolt is never asked about the far frame's body.
        var asked = new List<Entity>();
        using World world = NewWorld(out _);
        FrameHandle ground = Ground(world, 0);
        FrameHandle farFrame = Bubble(world, ground, new(120, 0, 0), 10);
        FrameHandle nearFrame = Bubble(world, ground, new(40, 0, 0), 10);
        var probe = new Probe
            { Casts = [new(QueryShape.Sphere(0.5f), At(0), Double3.UnitX, 200, Recording(asked), Tag: 1)] };
        Spawn(world, ground, Double3.Zero, null, probe);
        Entity groundBox = Spawn(world, ground, new(150, 0, 0), StaticBox());
        Entity farBody = Spawn(world, farFrame, Double3.Zero, StaticBox());
        Entity nearBody = Spawn(world, nearFrame, Double3.Zero, StaticBox());

        Tick(world, 2);

        ShouldBeAt(probe.CastAnswersTo(1).Single(), nearBody, new(39.5, 0, 0), -Vector3.UnitX, 39);
        asked.Should().Contain([groundBox, nearBody], "the cast looked in the ground and in the near frame");
        asked.Should().NotContain(farBody, "the near frame's hit ends the cast's search before the far frame");
        world.Errors.Should().BeEmpty();
    }

    [Fact]
    public void AShapeCastThatLeavesTheBubbleOfItsFrameTouchesAParentBodyNearTheEdgeWhereItReallyTouchesIt()
    {
        // From the child's origin along its +Z, a sphere of 2 m. The ground's box at (121, 0, 0) is at 21 m along the
        // child's +Z, with its face at 20.5 m, outside the bubble of 20 m. The sphere touches it with its center at 18.5 m,
        // inside the bubble, so the ground's part of the cast starts where the sphere first sticks out of the bubble, with
        // its center at 18 m, and not where its center leaves the bubble, where it would already overlap the box.
        using World world = NewWorld(out _);
        FrameHandle ground = Ground(world, 0);
        FrameHandle child = TurnedChildFrame(world, ground);
        var probe = new Probe { Casts = [new(QueryShape.Sphere(2), At(0), Double3.UnitZ, 100, default, Tag: 1)] };
        Spawn(world, child, Double3.Zero, null, probe);
        Entity box = Spawn(world, ground, new(121, 0, 0), StaticBox());

        Tick(world, 2);

        ShouldBeAt(probe.CastAnswersTo(1).Single(), box, new(0, 0, 20.5), -Vector3.UnitZ, 18.5);
    }

    [Fact]
    public void ABoxCastHasSharpEdges()
    {
        // A cube of 1 m turned by 45° about +Y leads with an edge 0.7071 m ahead of its center, which reaches the face at
        // x = 9.5 when its center is at x = 8.7929. A box rounded by Jolt's default convex radius of 5 cm would lead with
        // its edge 2 cm further back.
        using World world = NewWorld(out _);
        FrameHandle ground = Ground(world, 0);
        DPose start = Turned(Double3.Zero, Double3.UnitY, Math.PI / 4);
        var probe = new Probe { Casts = [new(QueryShape.Box(new(0.5f)), start, Double3.UnitX, 50, default, Tag: 1)] };
        Spawn(world, ground, Double3.Zero, null, probe);
        Entity target = Spawn(world, ground, new(10, 0, 0), StaticBox());

        Tick(world, 2);

        ShapeHit hit = probe.CastAnswersTo(1).Single();
        hit.Entity.Should().Be(target);
        ((double)hit.Distance).Should().BeApproximately(9.5 - 0.5 * Math.Sqrt(2), Tolerance);
        // The edge meets the face along a line, so Jolt may name any point of it.
        hit.Point.X.Should().BeApproximately(9.5, Tolerance);
        hit.Point.Z.Should().BeApproximately(0, Tolerance);
    }

    [Fact]
    public void AShapeCastThatLeavesTheBubbleOfItsFrameHitsABodyInTheParentFrameInTheAxesOfTheAsker()
    {
        using World world = NewWorld(out _);
        FrameHandle ground = Ground(world, 0);
        FrameHandle child = TurnedChildFrame(world, ground);
        // From the child's origin along its +Z, which is the ground's +X.
        var probe = new Probe { Casts = [new(QueryShape.Sphere(0.5f), At(0), Double3.UnitZ, 100, default, Tag: 1)] };
        Spawn(world, child, Double3.Zero, null, probe);
        Entity outside = Spawn(world, ground, new(150, 0, 0), StaticBox());

        Tick(world, 2);

        // The face at x = 149.5 in the ground is 49.5 m along the child's +Z, and its normal is the child's -Z.
        ShouldBeAt(probe.CastAnswersTo(1).Single(), outside, new(0, 0, 49.5), -Vector3.UnitZ, 49);
    }

    [Fact]
    public void AShapeCastFromInsideAShipPassesOutThroughItsOwnHullToTheBodiesBeyond()
    {
        // The hull is a solid box of 10 m by 6 m by 30 m in the ground's system, larger than the ship's bubble of 10 m. The
        // sphere of 2.9 m starts 1 m above the ship's center, clear of the deck, whose top is 2 m below it. The ground's
        // part of the cast starts where the sphere first sticks out of the bubble, with its center about 7 m out, where it
        // still overlaps the hull. The cast must skip the hull and reach the box, whose face at x = 29.5 the sphere touches
        // with its center at x = 26.6.
        using World world = NewWorld(out _);
        FrameHandle ground = Ground(world, 0);
        Scenes.ShipScene ship = Scenes.ShipWithInterior(world, ground, bubbleRadius: 10);
        var probe = new Probe { Casts = [new(QueryShape.Sphere(2.9f), At(0, 1), Double3.UnitX, 100, default, Tag: 1)] };
        Spawn(world, ship.Interior, new(0, 1, 0), null, probe);
        Entity beyond = Spawn(world, ground, new(30, 1, 0), StaticBox());

        Tick(world, 2);

        ShouldBeAt(probe.CastAnswersTo(1).Single(), beyond, new(29.5, 1, 0), -Vector3.UnitX, 26.6);
    }

    // ---- Overlaps ----

    [Fact]
    public void AnOverlapAnswersWithEveryEntityWhoseBodyOverlapsTheSphereInEntityOrderAndNoneOutsideIt()
    {
        using World world = NewWorld(out _);
        FrameHandle ground = Ground(world, 0);
        var probe = new Probe { Overlaps = [new(QueryShape.Sphere(5), At(0), default, Tag: 2)] };
        // The asker's own box is inside the sphere, and the query skips it.
        Spawn(world, ground, Double3.Zero, StaticBox(), probe);
        Entity faceAtFour = Spawn(world, ground, new(0, 4.5, 0), StaticBox());
        Spawn(world, ground, new(6, 0, 0), StaticBox());
        Entity faceJustInside = Spawn(world, ground, new(0, 0, -5.4), StaticBox());
        Spawn(world, ground, new(0, -5.6, 0), StaticBox());
        Entity crate = Spawn(world, ground, new(3, 0, 0), Cube());
        // A mesh cube whose face at z = 4.5 and whose sides reach into the sphere: Jolt reports each triangle it touches.
        Entity mesh = Spawn(world, ground, new(0, 0, 5.5), new(MeshCube(1), BodyMotion.Static));

        Tick(world, 2);

        probe.OverlapAnswers.Should().OnlyContain(a => a.Tick == 1);
        probe.OverlapAnswersTo(2).Should().Equal(faceAtFour, faceJustInside, crate, mesh);
        world.Errors.Should().BeEmpty();
    }

    [Fact]
    public void AnOverlapThatFindsNothingAnswersOnceWithNoEntity()
    {
        using World world = NewWorld(out _);
        FrameHandle ground = Ground(world, 0);
        var probe = new Probe { Overlaps = [new(QueryShape.Sphere(1), At(0), default, Tag: 3)] };
        Spawn(world, ground, Double3.Zero, null, probe);
        Spawn(world, ground, new(10, 0, 0), StaticBox());

        Tick(world, 2);

        OverlapHit none = probe.OverlapAnswers.Should().ContainSingle().Subject.Hit;
        none.IsHit.Should().BeFalse();
        none.Tag.Should().Be(3);
    }

    [Fact]
    public void ABoxOrACapsuleOverlapsOnlyWhatItsOwnShapeTurnedByItsPoseOverlaps()
    {
        // Both shapes lie along the ground's Y: the box turned by 90° about +Z, the capsule upright. Each reaches the box
        // above, from y = 4 to y = 5, and neither reaches the box beside, from x = 4 to x = 5, which a sphere of the same
        // bounding radius would.
        using World world = NewWorld(out _);
        FrameHandle ground = Ground(world, 0);
        var probe = new Probe
        {
            Overlaps =
            [
                new(QueryShape.Box(new(4.5f, 0.5f, 0.5f)), Turned(Double3.Zero, Double3.UnitZ, Math.PI / 2), default,
                    Tag: 1),
                new(QueryShape.Capsule(4, 0.5f), At(0), default, Tag: 2)
            ]
        };
        Spawn(world, ground, Double3.Zero, null, probe);
        Entity above = Spawn(world, ground, new(0, 4.5, 0), StaticBox());
        Spawn(world, ground, new(4.5, 0, 0), StaticBox());

        Tick(world, 2);

        probe.OverlapAnswersTo(1).Should().Equal(above);
        probe.OverlapAnswersTo(2).Should().Equal(above);
    }

    [Fact]
    public void AnOverlapWithARotationThatIsNotUnitLengthTurnsTheShapeAsTheUnitRotationDoes()
    {
        // The rotation of 90° about +Z, times 2. Like the unit rotation in the test above, it lays the box along the Y of
        // the ground, so the box reaches the one above and not the one beside.
        using World world = NewWorld(out _);
        FrameHandle ground = Ground(world, 0);
        double side = Math.Sqrt(0.5) * 2;
        var probe = new Probe
        {
            Overlaps =
            [
                new(QueryShape.Box(new(4.5f, 0.5f, 0.5f)), new(Double3.Zero, new(0, 0, side, side)), default, Tag: 1)
            ]
        };
        Spawn(world, ground, Double3.Zero, null, probe);
        Entity above = Spawn(world, ground, new(0, 4.5, 0), StaticBox());
        Spawn(world, ground, new(4.5, 0, 0), StaticBox());

        Tick(world, 2);

        probe.OverlapAnswersTo(1).Should().Equal(above);
    }

    [Fact]
    public void ABoxOverlapHasSharpCorners()
    {
        // A ball of 0.5 m whose center is 0.49 m from the corner (1, 1, 0) of a box with half extents of 1 m, along the
        // diagonal, dips 1 cm into the corner. A box rounded by Jolt's default convex radius of 5 cm would miss it by 1 cm.
        using World world = NewWorld(out _);
        FrameHandle ground = Ground(world, 0);
        var probe = new Probe { Overlaps = [new(QueryShape.Box(Vector3.One), At(0), default, Tag: 1)] };
        Spawn(world, ground, Double3.Zero, null, probe);
        double offset = 1 + 0.49 / Math.Sqrt(2);
        Entity ball = Spawn(world, ground, new(offset, offset, 0), new(new SphereShapeDesc(0.5f), BodyMotion.Static));

        Tick(world, 2);

        probe.OverlapAnswersTo(1).Should().Equal(ball);
    }

    [Fact]
    public void
        AnOverlapThatReachesIntoTheBubbleOfAChildFrameFindsTheChildsBodiesInAndJustOutsideTheBubbleInEntityOrder()
    {
        // A box from x = 77 to x = 81 in the ground, around (79, 0, 0), reaches across the edge of the child's bubble, at
        // x = 80. In the child, which is turned, its length lies along the child's Z. The child's static box at (0, 0, -19)
        // is (81, 0, 0) in the ground, from x = 80.5, inside the bubble. The child's static box at (0, 0, -21.5) is
        // (78.5, 0, 0), outside the bubble, where only the child's system holds it. The ground's own box, from x = 77 to
        // x = 78, comes between them in entity order, and the ground's system is queried first.
        using World world = NewWorld(out _);
        FrameHandle ground = Ground(world, 0);
        FrameHandle child = TurnedChildFrame(world, ground);
        var probe = new Probe { Overlaps = [new(QueryShape.Box(new(2, 0.5f, 0.5f)), At(79), default, Tag: 1)] };
        Spawn(world, ground, new(79, 0, 0), null, probe);
        Entity inside = Spawn(world, child, new(0, 0, -19), StaticBox());
        Entity groundBox = Spawn(world, ground, new(77.5, 0, 0), StaticBox());
        Entity outside = Spawn(world, child, new(0, 0, -21.5), StaticBox());

        Tick(world, 2);

        probe.OverlapAnswersTo(1).Should().Equal(inside, groundBox, outside);
    }

    [Fact]
    public void AnOverlapBesideTheBubbleOfAChildFrameFindsAChildBodyThatTheShapeReaches()
    {
        // The child's static box at (0, 20, 0), at the top of the bubble, reaches up to y = 20.5. The sphere of 2 m at
        // (100, 22, 0) in the ground, above the child, has its center outside every body of the child, but it reaches down
        // to y = 20.
        using World world = NewWorld(out _);
        FrameHandle ground = Ground(world, 0);
        FrameHandle child = TurnedChildFrame(world, ground);
        var probe = new Probe { Overlaps = [new(QueryShape.Sphere(2), At(100, 22), default, Tag: 1)] };
        Spawn(world, ground, new(100, 22, 0), null, probe);
        Entity top = Spawn(world, child, new(0, 20, 0), StaticBox());

        Tick(world, 2);

        probe.OverlapAnswersTo(1).Should().Equal(top);
    }

    [Fact]
    public void AnOverlapLargerThanTheBubbleOfItsFrameFindsTheBodiesOfTheParent()
    {
        // A sphere of 25 m at the center of the child, whose bubble has a radius of 20 m, reaches the ground's box at
        // (124, 0, 0), 23.5 m from the child's center.
        using World world = NewWorld(out _);
        FrameHandle ground = Ground(world, 0);
        FrameHandle child = TurnedChildFrame(world, ground);
        var probe = new Probe { Overlaps = [new(QueryShape.Sphere(25), At(0), default, Tag: 1)] };
        Spawn(world, child, Double3.Zero, null, probe);
        Entity box = Spawn(world, ground, new(124, 0, 0), StaticBox());

        Tick(world, 2);

        probe.OverlapAnswersTo(1).Should().Equal(box);
    }

    [Fact]
    public void AnOverlapThatReachesOutOfAShipsBubbleFindsTheBodiesOfTheParentButNotTheShipsOwnHull()
    {
        // The ship's bubble has a radius of 10 m, and its hull is a solid box out to x = 5. The sphere of 3.2 m at
        // (7, 1, 0) in the ship reaches out of the bubble to x = 10.2, and it overlaps the hull, from x = 3.8, but not the
        // deck. The ground's box from x = 10 to x = 11 overlaps it.
        using World world = NewWorld(out _);
        FrameHandle ground = Ground(world, 0);
        Scenes.ShipScene ship = Scenes.ShipWithInterior(world, ground, bubbleRadius: 10);
        var probe = new Probe { Overlaps = [new(QueryShape.Sphere(3.2f), At(7, 1), default, Tag: 1)] };
        Spawn(world, ship.Interior, new(7, 1, 0), null, probe);
        Entity box = Spawn(world, ground, new(10.5, 1, 0), StaticBox());

        Tick(world, 2);

        probe.OverlapAnswersTo(1).Should().Equal(box);
    }

    // ---- Refusals, determinism and cost ----

    private sealed class BadQuery(QueryShape shape, DPose pose, Double3 direction, double length, bool overlap)
        : IComponent
    {
        public int Priority => 0;
        public EventMask Wants => EventMask.Of<Tick>();

        public void Handle(Self self, scoped ref EventList events, scoped ref Outbox outbox)
        {
            if (overlap)
                self.Physics.Overlap(shape, pose, default, 1);
            else
                self.Physics.CastShape(shape, pose, direction, length, default, 1);
        }
    }

    public static TheoryData<string, QueryShape, double, double, double, bool> BadQueries() => new()
    {
        { "radius", QueryShape.Sphere(0), 1, 1, 10, false },
        { "default shape", default, 1, 1, 10, false },
        { "default shape", default, 1, 1, 10, true },
        { "half extents", QueryShape.Box(new(1, float.NaN, 1)), 1, 1, 10, false },
        { "half height", QueryShape.Capsule(-1, 0.5f), 1, 1, 10, true },
        { "position", QueryShape.Sphere(1), double.PositiveInfinity, 1, 10, true },
        { "rotation", QueryShape.Sphere(1), 0, 0, 10, true },
        { "direction", QueryShape.Sphere(1), 0, 1, 0, false },
        { "length", QueryShape.Sphere(1), 0, 1, double.NaN, false }
    };

    [Theory]
    [MemberData(nameof(BadQueries))]
    public void AShapeQueryWithAShapePoseDirectionOrLengthThatIsNotUsableIsRefusedAtOnce(string what, QueryShape shape,
        double x, double w,
        double length, bool overlap)
    {
        using World world = NewWorld(out _);
        FrameHandle ground = Ground(world, 0);
        var probe = new Probe();
        // x places the shape, w is the rotation's real part, and a length of 0 stands for a zero direction.
        var pose = new DPose(new(x, 0, 0), new(0, 0, 0, w));
        Double3 direction = length == 0 ? Double3.Zero : Double3.UnitX;
        Spawn(world, ground, Double3.Zero, null, probe,
            new BadQuery(shape, pose, direction, length == 0 ? 10 : length, overlap));

        Tick(world, 2);

        world.Errors.Should().Contain(e => e.Contains("BadQuery") && e.Contains(what));
        probe.ShapeAnswers.Should().BeEmpty();
        probe.OverlapAnswers.Should().BeEmpty();
    }

    /// <summary>
    /// A shape cast and an overlap query run the asker's predicate inside Jolt, so a predicate that throws must not cross
    /// into native code: the query has no answer, the error names the kind of query, and the other askers go on.
    /// </summary>
    [Fact]
    public void AShapeCastWhosePredicateThrowsReportsAShapeCastFailureAndGivesNoAnswerWhileTheWorldGoesOn()
    {
        using World world = NewWorld(out _);
        FrameHandle ground = Ground(world, 0);
        EntityQuery throws =
            EntityQuery.Where(static (_, _) => throw new InvalidOperationException("the predicate broke"));
        var broken = new Probe { Casts = [new(QueryShape.Sphere(0.5f), At(0), Double3.UnitX, 50, throws, Tag: 1)] };
        var fine = new Probe { Casts = [new(QueryShape.Sphere(0.5f), At(0, 0.1), Double3.UnitX, 50, default, Tag: 2)] };
        Spawn(world, ground, Double3.Zero, null, broken);
        Spawn(world, ground, new(0, 0.1, 0), null, fine);
        Entity box = Spawn(world, ground, new(10, 0, 0), StaticBox());

        Tick(world, 3);

        world.Errors.Should().ContainSingle().Which.Should().Contain("the shape cast failed").And
            .Contain("the predicate broke");
        broken.ShapeAnswers.Should().BeEmpty();
        fine.CastAnswersTo(2).Should().ContainSingle().Which.Entity.Should().Be(box);
    }

    [Fact]
    public void AnOverlapWhosePredicateThrowsReportsAnOverlapQueryFailureAndGivesNoAnswerWhileTheWorldGoesOn()
    {
        using World world = NewWorld(out _);
        FrameHandle ground = Ground(world, 0);
        EntityQuery throws =
            EntityQuery.Where(static (_, _) => throw new InvalidOperationException("the predicate broke"));
        var broken = new Probe { Overlaps = [new(QueryShape.Sphere(2), At(0), throws, Tag: 1)] };
        var fine = new Probe { Overlaps = [new(QueryShape.Sphere(2), At(0, 0.1), default, Tag: 2)] };
        Spawn(world, ground, Double3.Zero, null, broken);
        Spawn(world, ground, new(0, 0.1, 0), null, fine);
        Entity box = Spawn(world, ground, new(1, 0, 0), StaticBox());

        Tick(world, 3);

        // The test pins the name of the kind of query, and not the article before it.
        world.Errors.Should().ContainSingle().Which.Should().Contain("the overlap query failed").And
            .Contain("the predicate broke");
        broken.OverlapAnswers.Should().BeEmpty();
        fine.OverlapAnswersTo(2).Should().Equal(box);
    }

    /// <summary>
    /// A scene with bodies in a frame and in a child frame, and eight askers that each cast a shape and test an overlap
    /// every tick. Returns every answer that they heard, in the order heard, as raw bits.
    /// </summary>
    private static List<string> AnswersOfASceneOfMany(int maxDegreeOfParallelism, int ticks)
    {
        using World world = NewWorld(out _,
            configure: options => options.MaxDegreeOfParallelism = maxDegreeOfParallelism);
        FrameHandle ground = Ground(world, 0);
        FrameHandle child = TurnedChildFrame(world, ground);
        for (int i = 0; i < 24; i++)
            Spawn(world, ground, new(5 + 7 * i, i % 5 - 2, i % 3 - 1), StaticBox(0.5f + 0.1f * (i % 4)),
                i % 2 == 0 ? [new Crew()] : []);
        for (int i = 0; i < 6; i++)
            Spawn(world, child, new(i - 3, 0, 3 * i - 8), StaticBox(), new Crew());
        QueryShape[] shapes =
            [QueryShape.Sphere(0.4f), QueryShape.Box(new(0.3f, 0.2f, 0.5f)), QueryShape.Capsule(0.5f, 0.3f)];
        QueryShape[] volumes = [QueryShape.Sphere(8), QueryShape.Box(new(8, 3, 3)), QueryShape.Capsule(6, 2)];
        var probes = new List<Probe>();
        for (int i = 0; i < 8; i++)
        {
            var from = new Double3(0, i % 5 - 2, i % 3 - 1);
            Double3 direction = new Double3(1, 0.002 * i, -0.001 * i).Normalized();
            var probe = new Probe
            {
                Casts = [new(shapes[i % 3], Turned(from, Double3.UnitY, 0.3 * i), direction, 300, default, Tag: 1)],
                Overlaps =
                [
                    new(volumes[(i + 1) % 3], Turned(new(7 * i + 5, 0, 0), Double3.UnitZ, 0.2 * i),
                        EntityQuery.With<Crew>(), Tag: 2)
                ],
                EveryTick = true
            };
            probes.Add(probe);
            Spawn(world, ground, from, StaticBox(0.25f), probe);
        }

        Tick(world, ticks);

        var heard = new List<string>();
        foreach (Probe probe in probes)
        {
            foreach ((long tick, ShapeHit hit) in probe.ShapeAnswers)
                heard.Add(
                    $"{tick} cast {hit.Tag} {hit.Entity} {BitConverter.DoubleToInt64Bits(hit.Point.X)} {BitConverter.DoubleToInt64Bits(hit.Point.Y)} " +
                    $"{BitConverter.DoubleToInt64Bits(hit.Point.Z)} {BitConverter.SingleToInt32Bits(hit.Distance)} {hit.Normal.X:R} {hit.Normal.Y:R} {hit.Normal.Z:R}");
            foreach ((long tick, OverlapHit hit) in probe.OverlapAnswers)
                heard.Add($"{tick} overlap {hit.Tag} {hit.Entity}");
        }

        return heard;
    }

    [Fact]
    public void TheSameWorldGivesTheSameShapeHitsAndOverlapsInEveryRun()
    {
        List<string> first = AnswersOfASceneOfMany(1, 6);
        List<string> second = AnswersOfASceneOfMany(1, 6);
        List<string> parallel = AnswersOfASceneOfMany(4, 6);

        first.Should().HaveCountGreaterThan(8 * 5 * 2,
            "every asker hears at least one answer to each query on each tick after the first, and some overlaps find several entities");
        first.Where(line => line.Contains("cast") && !line.Contains("Entity(null)")).Should().NotBeEmpty();
        first.Where(line => line.Contains("overlap") && !line.Contains("Entity(null)")).Should().NotBeEmpty();
        second.Should().Equal(first);
        parallel.Should().Equal(first);
    }

    [Fact]
    public void AShapeCastOrOverlapRequestAllocatesNothingOnceTheWorldIsWarm()
    {
        using World world = NewWorld(out _, debugChecks: false);
        FrameHandle ground = Ground(world, 0);
        FrameHandle child = TurnedChildFrame(world, ground);
        for (int i = 0; i < 16; i++)
            Spawn(world, ground, new(5 + 9 * i, 0, 0), StaticBox(), new Crew());
        Spawn(world, child, new(0, 0, 5), StaticBox(), new Crew());
        const int Askers = 8;
        var probes = new List<Probe>();
        for (int i = 0; i < Askers; i++)
        {
            var from = new Double3(0, 0.01 * i, 0);
            var probe = new Probe
            {
                Casts =
                [
                    new(QueryShape.Capsule(0.3f, 0.2f), new(from, DQuaternion.Identity), Double3.UnitX, 300,
                        EntityQuery.With<Crew>().Near(500), Tag: 1)
                ],
                Overlaps =
                [
                    new(QueryShape.Box(new(30, 1, 1)), new(new(30, 0, 0), DQuaternion.Identity), default, Tag: 2)
                ],
                EveryTick = true,
                Keeps = false
            };
            probes.Add(probe);
            Spawn(world, ground, from, null, probe);
        }

        Tick(world, 60);

        // As for rays: each window collects garbage every 10 ticks.
        const int Ticks = 200;
        int heardBefore = probes.Sum(p => p.Heard);
        int windows = 0;
        long allocated = Allocations.Fewest(() =>
        {
            windows++;
            TickCollectingGarbage(world, Ticks);
        });

        allocated.Should().Be(0, "a shape request must not allocate once the world is warm");
        // One cast answer and the 7 boxes from x = 5 to x = 59 in the overlap, for each asker on each tick: real queries.
        (probes.Sum(p => p.Heard) - heardBefore).Should().Be(windows * Ticks * Askers * 8);
        world.Errors.Should().BeEmpty();
    }
}
