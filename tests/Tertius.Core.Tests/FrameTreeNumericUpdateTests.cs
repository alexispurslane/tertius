using AwesomeAssertions;

namespace Tertius.Core.Tests;

/// <summary>
/// <see cref="FrameTree.UpdateNumericFrames"/> on a tree built in the test, with plain body records and the starting values
/// of <see cref="NumericFrameOptions"/> unless a test tunes them. Every numeric frame comes from an update, as in the
/// engine. After every update the tree keeps its invariants, every move starts from the body's home and ends in a live
/// frame of its semantic frame, and the frame that a body leaves is still alive when the moves are handed out.
/// </summary>
public class FrameTreeNumericUpdateTests
{
    private const double Grid = 1 << 20;
    private const double LightYear = 9.4607e15;
    private readonly FrameTree frames = new();
    private readonly FrameHandle ground;
    private readonly PhysicsChanges changes = new();
    private List<NumericMove> moves = [];

    public FrameTreeNumericUpdateTests() =>
        ground = frames.Create(new Entity(1, 1), FrameHandle.Null, DPose.Identity, FrameBounds.Sphere(1e9f), MotionDriver.Static);

    private static Double3 X(double x) => new(x, 0, 0);

    /// <summary>A body of the frame ground at <paramref name="x"/>, whose place in its home the tree gives now.</summary>
    private NumericBody Body(uint id, double x, double vx = 0, double radius = 1, FrameHandle home = default, double mass = 1)
    {
        FrameHandle physicsHome = home.IsNull ? ground : home;
        return new NumericBody
        {
            Entity = new Entity(id, 1),
            Semantic = ground,
            Home = physicsHome,
            Position = X(x),
            HomePosition = X(x) - frames.HomeOffset(physicsHome, ground),
            Velocity = X(vx),
            Radius = radius,
            Mass = mass,
        };
    }

    /// <summary>Runs one update and returns the bodies with their new homes and their places in them.</summary>
    private NumericBody[] Update(double dt, params NumericBody[] bodies)
    {
        changes.Clear();
        moves = [];
        frames.UpdateNumericFrames(bodies, dt, changes, handed =>
        {
            foreach (NumericMove move in handed)
                frames.IsAlive(move.From).Should().BeTrue("physics converts each move through the frame that the body leaves");
            moves.AddRange(handed);
        });
        frames.AssertInvariants();
        var homes = moves.ToDictionary(move => move.Entity, move => move.To);
        foreach (NumericMove move in moves)
        {
            NumericBody body = bodies.Single(b => b.Entity == move.Entity);
            move.From.Should().Be(body.Home);
            move.To.Should().NotBe(body.Home);
            frames.IsAlive(move.To).Should().BeTrue();
            frames.SemanticOf(move.To).Should().Be(body.Semantic);
        }
        return bodies.Select(body =>
        {
            FrameHandle home = homes.TryGetValue(body.Entity, out FrameHandle to) ? to : body.Home;
            return body with { Home = home, HomePosition = body.Position - frames.HomeOffset(home, body.Semantic) };
        }).ToArray();
    }

    /// <summary>Moves the falling frames over <paramref name="seconds"/> in one step, as the engine does between updates.</summary>
    private void Advance(double seconds)
    {
        frames.Update();
        frames.BeginNumericStep();
        frames.AdvanceNumericFrames(seconds);
    }

    [Fact]
    public void An_update_without_bodies_changes_nothing()
    {
        Update(0.1);
        moves.Should().BeEmpty();
        frames.NumericCount.Should().Be(0);
    }

    [Fact]
    public void A_slow_near_body_stays_in_its_semantic_frame()
    {
        Update(0.1, Body(10, x: 100, vx: 49));
        moves.Should().BeEmpty();
        frames.NumericCount.Should().Be(0);
    }

    [Fact]
    public void A_fast_body_gets_a_falling_frame_at_its_place_with_its_velocity()
    {
        FrameHandle home = Update(0.1, Body(10, x: 100, vx: 60))[0].Home;
        frames.Numeric(home).Should().Be(NumericMode.Falling);
        frames.LocalPose(home).Position.Should().Be(X(100));
        frames.LinearVelocity(home).Should().Be(X(60));
        changes.FramesCreated.Should().Equal(home);
    }

    [Fact]
    public void A_far_slow_body_gets_a_resting_frame()
    {
        FrameHandle home = Update(0.1, Body(10, x: 20_000, vx: 3))[0].Home;
        frames.Numeric(home).Should().Be(NumericMode.Resting);
        frames.LinearVelocity(home).Should().Be(Double3.Zero);
    }

    [Fact]
    public void Three_bodies_in_a_chain_share_one_frame()
    {
        // Each body reaches 4 m: the first and the last are 6 m apart and link only through the middle one.
        NumericBody[] after = Update(0.1, Body(10, x: 0, vx: 60, radius: 2), Body(11, x: 3, vx: 60, radius: 2), Body(12, x: 6, vx: 60, radius: 2));
        after.Select(body => body.Home).Distinct().Should().ContainSingle().Which.Should().NotBe(ground);
        frames.NumericCount.Should().Be(1);
    }

    [Fact]
    public void Groups_get_their_frames_and_moves_in_the_order_of_their_first_body()
    {
        NumericBody[] after = Update(0.1, Body(10, x: 0, vx: 60), Body(11, x: 1_000, vx: 60), Body(12, x: 2, vx: 60));
        changes.FramesCreated.Should().Equal(after[0].Home, after[1].Home);
        after[2].Home.Should().Be(after[0].Home);
        moves.Select(move => move.Entity.Index).Should().Equal(10u, 12u, 11u);
        frames.LocalPose(after[0].Home).Position.Should().Be(X(1));
    }

    [Fact]
    public void An_anchored_group_near_the_origin_that_does_not_fall_stays_in_its_semantic_frame()
    {
        Update(0.1, Body(10, x: 0) with { Static = true }, Body(11, x: 2, vx: 10));
        moves.Should().BeEmpty();
    }

    [Fact]
    public void A_far_static_body_and_a_slow_body_on_it_share_a_resting_frame()
    {
        NumericBody[] after = Update(0.1, Body(10, x: 20_000) with { Static = true }, Body(11, x: 20_001));
        after[0].Home.Should().Be(after[1].Home);
        frames.Numeric(after[0].Home).Should().Be(NumericMode.Resting);
    }

    [Fact]
    public void A_fast_body_near_a_static_body_falls_and_its_frame_keeps_a_ghost_of_the_static_body()
    {
        NumericBody[] after = Update(0.1, Body(10, x: 0) with { Static = true }, Body(11, x: 2, vx: 60));
        after[0].Home.Should().Be(ground, "a static body stays in its own system");
        frames.Numeric(after[1].Home).Should().Be(NumericMode.Falling);
        frames.LinearVelocity(after[1].Home).Should().Be(X(60), "the static body does not slow the group down");
        frames.AnchorGhosts.ToArray().Should().Equal(new NumericGhost(new Entity(10, 1), after[1].Home));
    }

    [Fact]
    public void A_fast_body_near_a_fixed_hull_falls_and_the_hull_stays_in_its_semantic_frame()
    {
        NumericBody[] after = Update(0.1, Body(10, x: 0, vx: 60) with { Fixed = true }, Body(11, x: 2, vx: 60));
        after[0].Home.Should().Be(ground);
        frames.Numeric(after[1].Home).Should().Be(NumericMode.Falling);
        frames.AnchorGhosts.ToArray().Should().Equal(new NumericGhost(new Entity(10, 1), after[1].Home));
    }

    [Fact]
    public void A_static_body_that_a_falling_frame_reaches_does_not_stop_the_frame()
    {
        FrameHandle pod = Update(0.1, Body(11, x: 0, vx: 7_700))[0].Home;
        NumericBody[] after = Update(0.1, Body(10, x: 3) with { Static = true }, Body(11, x: 0, vx: 7_700, home: pod));
        after[1].Home.Should().Be(pod);
        frames.Numeric(pod).Should().Be(NumericMode.Falling);
        frames.LinearVelocity(pod).Should().Be(X(7_700), "no orbital velocity drains into the bodies' floats");
        changes.FramesShifted.Should().BeEmpty();
        frames.AnchorGhosts.ToArray().Should().Equal(new NumericGhost(new Entity(10, 1), pod));
    }

    [Fact]
    public void An_anchor_ghost_goes_when_the_falling_frame_passes()
    {
        FrameHandle pod = Update(0.1, Body(10, x: 0) with { Static = true }, Body(11, x: 2, vx: 60))[1].Home;
        Update(0.1, Body(10, x: 0) with { Static = true }, Body(11, x: 500, vx: 60, home: pod));
        frames.AnchorGhosts.IsEmpty.Should().BeTrue();
    }

    [Fact]
    public void A_body_at_the_edge_of_the_reach_of_a_fixed_hull_does_not_go_in_and_out_of_the_semantic_frames_system()
    {
        // Alone, the slow body 20 km out gets a resting frame; with the fixed hull, it stays in the system of ground.
        NumericBody hull = Body(20, x: 20_000) with { Fixed = true };
        Update(0.1, hull, Body(10, x: 20_001));
        moves.Should().BeEmpty();

        // 7 m apart: beyond the reach of 2 m, within the split margin of 10 m, so it stays.
        Update(0.1, hull, Body(10, x: 20_007));
        moves.Should().BeEmpty();

        // 13 m apart for a whole split cooldown: it leaves into a resting frame of its own.
        NumericBody[] after = Update(1, hull, Body(10, x: 20_013));
        FrameHandle own = after[1].Home;
        frames.Numeric(own).Should().Be(NumericMode.Resting);

        // Back to 7 m apart: the two systems differ, so there is no margin, and it stays in its frame.
        Update(0.1, hull, Body(10, x: 20_007, home: own));
        moves.Should().BeEmpty();
    }

    /// <summary>A fixed hull 20 km out, at rest.</summary>
    private NumericBody FarHull() => Body(20, x: 20_000) with { Fixed = true };

    /// <summary>
    /// A slow body 15 m from <see cref="FarHull"/>. At 7 m/s its reach is 16 m, which links it to the hull from any system.
    /// At rest its reach is 2 m, and 12 m with the split margin, which does not. Alone, it needs a resting frame.
    /// </summary>
    private NumericBody EdgeBody(double vx = 0, FrameHandle home = default) => Body(10, x: 20_015, vx: vx, home: home);

    [Fact]
    public void A_body_that_crosses_the_edge_of_a_fixed_hulls_reach_every_update_changes_its_home_at_most_once_per_cooldown()
    {
        FrameHandle home = ground;
        var changedAt = new List<int>();
        for (int update = 0; update < 40; update++)
        {
            home = Update(0.1, FarHull(), EdgeBody(vx: update % 2 == 0 ? 7 : 0, home: home))[1].Home;
            if (moves.Count > 0)
                changedAt.Add(update);
        }
        changedAt.Zip(changedAt.Skip(1), (first, next) => next - first).Should().OnlyContain(gap => gap >= 10,
            "a split cooldown of 1 s is 10 updates of 0.1 s");
    }

    [Fact]
    public void A_body_that_stays_beyond_the_edge_of_a_fixed_hulls_reach_leaves_after_the_cooldown()
    {
        Update(0.25, FarHull(), EdgeBody(vx: 7));
        for (int update = 1; update <= 3; update++)
        {
            Update(0.25, FarHull(), EdgeBody());
            moves.Should().BeEmpty("the body has been beyond the reach for {0} s only", 0.25 * update);
        }

        NumericBody[] after = Update(0.25, FarHull(), EdgeBody());
        moves.Should().Equal(new NumericMove(new Entity(10, 1), ground, after[1].Home));
        frames.Numeric(after[1].Home).Should().Be(NumericMode.Resting);
    }

    [Fact]
    public void A_body_that_was_gone_from_the_input_comes_back_without_its_old_clock()
    {
        Update(0.25, FarHull(), EdgeBody(vx: 7));
        Update(0.25, FarHull(), EdgeBody());
        Update(0.25, FarHull(), EdgeBody());
        moves.Should().BeEmpty("the body has been beyond the reach for 0.5 s");

        Update(0.25, FarHull());
        NumericBody[] after = Update(0.25, FarHull(), EdgeBody());
        frames.Numeric(after[1].Home).Should().Be(NumericMode.Resting, "nothing keeps it in the system of ground any more");
    }

    [Fact]
    public void A_held_body_that_a_member_of_a_numeric_frame_reaches_joins_that_frame_at_once()
    {
        FrameHandle raft = Update(0.25, FarHull(), EdgeBody(vx: 7), Body(11, x: 20_100))[2].Home;
        Update(0.25, FarHull(), EdgeBody(), Body(11, x: 20_100, home: raft));
        moves.Should().BeEmpty("the body is held in the system of ground");

        Update(0.25, FarHull(), EdgeBody(), Body(11, x: 20_017, home: raft));
        moves.Should().Equal(new NumericMove(new Entity(10, 1), ground, raft));
    }

    [Fact]
    public void A_body_kept_with_a_fixed_hull_that_speeds_up_to_fall_leaves_at_once()
    {
        Update(0.25, FarHull(), EdgeBody(vx: 7));
        NumericBody[] after = Update(0.25, FarHull(), EdgeBody(vx: 60));
        frames.Numeric(after[1].Home).Should().Be(NumericMode.Falling);
    }

    [Fact]
    public void A_near_body_that_moves_far_gets_its_frame_at_once()
    {
        // The far body makes the update work on the semantic frame, so that it sees the near one.
        FrameHandle raft = Update(0.25, Body(11, x: 50_000), Body(10, x: 100))[0].Home;
        NumericBody[] after = Update(0.25, Body(11, x: 50_000, home: raft), Body(10, x: 20_000));
        frames.Numeric(after[1].Home).Should().Be(NumericMode.Resting);
    }

    [Fact]
    public void A_fixed_hull_in_a_numeric_frame_goes_back_to_its_semantic_frame_with_its_frame()
    {
        FrameHandle hull = frames.Create(new Entity(20, 1), ground, new DPose(X(50), DQuaternion.Identity), FrameBounds.Sphere(50), MotionDriver.Physics);
        frames.TreeLinearVelocity(hull) = X(60);
        NumericBody owner = Body(20, x: 50, vx: 60) with { OwnedFrame = hull };
        FrameHandle pod = Update(0.1, owner)[0].Home;
        frames.TreeParent(hull).Should().Be(pod);

        // The game parks the ship: physics no longer drives its frame.
        frames.SetDriver(hull, MotionDriver.Static);
        Update(0.1, Body(20, x: 50, vx: 60, home: pod) with { OwnedFrame = hull, Fixed = true });

        moves.Should().Equal(new NumericMove(new Entity(20, 1), pod, ground));
        frames.TreeParent(hull).Should().Be(ground);
        frames.LocalPose(hull).Position.Should().Be(X(50));
        frames.IsAlive(pod).Should().BeFalse();
    }

    [Fact]
    public void A_frame_that_physics_does_not_drive_under_a_numeric_frame_breaks_an_invariant()
    {
        FrameHandle hull = frames.Create(new Entity(20, 1), ground, new DPose(X(50), DQuaternion.Identity), FrameBounds.Sphere(50), MotionDriver.Physics);
        Update(0.1, Body(20, x: 50, vx: 60) with { OwnedFrame = hull });
        frames.SetDriver(hull, MotionDriver.Static);
        frames.Invoking(tree => tree.AssertInvariants()).Should().Throw<ContractException>();
    }

    [Fact]
    public void A_slow_near_frame_hands_its_bodies_back_and_goes_away()
    {
        FrameHandle pod = Update(0.1, Body(10, x: 100, vx: 60))[0].Home;
        Update(0.1, Body(10, x: 100, vx: 10, home: pod))[0].Home.Should().Be(ground);
        frames.IsAlive(pod).Should().BeFalse();
        changes.FramesDestroyed.Should().Equal(pod);
    }

    [Fact]
    public void A_numeric_frame_without_bodies_goes_away()
    {
        FrameHandle pod = Update(0.1, Body(10, x: 20_000))[0].Home;
        Update(0.1);
        frames.IsAlive(pod).Should().BeFalse();
    }

    [Fact]
    public void Members_that_drift_apart_split_only_after_the_cooldown()
    {
        NumericBody[] bodies = Update(0.1, Body(10, x: 0, vx: 60), Body(11, x: 1, vx: 60));
        FrameHandle pod = bodies[0].Home;
        bodies = [bodies[0], Body(11, x: 100, vx: 60, home: pod)];
        Update(0.5, bodies);
        moves.Should().BeEmpty();

        NumericBody[] after = Update(0.5, bodies);
        after[0].Home.Should().Be(pod, "the first group keeps the frame on a tie of mass");
        after[1].Home.Should().NotBe(pod);
        frames.LocalPose(after[1].Home).Position.Should().Be(X(100));
    }

    [Fact]
    public void Members_that_come_together_again_restart_the_split_clock()
    {
        FrameHandle pod = Update(0.1, Body(10, x: 0, vx: 60), Body(11, x: 1, vx: 60))[0].Home;
        NumericBody near = Body(10, x: 0, vx: 60, home: pod), far = Body(11, x: 100, vx: 60, home: pod);
        Update(0.5, near, far);
        Update(0.5, near, Body(11, x: 5, vx: 60, home: pod));
        Update(0.5, near, far);
        moves.Should().BeEmpty();
    }

    [Fact]
    public void A_heavier_group_that_splits_off_later_takes_the_frame_from_the_first_group()
    {
        FrameHandle pod = Update(0.1, Body(10, x: 0, vx: 60, mass: 1), Body(11, x: 1, vx: 60, mass: 5))[0].Home;
        NumericBody[] bodies = [Body(10, x: 0, vx: 60, home: pod, mass: 1), Body(11, x: 100, vx: 60, home: pod, mass: 5)];
        Update(0.5, bodies);
        NumericBody[] after = Update(0.5, bodies);
        after[1].Home.Should().Be(pod, "the group with more mass in the frame keeps it");
        after[0].Home.Should().NotBe(pod);
        moves.Should().Equal(new NumericMove(new Entity(10, 1), pod, after[0].Home));
    }

    [Fact]
    public void Two_numeric_frames_whose_members_meet_merge_into_the_one_with_more_mass()
    {
        NumericBody[] first = Update(0.1, Body(10, x: 0, vx: 60, mass: 1), Body(11, x: 1_000, vx: 60, mass: 5));
        FrameHandle light = first[0].Home, heavy = first[1].Home;
        light.Should().NotBe(heavy);

        Update(0.1, Body(10, x: 0, vx: 60, home: light, mass: 1), Body(11, x: 1.5, vx: 60, home: heavy, mass: 5));

        moves.Should().Equal(new NumericMove(new Entity(10, 1), light, heavy));
        frames.IsAlive(light).Should().BeFalse("the frame that lost its bodies goes");
        changes.FramesDestroyed.Should().Equal(light);
    }

    [Fact]
    public void Bodies_of_two_semantic_frames_never_link()
    {
        FrameHandle other = frames.Create(new Entity(2, 1), FrameHandle.Null, DPose.Identity, FrameBounds.Sphere(1e9f), MotionDriver.Static);
        NumericBody[] after = Update(0.1, Body(10, x: 0, vx: 60), Body(11, x: 0, vx: 60) with { Semantic = other, Home = other });
        after[0].Home.Should().NotBe(after[1].Home);
        frames.SemanticOf(after[1].Home).Should().Be(other);
    }

    [Fact]
    public void A_frame_takes_over_its_members_common_velocity_and_tells_physics()
    {
        FrameHandle pod = Update(0.1, Body(10, x: 0, vx: 7_700))[0].Home;
        Update(0.1, Body(10, x: 0, vx: 7_702, home: pod));
        frames.LinearVelocity(pod).Should().Be(X(7_702));
        changes.FramesShifted.Should().Equal(new FrameShift(pod, Double3.Zero, X(2)));
        moves.Should().BeEmpty();
    }

    [Fact]
    public void A_drain_and_a_recenter_keep_the_pose_and_velocity_of_a_ship_frame_in_its_semantic_frame()
    {
        FrameHandle hull = frames.Create(new Entity(20, 1), ground, new DPose(X(50), DQuaternion.Identity), FrameBounds.Sphere(50), MotionDriver.Physics);
        frames.TreeLinearVelocity(hull) = X(60);
        FrameHandle pod = Update(0.1, Body(20, x: 50, vx: 60) with { OwnedFrame = hull })[0].Home;
        DPose pose = frames.LocalPose(hull);
        Double3 velocity = frames.LinearVelocity(hull);

        // The members' center is 2 km from the frame's origin and their speed is 2 m/s more: the frame moves and drains.
        Update(0.1, Body(20, x: 2_050, vx: 62, home: pod) with { OwnedFrame = hull });

        changes.FramesShifted.Should().Equal(new FrameShift(pod, X(2_000), X(2)));
        frames.LocalPose(hull).Should().Be(pose);
        frames.LinearVelocity(hull).Should().Be(velocity);
        frames.TreeLocalPose(hull).Position.Should().Be(X(-2_000));
    }

    [Fact]
    public void A_resting_frame_whose_members_speed_up_starts_to_fall()
    {
        FrameHandle pod = Update(0.1, Body(10, x: 20_000))[0].Home;
        Update(0.1, Body(10, x: 20_000, vx: 60, home: pod));
        frames.Numeric(pod).Should().Be(NumericMode.Falling);
        frames.LinearVelocity(pod).Should().Be(X(60));
    }

    [Fact]
    public void A_far_falling_frame_whose_members_slow_down_comes_to_rest()
    {
        FrameHandle pod = Update(0.1, Body(10, x: 20_000, vx: 60))[0].Home;
        Update(0.1, Body(10, x: 20_000, vx: 10, home: pod));
        frames.Numeric(pod).Should().Be(NumericMode.Resting);
        frames.LinearVelocity(pod).Should().Be(Double3.Zero);
    }

    [Fact]
    public void A_moving_owner_takes_its_frame_into_its_new_home()
    {
        FrameHandle hull = frames.Create(new Entity(20, 1), ground, new DPose(X(50), DQuaternion.Identity), FrameBounds.Sphere(50), MotionDriver.Physics);
        FrameHandle home = Update(0.1, Body(20, x: 50, vx: 60) with { OwnedFrame = hull })[0].Home;
        frames.TreeParent(hull).Should().Be(home);
    }

    [Fact]
    public void A_far_frame_nests_on_the_grid_and_keeps_its_place()
    {
        FrameHandle pod = Update(0.1, Body(10, x: 3e9, vx: 60))[0].Home;
        FrameHandle nest = frames.TreeParent(pod);
        frames.Numeric(nest).Should().Be(NumericMode.Nesting);
        changes.FramesCreated.Should().Equal(pod, nest);
        frames.HomeOffset(pod, ground).Should().Be(X(3e9));
        frames.TreeLocalPose(pod).Position.Length.Should().BeLessThanOrEqualTo(Grid / 2);
    }

    [Fact]
    public void A_second_far_frame_joins_the_nearest_nest()
    {
        NumericBody[] after = Update(0.1, Body(10, x: 3e9, vx: 60), Body(11, x: 3.1e9, vx: 60));
        frames.TreeParent(after[1].Home).Should().Be(frames.TreeParent(after[0].Home));
        frames.NumericCount.Should().Be(3);
    }

    [Fact]
    public void A_nest_jumps_by_whole_grid_steps_when_its_frames_drift_far_together()
    {
        const double place = 3L << 30;
        FrameHandle pod = Update(0.1, Body(10, x: place, vx: 60))[0].Home;
        FrameHandle nest = frames.TreeParent(pod);
        frames.TreeLocalPose(nest).Position.Should().Be(X(place));
        Advance(2e9 / 60);
        Double3 drift = frames.TreeLocalPose(pod).Position;

        Update(0.1, Body(10, x: place + drift.X, vx: 60, home: pod));

        Double3 step = NumericFrameRules.GridPoint(drift, Grid);
        frames.TreeParent(pod).Should().Be(nest);
        frames.TreeLocalPose(nest).Position.Should().Be(X(place) + step);
        frames.HomeOffset(pod, ground).Should().Be(X(place) + drift);
        changes.FramesShifted.Should().Equal(new FrameShift(nest, step, Double3.Zero));
    }

    [Fact]
    public void A_nested_frame_that_its_nest_cannot_follow_moves_to_a_new_nest_and_an_empty_nest_goes_after_its_frames()
    {
        const double place = 3L << 30;
        // A falling pod and a resting raft share one nest.
        NumericBody[] first = Update(0.1, Body(10, x: place, vx: 60), Body(11, x: place + 1e8));
        FrameHandle pod = first[0].Home, raft = first[1].Home;
        FrameHandle nest = frames.TreeParent(pod);
        frames.TreeParent(raft).Should().Be(nest);

        // The pod drifts 2e9 m away: a jump of the nest would leave the raft far from it.
        Advance(2e9 / 60);
        Double3 drift = frames.TreeLocalPose(pod).Position;
        Update(0.1, Body(10, x: place + drift.X, vx: 60, home: pod), Body(11, x: place + 1e8, home: raft));
        FrameHandle newNest = frames.TreeParent(pod);
        newNest.Should().NotBe(nest);
        frames.Numeric(newNest).Should().Be(NumericMode.Nesting);
        frames.TreeParent(raft).Should().Be(nest);
        frames.HomeOffset(pod, ground).Should().Be(X(place) + drift);

        // The raft's body goes: the raft goes, then its nest.
        Update(0.1, Body(10, x: place + drift.X, vx: 60, home: pod));
        changes.FramesDestroyed.Should().Equal(raft, nest);
    }

    [Fact]
    public void The_merge_test_far_from_the_origin_measures_bodies_by_their_places_in_their_homes()
    {
        frames.NumericOptions = new NumericFrameOptions { RecenterDistance = 1e12 };
        NumericBody[] first = Update(0.1, Body(10, x: LightYear, vx: 60), Body(11, x: LightYear + 1_000, vx: 60));
        FrameHandle a = first[0].Home, b = first[1].Home;
        frames.TreeParent(a).Should().Be(frames.TreeParent(b));
        double between = frames.Relative(b, a).Position.X;

        // A light-year out, a double steps by 2 m, so both rows round to one place. In their homes, they are 3 m apart.
        NumericBody bodyA = Body(10, x: LightYear, vx: 60, home: a) with { HomePosition = Double3.Zero };
        NumericBody bodyB = Body(11, x: LightYear, vx: 60, home: b) with { HomePosition = X(3 - between) };
        Update(0.1, bodyA, bodyB);
        moves.Should().BeEmpty("radii of 1 m do not reach 3 m");

        Update(0.1, bodyA, bodyB with { HomePosition = X(1.5 - between) });
        moves.Should().ContainSingle();
    }

    [Fact]
    public void A_semantic_frame_that_dies_takes_its_numeric_frames_with_it_and_its_ship_frames_keep_their_places()
    {
        DQuaternion turned = DQuaternion.FromAxisAngle(new Double3(0, 1, 0), 0.5);
        FrameHandle station = frames.Create(new Entity(2, 1), ground, new DPose(X(1_000), turned), FrameBounds.Sphere(1e6f), MotionDriver.Static);
        frames.AngularVelocity(station) = new System.Numerics.Vector3(0, 0.1f, 0);
        FrameHandle hull = frames.Create(new Entity(20, 1), station, new DPose(X(50), DQuaternion.Identity), FrameBounds.Sphere(50), MotionDriver.Physics);
        frames.TreeLinearVelocity(hull) = X(60);
        NumericBody owner = Body(20, x: 50, vx: 60) with { Semantic = station, Home = station, HomePosition = X(50), OwnedFrame = hull };
        FrameHandle pod = Update(0.1, owner)[0].Home;
        frames.TreeParent(hull).Should().Be(pod);
        frames.Update();
        DPose before = frames.Relative(hull, ground);

        var dissolved = new List<FrameHandle>();
        frames.Destroy(station, dissolved);
        frames.Update();

        dissolved.Should().Equal(pod);
        frames.IsAlive(pod).Should().BeFalse();
        frames.TreeParent(hull).Should().Be(ground);
        DPose after = frames.Relative(hull, ground);
        (after.Position - before.Position).Length.Should().BeLessThan(1e-9);
        frames.NumericCount.Should().Be(0);
        frames.AssertInvariants();
    }

    [Fact]
    public void The_same_input_makes_the_same_frames_and_moves()
    {
        static (List<NumericMove> Moves, List<Double3> Places) Run()
        {
            var test = new FrameTreeNumericUpdateTests();
            test.Update(0.1, test.Body(10, x: 0, vx: 60), test.Body(11, x: 3e9, vx: 0), test.Body(12, x: 1, vx: 60), test.Body(13, x: 20_000));
            return (test.moves, test.changes.FramesCreated.Select(frame => test.frames.HomeOffset(frame, test.ground)).ToList());
        }

        (List<NumericMove> moves, List<Double3> places) = Run();
        (List<NumericMove> again, List<Double3> placesAgain) = Run();
        again.Should().Equal(moves);
        placesAgain.Should().Equal(places);
    }
}
