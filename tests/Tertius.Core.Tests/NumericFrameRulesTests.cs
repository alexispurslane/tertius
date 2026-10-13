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

    [Fact]
    public void Two_still_bodies_whose_spheres_touch_may_touch() =>
        MayTouch(Body(0, radius: 1), Body(3, radius: 2), Options).Should().BeTrue();

    [Fact]
    public void Two_still_bodies_just_apart_may_not_touch() =>
        MayTouch(Body(0, radius: 1), Body(3.001, radius: 2), Options).Should().BeFalse();

    [Fact]
    public void The_relative_speed_times_the_lookahead_grows_the_reach()
    {
        // 10 m apart with radii 1 m: the reach is 2 m plus the relative speed times 2 s.
        MayTouch(Body(0, vx: 4), Body(10), Options).Should().BeTrue();
        MayTouch(Body(0, vx: 3.9), Body(10), Options).Should().BeFalse();
    }

    [Fact]
    public void A_common_velocity_does_not_grow_the_reach() =>
        MayTouch(Body(0, vx: 7_700), Body(3, vx: 7_700), Options).Should().BeFalse();

    [Fact]
    public void The_merge_test_is_symmetric()
    {
        NumericBody a = Body(0, vx: 4), b = Body(9.99) with { Velocity = new Double3(0, 1, 0) };
        MayTouch(a, b, Options).Should().Be(MayTouch(b, a, Options));
    }

    [Fact]
    public void Members_of_one_numeric_frame_stay_linked_up_to_the_split_margin()
    {
        MayTouch(Body(0, home: Pod), Body(12, home: Pod), Options).Should().BeTrue();
        MayTouch(Body(0, home: Pod), Body(12.001, home: Pod), Options).Should().BeFalse();
    }

    [Fact]
    public void Bodies_in_the_semantic_frame_get_no_split_margin() =>
        MayTouch(Body(0), Body(5), Options).Should().BeFalse();

    [Fact]
    public void Bodies_of_two_numeric_frames_get_no_split_margin() =>
        MayTouch(Body(0, home: Pod), Body(5, home: Raft), Options).Should().BeFalse();

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
