using System.Numerics;
using AwesomeAssertions;
using Tertius.Frames.Rails;

namespace Tertius.Core.Tests;

/// <summary>
/// Orbit frames (<see cref="NumericMode.Following"/>, docs/plans/numeric-frames.md, "Anchors", rule 3) on a tree built in
/// the test: the kinematic hull of a frame whose samples move it fast or far in its semantic frame lives in a numeric frame
/// of its own, K, that follows the frame's motion samples (docs/plans/rails-components.md, "Numeric frames"), and the hull's
/// frame hangs at K's origin. The tests hand the tree the intents of each step, with the samples as the owners' motion
/// intents, and do before each step and at each sync point what the World and physics do with them (see <see cref="Sync"/>). The tests run
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

    private readonly FrameTree frames = new();
    private readonly FrameHandle earth;
    private readonly PhysicsChanges changes = new();
    private List<NumericMove> moves = [];

    public FrameTreeOrbitFrameTests() =>
        earth = frames.Create(new Entity(1, 1), FrameHandle.Null, DPose.Identity, FrameBounds.Sphere(1e9f),
            GravityField.Point(EarthMu));

    /// <summary>
    /// A frame of the owner <paramref name="id"/> in <paramref name="parent"/>, or in earth, with a kinematic hull, at the
    /// place, rotation and velocity of <paramref name="orbit"/> at <see cref="Start"/>, as a spawn from the rails library puts it.
    /// </summary>
    private FrameHandle Station(uint id, KeplerOrbit orbit, FrameHandle parent = default)
    {
        FrameHandle space = parent.IsNull ? earth : parent;
        FrameHandle frame = frames.Create(new Entity(id, 1), space, DPose.Identity, FrameBounds.Sphere(40));
        frames.SetHullMotion(frame, BodyMotion.Kinematic);
        double mu = KeplerOrbit.MuFrom(frames.Gravity(space));
        (Double3 place, Double3 velocity) = orbit.StateAt(Start, mu);
        frames.SetLocalPose(frame, new DPose(place, orbit.RotationAt(Start, mu)));
        frames.SetTreeLinearVelocity(frame, velocity);
        return frame;
    }

    /// <summary>
    /// The intents of a step in which only the owner of <paramref name="station"/> wrote one: <paramref name="move"/>, relative
    /// to the station's semantic parent, as the owner writes it, with a teleport to <paramref name="teleport"/> when it has one.
    /// </summary>
    private PhysicsIntent[] Moving(FrameHandle station, in MotionIntent move, DPose? teleport = null)
    {
        var intents = new PhysicsIntent[station.Entity.Index + 1];
        intents[station.Entity.Index] = new PhysicsIntent
        {
            Frame = frames.Parent(station),
            Move = move,
            Flags = teleport is null ? IntentFlags.None : IntentFlags.Teleport,
            TeleportPose = teleport ?? default,
        };
        return intents;
    }

    /// <summary>
    /// The step that ends at <paramref name="time"/> and its sync point, for <paramref name="station"/> on
    /// <paramref name="orbit"/>: its owner wrote the orbit's sample of that step in the tick before, which the step applies,
    /// so before the step the tree derives the plan of a station that hangs in its orbit frame (see
    /// <see cref="FrameTree.BeginNumericStep"/>). At the sync point, the orbit frame lands on the plan, takes its place and
    /// chord exactly (<see cref="FrameTree.PlaceFollowingFrame"/>), and the station takes the record that physics releases
    /// for its hull, at the origin of the orbit frame with the rest of the sample's velocity. Any other station takes the
    /// sample.
    /// </summary>
    private MotionSample Sync(FrameHandle station, KeplerOrbit orbit, double time)
    {
        double mu = KeplerOrbit.MuFrom(frames.Gravity(frames.Parent(station)));
        MotionSample sample = orbit.Sample(mu, time, Step);
        PhysicsIntent[] intents = Moving(station, new MotionIntent(sample));
        frames.Update();
        frames.BeginNumericStep(intents, Step);
        MotionSample record = sample;
        if (frames.InFollowingFrame(station))
        {
            frames.AdvanceNumericFrames(Step);
            frames.PlaceFollowingFrame(frames.TreeParent(station));
            record = AtOrigin(sample);
        }
        frames.TakeStepRecords(intents, Step, new StepOwners().WithHullRecord(station, record));
        return sample;
    }

    /// <summary>The record that physics releases for a hull at the origin of its orbit frame that applied <paramref name="sample"/>.</summary>
    private static MotionSample AtOrigin(in MotionSample sample) => sample with
    {
        Pose = new DPose(Double3.Zero, sample.Pose.Rotation),
        ChordVelocity = Double3.Zero,
        Velocity = sample.Velocity - sample.ChordVelocity,
        Acceleration = Double3.Zero,
    };

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
            Follows = true,
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

    private static (Double3 Position, Double3 Velocity) StateAt(KeplerOrbit orbit, double time) => orbit.StateAt(time, EarthMu);

    [Fact]
    public void A_fast_kinematic_hull_alone_gets_an_orbit_frame_and_its_frame_hangs_at_the_orbit_frames_origin()
    {
        FrameHandle station = Station(10, Low);
        (Double3 place, Double3 velocity) = StateAt(Low, Start);

        FrameHandle orbit = Update(Hull(station))[0].Home;

        frames.Numeric(orbit).Should().Be(NumericMode.Following);
        frames.TreeParent(orbit).Should().Be(earth);
        frames.TreeParent(station).Should().Be(orbit, "the hull's frame hangs under the hull's home, as a ship's frame does");
        frames.Parent(station).Should().Be(earth, "gameplay sees the semantic tree");
        frames.TreeLocalPose(station).Position.Should().Be(Double3.Zero, "the hull rests at the orbit frame's origin");
        frames.TreeLocalPose(orbit).Position.Should().Be(place, "the orbit frame is where the orbit is");
        frames.LocalPose(station).Position.Should().Be(place, "a read in earth is the orbit's place, to the last bit");
        frames.LocalPose(station).Rotation.Should().Be(Low.RotationAt(Start, EarthMu), "the frame keeps its own rotation");
        Double3 chord = velocity - GravityField.Point(EarthMu).AtPrecise(place) * (Step / 2);
        frames.LinearVelocity(orbit).Should().Be(chord, "the orbit frame starts with the chord of the frame's instant velocity");
        frames.TreeLinearVelocity(station).Should().Be(velocity - chord);
        (frames.LinearVelocity(station) - velocity).Length.Should().BeLessThan(1e-9, "a read in earth is the frame's velocity");
        changes.FramesCreated.Should().Equal(orbit);
        moves.Should().Equal(new NumericMove(station.Entity, earth, orbit));
    }

    [Fact]
    public void At_each_sync_point_an_orbit_frame_takes_its_anchors_sample_place_and_chord()
    {
        FrameHandle station = Station(10, Low);
        FrameHandle orbit = Update(Hull(station))[0].Home;

        for (int tick = 1; tick <= 600; tick++)
        {
            double time = Start + tick * Step;
            MotionSample sample = Sync(station, Low, time);
            frames.TreeLocalPose(orbit).Position.Should().Be(sample.Pose.Position);
            frames.TreeLocalPose(orbit).Position.Should().Be(StateAt(Low, time).Position, "the sample is the orbit's place, to the last bit");
            frames.LinearVelocity(orbit).Should().Be(sample.ChordVelocity);
            frames.TreeLocalPose(station).Should().Be(new DPose(Double3.Zero, sample.Pose.Rotation));
            frames.LinearVelocity(station).Should().Be(sample.ChordVelocity + (sample.Velocity - sample.ChordVelocity),
                "a read in earth is the sample's instant velocity");
        }
        frames.AssertInvariants();
    }

    [Fact]
    public void An_orbit_frame_whose_anchor_has_no_applied_sample_stays_where_it_is_at_the_sync_point()
    {
        FrameHandle station = Station(10, Low);
        FrameHandle orbit = Update(Hull(station))[0].Home;
        DPose before = frames.TreeLocalPose(orbit);

        frames.PlaceFollowingFrame(frames.TreeParent(station));

        frames.TreeLocalPose(orbit).Should().Be(before);
    }

    [Fact]
    public void During_a_step_an_orbit_frame_falls_with_the_acceleration_that_lands_it_on_its_anchors_plan_and_ends_on_the_plan()
    {
        FrameHandle station = Station(10, Low);
        FrameHandle orbit = Update(Hull(station))[0].Home;
        Sync(station, Low, Start + Step);
        // The sample of the next step, which its owner wrote in the tick before.
        MotionSample next = Low.Sample(EarthMu, Start + 2 * Step, Step);
        Double3 velocity = frames.LinearVelocity(orbit);

        frames.Update();
        frames.BeginNumericStep(Moving(station, new MotionIntent(next)), Step);
        frames.HomeAcceleration(orbit, earth).Should().Be((next.ChordVelocity - velocity) / Step);
        frames.EffectiveGravity(orbit, Double3.Zero).Length().Should().BeLessThan(1e-6f, "a body at the hull feels the field minus the orbit's pull");
        frames.AdvanceNumericFrames(Step);

        (frames.TreeLocalPose(orbit).Position - next.Pose.Position).Length.Should().BeLessThan(1e-6);
    }

    [Fact]
    public void An_orbit_frame_takes_the_acceleration_that_lands_it_on_its_anchors_plan_whatever_the_field_or_the_plans_acceleration_says()
    {
        FrameHandle station = Station(10, Low);
        FrameHandle orbit = Update(Hull(station))[0].Home;
        var pushed = new MotionSample(frames.LocalPose(station), Double3.Zero, Double3.Zero, new Double3(1, 2, 3), Double3.Zero, Double3.Zero);
        Double3 velocity = frames.LinearVelocity(orbit);

        frames.Update();
        frames.BeginNumericStep(Moving(station, new MotionIntent(pushed)), Step);

        frames.HomeAcceleration(orbit, earth).Should().Be((Double3.Zero - velocity) / Step, "the plan's chord is zero, so the frame stops");
    }

    /// <summary>
    /// The landing rule of "The step plan of a following frame" (docs/plans/rails-components.md): an end pose has no
    /// acceleration that lands the frame on it, because its velocity changes at the start of the step, so the orbit frame
    /// takes the acceleration that turns its velocity into the plan's chord.
    /// </summary>
    [Fact]
    public void AFollowingFrameLandsOnItsAnchorsPlanAtEverySyncPoint()
    {
        FrameHandle station = Station(10, Low);
        FrameHandle orbit = Update(Hull(station))[0].Home;

        for (int tick = 1; tick <= 5; tick++)
        {
            DPose start = frames.LocalPose(station);
            var end = new DPose(start.Position + new Double3(7_000, 100 * tick, -50) * Step, start.Rotation);
            PhysicsIntent[] intents = Moving(station, new MotionIntent(new EndPose(end)));
            frames.Update();
            frames.BeginNumericStep(intents, Step);
            frames.AdvanceNumericFrames(Step);

            (frames.TreeLocalPose(orbit).Position - end.Position).Length.Should().BeLessThan(1e-6, "the step lands the frame on its plan to its rounding");
            frames.PlaceFollowingFrame(frames.TreeParent(station));
            frames.TreeLocalPose(orbit).Position.Should().Be(end.Position);
            frames.LinearVelocity(orbit).Should().Be((end.Position - start.Position) / Step, "the plan's chord");
            frames.TakeStepRecords(intents, Step, new StepOwners().WithHullRecord(station,
                new MotionSample(new DPose(Double3.Zero, end.Rotation), Double3.Zero, Double3.Zero, Double3.Zero, Double3.Zero, Double3.Zero)));
        }
    }

    [Fact]
    public void An_orbit_frame_whose_anchor_has_no_sample_falls_as_a_falling_frame()
    {
        FrameHandle station = Station(10, Low);
        FrameHandle orbit = Update(Hull(station))[0].Home;
        Double3 place = frames.TreeLocalPose(orbit).Position;

        frames.Update();
        frames.BeginNumericStep([], Step);

        frames.Numeric(orbit).Should().Be(NumericMode.Following, "the hull is still kinematic");
        frames.HomeAcceleration(orbit, earth).Should().Be(GravityField.Point(EarthMu).AtPrecise(place), "the field of earth at its origin pulls it");
    }

    /// <summary>
    /// The start of a new following frame (docs/plans/rails-components.md, "The first step of a new following frame"): it
    /// lies at its anchor's place, and its velocity is the chord that the numeric frames expect of a frame at the start of a
    /// step, the chord of its anchor's motion over the step that ended, from what the anchor's row keeps of that step: the
    /// instant velocity less half a step of the acceleration, which is the gravity at the anchor's place plus the anchor's
    /// proper acceleration, turned out of its axes. The anchor rests at its origin and keeps the rest of its instant
    /// velocity, so a read in earth gives that velocity.
    /// </summary>
    [Fact]
    public void ANewFollowingFrameStartsAtItsAnchorsPlaceWithTheChordOfItsAnchorsMotionOverTheStepThatEnded()
    {
        FrameHandle station = Station(10, Low);
        // The record of the step that ended at this sync point, with a chord of its own, which the row does not keep, so the
        // new frame does not take it. The chord moves the place where the record says the step started, and the gravity
        // there, so the station's proper acceleration is not zero.
        MotionSample sample = Low.Sample(EarthMu, Start, Step) with { ChordVelocity = new Double3(7_000, 100, 200) };
        frames.TakeStepRecords(Moving(station, new MotionIntent(sample)), Step, new StepOwners().WithHullRecord(station, sample));

        FrameHandle orbit = Update(Hull(station))[0].Home;

        Double3 acceleration = GravityField.Point(EarthMu).AtPrecise(sample.Pose.Position)
            + sample.Pose.Rotation.Rotate((Double3)frames.ProperAcceleration(station));
        Double3 chord = sample.Velocity - acceleration * (Step / 2);
        frames.ProperAcceleration(station).Should().NotBe(Vector3.Zero);
        frames.Numeric(orbit).Should().Be(NumericMode.Following);
        frames.TreeLocalPose(orbit).Position.Should().Be(sample.Pose.Position);
        frames.LinearVelocity(orbit).Should().Be(chord, "the anchor's instant velocity less half a step of the acceleration that its row keeps");
        frames.TreeLocalPose(station).Should().Be(new DPose(Double3.Zero, sample.Pose.Rotation));
        frames.TreeLinearVelocity(station).Should().Be(sample.Velocity - chord, "the anchor keeps the rest of its instant velocity");
        (frames.LinearVelocity(station) - sample.Velocity).Length.Should().BeLessThan(1e-9, "a read in earth keeps the anchor's velocity");
    }

    /// <summary>
    /// An anchor on a Kepler orbit falls freely, so its proper acceleration is zero and its acceleration is the pull at its
    /// place. Its new following frame starts with the orbit's chord of the step that ended (<see cref="KeplerOrbit.Chord"/>,
    /// the two-body form) to the jerk term, 4.5e-7 m/s in low orbit, and its first plan lands it with the pull, as every
    /// later plan does.
    /// </summary>
    [Fact]
    public void ANewFollowingFrameOfAnAnchorOnAKeplerOrbitStartsWithTheOrbitsChordAndLandsOnItsFirstPlanWithThePull()
    {
        FrameHandle station = Station(10, Low);
        MotionSample sample = Low.Sample(EarthMu, Start, Step);
        frames.TakeStepRecords(Moving(station, new MotionIntent(sample)), Step, new StepOwners().WithHullRecord(station, sample));

        FrameHandle orbit = Update(Hull(station))[0].Home;
        frames.Update();
        frames.BeginNumericStep(Moving(station, new MotionIntent(Low.Sample(EarthMu, Start + Step, Step))), Step);

        (frames.LinearVelocity(orbit) - KeplerOrbit.Chord(sample.Pose.Position, sample.Velocity, EarthMu, Step)).Length
            .Should().BeLessThan(1e-6, "the orbit's chord over the step that ended, to its jerk");
        (frames.HomeAcceleration(orbit, earth) - GravityField.Point(EarthMu).AtPrecise(sample.Pose.Position)).Length
            .Should().BeLessThan(1e-4, "the first plan lands the frame with the pull of 8.6 m/s², to the jerk of the orbit");
    }

    /// <summary>
    /// An anchor that moves on end poses at a steady velocity does not fall: its record has no acceleration, so its proper
    /// acceleration is the pull reversed. Its new following frame starts with the anchor's velocity, which is the chord of
    /// the step that ended, and its first plan, the next end pose, lands it without an acceleration, as every later plan
    /// does. A start from the pull alone would give that first step half the pull.
    /// </summary>
    [Fact]
    public void ANewFollowingFrameOfAnAnchorOnEndPosesStartsWithItsAnchorsVelocityAndTakesNoAccelerationInItsFirstPlannedStep()
    {
        FrameHandle station = Station(10, Low);
        Double3 place = new(6.8e6, 0, 0), velocity = new(0, 2_000, 0);
        var record = new MotionSample(new DPose(place, DQuaternion.Identity), velocity, velocity, Double3.Zero, Double3.Zero, Double3.Zero);
        frames.TakeStepRecords(Moving(station, new MotionIntent(new EndPose(record.Pose))), Step, new StepOwners().WithHullRecord(station, record));

        FrameHandle orbit = Update(Hull(station))[0].Home;
        frames.Update();
        frames.BeginNumericStep(Moving(station, new MotionIntent(new EndPose(new DPose(place + velocity * Step, DQuaternion.Identity)))), Step);

        (frames.LinearVelocity(orbit) - velocity).Length.Should().BeLessThan(1e-6, "the anchor's velocity is its chord");
        frames.HomeAcceleration(orbit, earth).Length.Should().BeLessThan(1e-4, "an end pose at a steady velocity has no acceleration");
    }

    /// <summary>
    /// The acceleration of the anchor, read backward from its row, takes the gravity at its place with its parent's proper
    /// acceleration as the step that ended found it: the value that the rule of the anchor's own proper acceleration read
    /// (docs/plans/rails-components.md, "Which proper acceleration of the parent"). Here the anchor's parent, a ship without
    /// a hull in empty space, starts to accelerate at 10 m/s² in the step that ended, so its value goes from zero to that at
    /// the same step 2, and the anchor moves on end poses at a steady 2 km/s relative to the ship. The new following frame
    /// starts with the anchor's velocity, which is the chord of the step that ended, and its first plan, the next end pose,
    /// lands it without an acceleration. A read with the ship's new value would start it 0.083 m/s off, and give its first
    /// step 5 m/s².
    /// </summary>
    [Fact]
    public void ANewFollowingFrameReadsItsAnchorsAccelerationWithTheParentsProperAccelerationAsTheStepThatEndedFoundIt()
    {
        FrameHandle space = frames.Create(new Entity(20, 1), FrameHandle.Null, DPose.Identity, FrameBounds.Sphere(1e9f), GravityField.None);
        FrameHandle ship = frames.Create(new Entity(21, 1), space, DPose.Identity, FrameBounds.Sphere(1e6f));
        Double3 place = new(1_000, 0, 0), velocity = new(0, 0, 2_000);
        FrameHandle station = frames.Create(new Entity(22, 1), ship, new DPose(place - velocity * Step, DQuaternion.Identity), FrameBounds.Sphere(40));
        frames.SetHullMotion(station, BodyMotion.Kinematic);
        frames.SetTreeLinearVelocity(station, velocity);
        var thrust = new Double3(10, 0, 0);
        var shipRecord = new MotionSample(new DPose(thrust * (Step * Step), DQuaternion.Identity), thrust * Step, thrust * Step, thrust, Double3.Zero, Double3.Zero);
        var record = new MotionSample(new DPose(place, DQuaternion.Identity), velocity, velocity, Double3.Zero, Double3.Zero, Double3.Zero);
        // The ship has no hull, so it takes its owner's full sample, and physics released the record of the station's hull.
        var intents = new PhysicsIntent[station.Entity.Index + 1];
        intents[ship.Entity.Index] = new PhysicsIntent { Frame = space, Move = new MotionIntent(shipRecord) };
        intents[station.Entity.Index] = new PhysicsIntent { Frame = ship, Move = new MotionIntent(new EndPose(record.Pose)) };
        frames.TakeStepRecords(intents, Step, new StepOwners().WithHullRecord(station, record));
        frames.ProperAcceleration(ship).Should().Be(thrust.ToVector3(), "the ship's value changes at this step 2");
        frames.ProperAcceleration(station).Should().Be(Vector3.Zero, "the anchor kept a steady velocity in a ship whose value was zero in the step");

        FrameHandle orbit = Update(Hull(station))[0].Home;
        frames.Update();
        frames.BeginNumericStep(Moving(station, new MotionIntent(new EndPose(new DPose(place + velocity * Step, DQuaternion.Identity)))), Step);

        frames.Numeric(orbit).Should().Be(NumericMode.Following);
        frames.LinearVelocity(orbit).Should().Be(velocity, "the anchor's velocity is the chord of its end poses");
        frames.HomeAcceleration(orbit, ship).Length.Should().BeLessThan(1e-9, "an end pose at a steady velocity has no acceleration");
    }

    /// <summary>
    /// A frame created after step 2 in the slot of a destroyed frame has no proper acceleration as the step found it, as it
    /// has none in its row. So a new fast hull in it, which starts as one that fell freely, starts its following frame with
    /// its own velocity in the empty space of the new frame, and not with the thrust of the frame that died.
    /// </summary>
    [Fact]
    public void AFrameInTheSlotOfADestroyedFrameKeepsNoProperAccelerationOfTheDeadFrameAsTheStepFoundIt()
    {
        FrameHandle space = frames.Create(new Entity(20, 1), FrameHandle.Null, DPose.Identity, FrameBounds.Sphere(1e9f), GravityField.None);
        FrameHandle dead = frames.Create(new Entity(21, 1), space, DPose.Identity, FrameBounds.Sphere(1e6f));
        var thrust = new Double3(10, 0, 0);
        var record = new MotionSample(DPose.Identity, Double3.Zero, Double3.Zero, thrust, Double3.Zero, Double3.Zero);
        var intents = new PhysicsIntent[dead.Entity.Index + 1];
        intents[dead.Entity.Index] = new PhysicsIntent { Frame = space, Move = new MotionIntent(record) };
        for (int step = 0; step < 2; step++)
            frames.TakeStepRecords(intents, Step);
        frames.Destroy(dead);
        FrameHandle ship = frames.Create(new Entity(22, 1), space, DPose.Identity, FrameBounds.Sphere(1e6f));
        ship.Index.Should().Be(dead.Index, "the new frame takes the slot of the dead one");
        var velocity = new Double3(0, 0, 2_000);
        FrameHandle station = frames.Create(new Entity(23, 1), ship, new DPose(new Double3(1_000, 0, 0), DQuaternion.Identity), FrameBounds.Sphere(40));
        frames.SetHullMotion(station, BodyMotion.Kinematic);
        frames.SetTreeLinearVelocity(station, velocity);

        FrameHandle orbit = Update(Hull(station))[0].Home;

        frames.Numeric(orbit).Should().Be(NumericMode.Following);
        frames.LinearVelocity(orbit).Should().Be(velocity, "nothing pulls or pushes in the new ship");
    }

    [Fact]
    public void A_far_kinematic_hull_that_turns_in_place_gets_an_orbit_frame_as_any_kinematic_hull_does()
    {
        // 10,000 km out, past the create distance of 2^23 m (about 8,389 km), a hull that turns in place is slow but far.
        FrameHandle station = Station(10, Far);
        frames.SetTreeLinearVelocity(station, Double3.Zero);

        FrameHandle orbit = Update(Hull(station) with { Velocity = Double3.Zero })[0].Home;

        frames.Numeric(orbit).Should().Be(NumericMode.Following);
        frames.InFollowingFrame(station).Should().BeTrue();
    }

    [Fact]
    public void An_orbit_frame_jumps_with_its_anchors_teleport_and_its_other_contents_keep_their_places_and_velocities_in_earth()
    {
        FrameHandle station = Station(10, Low);
        FrameHandle pod = frames.Create(new Entity(11, 1), earth, DPose.Identity, FrameBounds.Sphere(1));
        frames.SetHullMotion(pod, BodyMotion.Dynamic);
        (Double3 place, Double3 velocity) = StateAt(Low, Start);
        frames.SetLocalPose(pod, new DPose(place + new Double3(5, 0, 0), DQuaternion.Identity));
        frames.SetTreeLinearVelocity(pod, velocity + new Double3(0, 1, 0));
        NumericBody podHull = Crate(11, place + new Double3(5, 0, 0), velocity + new Double3(0, 1, 0)) with { OwnedFrame = pod };
        NumericBody[] after = Update(Hull(station), podHull);
        FrameHandle orbit = after[0].Home;
        after[1].Home.Should().Be(orbit, "the pod's hull joins the orbit frame");
        DPose podInEarth = frames.LocalPose(pod);
        Double3 podVelocity = frames.LinearVelocity(pod);
        // The hull teleported 1 km on along its orbit in the step, so physics released its record 1 km from the origin.
        MotionSample sample = Low.Sample(EarthMu, Start + 0.13, Step);
        PhysicsIntent[] intents = Moving(station, new MotionIntent(sample), teleport: sample.Pose);
        frames.Update();
        frames.BeginNumericStep(intents, Step);
        Double3 offset = sample.Pose.Position - frames.TreeLocalPose(orbit).Position;
        Double3 change = sample.ChordVelocity - frames.LinearVelocity(orbit);
        MotionSample record = AtOrigin(sample) with { Pose = new DPose(offset, sample.Pose.Rotation) };
        // Physics released the pod's record where the pod stands, with its velocity in the orbit frame.
        Double3 podTreeVelocity = frames.TreeLinearVelocity(pod);
        var podRecord = new MotionSample(frames.TreeLocalPose(pod), podTreeVelocity, podTreeVelocity, Double3.Zero,
            (Double3)frames.AngularVelocity(pod), Double3.Zero);
        frames.TakeStepRecords(intents, Step, new StepOwners().WithHullRecord(station, record).WithHullRecord(pod, podRecord));
        changes.Clear();

        frames.JumpFollowingFrame(frames.TreeParent(station), record, changes);

        frames.TreeLocalPose(orbit).Position.Should().Be(sample.Pose.Position);
        frames.LinearVelocity(orbit).Should().Be(sample.ChordVelocity);
        frames.TreeLocalPose(station).Should().Be(new DPose(Double3.Zero, sample.Pose.Rotation));
        (frames.LocalPose(pod).Position - podInEarth.Position).Length.Should().BeLessThan(1e-6, "the pod keeps its place in earth");
        (frames.LinearVelocity(pod) - podVelocity).Length.Should().BeLessThan(1e-9, "the pod keeps its velocity in earth");
        changes.FramesShifted.Should().Equal(new FrameShift(orbit, offset, change));
        frames.AssertInvariants();
    }

    [Fact]
    public void A_crate_near_an_orbiting_hull_joins_its_orbit_frame()
    {
        FrameHandle station = Station(10, Low);
        FrameHandle orbit = Update(Hull(station))[0].Home;
        (Double3 place, Double3 velocity) = StateAt(Low, Start);
        Double3 start = frames.LinearVelocity(orbit);

        // The merge test reaches the radii of 5 m and 1 m, and 2 s of the crate's 1 m/s relative to the hull.
        NumericBody[] after = Update(Hull(station), Crate(11, place + new Double3(7, 0, 0), velocity + new Double3(1, 0, 0)));

        after[1].Home.Should().Be(orbit);
        frames.NumericCount.Should().Be(1);
        frames.LinearVelocity(orbit).Should().Be(start, "an orbit frame never drains");
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
    public void A_slow_near_kinematic_hull_stays_in_its_semantic_frames_own_system_and_takes_a_fast_body_there()
    {
        // A circular orbit of 1 km in a field of 1e-9 m³/s² moves at 1 µm/s. The fast body is a character, which always
        // thrusts, so at 60 m/s it would fall in a frame of its own.
        FrameHandle space = frames.Create(new Entity(2, 1), FrameHandle.Null, DPose.Identity, FrameBounds.Sphere(1e7f),
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
        Sync(station, Far, Start + 3);
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

        frames.Numeric(after[0].Home).Should().Be(NumericMode.Following);
        frames.Numeric(after[1].Home).Should().Be(NumericMode.Following);
        after[1].Home.Should().NotBe(after[0].Home, "each hull rests in an orbit frame of its own");
        after[2].Home.Should().Be(after[0].Home, "the group takes the orbit frame of its first hull");
        frames.AnchorGhosts.ToArray().Should().Equal(new NumericGhost(second.Entity, after[0].Home));
    }

    [Fact]
    public void A_hull_whose_orbit_no_longer_needs_a_frame_goes_back_with_its_frame_and_its_orbit_frame_goes_away()
    {
        // 10,000 km out in a field of 1e-9 m³/s², the hull is slow but far: past the create distance of 2^23 m (about
        // 8,389 km), so it lives in an orbit frame. Then it moves to an orbit of 1 km, within the leave distance of 2^22 m.
        FrameHandle space = frames.Create(new Entity(2, 1), FrameHandle.Null, DPose.Identity, FrameBounds.Sphere(1e8f),
            GravityField.Point(1e-9));
        FrameHandle station = Station(10, KeplerOrbit.Circular(1e7), space);
        FrameHandle orbit = Update(Hull(station))[0].Home;
        frames.Numeric(orbit).Should().Be(NumericMode.Following);

        KeplerOrbit near = KeplerOrbit.Circular(1_000);
        Sync(station, near, Start);
        NumericBody[] after = Update(Hull(station));

        after[0].Home.Should().Be(space);
        frames.TreeParent(station).Should().Be(space);
        frames.IsAlive(orbit).Should().BeFalse("an orbit frame that its frame leaves falls, and one without bodies goes");
        (frames.LocalPose(station).Position - near.StateAt(Start, 1e-9).Position).Length.Should().BeLessThan(1e-9);
    }

    [Fact]
    public void A_next_step_motion_that_is_not_kinematic_leaves_the_orbit_frame_falling()
    {
        FrameHandle station = Station(10, Low);
        FrameHandle orbit = Update(Hull(station))[0].Home;
        Double3 velocity = frames.LinearVelocity(orbit);

        frames.PlanHullMotion(station, BodyMotion.Dynamic);

        frames.Numeric(orbit).Should().Be(NumericMode.Falling, "a frame with a dynamic hull may hang in a falling frame");
        frames.InFollowingFrame(station).Should().BeFalse();
        frames.LinearVelocity(orbit).Should().Be(velocity, "it falls on from where it was");
        frames.TreeParent(station).Should().Be(orbit);
        frames.HullMotion(station).Should().Be(BodyMotion.Kinematic, "the stored motion changes only after the next step");
        frames.AssertInvariants(StepIntents.Changing(station, BodyMotion.Dynamic));
    }

    // The World skips its second pass over the owner views of a sync point while the owner view stamp has not changed
    // since the first (see FrameTree.OwnerViewStamp). The change of motion comes between the two passes, at step 4. An
    // owner view reads the pose, the velocities and the numeric frames above the frame, and an orbit frame that falls is
    // still a numeric frame, so the skip is right only while the station's owner view stays as it was.
    [Fact]
    public void A_hull_that_leaves_its_orbit_frame_changes_no_owner_view_and_leaves_the_owner_view_stamp_as_it_is()
    {
        FrameHandle station = Station(10, Low);
        Update(Hull(station));
        frames.Update();
        FrameOwnerView before = frames.OwnerView(station);
        long stamp = frames.OwnerViewStamp;

        frames.PlanHullMotion(station, BodyMotion.Dynamic);

        frames.InFollowingFrame(station).Should().BeFalse();
        frames.OwnerView(station).Should().Be(before);
        frames.OwnerViewStamp.Should().Be(stamp);
    }

    [Fact]
    public void A_kinematic_next_step_motion_keeps_the_frame_in_its_orbit_frame()
    {
        FrameHandle station = Station(10, Low);
        FrameHandle orbit = Update(Hull(station))[0].Home;

        frames.PlanHullMotion(station, BodyMotion.Kinematic);

        frames.Numeric(orbit).Should().Be(NumericMode.Following);
        frames.InFollowingFrame(station).Should().BeTrue();
        frames.AssertInvariants();
    }

    /// <summary>
    /// The stored motion describes the last finished step, and the orbit frame was planned for the step that applies the
    /// intents. So storing a motion changes nothing else: the frame left its orbit frame when the change was planned.
    /// </summary>
    [Fact]
    public void Storing_a_hull_motion_leaves_the_orbit_frame_as_it_is()
    {
        FrameHandle station = Station(10, Low);
        FrameHandle orbit = Update(Hull(station))[0].Home;

        frames.SetHullMotion(station, BodyMotion.Dynamic);

        frames.Numeric(orbit).Should().Be(NumericMode.Following);
        frames.InFollowingFrame(station).Should().BeTrue();
    }

    /// <summary>
    /// The invariants judge the numeric frames by the motion of the step that applies the intents they are given: the
    /// frame of a hull that is dynamic in that step may hang in a falling frame, although its stored motion is kinematic.
    /// </summary>
    [Fact]
    public void The_invariants_judge_a_frame_in_a_falling_frame_by_the_motion_of_the_step_that_applies_the_intents()
    {
        FrameHandle station = Station(10, Low);
        Update(Hull(station));
        frames.PlanHullMotion(station, BodyMotion.Dynamic);

        frames.Invoking(tree => tree.AssertInvariants(StepIntents.Changing(station, BodyMotion.Dynamic))).Should().NotThrow();
        frames.Invoking(tree => tree.AssertInvariants()).Should().Throw<ContractException>("without the change the hull is kinematic, and so cannot hang in a falling frame");
    }

    [Fact]
    public void A_frame_away_from_the_origin_of_its_orbit_frame_breaks_an_invariant()
    {
        FrameHandle station = Station(10, Low);
        FrameHandle orbit = Update(Hull(station))[0].Home;
        frames.InFollowingFrame(station).Should().BeTrue();

        frames.SetTreeLocalPose(station, new DPose(new Double3(1, 0, 0), frames.TreeLocalPose(station).Rotation));

        frames.Invoking(tree => tree.AssertInvariants()).Should().Throw<ContractException>();
        frames.Numeric(orbit).Should().Be(NumericMode.Following);
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
        frames.InFollowingFrame(station).Should().BeFalse();
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
        frames.InFollowingFrame(station).Should().BeTrue("the hull's own frame did not change");
    }

    [Fact]
    public void Destroying_the_semantic_frame_of_an_orbit_frame_dissolves_it_and_drops_the_ghosts_of_its_hull()
    {
        // Earth is a semantic child of the sun, which has no gravity of its own here.
        FrameHandle sun = frames.Create(new Entity(3, 1), FrameHandle.Null, DPose.Identity, FrameBounds.Sphere(1e9f));
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

        frames.Numeric(after[0].Home).Should().Be(NumericMode.Following);
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
