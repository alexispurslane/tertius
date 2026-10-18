using System.Numerics;
using Tertius.Frames.Rails;
using Tertius.Simulation;
using static Tertius.Testing.WorldTesting;

namespace Tertius.Physics.Tests;

/// <summary>
/// Frames follow their owners' step records (docs/plans/rails-components.md, "One motion record per frame", "The sync
/// point" and "Physics"), on the World and the real backend. A rails component writes a motion sample for each step. A
/// frame without a body takes its sample at the sync point after the step; physics aims a kinematic hull at its sample,
/// snaps it onto the sample's pose after the Jolt update, and releases the sample as the frame's record; and physics
/// predicts the frame's ghosts and hand-offs from the record. A new frame on rails holds still for its first step, so it
/// stands on its rails from the second sync point after its spawn.
/// <para>
/// In this ticket the Kepler driver still makes a hull kinematic. The tree still keeps the orbit of the frame spec, which
/// nothing follows any more: these tests give it another orbit, <see cref="Decoy"/>, so that only the component can put
/// the frame where the test expects it. A frame without a body needs no driver, so it has the Static driver.
/// </para>
/// </summary>
public class RailsFollowTests
{
    /// <summary>Ten minutes of ticks at 60 Hz.</summary>
    private const int TenMinutes = 36_000;

    private const double Mu = Scenes.AsteroidMu;

    /// <summary>
    /// A slow orbit of 400 km in an <see cref="Scenes.AsteroidSpace"/>, at 224 m/s, on which a hull stays in the system of
    /// space: it is slower than 1,000 m/s and nearer than 2^23 m. The frame turns at 0.2 rad/s.
    /// </summary>
    private static readonly KeplerOrbit Slow = KeplerOrbit.Circular(4e5, phase: 0.3, spinRate: 0.2);

    /// <summary>A fast orbit of 5 km at 2 km/s, on which a hull lives in an orbit frame of its own. The frame turns at 0.2 rad/s.</summary>
    private static readonly KeplerOrbit Fast = KeplerOrbit.Circular(5_000, phase: 0.3, spinRate: 0.2);

    /// <summary>A fast orbit of 5 km at 2 km/s, whose frame does not turn, so that a body at rest in it moves with its origin.</summary>
    private static readonly KeplerOrbit FastStill = KeplerOrbit.Circular(5_000, phase: 0.3);

    /// <summary>The orbit that the frame spec of a hull names: the same orbit 1 rad further on, which nothing follows any more.</summary>
    private static KeplerOrbit Decoy(KeplerOrbit orbit) => orbit with { MeanAnomalyAtEpoch = orbit.MeanAnomalyAtEpoch + 1 };

    /// <summary>The owner of a frame on <paramref name="orbit"/> in <paramref name="space"/>, whose hull is a ball of 0.5 m.</summary>
    private static Entity Hull(World world, FrameHandle space, KeplerOrbit orbit) => world.Spawn(new SpawnDesc
    {
        Frame = space,
        Body = Ball(),
        AsFrame = new FrameSpec(FrameBounds.Sphere(0.01f), MotionDriver.Kepler),
    }.OnKeplerOrbit(world, orbit));

    /// <summary>The owner of a frame without a body on <paramref name="orbit"/> in <paramref name="space"/>, whose bubble has a radius of <paramref name="bubble"/> m.</summary>
    private static Entity Bodyless(World world, FrameHandle space, KeplerOrbit orbit, float bubble = 5) => world.Spawn(new SpawnDesc
    {
        Frame = space,
        AsFrame = new FrameSpec(FrameBounds.Sphere(bubble), MotionDriver.Static),
    }.OnKeplerOrbit(world, orbit));

    private static Entity Owner(World world, FrameHandle space, KeplerOrbit orbit, bool withBody) =>
        withBody ? Hull(world, space, orbit) : Bodyless(world, space, orbit);

    /// <summary>The pose of <paramref name="orbit"/> at <paramref name="time"/> in its parent.</summary>
    private static DPose RailsPose(KeplerOrbit orbit, double time) => new(orbit.StateAt(time, Mu).Position, orbit.RotationAt(time, Mu));

    /// <summary>
    /// Ticks for ten minutes, and checks at every sync point from the second one after the spawn that <paramref name="owner"/>
    /// stands in <paramref name="space"/> at the pose of <paramref name="orbit"/> at the world's time, to the last bit.
    /// </summary>
    private static void AssertOnItsOrbitForTenMinutes(World world, Entity owner, FrameHandle space, KeplerOrbit orbit, Action? eachSyncPoint = null)
    {
        world.Tick();
        for (int tick = 1; tick < TenMinutes; tick++)
        {
            world.Tick();
            DPose pose = world.PoseIn(owner, space);
            DPose expected = RailsPose(orbit, world.Time);
            if (pose.Position != expected.Position || pose.Rotation != expected.Rotation)
                pose.Should().Be(expected, "at sync point {0} the frame stands on its orbit", world.TickNumber);
            eachSyncPoint?.Invoke();
        }
        world.Errors.Should().BeEmpty();
    }

    // ---- New test 2: on the rails to the last bit -----------------------------------------------------------------

    [Fact]
    public void AFrameWithoutABodyOnRailsStandsExactlyOnItsOrbitAtEverySyncPointFromTheSecondAfterItsSpawn()
    {
        using World world = NewWorld(out _);
        FrameHandle space = Scenes.AsteroidSpace(world);
        Entity owner = Bodyless(world, space, Fast);

        AssertOnItsOrbitForTenMinutes(world, owner, space, Fast);
    }

    [Fact]
    public void AKinematicHullOnRailsInItsSemanticParentStandsExactlyOnItsOrbitAtEverySyncPointFromTheSecondAfterItsSpawn()
    {
        using World world = NewWorld(out _);
        FrameHandle space = Scenes.AsteroidSpace(world);
        Entity owner = Hull(world, space, Slow);

        AssertOnItsOrbitForTenMinutes(world, owner, space, Slow, () =>
        {
            if (world.PhysicsHomeOf(owner) != space)
                world.PhysicsHomeOf(owner).Should().Be(space, "a slow hull stays in the system of space");
        });
    }

    [Fact]
    public void AFastKinematicHullStandsExactlyOnItsOrbitInsideItsOrbitFrameAtEverySyncPointFromTheSecondAfterItsSpawn()
    {
        using World world = NewWorld(out _);
        FrameHandle space = Scenes.AsteroidSpace(world);
        Entity owner = Hull(world, space, Fast);

        AssertOnItsOrbitForTenMinutes(world, owner, space, Fast, () =>
        {
            FrameHandle home = world.PhysicsHomeOf(owner);
            if (world.Frames.Numeric(home) != NumericMode.Orbiting)
                world.Frames.Numeric(home).Should().Be(NumericMode.Orbiting, "a fast hull lives in an orbit frame of its own");
        });
    }

    [Fact]
    public void TheAnchorOfAnOrbitFrameSitsAtItsOriginWithTheSamplesVelocityLessTheChordSoAReadInItsParentGivesTheSamplesVelocity()
    {
        using World world = NewWorldWithFaults(out FaultInjectingBackend faults);
        FrameHandle space = Scenes.AsteroidSpace(world);
        Entity owner = Hull(world, space, Fast);
        FrameHandle frame = world.GetOwnedFrame(owner);

        Tick(world, 5);

        MotionSample sample = faults.Samples.Single(record => record.Frame == frame && record.Tick == world.TickNumber - 1).Sample;
        FrameHandle orbitFrame = world.Frames.TreeParent(frame);
        world.Frames.Numeric(orbitFrame).Should().Be(NumericMode.Orbiting);
        world.Frames.TreeLocalPose(orbitFrame).Position.Should().Be(sample.Pose.Position);
        world.Frames.LinearVelocity(orbitFrame).Should().Be(sample.ChordVelocity);
        world.Frames.TreeLocalPose(frame).Should().Be(new DPose(Double3.Zero, sample.Pose.Rotation));
        world.Frames.TreeLinearVelocity(frame).Should().Be(sample.Velocity - sample.ChordVelocity);
        (world.Frames.LinearVelocity(frame) - sample.Velocity).Length.Should().BeLessThan(1e-9, "a read in space is the sample's instant velocity");
        world.Frames.AngularVelocity(frame).Should().Be(sample.AngularVelocity.ToVector3());
        world.Errors.Should().BeEmpty();
    }

    // ---- New test 3: the snap -------------------------------------------------------------------------------------

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void AfterEachStepAHullOnRailsSitsInJoltExactlyOnItsTarget(bool fast)
    {
        using World world = NewWorldWithFaults(out FaultInjectingBackend faults);
        FrameHandle space = Scenes.AsteroidSpace(world);
        Entity owner = Hull(world, space, fast ? Fast : Slow);
        FrameHandle frame = world.GetOwnedFrame(owner);
        // Step 1 teleports the hull onto its rails. From step 2 on, a fast hull rests at the origin of its orbit frame.
        Tick(world, 3);

        for (int i = 0; i < 120; i++)
        {
            world.Tick();
            long step = world.TickNumber - 1;
            MotionSample sample = faults.Samples.Single(record => record.Frame == frame && record.Tick == step).Sample;
            BodyState hull = faults.Outputs[^1].Bodies.Single(state => state.Entity == owner);
            // The target of a hull at the origin of its orbit frame is that origin; any other target is the sample's place.
            Double3 target = fast ? Double3.Zero : sample.Pose.Position;
            if (hull.Pose.Position != target)
                hull.Pose.Position.Should().Be(target, "after step {0} the hull sits on its target", step);
            AngleBetween(hull.Pose.Rotation, sample.Pose.Rotation).Should().BeLessThan(1e-6, "Jolt keeps the rotation as a float");
        }
        world.Errors.Should().BeEmpty();
    }

    // ---- New test 4: ghosts and hand-offs of a frame on rails -------------------------------------------------------

    [Fact]
    public void ABodyThatSticksOutOfAFrameOnRailsHasAGhostWithTheSamplesChordVelocity()
    {
        using World world = NewWorldWithFaults(out FaultInjectingBackend faults);
        FrameHandle space = Scenes.AsteroidSpace(world);
        FrameHandle frame = world.GetOwnedFrame(Bodyless(world, space, FastStill));
        // A ball of 1 m whose center is 4.5 m from the frame's origin sticks out of the bubble of 5 m. The frame has no
        // gravity and does not turn, so the ball rests in it, and its ghost in space moves with the frame's origin.
        Entity ball = world.Spawn(new SpawnDesc { Frame = frame, Pose = At(0, 4.5), Body = Ball(1) });

        Tick(world, 10);

        MotionSample sample = faults.Samples.Last(record => record.Frame == frame).Sample;
        JoltPhysicsBackend.BodyFacts ghost = faults.Jolt.BodiesIn(space)
            .Single(facts => BodyUserData.Kind(facts.UserData) == BodyKind.Ghost && BodyUserData.Names(facts.UserData, ball));
        ((Double3)ghost.LinearVelocity - sample.ChordVelocity).Length.Should().BeLessThan(1e-3);
        (sample.Velocity - sample.ChordVelocity).Length.Should().BeGreaterThan(5, "space pulls the frame at 800 m/s², so the instant velocity is 6.7 m/s past the chord");
        world.Errors.Should().BeEmpty();
    }

    [Fact]
    public void ABodyThatLeavesTheBubbleOfAFrameOnRailsHandsOffWithTheSamplesInstantVelocity()
    {
        using World world = NewWorldWithFaults(out FaultInjectingBackend faults);
        FrameHandle space = Scenes.AsteroidSpace(world);
        FrameHandle frame = world.GetOwnedFrame(Bodyless(world, space, FastStill));
        // The ball drifts out along +Z at 2 m/s in a frame without gravity that does not turn.
        Entity ball = world.Spawn(new SpawnDesc { Frame = frame, Pose = At(0, 0, 3), LinearVelocity = new Double3(0, 0, 2), Body = Ball(0.2f) });

        HandOff? exit = null;
        DPose localBefore = default;
        Double3 velocityBefore = default;
        for (int i = 0; i < 200 && exit is null; i++)
        {
            localBefore = world.PoseInOwnFrame(ball);
            velocityBefore = world.VelocityInOwnFrame(ball);
            world.Tick();
            exit = faults.Outputs[^1].HandOffs.Where(handOff => handOff.Entity == ball).Cast<HandOff?>().SingleOrDefault();
        }

        exit.Should().NotBeNull();
        exit!.Value.From.Should().Be(frame);
        exit.Value.To.Should().Be(space);
        MotionSample sample = faults.Samples.Single(record => record.Frame == frame && record.Tick == world.TickNumber - 1).Sample;
        // The ball's instant velocity in space at the end of the step: the frame's instant velocity plus the ball's own.
        // Jolt gets the chord of it in space: half a step of space's gravity there behind it.
        double dt = world.DeltaTime;
        Double3 place = sample.Pose.TransformPoint(localBefore.Position + velocityBefore * dt);
        Double3 instant = sample.Velocity + sample.Pose.TransformDirection(velocityBefore);
        Double3 expected = instant - GravityField.Point(Mu).AtPrecise(place) * (dt / 2);
        ((Double3)exit.Value.LinearVelocity - expected).Length.Should().BeLessThan(0.05);
        Double3 fromChord = sample.ChordVelocity + sample.Pose.TransformDirection(velocityBefore) - GravityField.Point(Mu).AtPrecise(place) * (dt / 2);
        ((Double3)exit.Value.LinearVelocity - fromChord).Length.Should().BeGreaterThan(5, "the chord of the frame would be 6.7 m/s off");
        world.Errors.Should().BeEmpty();
    }

    // ---- New test 5: the start of a frame on rails and a swap of components ----------------------------------------

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ANewFrameOnRailsHoldsStillAtItsSpawnPoseForItsFirstStep(bool withBody)
    {
        using World world = NewWorldWithFaults(out FaultInjectingBackend faults);
        FrameHandle space = Scenes.AsteroidSpace(world);
        DPose spawnPose = RailsPose(Slow, world.Time);
        // The frame spec names the component's orbit, which the Kepler driver would follow at once.
        Entity owner = world.Spawn(new SpawnDesc
        {
            Frame = space,
            Body = withBody ? Ball() : null,
            AsFrame = new FrameSpec(FrameBounds.Sphere(0.01f), MotionDriver.Kepler),
        }.OnKeplerOrbit(world, Slow));

        world.Tick();

        world.PoseIn(owner, space).Should().Be(spawnPose);
        world.VelocityIn(owner, space).Should().Be(Double3.Zero, "a frame without a step record holds still");
        faults.Outputs[^1].Bodies.Where(state => state.Entity == owner).Should().OnlyContain(state => state.Pose == spawnPose,
            "the hull holds still in Jolt too");
        world.Errors.Should().BeEmpty();
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ANewFrameOnRailsStandsOnItsRailsAtTheSecondSyncPointAfterItsSpawn(bool withBody)
    {
        using World world = NewWorld(out _);
        FrameHandle space = Scenes.AsteroidSpace(world);
        Entity owner = withBody ? Hull(world, space, Slow) : Bodyless(world, space, Slow);

        Tick(world, 2);

        world.PoseIn(owner, space).Should().Be(RailsPose(Slow, world.Time));
        world.Errors.Should().BeEmpty();
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ASwapOfRailsComponentsLeavesNoStepWithoutASample(bool withBody)
    {
        using World world = NewWorldWithFaults(out FaultInjectingBackend faults);
        FrameHandle space = Scenes.AsteroidSpace(world);
        Entity owner = Owner(world, space, Slow, withBody);
        FrameHandle frame = world.GetOwnedFrame(owner);
        Tick(world, 3);
        KeplerOrbit next = Slow with { MeanAnomalyAtEpoch = Slow.MeanAnomalyAtEpoch + 0.5 };

        world.RemoveComponent(owner, world.GetComponent<KeplerMotion>(owner)!);
        world.AddComponent(owner, new KeplerMotion { Orbit = next });

        // The old component's last sample moves the frame in the step of the next tick, and the new component's first
        // sample in the step after it.
        world.Tick();
        world.PoseIn(owner, space).Should().Be(RailsPose(Slow, world.Time));
        world.Tick();
        world.PoseIn(owner, space).Should().Be(RailsPose(next, world.Time));
        world.Tick();
        world.PoseIn(owner, space).Should().Be(RailsPose(next, world.Time));
        faults.Samples.Where(record => record.Frame == frame).Select(record => record.Tick).Should().Equal(1, 2, 3, 4, 5);
        world.Errors.Should().BeEmpty();
    }

    [Fact]
    public void AFastHullWhoseRailsChangeJumpsWithItsOrbitFrameAndACrateInTheOrbitFrameKeepsItsPlaceInSpace()
    {
        using World world = NewWorld(out _);
        FrameHandle space = Scenes.AsteroidSpace(world);
        Entity owner = Hull(world, space, Fast);
        // A crate 3 m from the hull, on the hull's orbit, joins the hull's orbit frame.
        (Double3 place, Double3 velocity) = Fast.StateAt(world.Time, Mu);
        Entity crate = world.Spawn(new SpawnDesc { Frame = space, Pose = new DPose(place + new Double3(0, 3, 0), DQuaternion.Identity), LinearVelocity = velocity, Body = Cube() });
        Tick(world, 5);
        world.PhysicsHomeOf(crate).Should().Be(world.PhysicsHomeOf(owner), "the crate joined the hull's orbit frame");
        KeplerOrbit next = Fast with { MeanAnomalyAtEpoch = Fast.MeanAnomalyAtEpoch + 1 };
        Double3 crateBefore = world.PoseIn(crate, space).Position;

        // The new component teleports the hull 4.8 km on, onto its new rails, in the step after the next tick.
        world.RemoveComponent(owner, world.GetComponent<KeplerMotion>(owner)!);
        world.AddComponent(owner, new KeplerMotion { Orbit = next });
        Tick(world, 2);

        world.PoseIn(owner, space).Should().Be(RailsPose(next, world.Time));
        world.Frames.Numeric(world.PhysicsHomeOf(owner)).Should().Be(NumericMode.Orbiting);
        // The crate flew on at 2 km/s for two steps; it stayed out of the hull's jump of 4.8 km.
        (world.PoseIn(crate, space).Position - crateBefore).Length.Should().BeLessThan(100);
        world.Errors.Should().BeEmpty();
    }

    [Fact]
    public void AFarSpinHullLivesInAnOrbitFrameOfItsOwnAsAnyFarKinematicHullDoes()
    {
        using World world = NewWorld(out _);
        FrameHandle space = world.CreateRootFrame(new FrameSpec(FrameBounds.Sphere(1e8f), MotionDriver.Static));
        // 10,000 km from the origin, past the 2^23 m (8,389 km) from which a body gets a numeric frame.
        var turning = new FrameSpin(DQuaternion.Identity, 0.1);
        Entity owner = world.Spawn(new SpawnDesc
        {
            Frame = space,
            Pose = At(1e7),
            Body = Ball(),
            AsFrame = new FrameSpec(FrameBounds.Sphere(0.01f), MotionDriver.Spin),
        }.Spinning(world, turning));

        Tick(world, 3);

        world.Frames.Numeric(world.PhysicsHomeOf(owner)).Should().Be(NumericMode.Orbiting);
        world.PoseIn(owner, space).Should().Be(new DPose(new Double3(1e7, 0, 0), turning.RotationAt(world.Time)));
        world.Errors.Should().BeEmpty();
    }

    // ---- New test 7: samples that stop ----------------------------------------------------------------------------

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void AFrameOnRailsWhoseSamplesStopHoldsStillInItsParent(bool withBody)
    {
        using World world = NewWorldWithFaults(out FaultInjectingBackend faults);
        FrameHandle space = Scenes.AsteroidSpace(world);
        Entity owner = Owner(world, space, Slow, withBody);
        Tick(world, 3);
        world.RemoveComponent(owner, world.GetComponent<KeplerMotion>(owner)!);
        // The last sample moves the frame in the step of the next tick.
        world.Tick();
        DPose last = world.PoseIn(owner, space);
        last.Should().Be(RailsPose(Slow, world.Time));

        Tick(world, 3);

        world.PoseIn(owner, space).Should().Be(last);
        world.VelocityIn(owner, space).Should().Be(Double3.Zero);
        world.AngularVelocityIn(owner, space).Should().Be(Vector3.Zero);
        faults.Outputs[^1].Bodies.Where(state => state.Entity == owner).Should().OnlyContain(state => state.Pose.Position == last.Position,
            "the hull holds still in Jolt too");
        world.Errors.Should().BeEmpty();
    }

    [Fact]
    public void TheOrbitFrameOfAHullWhoseSamplesStopFallsOn()
    {
        using World world = NewWorld(out _);
        FrameHandle space = Scenes.AsteroidSpace(world);
        Entity owner = Hull(world, space, Fast);
        FrameHandle frame = world.GetOwnedFrame(owner);
        Tick(world, 5);
        FrameHandle orbitFrame = world.PhysicsHomeOf(owner);
        world.Frames.Numeric(orbitFrame).Should().Be(NumericMode.Orbiting);
        world.RemoveComponent(owner, world.GetComponent<KeplerMotion>(owner)!);
        world.Tick();
        Double3 place = world.Frames.TreeLocalPose(orbitFrame).Position;
        Double3 velocity = world.Frames.LinearVelocity(orbitFrame);
        place.Should().Be(RailsPose(Fast, world.Time).Position, "the last sample put the orbit frame on the orbit");

        double dt = world.DeltaTime;
        for (int i = 0; i < 10; i++)
        {
            world.Tick();
            // A falling frame steps as Jolt steps a body, with the pull of space at its origin.
            velocity += GravityField.Point(Mu).AtPrecise(place) * dt;
            place += velocity * dt;
        }

        world.PhysicsHomeOf(owner).Should().Be(orbitFrame, "the hull stays in its orbit frame");
        world.Frames.Numeric(orbitFrame).Should().Be(NumericMode.Orbiting, "the hull is still kinematic");
        (world.Frames.TreeLocalPose(orbitFrame).Position - place).Length.Should().BeLessThan(1e-6);
        world.Frames.TreeLocalPose(frame).Position.Should().Be(Double3.Zero, "the hull holds still at the orbit frame's origin");
        world.Frames.TreeLinearVelocity(frame).Should().Be(Double3.Zero);
        world.Errors.Should().BeEmpty();
    }

    // ---- Hulls without a sample, and what a hull's body state writes ------------------------------------------------

    [Fact]
    public void AKinematicHullWithoutASampleHoldsStillInItsFirstStep()
    {
        using World world = NewWorldWithFaults(out FaultInjectingBackend faults);
        FrameHandle space = Scenes.AsteroidSpace(world);
        // No component writes a sample, so nothing moves the hull, whatever its spawn velocity says.
        Entity owner = world.Spawn(new SpawnDesc
        {
            Frame = space,
            Pose = At(1000),
            LinearVelocity = new Double3(0, 0, 100),
            Body = Ball(),
            AsFrame = new FrameSpec(FrameBounds.Sphere(0.01f), MotionDriver.Kepler),
        });

        Tick(world, 3);

        world.PoseIn(owner, space).Should().Be(At(1000));
        faults.Outputs.SelectMany(output => output.Bodies).Where(state => state.Entity == owner)
            .Should().OnlyContain(state => state.Pose == At(1000));
        world.Errors.Should().BeEmpty();
    }

    [Fact]
    public void ABodyStateOfAHullNeverWritesItsOwnersRow()
    {
        using World world = NewWorldWithFaults(out FaultInjectingBackend faults);
        FrameHandle space = world.CreateRootFrame(new FrameSpec(FrameBounds.Sphere(1e6f), MotionDriver.Static));
        Scenes.ShipScene ship = Scenes.ShipWithInterior(world, space);
        // The body state of the hull says that the hull is far away; the frame's step record says where it is.
        faults.EditOutput = output =>
        {
            for (int i = 0; i < output.Bodies.Count; i++)
                if (output.Bodies[i].Entity == ship.Hull)
                    output.Bodies[i] = output.Bodies[i] with { Pose = At(5_000) };
        };

        Tick(world, 3);

        world.PoseIn(ship.Hull, space).Should().Be(world.Frames.LocalPose(ship.Interior));
        world.PoseIn(ship.Hull, space).Position.X.Should().BeLessThan(1);
        world.Errors.Should().BeEmpty();
    }

    // ---- One step record per frame ----------------------------------------------------------------------------------

    [Fact]
    public void AFrameWithADynamicHullTakesTheStepRecordThatPhysicsMeasured()
    {
        using World world = NewWorldWithFaults(out FaultInjectingBackend faults);
        const float g = 9.81f;
        FrameHandle space = Ground(world, g);
        Scenes.ShipScene ship = Scenes.ShipWithInterior(world, space, new Double3(0, 1000, 0));

        Tick(world, 5);

        MotionSample record = faults.Outputs[^1].Frames.Single(state => state.Frame == ship.Interior).Motion;
        world.Frames.TreeLocalPose(ship.Interior).Should().Be(record.Pose);
        world.Frames.TreeLinearVelocity(ship.Interior).Should().Be(record.Velocity, "the tree keeps the instant velocity");
        world.Frames.AngularVelocity(ship.Interior).Should().Be(record.AngularVelocity.ToVector3());
        world.Frames.ProperAcceleration(ship.Interior).Should().Be(record.ProperAcceleration);
        // The ship falls freely, so the step accelerates it by the gravity of space, and its instant velocity at the end of
        // the step is half a step of that past its chord, which Jolt keeps.
        var gravity = new Double3(0, -g, 0);
        (record.Acceleration - gravity).Length.Should().BeLessThan(1e-3);
        (record.Velocity - record.ChordVelocity - gravity * (world.DeltaTime / 2.0)).Length.Should().BeLessThan(1e-4);
        record.AngularAcceleration.Length.Should().BeLessThan(1e-3);
        world.Errors.Should().BeEmpty();
    }

    [Theory]
    [InlineData("a dynamic hull")]
    [InlineData("a kinematic hull on rails")]
    [InlineData("no body, on rails")]
    [InlineData("no body, and a transform write")]
    public void TheFollowedBufferHoldsTheStepRecordThatMovedTheFrame(string owner)
    {
        using World world = NewWorldWithFaults(out FaultInjectingBackend faults);
        FrameHandle space = Scenes.AsteroidSpace(world);
        Entity entity = owner switch
        {
            "a dynamic hull" => Scenes.ShipWithInterior(world, space, new Double3(1000, 0, 0)).Hull,
            "a kinematic hull on rails" => Hull(world, space, Slow),
            "no body, on rails" => Bodyless(world, space, Slow),
            _ => world.Spawn(new SpawnDesc
            {
                Frame = space,
                AsFrame = new FrameSpec(FrameBounds.Sphere(5), MotionDriver.Static),
                Components = [new Climber()],
            }),
        };
        FrameHandle frame = world.GetOwnedFrame(entity);

        Tick(world, 4);

        world.Frames.Samples.Followed.TryGet(frame, out MotionSample record, out FrameHandle relativeTo).Should().BeTrue();
        relativeTo.Should().Be(space, "a record in the table is relative to the frame's semantic parent");
        world.Frames.LocalPose(frame).Should().Be(record.Pose, "the frame stands where its record says");
        world.Errors.Should().BeEmpty();
    }

    [Fact]
    public void AGhostOfAFrameWithADynamicHullMovesWithTheRecordsChord()
    {
        using World world = NewWorldWithFaults(out FaultInjectingBackend faults);
        FrameHandle space = Ground(world, 9.81f);
        Scenes.ShipScene ship = Scenes.ShipWithInterior(world, space, new Double3(0, 1000, 0), bubbleRadius: 20);
        // A crate whose center is 19.8 m from the ship's origin sticks out of the bubble. The ship falls freely, so the
        // crate floats in it.
        Entity crate = world.Spawn(new SpawnDesc { Frame = ship.Interior, Pose = At(0, 19.8), Body = Cube() });

        Tick(world, 10);

        MotionSample record = faults.Outputs[^1].Frames.Single(state => state.Frame == ship.Interior).Motion;
        JoltPhysicsBackend.BodyFacts ghost = faults.Jolt.BodiesIn(space)
            .Single(facts => BodyUserData.Kind(facts.UserData) == BodyKind.Ghost && BodyUserData.Names(facts.UserData, crate));
        ((Double3)ghost.LinearVelocity - record.ChordVelocity).Length.Should().BeLessThan(1e-3);
        (record.Velocity - record.ChordVelocity).Length.Should().BeGreaterThan(0.05, "half a step of the gravity of space lies between them");
        world.Errors.Should().BeEmpty();
    }

    [Fact]
    public void ABodyThatLeavesTheBubbleOfAFrameWithADynamicHullHandsOffWithTheRecordsInstantVelocity()
    {
        using World world = NewWorldWithFaults(out FaultInjectingBackend faults);
        const float g = 9.81f;
        FrameHandle space = Ground(world, g);
        Scenes.ShipScene ship = Scenes.ShipWithInterior(world, space, new Double3(0, 1000, 0), bubbleRadius: 20);
        // The ball drifts out along +X at 2 m/s in the falling ship, where it floats.
        Entity ball = world.Spawn(new SpawnDesc { Frame = ship.Interior, Pose = At(17), LinearVelocity = new Double3(2, 0, 0), Body = Ball(0.2f) });

        HandOff? exit = null;
        Double3 velocityBefore = default;
        for (int i = 0; i < 200 && exit is null; i++)
        {
            velocityBefore = world.VelocityInOwnFrame(ball);
            world.Tick();
            exit = faults.Outputs[^1].HandOffs.Where(handOff => handOff.Entity == ball).Cast<HandOff?>().SingleOrDefault();
        }

        exit.Should().NotBeNull();
        exit!.Value.To.Should().Be(space);
        MotionSample record = faults.Outputs[^1].Frames.Single(state => state.Frame == ship.Interior).Motion;
        // The ball's instant velocity in space, of which Jolt gets the chord: half a step of the gravity of space behind it.
        var gravity = new Double3(0, -g, 0);
        Double3 expected = record.Velocity + record.Pose.TransformDirection(velocityBefore) - gravity * (world.DeltaTime / 2.0);
        ((Double3)exit.Value.LinearVelocity - expected).Length.Should().BeLessThan(1e-3);
        world.Errors.Should().BeEmpty();
    }

    // ---- Helpers ------------------------------------------------------------------------------------------------

    /// <summary>The angle in radians between two rotations.</summary>
    private static double AngleBetween(DQuaternion a, DQuaternion b)
    {
        double dot = Math.Abs(a.X * b.X + a.Y * b.Y + a.Z * b.Z + a.W * b.W) / Math.Sqrt(a.LengthSquared * b.LengthSquared);
        return 2 * Math.Acos(Math.Min(1, dot));
    }

    /// <summary>Writes its owner's transform 1 m higher in every tick.</summary>
    private sealed class Climber : IComponent
    {
        public int Priority => 0;

        public EventMask Wants => EventMask.Of<Tick>();

        public void Handle(Self self, scoped ref EventList events, scoped ref Outbox outbox)
        {
            DPose pose = self.PoseInOwnFrame();
            self.SetTransform(pose with { Position = pose.Position + new Double3(0, 1, 0) });
        }
    }
}
