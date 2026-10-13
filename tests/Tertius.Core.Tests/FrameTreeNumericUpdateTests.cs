using AwesomeAssertions;

namespace Tertius.Core.Tests;

/// <summary>
/// <see cref="FrameTree.UpdateNumericFrames"/> on a tree built in the test, with plain body records and the starting values
/// of <see cref="NumericFrameOptions"/>. After every update the tree keeps its invariants, every move starts from the
/// body's home and ends in a live frame of its semantic frame, and the frame that a body leaves is still alive when the
/// moves are handed out.
/// </summary>
public class FrameTreeNumericUpdateTests
{
    private const double Grid = 1 << 20;
    private readonly FrameTree frames = new();
    private readonly FrameHandle ground;
    private readonly PhysicsChanges changes = new();
    private List<NumericMove> moves = [];

    public FrameTreeNumericUpdateTests() =>
        ground = frames.Create(new Entity(1, 1), FrameHandle.Null, DPose.Identity, FrameBounds.Sphere(1e9f), MotionDriver.Static);

    private static Double3 X(double x) => new(x, 0, 0);

    private NumericBody Body(uint id, double x, double vx = 0, double radius = 1, FrameHandle home = default) => new()
    {
        Entity = new Entity(id, 1),
        Semantic = ground,
        Home = home.IsNull ? ground : home,
        Position = X(x),
        Velocity = X(vx),
        Radius = radius,
        Mass = 1,
    };

    /// <summary>Runs one update and returns the bodies with their new homes.</summary>
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
        return bodies.Select(body => homes.TryGetValue(body.Entity, out FrameHandle home) ? body with { Home = home } : body).ToArray();
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
    public void An_anchored_group_near_the_origin_stays_in_its_semantic_frame()
    {
        Update(0.1, Body(10, x: 0) with { Static = true }, Body(11, x: 2, vx: 60));
        moves.Should().BeEmpty();
    }

    [Fact]
    public void A_group_with_a_fixed_body_stays_in_its_semantic_frame()
    {
        Update(0.1, Body(10, x: 0, vx: 60) with { Fixed = true }, Body(11, x: 2, vx: 60));
        moves.Should().BeEmpty();
    }

    [Fact]
    public void A_slow_near_frame_hands_its_bodies_back_and_goes_away()
    {
        FrameHandle pod = frames.CreateNumeric(ground, X(100), X(30), NumericMode.Falling);
        Update(0.1, Body(10, x: 100, vx: 10, home: pod))[0].Home.Should().Be(ground);
        frames.IsAlive(pod).Should().BeFalse();
        changes.FramesDestroyed.Should().Equal(pod);
    }

    [Fact]
    public void A_numeric_frame_without_bodies_goes_away()
    {
        FrameHandle pod = frames.CreateNumeric(ground, X(20_000), Double3.Zero, NumericMode.Resting);
        Update(0.1);
        frames.IsAlive(pod).Should().BeFalse();
    }

    [Fact]
    public void Members_that_drift_apart_split_only_after_the_cooldown()
    {
        FrameHandle pod = frames.CreateNumeric(ground, X(0), X(60), NumericMode.Falling);
        NumericBody[] bodies = [Body(10, x: 0, vx: 60, home: pod), Body(11, x: 100, vx: 60, home: pod)];
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
        FrameHandle pod = frames.CreateNumeric(ground, X(0), X(60), NumericMode.Falling);
        NumericBody near = Body(10, x: 0, vx: 60, home: pod), far = Body(11, x: 100, vx: 60, home: pod);
        Update(0.5, near, far);
        Update(0.5, near, far with { Position = X(5) });
        Update(0.5, near, far);
        moves.Should().BeEmpty();
    }

    [Fact]
    public void A_frame_takes_over_its_members_common_velocity_and_tells_physics()
    {
        FrameHandle pod = frames.CreateNumeric(ground, X(0), X(7_700), NumericMode.Falling);
        Update(0.1, Body(10, x: 0, vx: 7_702, home: pod));
        frames.LinearVelocity(pod).Should().Be(X(7_702));
        changes.FramesShifted.Should().Equal(new FrameShift(pod, Double3.Zero, X(2)));
        moves.Should().BeEmpty();
    }

    [Fact]
    public void A_resting_frame_whose_members_speed_up_starts_to_fall()
    {
        FrameHandle pod = frames.CreateNumeric(ground, X(20_000), Double3.Zero, NumericMode.Resting);
        Update(0.1, Body(10, x: 20_000, vx: 60, home: pod));
        frames.Numeric(pod).Should().Be(NumericMode.Falling);
        frames.LinearVelocity(pod).Should().Be(X(60));
    }

    [Fact]
    public void A_far_falling_frame_whose_members_slow_down_comes_to_rest()
    {
        FrameHandle pod = frames.CreateNumeric(ground, X(20_000), X(30), NumericMode.Falling);
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
        FrameHandle pod = frames.CreateNumeric(ground, X(3e9), X(60), NumericMode.Falling);
        Update(0.1, Body(10, x: 3e9, vx: 60, home: pod));
        FrameHandle nest = frames.TreeParent(pod);
        frames.Numeric(nest).Should().Be(NumericMode.Nesting);
        changes.FramesCreated.Should().Equal(nest);
        frames.HomeOffset(pod, ground).Should().Be(X(3e9));
        frames.LocalPose(pod).Position.Length.Should().BeLessThanOrEqualTo(Grid / 2);
    }

    [Fact]
    public void A_second_far_frame_joins_the_nearest_nest()
    {
        FrameHandle pod = frames.CreateNumeric(ground, X(3e9), X(60), NumericMode.Falling);
        FrameHandle raft = frames.CreateNumeric(ground, X(3.1e9), X(60), NumericMode.Falling);
        Update(0.1, Body(10, x: 3e9, vx: 60, home: pod), Body(11, x: 3.1e9, vx: 60, home: raft));
        frames.TreeParent(raft).Should().Be(frames.TreeParent(pod));
        frames.NumericCount.Should().Be(3);
    }

    [Fact]
    public void A_nest_jumps_by_whole_grid_steps_when_its_frames_drift_far_together()
    {
        const double place = 3L << 30;
        FrameHandle nest = frames.CreateNumeric(ground, X(place), Double3.Zero, NumericMode.Nesting);
        FrameHandle pod = frames.CreateNumeric(ground, X(place + 2e9), X(60), NumericMode.Falling);
        frames.MoveUnderNest(pod, nest, X(2e9));
        Update(0.1, Body(10, x: place + 2e9, vx: 60, home: pod));
        Double3 step = NumericFrameRules.GridPoint(X(2e9), Grid);
        frames.TreeParent(pod).Should().Be(nest);
        frames.LocalPose(nest).Position.Should().Be(X(place) + step);
        frames.HomeOffset(pod, ground).Should().Be(X(place + 2e9));
        changes.FramesShifted.Should().Equal(new FrameShift(nest, step, Double3.Zero));
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
