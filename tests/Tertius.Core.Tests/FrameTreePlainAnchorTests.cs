using System.Numerics;
using AwesomeAssertions;
using Tertius.Frames.Rails;

namespace Tertius.Core.Tests;

/// <summary>
/// Following frames whose anchor is a plain kinematic body, the body of an entity that owns no frame
/// (docs/plans/numeric-frames.md, "Following frames", ticket ter-zg77), on a tree built in the test. The anchor is a member
/// of its following frame's system, at the origin, and the tree keeps it by entity. The tests run
/// <see cref="FrameTree.UpdateNumericFrames"/> with plain body records at the defaults of <see cref="NumericFrameOptions"/>,
/// under which a kinematic body gets a following frame from 1,000 m/s or 2^23 m and keeps it down to 400 m/s or 2^22 m.
/// The lookahead of the merge test is 2 s. After every update the tree keeps its invariants.
/// </summary>
public class FrameTreePlainAnchorTests
{
    private const double EarthMu = 3.986004418e14;
    private const double Step = 1.0 / 60;

    private readonly FrameTree frames = new();
    // A root frame without gravity, and earth with its point gravity.
    private readonly FrameHandle space;
    private readonly FrameHandle earth;
    private readonly PhysicsChanges changes = new();
    private List<NumericMove> moves = [];

    public FrameTreePlainAnchorTests()
    {
        space = frames.Create(new Entity(1, 1), FrameHandle.Null, DPose.Identity, FrameBounds.Sphere(1e9f));
        earth = frames.Create(new Entity(2, 1), FrameHandle.Null, DPose.Identity, FrameBounds.Sphere(1e9f), GravityField.Point(EarthMu));
    }

    /// <summary>
    /// A plain kinematic ball of the entity <paramref name="id"/>, of 0.5 m and 1 kg, at <paramref name="position"/> in
    /// <paramref name="semantic"/>, or in space, flying at <paramref name="velocity"/> there, in the system of
    /// <paramref name="home"/>, or of the semantic frame itself. The World marks every kinematic body as one that follows.
    /// </summary>
    private NumericBody Ball(uint id, Double3 position, Double3 velocity, FrameHandle home = default, FrameHandle semantic = default)
    {
        FrameHandle frame = semantic.IsNull ? space : semantic;
        FrameHandle physicsHome = home.IsNull ? frame : home;
        return new NumericBody
        {
            Entity = new Entity(id, 1),
            Semantic = frame,
            Home = physicsHome,
            Position = position,
            HomePosition = position - frames.HomeOffset(physicsHome, frame),
            Velocity = velocity,
            Radius = 0.5,
            Mass = 1,
            Kinematic = true,
            Follows = true,
        };
    }

    /// <summary>A dynamic crate of 1 m and 10 kg, as <see cref="Ball"/> places it.</summary>
    private NumericBody Crate(uint id, Double3 position, Double3 velocity, FrameHandle home = default) =>
        Ball(id, position, velocity, home) with { Radius = 1, Mass = 10, Kinematic = false, Follows = false };

    /// <summary>The intents of a step in which only <paramref name="ball"/> wrote one: <paramref name="move"/>, relative to its semantic frame.</summary>
    private static PhysicsIntent[] Moving(in NumericBody ball, in MotionIntent move, DPose? teleport = null)
    {
        var intents = new PhysicsIntent[ball.Entity.Index + 1];
        intents[ball.Entity.Index] = new PhysicsIntent
        {
            Frame = ball.Semantic,
            Move = move,
            Flags = teleport is null ? IntentFlags.None : IntentFlags.Teleport,
            TeleportPose = teleport ?? default,
        };
        return intents;
    }

    /// <summary>Runs one update and returns the bodies with their new homes and their places in them.</summary>
    private NumericBody[] Update(params NumericBody[] bodies)
    {
        changes.Clear();
        moves = [];
        frames.UpdateNumericFrames(bodies, Step, Step, changes, handed =>
        {
            foreach (NumericMove move in handed)
                frames.IsAlive(move.From).Should().BeTrue("physics converts each move through the frame that the body leaves");
            moves.AddRange(handed);
        });
        frames.AssertInvariants();
        var homes = moves.ToDictionary(move => move.Entity, move => move.To);
        return bodies.Select(body =>
        {
            FrameHandle home = homes.TryGetValue(body.Entity, out FrameHandle to) ? to : body.Home;
            return body with { Home = home, HomePosition = body.Position - frames.HomeOffset(home, body.Semantic) };
        }).ToArray();
    }

    /// <summary>
    /// A fast plain kinematic body gets a following frame at its place, and it moves into it: it is the frame's anchor. A
    /// plain body owns no frame, so the tree keeps no proper acceleration of it, and the frame starts with the chord of a
    /// free fall: the body's velocity less half a step of the pull at its place.
    /// </summary>
    [Fact]
    public void A_fast_plain_kinematic_body_gets_a_following_frame_at_its_place_with_the_chord_of_a_free_fall_and_moves_into_it()
    {
        var place = new Double3(6.8e6, 0, 0);
        var velocity = new Double3(0, 7_660, 0);

        NumericBody ball = Update(Ball(10, place, velocity, semantic: earth))[0];

        FrameHandle home = ball.Home;
        frames.Numeric(home).Should().Be(NumericMode.Following);
        frames.TreeParent(home).Should().Be(earth, "a following frame lies in its anchor's semantic frame");
        frames.Anchors(home, ball.Entity).Should().BeTrue("the body is the anchor of its following frame");
        frames.TreeLocalPose(home).Position.Should().Be(place, "the frame starts at the anchor's place");
        frames.LinearVelocity(home).Should().Be(velocity - GravityField.Point(EarthMu).AtPrecise(place) * (Step / 2),
            "the frame starts with the chord of a free fall");
        moves.Should().Equal(new NumericMove(ball.Entity, earth, home));
        changes.FramesCreated.Should().Equal(home);
    }

    /// <summary>
    /// Before each step, the following frame of a plain anchor plans the anchor's end pose from the frame's origin, where
    /// the anchor stands, and takes the acceleration that lands it on the plan. At the sync point it takes the plan's place
    /// and chord. An end pose moves at one velocity over the step, so the anchor has no velocity past the frame's chord.
    /// </summary>
    [Fact]
    public void Before_each_step_the_following_frame_of_a_plain_anchor_plans_its_end_pose_from_its_origin_and_lands_on_it()
    {
        NumericBody ball = Update(Ball(10, new Double3(100, 0, 0), new Double3(2_000, 0, 0)))[0];
        FrameHandle home = ball.Home;
        DQuaternion turn = DQuaternion.FromAxisAngle(Double3.UnitZ, 0.5);

        for (int tick = 1; tick <= 5; tick++)
        {
            Double3 place = frames.TreeLocalPose(home).Position;
            Double3 velocity = frames.LinearVelocity(home);
            var end = new DPose(place + new Double3(2_000, 50 * tick, 0) * Step, turn);
            frames.Update();
            frames.BeginNumericStep(Moving(ball, new MotionIntent(new EndPose(end))), Step);

            Double3 chord = (end.Position - place) / Step;
            frames.HomeAcceleration(home, space).Should().Be((chord - velocity) / Step, "the acceleration lands the frame on the plan");
            frames.HomeInstantVelocityOverChord(home, space, Step).Should().Be(Double3.Zero, "an end pose's instant velocity is its chord");
            frames.AdvanceNumericFrames(Step);
            frames.PlaceFollowingFrame(home);
            frames.TreeLocalPose(home).Position.Should().Be(end.Position, "at the sync point the frame takes the plan's place");
            frames.LinearVelocity(home).Should().Be(chord, "and the plan's chord");
        }
    }

    /// <summary>
    /// A plain anchor that dies, or hands off into a bubble in the step, is no body of its following frame at the next
    /// update. The frame then turns into a falling frame that falls on from its place, and the crate in it keeps it.
    /// </summary>
    [Fact]
    public void A_following_frame_whose_plain_anchor_is_no_body_in_it_at_the_update_falls_on_and_keeps_the_bodies_in_it()
    {
        NumericBody[] together = Update(Ball(10, Double3.Zero, new Double3(2_000, 0, 0)), Crate(11, new Double3(0, 2, 0), new Double3(2_000, 0.5, 0)));
        FrameHandle home = together[0].Home;
        together[1].Home.Should().Be(home, "the crate joins the anchor's following frame");
        Double3 velocity = frames.LinearVelocity(home);

        NumericBody crate = Update(together[1])[0];

        frames.Numeric(home).Should().Be(NumericMode.Falling, "the frame lost its anchor");
        frames.Anchors(home, together[0].Entity).Should().BeFalse();
        crate.Home.Should().Be(home, "the crate's group keeps the falling frame");
        moves.Should().BeEmpty("no body changes its system");
        frames.LinearVelocity(home).Should().Be(velocity, "the frame falls on at its velocity");
    }

    /// <summary>
    /// A plain anchor that slows down below 400 m/s no longer needs a following frame. The update moves it into its semantic
    /// frame's own system, the frame loses its anchor first, and the empty frame goes.
    /// </summary>
    [Fact]
    public void A_plain_anchor_that_slows_down_moves_into_its_semantic_frames_own_system_and_its_following_frame_goes()
    {
        FrameHandle home = Update(Ball(10, Double3.Zero, new Double3(2_000, 0, 0)))[0].Home;

        NumericBody ball = Update(Ball(10, new Double3(50, 0, 0), new Double3(300, 0, 0), home))[0];

        ball.Home.Should().Be(space);
        frames.IsAlive(home).Should().BeFalse("the empty frame goes");
        changes.FramesDestroyed.Should().Equal(home);
        moves.Should().Equal(new NumericMove(ball.Entity, home, space));
    }

    /// <summary>
    /// A plain kinematic body that follows, and that lives in the following frame of another anchor, for example after it
    /// sped up there, gets a following frame of its own: no following frame holds two anchors. Its group takes the frame of
    /// the first anchor, and that frame keeps a ghost of the second.
    /// </summary>
    [Fact]
    public void A_plain_kinematic_body_that_follows_in_the_frame_of_another_anchor_gets_a_following_frame_of_its_own()
    {
        NumericBody first = Update(Ball(10, Double3.Zero, new Double3(2_000, 0, 0)))[0];
        FrameHandle home = first.Home;

        NumericBody[] after = Update(first, Ball(11, new Double3(3, 0, 0), new Double3(2_000, 0, 0), home));

        after[0].Home.Should().Be(home, "the first anchor keeps its frame");
        after[1].Home.Should().NotBe(home);
        frames.Numeric(after[1].Home).Should().Be(NumericMode.Following);
        frames.Anchors(after[1].Home, after[1].Entity).Should().BeTrue("the second body anchors a following frame of its own");
        frames.AnchorGhosts.ToArray().Should().Equal(new NumericGhost(after[1].Entity, home));
    }

    /// <summary>
    /// A plain anchor that teleported in the step ends the step away from the origin of its following frame. The frame jumps
    /// to the anchor's place, with the anchor's own record when the frame had no plan. Every frame in it keeps its place and
    /// velocity in space, and the shift goes to physics and to the rows.
    /// </summary>
    [Fact]
    public void The_following_frame_of_a_plain_anchor_jumps_to_it_and_the_frames_in_it_keep_their_places_in_space()
    {
        FrameHandle pod = frames.Create(new Entity(12, 1), space, new DPose(new Double3(0, 2, 0), DQuaternion.Identity), FrameBounds.Sphere(1));
        frames.SetHullMotion(pod, BodyMotion.Dynamic);
        frames.SetTreeLinearVelocity(pod, new Double3(2_000, 0.5, 0));
        NumericBody podHull = Crate(12, new Double3(0, 2, 0), new Double3(2_000, 0.5, 0)) with { OwnedFrame = pod };
        NumericBody[] after = Update(Ball(10, Double3.Zero, new Double3(2_000, 0, 0)), podHull);
        FrameHandle home = after[0].Home;
        frames.TreeParent(pod).Should().Be(home, "the pod's frame hangs in the following frame");
        DPose podInSpace = frames.LocalPose(pod);
        Double3 podVelocity = frames.LinearVelocity(pod);
        Double3 velocity = frames.LinearVelocity(home);
        // The ball teleported 1 km ahead in a step without a motion intent, so it rests there in its home, at rest in space.
        var record = new MotionSample(new DPose(new Double3(1_000, 0, 0), DQuaternion.Identity), -velocity, -velocity, Double3.Zero, Double3.Zero, Double3.Zero);
        changes.Clear();

        frames.JumpFollowingFrame(home, record, changes);

        frames.TreeLocalPose(home).Position.Should().Be(new Double3(1_000, 0, 0));
        frames.LinearVelocity(home).Should().Be(Double3.Zero, "the anchor rests in space");
        (frames.LocalPose(pod).Position - podInSpace.Position).Length.Should().BeLessThan(1e-9, "the pod keeps its place in space");
        (frames.LinearVelocity(pod) - podVelocity).Length.Should().BeLessThan(1e-9, "the pod keeps its velocity in space");
        changes.FramesShifted.Should().Equal(new FrameShift(home, new Double3(1_000, 0, 0), -velocity));
    }

    /// <summary>
    /// The rule of anchors in the update: a static body and a fixed hull are anchors always, and a plain kinematic body only
    /// while it lives in a following frame of its own. A slow kinematic body is infinitely heavy in its group, but it moves
    /// between frames, so it is no anchor.
    /// </summary>
    [Fact]
    public void A_plain_kinematic_body_is_an_anchor_only_while_it_follows_in_a_following_frame_of_its_own()
    {
        var options = new NumericFrameOptions();
        NumericFrameRules.IsAnchor(Ball(10, Double3.Zero, new Double3(2_000, 0, 0)), options).Should().BeTrue("a fast kinematic body follows");
        NumericFrameRules.IsAnchor(Ball(10, new Double3(9e6, 0, 0), Double3.Zero), options).Should().BeTrue("so does a far one");
        NumericFrameRules.IsAnchor(Ball(10, Double3.Zero, new Double3(60, 0, 0)), options).Should().BeFalse("a slow, near kinematic body is no anchor");
        NumericFrameRules.IsAnchor(Crate(11, Double3.Zero, new Double3(2_000, 0, 0)), options).Should().BeFalse("a dynamic body is no anchor");
        NumericFrameRules.IsAnchor(Crate(11, Double3.Zero, Double3.Zero) with { Static = true }, options).Should().BeTrue();
        NumericFrameRules.IsAnchor(Crate(11, Double3.Zero, Double3.Zero) with { Fixed = true }, options).Should().BeTrue();
    }

    /// <summary>
    /// A group with a body that follows takes that body's own following frame, the home that it anchors. A body that
    /// follows in a following frame of another anchor has no frame of its own yet, so its group takes a new one.
    /// </summary>
    [Fact]
    public void A_group_takes_the_following_frame_that_its_first_follower_anchors_and_else_a_new_one()
    {
        var options = new NumericFrameOptions();
        FrameHandle home = Update(Ball(10, Double3.Zero, new Double3(2_000, 0, 0)))[0].Home;
        NumericBody follower = Ball(11, Double3.Zero, new Double3(2_000, 0, 0), home) with { HomeMode = NumericMode.Following };
        NumericBody crate = Crate(12, new Double3(0, 2, 0), new Double3(2_000, 0, 0));

        NumericGroup own = NumericFrameRules.Summarize([crate, follower with { AnchorsHome = true }], [], options);
        NumericGroup other = NumericFrameRules.Summarize([crate, follower], [], options);

        own.Follows.Should().BeTrue();
        own.Frame.Should().Be(home, "the group takes the frame that its follower anchors");
        other.Follows.Should().BeTrue();
        other.Frame.IsNull.Should().BeTrue("the follower anchors no frame, so the group takes a new one");
    }

    /// <summary>
    /// A kinematic body alone needs a following frame by the speeds and distances of a free body, because it never thrusts.
    /// Below them it is a body as any other, so in a semantic frame where a body thrusts, a plain kinematic body at 60 m/s
    /// needs a falling frame by the speeds of a body that thrusts: it can join the group of a character at 40 m/s, which
    /// then falls at 50 m/s.
    /// </summary>
    [Fact]
    public void A_kinematic_body_alone_needs_a_following_frame_by_the_speeds_of_a_free_body_and_else_takes_the_speeds_of_its_frame()
    {
        var options = new NumericFrameOptions();
        NumericFrameRules.BodyMode(Ball(10, Double3.Zero, new Double3(2_000, 0, 0)), thrusts: true, options).Should().Be(NumericMode.Following);
        NumericFrameRules.BodyMode(Ball(10, Double3.Zero, new Double3(60, 0, 0)), thrusts: true, options).Should().Be(NumericMode.Falling,
            "below 1,000 m/s the kinematic body takes the speeds of a body that thrusts");
        NumericFrameRules.BodyMode(Ball(10, Double3.Zero, new Double3(60, 0, 0)), thrusts: false, options).Should().Be(NumericMode.None);
    }
}
