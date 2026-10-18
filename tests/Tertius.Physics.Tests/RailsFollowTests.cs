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
/// The tree keeps no orbit, so only the component puts the frame where the test expects it. A hull that follows samples is
/// kinematic, and a frame without a body follows its samples with no hull.
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

    /// <summary>The owner of a frame on <paramref name="orbit"/> in <paramref name="space"/>, whose hull is a ball of 0.5 m.</summary>
    private static Entity Hull(World world, FrameHandle space, KeplerOrbit orbit) => world.Spawn(new SpawnDesc
    {
        Frame = space,
        Body = Ball() with { Motion = BodyMotion.Kinematic },
        AsFrame = new FrameSpec(FrameBounds.Sphere(0.01f)),
    }.OnKeplerOrbit(world, orbit));

    /// <summary>The owner of a frame without a body on <paramref name="orbit"/> in <paramref name="space"/>, whose bubble has a radius of <paramref name="bubble"/> m.</summary>
    private static Entity Bodyless(World world, FrameHandle space, KeplerOrbit orbit, float bubble = 5) => world.Spawn(new SpawnDesc
    {
        Frame = space,
        AsFrame = new FrameSpec(FrameBounds.Sphere(bubble)),
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
        // The spawn gives the frame the velocities of its orbit, which it does not take before its component's first sample.
        Entity owner = world.Spawn(new SpawnDesc
        {
            Frame = space,
            Body = withBody ? Ball() with { Motion = BodyMotion.Kinematic } : null,
            AsFrame = new FrameSpec(FrameBounds.Sphere(0.01f)),
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
        FrameHandle space = world.CreateRootFrame(new FrameSpec(FrameBounds.Sphere(1e8f)));
        // 10,000 km from the origin, past the 2^23 m (8,389 km) from which a body gets a numeric frame.
        var turning = new FrameSpin(DQuaternion.Identity, 0.1);
        Entity owner = world.Spawn(new SpawnDesc
        {
            Frame = space,
            Pose = At(1e7),
            Body = Ball() with { Motion = BodyMotion.Kinematic },
            AsFrame = new FrameSpec(FrameBounds.Sphere(0.01f)),
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
        Double3 gravity = default;
        for (int i = 0; i < 10; i++)
        {
            world.Tick();
            // A falling frame steps as Jolt steps a body, with the pull of space at its origin.
            gravity = GravityField.Point(Mu).AtPrecise(place);
            velocity += gravity * dt;
            place += velocity * dt;
        }

        world.PhysicsHomeOf(owner).Should().Be(orbitFrame, "the hull stays in its orbit frame");
        world.Frames.Numeric(orbitFrame).Should().Be(NumericMode.Orbiting, "the hull is still kinematic");
        (world.Frames.TreeLocalPose(orbitFrame).Position - place).Length.Should().BeLessThan(1e-6);
        world.Frames.TreeLocalPose(frame).Position.Should().Be(Double3.Zero, "the hull holds still at the orbit frame's origin");
        // It moves with the orbit frame, whose instant velocity is half a step of the step's pull past its chord.
        (world.Frames.TreeLinearVelocity(frame) - gravity * (dt / 2)).Length.Should().BeLessThan(1e-6);
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
            Body = Ball() with { Motion = BodyMotion.Kinematic },
            AsFrame = new FrameSpec(FrameBounds.Sphere(0.01f)),
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
        FrameHandle space = world.CreateRootFrame(new FrameSpec(FrameBounds.Sphere(1e6f)));
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
    [InlineData("a static hull that teleported")]
    public void TheFollowedBufferHoldsTheStepRecordThatMovedTheFrame(string owner)
    {
        using World world = NewWorldWithFaults(out FaultInjectingBackend faults);
        FrameHandle space = Scenes.AsteroidSpace(world);
        Entity entity = owner switch
        {
            "a dynamic hull" => Scenes.ShipWithInterior(world, space, new Double3(1000, 0, 0)).Hull,
            "a kinematic hull on rails" => Hull(world, space, Slow),
            "no body, on rails" => Bodyless(world, space, Slow),
            "a static hull that teleported" => world.Spawn(new SpawnDesc
            {
                Frame = space,
                Body = Ball() with { Motion = BodyMotion.Static },
                AsFrame = new FrameSpec(FrameBounds.Sphere(0.01f)),
                Components = [new Hopper()],
            }),
            _ => world.Spawn(new SpawnDesc
            {
                Frame = space,
                AsFrame = new FrameSpec(FrameBounds.Sphere(5)),
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

    // ---- Velocity reads in the semantic parent ----------------------------------------------------------------------
    //
    // Every kind of frame in the table "Who fills the step record" (docs/plans/rails-components.md, "One motion record per
    // frame") reads its instant velocity in its semantic parent. Under a numeric home the tree keeps a frame's velocity
    // relative to the home's frame velocity, the chord of the step, so a frame at rest in the home keeps the home's instant
    // velocity past its chord. Two kinds of frame live under a numeric home: a dynamic hull in a falling frame (see
    // DynamicHullRecordTests.AShipThatFallsInANumericFrameReadsTheInstantVelocityOfItsOrbitInEarth) and a kinematic hull at
    // the origin of its orbit frame, with a sample (see
    // TheAnchorOfAnOrbitFrameSitsAtItsOriginWithTheSamplesVelocityLessTheChordSoAReadInItsParentGivesTheSamplesVelocity) or
    // without one, below. A frame without a body, and a static hull, live in their semantic parent always.

    /// <summary>
    /// A fast hull that lives in its orbit frame, whose rails component <paramref name="world"/> removes after five ticks and
    /// replaces with <paramref name="next"/>, if any. From the second tick after that, the hull has no sample.
    /// </summary>
    private static (Entity Owner, FrameHandle OrbitFrame) FastHullWhoseSamplesStop(World world, FrameHandle space, IComponent? next = null)
    {
        Entity owner = Hull(world, space, FastStill);
        Tick(world, 5);
        FrameHandle orbitFrame = world.PhysicsHomeOf(owner);
        world.Frames.Numeric(orbitFrame).Should().Be(NumericMode.Orbiting);
        world.RemoveComponent(owner, world.GetComponent<KeplerMotion>(owner)!);
        if (next is not null)
            world.AddComponent(owner, next);
        // The last sample moves the frame in the step of the next tick.
        world.Tick();
        return (owner, orbitFrame);
    }

    /// <summary>
    /// Ticks once, and gives the instant velocity in space of the origin of <paramref name="orbitFrame"/> at the end of the
    /// step. The orbit frame falls as Jolt steps a body: its frame velocity is the chord of the step, and its instant velocity
    /// at the end of the step is half the step's change of velocity further on. A jump of the orbit frame keeps its chord.
    /// </summary>
    private static Double3 TickAndReadInstantVelocity(World world, FrameHandle orbitFrame)
    {
        Double3 chordBefore = world.Frames.LinearVelocity(orbitFrame);
        world.Tick();
        Double3 chord = world.Frames.LinearVelocity(orbitFrame);
        return chord + (chord - chordBefore) / 2;
    }

    [Fact]
    public void AKinematicHullThatHoldsStillAtTheOriginOfItsFallingOrbitFrameReadsTheOrbitFramesInstantVelocityInSpace()
    {
        using World world = NewWorld(out _);
        FrameHandle space = Scenes.AsteroidSpace(world);
        (Entity owner, FrameHandle orbitFrame) = FastHullWhoseSamplesStop(world, space);
        Tick(world, 2);

        Double3 instant = TickAndReadInstantVelocity(world, orbitFrame);

        // The orbit frame falls at about 800 m/s², so a hull that missed half a step of it would be 6.7 m/s off.
        world.PhysicsHomeOf(owner).Should().Be(orbitFrame);
        world.Frames.TreeLocalPose(world.GetOwnedFrame(owner)).Position.Should().Be(Double3.Zero, "the hull holds still at the orbit frame's origin");
        (world.VelocityIn(owner, space) - instant).Length.Should().BeLessThan(1e-4);
        world.Errors.Should().BeEmpty();
    }

    [Fact]
    public void AKinematicHullWithoutASampleThatTeleportsInItsOrbitFrameReadsTheOrbitFramesInstantVelocityInSpace()
    {
        using World world = NewWorld(out _);
        FrameHandle space = Scenes.AsteroidSpace(world);
        (Entity owner, FrameHandle orbitFrame) = FastHullWhoseSamplesStop(world, space, new Hopper());
        Tick(world, 2);

        Double3 instant = TickAndReadInstantVelocity(world, orbitFrame);

        // The teleport leaves the hull at rest in its orbit frame, which jumps with it.
        world.PhysicsHomeOf(owner).Should().Be(orbitFrame);
        world.Frames.TreeLocalPose(world.GetOwnedFrame(owner)).Position.Should().Be(Double3.Zero, "the orbit frame jumped with the hull");
        (world.VelocityIn(owner, space) - instant).Length.Should().BeLessThan(1e-4);
        world.Errors.Should().BeEmpty();
    }

    [Fact]
    public void AShipThatFloatsAtAStationsPlaceInItsOrbitFrameReadsInSpaceTheStationsVelocityPlusItsOwnInTheOrbitFrame()
    {
        using World world = NewWorldWithFaults(out FaultInjectingBackend faults);
        FrameHandle space = Scenes.AsteroidSpace(world);
        Entity station = Hull(world, space, FastStill);
        FrameHandle stationFrame = world.GetOwnedFrame(station);
        // The ship starts at the station's place, with the station's velocity, and passes through the station's hull, so the
        // two fall together in the station's orbit frame.
        (Double3 place, Double3 velocity) = FastStill.StateAt(world.Time, Mu);
        Entity ship = world.Spawn(new SpawnDesc
        {
            Frame = space,
            Pose = new DPose(place, DQuaternion.Identity),
            LinearVelocity = velocity,
            Body = Ball() with { CollisionMask = CollisionMask.Empty },
            AsFrame = new FrameSpec(FrameBounds.Sphere(0.01f)),
        });

        Tick(world, 6);

        FrameHandle orbitFrame = world.PhysicsHomeOf(station);
        world.Frames.Numeric(orbitFrame).Should().Be(NumericMode.Orbiting);
        world.PhysicsHomeOf(ship).Should().Be(orbitFrame, "the ship joined the station's orbit frame");
        MotionSample sample = faults.Samples.Single(record => record.Frame == stationFrame && record.Tick == world.TickNumber - 1).Sample;
        // Physics releases the ship's record in the orbit frame, with Jolt's chord there and the acceleration that the step
        // gave the ship there, so the ship's own instant velocity in the orbit frame is half a step of that acceleration past
        // the chord. The orbit frame's instant velocity at the end of the step is the station's, the sample's.
        double dt = world.DeltaTime;
        MotionSample released = faults.Outputs[^1].Frames.Single(record => record.Frame == world.GetOwnedFrame(ship)).Motion;
        Double3 own = released.ChordVelocity + released.Acceleration * (dt / 2);
        (world.VelocityIn(ship, space) - (sample.Velocity + own)).Length.Should().BeLessThan(1e-6);
        (sample.Velocity - sample.ChordVelocity - sample.Acceleration * (dt / 2)).Length.Should().BeGreaterThan(0.01,
            "half a step of the orbit's pull at the start of the step misses the change of the pull over the step by 3 cm/s");
        world.Errors.Should().BeEmpty();
    }

    /// <summary>
    /// A body 3 m from a station that moves as the station does, in an <see cref="Scenes.AsteroidSpace"/>, with a
    /// <see cref="Kicker"/>, so that it joins the station's orbit frame.
    /// </summary>
    private static (FrameHandle Space, Entity Station, Entity Body, Kicker Kicker) BodyBesideAStation(World world, BodyDesc body)
    {
        FrameHandle space = Scenes.AsteroidSpace(world);
        Entity station = Hull(world, space, FastStill);
        (Double3 place, Double3 velocity) = FastStill.StateAt(world.Time, Mu);
        var kicker = new Kicker();
        Entity entity = world.Spawn(new SpawnDesc
        {
            Frame = space,
            Pose = new DPose(place + new Double3(0, 3, 0), DQuaternion.Identity),
            LinearVelocity = velocity,
            Body = body,
            Components = [kicker],
        });
        return (space, station, entity, kicker);
    }

    [Fact]
    public void ACharacterInTheAirAndABallThatGameplayGivesOneVelocityInAStationsOrbitFrameMoveAlike()
    {
        using World characterWorld = NewWorldWithFaults(out FaultInjectingBackend faults);
        using World ballWorld = NewWorld(out _);
        var person = new BodyDesc(new CapsuleShapeDesc(0.6f, 0.3f), BodyMotion.Character, Mass: 70);
        (FrameHandle space, Entity station, Entity character, Kicker characterKicker) = BodyBesideAStation(characterWorld, person);
        (FrameHandle ballSpace, Entity ballStation, Entity ball, Kicker ballKicker) = BodyBesideAStation(ballWorld, Ball());
        Tick(characterWorld, 5);
        Tick(ballWorld, 5);
        characterWorld.PhysicsHomeOf(character).Should().Be(characterWorld.PhysicsHomeOf(station), "the character joined the station's orbit frame");
        ballWorld.PhysicsHomeOf(ball).Should().Be(ballWorld.PhysicsHomeOf(ballStation), "the ball joined the station's orbit frame");
        // Space pulls the character 3 m from the orbit frame's origin by more than twice the threshold of weightlessness.
        faults.Jolt.HasRigidForm(character).Should().BeFalse("the character walks, in the air");

        // The kick is 1 m/s along Z past the station's velocity in space. Gameplay writes it in the next tick, and the step
        // of the tick after applies it.
        Double3 kick = characterWorld.VelocityIn(station, space) + new Double3(0, 0, 1);
        characterKicker.Velocity = kick;
        ballKicker.Velocity = kick;
        Tick(characterWorld, 2);
        Tick(ballWorld, 2);

        characterWorld.Snapshot.IsStanding(character).Should().BeFalse();
        (characterWorld.VelocityIn(character, space) - ballWorld.VelocityIn(ball, ballSpace)).Length.Should().BeLessThan(1e-4);
        characterWorld.Errors.Should().BeEmpty();
        ballWorld.Errors.Should().BeEmpty();
    }

    [Theory]
    [InlineData("a kinematic hull on rails")]
    [InlineData("no body, on rails")]
    [InlineData("a static hull that teleported")]
    [InlineData("a kinematic hull without a sample that teleported")]
    [InlineData("no body, and a transform write")]
    [InlineData("a static hull that holds still")]
    [InlineData("a kinematic hull without a sample that holds still")]
    public void AFrameInItsSemanticParentReadsThereTheInstantVelocityOfItsStepRecord(string owner)
    {
        using World world = NewWorld(out _);
        FrameHandle space = Scenes.AsteroidSpace(world);
        bool onRails = owner.EndsWith("on rails", StringComparison.Ordinal);
        Entity entity = owner switch
        {
            "a kinematic hull on rails" => Hull(world, space, Slow),
            "no body, on rails" => Bodyless(world, space, Slow),
            "no body, and a transform write" => world.Spawn(new SpawnDesc
            {
                Frame = space,
                Pose = At(1000),
                AsFrame = new FrameSpec(FrameBounds.Sphere(5)),
                Components = [new Climber()],
            }),
            _ => world.Spawn(new SpawnDesc
            {
                Frame = space,
                Pose = At(1000),
                Body = Ball() with { Motion = owner.Contains("static", StringComparison.Ordinal) ? BodyMotion.Static : BodyMotion.Kinematic },
                AsFrame = new FrameSpec(FrameBounds.Sphere(0.01f)),
                Components = owner.EndsWith("teleported", StringComparison.Ordinal) ? [new Hopper()] : [],
            }),
        };

        Tick(world, 4);

        world.PhysicsHomeOf(entity).Should().Be(space, "the frame lives in its semantic parent");
        Double3 expected = onRails ? Slow.StateAt(world.Time, Mu).Velocity : Double3.Zero;
        (world.VelocityIn(entity, space) - expected).Length.Should().BeLessThan(1e-4);
        world.Errors.Should().BeEmpty();
    }

    // ---- Helpers ------------------------------------------------------------------------------------------------

    /// <summary>The angle in radians between two rotations.</summary>
    private static double AngleBetween(DQuaternion a, DQuaternion b)
    {
        double dot = Math.Abs(a.X * b.X + a.Y * b.Y + a.Z * b.Z + a.W * b.W) / Math.Sqrt(a.LengthSquared * b.LengthSquared);
        return 2 * Math.Acos(Math.Min(1, dot));
    }

    /// <summary>Teleports its owner's body 1 m higher in every tick.</summary>
    private sealed class Hopper : IComponent
    {
        public int Priority => 0;

        public EventMask Wants => EventMask.Of<Tick>();

        public void Handle(Self self, scoped ref EventList events, scoped ref Outbox outbox)
        {
            DPose pose = self.PoseInOwnFrame();
            self.Body.Teleport(pose with { Position = pose.Position + new Double3(0, 1, 0) });
        }
    }

    /// <summary>Gives its owner <see cref="Velocity"/> in its own frame in the next tick, once.</summary>
    private sealed class Kicker : IComponent
    {
        public Double3? Velocity;

        public int Priority => 0;

        public EventMask Wants => EventMask.Of<Tick>();

        public void Handle(Self self, scoped ref EventList events, scoped ref Outbox outbox)
        {
            if (Velocity is not { } velocity)
                return;
            self.Body.SetVelocity(velocity);
            Velocity = null;
        }
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
