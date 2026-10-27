using AwesomeAssertions;
using Tertius.Frames.Rails;
using Tertius.Testing;

namespace Tertius.Core.Tests;

/// <summary>
/// The places of ghosts in <see cref="FrameTree.UpdateNumericFrames"/> (ticket ter-4psc): everything that a group cannot
/// move counts as infinitely heavy in the merge test, ghosts included. The update of a semantic frame P takes as inputs the
/// places of the ghosts that physics keeps in the frames of P: a body that may stick out of the bubble of a semantic child C
/// before the next update has a ghost in C's tree parent, the home of C's hull, and a body of P's semantic parent, or of a
/// sibling of P, that may touch P's bubble has a ghost in P's own system. A body of P that the merge test links to such a
/// place takes the home where the ghost lives: it joins the group of C's hull, or it stays in P's own system. Only a body
/// that meets the ghost as physics has it counts: a dynamic body or a character, or a static or kinematic body when one of
/// the two is a sensor, and never a frame hull. A frame without a hull never hangs in a numeric frame, so the places of
/// its bodies are fixed in its tree parent's own system.
/// <para>
/// The tests run plain body records on a tree built in the test, at the defaults of <see cref="NumericFrameOptions"/>: a
/// group of free bodies falls from 1,000 m/s, a group with a body that thrusts from 50 m/s, and a slow group rests from
/// 2^23 m (about 8,389 km) out; the lookahead is 2 s and the split margin 10 m. A character thrusts from its first update,
/// so a body that must fall at a few tens of m/s is a character. After every update the tree keeps its invariants.
/// </para>
/// </summary>
public sealed class FrameTreeGhostPlaceTests
{
    /// <summary>
    /// A place 2^24 m (about 16,777 km) from ground's origin: twice the distance at which a slow group gets a resting frame,
    /// and short of the distance at which a numeric frame nests.
    /// </summary>
    private const double Far = 1 << 24;

    private readonly FrameTree frames = new();
    private readonly FrameHandle ground;
    private readonly PhysicsChanges changes = new();
    private List<NumericMove> moves = [];

    public FrameTreeGhostPlaceTests()
    {
        ground = frames.Create(
            frameOwner: new(1, 1),
            parent: FrameHandle.Null,
            localPose: DPose.Identity,
            frameBounds: FrameBounds.Sphere(1e9f)
        );
    }

    private static Double3 X(double x) => new(x, 0, 0);

    private static Double3 Y(double y) => new(0, y, 0);

    /// <summary>
    /// A body of radius 1 m of the frame ground at <paramref name="position"/>, whose place in its home the tree gives now.
    /// A <paramref name="character"/> always thrusts, so its group falls from 50 m/s.
    /// </summary>
    private NumericBody Body(
        uint id,
        Double3 position,
        Double3 velocity = default,
        FrameHandle home = default,
        bool character = false,
        double radius = 1
    )
    {
        FrameHandle physicsHome = home.IsNull ? ground : home;
        return new()
        {
            Entity = new(id, 1),
            Semantic = ground,
            Home = physicsHome,
            Position = position,
            HomePosition = position - frames.HomeOffset(physicsHome, ground),
            Velocity = velocity,
            Radius = radius,
            Mass = 1,
            Character = character,
        };
    }

    /// <summary>
    /// A semantic child of ground of the owner <paramref name="owner"/> at <paramref name="position"/>, with a bubble of
    /// radius <paramref name="bubble"/>, whose hull has <paramref name="motion"/>. A frame with a static hull that the test
    /// leaves out of the update is a room or a parked station.
    /// </summary>
    private FrameHandle Station(
        uint owner,
        Double3 position,
        BodyMotion motion = BodyMotion.Static,
        float bubble = 40,
        DQuaternion rotation = default
    )
    {
        FrameHandle station = frames.Create(
            frameOwner: new(owner, 1),
            parent: ground,
            localPose: new(position, rotation == default ? DQuaternion.Identity : rotation),
            frameBounds: FrameBounds.Sphere(bubble)
        );
        frames.SetHullMotion(station, motion);
        return station;
    }

    /// <summary>A body of radius 1 m of the frame <paramref name="frame"/>, at <paramref name="position"/> in its own system.</summary>
    private static NumericBody BodyOf(FrameHandle frame, uint id, Double3 position, Double3 velocity = default) =>
        new()
        {
            Entity = new(id, 1),
            Semantic = frame,
            Home = frame,
            Position = position,
            HomePosition = position,
            Velocity = velocity,
            Radius = 1,
            Mass = 1,
        };

    /// <summary>The static hull of a parked station: a static body that stays in ground's own system.</summary>
    private NumericBody ParkedHull(FrameHandle station, double radius = 5) =>
        Body(id: station.Entity.Index, position: frames.LocalPose(station).Position, radius: radius) with
        {
            Entity = station.Entity,
            Static = true,
            Fixed = true,
            OwnedFrame = station,
        };

    /// <summary>
    /// Runs one update over <paramref name="ticks"/> ticks at 60 ticks per second and returns the bodies with their new homes
    /// and their places in them.
    /// </summary>
    private NumericBody[] Update(int ticks, params NumericBody[] bodies) =>
        Run(frames: frames, changes: changes, ticks: ticks, bodies: bodies, moves: out moves);

    private static NumericBody[] Run(
        FrameTree frames,
        PhysicsChanges changes,
        int ticks,
        NumericBody[] bodies,
        out List<NumericMove> moves
    )
    {
        changes.Clear();
        var handedMoves = new List<NumericMove>();
        frames.UpdateNumericFrames(
            bodies: bodies,
            ticks: ticks,
            tickRate: 60,
            changes: changes,
            applyMoves: handed =>
            {
                foreach (NumericMove move in handed)
                    frames
                        .IsAlive(move.From)
                        .Should()
                        .BeTrue("physics converts each move through the frame that the body leaves");
                handedMoves.AddRange(handed);
            }
        );
        frames.AssertInvariants();
        moves = handedMoves;
        var homes = handedMoves.ToDictionary(move => move.Entity, move => move.To);
        foreach (NumericMove move in handedMoves)
        {
            NumericBody body = bodies.Single(b => b.Entity == move.Entity && b.Semantic == frames.SemanticOf(move.To));
            move.From.Should().Be(body.Home);
            frames.IsAlive(move.To).Should().BeTrue();
        }

        return
        [
            .. bodies.Select(body =>
            {
                FrameHandle home =
                    homes.TryGetValue(body.Entity, out FrameHandle to) && frames.SemanticOf(to) == body.Semantic
                        ? to
                        : body.Home;
                return body with { Home = home, HomePosition = body.Position - frames.HomeOffset(home, body.Semantic) };
            }),
        ];
    }

    // ---- Bodies that stick out of a semantic child ------------------------------------------------------------------

    [Fact]
    public void A_fast_body_near_a_crate_that_may_stick_out_of_a_child_bubble_whose_hull_falls_joins_the_hulls_falling_frame()
    {
        // The ship falls at 60 m/s with its hull. Its crate sticks out 0.5 m at 1,199.5 m in ground, and the rock 15.5 m
        // ahead of it flies at 50 m/s, so the crate comes at it at 10 m/s: the merge test reaches 2 m plus 2 s of that. The
        // rock is 215 m from the hull, and alone it falls at 50 m/s in a frame of its own.
        FrameHandle ship = Station(owner: 20, position: X(1_000), motion: BodyMotion.Dynamic, bubble: 200);
        frames.SetTreeLinearVelocity(ship, X(60));
        NumericBody hull = Body(id: 20, position: X(1_000), velocity: X(60), character: true, radius: 2) with
        {
            OwnedFrame = ship,
        };
        NumericBody rock = Body(id: 10, position: X(1_215), velocity: X(50), character: true);

        NumericBody[] after = Update(6, hull, rock, BodyOf(frame: ship, id: 11, position: X(199.5)));

        frames.Numeric(after[0].Home).Should().Be(NumericMode.Falling);
        after[1]
            .Home.Should()
            .Be(after[0].Home, "the crate's ghost lives in the ship's tree parent, the home of the hull");
        frames.TreeParent(ship).Should().Be(after[0].Home);
    }

    [Fact]
    public void A_fast_body_near_a_crate_that_may_stick_out_of_a_child_bubble_whose_hull_is_parked_stays_in_the_semantic_frames_own_system()
    {
        // The rock comes at the crate at 60 m/s from 120 m: 2 m plus 2 s of 60 m/s reach it, but not the hull, 160 m away
        // with a radius of 5 m.
        FrameHandle station = Station(owner: 2, position: X(1_000));
        NumericBody rock = Body(id: 10, position: new(1_000, 160, 0), velocity: Y(-60), character: true);

        NumericBody[] after = Update(6, ParkedHull(station), rock, BodyOf(frame: station, id: 11, position: Y(40)));

        after[1].Home.Should().Be(ground, "the crate's ghost lives in ground's own system with the parked hull");
        frames.NumericCount.Should().Be(0);
    }

    [Fact]
    public void A_fast_body_near_a_crate_that_may_stick_out_of_a_bubble_without_a_hull_stays_in_the_semantic_frames_own_system()
    {
        FrameHandle station = Station(owner: 2, position: X(1_000));
        NumericBody rock = Body(id: 10, position: new(1_000, 160, 0), velocity: Y(-60), character: true);

        NumericBody[] after = Update(6, rock, BodyOf(frame: station, id: 11, position: Y(40)));

        after[0].Home.Should().Be(ground);
        frames.NumericCount.Should().Be(0);
    }

    [Fact]
    public void A_fast_body_far_from_every_place_of_a_ghost_keeps_its_own_frame()
    {
        // 360 m from the crate: beyond 2 m plus 2 s of 60 m/s and the split margin.
        FrameHandle station = Station(owner: 2, position: X(1_000));
        NumericBody rock = Body(id: 10, position: new(1_000, 400, 0), velocity: Y(-60), character: true);

        NumericBody[] after = Update(6, rock, BodyOf(frame: station, id: 11, position: Y(40)));

        frames.Numeric(after[0].Home).Should().Be(NumericMode.Falling);
    }

    [Fact]
    public void A_static_body_that_sticks_out_of_a_child_bubble_leaves_no_place()
    {
        // A static body that is not a hull has no ghost in a bubble's tree parent.
        FrameHandle station = Station(owner: 2, position: X(1_000));
        NumericBody rock = Body(id: 10, position: new(1_000, 160, 0), velocity: Y(-60), character: true);

        NumericBody[] after = Update(6, rock, BodyOf(frame: station, id: 11, position: Y(40)) with { Static = true });

        frames.Numeric(after[0].Home).Should().Be(NumericMode.Falling);
    }

    [Fact]
    public void A_hull_on_rails_far_outside_its_frames_bubble_leaves_its_place_where_it_lies()
    {
        // A hull that moves its frame never leaves the bubble, so it sticks out by as far as it lies: 20 m outside
        // the station's bubble, with a radius of 5 m. The slow body far out touches it, and alone it would rest in a frame.
        FrameHandle station = Station(owner: 2, position: X(Far));
        NumericBody radar = BodyOf(frame: station, id: 11, position: Y(60)) with
        {
            Radius = 5,
            Fixed = true,
            Kinematic = true,
        };

        NumericBody[] after = Update(6, Body(id: 10, position: new(Far, 66, 0)), radar);

        after[0].Home.Should().Be(ground);
    }

    [Fact]
    public void The_place_of_a_body_that_sticks_out_of_a_turned_bubble_turns_with_the_bubble()
    {
        // The station is turned a quarter turn about Z, so the crate at 40 m on its Y axis lies 40 m along ground's -X. The
        // slow body far out touches it there, 58 m from where the crate would be without the turn.
        FrameHandle station = Station(
            owner: 2,
            position: X(Far),
            rotation: DQuaternion.FromAxisAngle(Double3.UnitZ, Math.PI / 2)
        );

        NumericBody[] after = Update(
            6,
            Body(id: 10, position: X(Far - 42)),
            BodyOf(frame: station, id: 11, position: Y(40))
        );

        after[0].Home.Should().Be(ground);
    }

    [Fact]
    public void The_place_of_a_body_that_sticks_out_of_a_spinning_bubble_moves_with_the_spin()
    {
        // The station turns at 1 rad/s about Z, so the crate at 40 m on its Y axis moves at 40 m/s along -X. The slow body
        // 50 m ahead of it is within 2 m plus 2 s of 40 m/s.
        FrameHandle station = Station(owner: 2, position: X(Far));
        frames.SetAngularVelocity(station, new(0, 0, 1));

        NumericBody[] after = Update(
            6,
            Body(id: 10, position: new(Far - 50, 40, 0)),
            BodyOf(frame: station, id: 11, position: Y(40))
        );

        after[0].Home.Should().Be(ground);
    }

    [Fact]
    public void A_fast_body_that_has_passed_the_place_of_a_crate_gets_its_own_frame()
    {
        FrameHandle station = Station(owner: 2, position: X(100));
        NumericBody[] near = Update(
            6,
            Body(id: 10, position: X(0), velocity: X(60), character: true),
            BodyOf(frame: station, id: 11, position: Y(40))
        );
        near[0].Home.Should().Be(ground);

        // 204 m past the crate: beyond 2 m plus 2 s of 60 m/s and the split margin.
        NumericBody[] after = Update(
            6,
            Body(id: 10, position: X(300), velocity: X(60), character: true),
            BodyOf(frame: station, id: 11, position: Y(40))
        );

        frames.Numeric(after[0].Home).Should().Be(NumericMode.Falling);
    }

    [Fact]
    public void A_body_that_flies_beside_a_crate_that_sticks_out_of_a_falling_ship_shares_the_ships_falling_frame()
    {
        // Both rocks fly with the ship at 60 m/s, so the merge test reaches only the radii, and the split margin in one
        // system. The first rock is 3 m from the crate at 1,200 m and 203 m from the hull; the second is 50 m from the crate.
        FrameHandle ship = Station(owner: 20, position: X(1_000), motion: BodyMotion.Dynamic, bubble: 200);
        frames.SetTreeLinearVelocity(ship, X(60));
        NumericBody hull = Body(id: 20, position: X(1_000), velocity: X(60), character: true, radius: 2) with
        {
            OwnedFrame = ship,
        };

        NumericBody[] after = Update(
            6,
            hull,
            Body(id: 10, position: X(1_203), velocity: X(60), character: true),
            Body(id: 12, position: X(1_150), velocity: X(60), character: true),
            BodyOf(frame: ship, id: 11, position: X(200))
        );

        frames.Numeric(after[0].Home).Should().Be(NumericMode.Falling);
        after[1].Home.Should().Be(after[0].Home);
        after[2].Home.Should().NotBe(after[0].Home);
        frames.Numeric(after[2].Home).Should().Be(NumericMode.Falling);
    }

    [Fact]
    public void A_body_beside_a_crate_that_sticks_out_of_a_falling_ship_stays_in_the_ships_falling_frame_up_to_the_split_margin()
    {
        // The ghost of the crate lives in the ship's tree parent, the falling frame of the hull, with the rock. 8 m from the
        // crate, the rock is beyond the reach of 2 m but within the split margin, so it stays, although a whole split
        // cooldown passes.
        FrameHandle ship = Station(owner: 20, position: X(1_000), motion: BodyMotion.Dynamic, bubble: 200);
        frames.SetTreeLinearVelocity(ship, X(60));
        NumericBody[] before = Update(
            6,
            Body(id: 20, position: X(1_000), velocity: X(60), character: true, radius: 2) with
            {
                OwnedFrame = ship,
            },
            Body(id: 10, position: X(1_203), velocity: X(60), character: true),
            BodyOf(frame: ship, id: 11, position: X(200))
        );
        FrameHandle pod = before[0].Home;
        before[1].Home.Should().Be(pod);

        Update(
            60,
            Body(id: 20, position: X(1_000), velocity: X(60), home: pod, character: true, radius: 2) with
            {
                OwnedFrame = ship,
            },
            Body(id: 10, position: X(1_208), velocity: X(60), home: pod, character: true),
            BodyOf(frame: ship, id: 11, position: X(200))
        );

        moves.Should().BeEmpty();
    }

    [Fact]
    public void A_body_in_a_falling_frame_of_its_own_that_nears_a_crate_of_a_parked_station_moves_into_the_semantic_frames_own_system()
    {
        FrameHandle station = Station(owner: 2, position: X(1_000));
        NumericBody[] before = Update(
            6,
            Body(id: 10, position: new(1_000, 1_000, 0), velocity: Y(-60), character: true),
            BodyOf(frame: station, id: 11, position: Y(40))
        );
        FrameHandle pod = before[0].Home;
        frames.Numeric(pod).Should().Be(NumericMode.Falling);

        // 100 m from the crate: within 2 m plus 2 s of 60 m/s.
        NumericBody[] after = Update(
            6,
            Body(id: 10, position: new(1_000, 140, 0), velocity: Y(-60), home: pod, character: true),
            BodyOf(frame: station, id: 11, position: Y(40))
        );

        after[0].Home.Should().Be(ground);
    }

    [Fact]
    public void The_place_of_a_crate_that_sticks_out_of_a_ship_moves_with_the_ship()
    {
        // The still ship's crate lies 50 m from the rock that comes at it at 60 m/s: the rock joins the hull's group, which
        // is too slow for a frame. When the ship flies with the rock, the rock is 50 m from the crate at no relative speed.
        FrameHandle ship = Station(owner: 20, position: X(1_000), motion: BodyMotion.Dynamic, bubble: 200);
        NumericBody[] still = Update(
            6,
            Body(id: 20, position: X(1_000), radius: 2) with
            {
                OwnedFrame = ship,
            },
            Body(id: 10, position: X(1_250), velocity: X(60), character: true),
            BodyOf(frame: ship, id: 11, position: X(200))
        );
        still[1].Home.Should().Be(ground, "the rock shares the group of the still hull");

        frames.SetTreeLinearVelocity(ship, X(60));
        NumericBody[] after = Update(
            6,
            Body(id: 20, position: X(1_000), velocity: X(60), character: true, radius: 2) with
            {
                OwnedFrame = ship,
            },
            Body(id: 10, position: X(1_250), velocity: X(60), character: true),
            BodyOf(frame: ship, id: 11, position: X(200))
        );

        after[1].Home.Should().NotBe(after[0].Home);
        frames.Numeric(after[1].Home).Should().Be(NumericMode.Falling);
    }

    /// <summary>
    /// A station 1e13 m out in ground, and a rock that thrusts and flies at 60 m/s 120 m or 140 m from the station's origin
    /// along X, whose place in ground the input gives 20 m off the place in its home. The crate sticks out 40 m along Y. The
    /// reach is 2 m, 2 s of 60 m/s and the split margin, 132 m: the crate is 126.5 m from the rock at 120 m and 145.6 m from
    /// the rock at 140 m.
    /// </summary>
    [Theory]
    [InlineData(120, 140, true)]
    [InlineData(140, 120, false)]
    public void Far_out_the_merge_test_measures_a_body_from_its_place_in_its_home_to_the_place_of_a_ghost(
        double inHome,
        double inGround,
        bool linked
    )
    {
        FrameHandle station = Station(owner: 2, position: X(1e13));
        NumericBody rock = Body(id: 10, position: X(1e13 + inGround), velocity: X(60), character: true) with
        {
            HomePosition = X(1e13 + inHome),
        };

        NumericBody[] after = Update(6, rock, BodyOf(frame: station, id: 11, position: Y(40)));

        if (linked)
            after[0].Home.Should().Be(ground);
        else
            frames.Numeric(after[0].Home).Should().Be(NumericMode.Falling);
    }

    // ---- Which bodies a place links -----------------------------------------------------------------------------------

    [Fact]
    public void A_static_body_that_is_not_a_sensor_is_not_linked_by_a_place()
    {
        // A ghost is kinematic, and Jolt makes no contact between a kinematic body and a static one unless one is a sensor.
        // The static body far out lies 5 m from the crate, and alone it rests in a frame.
        FrameHandle station = Station(owner: 2, position: X(Far));

        NumericBody[] after = Update(
            6,
            Body(id: 10, position: new(Far, 45, 0)) with
            {
                Static = true,
            },
            BodyOf(frame: station, id: 11, position: Y(40))
        );

        frames.Numeric(after[0].Home).Should().Be(NumericMode.Resting);
    }

    [Fact]
    public void A_kinematic_body_that_is_not_a_sensor_is_not_linked_by_a_place()
    {
        // The kinematic body far out lies 5 m from the crate, and alone it lives in a following frame of its own.
        FrameHandle station = Station(owner: 2, position: X(Far));

        NumericBody[] after = Update(
            6,
            Body(id: 10, position: new(Far, 45, 0)) with
            {
                Kinematic = true,
            },
            BodyOf(frame: station, id: 11, position: Y(40))
        );

        frames.Numeric(after[0].Home).Should().Be(NumericMode.Following);
        frames.AnchorGhosts.ToArray().Should().BeEmpty("nothing links the body to ground's own system");
    }

    /// <summary>
    /// The place links the sensor, so its group stays in ground's own system, where the ghost lives. A far kinematic body
    /// lives in a following frame of its own, which follows its motion intents, so the place counts as an anchor at rest in
    /// that system, and the system keeps a ghost of the body, as for a static body there.
    /// </summary>
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void A_static_or_kinematic_sensor_is_linked_by_a_place(bool isStatic)
    {
        FrameHandle station = Station(owner: 2, position: X(Far));
        NumericBody sensor = Body(id: 10, position: new(Far, 45, 0)) with
        {
            Static = isStatic,
            Kinematic = !isStatic,
            Sensor = true,
        };

        NumericBody[] after = Update(6, sensor, BodyOf(frame: station, id: 11, position: Y(40)));

        if (isStatic)
            after[0].Home.Should().Be(ground);
        else
            KeepsItsFollowingFrameAndAGhostInGround(after[0]);
    }

    /// <summary>
    /// Checks that <paramref name="body"/>, a far kinematic body that a fixed place links, anchors a following frame of its
    /// own, and that ground's own system keeps a ghost of it.
    /// </summary>
    private void KeepsItsFollowingFrameAndAGhostInGround(in NumericBody body)
    {
        frames
            .Anchors(body.Home, body.Entity)
            .Should()
            .BeTrue("a far kinematic body lives in a following frame of its own");
        frames
            .AnchorGhosts.ToArray()
            .Should()
            .Equal([new(body.Entity, ground)], "the place is fixed in ground's own system");
    }

    [Fact]
    public void A_falling_ships_hull_near_a_crate_that_sticks_out_of_a_station_is_not_linked_to_the_crates_place()
    {
        // The review's scene: the ship's hull flies at 60 m/s 35 m beside the crate, within the merge test's reach of 2 s of
        // its speed. A ghost has the ghost role, and the mask of a hull names only the static and hull roles, so the hull
        // never meets the crate's ghost and keeps falling in its numeric frame.
        FrameHandle station = Station(owner: 2, position: X(1_000));
        FrameHandle ship = Station(owner: 20, position: new(1_000, 75, 0), motion: BodyMotion.Dynamic, bubble: 30);
        frames.SetTreeLinearVelocity(ship, X(-60));
        NumericBody hull = Body(id: 20, position: new(1_000, 75, 0), velocity: X(-60), character: true, radius: 2) with
        {
            OwnedFrame = ship,
        };

        NumericBody[] after = Update(6, hull, BodyOf(frame: station, id: 11, position: Y(40)));

        frames.Numeric(after[0].Home).Should().Be(NumericMode.Falling);
    }

    [Fact]
    public void A_sensor_near_the_place_of_a_parked_hull_is_not_linked_to_it()
    {
        // The depot's static hull reaches the station's bubble as far as its own bubble, so it has a ghost in the station's
        // own system. That ghost meets only what the static hull meets, a dynamic body, so a kinematic sensor at 1,000 m/s
        // in the station does not detect it, and alone it lives in a following frame of its own in the station.
        FrameHandle station = Station(owner: 2, position: X(1_000));
        FrameHandle depot = Station(owner: 3, position: new(1_000, 100, 0), bubble: 70);
        NumericBody sensor = BodyOf(frame: station, id: 12, position: Double3.Zero, velocity: X(1_000)) with
        {
            Kinematic = true,
            Sensor = true,
        };

        NumericBody[] after = Update(6, ParkedHull(depot), sensor);

        frames.Numeric(after[1].Home).Should().Be(NumericMode.Following);
        frames.AnchorGhosts.ToArray().Should().BeEmpty("nothing links the sensor to the station's own system");
    }

    [Fact]
    public void A_kinematic_body_near_a_sensor_that_sticks_out_of_a_child_bubble_is_linked_to_its_place()
    {
        // Jolt makes a contact between a kinematic body and a sensor. So the kinematic body far out, 5 m from the sensor's
        // place, keeps its following frame, and the system of ground, where the sensor's ghost lives, keeps a ghost of it.
        FrameHandle station = Station(owner: 2, position: X(Far));

        NumericBody[] after = Update(
            6,
            Body(id: 10, position: new(Far, 45, 0)) with
            {
                Kinematic = true,
            },
            BodyOf(frame: station, id: 11, position: Y(40)) with
            {
                Sensor = true,
            }
        );

        KeepsItsFollowingFrameAndAGhostInGround(after[0]);
    }

    [Fact]
    public void The_update_finds_the_fixed_ghosts_that_a_body_may_touch_itself()
    {
        // The tree fills in the field, as it fills in the mode of the home, so a value in the input does not count.
        NumericBody rock = Body(id: 10, position: X(1_000), velocity: X(60), character: true) with
        {
            MayTouchFixedGhost = true,
        };

        NumericBody[] after = Update(6, rock);

        frames.Numeric(after[0].Home).Should().Be(NumericMode.Falling);
    }

    // ---- Places in a frame's own system from outside it (ter-tenw) ----------------------------------------------------

    [Fact]
    public void Bodies_of_a_child_frame_near_the_place_of_a_parent_body_that_may_touch_the_childs_bubble_stay_in_the_childs_own_system()
    {
        // The rock of ground touches the station's bubble from outside, so it has a ghost in the station's own system. The
        // body in the station flies at 60 m/s 11 m from it, and alone it would fall in a frame of the station.
        FrameHandle station = Station(owner: 2, position: X(1_000));
        NumericBody inside = BodyOf(frame: station, id: 12, position: Y(30), velocity: X(60)) with { Character = true };

        NumericBody[] after = Update(6, Body(id: 10, position: new(1_000, 41, 0)), inside);

        after[1].Home.Should().Be(station);
        frames.NumericCount.Should().Be(0);
    }

    [Fact]
    public void The_hull_of_a_frame_in_the_parent_reaches_a_childs_bubble_as_far_as_its_own_bubble()
    {
        // The ship's hull lies 110 m outside the station's bubble, but the ship's bubble of 120 m touches it, so the hull
        // has a ghost in the station's own system. The body in the station comes at it at 60 m/s from 115 m.
        FrameHandle station = Station(owner: 2, position: X(1_000));
        FrameHandle ship = Station(owner: 20, position: new(1_000, 150, 0), motion: BodyMotion.Dynamic, bubble: 120);
        NumericBody hull = Body(id: 20, position: new(1_000, 150, 0), radius: 2) with { OwnedFrame = ship };
        NumericBody inside = BodyOf(frame: station, id: 12, position: Y(35), velocity: Y(60)) with { Character = true };

        NumericBody[] after = Update(6, hull, inside);

        after[1].Home.Should().Be(station);
    }

    [Fact]
    public void A_body_of_the_parent_that_a_moving_child_comes_at_leaves_a_place_in_the_childs_own_system()
    {
        // The rock lies still 15 m outside the station's bubble, beyond the split margin, and the station comes at it at
        // 10 m/s, so the rock can touch the bubble within 2 s. The body in the station flies at 60 m/s 17 m from the rock.
        FrameHandle station = Station(owner: 2, position: X(1_000), motion: BodyMotion.Dynamic);
        frames.SetTreeLinearVelocity(station, Y(10));
        NumericBody inside = BodyOf(frame: station, id: 12, position: Y(38), velocity: X(60)) with { Character = true };

        NumericBody[] after = Update(6, Body(id: 10, position: new(1_000, 55, 0)), inside);

        after[1].Home.Should().Be(station);
    }

    [Fact]
    public void A_body_of_the_parent_beside_a_spinning_child_leaves_a_place_in_the_childs_own_system()
    {
        // The station turns at 0.2 rad/s, so in its axes the rock, still in ground 21 m outside the bubble, moves at
        // 12.2 m/s, and 2 s of that reach the bubble. The body in the station flies at 60 m/s 23 m from the rock.
        FrameHandle station = Station(owner: 2, position: X(1_000));
        frames.SetAngularVelocity(station, new(0, 0, 0.2f));
        NumericBody inside = BodyOf(frame: station, id: 12, position: Y(38), velocity: X(60)) with { Character = true };

        NumericBody[] after = Update(6, Body(id: 10, position: new(1_000, 61, 0)), inside);

        after[1].Home.Should().Be(station);
    }

    [Fact]
    public void The_place_of_a_parent_body_in_a_turned_child_lies_where_the_body_is_in_the_childs_axes()
    {
        // The station is turned a quarter turn about Z, so the rock 1,010 m along ground's -X from its origin lies 1,010 m
        // along its Y axis, where its radius of 10 m touches the bubble of 1,000 m. The body in the station flies at 60 m/s
        // 25 m from it, and 1,410 m from where the rock would be without the turn.
        FrameHandle station = Station(
            owner: 2,
            position: X(10_000),
            bubble: 1_000,
            rotation: DQuaternion.FromAxisAngle(Double3.UnitZ, Math.PI / 2)
        );
        NumericBody inside = BodyOf(frame: station, id: 12, position: Y(985), velocity: X(60)) with
        {
            Character = true,
        };

        NumericBody[] after = Update(6, Body(id: 10, position: X(10_000 - 1_010), radius: 10), inside);

        after[1].Home.Should().Be(station);
    }

    [Fact]
    public void A_static_body_of_the_parent_that_touches_a_childs_bubble_leaves_a_place_in_it()
    {
        // A static body has a ghost in each child bubble that it reaches, as a hull does (ter-xxmj). The body in the station
        // flies at 60 m/s 11 m from it, and alone it would fall in a frame of the station.
        FrameHandle station = Station(owner: 2, position: X(1_000));
        NumericBody inside = BodyOf(frame: station, id: 12, position: Y(30), velocity: X(60)) with { Character = true };

        NumericBody[] after = Update(6, Body(id: 10, position: new(1_000, 41, 0)) with { Static = true }, inside);

        after[1].Home.Should().Be(station);
        frames.NumericCount.Should().Be(0);
    }

    [Fact]
    public void The_interior_of_the_parents_owner_leaves_no_place_in_a_child()
    {
        // The interior of ground's owner is a static body of radius 30 m at ground's origin, which reaches the station's
        // bubble. An interior has no ghost in a child frame yet, so the body in the station, which flies at 60 m/s 1 m from
        // where its place would be, falls in a frame of the station.
        FrameHandle station = Station(owner: 2, position: X(60));
        NumericBody interior = Body(id: 1, position: Double3.Zero, radius: 30) with
        {
            Entity = ground.Entity,
            Static = true,
            Fixed = true,
        };
        NumericBody inside = BodyOf(frame: station, id: 12, position: X(-29), velocity: Y(60)) with
        {
            Character = true,
        };

        NumericBody[] after = Update(6, interior, inside);

        frames.Numeric(after[1].Home).Should().Be(NumericMode.Falling);
    }

    [Theory]
    [InlineData(BodyMotion.Static)]
    [InlineData(BodyMotion.Kinematic)]
    public void A_static_or_kinematic_body_of_the_parent_that_reaches_the_childs_content_beyond_its_bubble_leaves_a_place_in_it(
        BodyMotion motion
    )
    {
        // The body in the station flies at 60 m/s 1 m inside the bubble, so within 2 s it can stick out by 120 m: the
        // station's content reaches that far beyond the bubble. The rock of ground rests 15 m outside the bubble, beyond its
        // own reach of 1 m and the split margin, but within the content. The stick-out ghost of the body in the station is
        // kinematic and meets no static or kinematic body, so the rock reaches the content (fork F2) and has a ghost in the
        // station's own system, 17 m from the body.
        FrameHandle station = Station(owner: 2, position: X(1_000));
        NumericBody inside = BodyOf(frame: station, id: 12, position: Y(39), velocity: X(60)) with { Character = true };
        NumericBody rock = Body(id: 10, position: new(1_000, 56, 0)) with
        {
            Static = motion == BodyMotion.Static,
            Kinematic = motion == BodyMotion.Kinematic,
        };

        NumericBody[] after = Update(6, rock, inside);

        after[1].Home.Should().Be(station);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void A_dynamic_body_or_character_of_the_parent_that_reaches_only_the_childs_content_leaves_no_place_in_it(
        bool character
    )
    {
        // The scene of the static rock, with a rock that can be pushed. The stick-out ghost of the body in the station meets
        // it, and a ghost of the rock in the station would make a mirror pair of two kinematic ghosts, in which each real body
        // meets the other as an immovable body. So the rock reaches only the bubble (fork F2), and the body in the station
        // falls in a frame of its own.
        FrameHandle station = Station(owner: 2, position: X(1_000));
        NumericBody inside = BodyOf(frame: station, id: 12, position: Y(39), velocity: X(60)) with { Character = true };

        NumericBody[] after = Update(6, Body(id: 10, position: new(1_000, 56, 0), character: character), inside);

        frames.Numeric(after[1].Home).Should().Be(NumericMode.Falling);
    }

    [Fact]
    public void A_dynamic_hull_of_the_parent_that_reaches_the_childs_content_beyond_its_own_bubble_leaves_a_place_in_it()
    {
        // The scene of the static rock, with the dynamic hull of a pod in place of the rock. The pod's bubble of 1 m is no
        // larger than the hull's sphere, so the hull reaches 1 m. A stick-out ghost has the ghost role, which meets no hull,
        // so the hull reaches the content although it is dynamic (fork F2).
        FrameHandle station = Station(owner: 2, position: X(1_000));
        var at = new Double3(1_000, 56, 0);
        FrameHandle pod = frames.Create(
            frameOwner: new(10, 1),
            parent: ground,
            localPose: new(at, DQuaternion.Identity),
            frameBounds: FrameBounds.Sphere(1)
        );
        frames.SetHullMotion(pod, BodyMotion.Dynamic);
        NumericBody inside = BodyOf(frame: station, id: 12, position: Y(39), velocity: X(60)) with { Character = true };

        NumericBody[] after = Update(6, Body(id: 10, position: at) with { OwnedFrame = pod }, inside);

        after[1].Home.Should().Be(station);
    }

    [Fact]
    public void A_falling_hull_in_a_child_is_linked_to_the_place_of_a_parked_hull_of_the_parent()
    {
        // The depot's static hull reaches the station's bubble as far as the depot's bubble, so it has a ghost in the
        // station's own system with the static role, which the mask of a hull names (fork F1). The shuttle's hull falls at
        // 60 m/s in the station, 70 m from the depot's hull, and alone it would fall in a frame of the station.
        FrameHandle station = Station(owner: 2, position: X(1_000), bubble: 200);
        FrameHandle depot = Station(owner: 3, position: new(1_000, 260, 0), bubble: 70);
        FrameHandle shuttle = frames.Create(
            frameOwner: new(30, 1),
            parent: station,
            localPose: new(Y(190), DQuaternion.Identity),
            frameBounds: FrameBounds.Sphere(10)
        );
        frames.SetHullMotion(shuttle, BodyMotion.Dynamic);
        NumericBody hull = BodyOf(frame: station, id: 30, position: Y(190), velocity: X(60)) with
        {
            Entity = shuttle.Entity,
            OwnedFrame = shuttle,
            Character = true,
            Radius = 2,
        };

        NumericBody[] after = Update(6, ParkedHull(depot, radius: 30), hull);

        after[1].Home.Should().Be(station);
    }

    [Fact]
    public void A_falling_hull_in_a_child_is_linked_to_the_place_of_a_static_body_of_the_parent()
    {
        // The scene of the parked depot, with a static rock of radius 30 m in place of the depot's hull. The rock reaches the
        // station's bubble, so it has a ghost in the station's own system with its own static role, which the mask of a hull
        // names (fork F1).
        FrameHandle station = Station(owner: 2, position: X(1_000), bubble: 200);
        FrameHandle shuttle = frames.Create(
            frameOwner: new(30, 1),
            parent: station,
            localPose: new(Y(190), DQuaternion.Identity),
            frameBounds: FrameBounds.Sphere(10)
        );
        frames.SetHullMotion(shuttle, BodyMotion.Dynamic);
        NumericBody hull = BodyOf(frame: station, id: 30, position: Y(190), velocity: X(60)) with
        {
            Entity = shuttle.Entity,
            OwnedFrame = shuttle,
            Character = true,
            Radius = 2,
        };

        NumericBody[] after = Update(
            6,
            Body(id: 3, position: new(1_000, 260, 0), radius: 30) with
            {
                Static = true,
            },
            hull
        );

        after[1].Home.Should().Be(station);
    }

    [Fact]
    public void The_hull_of_a_frame_leaves_no_place_in_the_frames_own_system()
    {
        // The hull lies at the ship's origin, inside its bubble, and the body in the ship flies at 60 m/s 5 m from it. The
        // ship has no interior here, so nothing keeps the body in the ship's own system.
        FrameHandle ship = Station(owner: 20, position: X(1_000), motion: BodyMotion.Dynamic, bubble: 200);
        NumericBody hull = Body(id: 20, position: X(1_000), radius: 2) with { OwnedFrame = ship };
        NumericBody inside = BodyOf(frame: ship, id: 12, position: Y(5), velocity: X(60)) with { Character = true };

        NumericBody[] after = Update(6, hull, inside);

        frames.Numeric(after[1].Home).Should().Be(NumericMode.Falling);
    }

    [Fact]
    public void Bodies_of_a_child_frame_near_the_place_of_a_siblings_body_that_may_stick_out_and_touch_the_bubble_stay_in_the_childs_own_system()
    {
        // The depot beside the station touches it at 1,040 m in ground. The crate of the depot sticks out of it there and
        // touches the station's bubble, so it has a ghost in the station's own system. The body in the station flies at
        // 60 m/s 10 m from it.
        FrameHandle station = Station(owner: 2, position: X(1_000));
        FrameHandle depot = Station(owner: 3, position: X(1_080));
        NumericBody inside = BodyOf(frame: station, id: 12, position: X(30), velocity: Y(60)) with { Character = true };

        NumericBody[] after = Update(6, BodyOf(frame: depot, id: 11, position: X(-40)), inside);

        after[1].Home.Should().Be(station);
        frames.NumericCount.Should().Be(0);
    }

    [Fact]
    public void A_body_that_sticks_out_of_a_sibling_that_comes_at_the_frame_leaves_a_place_in_the_frames_own_system()
    {
        // The depot comes at the station at 10 m/s. Its crate sticks out of it 20 m outside the station's bubble, so it may
        // touch that bubble within 2 s. The body in the station flies at 60 m/s 22 m from it.
        FrameHandle station = Station(owner: 2, position: X(1_000));
        FrameHandle depot = Station(owner: 3, position: X(1_100), motion: BodyMotion.Dynamic);
        frames.SetTreeLinearVelocity(depot, X(-10));
        NumericBody inside = BodyOf(frame: station, id: 12, position: X(38), velocity: Y(60)) with { Character = true };

        NumericBody[] after = Update(6, BodyOf(frame: depot, id: 11, position: X(-40)), inside);

        after[1].Home.Should().Be(station);
    }

    [Fact]
    public void A_body_that_comes_at_the_frame_as_it_sticks_out_of_a_sibling_leaves_a_place_in_the_frames_own_system()
    {
        // The depot's crate sticks out of the depot 20 m outside the station's bubble and comes at it at 10 m/s, so it can
        // touch the bubble within 2 s. The body in the station flies at 60 m/s 22 m from it.
        FrameHandle station = Station(owner: 2, position: X(1_000));
        FrameHandle depot = Station(owner: 3, position: X(1_100));
        NumericBody inside = BodyOf(frame: station, id: 12, position: X(38), velocity: Y(60)) with { Character = true };

        NumericBody[] after = Update(6, BodyOf(frame: depot, id: 11, position: X(-40), velocity: X(-10)), inside);

        after[1].Home.Should().Be(station);
    }

    [Fact]
    public void A_body_that_sticks_out_of_a_spinning_sibling_leaves_a_place_in_the_frames_own_system()
    {
        // The depot turns at 0.25 rad/s, so its crate, which sticks out of it 20 m outside the station's bubble, moves at
        // 10 m/s, and 2 s of that reach the bubble. The body in the station flies at 60 m/s 22 m from it.
        FrameHandle station = Station(owner: 2, position: X(1_000));
        FrameHandle depot = Station(owner: 3, position: X(1_100));
        frames.SetAngularVelocity(depot, new(0, 0, 0.25f));
        NumericBody inside = BodyOf(frame: station, id: 12, position: X(38), velocity: Y(60)) with { Character = true };

        NumericBody[] after = Update(6, BodyOf(frame: depot, id: 11, position: X(-40)), inside);

        after[1].Home.Should().Be(station);
    }

    [Fact]
    public void A_body_that_sticks_out_of_a_sibling_beside_a_spinning_frame_leaves_a_place_in_the_frames_own_system()
    {
        // The station turns at 0.25 rad/s, so in its axes the depot's crate, which sticks out of the depot 20 m outside the
        // station's bubble, moves at 15 m/s, and 2 s of that reach the bubble. The body in the station flies at 60 m/s 22 m
        // from it.
        FrameHandle station = Station(owner: 2, position: X(1_000));
        frames.SetAngularVelocity(station, new(0, 0, 0.25f));
        FrameHandle depot = Station(owner: 3, position: X(1_100));
        NumericBody inside = BodyOf(frame: station, id: 12, position: X(38), velocity: Y(60)) with { Character = true };

        NumericBody[] after = Update(6, BodyOf(frame: depot, id: 11, position: X(-40)), inside);

        after[1].Home.Should().Be(station);
    }

    [Fact]
    public void A_large_body_that_sticks_out_of_a_sibling_reaches_the_frame_with_its_radius()
    {
        // The depot's crate has a radius of 5 m, and its center lies 47 m from the depot's and 13 m outside the station's
        // bubble: its sphere and the split margin reach the bubble, its center and the margin do not. The body in the station
        // flies at 60 m/s 15 m from it.
        FrameHandle station = Station(owner: 2, position: X(1_000));
        FrameHandle depot = Station(owner: 3, position: X(1_100));
        NumericBody inside = BodyOf(frame: station, id: 12, position: X(38), velocity: Y(60)) with { Character = true };

        NumericBody[] after = Update(6, BodyOf(frame: depot, id: 11, position: X(-47)) with { Radius = 5 }, inside);

        after[1].Home.Should().Be(station);
    }

    [Fact]
    public void A_body_deep_inside_a_sibling_whose_bubble_overlaps_the_frames_leaves_no_place_in_it()
    {
        // The depot's bubble overlaps the station's from 1,020 m to 1,040 m. Its crate lies there, at 1,031 m, and its sphere
        // ends 10 m inside the depot's bubble, the split margin, so it cannot stick out of the depot. The body in the station
        // flies at 60 m/s 5 m from it.
        FrameHandle station = Station(owner: 2, position: X(1_000));
        FrameHandle depot = Station(owner: 3, position: X(1_060));
        NumericBody inside = BodyOf(frame: station, id: 12, position: X(26), velocity: Y(60)) with { Character = true };

        NumericBody[] after = Update(6, BodyOf(frame: depot, id: 11, position: X(-29)), inside);

        frames.Numeric(after[1].Home).Should().Be(NumericMode.Falling);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void A_kinematic_body_or_hull_that_sticks_out_of_a_sibling_on_its_far_side_leaves_a_place_in_the_frame_within_its_content(
        bool hull
    )
    {
        // The depot's crate, kinematic or the dynamic hull of a pod in the depot, sticks out of it at 1,120 m in ground, 80 m
        // outside the station's bubble. The body in the station flies at 60 m/s 10 m inside the bubble, so within 2 s it can
        // stick out by 111 m: the station's content reaches the crate. The stick-out ghost of the body in the station meets
        // neither a kinematic body nor a hull, so the crate reaches the content (fork F2) and has a ghost in the station's own
        // system. The body is 90 m from it, within 2 m plus 2 s of 60 m/s.
        FrameHandle station = Station(owner: 2, position: X(1_000));
        FrameHandle depot = Station(owner: 3, position: X(1_080));
        NumericBody crate = BodyOf(frame: depot, id: 11, position: X(40)) with { Kinematic = !hull };
        if (hull)
        {
            FrameHandle pod = frames.Create(
                frameOwner: new(11, 1),
                parent: depot,
                localPose: new(X(40), DQuaternion.Identity),
                frameBounds: FrameBounds.Sphere(1)
            );
            frames.SetHullMotion(pod, BodyMotion.Dynamic);
            crate = crate with { OwnedFrame = pod };
        }

        NumericBody inside = BodyOf(frame: station, id: 12, position: X(30), velocity: Y(60)) with { Character = true };

        NumericBody[] after = Update(6, crate, inside);

        after[1].Home.Should().Be(station);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void A_dynamic_body_or_character_that_sticks_out_of_a_sibling_on_its_far_side_leaves_no_place_in_the_frame(
        bool character
    )
    {
        // The scene of the kinematic crate, with a crate that can be pushed. The stick-out ghost of the body in the station
        // meets it, and a ghost of the crate in the station would make a mirror pair of two kinematic ghosts. So the crate
        // reaches only the bubble (fork F2), 80 m away, and the body in the station falls in a frame of its own.
        FrameHandle station = Station(owner: 2, position: X(1_000));
        FrameHandle depot = Station(owner: 3, position: X(1_080));
        NumericBody inside = BodyOf(frame: station, id: 12, position: X(30), velocity: Y(60)) with { Character = true };

        NumericBody[] after = Update(
            6,
            BodyOf(frame: depot, id: 11, position: X(40)) with
            {
                Character = character,
            },
            inside
        );

        frames.Numeric(after[1].Home).Should().Be(NumericMode.Falling);
    }

    [Fact]
    public void A_falling_hull_in_a_frame_is_linked_to_the_place_of_a_hull_that_sticks_out_of_a_sibling()
    {
        // The scene of the depot's crate that sticks out of it at 1,040 m in ground, with the dynamic hull of a pod in the
        // depot in place of the crate. The station's system holds no hull of a frame that holds the pod, so physics gives
        // the pod's ghost there the static role of a child ghost of a hull, which the mask of a hull names (fork F1). The
        // shuttle's hull falls at 60 m/s in the station, 10 m from the pod's hull, and alone it would fall in a frame of
        // the station.
        FrameHandle station = Station(owner: 2, position: X(1_000));
        FrameHandle depot = Station(owner: 3, position: X(1_080));
        FrameHandle pod = frames.Create(
            frameOwner: new(11, 1),
            parent: depot,
            localPose: new(X(-40), DQuaternion.Identity),
            frameBounds: FrameBounds.Sphere(1)
        );
        frames.SetHullMotion(pod, BodyMotion.Dynamic);
        FrameHandle shuttle = frames.Create(
            frameOwner: new(30, 1),
            parent: station,
            localPose: new(X(30), DQuaternion.Identity),
            frameBounds: FrameBounds.Sphere(5)
        );
        frames.SetHullMotion(shuttle, BodyMotion.Dynamic);
        NumericBody hull = BodyOf(frame: station, id: 30, position: X(30), velocity: Y(60)) with
        {
            Entity = shuttle.Entity,
            OwnedFrame = shuttle,
            Character = true,
            Radius = 2,
        };

        NumericBody[] after = Update(6, BodyOf(frame: depot, id: 11, position: X(-40)) with { OwnedFrame = pod }, hull);

        after[1].Home.Should().Be(station);
    }

    [Fact]
    public void A_body_that_may_stick_out_of_its_own_frame_leaves_no_place_in_the_frames_own_system()
    {
        // Its ghost lives in the station's tree parent, not in the station's own system, so nothing keeps it there.
        FrameHandle station = Station(owner: 2, position: X(1_000));

        NumericBody[] after = Update(
            6,
            BodyOf(frame: station, id: 12, position: Y(39.5), velocity: X(60)) with
            {
                Character = true,
            }
        );

        frames.Numeric(after[0].Home).Should().Be(NumericMode.Falling);
    }

    // ---- Hysteresis ---------------------------------------------------------------------------------------------------

    [Fact]
    public void A_body_at_the_edge_of_the_reach_of_a_place_does_not_go_in_and_out_of_its_system()
    {
        // Alone, the slow body far out gets a resting frame; touching the crate, it stays in the system of ground.
        FrameHandle station = Station(owner: 2, position: X(Far));
        NumericBody crate = BodyOf(frame: station, id: 11, position: Y(40));
        Update(60, Body(id: 10, position: new(Far, 42, 0)), crate);
        moves.Should().BeEmpty();

        // 7 m apart: beyond the reach of 2 m, within the split margin of 10 m, so it stays, although a whole split cooldown
        // passes.
        Update(60, Body(id: 10, position: new(Far, 47, 0)), crate);
        moves.Should().BeEmpty();

        // 13 m apart for a whole split cooldown: it leaves into a resting frame of its own.
        NumericBody[] after = Update(60, Body(id: 10, position: new(Far, 53, 0)), crate);
        FrameHandle own = after[0].Home;
        frames.Numeric(own).Should().Be(NumericMode.Resting);

        // Back to 7 m apart: the body and the ghost are in two systems, so there is no margin, and it stays in its frame.
        Update(60, Body(id: 10, position: new(Far, 47, 0), home: own), crate);
        moves.Should().BeEmpty();
    }

    [Fact]
    public void A_fast_body_near_a_crate_that_rests_just_inside_a_stations_bubble_stays_in_the_semantic_frames_own_system_whether_the_crate_sleeps_or_not()
    {
        // The review's scene: the crate's sphere ends 1 mm inside the bubble. Awake it moves at 1 mm/s, asleep not at all.
        // The rock comes at it at 60 m/s from 100 m, within 2 m plus 2 s of 60 m/s, and alone it falls in a frame.
        FrameHandle station = Station(owner: 2, position: X(1_000));
        NumericBody rock = Body(id: 10, position: new(1_000, 140, 0), velocity: Y(-60), character: true);

        NumericBody[] awake = Update(6, rock, BodyOf(frame: station, id: 11, position: Y(38.999), velocity: X(1e-3)));
        awake[0].Home.Should().Be(ground, "the awake crate can stick out");
        NumericBody[] asleep = Update(6, rock, BodyOf(frame: station, id: 11, position: Y(38.999)));

        asleep[0].Home.Should().Be(ground, "the crate is as near the edge of the bubble asleep as awake");
        moves.Should().BeEmpty();
    }

    // ---- Anchors that move differently --------------------------------------------------------------------------------

    [Fact]
    public void A_group_that_a_place_links_to_a_Kepler_hull_in_a_following_frame_of_its_own_lives_in_the_home_of_its_static_rock_and_keeps_the_hulls_ghost()
    {
        // A station on a circular orbit 2^24 m out in a weak field, at 7.7 m/s: far enough for a following frame of its own.
        // Its crate sticks out 40 m along Y. The pod flies with the station 1.5 m beyond the crate and 41.5 m from the hull,
        // so the merge test links it to the crate's place but not to the hull. The static rock 15 m beyond the pod is within
        // 2 m plus 2 s of the pod's 7.7 m/s, but 56.5 m from the hull, beyond its reach of 21.4 m.
        const double mu = 1e9,
            start = 100;
        FrameHandle space = frames.Create(
            frameOwner: new(3, 1),
            parent: FrameHandle.Null,
            localPose: DPose.Identity,
            frameBounds: FrameBounds.Sphere(1e9f),
            field: GravityField.Point(mu)
        );
        var path = KeplerOrbit.Circular(Far);
        (Double3 place, Double3 velocity) = path.StateAt(start, mu);
        FrameHandle station = frames.Create(
            frameOwner: new(2, 1),
            parent: space,
            localPose: new(place, path.RotationAt(start, mu)),
            frameBounds: FrameBounds.Sphere(40)
        );
        frames.SetHullMotion(station, BodyMotion.Kinematic);
        frames.SetTreeLinearVelocity(station, velocity);
        FrameHandle orbit = Update(6, KeplerHull(station))[0].Home;
        frames.Numeric(orbit).Should().Be(NumericMode.Following);
        NumericBody pod = SpaceBody(space: space, id: 10, position: place + Y(41.5), velocity: velocity);
        NumericBody rock = SpaceBody(space: space, id: 12, position: place + Y(56.5), velocity: Double3.Zero) with
        {
            Static = true,
        };

        NumericBody[] after = Update(
            6,
            KeplerHull(station),
            pod,
            rock,
            BodyOf(frame: station, id: 11, position: Y(40))
        );

        after[0].Home.Should().Be(orbit, "the hull's orbit moves it, so it keeps its own home");
        FrameHandle raft = after[2].Home;
        frames.Numeric(raft).Should().Be(NumericMode.Resting, "the anchor at rest wins over the following frame");
        after[1].Home.Should().Be(raft);
        frames.AnchorGhosts.ToArray().Should().Equal(new NumericGhost(station.Entity, raft));
    }

    /// <summary>The hull of the Kepler frame <paramref name="station"/>, a body of 5 m, as the World hands it to the update.</summary>
    private NumericBody KeplerHull(FrameHandle station)
    {
        FrameHandle home = frames.TreeParent(station);
        return new()
        {
            Entity = station.Entity,
            Semantic = frames.SemanticOf(home),
            Home = home,
            Position = frames.LocalPose(station).Position,
            HomePosition = frames.TreeLocalPose(station).Position,
            Velocity = frames.LinearVelocity(station),
            Radius = 5,
            Mass = 1e5,
            Kinematic = true,
            Fixed = true,
            OwnedFrame = station,
        };
    }

    /// <summary>A body of radius 1 m in the own system of the root frame <paramref name="space"/>.</summary>
    private static NumericBody SpaceBody(FrameHandle space, uint id, Double3 position, Double3 velocity) =>
        new()
        {
            Entity = new(id, 1),
            Semantic = space,
            Home = space,
            Position = position,
            HomePosition = position,
            Velocity = velocity,
            Radius = 1,
            Mass = 10,
        };

    // ---- Frames without a hull (review of ter-4psc) -------------------------------------------------------------------

    /// <summary>
    /// A ship of a hull of radius 2 m and a bubble of radius 200 m, which flies at 60 m/s at 1,000 m along X in ground and
    /// falls in a numeric frame, and a room without a hull at the ship's origin, with a bubble of
    /// radius 20 m. Returns the ship, the ship's numeric frame and the room.
    /// </summary>
    private (FrameHandle Ship, FrameHandle Pod, FrameHandle Room) RoomInAFallingShip()
    {
        FrameHandle ship = Station(owner: 20, position: X(1_000), motion: BodyMotion.Dynamic, bubble: 200);
        frames.SetTreeLinearVelocity(ship, X(60));
        FrameHandle pod = Update(
            6,
            Body(id: 20, position: X(1_000), velocity: X(60), character: true, radius: 2) with
            {
                OwnedFrame = ship,
            }
        )[0].Home;
        frames.Numeric(pod).Should().Be(NumericMode.Falling);
        FrameHandle room = frames.Create(
            frameOwner: new(30, 1),
            parent: ship,
            localPose: DPose.Identity,
            frameBounds: FrameBounds.Sphere(20)
        );
        return (ship, pod, room);
    }

    [Fact]
    public void A_frame_without_a_hull_in_a_ship_that_dies_in_a_numeric_frame_goes_to_the_ships_semantic_parent()
    {
        (FrameHandle ship, _, FrameHandle room) = RoomInAFallingShip();

        frames.Destroy(ship);

        frames.TreeParent(room).Should().Be(ground, "no hull of the room lives in a numeric frame");
        frames.AssertInvariants();
    }

    [Fact]
    public void A_fast_body_near_a_crate_that_sticks_out_of_a_frame_without_a_hull_lives_where_physics_keeps_the_crates_ghost()
    {
        // Physics keeps the ghost of the crate in the room's tree parent. The rock comes at the crate at 60 m/s from 40 m.
        (FrameHandle ship, _, FrameHandle room) = RoomInAFallingShip();
        frames.Destroy(ship);

        NumericBody[] after = Update(
            6,
            Body(id: 10, position: new(1_000, 60, 0), velocity: Y(-60), character: true),
            BodyOf(frame: room, id: 31, position: Y(19.5))
        );

        after[0].Home.Should().Be(frames.TreeParent(room));
    }

    [Fact]
    public void A_frame_without_a_hull_in_a_numeric_frame_breaks_the_trees_invariants()
    {
        (_, FrameHandle pod, _) = RoomInAFallingShip();
        FrameHandle hall = frames.Create(
            frameOwner: new(40, 1),
            parent: ground,
            localPose: new(X(1_000), DQuaternion.Identity),
            frameBounds: FrameBounds.Sphere(20)
        );

        frames.Reparent(hall, pod);

        frames.Invoking(tree => tree.AssertInvariants()).Should().Throw<ContractException>();
    }

    // ---- Cost ---------------------------------------------------------------------------------------------------------

    [Fact]
    public void A_warm_update_with_places_of_ghosts_allocates_nothing()
    {
        // A rock comes at the crate of a parked station, and a body in the station comes at the rock: the place of the crate
        // links the rock to the hull, and the place of the rock keeps the body in the station's own system. So the update
        // works on both semantic frames with places and makes no numeric frame, whose collection allocates on its own.
        FrameHandle station = Station(owner: 2, position: X(1_000));
        NumericBody[] bodies = Update(
            6,
            ParkedHull(station),
            Body(id: 10, position: new(1_000, 160, 0), velocity: Y(-60), character: true),
            BodyOf(frame: station, id: 11, position: Y(40)),
            BodyOf(frame: station, id: 12, position: Y(38), velocity: X(60)) with
            {
                Character = true,
            }
        );
        bodies.Select(body => body.Home).Should().Equal(ground, ground, station, station);
        frames.NumericCount.Should().Be(0);

        static void ignore(List<NumericMove> _) { }

        frames.UpdateNumericFrames(bodies: bodies, ticks: 1, tickRate: 10, changes: changes, applyMoves: ignore);

        long allocated = Allocations.Fewest(() =>
            frames.UpdateNumericFrames(bodies: bodies, ticks: 1, tickRate: 10, changes: changes, applyMoves: ignore)
        );

        allocated.Should().Be(0);
    }

    // ---- Determinism --------------------------------------------------------------------------------------------------

    [Fact]
    public void Two_runs_with_the_same_input_make_the_same_frames_and_moves()
    {
        (List<NumericMove> Moves, List<FrameHandle> Created, NumericGhost[] Ghosts) first = RunScene(),
            second = RunScene();

        first.Moves.Should().NotBeEmpty();
        second.Moves.Should().Equal(first.Moves);
        second.Created.Should().Equal(first.Created);
        second.Ghosts.Should().Equal(first.Ghosts);
    }

    /// <summary>A character of radius 1 m in <paramref name="frame"/>, at <paramref name="position"/>, flying at <paramref name="velocity"/>.</summary>
    private static NumericBody FreeCharacter(uint id, Double3 position, Double3 velocity, FrameHandle frame) =>
        new()
        {
            Entity = new(id, 1),
            Semantic = frame,
            Home = frame,
            Position = position,
            HomePosition = position,
            Velocity = velocity,
            Radius = 1,
            Mass = 1,
            Character = true,
        };

    /// <summary>
    /// A falling ship and a parked station, each with a crate that sticks out, and rocks near both crates, over three
    /// updates. Returns every move, every frame that the updates create, and the last ghosts of anchors.
    /// </summary>
    private static (List<NumericMove> Moves, List<FrameHandle> Created, NumericGhost[] Ghosts) RunScene()
    {
        var tree = new FrameTree();
        var changes = new PhysicsChanges();
        FrameHandle root = tree.Create(
            frameOwner: new(1, 1),
            parent: FrameHandle.Null,
            localPose: DPose.Identity,
            frameBounds: FrameBounds.Sphere(1e9f)
        );
        FrameHandle ship = tree.Create(
            frameOwner: new(20, 1),
            parent: root,
            localPose: new(X(1_000), DQuaternion.Identity),
            frameBounds: FrameBounds.Sphere(200)
        );
        tree.SetHullMotion(ship, BodyMotion.Dynamic);
        tree.SetTreeLinearVelocity(ship, X(60));
        FrameHandle station = tree.Create(
            frameOwner: new(2, 1),
            parent: root,
            localPose: new(X(-1_000), DQuaternion.Identity),
            frameBounds: FrameBounds.Sphere(40)
        );

        NumericBody[] bodies =
        [
            FreeCharacter(id: 20, position: X(1_000), velocity: X(60), frame: root) with
            {
                Radius = 2,
                OwnedFrame = ship,
            },
            FreeCharacter(id: 10, position: X(1_215), velocity: X(50), frame: root),
            FreeCharacter(id: 12, position: new(-1_000, 160, 0), velocity: Y(-60), frame: root),
            FreeCharacter(id: 13, position: new(-1_000, 400, 0), velocity: Y(-60), frame: root),
            BodyOf(frame: ship, id: 11, position: X(199.5)),
            BodyOf(frame: station, id: 14, position: Y(40)),
        ];
        var allMoves = new List<NumericMove>();
        var created = new List<FrameHandle>();
        for (int update = 0; update < 3; update++)
        {
            bodies = Run(frames: tree, changes: changes, ticks: 6, bodies: bodies, moves: out List<NumericMove> moves);
            allMoves.AddRange(moves);
            created.AddRange(changes.FramesCreated);
        }

        return (allMoves, created, tree.AnchorGhosts.ToArray());
    }
}
