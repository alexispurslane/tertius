using AwesomeAssertions;
using static Tertius.NumericFrameRules;

namespace Tertius.Core.Tests;

/// <summary>
/// The decisions of the membership and nesting rules of numeric frames, on plain data, at the starting values of
/// <see cref="NumericFrameOptions"/>: create speed 50 m/s, leave speed 20 m/s, create distance 10 km, leave distance 5 km,
/// lookahead 2 s, split margin 10 m, split cooldown 1 s, drain speed 1 m/s, recenter distance 1 km, nest distance 1e9 m.
/// </summary>
public class NumericFrameRulesTests
{
    private static readonly NumericFrameOptions Options = new();
    private static readonly FrameHandle Ground = new(new Entity(1, 1), 0, 1);
    private static readonly FrameHandle Pod = new(Entity.Null, 1, 1);
    private static readonly FrameHandle Raft = new(Entity.Null, 2, 1);

    private static NumericBody Body(double x, double vx = 0, double radius = 1, double mass = 1, FrameHandle home = default) => new()
    {
        Entity = new Entity(10, 1),
        Semantic = Ground,
        Home = home.IsNull ? Ground : home,
        Position = new Double3(x, 0, 0),
        Velocity = new Double3(vx, 0, 0),
        Radius = radius,
        Mass = mass,
    };

    private static Double3 X(double x) => new(x, 0, 0);

    // ---- Mode ----------------------------------------------------------------------------------------------------

    [Fact]
    public void A_slow_near_group_needs_no_frame() =>
        Mode(NumericMode.None, X(49.9), X(9_999), false, false, Options).Should().Be(NumericMode.None);

    [Fact]
    public void A_group_at_the_create_speed_gets_a_falling_frame() =>
        Mode(NumericMode.None, X(50), X(0), false, false, Options).Should().Be(NumericMode.Falling);

    [Fact]
    public void A_group_at_the_create_distance_gets_a_resting_frame() =>
        Mode(NumericMode.None, X(0), X(10_000), false, false, Options).Should().Be(NumericMode.Resting);

    [Fact]
    public void A_fast_far_group_gets_a_falling_frame() =>
        Mode(NumericMode.None, X(50), X(10_000), false, false, Options).Should().Be(NumericMode.Falling);

    [Fact]
    public void An_anchored_group_never_falls() =>
        Mode(NumericMode.None, X(500), X(0), true, false, Options).Should().Be(NumericMode.None);

    [Fact]
    public void A_far_anchored_group_gets_a_resting_frame() =>
        Mode(NumericMode.None, X(500), X(10_000), true, false, Options).Should().Be(NumericMode.Resting);

    [Fact]
    public void A_fixed_group_never_gets_a_frame() =>
        Mode(NumericMode.None, X(500), X(1e6), false, true, Options).Should().Be(NumericMode.None);

    [Fact]
    public void A_fixed_group_leaves_its_falling_frame() =>
        Mode(NumericMode.Falling, X(500), X(1e6), false, true, Options).Should().Be(NumericMode.None);

    [Fact]
    public void A_falling_frame_keeps_falling_down_to_the_leave_speed() =>
        Mode(NumericMode.Falling, X(20), X(0), false, false, Options).Should().Be(NumericMode.Falling);

    [Fact]
    public void A_falling_frame_below_the_leave_speed_comes_to_rest_when_it_is_far() =>
        Mode(NumericMode.Falling, X(19.99), X(5_000), false, false, Options).Should().Be(NumericMode.Resting);

    [Fact]
    public void A_falling_frame_below_the_leave_speed_and_near_goes_back_to_the_semantic_frame() =>
        Mode(NumericMode.Falling, X(19.99), X(4_999.99), false, false, Options).Should().Be(NumericMode.None);

    [Fact]
    public void An_anchored_falling_frame_comes_to_rest_when_it_is_far() =>
        Mode(NumericMode.Falling, X(500), X(6_000), true, false, Options).Should().Be(NumericMode.Resting);

    [Fact]
    public void A_resting_frame_stays_down_to_the_leave_distance() =>
        Mode(NumericMode.Resting, X(0), X(5_000), false, false, Options).Should().Be(NumericMode.Resting);

    [Fact]
    public void A_resting_frame_nearer_than_the_leave_distance_goes_back_to_the_semantic_frame() =>
        Mode(NumericMode.Resting, X(0), X(4_999.99), false, false, Options).Should().Be(NumericMode.None);

    [Fact]
    public void A_resting_frame_starts_to_fall_only_at_the_create_speed()
    {
        Mode(NumericMode.Resting, X(49.99), X(6_000), false, false, Options).Should().Be(NumericMode.Resting);
        Mode(NumericMode.Resting, X(50), X(6_000), false, false, Options).Should().Be(NumericMode.Falling);
    }

    [Fact]
    public void A_body_alone_uses_the_create_thresholds_with_its_own_motion()
    {
        BodyMode(Body(0, vx: 50), Options).Should().Be(NumericMode.Falling);
        BodyMode(Body(10_000) with { Static = true }, Options).Should().Be(NumericMode.Resting);
        BodyMode(Body(10_000, vx: 50) with { Fixed = true }, Options).Should().Be(NumericMode.None);
        BodyMode(Body(9_999, vx: 49), Options).Should().Be(NumericMode.None);
    }

    // ---- MayTouch ------------------------------------------------------------------------------------------------

    /// <summary>The merge test of two bodies, with the separation that their positions in the semantic frame give.</summary>
    private static bool Touch(in NumericBody a, in NumericBody b) => MayTouch(a, b, b.Position - a.Position, Options);

    [Fact]
    public void Two_still_bodies_whose_spheres_touch_may_touch() =>
        Touch(Body(0, radius: 1), Body(3, radius: 2, home: Pod)).Should().BeTrue();

    [Fact]
    public void Two_still_bodies_just_apart_may_not_touch() =>
        Touch(Body(0, radius: 1), Body(3.001, radius: 2, home: Pod)).Should().BeFalse();

    [Fact]
    public void The_relative_speed_times_the_lookahead_grows_the_reach()
    {
        // 10 m apart with radii 1 m: the reach is 2 m plus the relative speed times 2 s.
        Touch(Body(0, vx: 4), Body(10, home: Pod)).Should().BeTrue();
        Touch(Body(0, vx: 3.9), Body(10, home: Pod)).Should().BeFalse();
    }

    [Fact]
    public void A_common_velocity_does_not_grow_the_reach() =>
        Touch(Body(0, vx: 7_700), Body(3, vx: 7_700, home: Pod)).Should().BeFalse();

    [Fact]
    public void The_merge_test_is_symmetric()
    {
        NumericBody a = Body(0, vx: 4), b = Body(9.99) with { Velocity = new Double3(0, 1, 0) };
        Touch(a, b).Should().Be(Touch(b, a));
    }

    [Fact]
    public void The_merge_test_measures_the_separation_that_it_is_given_not_the_positions_in_the_semantic_frame()
    {
        // Far from the semantic frame's origin, the positions round; the caller gives the separation in the homes.
        NumericBody a = Body(1e16), b = Body(1e16, home: Pod);
        MayTouch(a, b, X(3), Options).Should().BeFalse();
        MayTouch(a, b, X(2), Options).Should().BeTrue();
    }

    [Fact]
    public void Members_of_one_numeric_frame_stay_linked_up_to_the_split_margin()
    {
        Touch(Body(0, home: Pod), Body(12, home: Pod)).Should().BeTrue();
        Touch(Body(0, home: Pod), Body(12.001, home: Pod)).Should().BeFalse();
    }

    [Fact]
    public void Bodies_in_the_system_of_the_semantic_frame_stay_linked_up_to_the_split_margin()
    {
        // A body that leaves a group with a fixed hull into the semantic frame's system does not go in and out at the edge.
        Touch(Body(0), Body(12)).Should().BeTrue();
        Touch(Body(0), Body(12.001)).Should().BeFalse();
    }

    [Fact]
    public void Bodies_of_two_numeric_frames_get_no_split_margin() =>
        Touch(Body(0, home: Pod), Body(5, home: Raft)).Should().BeFalse();

    [Fact]
    public void A_body_of_a_numeric_frame_and_one_of_the_semantic_frame_get_no_split_margin() =>
        Touch(Body(0, home: Pod), Body(5)).Should().BeFalse();

    // ---- StickOutRadius and MayTouchStickOut ---------------------------------------------------------------------

    [Fact]
    public void A_body_sticks_out_of_a_bubble_by_at_most_its_diameter_plus_the_exit_margin()
    {
        // A body leaves a bubble when its center is farther outside than its radius plus the exit margin, the larger of 2 %
        // of the bubble and the body's radius. So its far side reaches at most two radii plus that margin past the bubble.
        StickOutRadius(FrameBounds.Sphere(200), largestRadius: 1).Should().Be(200 + 2 + 4, "2 % of 200 m is more than 1 m");
        StickOutRadius(FrameBounds.Sphere(40), largestRadius: 1).Should().Be(40 + 2 + 1, "1 m is more than 2 % of 40 m");
    }

    [Fact]
    public void The_sphere_that_holds_the_bodies_that_stick_out_of_an_ellipsoid_starts_at_its_longest_radius() =>
        StickOutRadius(new FrameBounds(new System.Numerics.Vector3(10, 50, 20)), largestRadius: 2).Should().Be(50 + 4 + 2);

    [Fact]
    public void A_body_that_leaves_its_bubble_reaches_as_far_from_its_frames_origin_as_the_stick_out_radius() =>
        // The body is 39 m from the origin, so it lies inside the bubble, and its distance does not count.
        StickOutReach(FrameBounds.Sphere(40), Body(39, vx: 5, radius: 1), Options).Should().Be(StickOutRadius(FrameBounds.Sphere(40), 1));

    [Fact]
    public void A_hull_on_rails_reaches_its_distance_from_its_frames_origin_plus_its_radius_and_its_lookahead()
    {
        // A hull that its frame's driver moves never leaves the bubble, so it can stick out by any amount: 60 m out, with a
        // radius of 5 m and 2 s at 3 m/s, it reaches 71 m, past the 55 m of a body of its radius that leaves.
        StickOutReach(FrameBounds.Sphere(40), Body(60, vx: 3, radius: 5) with { Fixed = true }, Options).Should().Be(60 + 5 + 3 * 2);
        StickOutReach(FrameBounds.Sphere(40), Body(10, radius: 5) with { Fixed = true }, Options).Should().Be(15,
            "a hull on rails deep inside its bubble reaches less far than a body that leaves");
    }

    /// <summary>
    /// The merge test of a body and the bodies that stick out of a bubble whose sphere of <paramref name="stickOut"/> m
    /// around the frame's origin, which is at <paramref name="frameX"/> on X and moves at <paramref name="frameVx"/>.
    /// </summary>
    private static bool TouchStickOut(in NumericBody body, double frameX, double stickOut, double frameVx = 0) =>
        MayTouchStickOut(body, X(frameX) - body.Position, X(frameVx), stickOut, Options);

    [Fact]
    public void A_still_body_whose_sphere_touches_the_sphere_of_the_bodies_that_stick_out_may_touch_them()
    {
        TouchStickOut(Body(0, radius: 1), frameX: 44, stickOut: 43).Should().BeTrue();
        TouchStickOut(Body(0, radius: 1), frameX: 44.001, stickOut: 43).Should().BeFalse();
    }

    [Fact]
    public void The_speed_relative_to_the_bubbles_frame_times_the_lookahead_grows_the_reach()
    {
        // 54 m from the frame's origin: the reach is 44 m plus the speed relative to the frame times 2 s.
        TouchStickOut(Body(0, vx: 5), frameX: 54, stickOut: 43).Should().BeTrue();
        TouchStickOut(Body(0, vx: 4.9), frameX: 54, stickOut: 43).Should().BeFalse();
        TouchStickOut(Body(0, vx: 60), frameX: 54, stickOut: 43, frameVx: 55).Should().BeTrue();
        TouchStickOut(Body(0, vx: 60), frameX: 54, stickOut: 43, frameVx: 55.1).Should().BeFalse();
    }

    [Fact]
    public void A_body_that_moves_with_the_bubbles_frame_gets_no_lookahead() =>
        TouchStickOut(Body(0, vx: 7_700), frameX: 45, stickOut: 43, frameVx: 7_700).Should().BeFalse();

    [Fact]
    public void A_body_gets_no_split_margin_against_the_bodies_that_stick_out_of_a_bubble() =>
        TouchStickOut(Body(0, home: Pod), frameX: 50, stickOut: 43).Should().BeFalse("the reach is 44 m, without the 10 m split margin");

    [Fact]
    public void The_slack_of_two_places_in_a_semantic_frame_covers_their_rounding_far_from_its_origin()
    {
        // A light-year out, a double steps by 2 m; 1 km out, by about 1e-13 m.
        double step = Math.BitIncrement(1e16) - 1e16;
        PlaceSlack(X(1e16), X(-1e16)).Should().BeGreaterThan(1000 * step);
        PlaceSlack(X(1_000), X(0)).Should().BeLessThan(1e-6, "near the origin the first test reaches almost exactly as far as the second");
    }

    [Fact]
    public void The_test_of_the_bodies_that_stick_out_measures_the_separation_that_it_is_given()
    {
        // Far from the semantic frame's origin, the positions round; the caller gives the separation through the homes.
        NumericBody body = Body(1e16, home: Pod);
        MayTouchStickOut(body, X(44.001), Double3.Zero, 43, Options).Should().BeFalse();
        MayTouchStickOut(body, X(44), Double3.Zero, 43, Options).Should().BeTrue();
    }

    // ---- SplitClock ----------------------------------------------------------------------------------------------

    [Fact]
    public void Members_that_are_together_need_no_hold_and_reset_the_clock() =>
        SplitClock(together: true, apart: 0.5, dt: 0.25, Options).Should().Be((false, 0.0));

    [Fact]
    public void Members_that_are_apart_are_held_before_the_cooldown() =>
        SplitClock(together: false, apart: 0, dt: 0.25, Options).Should().Be((true, 0.25));

    [Fact]
    public void The_split_clock_adds_up_over_sync_points() =>
        SplitClock(together: false, apart: 0.5, dt: 0.25, Options).Should().Be((true, 0.75));

    [Fact]
    public void Members_split_when_the_clock_reaches_the_cooldown_and_the_clock_starts_again() =>
        SplitClock(together: false, apart: 0.75, dt: 0.25, Options).Should().Be((false, 0.0));

    [Fact]
    public void A_sync_point_without_time_holds_members_that_are_apart() =>
        SplitClock(together: false, apart: 0, dt: 0, Options).Should().Be((true, 0.0));

    [Fact]
    public void A_zero_cooldown_splits_at_once() =>
        SplitClock(together: false, apart: 0, dt: 0, Options with { SplitCooldown = 0 }).Should().Be((false, 0.0));

    // ---- LeaveClock ----------------------------------------------------------------------------------------------

    [Fact]
    public void A_body_whose_group_stays_in_the_semantic_system_needs_no_hold_and_resets_its_clock() =>
        LeaveClock(NumericMode.None, FrameHandle.Null, apart: 0.5, dt: 0.25, Options).Should().Be((false, 0.0));

    [Fact]
    public void A_body_whose_group_would_rest_in_a_new_frame_is_held_before_the_cooldown() =>
        LeaveClock(NumericMode.Resting, FrameHandle.Null, apart: 0, dt: 0.25, Options).Should().Be((true, 0.25));

    [Fact]
    public void The_leave_clock_adds_up_over_sync_points() =>
        LeaveClock(NumericMode.Resting, FrameHandle.Null, apart: 0.5, dt: 0.25, Options).Should().Be((true, 0.75));

    [Fact]
    public void A_body_is_held_just_below_the_cooldown() =>
        LeaveClock(NumericMode.Resting, FrameHandle.Null, apart: 0.5, dt: 0.4990234375, Options).Should().Be((true, 0.9990234375));

    [Fact]
    public void A_body_leaves_when_its_clock_reaches_the_cooldown() =>
        LeaveClock(NumericMode.Resting, FrameHandle.Null, apart: 0.75, dt: 0.25, Options).Should().Be((false, 1.0));

    [Fact]
    public void A_clock_past_the_cooldown_keeps_counting_so_that_another_member_cannot_restart_it() =>
        LeaveClock(NumericMode.Resting, FrameHandle.Null, apart: 1.5, dt: 0.25, Options).Should().Be((false, 1.75));

    [Fact]
    public void A_sync_point_without_time_holds_a_body_about_to_leave() =>
        LeaveClock(NumericMode.Resting, FrameHandle.Null, apart: 0, dt: 0, Options).Should().Be((true, 0.0));

    [Fact]
    public void A_body_whose_group_falls_leaves_at_once() =>
        LeaveClock(NumericMode.Falling, FrameHandle.Null, apart: 0.5, dt: 0.25, Options).Should().Be((false, 0.0));

    [Fact]
    public void A_body_whose_group_joins_a_numeric_frame_merges_at_once() =>
        LeaveClock(NumericMode.Resting, Pod, apart: 0.5, dt: 0.25, Options).Should().Be((false, 0.0));

    // ---- Summarize -----------------------------------------------------------------------------------------------

    private static NumericGroup Summary(params NumericBody[] members) => Summarize(members, new());

    [Fact]
    public void The_center_is_the_mass_weighted_mean_of_every_body() =>
        Summary(Body(0, mass: 3), Body(8, mass: 1) with { Static = true }).Center.Should().Be(X(2));

    [Fact]
    public void The_velocity_is_the_mass_weighted_mean_of_the_bodies() =>
        Summary(Body(0, vx: 100, mass: 3), Body(1, vx: 200, mass: 1)).Velocity.Should().Be(X(125));

    [Fact]
    public void A_static_body_anchors_the_group_and_zeroes_its_velocity()
    {
        NumericGroup group = Summary(Body(0, vx: 100), Body(1) with { Static = true });
        group.Anchored.Should().BeTrue();
        group.Velocity.Should().Be(Double3.Zero);
    }

    [Fact]
    public void A_fixed_body_fixes_the_whole_group() =>
        Summary(Body(0, vx: 100), Body(1) with { Fixed = true }).Fixed.Should().BeTrue();

    [Fact]
    public void A_group_in_its_semantic_frame_has_no_frame()
    {
        NumericGroup group = Summary(Body(0), Body(1));
        group.Frame.IsNull.Should().BeTrue();
        group.FrameMass.Should().Be(0);
    }

    [Fact]
    public void The_group_keeps_the_numeric_frame_that_holds_most_of_its_mass()
    {
        NumericGroup group = Summary(Body(0, mass: 2, home: Pod), Body(1, mass: 2, home: Raft), Body(2, mass: 1, home: Raft), Body(3, mass: 9));
        group.Frame.Should().Be(Raft);
        group.FrameMass.Should().Be(3);
    }

    [Fact]
    public void On_a_tie_the_group_keeps_the_frame_of_its_first_body() =>
        Summary(Body(0, mass: 2, home: Raft), Body(1, mass: 2, home: Pod)).Frame.Should().Be(Raft);

    // ---- Drain and origin ----------------------------------------------------------------------------------------

    private static readonly Double3 Orbit = X(7_700);

    [Fact]
    public void A_sleeping_member_counts_with_zero_local_velocity_and_holds_the_drain_back()
    {
        // The sleeper moves with the frame. The mover's 4 m/s, a quarter of the mass, is a common part of exactly 1 m/s.
        NumericBody sleeper = Body(0, vx: Orbit.X, mass: 3, home: Pod), mover = Body(1, vx: Orbit.X + 4, mass: 1, home: Pod);
        Double3 common = Summary(sleeper, mover).Velocity;
        DrainVelocity(NumericMode.Falling, NumericMode.Falling, Orbit, common, Options).Should().Be(Orbit);

        Double3 faster = Summary(sleeper, mover with { Velocity = X(Orbit.X + 4.04) }).Velocity;
        DrainVelocity(NumericMode.Falling, NumericMode.Falling, Orbit, faster, Options).Should().Be(faster);
    }

    [Fact]
    public void A_falling_frame_keeps_its_velocity_up_to_the_drain_speed() =>
        DrainVelocity(NumericMode.Falling, NumericMode.Falling, Orbit, Orbit + X(1), Options).Should().Be(Orbit);

    [Fact]
    public void A_falling_frame_takes_its_members_velocity_above_the_drain_speed() =>
        DrainVelocity(NumericMode.Falling, NumericMode.Falling, Orbit, Orbit + X(1.01), Options).Should().Be(Orbit + X(1.01));

    [Fact]
    public void A_resting_frame_that_starts_to_fall_takes_its_members_velocity() =>
        DrainVelocity(NumericMode.Resting, NumericMode.Falling, Double3.Zero, X(50.5), Options).Should().Be(X(50.5));

    [Fact]
    public void A_frame_that_comes_to_rest_keeps_no_velocity() =>
        DrainVelocity(NumericMode.Falling, NumericMode.Resting, Orbit, X(3), Options).Should().Be(Double3.Zero);

    [Fact]
    public void The_origin_stays_while_the_center_is_within_the_recenter_distance() =>
        Origin(X(10_000), X(11_000), Options).Should().Be(X(10_000));

    [Fact]
    public void The_origin_moves_to_the_center_beyond_the_recenter_distance() =>
        Origin(X(10_000), X(11_000.01), Options).Should().Be(X(11_000.01));

    // ---- Collect -------------------------------------------------------------------------------------------------

    [Fact]
    public void A_frame_with_no_body_and_no_frame_goes_away() => Collects(holdsBody: false, holdsFrame: false).Should().BeTrue();

    [Fact]
    public void A_frame_that_a_body_calls_home_stays() => Collects(holdsBody: true, holdsFrame: false).Should().BeFalse();

    [Fact]
    public void A_frame_that_holds_a_frame_stays() => Collects(holdsBody: false, holdsFrame: true).Should().BeFalse();

    // ---- Nesting -------------------------------------------------------------------------------------------------

    [Fact]
    public void A_frame_nests_only_beyond_the_nest_distance()
    {
        NeedsNest(X(1e9), Options).Should().BeFalse();
        NeedsNest(X(1e9 + 1), Options).Should().BeTrue();
    }

    [Fact]
    public void The_grid_point_is_the_nearest_whole_step_in_each_axis() =>
        GridPoint(new Double3(1_500, -600, 511), 1_024).Should().Be(new Double3(1_024, -1_024, 0));

    [Fact]
    public void A_grid_point_far_away_is_exact_and_on_the_grid()
    {
        var position = new Double3(3e15 + 0.25, -7.123e12, 5e8);
        Double3 point = GridPoint(position, 1 << 20);
        OnGrid(point, 1 << 20).Should().BeTrue();
        (point - position).X.Should().BeInRange(-(1 << 19), 1 << 19);
    }

    [Fact]
    public void A_half_step_is_not_on_the_grid()
    {
        OnGrid(new Double3(2_048, -1_024, 0), 1_024).Should().BeTrue();
        OnGrid(new Double3(2_048, 512, 0), 1_024).Should().BeFalse();
    }

    [Fact]
    public void A_nest_jumps_only_when_all_of_its_frames_are_then_within_half_the_nest_distance()
    {
        Double3 step = X(1 << 30);
        NestCanJump([X((1 << 30) + 5e8), X((1 << 30) - 5e8)], step, Options).Should().BeTrue();
        NestCanJump([X((1 << 30) + 5e8), X(0)], step, Options).Should().BeFalse();
    }

    [Fact]
    public void A_nest_with_no_frames_may_jump() => NestCanJump([], X(1 << 30), Options).Should().BeTrue();

    [Fact]
    public void The_nearest_nest_within_half_the_nest_distance_wins()
    {
        // The frame is at 1.2e9 + 1e8 = 1.3e9 in the semantic frame.
        (FrameHandle, Double3)[] nests = [(Pod, X(2e9)), (Raft, X(1.5e9))];
        NearestNest(nests, X(1.2e9), X(1e8), Options).Should().Be(Raft);
    }

    [Fact]
    public void No_nest_is_near_beyond_half_the_nest_distance() =>
        NearestNest([(Pod, X(0))], X(5e8), X(1), Options).IsNull.Should().BeTrue();

    [Fact]
    public void On_a_tie_the_later_nest_wins() =>
        NearestNest([(Pod, X(-1e8)), (Raft, X(1e8))], Double3.Zero, Double3.Zero, Options).Should().Be(Raft);

    [Fact]
    public void No_nests_give_no_nest() => NearestNest([], Double3.Zero, X(1e9), Options).IsNull.Should().BeTrue();
}
