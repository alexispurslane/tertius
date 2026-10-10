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
            Assert.Equal(planet, hull.Frame);
            Assert.True((hull.Pose.Position - tree.Position).Length < 1e-3, $"tick {i}: {(hull.Pose.Position - tree.Position).Length} m off");
            DQuaternion a = hull.Pose.Rotation, b = tree.Rotation;
            Assert.True(Math.Abs(a.X * b.X + a.Y * b.Y + a.Z * b.Z + a.W * b.W) > 1 - 1e-6);
            // The kinematic velocity is the chord over one step, close to the orbital velocity.
            Assert.True(((Double3)hull.LinearVelocity - h.Frames.LinearVelocity(station)).Length < 0.1);
        }
        Assert.Empty(h.HandOffs);
    }

    [Fact]
    public void FrameOwnerGetsHullInParentAndInteriorInItsOwnFrame()
    {
        using var h = new PhysicsHarness();
        FrameHandle space = h.AddFrame(FrameHandle.Null, DPose.Identity, FrameBounds.Sphere(1e6f), MotionDriver.Static);
        FrameHandle ship = h.AddFrameWithBody(space, Shapes.At(10, 0, 0), FrameBounds.Sphere(30), MotionDriver.Physics,
            new BodyDesc(new BoxShapeDesc(new Vector3(3)), Mass: 100));
        h.Sync();

        Assert.True(h.Backend.TryGetBodyFrame(ship.Entity, out FrameHandle hullFrame));
        Assert.Equal(space, hullFrame);
        Assert.True(h.Backend.HasInterior(ship.Entity));
        Assert.Equal(2, h.Backend.SystemCount);

        // A root frame has no parent, so its owner only has the interior.
        FrameHandle root = h.AddFrameWithBody(FrameHandle.Null, DPose.Identity, FrameBounds.Sphere(5), MotionDriver.Static,
            new BodyDesc(new BoxShapeDesc(new Vector3(1)), BodyMotion.Static));
        h.Sync();
        Assert.False(h.Backend.TryGetBodyFrame(root.Entity, out _));
        Assert.True(h.Backend.HasInterior(root.Entity));
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

        Assert.False(h.Backend.TryGetBodyFrame(crate, out _));
        Assert.False(h.Backend.TryGetBodyFrame(stray, out _));
        Assert.False(h.Backend.TryGetBodyFrame(ship.Entity, out _));
        Assert.Equal(1, h.Backend.SystemCount);
        Assert.Contains(h.Warnings, w => w.Contains(stray.ToString()));
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

        Assert.Equal(space, h.Frames.Parent(fighter));
        Assert.True(h.Backend.TryGetBodyFrame(fighter.Entity, out FrameHandle hullFrame) && hullFrame == space);
        Assert.True((h.Frames.LocalPose(fighter).Position - fighterInSpace).Length < 0.1, h.Frames.LocalPose(fighter).Position.ToString());
        Assert.Equal(1, h.Frames.LinearVelocity(fighter).X, 3);
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
        Assert.Empty(h.HandOffs);

        // Docking: the simulation moves the shuttle into the station on its own.
        h.Frames.Reparent(shuttle, station);
        h.Frames.Update();
        h.Step();

        Assert.True(h.Backend.TryGetBodyFrame(shuttle.Entity, out FrameHandle hullFrame));
        Assert.Equal(station, hullFrame);
        Assert.Equal(station, h[shuttle.Entity].Frame);
        Assert.True((h[shuttle.Entity].Pose.Position - new Double3(5, 0, 0)).Length < 1e-6);
        Assert.Empty(h.HandOffs);
    }
}
