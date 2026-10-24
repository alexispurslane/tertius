using System.Numerics;
using Tertius.Simulation;
using static Tertius.Testing.WorldTesting;

namespace Tertius.Physics.Tests;

/// <summary>
/// Ray casts from components, on the real World and Jolt. A component asks with <see cref="Self.Physics"/> during a
/// tick, the sync point casts the ray against Jolt and the snapshot of the end of that tick, and the answer, a
/// <see cref="RayHit"/>, arrives in the next tick. Every expected distance, point and normal comes from the geometry of
/// the scene, worked out in the test.
/// </summary>
public class WorldRayCastTests
{
    private const double Tolerance = 1e-4;

    /// <summary>A crew member, for the queries that filter by component.</summary>
    private sealed class Crew : IComponent
    {
        public int Priority => 0;
        public EventMask Wants => default;

        public void Handle(Self self, scoped ref EventList events, scoped ref Outbox outbox)
        {
        }
    }

    /// <summary>One ray that a component asks for, on tick <see cref="OnTick"/>.</summary>
    private sealed record Ray(
        Double3 From,
        Double3 Direction,
        double Length,
        EntityQuery Query,
        int Tag,
        bool All = false,
        long OnTick = 0);

    /// <summary>Asks for its rays on their ticks, and keeps every RayHit that it hears, with the tick it heard it on.</summary>
    private sealed class Aim(params Ray[] rays) : IComponent
    {
        public List<(long Tick, RayHit Hit)> Answers { get; } = [];

        /// <summary>When set, the component asks for every ray on every tick.</summary>
        public bool EveryTick { get; init; }

        /// <summary>When cleared, the component counts the answers but keeps none, so that it allocates nothing itself.</summary>
        public bool Keeps { get; init; } = true;

        /// <summary>How many answers the component heard.</summary>
        public int Heard { get; private set; }

        public int Priority => 0;
        public EventMask Wants => EventMask.Of<Tick>().And<RayHit>();

        public void Handle(Self self, scoped ref EventList events, scoped ref Outbox outbox)
        {
            foreach (ref RayHit hit in events.Of<RayHit>())
            {
                Heard++;
                if (Keeps)
                    Answers.Add((self.Tick, hit));
            }

            foreach (Ray ray in rays)
            {
                if (!EveryTick && ray.OnTick != self.Tick)
                    continue;
                if (ray.All)
                    self.Physics.CastRayAll(ray.From, ray.Direction, ray.Length, ray.Query, ray.Tag);
                else
                    self.Physics.CastRay(ray.From, ray.Direction, ray.Length, ray.Query, ray.Tag);
            }
        }

        public RayHit[] AnswersTo(int tag) => [.. Answers.Where(a => a.Hit.Tag == tag).Select(a => a.Hit)];
    }

    private static BodyDesc StaticBox(float half = 0.5f) => new(new BoxShapeDesc(new(half)), BodyMotion.Static);

    private static Entity Spawn(World world, FrameHandle frame, Double3 at, BodyDesc? body,
        params IComponent[] components) =>
        world.Spawn(new()
            { Frame = frame, Pose = new(at, DQuaternion.Identity), Body = body, Components = components });

    private static void ShouldBeAt(RayHit hit, Entity entity, Double3 point, Vector3 normal, double distance)
    {
        hit.Entity.Should().Be(entity);
        (hit.Point - point).Length.Should().BeLessThan(Tolerance, $"the hit point {hit.Point} should be {point}");
        Vector3.Distance(hit.Normal, normal).Should().BeLessThan(1e-4f, $"the normal {hit.Normal} should be {normal}");
        ((double)hit.Distance).Should().BeApproximately(distance, Tolerance);
    }

    [Fact]
    public void ARayAnswersInTheNextTickWithTheNearestBodyItsPointItsNormalAndItsDistance()
    {
        using World world = NewWorld(out _);
        FrameHandle ground = Ground(world, 0);
        var aim = new Aim(new Ray(Double3.Zero, Double3.UnitX, 50, default, Tag: 4));
        Spawn(world, ground, Double3.Zero, null, aim);
        Entity near = Spawn(world, ground, new(10, 0, 0), StaticBox());
        Spawn(world, ground, new(20, 0, 0), StaticBox());

        Tick(world, 3);

        aim.Answers.Should().ContainSingle();
        aim.Answers[0].Tick.Should().Be(1, "the ray asked on tick 0, and its answer arrives with the next tick");
        ShouldBeAt(aim.Answers[0].Hit, near, new(9.5, 0, 0), -Vector3.UnitX, 9.5);
        aim.Answers[0].Hit.Tag.Should().Be(4);
        world.Errors.Should().BeEmpty();
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ARayWithADirectionThatIsNotUnitLengthHitsAtADistanceInMeters(bool all)
    {
        // The direction (4, 0, 0) is four meters long. Its hit still lies 9.5 m along the ray, not 9.5 times 4 m.
        using World world = NewWorld(out _);
        FrameHandle ground = Ground(world, 0);
        var aim = new Aim(new Ray(Double3.Zero, new(4, 0, 0), 50, default, Tag: 4, All: all));
        Spawn(world, ground, Double3.Zero, null, aim);
        Entity near = Spawn(world, ground, new(10, 0, 0), StaticBox());

        Tick(world, 2);

        ShouldBeAt(aim.AnswersTo(4).Should().ContainSingle().Subject, near, new(9.5, 0, 0), -Vector3.UnitX, 9.5);
        world.Errors.Should().BeEmpty();
    }

    [Fact]
    public void ARayWithADirectionThatIsNotUnitLengthMissesAtTheEndOfItsLengthInMeters()
    {
        using World world = NewWorld(out _);
        FrameHandle ground = Ground(world, 0);
        var aim = new Aim(new Ray(new(1, 2, 3), new(0, 3, 0), 50, default, Tag: 9));
        Spawn(world, ground, new(1, 2, 3), null, aim);

        Tick(world, 2);

        RayHit miss = aim.AnswersTo(9).Should().ContainSingle().Subject;
        miss.IsHit.Should().BeFalse();
        (miss.Point - new Double3(1, 52, 3)).Length.Should().BeLessThan(1e-9);
        miss.Distance.Should().Be(50);
    }

    [Fact]
    public void ARayThatMeetsNothingAnswersWithAMissAtTheEndOfTheRay()
    {
        using World world = NewWorld(out _);
        FrameHandle ground = Ground(world, 0);
        var aim = new Aim(new Ray(new(1, 2, 3), Double3.UnitY, 50, default, Tag: 9));
        Spawn(world, ground, new(1, 2, 3), null, aim);
        Spawn(world, ground, new(10, 0, 0), StaticBox());

        Tick(world, 2);

        RayHit miss = aim.AnswersTo(9).Should().ContainSingle().Subject;
        miss.IsHit.Should().BeFalse();
        miss.Entity.Should().Be(Entity.Null);
        (miss.Point - new Double3(1, 52, 3)).Length.Should().BeLessThan(1e-9);
        miss.Distance.Should().Be(50);
        miss.Normal.Should().Be(Vector3.Zero);
    }

    [Fact]
    public void AQueryByComponentSkipsTheBodiesOfEntitiesWithoutTheComponent()
    {
        using World world = NewWorld(out _);
        FrameHandle ground = Ground(world, 0);
        var aim = new Aim(new Ray(Double3.Zero, Double3.UnitX, 50, EntityQuery.With<Crew>(), Tag: 1));
        Spawn(world, ground, Double3.Zero, null, aim);
        Spawn(world, ground, new(10, 0, 0), StaticBox());
        Entity crew = Spawn(world, ground, new(20, 0, 0), StaticBox(), new Crew());

        Tick(world, 2);

        ShouldBeAt(aim.AnswersTo(1).Single(), crew, new(19.5, 0, 0), -Vector3.UnitX, 19.5);
    }

    [Fact]
    public void AQueryByCollisionGroupSkipsTheBodiesOutsideTheGroup()
    {
        var registry = new CollisionGroupRegistry();
        CollisionGroup glass = registry.Register("Glass");
        using World world = NewWorld(out _);
        FrameHandle ground = Ground(world, 0);
        var aim = new Aim(new Ray(Double3.Zero, Double3.UnitX, 50, EntityQuery.InAny(CollisionGroup.Solid), Tag: 1));
        Spawn(world, ground, Double3.Zero, null, aim);
        Spawn(world, ground, new(10, 0, 0), StaticBox() with { CollisionGroups = CollisionGroups.Of(glass) });
        Entity wall = Spawn(world, ground, new(20, 0, 0), StaticBox());

        Tick(world, 2);

        ShouldBeAt(aim.AnswersTo(1).Single(), wall, new(19.5, 0, 0), -Vector3.UnitX, 19.5);
    }

    [Fact]
    public void ARaySkipsTheBodyOfTheEntityThatAsked()
    {
        // The ray starts inside the asker's own box, which Jolt treats as solid, so without the skip it would hit the
        // asker at once.
        using World world = NewWorld(out _);
        FrameHandle ground = Ground(world, 0);
        var aim = new Aim(new Ray(Double3.Zero, Double3.UnitX, 50, default, Tag: 1));
        Spawn(world, ground, Double3.Zero, StaticBox(2), aim);
        Entity target = Spawn(world, ground, new(10, 0, 0), StaticBox());

        Tick(world, 2);

        ShouldBeAt(aim.AnswersTo(1).Single(), target, new(9.5, 0, 0), -Vector3.UnitX, 9.5);
    }

    [Fact]
    public void ARayThatStartsInsideAMeshHullPassesOutThroughItButARayFromOutsideHitsIt()
    {
        using World world = NewWorld(out _);
        FrameHandle ground = Ground(world, 0);
        var inside = new Aim(new Ray(Double3.Zero, Double3.UnitX, 50, default, Tag: 1));
        var outside = new Aim(new Ray(new(-10, 0, 0), Double3.UnitX, 50, default, Tag: 2));
        Spawn(world, ground, Double3.Zero, null, inside);
        Spawn(world, ground, new(-10, 0, 0), null, outside);
        Entity hull = Spawn(world, ground, Double3.Zero, new(MeshCube(3), BodyMotion.Static));
        Entity target = Spawn(world, ground, new(10, 0, 0), StaticBox());

        Tick(world, 2);

        ShouldBeAt(inside.AnswersTo(1).Single(), target, new(9.5, 0, 0), -Vector3.UnitX, 9.5);
        ShouldBeAt(outside.AnswersTo(2).Single(), hull, new(-3, 0, 0), -Vector3.UnitX, 7);
    }

    [Fact]
    public void CastRayAllAnswersWithEveryEntityAlongTheRayByDistance()
    {
        using World world = NewWorld(out _);
        FrameHandle ground = Ground(world, 0);
        var aim = new Aim(new Ray(Double3.Zero, Double3.UnitX, 50, default, Tag: 3, All: true));
        Spawn(world, ground, Double3.Zero, null, aim);
        // Spawned far to near, so that entity order is not distance order.
        Entity far = Spawn(world, ground, new(30, 0, 0), StaticBox());
        Entity hull = Spawn(world, ground, new(20, 0, 0), new(MeshCube(1), BodyMotion.Static));
        Entity near = Spawn(world, ground, new(10, 0, 0), StaticBox());
        Spawn(world, ground, new(60, 0, 0), StaticBox());

        Tick(world, 2);

        RayHit[] hits = aim.AnswersTo(3);
        hits.Select(h => h.Entity).Should().Equal(near, hull, far);
        ShouldBeAt(hits[0], near, new(9.5, 0, 0), -Vector3.UnitX, 9.5);
        ShouldBeAt(hits[1], hull, new(19, 0, 0), -Vector3.UnitX, 19);
        ShouldBeAt(hits[2], far, new(29.5, 0, 0), -Vector3.UnitX, 29.5);
    }

    [Fact]
    public void CastRayAllWithNothingAlongTheRayAnswersWithOneMiss()
    {
        using World world = NewWorld(out _);
        FrameHandle ground = Ground(world, 0);
        var aim = new Aim(new Ray(Double3.Zero, Double3.UnitY, 50, default, Tag: 3, All: true));
        Spawn(world, ground, Double3.Zero, null, aim);
        Spawn(world, ground, new(10, 0, 0), StaticBox());

        Tick(world, 2);

        aim.AnswersTo(3).Should().ContainSingle().Which.IsHit.Should().BeFalse();
    }

    /// <summary>
    /// A static frame at (100, 0, 0) in <paramref name="parent"/>, turned by 90° about +Y, so that its +Z is the parent's
    /// +X, with a bubble of 20 m. Its owner has no body, so the parent's system holds nothing of it.
    /// </summary>
    private static FrameHandle TurnedChildFrame(World world, FrameHandle parent)
    {
        Entity owner = world.Spawn(new()
        {
            Frame = parent,
            Pose = new(new(100, 0, 0), DQuaternion.FromAxisAngle(Double3.UnitY, Math.PI / 2)),
            AsFrame = new(FrameBounds.Sphere(20))
        });
        return world.GetOwnedFrame(owner);
    }

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

    [Fact]
    public void ARayThatCrossesIntoTheBubbleOfAChildFrameHitsABodyInThatFrame()
    {
        using World world = NewWorld(out _);
        FrameHandle ground = Ground(world, 0);
        FrameHandle child = TurnedChildFrame(world, ground);
        var aim = new Aim(new Ray(Double3.Zero, Double3.UnitX, 200, default, Tag: 1));
        Spawn(world, ground, Double3.Zero, null, aim);
        // At (0, 0, 5) in the child, which is (105, 0, 0) in the ground. Its face toward the ray is the child's -Z face.
        Entity inside = Spawn(world, child, new(0, 0, 5), StaticBox());

        Tick(world, 2);

        ShouldBeAt(aim.AnswersTo(1).Single(), inside, new(104.5, 0, 0), -Vector3.UnitX, 104.5);
        world.Errors.Should().BeEmpty();
    }

    [Fact]
    public void ARayFromTheParentHitsABodyOfAChildFrameThatLiesJustOutsideTheChildsBubble()
    {
        // A static box of the turned child, 21 m along the child's -Z: (79, 0, 0) in the ground. Its near face, at x = 78.5,
        // is 1.5 m outside the bubble, which starts at x = 80. A static body never leaves its frame and has no ghost, so
        // only the child's system holds it.
        using World world = NewWorld(out _);
        FrameHandle ground = Ground(world, 0);
        FrameHandle child = TurnedChildFrame(world, ground);
        var aim = new Aim(new Ray(Double3.Zero, Double3.UnitX, 200, default, Tag: 1));
        Spawn(world, ground, Double3.Zero, null, aim);
        Entity outside = Spawn(world, child, new(0, 0, -21), StaticBox());

        Tick(world, 2);

        ShouldBeAt(aim.AnswersTo(1).Single(), outside, new(78.5, 0, 0), -Vector3.UnitX, 78.5);
        world.Errors.Should().BeEmpty();
    }

    [Fact]
    public void CastRayAllReportsABodyOfAChildFrameThatStraddlesTheBubbleOnceAtItsNearFace()
    {
        // A static box with faces 1 m from its center, 20 m along the child's -Z: (80, 0, 0) in the ground, across the edge
        // of the bubble. Its near face is at x = 79, outside the bubble, and a ray that starts inside a box hits it at once.
        using World world = NewWorld(out _);
        FrameHandle ground = Ground(world, 0);
        FrameHandle child = TurnedChildFrame(world, ground);
        var aim = new Aim(new Ray(Double3.Zero, Double3.UnitX, 200, default, Tag: 1, All: true));
        Spawn(world, ground, Double3.Zero, null, aim);
        Entity straddler = Spawn(world, child, new(0, 0, -20), StaticBox(1));
        Entity inside = Spawn(world, child, new(0, 0, 5), StaticBox());

        Tick(world, 2);

        RayHit[] hits = aim.AnswersTo(1);
        hits.Select(h => h.Entity).Should().Equal(straddler, inside);
        ShouldBeAt(hits[0], straddler, new(79, 0, 0), -Vector3.UnitX, 79);
    }

    [Fact]
    public void AMovingBodyThatStraddlesTheBubbleIsInBothSystemsAndCastRayAllReportsItOnce()
    {
        // A cube at rest across the edge of the child's bubble, with no gravity: the child's system holds it, and from the
        // first step on the ground's system holds its ghost.
        using World world = NewWorld(out JoltPhysicsBackend backend);
        FrameHandle ground = Ground(world, 0);
        FrameHandle child = TurnedChildFrame(world, ground);
        var aim = new Aim(new Ray(Double3.Zero, Double3.UnitX, 200, default, Tag: 1, All: true, OnTick: 2));
        Spawn(world, ground, Double3.Zero, null, aim);
        Entity cube = Spawn(world, child, new(0, 0, -20), Cube());

        Tick(world, 4);

        world.Snapshot.FrameOf(cube).Should().Be(child);
        backend.BodiesIn(ground).Should().Contain(
            f => BodyUserData.Kind(f.UserData) == BodyKind.Ghost && BodyUserData.Names(f.UserData, cube),
            "the ground must hold a ghost of the cube for this test to mean anything");
        RayHit hit = aim.AnswersTo(1).Should().ContainSingle().Subject;
        ShouldBeAt(hit, cube, new(79.5, 0, 0), -Vector3.UnitX, 79.5);
    }

    [Fact]
    public void ARayFromAFrameHitsABodyOfASiblingFrameThatLiesInsideTheBubbleTheRayStartsIn()
    {
        // Two frames side by side in the ground: the asker's at (100, 0, 0), with its bubble from x = 80 to x = 120, and a
        // sibling at (150, 0, 0). The sibling's static box at (-35, 0, 0) in the sibling is (115, 0, 0) in the ground,
        // inside the asker's bubble, and its near face is 14.5 m from the asker.
        using World world = NewWorld(out _);
        FrameHandle ground = Ground(world, 0);
        FrameHandle home = world.GetOwnedFrame(world.Spawn(new()
        {
            Frame = ground, Pose = At(100), AsFrame = new(FrameBounds.Sphere(20))
        }));
        FrameHandle sibling = world.GetOwnedFrame(world.Spawn(new()
        {
            Frame = ground, Pose = At(150), AsFrame = new(FrameBounds.Sphere(20))
        }));
        var aim = new Aim(new Ray(Double3.Zero, Double3.UnitX, 100, default, Tag: 1));
        Spawn(world, home, Double3.Zero, null, aim);
        Entity box = Spawn(world, sibling, new(-35, 0, 0), StaticBox());

        Tick(world, 2);

        ShouldBeAt(aim.AnswersTo(1).Single(), box, new(14.5, 0, 0), -Vector3.UnitX, 14.5);
    }

    [Fact]
    public void ARayThatLeavesTheBubbleOfItsFrameHitsABodyInTheParentFrameInTheAxesOfTheAsker()
    {
        using World world = NewWorld(out _);
        FrameHandle ground = Ground(world, 0);
        FrameHandle child = TurnedChildFrame(world, ground);
        // From the child's origin along its +Z, which is the ground's +X.
        var aim = new Aim(new Ray(Double3.Zero, Double3.UnitZ, 100, default, Tag: 1));
        Spawn(world, child, Double3.Zero, null, aim);
        Entity outside = Spawn(world, ground, new(150, 0, 0), StaticBox());

        Tick(world, 2);

        // The face at x = 149.5 in the ground is 49.5 m along the child's +Z, and its normal is the child's -Z.
        ShouldBeAt(aim.AnswersTo(1).Single(), outside, new(0, 0, 49.5), -Vector3.UnitZ, 49.5);
    }

    [Fact]
    public void CastRayAllMergesTheHitsOfEveryFrameItCrossesByDistanceWithEachEntityOnce()
    {
        using World world = NewWorld(out _);
        FrameHandle ground = Ground(world, 0);
        // A turned frame at (100, 0, 0) whose owner's static hull is a cube with faces 2 m from its center. Its interior is
        // the same cube in the frame's own system, so the ray meets the owner in both systems at the same face.
        Entity owner = world.Spawn(new()
        {
            Frame = ground,
            Pose = new(new(100, 0, 0), DQuaternion.FromAxisAngle(Double3.UnitY, Math.PI / 2)),
            Body = StaticBox(2),
            AsFrame = new(FrameBounds.Sphere(20))
        });
        FrameHandle child = world.GetOwnedFrame(owner);
        var aim = new Aim(new Ray(Double3.Zero, Double3.UnitX, 200, default, Tag: 5, All: true));
        Spawn(world, ground, Double3.Zero, null, aim);
        Entity near = Spawn(world, ground, new(50, 0, 0), StaticBox());
        Entity far = Spawn(world, ground, new(150, 0, 0), StaticBox());
        // At (0, 0, 5) in the child: (105, 0, 0) in the ground, between the hull and the far box.
        Entity inside = Spawn(world, child, new(0, 0, 5), StaticBox());

        Tick(world, 2);

        RayHit[] hits = aim.AnswersTo(5);
        hits.Select(h => h.Entity).Should().Equal(near, owner, inside, far);
        ShouldBeAt(hits[1], owner, new(98, 0, 0), -Vector3.UnitX, 98);
        ShouldBeAt(hits[2], inside, new(104.5, 0, 0), -Vector3.UnitX, 104.5);
        ShouldBeAt(hits[3], far, new(149.5, 0, 0), -Vector3.UnitX, 149.5);
    }

    [Fact]
    public void ARayThatMeetsAFarBodyInTheParentAndANearerBodyInAChildFrameNamesTheNearerBody()
    {
        // The ground's system answers first, with the far box. The child's system then answers with a nearer box, and the
        // nearer one is the ray's hit.
        using World world = NewWorld(out _);
        FrameHandle ground = Ground(world, 0);
        FrameHandle child = TurnedChildFrame(world, ground);
        var aim = new Aim(new Ray(Double3.Zero, Double3.UnitX, 200, default, Tag: 1));
        Spawn(world, ground, Double3.Zero, null, aim);
        Spawn(world, ground, new(150, 0, 0), StaticBox());
        // At (0, 0, 5) in the child, which is (105, 0, 0) in the ground.
        Entity nearer = Spawn(world, child, new(0, 0, 5), StaticBox());

        Tick(world, 2);

        ShouldBeAt(aim.AnswersTo(1).Single(), nearer, new(104.5, 0, 0), -Vector3.UnitX, 104.5);
        world.Errors.Should().BeEmpty();
    }

    [Fact]
    public void ARayDoesNotLookInAChildFrameThatLiesBeyondTheNearestHitItHasAlreadyFound()
    {
        // Three places along +X: the ground's box at x = 150, a near frame around x = 40, and a far frame around x = 120.
        // The far frame is made first, so the ray's walk visits it after the near frame. The ground's system answers with
        // the box at x = 150, the near frame's system then answers with a nearer hit, and the far frame's part of the ray,
        // from about x = 110, starts beyond that hit. So Jolt is never asked about the far frame's body.
        var asked = new List<Entity>();
        using World world = NewWorld(out _);
        FrameHandle ground = Ground(world, 0);
        FrameHandle farFrame = Bubble(world, ground, new(120, 0, 0), 10);
        FrameHandle nearFrame = Bubble(world, ground, new(40, 0, 0), 10);
        var aim = new Aim(new Ray(Double3.Zero, Double3.UnitX, 200, Recording(asked), Tag: 1));
        Spawn(world, ground, Double3.Zero, null, aim);
        Entity groundBox = Spawn(world, ground, new(150, 0, 0), StaticBox());
        Entity farBody = Spawn(world, farFrame, Double3.Zero, StaticBox());
        Entity nearBody = Spawn(world, nearFrame, Double3.Zero, StaticBox());

        Tick(world, 2);

        ShouldBeAt(aim.AnswersTo(1).Single(), nearBody, new(39.5, 0, 0), -Vector3.UnitX, 39.5);
        asked.Should().Contain([groundBox, nearBody], "the ray looked in the ground and in the near frame");
        asked.Should().NotContain(farBody, "the near frame's hit ends the ray's search before the far frame");
        world.Errors.Should().BeEmpty();
    }

    [Fact]
    public void ARayThatPassesBesideTheBubbleOfAChildFrameDoesNotHitItsBodiesAndFindsTheBodyBeyondInTheParent()
    {
        // The ray runs along +X, 25 m above the child's center, while the child's bubble has a radius of 20 m.
        using World world = NewWorld(out _);
        FrameHandle ground = Ground(world, 0);
        FrameHandle child = TurnedChildFrame(world, ground);
        var aim = new Aim(new Ray(new(0, 25, 0), Double3.UnitX, 300, default, Tag: 1));
        Spawn(world, ground, new(0, 25, 0), null, aim);
        Spawn(world, child, new(0, 0, 5), StaticBox());
        Entity beyond = Spawn(world, ground, new(200, 25, 0), StaticBox());

        Tick(world, 2);

        ShouldBeAt(aim.AnswersTo(1).Single(), beyond, new(199.5, 25, 0), -Vector3.UnitX, 199.5);
        world.Errors.Should().BeEmpty();
    }

    [Fact]
    public void CastRayAllThatPassesBesideOneChildFrameGoesOnToTheNextChildFrameAndTheParent()
    {
        // The beside frame is made first, so the ray's walk meets it first among the ground's child frames. Passing beside
        // its bubble must not end the walk: the ray still crosses the bubble of the frame after it.
        using World world = NewWorld(out _);
        FrameHandle ground = Ground(world, 0);
        FrameHandle beside = Bubble(world, ground, new(100, 40, 0), 20);
        FrameHandle crossed = Bubble(world, ground, new(140, 0, 0), 20);
        var aim = new Aim(new Ray(Double3.Zero, Double3.UnitX, 300, default, Tag: 1, All: true));
        Spawn(world, ground, Double3.Zero, null, aim);
        Spawn(world, beside, Double3.Zero, StaticBox());
        Entity inCrossed = Spawn(world, crossed, Double3.Zero, StaticBox());
        Entity inGround = Spawn(world, ground, new(200, 0, 0), StaticBox());

        Tick(world, 2);

        RayHit[] hits = aim.AnswersTo(1);
        hits.Select(h => h.Entity).Should().Equal(inCrossed, inGround);
        ShouldBeAt(hits[0], inCrossed, new(139.5, 0, 0), -Vector3.UnitX, 139.5);
        ShouldBeAt(hits[1], inGround, new(199.5, 0, 0), -Vector3.UnitX, 199.5);
    }

    [Fact]
    public void ARayCrossesIntoAChildFrameWhenTheWorldHasMoreFramesThanTheFirstTableOfContentScalesHolds()
    {
        // The runner keeps the content scale of each frame in a table of 16 entries, by the frame's slot, and grows it
        // when a frame past the end is looked at. The ray looks at every child of the ground, so it reaches the frames
        // that come after the 16th while it walks. These frames lie far above the ray, and the child comes after them.
        using World world = NewWorld(out _);
        FrameHandle ground = Ground(world, 0);
        for (int i = 0; i < 20; i++)
            Bubble(world, ground, new(0, 1000 + 100 * i, 0), 10);
        FrameHandle child = TurnedChildFrame(world, ground);
        var aim = new Aim(new Ray(Double3.Zero, Double3.UnitX, 200, default, Tag: 1));
        Spawn(world, ground, Double3.Zero, null, aim);
        Entity inside = Spawn(world, child, new(0, 0, 5), StaticBox());

        Tick(world, 2);

        child.Index.Should()
            .BeGreaterThan(16, "the child must come after the 16th frame for this test to mean anything");
        ShouldBeAt(aim.AnswersTo(1).Single(), inside, new(104.5, 0, 0), -Vector3.UnitX, 104.5);
        world.Errors.Should().BeEmpty();
    }

    [Fact]
    public void ARayFromInsideAShipPassesOutThroughItsSolidHullToTheBodiesBeyond()
    {
        // The hull is a solid box in the ground's system, and the ray starts inside it. The ground's part of the ray starts
        // where the ray leaves the ship's bubble, so the hull does not stop it.
        using World world = NewWorld(out _);
        FrameHandle ground = Ground(world, 0);
        Scenes.ShipScene ship = Scenes.ShipWithInterior(world, ground);
        var aim = new Aim(new Ray(Double3.Zero, Double3.UnitX, 100, default, Tag: 1));
        Spawn(world, ship.Interior, Double3.Zero, null, aim);
        Entity beyond = Spawn(world, ground, new(80, 0, 0), StaticBox());

        Tick(world, 2);

        ShouldBeAt(aim.AnswersTo(1).Single(), beyond, new(79.5, 0, 0), -Vector3.UnitX, 79.5);
    }

    /// <summary>Destroys its own entity on tick <paramref name="onTick"/>.</summary>
    private sealed class DestroySelf(long onTick) : IComponent
    {
        public int Priority => 1;
        public EventMask Wants => EventMask.Of<Tick>();

        public void Handle(Self self, scoped ref EventList events, scoped ref Outbox outbox)
        {
            if (self.Tick == onTick)
                self.Destroy();
        }
    }

    [Fact]
    public void AnAskerThatIsDestroyedInTheTickItAsksGetsNoAnswerAndCausesNoError()
    {
        using World world = NewWorld(out _);
        FrameHandle ground = Ground(world, 0);
        var aim = new Aim(new Ray(Double3.Zero, Double3.UnitX, 50, default, Tag: 1));
        Spawn(world, ground, Double3.Zero, null, aim, new DestroySelf(0));
        Spawn(world, ground, new(10, 0, 0), StaticBox());

        Tick(world, 3);

        aim.Answers.Should().BeEmpty();
        world.Errors.Should().BeEmpty();
    }

    [Fact]
    public void ARayWhoseFrameDiesInTheTickItAsksAnswersWithAMissAtAZeroPoint()
    {
        using World world = NewWorld(out _);
        FrameHandle ground = Ground(world, 0);
        Entity owner = world.Spawn(new()
        {
            Frame = ground,
            Pose = new(new(100, 0, 0), DQuaternion.Identity),
            AsFrame = new(FrameBounds.Sphere(20)),
            Components = [new DestroySelf(0)]
        });
        FrameHandle child = world.GetOwnedFrame(owner);
        var aim = new Aim(new Ray(Double3.Zero, Double3.UnitX, 50, default, Tag: 1));
        Entity asker = Spawn(world, child, Double3.Zero, null, aim);
        Spawn(world, child, new(10, 0, 0), StaticBox());

        Tick(world, 3);

        world.Snapshot.FrameOf(asker).Should().Be(ground, "the asker moved up when its frame died");
        RayHit miss = aim.AnswersTo(1).Should().ContainSingle().Subject;
        miss.IsHit.Should().BeFalse();
        miss.Point.Should().Be(Double3.Zero);
        miss.Distance.Should().Be(50);
    }

    [Fact]
    public void AnAnswerArrivesInTheFrameThatTheAskerIsInWhenItArrives()
    {
        // A ball asks on tick 2, from the ground, and physics hands it into the turned child frame in the step of that tick.
        using World world = NewWorld(out _);
        FrameHandle ground = Ground(world, 0);
        FrameHandle child = TurnedChildFrame(world, ground);
        var aim = new Aim(new Ray(new(0, 10, 0), Double3.UnitX, 100, default, Tag: 1, OnTick: 2));
        Entity ball = Scenes.BallApproachingBubble(world, ground, child, 2, aim);
        Entity box = Spawn(world, ground, new(50, 10, 0), StaticBox());

        Tick(world, 4);

        world.Snapshot.FrameOf(ball).Should().Be(child);
        aim.Answers.Should().ContainSingle().Which.Tick.Should().Be(3);
        // The face at (49.5, 10, 0) in the ground is (0, 10, -50.5) in the child, and the ground's -X is the child's -Z.
        ShouldBeAt(aim.Answers[0].Hit, box, new(0, 10, -50.5), -Vector3.UnitZ, 49.5);
    }

    private sealed class BadRay(Double3 from, Double3 direction, double length) : IComponent
    {
        public int Priority => 0;
        public EventMask Wants => EventMask.Of<Tick>();

        public void Handle(Self self, scoped ref EventList events, scoped ref Outbox outbox) =>
            self.Physics.CastRay(from, direction, length, default, 1);
    }

    [Theory]
    [InlineData(double.NaN, 1, 10, "origin")]
    [InlineData(0, 0, 10, "direction")]
    [InlineData(0, 1, 0, "length")]
    [InlineData(0, 1, double.PositiveInfinity, "length")]
    public void ARayWithAnOriginDirectionOrLengthThatIsNotUsableIsRefusedAtOnce(double x, double dx, double length,
        string what)
    {
        using World world = NewWorld(out _);
        FrameHandle ground = Ground(world, 0);
        var aim = new Aim();
        Spawn(world, ground, Double3.Zero, null, aim, new BadRay(new(x, 0, 0), new(dx, 0, 0), length));

        Tick(world, 2);

        world.Errors.Should().Contain(e => e.Contains("BadRay") && e.Contains(what));
        aim.Answers.Should().BeEmpty();
    }

    [Fact]
    public void TheOwnerOfARootFrameIsInNoFrameAndCannotCastARay()
    {
        using World world = NewWorld(out _);
        FrameHandle ground = Ground(world, 0);
        world.AddComponent(ground.Entity, new BadRay(Double3.Zero, Double3.UnitX, 10));

        Tick(world, 1);

        world.Errors.Should().Contain(e => e.Contains("BadRay") && e.Contains("no frame"));
    }

    [Fact]
    public void AQueryWhosePredicateThrowsReportsTheErrorAndGivesNoAnswerWhileTheWorldGoesOn()
    {
        // Jolt calls the filter from native code, which an exception must never cross.
        using World world = NewWorld(out _);
        FrameHandle ground = Ground(world, 0);
        EntityQuery throws =
            EntityQuery.Where(static (_, _) => throw new InvalidOperationException("the predicate broke"));
        var broken = new Aim(new Ray(Double3.Zero, Double3.UnitX, 50, throws, Tag: 1));
        var fine = new Aim(new Ray(new(0, 0.1, 0), Double3.UnitX, 50, default, Tag: 2));
        Spawn(world, ground, Double3.Zero, null, broken);
        Spawn(world, ground, new(0, 0.1, 0), null, fine);
        Entity box = Spawn(world, ground, new(10, 0, 0), StaticBox());

        Tick(world, 3);

        world.Errors.Should().ContainSingle().Which.Should().Contain("the predicate broke");
        broken.Answers.Should().BeEmpty();
        fine.AnswersTo(2).Should().ContainSingle().Which.Entity.Should().Be(box);
    }

    [Fact]
    public void ARayInAFrameHitsABodyThatPhysicsKeepsInANumericFrameAndAnswersInTheSemanticFrame()
    {
        using World world = NewWorld(out _);
        FrameHandle ground = Ground(world, 0);
        // A crate at rest 10,000 km out, beyond the 2^23 m (8,389 km) of NumericFrameOptions.CreateDistance, moves into a
        // resting numeric frame of its own.
        const double far = 1e7;
        Entity crate = Spawn(world, ground, new(far, 0, 0), Cube());
        var aim = new Aim(new Ray(new(far - 10, 0, 0), Double3.UnitX, 50, default, Tag: 1, OnTick: 3));
        Spawn(world, ground, new(far - 10, 0, 0), null, aim);

        Tick(world, 5);

        world.Frames.Numeric(world.PhysicsHomeOf(crate)).Should().Be(NumericMode.Resting,
            "the crate must be in a numeric frame for this test to mean anything");
        ShouldBeAt(aim.AnswersTo(1).Single(), crate, new(far - 0.5, 0, 0), -Vector3.UnitX, 9.5);
    }

    /// <summary>
    /// A scene with bodies in a frame and in a child frame, and eight askers that each cast both kinds of ray every tick.
    /// Returns every answer that they heard, in the order heard, as raw bits.
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
        var askers = new List<Aim>();
        for (int i = 0; i < 8; i++)
        {
            var from = new Double3(0, i % 5 - 2, i % 3 - 1);
            Double3 direction = new Double3(1, 0.002 * i, -0.001 * i).Normalized();
            var aim = new Aim(
                new Ray(from, direction, 300, default, Tag: 1),
                new Ray(from, direction, 300, EntityQuery.With<Crew>(), Tag: 2, All: true)) { EveryTick = true };
            askers.Add(aim);
            Spawn(world, ground, from, StaticBox(0.25f), aim);
        }

        Tick(world, ticks);

        var heard = new List<string>();
        foreach (Aim aim in askers)
        foreach ((long tick, RayHit hit) in aim.Answers)
            heard.Add(
                $"{tick} {hit.Tag} {hit.Entity} {BitConverter.DoubleToInt64Bits(hit.Point.X)} {BitConverter.DoubleToInt64Bits(hit.Point.Y)} " +
                $"{BitConverter.DoubleToInt64Bits(hit.Point.Z)} {BitConverter.SingleToInt32Bits(hit.Distance)} {hit.Normal.X:R} {hit.Normal.Y:R} {hit.Normal.Z:R}");
        return heard;
    }

    [Fact]
    public void TheSameWorldGivesTheSameHitsInEveryRun()
    {
        List<string> first = AnswersOfASceneOfMany(1, 6);
        List<string> second = AnswersOfASceneOfMany(1, 6);
        List<string> parallel = AnswersOfASceneOfMany(4, 6);

        first.Should().HaveCountGreaterThan(8 * 5 * 2,
            "every asker hears at least one answer to each ray on each tick after the first");
        first.Where(line => !line.Contains("Entity(null)")).Should().NotBeEmpty();
        second.Should().Equal(first);
        parallel.Should().Equal(first);
    }

    [Fact]
    public void ARayRequestAllocatesNothingOnceTheWorldIsWarm()
    {
        using World world = NewWorld(out _, debugChecks: false);
        FrameHandle ground = Ground(world, 0);
        FrameHandle child = TurnedChildFrame(world, ground);
        for (int i = 0; i < 16; i++)
            Spawn(world, ground, new(5 + 9 * i, 0, 0), StaticBox(), new Crew());
        Spawn(world, child, new(0, 0, 5), StaticBox(), new Crew());
        const int Askers = 8;
        var askers = new List<Aim>();
        for (int i = 0; i < Askers; i++)
        {
            var from = new Double3(0, 0.01 * i, 0);
            var aim = new Aim(
                new Ray(from, Double3.UnitX, 300, EntityQuery.With<Crew>().Near(500), Tag: 1),
                new Ray(from, Double3.UnitX, 300, default, Tag: 2, All: true)) { EveryTick = true, Keeps = false };
            askers.Add(aim);
            Spawn(world, ground, from, null, aim);
        }

        Tick(world, 60);

        // Each window of the measure collects garbage every 10 ticks: an object that a cast needs and that only a weak
        // reference holds would be made again after each collection, so it shows in every window.
        const int Ticks = 200;
        int heardBefore = askers.Sum(a => a.Heard);
        int windows = 0;
        long allocated = Allocations.Fewest(() =>
        {
            windows++;
            TickCollectingGarbage(world, Ticks);
        });

        allocated.Should().Be(0, "a ray request must not allocate once the world is warm");
        // One nearest answer and 17 hits of the other ray, for each asker on each tick: the measure covers real casts.
        (askers.Sum(a => a.Heard) - heardBefore).Should().Be(windows * Ticks * Askers * 18);
        world.Errors.Should().BeEmpty();
    }
}
