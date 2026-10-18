using System.Numerics;
using Tertius.Frames.Rails;
using Tertius;

namespace Tertius.Core.Tests;

public class FrameTests
{
    [Fact]
    public void SphereBoundsDistances()
    {
        var b = FrameBounds.Sphere(100);
        Assert.Equal(-100, b.SignedDistance(Double3.Zero));
        Assert.Equal(50, b.SignedDistance(new Double3(150, 0, 0)), 9);
        Assert.True(b.Touches(new Double3(105, 0, 0), 6));
        Assert.False(b.Touches(new Double3(107, 0, 0), 6));
        // Exit margin is max(2% of 100, radius 6) = 6.
        Assert.Equal(6, b.ExitMargin(6));
        Assert.True(b.FullyOutside(new Double3(112.5, 0, 0), 6, b.ExitMargin(6)));
        Assert.False(b.FullyOutside(new Double3(111, 0, 0), 6, b.ExitMargin(6)));
    }

    [Fact]
    public void EllipsoidBoundsAlongAxes()
    {
        var b = new FrameBounds(new Vector3(100, 50, 20));
        Assert.Equal(0, b.SignedDistance(new Double3(100, 0, 0)), 9);
        Assert.Equal(0, b.SignedDistance(new Double3(0, 50, 0)), 9);
        Assert.Equal(10, b.SignedDistance(new Double3(0, 0, 30)), 9);
        Assert.True(b.SignedDistance(new Double3(0, 0, 10)) < 0);
    }

    [Fact]
    public void FitsRequiresLessThanHalf()
    {
        Assert.True(FrameBounds.Fits(FrameBounds.Sphere(10), FrameBounds.Sphere(30)));
        Assert.False(FrameBounds.Fits(FrameBounds.Sphere(20), FrameBounds.Sphere(30)));
    }

    [Fact]
    public void PointGravityIsInverseSquare()
    {
        var g = GravityField.Point(3.986004418e14);
        Vector3 surface = g.At(new Double3(0, 6.371e6, 0));
        Assert.Equal(-9.82, surface.Y, 2);
        Vector3 twice = g.At(new Double3(0, 2 * 6.371e6, 0));
        Assert.Equal(surface.Y / 4, twice.Y, 3);
    }

    [Fact]
    public void UniformGravityRespectsRegion()
    {
        var g = GravityField.Uniform(new Vector3(0, -9.81f, 0), new BoundingBox(new Vector3(-10), new Vector3(10)));
        Assert.Equal(-9.81f, g.At(new Double3(0, 0, 0)).Y);
        Assert.Equal(0, g.At(new Double3(0, 20, 0)).Y);
    }

    [Fact]
    public void CircularOrbitKeepsRadiusAndSpeed()
    {
        const double mu = 3.986004418e14, r = 7e6;
        var orbit = KeplerOrbit.Circular(r);
        double speed = Math.Sqrt(mu / r);
        for (double t = 0; t < KeplerRails.Period(orbit, mu); t += KeplerRails.Period(orbit, mu) / 17)
        {
            var (p, v) = KeplerRails.StateAt(orbit, t, mu);
            Assert.Equal(r, p.Length, 3);
            Assert.Equal(speed, v.Length, 6);
            Assert.True(Math.Abs(Double3.Dot(p, v)) < 1e-3 * r * speed);
            Assert.True(Math.Abs(p.Y) < 1e-6);
        }
    }

    [Fact]
    public void EllipticOrbitConservesEnergyAndReturnsAfterOnePeriod()
    {
        var orbit = new KeplerOrbit(1e7, 0.6, 0.4, 1.0, 0.5, 0.3, 0, DQuaternion.Identity, 0, 0);
        const double mu = 3.986004418e14;
        double energy = -mu / (2 * orbit.SemiMajorAxis);
        var (p0, _) = KeplerRails.StateAt(orbit, 0, mu);
        for (double t = 0; t < KeplerRails.Period(orbit, mu); t += KeplerRails.Period(orbit, mu) / 23)
        {
            var (p, v) = KeplerRails.StateAt(orbit, t, mu);
            double e = v.LengthSquared / 2 - mu / p.Length;
            Assert.True(Math.Abs((e - energy) / energy) < 1e-9, $"energy drift at t={t}");
        }
        var (p1, _) = KeplerRails.StateAt(orbit, KeplerRails.Period(orbit, mu), mu);
        Assert.True((p1 - p0).Length < 1e-3);
    }

    [Fact]
    public void OrbitVelocityIsDerivativeOfPosition()
    {
        var orbit = new KeplerOrbit(2e7, 0.3, 0.2, 0.1, 1.1, 0, 0, DQuaternion.Identity, 0, 0);
        const double mu = 3.986004418e14, t = 1234.5, h = 1e-3;
        var (pa, _) = KeplerRails.StateAt(orbit, t - h, mu);
        var (pb, _) = KeplerRails.StateAt(orbit, t + h, mu);
        var (_, v) = KeplerRails.StateAt(orbit, t, mu);
        Double3 numeric = (pb - pa) / (2 * h);
        Assert.True((numeric - v).Length < 1e-3, $"{numeric} vs {v}");
    }

    [Fact]
    public void SpinRotatesAboutLocalY()
    {
        const double mu = 3.986004418e14;
        var orbit = KeplerOrbit.Circular(0, spinRate: Math.PI / 2);
        DQuaternion q = KeplerRails.RotationAt(orbit, 1, mu);
        Double3 x = q.Rotate(Double3.UnitX);
        Assert.True((x - new Double3(0, 0, -1)).Length < 1e-12);
        Assert.Equal(Math.PI / 2, KeplerRails.AngularVelocityAt(orbit, 0, mu).Y, 6);
    }

    [Fact]
    public void FrameTreeVelocityConversionIncludesSpin()
    {
        var frames = new FrameTree();
        FrameHandle root = frames.Create(new Entity(1, 1), FrameHandle.Null, DPose.Identity, FrameBounds.Sphere(1e12f), MotionDriver.Static);
        FrameHandle station = frames.Create(new Entity(2, 1), root, new DPose(new Double3(1000, 0, 0), DQuaternion.Identity), FrameBounds.Sphere(200), MotionDriver.Kepler);
        frames.TreeLinearVelocity(station) = new Double3(0, 0, 50);
        frames.AngularVelocity(station) = new Vector3(0, 0.1f, 0);
        frames.Update();

        // A point at rest on the station rim, 100 m out along +X.
        Double3 rim = new(100, 0, 0);
        Double3 vParent = frames.VelocityToParent(station, rim, Double3.Zero);
        // ω × r = (0, 0.1, 0) × (100, 0, 0) = (0, 0, -10).
        Assert.True((vParent - new Double3(0, 0, 40)).Length < 1e-6, vParent.ToString());

        Double3 back = frames.VelocityFromParent(station, frames.PointToParent(station, rim), vParent);
        Assert.True(back.Length < 1e-6);
    }

    [Fact]
    public void FrameTreeReparentKeepsPose()
    {
        var frames = new FrameTree();
        FrameHandle root = frames.Create(new Entity(1, 1), FrameHandle.Null, DPose.Identity, FrameBounds.Sphere(1e9f), MotionDriver.Static);
        FrameHandle carrier = frames.Create(new Entity(2, 1), root, new DPose(new Double3(5000, 0, 0), DQuaternion.FromAxisAngle(Double3.UnitY, 0.5)), FrameBounds.Sphere(400), MotionDriver.Physics);
        FrameHandle fighter = frames.Create(new Entity(3, 1), root, new DPose(new Double3(5100, 10, 0), DQuaternion.Identity), FrameBounds.Sphere(15), MotionDriver.Physics);
        frames.TreeLinearVelocity(carrier) = new Double3(10, 0, 0);
        frames.TreeLinearVelocity(fighter) = new Double3(12, 0, 0);
        frames.Update();

        DPose before = frames.Relative(fighter, root);
        frames.Reparent(fighter, carrier);
        frames.Update();

        Assert.Equal(carrier, frames.Parent(fighter));
        DPose after = frames.Relative(fighter, root);
        Assert.True((before.Position - after.Position).Length < 1e-9);
        // Relative velocity in carrier axes: (12 - 10, 0, 0) rotated into the carrier's yawed axes. Its length is 2.
        Assert.Equal(2, frames.LinearVelocity(fighter).Length, 9);
    }

    [Fact]
    public void FrameTreeRejectsStaleHandles()
    {
        var frames = new FrameTree();
        FrameHandle root = frames.Create(new Entity(1, 1), FrameHandle.Null, DPose.Identity, FrameBounds.Sphere(10), MotionDriver.Static);
        FrameHandle child = frames.Create(new Entity(2, 1), root, DPose.Identity, FrameBounds.Sphere(1), MotionDriver.Static);
        frames.Destroy(child);
        Assert.False(frames.IsAlive(child));
        Assert.Throws<InvalidOperationException>(() => frames.LocalPose(child));
        Assert.False(frames.TryGetFrame(new Entity(2, 1), out _));
    }
}
