using System.Numerics;
using Tertius.Frames.Rails;
using Tertius.Simulation;
using static Tertius.Testing.WorldTesting;

namespace Tertius.Physics.Tests;

/// <summary>
/// The hull of a frame's owner and the system of its interior, on the World and the real backend: how they follow the
/// frame tree, and what happens to them when a frame is destroyed or moved. The World rewrites the owner's state from the
/// frame state, so a test reads the hull that Jolt reported in the outputs that <see cref="FaultInjectingBackend"/> records.
/// </summary>
public class FrameHullTests
{
    private const double EarthMu = Scenes.EarthMu;

    /// <summary>A dynamic box with sides of 2 · <paramref name="half"/> m that nothing slows down on its own and that does not bounce.</summary>
    private static BodyDesc BoxBody(float half, float mass) =>
        new(new BoxShapeDesc(new Vector3(half)), Mass: mass, Restitution: 0, LinearDamping: 0, AngularDamping: 0);

    private static FrameHandle SpaceFrame(World world) =>
        world.CreateRootFrame(new FrameSpec(FrameBounds.Sphere(1e6f), MotionDriver.Static));

    /// <summary>A frame with the static driver and no body, with a bubble of <paramref name="radius"/> m.</summary>
    private static FrameHandle EmptyFrame(World world, FrameHandle parent, DPose pose, float radius) =>
        world.GetOwnedFrame(world.Spawn(new SpawnDesc { Frame = parent, Pose = pose, AsFrame = new FrameSpec(FrameBounds.Sphere(radius), MotionDriver.Static) }));

    /// <summary>The latest body state of <paramref name="entity"/> that Jolt sent, or null when Jolt never sent one.</summary>
    private static BodyState? LastReport(FaultInjectingBackend faults, Entity entity) =>
        faults.Outputs.SelectMany(output => output.Bodies).Where(state => state.Entity == entity).Cast<BodyState?>().LastOrDefault();

    /// <summary>
    /// The hull of a frame whose Kepler driver moves it at 7.5 km/s rests in an orbit frame of its own (ter-dhkf,
    /// docs/plans/numeric-frames.md, "Anchors", rule 3), so its body state is at the frame's pose in that orbit frame, the
    /// orbit frame's origin with the frame's rotation, and its kinematic velocity there, added to the orbit frame's chord,
    /// is the orbital velocity. Before rule 3, the hull lived in the planet's own system at the orbit's place.
    /// <para>
    /// A new frame on rails holds still at the pose of its spawn description for its first step, because its component has
    /// written no sample yet. The component's first run teleports the hull onto its rails, and the hull's first sample acts
    /// one step later (docs/plans/rails-components.md, "Other rules" in "The rails components"). So the test checks the hull
    /// from the sync point of the third tick on. In the step of the second tick, physics aims the hull at the place of its
    /// first sample, and the orbit frame jumps to the hull at the sync point, so the body state of that step is a few
    /// millimetres from the tree's origin.
    /// </para>
    /// </summary>
    [Fact]
    public void TheKinematicHullOfAKeplerFrameFollowsTheTreePoseInTheOrbitFrameWhereItRests()
    {
        using World world = NewWorldWithFaults(out FaultInjectingBackend faults);
        FrameHandle planet = world.CreateRootFrame(new FrameSpec(FrameBounds.Sphere(1e9f), MotionDriver.Static, GravityField.Point(EarthMu)));
        var orbit = KeplerOrbit.Circular(7_000_000, phase: 1.0, spinRate: 0.05);
        Entity owner = world.Spawn(new SpawnDesc
        {
            Frame = planet,
            Body = new BodyDesc(new BoxShapeDesc(new Vector3(10))),
            AsFrame = new FrameSpec(FrameBounds.Sphere(100), MotionDriver.Kepler),
        }.OnKeplerOrbit(world, orbit));
        FrameHandle station = world.GetOwnedFrame(owner);
        const int ticksBeforeTheHullIsOnItsRails = 2;
        Tick(world, ticksBeforeTheHullIsOnItsRails);

        for (int i = 0; i < 60; i++)
        {
            world.Tick();
            // The sync point has advanced the tree to the end of the step, where the hull must be.
            BodyState hull = faults.Outputs[^1].Bodies.Single(state => state.Entity == owner);
            DPose tree = world.Frames.TreeLocalPose(station);
            FrameHandle orbitFrame = world.Frames.TreeParent(station);
            world.Frames.Numeric(orbitFrame).Should().Be(NumericMode.Orbiting);
            hull.Frame.Should().Be(orbitFrame, "the hull rests in the orbit frame");
            tree.Position.Should().Be(Double3.Zero);
            (hull.Pose.Position - tree.Position).Length.Should().BeLessThan(1e-3, "the hull is at the tree pose after tick {0}", i + ticksBeforeTheHullIsOnItsRails);
            Math.Abs(Dot(hull.Pose.Rotation, tree.Rotation)).Should().BeGreaterThan(1 - 1e-6);
            // The kinematic velocity in the orbit frame and the orbit frame's own are chords over one step, close to the
            // orbital velocity together.
            ((Double3)hull.LinearVelocity + world.Frames.LinearVelocity(orbitFrame) - world.Frames.LinearVelocity(station)).Length.Should().BeLessThan(0.1);
            world.PoseIn(owner, planet).Should().Be(world.Frames.LocalPose(station), "the World rewrites the owner's state from the frame, not from the hull");
        }
        faults.Outputs.SelectMany(output => output.HandOffs).Should().BeEmpty();
        world.Errors.Should().BeEmpty();
    }

    [Fact]
    public void FrameOwnerGetsHullInParentAndInteriorInItsOwnFrame()
    {
        using World world = NewWorld(out JoltPhysicsBackend backend);
        FrameHandle space = SpaceFrame(world);
        Entity ship = world.Spawn(new SpawnDesc
        {
            Frame = space,
            Pose = At(10),
            Body = new BodyDesc(new BoxShapeDesc(new Vector3(3)), Mass: 100),
            AsFrame = new FrameSpec(FrameBounds.Sphere(30), MotionDriver.Physics),
        });
        world.Tick();

        backend.TryGetBodyFrame(ship, out FrameHandle hullFrame).Should().BeTrue();
        hullFrame.Should().Be(space);
        backend.HasInterior(ship).Should().BeTrue();
        backend.SystemCount.Should().Be(2);

        // A root frame has no parent, so its owner only has the interior.
        Entity root = world.Spawn(new SpawnDesc
        {
            Body = new BodyDesc(new BoxShapeDesc(new Vector3(1)), BodyMotion.Static),
            AsFrame = new FrameSpec(FrameBounds.Sphere(5), MotionDriver.Static),
        });
        world.Tick();
        backend.TryGetBodyFrame(root, out _).Should().BeFalse();
        backend.HasInterior(root).Should().BeTrue();
        world.Errors.Should().BeEmpty();
    }

    /// <summary>
    /// The World moves every body that survives a dying frame into the parent frame, so it never forgets a body in a frame
    /// that it destroys. A bug could, so a test stands in for it: it takes the rehome of the stray box out of the change
    /// batch that destroys the ship, and Jolt then holds the stray box in the system of a frame that is gone.
    /// </summary>
    [Fact]
    public void DestroyedBodiesAndFramesReleaseTheirSystems()
    {
        var warnings = new List<string>();
        using World world = NewWorldWithFaults(out FaultInjectingBackend faults, warnings);
        FrameHandle space = SpaceFrame(world);
        Entity ship = world.Spawn(new SpawnDesc
        {
            Frame = space,
            Pose = At(10),
            Body = new BodyDesc(new BoxShapeDesc(new Vector3(3)), Mass: 100, InteriorShape: new BoxShapeDesc(new Vector3(0.5f))),
            AsFrame = new FrameSpec(FrameBounds.Sphere(30), MotionDriver.Physics),
        });
        FrameHandle shipFrame = world.GetOwnedFrame(ship);
        Entity crate = world.Spawn(new SpawnDesc { Frame = shipFrame, Pose = At(0, 2), Body = BoxBody(0.25f, 1) });
        Entity stray = world.Spawn(new SpawnDesc { Frame = shipFrame, Pose = At(0, -2), Body = BoxBody(0.25f, 1) });
        Tick(world, 5);

        // The simulation destroys the crate and the ship, but the batch of the ship forgets the stray box.
        world.Destroy(crate);
        faults.EditNextChanges = changes =>
        {
            changes.BodiesDestroyed.Remove(stray);
            changes.BodiesCreated.RemoveAll(create => create.Entity == stray);
        };
        world.Destroy(ship);
        world.Tick();

        faults.Jolt.TryGetBodyFrame(crate, out _).Should().BeFalse();
        faults.Jolt.TryGetBodyFrame(stray, out _).Should().BeFalse();
        faults.Jolt.TryGetBodyFrame(ship, out _).Should().BeFalse();
        faults.Jolt.SystemCount.Should().Be(1);
        // Only the forgotten box is worth a warning. The crate and the ship went in the documented order.
        warnings.Should().ContainSingle().Which.Should().Contain(stray.ToString());
        world.Errors.Should().BeEmpty();
    }

    [Fact]
    public void ChildFrameHullMovesToGrandparentWhenParentFrameIsDestroyed()
    {
        using World world = NewWorld(out JoltPhysicsBackend backend);
        FrameHandle space = SpaceFrame(world);
        Entity carrier = world.Spawn(new SpawnDesc
        {
            Frame = space,
            Pose = At(100),
            Body = new BodyDesc(new BoxShapeDesc(new Vector3(20)), BodyMotion.Static, InteriorShape: new BoxShapeDesc(new Vector3(1))),
            AsFrame = new FrameSpec(FrameBounds.Sphere(200), MotionDriver.Static),
        });
        FrameHandle carrierFrame = world.GetOwnedFrame(carrier);
        Entity fighter = world.Spawn(new SpawnDesc
        {
            Frame = carrierFrame,
            Pose = At(0, 50),
            LinearVelocity = new Double3(1, 0, 0),
            Body = new BodyDesc(new BoxShapeDesc(new Vector3(2)), Mass: 1000, LinearDamping: 0),
            AsFrame = new FrameSpec(FrameBounds.Sphere(10), MotionDriver.Physics),
        });
        FrameHandle fighterFrame = world.GetOwnedFrame(fighter);
        Tick(world, 3);
        Double3 fighterInSpace = world.Frames.PointToParent(carrierFrame, world.Frames.LocalPose(fighterFrame).Position);

        world.Destroy(carrier);
        world.Tick();

        world.Frames.Parent(fighterFrame).Should().Be(space);
        backend.TryGetBodyFrame(fighter, out FrameHandle hullFrame).Should().BeTrue();
        hullFrame.Should().Be(space);
        (world.Frames.LocalPose(fighterFrame).Position - fighterInSpace).Length.Should().BeLessThan(0.1);
        world.Frames.LinearVelocity(fighterFrame).X.Should().BeApproximately(1, 5e-4);
        world.Errors.Should().BeEmpty();
    }

    [Fact]
    public void ChildFrameKeepsItsMotionWhenItsMovingParentFrameIsDestroyed()
    {
        using World world = NewWorld(out JoltPhysicsBackend backend);
        FrameHandle space = SpaceFrame(world);
        // A drifting, spinning carrier with a fighter inside that flies and spins on its own. Sphere hulls keep their
        // spin constant and nothing is damped, so every velocity stays exact.
        var carrierHull = new BodyDesc(new SphereShapeDesc(20), Mass: 1e6f, LinearDamping: 0, AngularDamping: 0, InteriorShape: new SphereShapeDesc(1));
        Entity carrier = world.Spawn(new SpawnDesc
        {
            Frame = space,
            Pose = At(100),
            LinearVelocity = new Double3(0, 0, 20),
            AngularVelocity = new Vector3(0, 0.2f, 0),
            Body = carrierHull,
            AsFrame = new FrameSpec(FrameBounds.Sphere(200), MotionDriver.Physics),
        });
        FrameHandle carrierFrame = world.GetOwnedFrame(carrier);
        var fighterHull = new BodyDesc(new SphereShapeDesc(2), Mass: 1000, LinearDamping: 0, AngularDamping: 0, InteriorShape: new SphereShapeDesc(0.5f));
        Entity fighter = world.Spawn(new SpawnDesc
        {
            Frame = carrierFrame,
            Pose = At(60),
            LinearVelocity = new Double3(1, 0, 0),
            AngularVelocity = new Vector3(0, 0, 0.5f),
            Body = fighterHull,
            AsFrame = new FrameSpec(FrameBounds.Sphere(10), MotionDriver.Physics),
        });
        FrameHandle fighterFrame = world.GetOwnedFrame(fighter);
        Tick(world, 10);

        // The fighter's motion in space: v' = R·v + v_carrier + ω × (R·p), and ω' = R·ω_fighter + ω_carrier.
        DPose carrierPose = world.Frames.LocalPose(carrierFrame);
        Double3 carrierSpin = world.Frames.AngularVelocity(carrierFrame);
        Double3 inCarrier = world.Frames.LocalPose(fighterFrame).Position;
        Double3 position = carrierPose.TransformPoint(inCarrier);
        Double3 velocity = carrierPose.TransformDirection(world.Frames.LinearVelocity(fighterFrame)) + world.Frames.LinearVelocity(carrierFrame) +
            Double3.Cross(carrierSpin, carrierPose.TransformDirection(inCarrier));
        Double3 spin = carrierPose.TransformDirection(world.Frames.AngularVelocity(fighterFrame)) + carrierSpin;

        world.Destroy(carrier);
        // The tree has already moved the fighter into space and converted its velocities.
        (world.Frames.LinearVelocity(fighterFrame) - velocity).Length.Should().BeLessThan(1e-6);
        world.Tick();

        // The hull took the tree's motion as it was. Converting it again through the carrier would add the carrier's
        // 20 m/s and the 12 m/s of ω × r a second time; turning the body's own velocity would drop them.
        backend.TryGetBodyFrame(fighter, out FrameHandle hullFrame).Should().BeTrue();
        hullFrame.Should().Be(space);
        world.Frames.Parent(fighterFrame).Should().Be(space);
        (world.Frames.LinearVelocity(fighterFrame) - velocity).Length.Should().BeLessThan(1e-4 * velocity.Length);
        ((Double3)world.Frames.AngularVelocity(fighterFrame) - spin).Length.Should().BeLessThan(1e-5);
        (world.Frames.LocalPose(fighterFrame).Position - (position + velocity * (double)world.DeltaTime)).Length.Should().BeLessThan(1e-3);
        world.Frames.ProperAcceleration(fighterFrame).Length().Should().BeLessThan(1e-6f);
        world.Errors.Should().BeEmpty();
    }

    /// <summary>
    /// The World destroys a frame together with its owner and sends physics both deaths, so an owner whose body outlives its
    /// frame in physics comes only from a bug. A test stands in for that bug: it takes the owner's body out of the change
    /// batch that destroys the owner and its frame. A hull without its frame has nothing to stand for, so the backend drops
    /// the hull and the interior, and warns.
    /// </summary>
    [Fact]
    public void OwnerThatOutlivesItsFrameLosesItsHull()
    {
        var warnings = new List<string>();
        using World world = NewWorldWithFaults(out FaultInjectingBackend faults, warnings);
        FrameHandle space = SpaceFrame(world);
        Entity shipOwner = world.Spawn(new SpawnDesc
        {
            Frame = space,
            Pose = At(10),
            Body = new BodyDesc(new BoxShapeDesc(new Vector3(3)), Mass: 100),
            AsFrame = new FrameSpec(FrameBounds.Sphere(30), MotionDriver.Physics),
        });
        Entity towerOwner = world.Spawn(new SpawnDesc
        {
            Frame = space,
            Pose = At(-50),
            Body = new BodyDesc(new BoxShapeDesc(new Vector3(2)), BodyMotion.Static),
            AsFrame = new FrameSpec(FrameBounds.Sphere(20), MotionDriver.Static),
        });
        world.Tick();
        faults.Jolt.HasInterior(shipOwner).Should().BeTrue();
        faults.Jolt.SystemCount.Should().Be(3);

        // Both frames go, but their owners keep their bodies in physics.
        foreach (Entity owner in new[] { shipOwner, towerOwner })
        {
            faults.EditNextChanges = changes => changes.BodiesDestroyed.Remove(owner);
            world.Destroy(owner);
        }
        Tick(world, 3);

        faults.Jolt.TryGetBodyFrame(shipOwner, out _).Should().BeFalse();
        faults.Jolt.TryGetBodyFrame(towerOwner, out _).Should().BeFalse();
        faults.Jolt.HasInterior(shipOwner).Should().BeFalse();
        faults.Jolt.SystemCount.Should().Be(1);
        warnings.Should().Contain(w => w.Contains(shipOwner.ToString()));
        warnings.Should().Contain(w => w.Contains(towerOwner.ToString()));
        faults.Outputs[^1].Frames.Should().BeEmpty();
        world.Errors.Should().BeEmpty();
    }

    /// <summary>
    /// The World sends a change batch that names a frame only when the frame dies with its owner, so a batch that names a
    /// frame that the tree still has, and a frame that leaves the tree with no change in the batch, come only from a bug. A
    /// test stands in for each: it adds the frame to the next batch, and it empties the batch that destroys the frame.
    /// </summary>
    [Fact]
    public void FrameSystemStaysUntilTheFrameLeavesTheTree()
    {
        var warnings = new List<string>();
        using World world = NewWorldWithFaults(out FaultInjectingBackend faults, warnings);
        FrameHandle space = SpaceFrame(world);
        Entity bubbleOwner = world.Spawn(new SpawnDesc { Frame = space, Pose = At(50), AsFrame = new FrameSpec(FrameBounds.Sphere(20), MotionDriver.Static) });
        FrameHandle bubble = world.GetOwnedFrame(bubbleOwner);
        // The ball sticks out of the bubble, so space holds a ghost of it.
        Entity ball = world.Spawn(new SpawnDesc { Frame = bubble, Pose = At(19.8), Body = Ball() });
        world.Tick();
        faults.Jolt.HasGhostIn(ball, space).Should().BeTrue();

        // A batch names a frame that the tree still has. Physics keeps following the tree. The World sends a batch only when
        // it holds a change, so the test spawns a box far away to make one.
        faults.EditNextChanges = changes => changes.FramesDestroyed.Add(bubble);
        world.Spawn(new SpawnDesc { Frame = space, Pose = At(0, -1000), Body = new BodyDesc(new BoxShapeDesc(new Vector3(1)), BodyMotion.Static) });
        world.Tick();
        warnings.Should().Contain(w => w.Contains(bubble.ToString()) && w.Contains("still in the frame tree"));
        faults.Jolt.TryGetBodyFrame(ball, out FrameHandle ballFrame).Should().BeTrue();
        ballFrame.Should().Be(bubble);
        faults.Jolt.SystemCount.Should().Be(2);

        // The frame then leaves the tree with nothing in the batch about it, so the step itself drops the system, and the
        // ball and its ghost with it. The World destroys the bubble with its owner, and the edit empties the batch that
        // would have told physics.
        faults.EditNextChanges = changes => changes.Clear();
        world.Destroy(bubbleOwner);
        world.Tick();
        warnings.Should().Contain(w => w.Contains(bubble.ToString()) && w.Contains("left the frame tree"));
        warnings.Should().Contain(w => w.Contains(ball.ToString()));
        faults.Jolt.TryGetBodyFrame(ball, out _).Should().BeFalse();
        faults.Jolt.GhostCount(ball).Should().Be(0);
        faults.Jolt.SystemCount.Should().Be(1);
        world.Errors.Should().BeEmpty();
    }

    [Fact]
    public void SimulationReparentingAFrameMovesItsHull()
    {
        using World world = NewWorldWithFaults(out FaultInjectingBackend faults);
        FrameHandle space = SpaceFrame(world);
        // The shuttle's bubble does not fit in the station's, so it never enters on its own.
        FrameHandle station = EmptyFrame(world, space, At(500), 15);
        Entity shuttleOwner = world.Spawn(new SpawnDesc
        {
            Frame = space,
            Pose = At(505),
            Body = new BodyDesc(new BoxShapeDesc(new Vector3(2)), Mass: 1000, Restitution: 0, LinearDamping: 0, AngularDamping: 0),
            AsFrame = new FrameSpec(FrameBounds.Sphere(10), MotionDriver.Physics),
        });
        FrameHandle shuttle = world.GetOwnedFrame(shuttleOwner);
        world.Tick();
        faults.Outputs.SelectMany(output => output.HandOffs).Should().BeEmpty();

        // Docking: the simulation moves the shuttle into the station on its own, with no physics changes. The step
        // sees the new tree and moves the hull.
        world.Frames.Reparent(shuttle, station);
        world.Tick();

        faults.Jolt.TryGetBodyFrame(shuttleOwner, out FrameHandle hullFrame).Should().BeTrue();
        hullFrame.Should().Be(station);
        world.GetFrame(shuttleOwner).Should().Be(station);
        (world.PoseIn(shuttleOwner, station).Position - new Double3(5, 0, 0)).Length.Should().BeLessThan(1e-6);
        BodyState hull = LastReport(faults, shuttleOwner)!.Value;
        hull.Frame.Should().Be(station, "Jolt reports the hull in the system that now holds it");
        (hull.Pose.Position - new Double3(5, 0, 0)).Length.Should().BeLessThan(1e-6);
        faults.Outputs.SelectMany(output => output.HandOffs).Should().BeEmpty();
        world.Errors.Should().BeEmpty();
    }

    [Fact]
    public void SimulationCanDockAFrameThatPhysicsHandedOffBefore()
    {
        var warnings = new List<string>();
        using World world = NewWorldWithFaults(out FaultInjectingBackend faults, warnings);
        FrameHandle space = SpaceFrame(world);
        FrameHandle hangar = EmptyFrame(world, space, At(400), 30);
        // The shuttle's bubble does not fit in the station's, so it never enters on its own.
        FrameHandle station = EmptyFrame(world, space, At(500), 15);
        var pilot = new Pilot();
        Entity shuttleOwner = world.Spawn(new SpawnDesc
        {
            Frame = hangar,
            Pose = At(25),
            LinearVelocity = new Double3(10, 0, 0),
            Body = new BodyDesc(new BoxShapeDesc(new Vector3(2)), Mass: 1000, Restitution: 0, LinearDamping: 0, AngularDamping: 0),
            AsFrame = new FrameSpec(FrameBounds.Sphere(10), MotionDriver.Physics),
            Components = [pilot],
        });
        FrameHandle shuttle = world.GetOwnedFrame(shuttleOwner);

        // Physics hands the shuttle's frame out of the hangar into space.
        TickUntil(world, () => faults.Outputs.Any(output => output.HandOffs.Count > 0), 120);
        HandOff exit = faults.Outputs.SelectMany(output => output.HandOffs).Should().ContainSingle().Which;
        exit.To.Should().Be(space);
        world.Frames.Parent(shuttle).Should().Be(space);

        // It stops at the station's center. The bubbles overlap, so the station's system holds a ghost of the hull. A
        // command that a component gives in one tick acts in the step of the next, so two ticks.
        pilot.Once = body => body.Teleport(At(500));
        Tick(world, 2);
        faults.Jolt.HasGhostIn(shuttleOwner, station).Should().BeTrue();

        // The simulation docks it, at a sync point that has other changes too. The earlier hand-off must not hold the
        // hull in space: the backend moves it into the station, and the real hull replaces its ghost there. The check
        // reads the backend after the tick that applies the batch and runs the step.
        world.Frames.Reparent(shuttle, station);
        // Cargo for the station, outside the shuttle's bubble.
        world.Spawn(new SpawnDesc { Frame = station, Pose = At(0, 12), Body = BoxBody(0.25f, 1) });
        world.Tick();
        faults.Jolt.TryGetBodyFrame(shuttleOwner, out FrameHandle hullFrame).Should().BeTrue();
        hullFrame.Should().Be(station);
        faults.Jolt.HasGhostIn(shuttleOwner, station).Should().BeFalse();

        world.Tick();
        world.GetFrame(shuttleOwner).Should().Be(station);
        world.PoseIn(shuttleOwner, station).Position.Length.Should().BeLessThan(1e-6);
        faults.Outputs.SelectMany(output => output.HandOffs).Should().ContainSingle();

        // Later the shuttle is scrapped. Nothing of it stays behind in space or in the station.
        world.Destroy(shuttleOwner);
        Tick(world, 2);
        faults.Jolt.TryGetBodyFrame(shuttleOwner, out _).Should().BeFalse();
        faults.Jolt.GhostCount(shuttleOwner).Should().Be(0);
        warnings.Should().BeEmpty();
        world.Errors.Should().BeEmpty();
    }

    private static double Dot(DQuaternion a, DQuaternion b) => a.X * b.X + a.Y * b.Y + a.Z * b.Z + a.W * b.W;
}
