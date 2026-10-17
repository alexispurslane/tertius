using AwesomeAssertions;

namespace Tertius.Core.Tests;

/// <summary>
/// Orbit frames (<see cref="NumericMode.Orbiting"/>, docs/plans/numeric-frames.md, "Anchors", rule 3) on a tree built in
/// the test: the hull of a frame whose Kepler driver moves it fast or far in its semantic frame lives in a numeric frame of
/// its own, K, that follows the orbit, and the hull's frame hangs at K's origin. The tests run
/// <see cref="FrameTree.UpdateNumericFrames"/> with plain body records, at the defaults of <see cref="NumericFrameOptions"/>.
/// A group of free bodies gets a falling frame from 1,000 m/s and keeps it down to 400 m/s. A group with a body that
/// thrusts, such as a character, gets one from 50 m/s and keeps it down to 20 m/s. A slow group gets a resting frame from
/// 2^23 m (about 8,389 km) from its semantic frame's origin and keeps it down to 2^22 m (about 4,194 km). A Kepler hull
/// never thrusts, so it gets an orbit frame from 1,000 m/s or 8,389 km. The lookahead is 2 s and the split margin 10 m.
/// After every update the tree keeps its invariants.
/// </summary>
public class FrameTreeOrbitFrameTests
{
    private const double EarthMu = 3.986004418e14;
    private const double Step = 1.0 / 60;
    private const double Start = 100;

    /// <summary>A station in low orbit that faces earth, so that its rotation turns with its place.</summary>
    private static readonly KeplerOrbit Low = KeplerOrbit.Circular(6.8e6, phase: 0.4) with { Attitude = KeplerAttitude.FacesParent };

    /// <summary>
    /// A station that faces earth on an orbit of 10,000 km at 6.31 km/s: past the create distance of a resting frame, 2^23 m
    /// (about 8,389 km). So a static body beside it rests in a resting frame of its own.
    /// </summary>
    private static readonly KeplerOrbit Far = KeplerOrbit.Circular(1e7, phase: 0.4) with { Attitude = KeplerAttitude.FacesParent };

    private readonly FrameTree frames = new() { StepLength = Step };
    private readonly FrameHandle earth;
    private readonly PhysicsChanges changes = new();
    private List<NumericMove> moves = [];

    public FrameTreeOrbitFrameTests() =>
        earth = frames.Create(new Entity(1, 1), FrameHandle.Null, DPose.Identity, FrameBounds.Sphere(1e9f), MotionDriver.Static,
            GravityField.Point(EarthMu));

    /// <summary>A frame of the owner <paramref name="id"/> in <paramref name="parent"/> on <paramref name="orbit"/>, at its place at <see cref="Start"/>.</summary>
    private FrameHandle Station(uint id, KeplerOrbit orbit, FrameHandle parent = default)
    {
        FrameHandle frame = frames.Create(new Entity(id, 1), parent.IsNull ? earth : parent, DPose.Identity, FrameBounds.Sphere(40),
            MotionDriver.Kepler, kepler: orbit);
        frames.PlaceOnOrbit(frame, Start);
        return frame;
    }

    /// <summary>The hull of <paramref name="station"/>, a body of 5 m, as the World hands it to the update: where the tree holds it now.</summary>
    private NumericBody Hull(FrameHandle station)
    {
        FrameHandle home = frames.TreeParent(station);
        FrameHandle semantic = frames.SemanticOf(home);
        return new NumericBody
        {
            Entity = station.Entity,
            Semantic = semantic,
            Home = home,
            Position = frames.LocalPose(station).Position,
            HomePosition = frames.TreeLocalPose(station).Position,
            Velocity = frames.LinearVelocity(station),
            Radius = 5,
            Mass = 1e5,
            Fixed = true,
            Orbits = true,
            OwnedFrame = station,
        };
    }

    /// <summary>A crate of 1 m at <paramref name="position"/> in the semantic frame of <paramref name="home"/>, flying at <paramref name="velocity"/> there.</summary>
    private NumericBody Crate(uint id, Double3 position, Double3 velocity, FrameHandle home = default, bool isStatic = false)
    {
        FrameHandle physicsHome = home.IsNull ? earth : home;
        FrameHandle semantic = frames.SemanticOf(physicsHome);
        return new NumericBody
        {
            Entity = new Entity(id, 1),
            Semantic = semantic,
            Home = physicsHome,
            Position = position,
            HomePosition = position - frames.HomeOffset(physicsHome, semantic),
            Velocity = velocity,
            Radius = 1,
            Mass = 10,
            Static = isStatic,
        };
    }

    /// <summary>Runs one update and returns the bodies with their new homes and their places in them.</summary>
    private NumericBody[] Update(params NumericBody[] bodies)
    {
        changes.Clear();
        moves = [];
        frames.UpdateNumericFrames(bodies, Step, changes, handed =>
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

    private static (Double3 Position, Double3 Velocity) StateAt(KeplerOrbit orbit, double time) => orbit.StateAt(time, EarthMu);

    [Fact]
    public void A_fast_Kepler_hull_alone_gets_an_orbit_frame_and_its_frame_hangs_at_the_orbit_frames_origin()
    {
        FrameHandle station = Station(10, Low);
        (Double3 place, Double3 velocity) = StateAt(Low, Start);

        FrameHandle orbit = Update(Hull(station))[0].Home;

        frames.Numeric(orbit).Should().Be(NumericMode.Orbiting);
        frames.TreeParent(orbit).Should().Be(earth);
        frames.TreeParent(station).Should().Be(orbit, "the hull's frame hangs under the hull's home, as a ship's frame does");
        frames.Parent(station).Should().Be(earth, "gameplay sees the semantic tree");
        frames.TreeLocalPose(station).Position.Should().Be(Double3.Zero, "the hull rests at the orbit frame's origin");
        frames.TreeLocalPose(orbit).Position.Should().Be(place, "the orbit frame is where the orbit is");
        frames.LocalPose(station).Position.Should().Be(place, "a read in earth is the orbit's place, to the last bit");
        frames.LocalPose(station).Rotation.Should().Be(Low.RotationAt(Start, EarthMu), "the frame keeps its own rotation");
        frames.LinearVelocity(orbit).Should().Be(NumericFrameRules.OrbitChord(place, velocity, EarthMu, Step));
        (frames.LinearVelocity(station) - velocity).Length.Should().BeLessThan(1e-9, "a read in earth is the orbit's velocity");
        changes.FramesCreated.Should().Equal(orbit);
        moves.Should().Equal(new NumericMove(station.Entity, earth, orbit));
    }

    [Fact]
    public void At_each_placement_the_orbit_frame_takes_the_orbits_place_and_chord_and_the_frame_stays_at_its_origin()
    {
        FrameHandle station = Station(10, Low);
        FrameHandle orbit = Update(Hull(station))[0].Home;

        for (int tick = 1; tick <= 600; tick++)
        {
            double time = Start + tick * Step;
            frames.PlaceOnOrbit(station, time);
            (Double3 place, Double3 velocity) = StateAt(Low, time);
            frames.TreeLocalPose(orbit).Position.Should().Be(place);
            frames.LinearVelocity(orbit).Should().Be(NumericFrameRules.OrbitChord(place, velocity, EarthMu, Step));
            frames.TreeLocalPose(station).Position.Should().Be(Double3.Zero);
            frames.TreeLocalPose(station).Rotation.Should().Be(Low.RotationAt(time, EarthMu));
            frames.TreeLinearVelocity(station).Should().Be(velocity - frames.LinearVelocity(orbit), "the frame moves at the orbit's velocity in earth");
        }
        frames.AssertInvariants();
    }

    [Fact]
    public void During_a_step_the_orbit_frame_falls_with_the_orbits_acceleration_and_ends_on_the_orbit()
    {
        FrameHandle station = Station(10, Low);
        FrameHandle orbit = Update(Hull(station))[0].Home;
        (Double3 place, _) = StateAt(Low, Start);

        frames.Update();
        frames.BeginNumericStep();
        frames.HomeAcceleration(orbit, earth).Should().Be(NumericFrameRules.OrbitAcceleration(place, EarthMu));
        frames.EffectiveGravity(orbit, Double3.Zero).Length().Should().BeLessThan(1e-6f, "a body at the hull feels the field minus the orbit's pull");
        frames.AdvanceNumericFrames(Step);

        (frames.TreeLocalPose(orbit).Position - StateAt(Low, Start + Step).Position).Length.Should().BeLessThan(1e-6);
    }

    [Fact]
    public void A_crate_near_an_orbiting_hull_joins_its_orbit_frame()
    {
        FrameHandle station = Station(10, Low);
        FrameHandle orbit = Update(Hull(station))[0].Home;
        (Double3 place, Double3 velocity) = StateAt(Low, Start);

        // The merge test reaches the radii of 5 m and 1 m, and 2 s of the crate's 1 m/s relative to the hull.
        NumericBody[] after = Update(Hull(station), Crate(11, place + new Double3(7, 0, 0), velocity + new Double3(1, 0, 0)));

        after[1].Home.Should().Be(orbit);
        frames.NumericCount.Should().Be(1);
        frames.LinearVelocity(orbit).Should().Be(NumericFrameRules.OrbitChord(place, velocity, EarthMu, Step), "an orbit frame never drains");
    }

    [Fact]
    public void A_crate_that_leaves_the_hulls_reach_falls_in_a_frame_of_its_own_and_the_hull_keeps_its_orbit_frame()
    {
        FrameHandle station = Station(10, Low);
        (Double3 place, Double3 velocity) = StateAt(Low, Start);
        NumericBody[] together = Update(Hull(station), Crate(11, place + new Double3(5, 0, 0), velocity));
        FrameHandle orbit = together[0].Home;
        together[1].Home.Should().Be(orbit);

        // 5 km away: beyond the radii, 2 s of the relative speed and the split margin.
        NumericBody[] after = Update(Hull(station), Crate(11, place + new Double3(5_000, 0, 0), velocity, home: orbit));

        after[0].Home.Should().Be(orbit);
        after[1].Home.Should().NotBe(orbit, "only the hull's group keeps an orbit frame");
        frames.Numeric(after[1].Home).Should().Be(NumericMode.Falling);
    }

    [Fact]
    public void A_slow_near_Kepler_hull_stays_in_its_semantic_frames_own_system_and_takes_a_fast_body_there()
    {
        // A circular orbit of 1 km in a field of 1e-9 m³/s² moves at 1 µm/s. The fast body is a character, which always
        // thrusts, so at 60 m/s it would fall in a frame of its own.
        FrameHandle space = frames.Create(new Entity(2, 1), FrameHandle.Null, DPose.Identity, FrameBounds.Sphere(1e7f), MotionDriver.Static,
            GravityField.Point(1e-9));
        FrameHandle station = Station(10, KeplerOrbit.Circular(1_000), space);
        NumericBody character = Crate(11, new Double3(1_000, 3, 0), new Double3(60, 0, 0), home: space) with { Character = true };
        NumericFrameRules.BodyMode(character, thrusts: true, frames.NumericOptions).Should().Be(NumericMode.Falling,
            "alone, a character at 60 m/s needs a falling frame");

        NumericBody[] after = Update(Hull(station), character);

        after[0].Home.Should().Be(space);
        after[1].Home.Should().Be(space, "the slow hull is an anchor at rest in the system of space");
        frames.NumericCount.Should().Be(0);
        frames.AnchorGhosts.IsEmpty.Should().BeTrue();
    }

    [Fact]
    public void A_group_that_links_an_orbiting_hull_and_a_static_body_takes_the_static_bodys_home_and_keeps_a_ghost_of_the_hull()
    {
        FrameHandle station = Station(10, Far);
        FrameHandle orbit = Update(Hull(station))[0].Home;
        (Double3 place, _) = StateAt(Far, Start);

        // The static body lies 10,000 km from earth's origin, past the create distance, so it rests in a resting frame of
        // its own.
        NumericBody[] after = Update(Hull(station), Crate(11, place + new Double3(0, 20, 0), Double3.Zero, isStatic: true));

        after[0].Home.Should().Be(orbit, "no home holds both anchors at rest, and the hull keeps its own");
        FrameHandle raft = after[1].Home;
        frames.Numeric(raft).Should().Be(NumericMode.Resting, "the anchor at rest wins over the orbit frame");
        frames.AnchorGhosts.ToArray().Should().Equal(new NumericGhost(station.Entity, raft));

        Update(Hull(station), after[1]);
        moves.Should().BeEmpty("the next sync point makes the same choice");
        frames.AnchorGhosts.ToArray().Should().Equal(new NumericGhost(station.Entity, raft));
    }

    [Fact]
    public void The_ghost_of_an_orbiting_hull_goes_when_the_group_lets_go_of_it()
    {
        FrameHandle station = Station(10, Far);
        Update(Hull(station));
        (Double3 place, _) = StateAt(Far, Start);
        FrameHandle raft = Update(Hull(station), Crate(11, place + new Double3(0, 20, 0), Double3.Zero, isStatic: true))[1].Home;
        frames.Numeric(raft).Should().Be(NumericMode.Resting, "the static body lies past the create distance");
        frames.AnchorGhosts.ToArray().Should().Equal(new NumericGhost(station.Entity, raft));

        // The hull flies on along its orbit, 6.31 km/s past the static body, 19 km in 3 s: beyond the reach of 2 s of that
        // speed and the split margin.
        frames.PlaceOnOrbit(station, Start + 3);
        Update(Hull(station), Crate(11, place + new Double3(0, 20, 0), Double3.Zero, home: raft, isStatic: true));

        frames.AnchorGhosts.IsEmpty.Should().BeTrue();
    }

    [Fact]
    public void Two_orbiting_hulls_take_the_frame_of_the_first_and_that_frame_keeps_a_ghost_of_the_second()
    {
        // Two stations on one orbit, 6.8 m apart.
        FrameHandle first = Station(10, Low);
        FrameHandle second = Station(11, Low with { MeanAnomalyAtEpoch = Low.MeanAnomalyAtEpoch + 1e-6 });
        (Double3 place, Double3 velocity) = StateAt(Low, Start);

        NumericBody[] after = Update(Hull(first), Hull(second), Crate(12, place + new Double3(0, 3, 0), velocity));

        frames.Numeric(after[0].Home).Should().Be(NumericMode.Orbiting);
        frames.Numeric(after[1].Home).Should().Be(NumericMode.Orbiting);
        after[1].Home.Should().NotBe(after[0].Home, "each hull rests in an orbit frame of its own");
        after[2].Home.Should().Be(after[0].Home, "the group takes the orbit frame of its first hull");
        frames.AnchorGhosts.ToArray().Should().Equal(new NumericGhost(second.Entity, after[0].Home));
    }

    [Fact]
    public void A_hull_whose_orbit_no_longer_needs_a_frame_goes_back_with_its_frame_and_its_orbit_frame_goes_away()
    {
        // 10,000 km out in a field of 1e-9 m³/s², the hull is slow but far: past the create distance of 2^23 m (about
        // 8,389 km), so it lives in an orbit frame. Then it moves to an orbit of 1 km, within the leave distance of 2^22 m.
        FrameHandle space = frames.Create(new Entity(2, 1), FrameHandle.Null, DPose.Identity, FrameBounds.Sphere(1e8f), MotionDriver.Static,
            GravityField.Point(1e-9));
        FrameHandle station = Station(10, KeplerOrbit.Circular(1e7), space);
        FrameHandle orbit = Update(Hull(station))[0].Home;
        frames.Numeric(orbit).Should().Be(NumericMode.Orbiting);

        KeplerOrbit near = KeplerOrbit.Circular(1_000);
        frames.SetOrbit(station, near);
        frames.PlaceOnOrbit(station, Start);
        NumericBody[] after = Update(Hull(station));

        after[0].Home.Should().Be(space);
        frames.TreeParent(station).Should().Be(space);
        frames.IsAlive(orbit).Should().BeFalse("an orbit frame that its frame leaves falls, and one without bodies goes");
        (frames.LocalPose(station).Position - near.StateAt(Start, 1e-9).Position).Length.Should().BeLessThan(1e-9);
    }

    [Fact]
    public void A_frame_that_leaves_its_orbit_frame_for_another_driver_turns_that_frame_into_a_falling_frame()
    {
        FrameHandle station = Station(10, Low);
        FrameHandle orbit = Update(Hull(station))[0].Home;
        Double3 velocity = frames.LinearVelocity(orbit);

        frames.SetDriver(station, MotionDriver.Physics);

        frames.Numeric(orbit).Should().Be(NumericMode.Falling, "a frame that physics drives may hang in a falling frame");
        frames.LinearVelocity(orbit).Should().Be(velocity, "it falls on from where it was");
        frames.TreeParent(station).Should().Be(orbit);
        frames.AssertInvariants();
    }

    [Fact]
    public void Moving_a_frame_out_of_its_orbit_frame_turns_that_frame_into_a_falling_frame_that_keeps_its_other_bodies()
    {
        FrameHandle station = Station(10, Low);
        (Double3 place, Double3 velocity) = StateAt(Low, Start);
        NumericBody[] after = Update(Hull(station), Crate(11, place + new Double3(5, 0, 0), velocity));
        FrameHandle orbit = after[0].Home;
        after[1].Home.Should().Be(orbit);

        frames.Reparent(station, earth);

        frames.Numeric(orbit).Should().Be(NumericMode.Falling, "the crate falls on in it");
        frames.InOrbitFrame(station).Should().BeFalse();
        frames.AssertInvariants();
    }

    [Fact]
    public void Destroying_a_frame_in_its_orbit_frame_turns_that_frame_into_a_falling_frame_and_drops_the_ghosts_of_its_hull()
    {
        FrameHandle station = Station(10, Far);
        FrameHandle orbit = Update(Hull(station))[0].Home;
        (Double3 place, _) = StateAt(Far, Start);
        FrameHandle raft = Update(Hull(station), Crate(11, place + new Double3(0, 20, 0), Double3.Zero, isStatic: true))[1].Home;
        frames.Numeric(raft).Should().Be(NumericMode.Resting, "the static body lies past the create distance");
        frames.AnchorGhosts.ToArray().Should().Equal(new NumericGhost(station.Entity, raft));

        frames.Destroy(station);

        frames.Numeric(orbit).Should().Be(NumericMode.Falling);
        frames.AnchorGhosts.IsEmpty.Should().BeTrue("the hull is gone");
        frames.AssertInvariants();
    }

    [Fact]
    public void Destroying_the_home_of_a_group_drops_the_ghosts_that_it_keeps_of_orbiting_hulls()
    {
        FrameHandle station = Station(10, Far);
        Update(Hull(station));
        (Double3 place, _) = StateAt(Far, Start);
        FrameHandle raft = Update(Hull(station), Crate(11, place + new Double3(0, 20, 0), Double3.Zero, isStatic: true))[1].Home;
        frames.Numeric(raft).Should().Be(NumericMode.Resting, "the static body lies past the create distance");
        frames.AnchorGhosts.ToArray().Should().Equal(new NumericGhost(station.Entity, raft));

        frames.Destroy(raft);

        frames.AnchorGhosts.IsEmpty.Should().BeTrue();
        frames.InOrbitFrame(station).Should().BeTrue("the hull's own frame did not change");
    }

    [Fact]
    public void Destroying_the_semantic_frame_of_an_orbit_frame_dissolves_it_and_drops_the_ghosts_of_its_hull()
    {
        // Earth is a semantic child of the sun, which has no gravity of its own here.
        FrameHandle sun = frames.Create(new Entity(3, 1), FrameHandle.Null, DPose.Identity, FrameBounds.Sphere(1e9f), MotionDriver.Static);
        frames.Reparent(earth, sun);
        FrameHandle station = Station(10, Far);
        FrameHandle orbit = Update(Hull(station))[0].Home;
        (Double3 place, _) = StateAt(Far, Start);
        FrameHandle raft = Update(Hull(station), Crate(11, place + new Double3(0, 20, 0), Double3.Zero, isStatic: true))[1].Home;
        frames.Numeric(raft).Should().Be(NumericMode.Resting, "the static body lies past the create distance");
        frames.AnchorGhosts.ToArray().Should().Equal(new NumericGhost(station.Entity, raft));

        var dissolved = new List<FrameHandle>();
        frames.Destroy(earth, dissolved);

        dissolved.Should().BeEquivalentTo([orbit, raft]);
        frames.Parent(station).Should().Be(sun);
        frames.TreeParent(station).Should().Be(sun);
        frames.AnchorGhosts.IsEmpty.Should().BeTrue();
    }

    [Fact]
    public void A_fast_body_near_a_hull_on_a_fast_Kepler_orbit_joins_the_hulls_orbit_frame_and_keeps_no_ghost_of_it()
    {
        // Before ter-dhkf, the body fell in a frame of its own beside the hull, and that frame kept a ghost of the hull.
        FrameHandle station = Station(10, Low);
        (Double3 place, Double3 velocity) = StateAt(Low, Start);

        NumericBody[] after = Update(Hull(station), Crate(11, place + new Double3(2, 0, 0), velocity + new Double3(60, 0, 0)));

        frames.Numeric(after[0].Home).Should().Be(NumericMode.Orbiting);
        after[1].Home.Should().Be(after[0].Home, "the hull counts as infinitely heavy, so the group takes its home");
        frames.AnchorGhosts.IsEmpty.Should().BeTrue("the hull is a real body of the group's system");
    }

    [Fact]
    public void An_orbit_frame_far_from_its_semantic_frames_origin_does_not_nest()
    {
        // A falling frame this far out would move under a nesting frame. The orbit gives the orbit frame its place, exactly.
        frames.NumericOptions = new NumericFrameOptions { NestDistance = 1e6 };
        FrameHandle station = Station(10, Low);
        FrameHandle orbit = Update(Hull(station))[0].Home;
        frames.TreeParent(orbit).Should().Be(earth);
        frames.NumericCount.Should().Be(1);
    }
}
