using AwesomeAssertions;
using static Tertius.NumericFrameRules;

namespace Tertius.Core.Tests;

/// <summary>
/// The decisions of the membership and nesting rules of numeric frames, on plain data, at the default values of
/// <see cref="NumericFrameOptions"/>: create speed 1,000 m/s and leave speed 400 m/s for a group of free bodies, 50 m/s and
/// 20 m/s for a group with a body that thrusts, create distance 2^23 m (8,388,608 m), leave distance 2^22 m, a body thrusts
/// after 0.5 s of ticks of at least 1 mm/s per minute within a window of 20 s, lookahead 2 s, split margin 10 m, split
/// cooldown 1 s, drain speed 1 m/s, recenter distance 1 km, nest distance 1e9 m.
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

    /// <summary>
    /// The mode of a group of free bodies only, which flies at <paramref name="speed"/> m/s with its center
    /// <paramref name="distance"/> m from the origin, both along X.
    /// </summary>
    private static NumericMode Free(NumericMode current, double speed, double distance, bool anchored = false, bool isFixed = false) =>
        Mode(current, X(speed), X(distance), anchored, isFixed, thrusts: false, Options);

    [Fact]
    public void A_slow_near_group_needs_no_frame() =>
        Free(NumericMode.None, speed: 999.9, distance: 8_388_607).Should().Be(NumericMode.None);

    [Fact]
    public void A_group_of_free_bodies_gets_a_falling_frame_only_at_the_create_speed()
    {
        Free(NumericMode.None, speed: 999, distance: 0).Should().Be(NumericMode.None);
        Free(NumericMode.None, speed: 1_000, distance: 0).Should().Be(NumericMode.Falling);
    }

    [Fact]
    public void A_group_at_the_create_distance_gets_a_resting_frame() =>
        Free(NumericMode.None, speed: 0, distance: 8_388_608).Should().Be(NumericMode.Resting, "2^23 m");

    [Fact]
    public void A_fast_far_group_gets_a_falling_frame() =>
        Free(NumericMode.None, speed: 1_000, distance: 8_388_608).Should().Be(NumericMode.Falling);

    [Fact]
    public void An_anchored_group_never_falls() =>
        Free(NumericMode.None, speed: 5_000, distance: 0, anchored: true).Should().Be(NumericMode.None);

    [Fact]
    public void A_far_anchored_group_gets_a_resting_frame() =>
        Free(NumericMode.None, speed: 5_000, distance: 8_388_608, anchored: true).Should().Be(NumericMode.Resting);

    [Fact]
    public void A_fixed_group_never_gets_a_frame() =>
        Free(NumericMode.None, speed: 5_000, distance: 1e9, isFixed: true).Should().Be(NumericMode.None);

    [Fact]
    public void A_fixed_group_leaves_its_falling_frame() =>
        Free(NumericMode.Falling, speed: 5_000, distance: 1e9, isFixed: true).Should().Be(NumericMode.None);

    [Fact]
    public void A_falling_frame_keeps_falling_down_to_the_leave_speed() =>
        Free(NumericMode.Falling, speed: 400, distance: 0).Should().Be(NumericMode.Falling);

    [Fact]
    public void A_falling_frame_below_the_leave_speed_comes_to_rest_when_it_is_far() =>
        Free(NumericMode.Falling, speed: 399.99, distance: 4_194_304).Should().Be(NumericMode.Resting, "2^22 m");

    [Fact]
    public void A_falling_frame_below_the_leave_speed_and_near_goes_back_to_the_semantic_frame() =>
        Free(NumericMode.Falling, speed: 399.99, distance: 4_194_303.99).Should().Be(NumericMode.None);

    [Fact]
    public void An_anchored_falling_frame_comes_to_rest_when_it_is_far() =>
        Free(NumericMode.Falling, speed: 5_000, distance: 5e6, anchored: true).Should().Be(NumericMode.Resting);

    [Fact]
    public void A_resting_frame_stays_down_to_the_leave_distance() =>
        Free(NumericMode.Resting, speed: 0, distance: 4_194_304).Should().Be(NumericMode.Resting);

    [Fact]
    public void A_resting_frame_nearer_than_the_leave_distance_goes_back_to_the_semantic_frame() =>
        Free(NumericMode.Resting, speed: 0, distance: 4_194_303.99).Should().Be(NumericMode.None);

    [Fact]
    public void A_resting_frame_starts_to_fall_only_at_the_create_speed()
    {
        Free(NumericMode.Resting, speed: 999.99, distance: 5e6).Should().Be(NumericMode.Resting);
        Free(NumericMode.Resting, speed: 1_000, distance: 5e6).Should().Be(NumericMode.Falling);
    }

    [Fact]
    public void A_body_alone_uses_the_create_thresholds_with_its_own_motion()
    {
        BodyMode(Body(0, vx: 1_000), Options).Should().Be(NumericMode.Falling);
        BodyMode(Body(8_388_608) with { Static = true }, Options).Should().Be(NumericMode.Resting);
        BodyMode(Body(8_388_608, vx: 1_000) with { Fixed = true }, Options).Should().Be(NumericMode.None);
        BodyMode(Body(8_388_607, vx: 999), Options).Should().Be(NumericMode.None);
    }

    // ---- The thrust pair ------------------------------------------------------------------------------------------

    /// <summary>The mode of a group with a body that thrusts, as <see cref="Free"/> gives it for a group of free bodies only.</summary>
    private static NumericMode Thrusting(NumericMode current, double speed, double distance) =>
        Mode(current, X(speed), X(distance), anchored: false, isFixed: false, thrusts: true, Options);

    [Fact]
    public void A_group_with_a_body_that_thrusts_gets_a_falling_frame_at_the_thrust_create_speed()
    {
        Thrusting(NumericMode.None, speed: 49.99, distance: 0).Should().Be(NumericMode.None);
        Thrusting(NumericMode.None, speed: 50, distance: 0).Should().Be(NumericMode.Falling);
    }

    [Fact]
    public void A_falling_group_that_thrusts_keeps_falling_down_to_the_thrust_leave_speed()
    {
        Thrusting(NumericMode.Falling, speed: 20, distance: 0).Should().Be(NumericMode.Falling);
        Thrusting(NumericMode.Falling, speed: 19.99, distance: 0).Should().Be(NumericMode.None);
    }

    [Fact]
    public void A_falling_group_whose_members_stop_thrusting_at_60_m_s_leaves_its_frame_once_they_no_longer_count()
    {
        Thrusting(NumericMode.Falling, speed: 60, distance: 0).Should().Be(NumericMode.Falling, "while a member counts as thrusting");
        Free(NumericMode.Falling, speed: 60, distance: 0).Should().Be(NumericMode.None, "60 m/s is below the leave speed of free bodies, 400 m/s");
    }

    [Fact]
    public void A_group_of_a_free_body_and_a_body_that_thrusts_uses_the_thrust_pair()
    {
        // Neither body reaches the create speed of its own pair, 1,000 m/s for the free body and 50 m/s for the other.
        NumericGroup group = Summary(Body(0, vx: 60), Body(1, vx: 40) with { Thrusts = true });
        group.Thrusts.Should().BeTrue();
        GroupMode(NumericMode.None, group, Options).Should().Be(NumericMode.Falling, "the common speed is 50 m/s");
    }

    [Fact]
    public void An_anchor_never_makes_its_group_thrust() =>
        Summary(Body(0, vx: 30), Body(1) with { Static = true, Thrusts = true }, Body(2) with { Fixed = true, Thrusts = true })
            .Thrusts.Should().BeFalse();

    [Fact]
    public void A_body_alone_uses_the_thrust_pair_when_it_thrusts()
    {
        BodyMode(Body(0, vx: 50) with { Thrusts = true }, Options).Should().Be(NumericMode.Falling);
        BodyMode(Body(0, vx: 50), Options).Should().Be(NumericMode.None);
    }

    [Fact]
    public void A_Kepler_hull_never_thrusts()
    {
        NumericBody hull = KeplerHull(0, vx: 60) with { Thrusts = true };
        BodyMode(hull, Options).Should().Be(NumericMode.None, "60 m/s is below the create speed of free bodies");
        FollowsInFrame(hull, Options).Should().BeFalse();
        FollowsInFrame(KeplerHull(0, vx: 60, Orbit, NumericMode.Following) with { Thrusts = true }, Options)
            .Should().BeFalse("60 m/s is below the leave speed of free bodies");
    }

    // ---- ThrustClock ---------------------------------------------------------------------------------------------

    /// <summary>One tick of the engine at 60 Hz: a float, a little longer than a sixtieth of a second.</summary>
    private const double FloatTick = 1f / 60;

    /// <summary>
    /// Runs the thrust clock from zero over one tick of <paramref name="dt"/> seconds for each acceleration of
    /// <paramref name="accelerations"/>, in m/s², and returns what it says after each tick.
    /// </summary>
    private static List<(bool Thrusts, double ThrustTime, double QuietTime)> RunClock(double dt, IEnumerable<double> accelerations)
    {
        var after = new List<(bool Thrusts, double ThrustTime, double QuietTime)>();
        (bool Thrusts, double ThrustTime, double QuietTime) clock = (false, 0, 0);
        foreach (double acceleration in accelerations)
        {
            clock = ThrustClock(acceleration, clock.ThrustTime, clock.QuietTime, dt, Options);
            after.Add(clock);
        }
        return after;
    }

    private static IEnumerable<double> Ticks(double acceleration, int count) => Enumerable.Repeat(acceleration, count);

    [Fact]
    public void A_body_that_thrusts_on_every_tick_counts_from_the_thirtieth_tick_at_60_Hz() =>
        // Half a second of thrust: 29 ticks of a float sixtieth are 0.4833 s, and 30 are a hair over 0.5 s.
        RunClock(FloatTick, Ticks(1, 40)).FindIndex(clock => clock.Thrusts).Should().Be(29);

    [Fact]
    public void A_single_tick_of_thrust_never_counts_however_strong() =>
        // An impulse lasts one tick: 1,000 m/s² over 1/60 s is a kick of 17 m/s.
        RunClock(FloatTick, Ticks(1_000, 1).Concat(Ticks(0, 60 * 25))).Should().NotContain(clock => clock.Thrusts);

    [Fact]
    public void Thrust_in_pulses_less_than_the_window_apart_adds_up()
    {
        // Two ticks of a quarter second, 19.75 s apart: half a second of thrust within the window of 20 s.
        var after = RunClock(0.25, Ticks(1, 1).Concat(Ticks(0, 79)).Concat(Ticks(1, 1)));
        after[^2].Should().Be((false, 0.25, 19.75));
        after[^1].Should().Be((true, 0.5, 0.0));
    }

    [Fact]
    public void A_quiet_window_starts_the_clock_again()
    {
        var after = RunClock(0.25, Ticks(1, 1).Concat(Ticks(0, 80)).Concat(Ticks(1, 1)));
        after[^2].Should().Be((false, 0.0, 0.0), "the quiet time reached the window of 20 s");
        after[^1].Should().Be((false, 0.25, 0.0), "the next pulse starts from zero");
    }

    [Fact]
    public void A_body_keeps_counting_for_the_window_after_its_last_thrust_tick()
    {
        var after = RunClock(0.25, Ticks(1, 2).Concat(Ticks(0, 80)));
        after[1].Thrusts.Should().BeTrue("half a second of thrust");
        after[80].Should().Be((true, 0.5, 19.75));
        after[81].Should().Be((false, 0.0, 0.0));
    }

    [Fact]
    public void A_tick_below_the_minimum_acceleration_is_quiet()
    {
        // The minimum is 1 mm/s per minute.
        const double minimum = 1e-3 / 60;
        RunClock(0.25, Ticks(Math.BitDecrement(minimum), 10)).Should().OnlyContain(clock => clock.ThrustTime == 0);
        RunClock(0.25, Ticks(minimum, 1)).Single().ThrustTime.Should().Be(0.25);
    }

    // ---- Options -------------------------------------------------------------------------------------------------

    [Fact]
    public void The_default_options_are_valid() => Options.Invoking(options => options.Validate()).Should().NotThrow();

    /// <summary>Options that break one rule of the thrust pair each, by why, with the member that the refusal names.</summary>
    private static readonly Dictionary<string, (NumericFrameOptions Options, string Member)> InvalidOptions = new()
    {
        ["a thrust leave speed above the thrust create speed"] = (Options with { ThrustLeaveSpeed = 51 }, nameof(NumericFrameOptions.ThrustLeaveSpeed)),
        ["a thrust leave speed above the leave speed of free bodies"] = (Options with { LeaveSpeed = 19 }, nameof(NumericFrameOptions.ThrustLeaveSpeed)),
        ["a thrust create speed above the create speed of free bodies"] = (Options with { ThrustCreateSpeed = 1_001 }, nameof(NumericFrameOptions.ThrustCreateSpeed)),
        ["a thrust create speed that is not a number"] = (Options with { ThrustCreateSpeed = double.NaN }, nameof(NumericFrameOptions.ThrustCreateSpeed)),
        ["a thrust leave speed that is not a number"] = (Options with { ThrustLeaveSpeed = double.NaN }, nameof(NumericFrameOptions.ThrustLeaveSpeed)),
        ["a negative minimum thrust acceleration"] = (Options with { MinThrustAcceleration = -1e-9 }, nameof(NumericFrameOptions.MinThrustAcceleration)),
        ["a negative thrust onset"] = (Options with { ThrustOnset = -0.1 }, nameof(NumericFrameOptions.ThrustOnset)),
        // With an onset of zero, the thrust clock says that every body thrusts, also one with no intents.
        ["a thrust onset of zero"] = (Options with { ThrustOnset = 0 }, nameof(NumericFrameOptions.ThrustOnset)),
        // With a minimum of zero, every tick is a thrust tick, also a tick without intents, so every body thrusts after the onset.
        ["a minimum thrust acceleration of zero"] = (Options with { MinThrustAcceleration = 0 }, nameof(NumericFrameOptions.MinThrustAcceleration)),
        ["a thrust window of zero"] = (Options with { ThrustWindow = 0 }, nameof(NumericFrameOptions.ThrustWindow)),
        ["a thrust window that is not a number"] = (Options with { ThrustWindow = double.NaN }, nameof(NumericFrameOptions.ThrustWindow)),
    };

    public static TheoryData<string> InvalidOptionCases
    {
        get
        {
            var cases = new TheoryData<string>();
            foreach (string why in InvalidOptions.Keys)
                cases.Add(why);
            return cases;
        }
    }

    [Theory]
    [MemberData(nameof(InvalidOptionCases))]
    public void Options_that_break_a_rule_of_the_thrust_pair_are_refused(string why)
    {
        (NumericFrameOptions options, string member) = InvalidOptions[why];
        options.Invoking(o => o.Validate()).Should().Throw<ArgumentOutOfRangeException>(why).Which.ParamName.Should().Be(member);
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

    // ---- Places of ghosts ----------------------------------------------------------------------------------------

    [Fact]
    public void A_body_whose_sphere_comes_nearer_than_the_split_margin_to_its_bubbles_edge_can_stick_out()
    {
        // 29 m from the origin of a bubble of 40 m, the body's radius of 1 m ends 10 m inside it, the split margin.
        MayStickOut(FrameBounds.Sphere(40), Body(29.001), Options).Should().BeTrue();
        MayStickOut(FrameBounds.Sphere(40), Body(29), Options).Should().BeFalse();
    }

    [Fact]
    public void A_resting_body_near_the_edge_of_its_bubble_can_stick_out_whether_it_sleeps_or_not()
    {
        // The review's scene: the crate's sphere ends 1 mm inside the bubble. Awake, it moves at about 1 mm/s.
        MayStickOut(FrameBounds.Sphere(40), Body(38.999), Options).Should().BeTrue();
        MayStickOut(FrameBounds.Sphere(40), Body(38.999, vx: 1e-3), Options).Should().BeTrue();
        // Just beyond the split margin, the body gives no place whether it sleeps or not. A margin added to the motion would
        // give it one while it is awake.
        MayStickOut(FrameBounds.Sphere(40), Body(28.999), Options).Should().BeFalse();
        MayStickOut(FrameBounds.Sphere(40), Body(28.999, vx: 1e-3), Options).Should().BeFalse();
    }

    [Fact]
    public void A_body_faster_than_the_split_margin_over_the_lookahead_can_stick_out_by_its_speed_times_the_lookahead()
    {
        // 20 m inside the bubble: the body reaches its radius plus its speed times 2 s, and it must pass the bubble.
        MayStickOut(FrameBounds.Sphere(40), Body(20, vx: 9.6), Options).Should().BeTrue();
        MayStickOut(FrameBounds.Sphere(40), Body(20, vx: 9.4), Options).Should().BeFalse();
        MayStickOut(FrameBounds.Sphere(40), Body(20) with { Velocity = new Double3(0, -9.6, 0) }, Options).Should().BeTrue(
            "the body may move anywhere before the next update");
    }

    [Fact]
    public void A_static_body_never_sticks_out() =>
        MayStickOut(FrameBounds.Sphere(40), Body(45) with { Static = true }, Options).Should().BeFalse();

    [Fact]
    public void A_body_touches_a_bubble_from_outside_within_its_reach_and_the_split_margin()
    {
        MayTouchBubble(FrameBounds.Sphere(40), X(51), Double3.Zero, reach: 1, Options).Should().BeTrue();
        MayTouchBubble(FrameBounds.Sphere(40), X(51.001), Double3.Zero, reach: 1, Options).Should().BeFalse();
        MayTouchBubble(FrameBounds.Sphere(40), X(51.001), X(1e-3), reach: 1, Options).Should().BeFalse("a slow body moves less than the split margin");
    }

    [Fact]
    public void The_speed_relative_to_the_bubbles_frame_times_the_lookahead_grows_the_reach_to_the_bubble_past_the_split_margin()
    {
        // 21 m outside the bubble: the reach of 1 m plus the speed times 2 s.
        MayTouchBubble(FrameBounds.Sphere(40), X(61), X(10), reach: 1, Options).Should().BeTrue();
        MayTouchBubble(FrameBounds.Sphere(40), X(61), X(9.9), reach: 1, Options).Should().BeFalse();
    }

    /// <summary>
    /// The place of a ghost of radius 1 m in <paramref name="home"/>, moving at <paramref name="vx"/> along X, of a body that
    /// is static or a sensor as asked.
    /// </summary>
    private static GhostPlace Place(double vx = 0, FrameHandle home = default, double radius = 1, bool ofStaticBody = false, bool sensor = false,
        bool meetsHulls = false) =>
        new(home.IsNull ? Ground : home, Double3.Zero, X(vx), radius, Target: -1, ofStaticBody, sensor, meetsHulls);

    [Fact]
    public void A_dynamic_body_and_a_character_meet_a_ghost()
    {
        MeetsGhost(Body(0), Place()).Should().BeTrue();
        MeetsGhost(Body(0) with { Character = true }, Place()).Should().BeTrue();
    }

    [Fact]
    public void A_static_or_kinematic_body_meets_a_ghost_only_as_a_sensor()
    {
        // A ghost is kinematic, and Jolt makes no contact between a kinematic body and a static or kinematic one unless one
        // of them is a sensor.
        MeetsGhost(Body(0) with { Static = true }, Place()).Should().BeFalse();
        MeetsGhost(Body(0) with { Kinematic = true }, Place()).Should().BeFalse();
        MeetsGhost(Body(0) with { Static = true, Sensor = true }, Place()).Should().BeTrue();
        MeetsGhost(Body(0) with { Kinematic = true, Sensor = true }, Place()).Should().BeTrue();
    }

    [Fact]
    public void A_frame_hull_never_meets_a_ghost_with_the_ghost_role()
    {
        // A stick-out ghost has the ghost role, and the mask of a hull names only the static and hull roles.
        FrameHandle ship = new(new Entity(20, 1), 3, 1);
        MeetsGhost(Body(0) with { OwnedFrame = ship }, Place()).Should().BeFalse();
        MeetsGhost(Body(0) with { OwnedFrame = ship, Character = true }, Place()).Should().BeFalse();
        MeetsGhost(Body(0) with { OwnedFrame = ship, Kinematic = true, Fixed = true }, Place(sensor: true)).Should().BeFalse();
    }

    [Fact]
    public void A_frame_hull_meets_a_ghost_that_meets_hulls_by_jolts_rule()
    {
        // A child ghost of a static body or a hull has the static role, which the mask of a hull names (fork F1). Jolt then
        // makes the contact when the hull is dynamic, or when it is kinematic and the ghost is a sensor's.
        FrameHandle ship = new(new Entity(20, 1), 3, 1);
        MeetsGhost(Body(0) with { OwnedFrame = ship }, Place(ofStaticBody: true, meetsHulls: true)).Should().BeTrue();
        MeetsGhost(Body(0) with { OwnedFrame = ship, Kinematic = true, Fixed = true }, Place(ofStaticBody: true, meetsHulls: true)).Should().BeFalse();
        MeetsGhost(Body(0) with { OwnedFrame = ship, Static = true, Fixed = true }, Place(meetsHulls: true)).Should().BeFalse();
        MeetsGhost(Body(0) with { OwnedFrame = ship, Kinematic = true, Fixed = true }, Place(ofStaticBody: true, sensor: true, meetsHulls: true)).Should().BeTrue();
    }

    [Fact]
    public void The_ghost_of_a_static_body_meets_only_what_the_static_body_meets()
    {
        // Physics lets such a ghost meet a dynamic body, and a kinematic one when the static body is a sensor.
        MeetsGhost(Body(0), Place(ofStaticBody: true)).Should().BeTrue();
        MeetsGhost(Body(0) with { Static = true, Sensor = true }, Place(ofStaticBody: true)).Should().BeFalse();
        MeetsGhost(Body(0) with { Kinematic = true, Sensor = true }, Place(ofStaticBody: true)).Should().BeFalse();
        MeetsGhost(Body(0) with { Kinematic = true }, Place(ofStaticBody: true, sensor: true)).Should().BeTrue();
    }

    [Fact]
    public void The_ghost_of_a_sensor_meets_a_kinematic_body_but_no_static_one()
    {
        // Jolt makes a contact between a kinematic body and a sensor.
        MeetsGhost(Body(0) with { Kinematic = true }, Place(sensor: true)).Should().BeTrue();
        MeetsGhost(Body(0) with { Static = true }, Place(sensor: true)).Should().BeFalse();
    }

    [Fact]
    public void A_body_may_touch_the_place_of_a_ghost_by_the_merge_test_of_two_bodies()
    {
        // 10 m apart with radii 1 m and 2 m in two systems: the reach is 3 m plus the relative speed times 2 s.
        MayTouch(Body(0, home: Pod), Place(radius: 2), X(3), Options).Should().BeTrue();
        MayTouch(Body(0, home: Pod), Place(radius: 2), X(3.001), Options).Should().BeFalse();
        MayTouch(Body(0, vx: 4, home: Pod), Place(radius: 2), X(11), Options).Should().BeTrue();
        MayTouch(Body(0, vx: 4, home: Pod), Place(vx: 4, radius: 2), X(11), Options).Should().BeFalse("a common velocity does not grow the reach");
    }

    [Fact]
    public void A_body_in_the_system_of_a_ghost_stays_linked_to_its_place_up_to_the_split_margin()
    {
        MayTouch(Body(0, home: Pod), Place(home: Pod), X(12), Options).Should().BeTrue();
        MayTouch(Body(0, home: Pod), Place(home: Pod), X(12.001), Options).Should().BeFalse();
        MayTouch(Body(0, home: Pod), Place(home: Raft), X(5), Options).Should().BeFalse("a ghost in another system gets no split margin");
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

    private static NumericGroup Summary(params NumericBody[] members) => Summarize(members, new(), Options);

    [Fact]
    public void The_center_of_a_group_without_a_static_body_is_the_mass_weighted_mean_of_every_body() =>
        Summary(Body(0, mass: 3), Body(8, mass: 1)).Center.Should().Be(X(2));

    [Fact]
    public void A_static_body_is_infinitely_heavy_so_its_group_is_centered_on_the_static_bodies() =>
        Summary(Body(0, mass: 3), Body(8, mass: 1) with { Static = true }, Body(12, mass: 3) with { Static = true }).Center.Should().Be(X(11));

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
    public void A_kinematic_body_is_infinitely_heavy_so_its_group_takes_its_velocity_and_its_center()
    {
        // A crate of 25 kg at 60 m/s and a still door of 1 kg: the door decides, so the group does not fall.
        NumericGroup still = Summary(Body(-120, vx: 60, mass: 25), Body(0, mass: 1) with { Kinematic = true });
        still.Velocity.Should().Be(Double3.Zero);
        still.Center.Should().Be(X(0));
        still.Anchored.Should().BeFalse("a kinematic body can take a velocity, so its group can fall with it");
        NumericGroup sliding = Summary(Body(0, vx: 60, mass: 25), Body(2, vx: 30, mass: 1) with { Kinematic = true },
            Body(6, vx: 90, mass: 3) with { Kinematic = true });
        sliding.Velocity.Should().Be(X(75),"the mass-weighted mean of the kinematic bodies alone");
        sliding.Center.Should().Be(X(5));
    }

    [Fact]
    public void A_static_body_outweighs_a_kinematic_one_so_their_group_does_not_move() =>
        Summary(Body(0, vx: 60, mass: 25), Body(1, vx: 70, mass: 1) with { Kinematic = true }, Body(2) with { Static = true })
            .Should().Match<NumericGroup>(group => group.Velocity == Double3.Zero && group.Center == X(2));

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

    [Fact]
    public void A_frame_that_holds_a_static_body_of_the_group_outweighs_every_frame_without_one()
    {
        NumericGroup group = Summary(Body(0, vx: 7_700, mass: 1e9, home: Pod), Body(1, mass: 1, home: Raft) with { Static = true });
        group.Frame.Should().Be(Raft);
        group.FrameMass.Should().Be(double.PositiveInfinity, "only comparisons read the mass of a frame");
        group.Center.Should().Be(X(1));
        group.Velocity.Should().Be(Double3.Zero);
    }

    [Fact]
    public void A_group_whose_static_bodies_lie_in_the_semantic_frames_own_system_keeps_no_numeric_frame()
    {
        // The fast body's falling frame holds most of the group's mass, but the static body lives in the system of ground.
        NumericGroup group = Summary(Body(0, vx: 60, mass: 5, home: Pod), Body(7_000) with { Static = true });
        group.Frame.IsNull.Should().BeTrue();
        group.FrameMass.Should().Be(0);
        group.Center.Should().Be(X(7_000));
    }

    [Fact]
    public void Between_two_frames_that_hold_static_bodies_of_the_group_the_first_wins_whatever_the_masses() =>
        Summary(Body(0, mass: 1, home: Pod) with { Static = true }, Body(1, mass: 9, home: Raft) with { Static = true },
            Body(2, mass: 9, home: Raft)).Frame.Should().Be(Pod);

    // ---- Kepler hulls and their following frames ----------------------------------------------------------------------

    /// <summary>The kinematic hull of a frame on a Kepler orbit, at <paramref name="x"/> and flying at <paramref name="vx"/>.</summary>
    private static NumericBody KeplerHull(double x, double vx, FrameHandle home = default, NumericMode homeMode = NumericMode.None) =>
        Body(x, vx, home: home) with { Kinematic = true, Fixed = true, HomeMode = homeMode, AnchorsHome = homeMode == NumericMode.Following };

    /// <summary>The following frame of the Kepler hull of these tests.</summary>
    private static readonly FrameHandle Orbit = new(Entity.Null, 3, 1);

    /// <summary>
    /// The World describes a body as kinematic and nothing else, as it is in physics, so that motion alone decides that the
    /// body follows: a far kinematic body lives in a following frame of its own, and a far dynamic body does not.
    /// </summary>
    [Fact]
    public void A_far_body_lives_in_a_following_frame_of_its_own_because_it_is_kinematic()
    {
        FollowsInFrame(Body(8_388_608) with { Kinematic = true }, Options).Should().BeTrue("only its motion intents move a kinematic body");
        FollowsInFrame(Body(8_388_608), Options).Should().BeFalse("a dynamic body moves with its group");
    }

    [Fact]
    public void A_Kepler_hull_that_its_orbit_moves_fast_or_far_lives_in_a_following_frame_of_its_own()
    {
        FollowsInFrame(KeplerHull(6.8e6, vx: 7_700), Options).Should().BeTrue("at the create speed or above");
        FollowsInFrame(KeplerHull(0, vx: 1_000), Options).Should().BeTrue();
        FollowsInFrame(KeplerHull(8_388_608, vx: 0), Options).Should().BeTrue("at the create distance or beyond");
        FollowsInFrame(KeplerHull(8_388_607, vx: 999), Options).Should().BeFalse("a slow, near hull stays in its semantic frame's own system");
    }

    [Fact]
    public void A_Kepler_hull_in_its_following_frame_keeps_it_down_to_the_thresholds_to_leave_one()
    {
        FollowsInFrame(KeplerHull(0, vx: 500, Orbit, NumericMode.Following), Options).Should().BeTrue("above the leave speed");
        FollowsInFrame(KeplerHull(0, vx: 500), Options).Should().BeFalse("below the create speed, outside a numeric frame");
        FollowsInFrame(KeplerHull(5e6, vx: 0, Orbit, NumericMode.Following), Options).Should().BeTrue("beyond the leave distance");
        FollowsInFrame(KeplerHull(4_194_303, vx: 399, Orbit, NumericMode.Following), Options).Should().BeFalse();
    }

    [Fact]
    public void Only_a_Kepler_hull_lives_in_a_following_frame()
    {
        FollowsInFrame(Body(0, vx: 7_700), Options).Should().BeFalse("a fast body falls");
        FollowsInFrame(Body(0, vx: 7_700) with { Fixed = true }, Options).Should().BeFalse("the hull of a Spin frame, which turns in place");
    }

    [Fact]
    public void A_fast_Kepler_hull_alone_needs_a_following_frame()
    {
        BodyMode(KeplerHull(0, vx: 7_700), Options).Should().Be(NumericMode.Following);
        BodyMode(KeplerHull(0, vx: 999), Options).Should().Be(NumericMode.None);
    }

    [Fact]
    public void An_following_frame_whose_group_no_longer_orbits_leaves_with_the_hysteresis_of_a_falling_frame()
    {
        Free(NumericMode.Following, speed: 500, distance: 0)
            .Should().Be(NumericMode.Falling, "a group above the leave speed keeps falling, as it would in a falling frame");
        Free(NumericMode.Following, speed: 0, distance: 5e6)
            .Should().Be(NumericMode.Resting, "a slow group beyond the leave distance rests, as it would in a falling frame");
        Free(NumericMode.Following, speed: 399, distance: 4_194_303)
            .Should().Be(NumericMode.None, "below both leave thresholds the frame goes");
    }

    [Fact]
    public void A_group_with_a_fast_Kepler_hull_and_no_anchor_at_rest_takes_the_hulls_following_frame()
    {
        NumericGroup group = Summary(Body(0, vx: 7_701, home: Pod), KeplerHull(5, vx: 7_700, Orbit, NumericMode.Following));
        group.Follows.Should().BeTrue();
        group.Anchor.Should().Be(1);
        group.Frame.Should().Be(Orbit);
        group.FrameMass.Should().Be(double.PositiveInfinity, "the hull is infinitely heavy, so no other group keeps its frame");
        GroupMode(NumericMode.Falling, group, Options).Should().Be(NumericMode.Following);
    }

    [Fact]
    public void A_group_whose_Kepler_hull_has_no_following_frame_yet_takes_a_new_one()
    {
        NumericGroup group = Summary(KeplerHull(0, vx: 7_700));
        group.Follows.Should().BeTrue();
        group.Frame.IsNull.Should().BeTrue("the update makes the frame for the hull");
    }

    [Fact]
    public void Among_Kepler_hulls_with_following_frames_the_first_one_gives_the_group_its_home()
    {
        NumericGroup group = Summary(Body(0, vx: 7_700), KeplerHull(1, vx: 7_700, Raft, NumericMode.Following), KeplerHull(2, vx: 7_700, Orbit, NumericMode.Following));
        group.Anchor.Should().Be(1);
        group.Frame.Should().Be(Raft);
    }

    [Fact]
    public void An_anchor_at_rest_wins_over_the_following_frame_of_a_Kepler_hull()
    {
        NumericGroup group = Summary(KeplerHull(0, vx: 7_700, Orbit, NumericMode.Following), Body(4, vx: 7_700), Body(6, home: Raft) with { Static = true });
        group.Follows.Should().BeFalse();
        group.Anchor.Should().Be(0, "the hull keeps its own frame, and the group's home keeps a ghost of it");
        group.Anchored.Should().BeTrue();
        group.Frame.Should().Be(Raft);
        group.Center.Should().Be(X(6), "the static body alone decides where the group lives");
        GroupMode(NumericMode.Resting, group, Options).Should().Be(NumericMode.None, "6 m from the origin");
    }

    [Fact]
    public void The_hull_of_a_Static_or_Spin_frame_and_a_slow_Kepler_hull_win_over_a_following_frame()
    {
        Summary(KeplerHull(0, vx: 7_700, Orbit, NumericMode.Following), Body(1) with { Fixed = true }).Fixed.Should().BeTrue();
        NumericGroup group = Summary(KeplerHull(0, vx: 7_700, Orbit, NumericMode.Following), KeplerHull(1, vx: 10));
        group.Fixed.Should().BeTrue("a slow, near Kepler hull stays in the semantic frame's own system");
        group.Follows.Should().BeFalse();
        GroupMode(NumericMode.None, group, Options).Should().Be(NumericMode.None);
    }

    [Fact]
    public void Bodies_in_a_following_frame_without_its_hull_do_not_keep_it()
    {
        // The hull's group keeps its frame by itself, so a group that the hull left must find a frame of its own.
        NumericGroup group = Summary(Body(0, vx: 7_700, mass: 5, home: Orbit) with { HomeMode = NumericMode.Following }, Body(1, vx: 7_700, home: Pod) with { HomeMode = NumericMode.Falling });
        group.Follows.Should().BeFalse();
        group.Frame.Should().Be(Pod);
        group.FrameMass.Should().Be(1);
    }

    // ---- Drain and origin ----------------------------------------------------------------------------------------

    private static readonly Double3 OrbitSpeed = X(7_700);

    [Fact]
    public void A_sleeping_member_counts_with_zero_local_velocity_and_holds_the_drain_back()
    {
        // The sleeper moves with the frame. The mover's 4 m/s, a quarter of the mass, is a common part of exactly 1 m/s.
        NumericBody sleeper = Body(0, vx: OrbitSpeed.X, mass: 3, home: Pod), mover = Body(1, vx: OrbitSpeed.X + 4, mass: 1, home: Pod);
        Double3 common = Summary(sleeper, mover).Velocity;
        DrainVelocity(NumericMode.Falling, NumericMode.Falling, OrbitSpeed, common, Options).Should().Be(OrbitSpeed);

        Double3 faster = Summary(sleeper, mover with { Velocity = X(OrbitSpeed.X + 4.04) }).Velocity;
        DrainVelocity(NumericMode.Falling, NumericMode.Falling, OrbitSpeed, faster, Options).Should().Be(faster);
    }

    [Fact]
    public void A_falling_frame_keeps_its_velocity_up_to_the_drain_speed() =>
        DrainVelocity(NumericMode.Falling, NumericMode.Falling, OrbitSpeed, OrbitSpeed + X(1), Options).Should().Be(OrbitSpeed);

    [Fact]
    public void A_falling_frame_takes_its_members_velocity_above_the_drain_speed() =>
        DrainVelocity(NumericMode.Falling, NumericMode.Falling, OrbitSpeed, OrbitSpeed + X(1.01), Options).Should().Be(OrbitSpeed + X(1.01));

    [Fact]
    public void A_resting_frame_that_starts_to_fall_takes_its_members_velocity() =>
        DrainVelocity(NumericMode.Resting, NumericMode.Falling, Double3.Zero, X(50.5), Options).Should().Be(X(50.5));

    [Fact]
    public void A_frame_that_comes_to_rest_keeps_no_velocity() =>
        DrainVelocity(NumericMode.Falling, NumericMode.Resting, OrbitSpeed, X(3), Options).Should().Be(Double3.Zero);

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
