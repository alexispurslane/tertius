using System.Numerics;

namespace Tertius.Physics.Tests;

public class FrameHullTests
{
    private const double EarthMu = 3.986004418e14;

    [Fact]
    public void KinematicHullOfKeplerFrameFollowsTheTreePose()
    {
        using var h = new PhysicsHarness();
        FrameHandle planet = h.AddFrame(FrameHandle.Null, DPose.Identity, FrameBounds.Sphere(1e9f), MotionDriver.Static, GravityField.Point(EarthMu));
        var orbit = KeplerOrbit.Circular(7_000_000, EarthMu, phase: 1.0, spinRate: 0.05);
        FrameHandle station = h.AddFrameWithBody(planet, DPose.Identity, FrameBounds.Sphere(100),
            MotionDriver.Kepler, new BodyDesc(new BoxShapeDesc(new Vector3(10))), orbit: orbit);

        for (int i = 0; i < 60; i++)
        {
            h.Step();
            // The harness has advanced the tree to the end of the step, where the hull must be.
            BodyState hull = h[station.Entity];
            DPose tree = h.Frames.LocalPose(station);
            hull.Frame.Should().Be(planet);
            (hull.Pose.Position - tree.Position).Length.Should().BeLessThan(1e-3, "the hull is at the tree pose after tick {0}", i);
            Math.Abs(Dot(hull.Pose.Rotation, tree.Rotation)).Should().BeGreaterThan(1 - 1e-6);
            // The kinematic velocity is the chord over one step, close to the orbital velocity.
            ((Double3)hull.LinearVelocity - h.Frames.LinearVelocity(station)).Length.Should().BeLessThan(0.1);
        }
        h.HandOffs.Should().BeEmpty();
    }

    [Fact]
    public void FrameOwnerGetsHullInParentAndInteriorInItsOwnFrame()
    {
        using var h = new PhysicsHarness();
        FrameHandle space = h.AddFrame(FrameHandle.Null, DPose.Identity, FrameBounds.Sphere(1e6f), MotionDriver.Static);
        FrameHandle ship = h.AddFrameWithBody(space, Shapes.At(10, 0, 0), FrameBounds.Sphere(30), MotionDriver.Physics,
            new BodyDesc(new BoxShapeDesc(new Vector3(3)), Mass: 100));
        h.Sync();

        h.Backend.TryGetBodyFrame(ship.Entity, out FrameHandle hullFrame).Should().BeTrue();
        hullFrame.Should().Be(space);
        h.Backend.HasInterior(ship.Entity).Should().BeTrue();
        h.Backend.SystemCount.Should().Be(2);

        // A root frame has no parent, so its owner only has the interior.
        FrameHandle root = h.AddFrameWithBody(FrameHandle.Null, DPose.Identity, FrameBounds.Sphere(5), MotionDriver.Static,
            new BodyDesc(new BoxShapeDesc(new Vector3(1)), BodyMotion.Static));
        h.Sync();
        h.Backend.TryGetBodyFrame(root.Entity, out _).Should().BeFalse();
        h.Backend.HasInterior(root.Entity).Should().BeTrue();
    }

    [Fact]
    public void DestroyedBodiesAndFramesReleaseTheirSystems()
    {
        using var h = new PhysicsHarness();
        FrameHandle space = h.AddFrame(FrameHandle.Null, DPose.Identity, FrameBounds.Sphere(1e6f), MotionDriver.Static);
        FrameHandle ship = h.AddFrameWithBody(space, Shapes.At(10, 0, 0), FrameBounds.Sphere(30), MotionDriver.Physics,
            new BodyDesc(new BoxShapeDesc(new Vector3(3)), Mass: 100, InteriorShape: new BoxShapeDesc(new Vector3(0.5f))));
        Entity crate = h.AddBody(ship, Shapes.Box(0.25f), Shapes.At(0, 2, 0));
        Entity stray = h.AddBody(ship, Shapes.Box(0.25f), Shapes.At(0, -2, 0));
        h.Step(5);

        // The simulation destroys the crate and the ship together, but forgets the stray box.
        h.Destroy(crate);
        h.Destroy(ship.Entity);
        h.Frames.Destroy(ship);
        h.Changes.FramesDestroyed.Add(ship);
        h.Step();

        h.Backend.TryGetBodyFrame(crate, out _).Should().BeFalse();
        h.Backend.TryGetBodyFrame(stray, out _).Should().BeFalse();
        h.Backend.TryGetBodyFrame(ship.Entity, out _).Should().BeFalse();
        h.Backend.SystemCount.Should().Be(1);
        // Only the forgotten box is worth a warning. The crate and the ship went in the documented order.
        h.Warnings.Should().ContainSingle().Which.Should().Contain(stray.ToString());
    }

    [Fact]
    public void ChildFrameHullMovesToGrandparentWhenParentFrameIsDestroyed()
    {
        using var h = new PhysicsHarness();
        FrameHandle space = h.AddFrame(FrameHandle.Null, DPose.Identity, FrameBounds.Sphere(1e6f), MotionDriver.Static);
        FrameHandle carrier = h.AddFrameWithBody(space, Shapes.At(100, 0, 0), FrameBounds.Sphere(200), MotionDriver.Static,
            new BodyDesc(new BoxShapeDesc(new Vector3(20)), BodyMotion.Static, InteriorShape: new BoxShapeDesc(new Vector3(1))));
        FrameHandle fighter = h.AddFrameWithBody(carrier, Shapes.At(0, 50, 0), FrameBounds.Sphere(10), MotionDriver.Physics,
            new BodyDesc(new BoxShapeDesc(new Vector3(2)), Mass: 1000, LinearDamping: 0), velocity: new Vector3(1, 0, 0));
        h.Step(3);
        Double3 fighterInSpace = h.Frames.PointToParent(carrier, h.Frames.LocalPose(fighter).Position);

        h.Destroy(carrier.Entity);
        h.Frames.Destroy(carrier);
        h.Changes.FramesDestroyed.Add(carrier);
        h.Step();

        h.Frames.Parent(fighter).Should().Be(space);
        h.Backend.TryGetBodyFrame(fighter.Entity, out FrameHandle hullFrame).Should().BeTrue();
        hullFrame.Should().Be(space);
        (h.Frames.LocalPose(fighter).Position - fighterInSpace).Length.Should().BeLessThan(0.1);
        h.Frames.LinearVelocity(fighter).X.Should().BeApproximately(1, 5e-4);
    }

    [Fact]
    public void ChildFrameKeepsItsMotionWhenItsMovingParentFrameIsDestroyed()
    {
        using var h = new PhysicsHarness();
        FrameHandle space = h.AddFrame(FrameHandle.Null, DPose.Identity, FrameBounds.Sphere(1e6f), MotionDriver.Static);
        // A drifting, spinning carrier with a fighter inside that flies and spins on its own. Sphere hulls keep their
        // spin constant and nothing is damped, so every velocity stays exact.
        var carrierHull = new BodyDesc(new SphereShapeDesc(20), Mass: 1e6f, LinearDamping: 0, AngularDamping: 0, InteriorShape: new SphereShapeDesc(1));
        FrameHandle carrier = h.AddFrameWithBody(space, Shapes.At(100, 0, 0), FrameBounds.Sphere(200), MotionDriver.Physics, carrierHull,
            velocity: new Vector3(0, 0, 20), angularVelocity: new Vector3(0, 0.2f, 0));
        var fighterHull = new BodyDesc(new SphereShapeDesc(2), Mass: 1000, LinearDamping: 0, AngularDamping: 0, InteriorShape: new SphereShapeDesc(0.5f));
        FrameHandle fighter = h.AddFrameWithBody(carrier, Shapes.At(60, 0, 0), FrameBounds.Sphere(10), MotionDriver.Physics, fighterHull,
            velocity: new Vector3(1, 0, 0), angularVelocity: new Vector3(0, 0, 0.5f));
        h.Step(10);

        // The fighter's motion in space: v' = R·v + v_carrier + ω × (R·p), and ω' = R·ω_fighter + ω_carrier.
        DPose carrierPose = h.Frames.LocalPose(carrier);
        Double3 carrierSpin = h.Frames.AngularVelocity(carrier);
        Double3 inCarrier = h.Frames.LocalPose(fighter).Position;
        Double3 position = carrierPose.TransformPoint(inCarrier);
        Double3 velocity = carrierPose.TransformDirection(h.Frames.LinearVelocity(fighter)) + h.Frames.LinearVelocity(carrier) +
            Double3.Cross(carrierSpin, carrierPose.TransformDirection(inCarrier));
        Double3 spin = carrierPose.TransformDirection(h.Frames.AngularVelocity(fighter)) + carrierSpin;

        h.Destroy(carrier.Entity);
        h.Frames.Destroy(carrier);
        h.Changes.FramesDestroyed.Add(carrier);
        // The tree has already moved the fighter into space and converted its velocities.
        (h.Frames.LinearVelocity(fighter) - velocity).Length.Should().BeLessThan(1e-6);
        h.Step();

        // The hull took the tree's motion as it was. Converting it again through the carrier would add the carrier's
        // 20 m/s and the 12 m/s of ω × r a second time; turning the body's own velocity would drop them.
        h.Backend.TryGetBodyFrame(fighter.Entity, out FrameHandle hullFrame).Should().BeTrue();
        hullFrame.Should().Be(space);
        h.Frames.Parent(fighter).Should().Be(space);
        (h.Frames.LinearVelocity(fighter) - velocity).Length.Should().BeLessThan(1e-4 * velocity.Length);
        ((Double3)h.Frames.AngularVelocity(fighter) - spin).Length.Should().BeLessThan(1e-5);
        (h.Frames.LocalPose(fighter).Position - (position + velocity * PhysicsHarness.Dt)).Length.Should().BeLessThan(1e-3);
        h.Frames.ProperAcceleration(fighter).Length().Should().BeLessThan(1e-6f);
    }

    [Fact]
    public void OwnerThatOutlivesItsFrameLosesItsHull()
    {
        using var h = new PhysicsHarness();
        FrameHandle space = h.AddFrame(FrameHandle.Null, DPose.Identity, FrameBounds.Sphere(1e6f), MotionDriver.Static);
        FrameHandle ship = h.AddFrameWithBody(space, Shapes.At(10, 0, 0), FrameBounds.Sphere(30), MotionDriver.Physics,
            new BodyDesc(new BoxShapeDesc(new Vector3(3)), Mass: 100));
        FrameHandle tower = h.AddFrameWithBody(space, Shapes.At(-50, 0, 0), FrameBounds.Sphere(20), MotionDriver.Static,
            new BodyDesc(new BoxShapeDesc(new Vector3(2)), BodyMotion.Static));
        h.Step();

        // Both frames go, but their owners keep their bodies. A hull without its frame has nothing to stand for.
        h.Frames.Destroy(ship);
        h.Frames.Destroy(tower);
        h.Changes.FramesDestroyed.Add(ship);
        h.Changes.FramesDestroyed.Add(tower);
        h.Step(3);

        h.Backend.TryGetBodyFrame(ship.Entity, out _).Should().BeFalse();
        h.Backend.TryGetBodyFrame(tower.Entity, out _).Should().BeFalse();
        h.Backend.HasInterior(ship.Entity).Should().BeFalse();
        h.Backend.SystemCount.Should().Be(1);
        h.Warnings.Should().Contain(w => w.Contains(ship.Entity.ToString()));
        h.Warnings.Should().Contain(w => w.Contains(tower.Entity.ToString()));
        h.Output.Frames.Should().BeEmpty();
    }

    [Fact]
    public void FrameSystemStaysUntilTheFrameLeavesTheTree()
    {
        using var h = new PhysicsHarness();
        FrameHandle space = h.AddFrame(FrameHandle.Null, DPose.Identity, FrameBounds.Sphere(1e6f), MotionDriver.Static);
        FrameHandle bubble = h.AddFrame(space, Shapes.At(50, 0, 0), FrameBounds.Sphere(20), MotionDriver.Static);
        // The ball sticks out of the bubble, so space holds a ghost of it.
        Entity ball = h.AddBody(bubble, Shapes.Ball(0.5f), Shapes.At(19.8, 0, 0));
        h.Step();
        h.Backend.HasGhostIn(ball, space).Should().BeTrue();

        // FramesDestroyed names a frame that the tree still has. Physics keeps following the tree.
        h.Changes.FramesDestroyed.Add(bubble);
        h.Step();
        h.Warnings.Should().Contain(w => w.Contains(bubble.ToString()) && w.Contains("still in the frame tree"));
        h.Backend.TryGetBodyFrame(ball, out FrameHandle ballFrame).Should().BeTrue();
        ballFrame.Should().Be(bubble);
        h.Backend.SystemCount.Should().Be(2);

        // The frame then leaves the tree with no change batch at all, so the step itself drops the system, and the
        // ball and its ghost with it.
        h.Frames.Destroy(bubble);
        h.Step();
        h.Warnings.Should().Contain(w => w.Contains(bubble.ToString()) && w.Contains("left the frame tree"));
        h.Warnings.Should().Contain(w => w.Contains(ball.ToString()));
        h.Backend.TryGetBodyFrame(ball, out _).Should().BeFalse();
        h.Backend.GhostCount(ball).Should().Be(0);
        h.Backend.SystemCount.Should().Be(1);
    }

    [Fact]
    public void SimulationReparentingAFrameMovesItsHull()
    {
        using var h = new PhysicsHarness();
        FrameHandle space = h.AddFrame(FrameHandle.Null, DPose.Identity, FrameBounds.Sphere(1e6f), MotionDriver.Static);
        // The shuttle's bubble does not fit in the station's, so it never enters on its own.
        FrameHandle station = h.AddFrame(space, Shapes.At(500, 0, 0), FrameBounds.Sphere(15), MotionDriver.Static);
        FrameHandle shuttle = h.AddFrameWithBody(space, Shapes.At(505, 0, 0), FrameBounds.Sphere(10), MotionDriver.Physics,
            new BodyDesc(new BoxShapeDesc(new Vector3(2)), Mass: 1000));
        h.Step();
        h.HandOffs.Should().BeEmpty();

        // Docking: the simulation moves the shuttle into the station on its own, with no physics changes. The step
        // sees the new tree and moves the hull.
        h.Frames.Reparent(shuttle, station);
        h.Frames.Update();
        h.Step();

        h.Backend.TryGetBodyFrame(shuttle.Entity, out FrameHandle hullFrame).Should().BeTrue();
        hullFrame.Should().Be(station);
        h[shuttle.Entity].Frame.Should().Be(station);
        (h[shuttle.Entity].Pose.Position - new Double3(5, 0, 0)).Length.Should().BeLessThan(1e-6);
        h.HandOffs.Should().BeEmpty();
    }

    [Fact]
    public void SimulationCanDockAFrameThatPhysicsHandedOffBefore()
    {
        using var h = new PhysicsHarness();
        FrameHandle space = h.AddFrame(FrameHandle.Null, DPose.Identity, FrameBounds.Sphere(1e6f), MotionDriver.Static);
        FrameHandle hangar = h.AddFrame(space, Shapes.At(400, 0, 0), FrameBounds.Sphere(30), MotionDriver.Static);
        // The shuttle's bubble does not fit in the station's, so it never enters on its own.
        FrameHandle station = h.AddFrame(space, Shapes.At(500, 0, 0), FrameBounds.Sphere(15), MotionDriver.Static);
        FrameHandle shuttle = h.AddFrameWithBody(hangar, Shapes.At(25, 0, 0), FrameBounds.Sphere(10), MotionDriver.Physics,
            new BodyDesc(new BoxShapeDesc(new Vector3(2)), Mass: 1000), velocity: new Vector3(10, 0, 0));

        // Physics hands the shuttle's frame out of the hangar into space.
        h.StepUntil(() => h.HandOffs.Count > 0, 120);
        HandOff exit = h.HandOffs.Should().ContainSingle().Which;
        exit.To.Should().Be(space);
        h.Frames.Parent(shuttle).Should().Be(space);

        // It stops at the station's center. The bubbles overlap, so the station's system holds a ghost of the hull.
        h.Intent(shuttle.Entity).Flags = IntentFlags.Teleport;
        h.Intent(shuttle.Entity).TeleportPose = Shapes.At(500, 0, 0);
        h.Step();
        h.Backend.HasGhostIn(shuttle.Entity, station).Should().BeTrue();

        // The simulation docks it, at a sync point that has other changes too. The earlier hand-off must not hold the
        // hull in space: ApplyChanges moves it into the station at once, and the real hull replaces its ghost there.
        h.Frames.Reparent(shuttle, station);
        // Cargo for the station, outside the shuttle's bubble.
        h.AddBody(station, Shapes.Box(0.25f), Shapes.At(0, 12, 0));
        h.Sync();
        h.Backend.TryGetBodyFrame(shuttle.Entity, out FrameHandle hullFrame).Should().BeTrue();
        hullFrame.Should().Be(station);
        h.Backend.HasGhostIn(shuttle.Entity, station).Should().BeFalse();

        h.Step();
        h[shuttle.Entity].Frame.Should().Be(station);
        h[shuttle.Entity].Pose.Position.Length.Should().BeLessThan(1e-6);
        h.HandOffs.Should().ContainSingle();

        // Later the shuttle is scrapped. Nothing of it stays behind in space or in the station.
        h.Destroy(shuttle.Entity);
        h.Frames.Destroy(shuttle);
        h.Changes.FramesDestroyed.Add(shuttle);
        h.Step(2);
        h.Backend.TryGetBodyFrame(shuttle.Entity, out _).Should().BeFalse();
        h.Backend.GhostCount(shuttle.Entity).Should().Be(0);
        h.Warnings.Should().BeEmpty();
    }

    private static double Dot(DQuaternion a, DQuaternion b) => a.X * b.X + a.Y * b.Y + a.Z * b.Z + a.W * b.W;
}
