using AwesomeAssertions;

namespace Tertius.Core.Tests;

/// <summary>
/// <see cref="FrameTree.UpdateNumericFrames"/> on a tree built in the test, with plain body records and the starting values
/// of <see cref="NumericFrameOptions"/> unless a test tunes them. Every numeric frame comes from an update, as in the
/// engine. After every update the tree keeps its invariants, every move starts from the body's home and ends in a live
/// frame of its semantic frame, and the frame that a body leaves is still alive when the moves are handed out.
/// <para>
/// A group of free bodies falls only from 1,000 m/s, and a group with a body that thrusts from 50 m/s. A character thrusts
/// from its first update, so a test whose bodies must fall at a few tens of m/s makes them characters: here a character
/// stands for any body that thrusts, and no thrust clock has to run first. A slow group rests from 2^23 m (about 8,389 km)
/// out, so the scenes of resting frames lie near <see cref="Far"/>.
/// </para>
/// </summary>
public sealed class FrameTreeNumericUpdateTests
{
    private const double Grid = 1 << 20;
    private const double LightYear = 9.4607e15;

    /// <summary>
    /// A place 2^24 m (about 16,777 km) from ground's origin: twice the distance at which a slow group gets a resting frame,
    /// and short of the distance at which a numeric frame nests.
    /// </summary>
    private const double Far = 1 << 24;

    private readonly FrameTree frames = new();
    private readonly FrameHandle ground;
    private readonly PhysicsChanges changes = new();
    private List<NumericMove> moves = [];

    public FrameTreeNumericUpdateTests()
    {
        ground = frames.Create(frameOwner: new(1, 1), parent: FrameHandle.Null, localPose: DPose.Identity,
            frameBounds: FrameBounds.Sphere(1e9f));
    }

    private static Double3 X(double x) => new(x, 0, 0);

    /// <summary>
    /// A body of the frame ground at <paramref name="x"/>, whose place in its home the tree gives now. A
    /// <paramref name="character"/> always thrusts, so its group falls from 50 m/s and keeps falling down to 20 m/s.
    /// </summary>
    private NumericBody Body(uint id, double x, double vx = 0, double radius = 1, FrameHandle home = default,
        double mass = 1,
        bool character = false)
    {
        FrameHandle physicsHome = home.IsNull ? ground : home;
        return new()
        {
            Entity = new(id, 1),
            Semantic = ground,
            Home = physicsHome,
            Position = X(x),
            HomePosition = X(x) - frames.HomeOffset(physicsHome, ground),
            Velocity = X(vx),
            Radius = radius,
            Mass = mass,
            Character = character
        };
    }

    /// <summary>Runs one update and returns the bodies with their new homes and their places in them.</summary>
    private NumericBody[] Update(double dt, params NumericBody[] bodies)
    {
        changes.Clear();
        moves = [];
        frames.UpdateNumericFrames(bodies: bodies, dt: dt, step: 1.0 / 60, changes: changes, applyMoves: handed =>
        {
            foreach (NumericMove move in handed)
                frames.IsAlive(move.From).Should()
                    .BeTrue("physics converts each move through the frame that the body leaves");
            moves.AddRange(handed);
        });
        frames.AssertInvariants();
        Dictionary<Entity, FrameHandle> homes = moves.ToDictionary(move => move.Entity, move => move.To);
        foreach (NumericMove move in moves)
        {
            NumericBody body = bodies.Single(b => b.Entity == move.Entity);
            move.From.Should().Be(body.Home);
            move.To.Should().NotBe(body.Home);
            frames.IsAlive(move.To).Should().BeTrue();
            frames.SemanticOf(move.To).Should().Be(body.Semantic);
        }

        return
        [
            .. bodies.Select(body =>
            {
                FrameHandle home = homes.TryGetValue(body.Entity, out FrameHandle to) ? to : body.Home;
                return body with { Home = home, HomePosition = body.Position - frames.HomeOffset(home, body.Semantic) };
            })
        ];
    }

    /// <summary>Moves the falling frames over <paramref name="seconds"/> in one step, as the engine does between updates.</summary>
    private void Advance(double seconds)
    {
        frames.Update();
        frames.BeginStep([], 1.0 / 60, lastStepFailed: false);
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
        // Just below the 50 m/s from which a group that thrusts falls.
        Update(0.1, Body(id: 10, x: 100, vx: 49, character: true));
        moves.Should().BeEmpty();
        frames.NumericCount.Should().Be(0);
    }

    [Fact]
    public void A_fast_body_gets_a_falling_frame_at_its_place_with_its_velocity()
    {
        FrameHandle home = Update(0.1, Body(id: 10, x: 100, vx: 60, character: true))[0].Home;
        frames.Numeric(home).Should().Be(NumericMode.Falling);
        frames.LocalPose(home).Position.Should().Be(X(100));
        frames.LinearVelocity(home).Should().Be(X(60));
        changes.FramesCreated.Should().Equal(home);
    }

    [Fact]
    public void A_far_slow_body_gets_a_resting_frame()
    {
        FrameHandle home = Update(0.1, Body(id: 10, x: Far, vx: 3))[0].Home;
        frames.Numeric(home).Should().Be(NumericMode.Resting);
        frames.LinearVelocity(home).Should().Be(Double3.Zero);
    }

    [Fact]
    public void Three_bodies_in_a_chain_share_one_frame()
    {
        // Each body reaches 4 m: the first and the last are 6 m apart and link only through the middle one.
        NumericBody[] after = Update(0.1, Body(id: 10, x: 0, vx: 60, radius: 2, character: true),
            Body(id: 11, x: 3, vx: 60, radius: 2, character: true),
            Body(id: 12, x: 6, vx: 60, radius: 2, character: true));
        after.Select(body => body.Home).Distinct().Should().ContainSingle().Which.Should().NotBe(ground);
        frames.NumericCount.Should().Be(1);
    }

    [Fact]
    public void Groups_get_their_frames_and_moves_in_the_order_of_their_first_body()
    {
        NumericBody[] after = Update(0.1, Body(id: 10, x: 0, vx: 60, character: true),
            Body(id: 11, x: 1_000, vx: 60, character: true),
            Body(id: 12, x: 2, vx: 60, character: true));
        changes.FramesCreated.Should().Equal(after[0].Home, after[1].Home);
        after[2].Home.Should().Be(after[0].Home);
        moves.Select(move => move.Entity.Index).Should().Equal(10u, 12u, 11u);
        frames.LocalPose(after[0].Home).Position.Should().Be(X(1));
    }

    [Fact]
    public void An_anchored_group_near_the_origin_that_does_not_fall_stays_in_its_semantic_frame()
    {
        Update(0.1, Body(id: 10, x: 0) with { Static = true }, Body(id: 11, x: 2, vx: 10));
        moves.Should().BeEmpty();
    }

    [Fact]
    public void A_far_static_body_and_a_slow_body_on_it_share_a_resting_frame()
    {
        NumericBody[] after = Update(0.1, Body(id: 10, x: Far) with { Static = true }, Body(id: 11, x: Far + 1));
        after[0].Home.Should().Be(after[1].Home);
        frames.Numeric(after[0].Home).Should().Be(NumericMode.Resting);
    }

    [Fact]
    public void A_fast_body_near_a_static_body_joins_the_static_bodys_system_and_keeps_no_ghost()
    {
        NumericBody[] after = Update(0.1, Body(id: 10, x: 0) with { Static = true },
            Body(id: 11, x: 2, vx: 60, character: true));
        after[0].Home.Should().Be(ground, "a static body stays in its own system");
        after[1].Home.Should().Be(ground, "the static body is infinitely heavy, so the group takes its home");
        moves.Should().BeEmpty();
        frames.NumericCount.Should().Be(0);
        frames.AnchorGhosts.IsEmpty.Should().BeTrue("the static body is a real body of the group's system");

        frames.Numeric(Update(0.1, Body(id: 11, x: 2, vx: 60, character: true))[0].Home).Should()
            .Be(NumericMode.Falling, "alone, the body falls");
    }

    [Fact]
    public void A_fast_body_near_the_hull_of_a_Static_or_Spin_frame_joins_the_hulls_system()
    {
        NumericBody[] after = Update(0.1, Body(id: 10, x: 0) with { Fixed = true },
            Body(id: 11, x: 2, vx: 60, character: true));
        after[0].Home.Should().Be(ground);
        after[1].Home.Should().Be(ground, "the hull's center rests in the system of ground");
        frames.AnchorGhosts.IsEmpty.Should().BeTrue();

        frames.Numeric(Update(0.1, Body(id: 11, x: 2, vx: 60, character: true))[0].Home).Should()
            .Be(NumericMode.Falling, "alone, the body falls");
    }

    [Fact]
    public void A_falling_frame_that_reaches_a_static_body_hands_its_body_to_the_static_bodys_system()
    {
        FrameHandle pod = Update(0.1, Body(id: 11, x: 0, vx: 7_700))[0].Home;
        NumericBody[] after = Update(0.1, Body(id: 10, x: 3) with { Static = true },
            Body(id: 11, x: 0, vx: 7_700, home: pod));
        after[1].Home.Should().Be(ground);
        moves.Should().Equal(new NumericMove(new(11, 1), pod, ground));
        frames.IsAlive(pod).Should().BeFalse("the falling frame lost its only member");
        frames.AnchorGhosts.IsEmpty.Should().BeTrue();
    }

    [Fact]
    public void A_fast_body_that_passes_a_static_body_joins_it_within_the_lookahead_lets_go_and_falls_again()
    {
        // Reach: radii of 1 m each plus 60 m/s for 2 s, 122 m, and 10 m more while both share a system.
        NumericBody wall = Body(id: 10, x: 0) with { Static = true };
        FrameHandle pod = Update(0.1, wall, Body(id: 11, x: -133, vx: 60, character: true))[1].Home;
        frames.Numeric(pod).Should().Be(NumericMode.Falling, "the body is beyond the reach and the split margin");

        Update(0.1, wall, Body(id: 11, x: -121, vx: 60, home: pod, character: true))[1].Home.Should()
            .Be(ground, "the merge test reaches the wall");
        Update(0.1, wall, Body(id: 11, x: 131, vx: 60, character: true))[1].Home.Should()
            .Be(ground, "the split margin holds the body");

        FrameHandle again = Update(0.1, wall, Body(id: 11, x: 133, vx: 60, character: true))[1].Home;
        frames.Numeric(again).Should()
            .Be(NumericMode.Falling, "the body falls again as soon as the merge test lets go");
        frames.LinearVelocity(again).Should().Be(X(60));
        frames.AnchorGhosts.IsEmpty.Should().BeTrue();
    }

    [Fact]
    public void A_fast_body_near_a_far_static_body_joins_its_resting_frame()
    {
        FrameHandle raft = Update(0.1, Body(id: 10, x: Far) with { Static = true })[0].Home;
        frames.Numeric(raft).Should().Be(NumericMode.Resting);

        NumericBody[] after = Update(0.1, Body(id: 10, x: Far, home: raft) with { Static = true },
            Body(id: 11, x: Far + 50, vx: 60, character: true));
        after[1].Home.Should().Be(raft);
        frames.Numeric(raft).Should().Be(NumericMode.Resting);
        frames.LinearVelocity(raft).Should().Be(Double3.Zero);
        frames.LocalPose(raft).Position.Should().Be(X(Far), "the static body alone decides where its frame rests");
        frames.NumericCount.Should().Be(1);
    }

    [Fact]
    public void A_falling_frame_cannot_pull_a_static_body_out_of_the_semantic_frames_own_system()
    {
        // 6,000 km out, the wall is nearer than the create distance of 8,389 km, so it lives in the system of ground.
        // Measured from the group's own center and with the mode of its falling frame, the group would come to rest there,
        // beyond the leave distance of 4,194 km, and take the wall.
        const double wall = 6e6;
        FrameHandle pod = Update(0.1, Body(id: 11, x: wall - 200, vx: 60, mass: 1_000, character: true))[0].Home;
        frames.Numeric(pod).Should().Be(NumericMode.Falling);
        NumericBody[] after = Update(0.1, Body(id: 10, x: wall) with { Static = true },
            Body(id: 11, x: wall - 100, vx: 60, home: pod, mass: 1_000, character: true));
        after[0].Home.Should().Be(ground);
        after[1].Home.Should().Be(ground);
        frames.NumericCount.Should().Be(0);
    }

    [Fact]
    public void A_fast_body_that_links_two_static_bodies_in_different_homes_takes_them_into_the_resting_frame()
    {
        // The near rock lies within the create distance of 2^23 m, so it stays in the system of ground; the far rock rests in
        // a frame.
        const double edge = 1 << 23;
        NumericBody[] before = Update(0.1, Body(id: 10, x: edge - 20) with { Static = true },
            Body(id: 12, x: edge + 20) with { Static = true });
        FrameHandle raft = before[1].Home;
        before[0].Home.Should().Be(ground);
        frames.Numeric(raft).Should().Be(NumericMode.Resting);

        NumericBody[] after = Update(0.1, before[0], before[1], Body(id: 11, x: edge, vx: 60, character: true));
        after.Select(body => body.Home).Should().OnlyContain(home => home == raft);
        moves.Should().Equal(new NumericMove(new(10, 1), ground, raft), new NumericMove(new(11, 1), ground, raft));
        frames.AnchorGhosts.IsEmpty.Should().BeTrue("every anchor is a real body of the group's system");

        Update(0.1, after);
        moves.Should().BeEmpty("the next sync point makes the same choice");
    }

    [Fact]
    public void A_group_with_a_static_body_keeps_its_frame_when_a_heavier_group_without_one_splits_off()
    {
        NumericBody[] together =
            Update(0.1, Body(id: 10, x: Far) with { Static = true }, Body(id: 11, x: Far + 1, mass: 1_000));
        FrameHandle raft = together[0].Home;
        together[1].Home.Should().Be(raft);

        NumericBody[] after = Update(0.1, together[0],
            together[1] with
            {
                Position = X(Far + 500), HomePosition = X(Far + 500) - frames.HomeOffset(raft, ground)
            });
        after[0].Home.Should().Be(raft, "a static body outweighs any mass");
        after[1].Home.Should().NotBe(raft);
        frames.Numeric(after[1].Home).Should().Be(NumericMode.Resting);
    }

    [Fact]
    public void A_body_at_the_edge_of_the_reach_of_a_fixed_hull_does_not_go_in_and_out_of_the_semantic_frames_system()
    {
        // Alone, the slow body far out gets a resting frame; with the fixed hull, it stays in the system of ground.
        NumericBody hull = Body(id: 20, x: Far) with { Fixed = true };
        Update(0.1, hull, Body(id: 10, x: Far + 1));
        moves.Should().BeEmpty();

        // 7 m apart: beyond the reach of 2 m, within the split margin of 10 m, so it stays.
        Update(0.1, hull, Body(id: 10, x: Far + 7));
        moves.Should().BeEmpty();

        // 13 m apart for a whole split cooldown: it leaves into a resting frame of its own.
        NumericBody[] after = Update(1, hull, Body(id: 10, x: Far + 13));
        FrameHandle own = after[1].Home;
        frames.Numeric(own).Should().Be(NumericMode.Resting);

        // Back to 7 m apart: the two systems differ, so there is no margin, and it stays in its frame.
        Update(0.1, hull, Body(id: 10, x: Far + 7, home: own));
        moves.Should().BeEmpty();
    }

    /// <summary>A fixed hull at <see cref="Far"/>, at rest.</summary>
    private NumericBody FarHull() => Body(id: 20, x: Far) with { Fixed = true };

    /// <summary>
    /// A slow body 15 m from <see cref="FarHull"/>. At 7 m/s its reach is 16 m, which links it to the hull from any system.
    /// At rest its reach is 2 m, and 12 m with the split margin, which does not. Alone, it needs a resting frame.
    /// </summary>
    private NumericBody EdgeBody(double vx = 0, FrameHandle home = default) =>
        Body(id: 10, x: Far + 15, vx: vx, home: home);

    [Fact]
    public void
        A_body_that_crosses_the_edge_of_a_fixed_hulls_reach_every_update_changes_its_home_at_most_once_per_cooldown()
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

        // Beyond the reach for longer than a cooldown, the body leaves into a resting frame: the hold kept it, not the scene.
        for (int update = 0; update < 11; update++)
            home = Update(0.1, FarHull(), EdgeBody(home: home))[1].Home;
        frames.Numeric(home).Should().Be(NumericMode.Resting);
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
        moves.Should().Equal(new NumericMove(new(10, 1), ground, after[1].Home));
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
        frames.Numeric(after[1].Home).Should()
            .Be(NumericMode.Resting, "nothing keeps it in the system of ground any more");
    }

    [Fact]
    public void A_held_body_that_a_member_of_a_numeric_frame_reaches_joins_that_frame_at_once()
    {
        FrameHandle raft = Update(0.25, FarHull(), EdgeBody(vx: 7), Body(id: 11, x: Far + 100))[2].Home;
        Update(0.25, FarHull(), EdgeBody(), Body(id: 11, x: Far + 100, home: raft));
        moves.Should().BeEmpty("the body is held in the system of ground");

        Update(0.25, FarHull(), EdgeBody(), Body(id: 11, x: Far + 17, home: raft));
        moves.Should().Equal(new NumericMove(new(10, 1), ground, raft));
    }

    [Fact]
    public void A_body_kept_with_a_fixed_hull_that_flies_off_fast_leaves_at_once()
    {
        Update(0.25, FarHull(), EdgeBody(vx: 7) with { Character = true });
        // At 60 m/s the merge test reaches 122 m, and 132 m in one system: a fast body nearer than that joins the hull.
        NumericBody[] after = Update(0.25, FarHull(), Body(id: 10, x: Far + 200, vx: 60, character: true));
        frames.Numeric(after[1].Home).Should()
            .Be(NumericMode.Falling, "falling has its own hysteresis, so no cooldown holds it");
    }

    [Fact]
    public void A_near_body_that_moves_far_gets_its_frame_at_once()
    {
        // The far body makes the update work on the semantic frame, so that it sees the near one.
        FrameHandle raft = Update(0.25, Body(id: 11, x: Far + 30_000), Body(id: 10, x: 100))[0].Home;
        NumericBody[] after = Update(0.25, Body(id: 11, x: Far + 30_000, home: raft), Body(id: 10, x: Far));
        frames.Numeric(after[1].Home).Should().Be(NumericMode.Resting);
    }

    [Fact]
    public void A_fixed_hull_in_a_numeric_frame_goes_back_to_its_semantic_frame_with_its_frame()
    {
        FrameHandle hull = frames.Create(frameOwner: new(20, 1), parent: ground,
            localPose: new(X(50), DQuaternion.Identity), frameBounds: FrameBounds.Sphere(50));
        frames.SetHullMotion(hull, BodyMotion.Dynamic);
        frames.SetTreeLinearVelocity(hull, X(60));
        NumericBody owner = Body(id: 20, x: 50, vx: 60, character: true) with { OwnedFrame = hull };
        FrameHandle pod = Update(0.1, owner)[0].Home;
        frames.Numeric(pod).Should().Be(NumericMode.Falling);
        frames.TreeParent(hull).Should().Be(pod);

        // The game parks the ship: its hull becomes static.
        frames.SetHullMotion(hull, BodyMotion.Static);
        Update(0.1, Body(id: 20, x: 50, vx: 60, home: pod, character: true) with { OwnedFrame = hull, Fixed = true });

        moves.Should().Equal(new NumericMove(new(20, 1), pod, ground));
        frames.TreeParent(hull).Should().Be(ground);
        frames.LocalPose(hull).Position.Should().Be(X(50));
        frames.IsAlive(pod).Should().BeFalse();
    }

    [Theory]
    [InlineData(BodyMotion.Static)]
    [InlineData(BodyMotion.Kinematic)]
    [InlineData(null)]
    public void A_frame_without_a_dynamic_hull_under_a_numeric_frame_breaks_an_invariant(BodyMotion? motion)
    {
        FrameHandle hull = frames.Create(frameOwner: new(20, 1), parent: ground,
            localPose: new(X(50), DQuaternion.Identity), frameBounds: FrameBounds.Sphere(50));
        frames.SetHullMotion(hull, BodyMotion.Dynamic);
        Update(0.1, Body(id: 20, x: 50, vx: 60, character: true) with { OwnedFrame = hull });
        frames.Numeric(frames.TreeParent(hull)).Should().Be(NumericMode.Falling);
        frames.AssertInvariants();

        frames.SetHullMotion(hull, motion);

        frames.Invoking(tree => tree.AssertInvariants()).Should().Throw<ContractException>();
    }

    [Fact]
    public void A_numeric_frame_with_a_hull_motion_breaks_an_invariant()
    {
        FrameHandle pod = Update(0.1, Body(id: 10, x: 100, vx: 2_000))[0].Home;
        frames.Numeric(pod).Should().Be(NumericMode.Falling);
        frames.AssertInvariants();

        frames.SetHullMotion(pod, BodyMotion.Dynamic);

        frames.Invoking(tree => tree.AssertInvariants()).Should().Throw<ContractException>();
    }

    [Fact]
    public void A_slow_near_frame_hands_its_bodies_back_and_goes_away()
    {
        FrameHandle pod = Update(0.1, Body(id: 10, x: 100, vx: 60, character: true))[0].Home;
        frames.Numeric(pod).Should().Be(NumericMode.Falling);
        Update(0.1, Body(id: 10, x: 100, vx: 10, home: pod, character: true))[0].Home.Should().Be(ground);
        frames.IsAlive(pod).Should().BeFalse();
        changes.FramesDestroyed.Should().Equal(pod);
    }

    [Fact]
    public void A_numeric_frame_without_bodies_goes_away()
    {
        FrameHandle pod = Update(0.1, Body(id: 10, x: Far))[0].Home;
        frames.Numeric(pod).Should().Be(NumericMode.Resting);
        Update(0.1);
        frames.IsAlive(pod).Should().BeFalse();
    }

    [Fact]
    public void Members_that_drift_apart_split_only_after_the_cooldown()
    {
        NumericBody[] bodies = Update(0.1, Body(id: 10, x: 0, vx: 60, character: true),
            Body(id: 11, x: 1, vx: 60, character: true));
        FrameHandle pod = bodies[0].Home;
        bodies = [bodies[0], Body(id: 11, x: 100, vx: 60, home: pod, character: true)];
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
        FrameHandle pod =
            Update(0.1, Body(id: 10, x: 0, vx: 60, character: true), Body(id: 11, x: 1, vx: 60, character: true))[0]
                .Home;
        frames.Numeric(pod).Should().Be(NumericMode.Falling);
        NumericBody near = Body(id: 10, x: 0, vx: 60, home: pod, character: true),
            far = Body(id: 11, x: 100, vx: 60, home: pod, character: true);
        Update(0.5, near, far);
        Update(0.5, near, Body(id: 11, x: 5, vx: 60, home: pod, character: true));
        Update(0.5, near, far);
        moves.Should().BeEmpty();
    }

    [Fact]
    public void A_heavier_group_that_splits_off_later_takes_the_frame_from_the_first_group()
    {
        FrameHandle pod = Update(0.1, Body(id: 10, x: 0, vx: 60, mass: 1, character: true),
            Body(id: 11, x: 1, vx: 60, mass: 5, character: true))[0].Home;
        NumericBody[] bodies =
        [
            Body(id: 10, x: 0, vx: 60, home: pod, mass: 1, character: true),
            Body(id: 11, x: 100, vx: 60, home: pod, mass: 5, character: true)
        ];
        Update(0.5, bodies);
        NumericBody[] after = Update(0.5, bodies);
        after[1].Home.Should().Be(pod, "the group with more mass in the frame keeps it");
        after[0].Home.Should().NotBe(pod);
        moves.Should().Equal(new NumericMove(new(10, 1), pod, after[0].Home));
    }

    [Fact]
    public void Two_numeric_frames_whose_members_meet_merge_into_the_one_with_more_mass()
    {
        NumericBody[] first = Update(0.1, Body(id: 10, x: 0, vx: 60, mass: 1, character: true),
            Body(id: 11, x: 1_000, vx: 60, mass: 5, character: true));
        FrameHandle light = first[0].Home, heavy = first[1].Home;
        light.Should().NotBe(heavy);

        Update(0.1, Body(id: 10, x: 0, vx: 60, home: light, mass: 1, character: true),
            Body(id: 11, x: 1.5, vx: 60, home: heavy, mass: 5, character: true));

        moves.Should().Equal(new NumericMove(new(10, 1), light, heavy));
        frames.IsAlive(light).Should().BeFalse("the frame that lost its bodies goes");
        changes.FramesDestroyed.Should().Equal(light);
    }

    [Fact]
    public void Bodies_of_two_semantic_frames_never_link()
    {
        FrameHandle other = frames.Create(frameOwner: new(2, 1), parent: FrameHandle.Null, localPose: DPose.Identity,
            frameBounds: FrameBounds.Sphere(1e9f));
        NumericBody[] after = Update(0.1, Body(id: 10, x: 0, vx: 60, character: true),
            Body(id: 11, x: 0, vx: 60, character: true) with { Semantic = other, Home = other });
        after[0].Home.Should().NotBe(after[1].Home);
        frames.Numeric(after[0].Home).Should().Be(NumericMode.Falling);
        frames.Numeric(after[1].Home).Should().Be(NumericMode.Falling);
        frames.SemanticOf(after[1].Home).Should().Be(other);
    }

    [Fact]
    public void A_frame_takes_over_its_members_common_velocity_and_tells_physics()
    {
        FrameHandle pod = Update(0.1, Body(id: 10, x: 0, vx: 7_700))[0].Home;
        Update(0.1, Body(id: 10, x: 0, vx: 7_702, home: pod));
        frames.LinearVelocity(pod).Should().Be(X(7_702));
        changes.FramesShifted.Should().Equal(new FrameShift(pod, Double3.Zero, X(2)));
        moves.Should().BeEmpty();
    }

    [Fact]
    public void A_drain_and_a_recenter_keep_the_pose_and_velocity_of_a_ship_frame_in_its_semantic_frame()
    {
        FrameHandle hull = frames.Create(frameOwner: new(20, 1), parent: ground,
            localPose: new(X(50), DQuaternion.Identity), frameBounds: FrameBounds.Sphere(50));
        frames.SetHullMotion(hull, BodyMotion.Dynamic);
        frames.SetTreeLinearVelocity(hull, X(60));
        FrameHandle pod = Update(0.1, Body(id: 20, x: 50, vx: 60, character: true) with { OwnedFrame = hull })[0].Home;
        DPose pose = frames.LocalPose(hull);
        Double3 velocity = frames.LinearVelocity(hull);

        // The members' center is 2 km from the frame's origin and their speed is 2 m/s more: the frame moves and drains.
        Update(0.1, Body(id: 20, x: 2_050, vx: 62, home: pod, character: true) with { OwnedFrame = hull });

        changes.FramesShifted.Should().Equal(new FrameShift(pod, X(2_000), X(2)));
        frames.LocalPose(hull).Should().Be(pose);
        frames.LinearVelocity(hull).Should().Be(velocity);
        frames.TreeLocalPose(hull).Position.Should().Be(X(-2_000));
    }

    [Fact]
    public void A_resting_frame_whose_members_speed_up_starts_to_fall()
    {
        FrameHandle pod = Update(0.1, Body(id: 10, x: Far, character: true))[0].Home;
        frames.Numeric(pod).Should().Be(NumericMode.Resting);
        Update(0.1, Body(id: 10, x: Far, vx: 60, home: pod, character: true));
        frames.Numeric(pod).Should().Be(NumericMode.Falling);
        frames.LinearVelocity(pod).Should().Be(X(60));
    }

    [Fact]
    public void A_far_falling_frame_whose_members_slow_down_comes_to_rest()
    {
        FrameHandle pod = Update(0.1, Body(id: 10, x: Far, vx: 60, character: true))[0].Home;
        frames.Numeric(pod).Should().Be(NumericMode.Falling);
        Update(0.1, Body(id: 10, x: Far, vx: 10, home: pod, character: true));
        frames.Numeric(pod).Should().Be(NumericMode.Resting);
        frames.LinearVelocity(pod).Should().Be(Double3.Zero);
    }

    [Fact]
    public void A_moving_owner_takes_its_frame_into_its_new_home()
    {
        FrameHandle hull = frames.Create(frameOwner: new(20, 1), parent: ground,
            localPose: new(X(50), DQuaternion.Identity), frameBounds: FrameBounds.Sphere(50));
        frames.SetHullMotion(hull, BodyMotion.Dynamic);
        FrameHandle home = Update(0.1, Body(id: 20, x: 50, vx: 60, character: true) with { OwnedFrame = hull })[0].Home;
        frames.Numeric(home).Should().Be(NumericMode.Falling);
        frames.TreeParent(hull).Should().Be(home);
    }

    [Fact]
    public void A_far_frame_nests_on_the_grid_and_keeps_its_place()
    {
        FrameHandle pod = Update(0.1, Body(id: 10, x: 3e9, vx: 60, character: true))[0].Home;
        FrameHandle nest = frames.TreeParent(pod);
        frames.Numeric(nest).Should().Be(NumericMode.Nesting);
        changes.FramesCreated.Should().Equal(pod, nest);
        frames.HomeOffset(pod, ground).Should().Be(X(3e9));
        frames.TreeLocalPose(pod).Position.Length.Should().BeLessThanOrEqualTo(Grid / 2);
    }

    [Fact]
    public void A_second_far_frame_joins_the_nearest_nest()
    {
        NumericBody[] after = Update(0.1, Body(id: 10, x: 3e9, vx: 60, character: true),
            Body(id: 11, x: 3.1e9, vx: 60, character: true));
        frames.TreeParent(after[1].Home).Should().Be(frames.TreeParent(after[0].Home));
        frames.NumericCount.Should().Be(3);
    }

    [Fact]
    public void A_nest_jumps_by_whole_grid_steps_when_its_frames_drift_far_together()
    {
        const double place = 3L << 30;
        FrameHandle pod = Update(0.1, Body(id: 10, x: place, vx: 60, character: true))[0].Home;
        FrameHandle nest = frames.TreeParent(pod);
        frames.TreeLocalPose(nest).Position.Should().Be(X(place));
        Advance(2e9 / 60);
        Double3 drift = frames.TreeLocalPose(pod).Position;

        Update(0.1, Body(id: 10, x: place + drift.X, vx: 60, home: pod, character: true));

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
        NumericBody[] first = Update(0.1, Body(id: 10, x: place, vx: 60, character: true),
            Body(id: 11, x: place + 1e8));
        FrameHandle pod = first[0].Home, raft = first[1].Home;
        FrameHandle nest = frames.TreeParent(pod);
        frames.TreeParent(raft).Should().Be(nest);

        // The pod drifts 2e9 m away: a jump of the nest would leave the raft far from it.
        Advance(2e9 / 60);
        Double3 drift = frames.TreeLocalPose(pod).Position;
        Update(0.1, Body(id: 10, x: place + drift.X, vx: 60, home: pod, character: true),
            Body(id: 11, x: place + 1e8, home: raft));
        FrameHandle newNest = frames.TreeParent(pod);
        newNest.Should().NotBe(nest);
        frames.Numeric(newNest).Should().Be(NumericMode.Nesting);
        frames.TreeParent(raft).Should().Be(nest);
        frames.HomeOffset(pod, ground).Should().Be(X(place) + drift);

        // The raft's body goes: the raft goes, then its nest.
        Update(0.1, Body(id: 10, x: place + drift.X, vx: 60, home: pod, character: true));
        changes.FramesDestroyed.Should().Equal(raft, nest);
    }

    [Fact]
    public void The_merge_test_far_from_the_origin_measures_bodies_by_their_places_in_their_homes()
    {
        frames.NumericOptions = new() { RecenterDistance = 1e12 };
        NumericBody[] first = Update(0.1, Body(id: 10, x: LightYear, vx: 60, character: true),
            Body(id: 11, x: LightYear + 1_000, vx: 60, character: true));
        FrameHandle a = first[0].Home, b = first[1].Home;
        frames.TreeParent(a).Should().Be(frames.TreeParent(b));
        double between = frames.Relative(b, a).Position.X;

        // A light-year out, a double steps by 2 m, so both rows round to one place. In their homes, they are 3 m apart.
        NumericBody bodyA = Body(id: 10, x: LightYear, vx: 60, home: a, character: true) with
        {
            HomePosition = Double3.Zero
        };
        NumericBody bodyB = Body(id: 11, x: LightYear, vx: 60, home: b, character: true) with
        {
            HomePosition = X(3 - between)
        };
        Update(0.1, bodyA, bodyB);
        moves.Should().BeEmpty("radii of 1 m do not reach 3 m");

        Update(0.1, bodyA, bodyB with { HomePosition = X(1.5 - between) });
        moves.Should().ContainSingle();
    }

    [Fact]
    public void A_position_in_a_nested_frame_a_light_year_out_rounds_once_in_the_semantic_frame()
    {
        FrameHandle pod = Update(0.1, Body(id: 10, x: LightYear, vx: 64, character: true))[0].Home;
        frames.Numeric(frames.TreeParent(pod)).Should().Be(NumericMode.Nesting);
        // 3/256 s at 64 m/s moves the pod 0.75 m in its nest, exactly.
        Advance(3.0 / 256);

        // The exact place is a light-year plus 1.25 m, and a double there steps by 2 m. Rounded once, that is a light-year
        // plus 2 m. Rounding the pod's place first gives a light-year, and adding 0.5 m to it rounds back to a light-year.
        frames.InSemantic(pod, ground, X(0.5)).Should().Be(X(LightYear + 2));
    }

    [Fact]
    public void A_semantic_frame_that_dies_takes_its_numeric_frames_with_it_and_its_ship_frames_keep_their_places()
    {
        DQuaternion turned = DQuaternion.FromAxisAngle(new(0, 1, 0), 0.5);
        FrameHandle station = frames.Create(frameOwner: new(2, 1), parent: ground, localPose: new(X(1_000), turned),
            frameBounds: FrameBounds.Sphere(1e6f));
        frames.SetAngularVelocity(station, new(0, 0.1f, 0));
        FrameHandle hull = frames.Create(frameOwner: new(20, 1), parent: station,
            localPose: new(X(50), DQuaternion.Identity), frameBounds: FrameBounds.Sphere(50));
        frames.SetHullMotion(hull, BodyMotion.Dynamic);
        frames.SetTreeLinearVelocity(hull, X(60));
        NumericBody owner = Body(id: 20, x: 50, vx: 60, character: true) with
        {
            Semantic = station, Home = station, HomePosition = X(50), OwnedFrame = hull
        };
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
            test.Update(0.1, test.Body(id: 10, x: 0, vx: 60, character: true), test.Body(id: 11, x: 3e9, vx: 0),
                test.Body(id: 12, x: 1, vx: 60, character: true),
                test.Body(id: 13, x: Far));
            return (test.moves,
                test.changes.FramesCreated.Select(frame => test.frames.HomeOffset(frame, test.ground)).ToList());
        }

        (List<NumericMove> moves, List<Double3> places) = Run();
        (List<NumericMove> again, List<Double3> placesAgain) = Run();
        again.Should().Equal(moves);
        placesAgain.Should().Equal(places);
    }
}
