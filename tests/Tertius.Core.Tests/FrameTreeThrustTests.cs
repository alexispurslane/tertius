using AwesomeAssertions;

namespace Tertius.Core.Tests;

/// <summary>
/// How <see cref="FrameTree.UpdateNumericFrames"/> tells a body that thrusts from a free one, on a tree built in the test,
/// with plain body records and the default values of <see cref="NumericFrameOptions"/>: a group of free bodies gets a
/// falling frame from 1,000 m/s, and a group with a body that thrusts from 50 m/s. A character always thrusts. Any other
/// body thrusts after 0.5 s of updates whose force and impulse ask for at least 1 mm/s per minute, and keeps thrusting for
/// 20 s after the last of them. After every update the tree keeps its invariants.
/// </summary>
public sealed class FrameTreeThrustTests
{
    private readonly FrameTree frames = new();
    private readonly FrameHandle ground;
    private readonly PhysicsChanges changes = new();

    public FrameTreeThrustTests()
    {
        ground = frames.Create(
            frameOwner: new(1, 1),
            parent: FrameHandle.Null,
            localPose: DPose.Identity,
            frameBounds: FrameBounds.Sphere(1e9f)
        );
    }

    private static Double3 X(double x) => new(x, 0, 0);

    /// <summary>
    /// A dynamic body of the frame ground at <paramref name="x"/>, flying at <paramref name="vx"/> m/s, whose force and
    /// impulse ask for <paramref name="thrust"/> m/s² in the next step.
    /// </summary>
    private NumericBody Body(uint id, double x, double vx, double thrust = 0) =>
        new()
        {
            Entity = new(id, 1),
            Semantic = ground,
            Home = ground,
            Position = X(x),
            HomePosition = X(x),
            Velocity = X(vx),
            Radius = 1,
            Mass = 1,
            ThrustAcceleration = thrust,
        };

    /// <summary>
    /// Runs one update over <paramref name="ticks"/> ticks at 60 ticks per second and returns the bodies with their new homes
    /// and their places in them.
    /// </summary>
    private NumericBody[] Update(int ticks, params NumericBody[] bodies)
    {
        changes.Clear();
        var moves = new List<NumericMove>();
        frames.UpdateNumericFrames(
            bodies: bodies,
            ticks: ticks,
            tickRate: 60,
            changes: changes,
            applyMoves: moves.AddRange
        );
        frames.AssertInvariants(tickRate: 60);
        var homes = moves.ToDictionary(move => move.Entity, move => move.To);
        return
        [
            .. bodies.Select(body =>
            {
                FrameHandle home = homes.TryGetValue(body.Entity, out FrameHandle to) ? to : body.Home;
                return body with { Home = home, HomePosition = body.Position - frames.HomeOffset(home, body.Semantic) };
            }),
        ];
    }

    /// <summary>Runs <paramref name="count"/> updates of the body alone and returns it with its home after the last one.</summary>
    private NumericBody UpdateTimes(int count, int ticks, NumericBody body)
    {
        for (int i = 0; i < count; i++)
            body = Update(ticks, body)[0];
        return body;
    }

    [Fact]
    public void A_free_body_at_60_m_s_stays_in_its_semantic_frame() =>
        Update(1, Body(id: 10, x: 100, vx: 60))[0]
            .Home.Should()
            .Be(ground, "a group of free bodies falls only from 1,000 m/s");

    [Fact]
    public void A_character_at_60_m_s_gets_a_falling_frame_at_the_first_update()
    {
        FrameHandle home = Update(1, Body(id: 10, x: 100, vx: 60) with { Character = true })[0].Home;
        frames.Numeric(home).Should().Be(NumericMode.Falling);
    }

    [Fact]
    public void A_body_that_thrusts_on_every_update_gets_its_falling_frame_at_the_thirtieth_update_at_60_Hz()
    {
        // Half a second of thrust: 30 ticks of a sixtieth of a second.
        NumericBody body = UpdateTimes(29, 1, Body(id: 10, x: 100, vx: 60, thrust: 1));
        body.Home.Should().Be(ground);
        FrameHandle home = Update(1, body)[0].Home;
        frames.Numeric(home).Should().Be(NumericMode.Falling);
    }

    [Fact]
    public void A_body_in_a_falling_frame_keeps_it_for_the_thrust_window_after_its_last_thrust()
    {
        // Two updates of a quarter second, 15 ticks each, make half a second of thrust. Then the body coasts at 60 m/s, below the leave
        // speed of free bodies, and it counts as thrusting until 20 s pass without thrust, at the 80th quiet update.
        NumericBody body = UpdateTimes(2, 15, Body(id: 10, x: 100, vx: 60, thrust: 1));
        FrameHandle pod = body.Home;
        frames.Numeric(pod).Should().Be(NumericMode.Falling);
        body = UpdateTimes(79, 15, body with { ThrustAcceleration = 0 });
        body.Home.Should().Be(pod);
        Update(15, body)[0].Home.Should().Be(ground);
    }

    [Fact]
    public void A_free_body_and_a_body_that_thrusts_whose_common_speed_reaches_the_thrust_create_speed_get_a_falling_frame()
    {
        // Neither body needs a frame by itself: the free one is below 1,000 m/s and the character below 50 m/s. Together
        // they thrust at 50 m/s, so the update must look at the frame although no body passes its own threshold.
        NumericBody[] after = Update(
            1,
            Body(id: 10, x: 0, vx: 60),
            Body(id: 11, x: 3, vx: 40) with
            {
                Character = true,
            }
        );
        after[0].Home.Should().Be(after[1].Home);
        frames.Numeric(after[0].Home).Should().Be(NumericMode.Falling);
    }

    [Fact]
    public void A_body_that_leaves_the_input_loses_its_thrust_clock()
    {
        // One tick short of the onset, the body is gone from one update. Back, it starts its thrust time again from zero.
        NumericBody body = UpdateTimes(29, 1, Body(id: 10, x: 100, vx: 60, thrust: 1));
        Update(1);
        Update(1, body)[0].Home.Should().Be(ground, "one tick of thrust is not half a second");
    }

    [Fact]
    public void A_body_that_turns_static_drops_its_thrust_clock()
    {
        // A ship that parks: its hull turns static. The invariant check of every update finds no clock for a static body.
        NumericBody body = UpdateTimes(2, 15, Body(id: 10, x: 100, vx: 0, thrust: 1));
        Update(15, body with { Static = true, ThrustAcceleration = 0 });
    }

    [Fact]
    public void The_invariant_check_rejects_a_thrust_clock_whose_quiet_ticks_reach_the_window()
    {
        // A second of quiet after a thrust, 60 quiet ticks at 60 Hz: a window shrunk to one second leaves a clock that the
        // update never makes.
        NumericBody body = UpdateTimes(1, 15, Body(id: 10, x: 100, vx: 0, thrust: 1));
        UpdateTimes(4, 15, body with { ThrustAcceleration = 0 });
        frames.NumericOptions = frames.NumericOptions with { ThrustWindow = 1 };
        frames.Invoking(tree => tree.AssertInvariants(tickRate: 60)).Should().Throw<ContractException>();
    }
}
