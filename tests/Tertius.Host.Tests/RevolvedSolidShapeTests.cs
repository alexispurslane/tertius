using System.Numerics;
using Tertius.Demo;
using Tertius.Physics;
using Tertius.Simulation;
using static Tertius.Testing.WorldTesting;

namespace Tertius.Host.Tests;

/// <summary>
/// The collision shapes of <see cref="RevolvedSolid"/>, on the real World and Jolt: a body rests on each shape where its
/// mesh is, and rays meet each shape at the mesh's facets all the way around. A solid turns about the Y axis in equal
/// segments: segment k spans the angles 2π k / n to 2π (k + 1) / n, measured from +X toward -Z, so the point at angle θ, a
/// distance ρ from the axis and at height y, is (ρ cos θ, y, -ρ sin θ). Each segment's facet is flat, so the middle of a
/// facet is ρ cos(π / n) from the axis where its edges are ρ from it. Every expectation comes from that geometry, worked
/// out here.
/// </summary>
public sealed class RevolvedSolidShapeTests
{
    private const float CylinderRadius = 1, CylinderHalfHeight = 0.75f;
    private const int CylinderSegments = 48;
    private const float RingInner = 20, RingOuter = 20.5f, RingHalfWidth = 2;
    private const int RingSegments = 256;
    private const float TorusMajor = 3, TorusMinor = 1;
    private const int TorusSegments = 64, TorusSides = 24;

    private static readonly RevolvedSolid Cylinder = RevolvedSolid.Cylinder(CylinderRadius, CylinderHalfHeight, CylinderSegments);
    private static readonly RevolvedSolid Ring = RevolvedSolid.Ring(RingInner, RingOuter, RingHalfWidth, RingSegments);
    private static readonly RevolvedSolid Torus = RevolvedSolid.Torus(TorusMajor, TorusMinor, TorusSegments, TorusSides);

    private const float BallRadius = 0.25f;

    /// <summary>
    /// How far a resting ball may be off the facet that the mesh draws, in meters, either way. Jolt would let a resting
    /// body sink into its support by up to the penetration slop, 5 mm in the engine
    /// (<see cref="JoltBackendOptions.PenetrationSlop"/>), but a ball dropped gently onto a flat facet rests within 0.1 mm
    /// of it. So 1 mm is a margin for rounding, and a shape off its mesh by more fails.
    /// </summary>
    private const double OffFacet = 0.001;

    /// <summary>
    /// How far a ball at the joint of two segments may sink below the mesh's facets, in meters. Jolt rounds the edges of
    /// each hull by its convex radius of 5 cm, so at a joint the two rounded edges leave a groove. A ball of 0.25 m in
    /// radius sinks 3.7 mm into the groove in a ring's floor, and 4.2 mm on a torus.
    /// </summary>
    private const double Groove = 0.005;

    private static Double3 At(double rho, double theta, double y) => new(rho * Math.Cos(theta), y, -rho * Math.Sin(theta));

    /// <summary>A turn of <paramref name="angle"/> radians about the Y axis, which moves the point at angle θ to θ + angle.</summary>
    private static DQuaternion AboutY(double angle) => DQuaternion.FromAxisAngle(Double3.UnitY, angle);

    /// <summary>
    /// A ring standing on edge: its axis along Z, and the point at angle θ (ρ cos θ, ρ sin θ, y), so its lowest point is at
    /// θ = 3π / 2.
    /// </summary>
    private static readonly DQuaternion OnEdge = DQuaternion.FromAxisAngle(Double3.UnitX, Math.PI / 2);

    /// <summary>
    /// Spawns <paramref name="solid"/> as a static body at the origin of a root frame, turned by <paramref name="turn"/>, in
    /// uniform gravity of 9.81 m/s² along <paramref name="down"/>. Drops a ball of radius <see cref="BallRadius"/> from
    /// <paramref name="from"/>, runs 3 s, and returns where the ball is then and how fast it moves.
    /// </summary>
    private static (Double3 Position, double Speed) Drop(RevolvedSolid solid, DQuaternion turn, Double3 from, Double3 down)
    {
        using World world = NewWorld(out _);
        FrameHandle frame = world.CreateRootFrame(new FrameSpec(FrameBounds.Sphere(1e6f), GravityField.Uniform((9.81 * down).ToVector3())));
        world.Spawn(new SpawnDesc { Frame = frame, Pose = new DPose(Double3.Zero, turn), Body = new BodyDesc(solid.Shape(), BodyMotion.Static) });
        Entity ball = world.Spawn(new SpawnDesc { Frame = frame, Pose = new DPose(from, DQuaternion.Identity), Body = new BodyDesc(new SphereShapeDesc(BallRadius), Mass: 1) });
        Tick(world, 180);
        world.Errors.Should().BeEmpty();
        return (world.PoseIn(ball, frame).Position, world.VelocityIn(ball, frame).Length);
    }

    /// <summary>
    /// The ball has come to rest with its center <paramref name="height"/> from the plane of a facet, on the side that the
    /// facet faces, which the mesh puts at <see cref="BallRadius"/>.
    /// </summary>
    private static void ExpectRestingOnFacet(double height, double speed)
    {
        speed.Should().BeLessThan(0.01, "the ball has come to rest");
        (height - BallRadius).Should().BeInRange(-OffFacet, OffFacet, "the ball rests on the facet that the mesh draws");
    }

    [Fact]
    public void ABallDroppedOnTheCapOfACylinderRestsOnTheCap()
    {
        var (position, speed) = Drop(Cylinder, DQuaternion.Identity, new Double3(0.4, CylinderHalfHeight + BallRadius + 0.1, 0.3), -Double3.UnitY);

        ExpectRestingOnFacet(position.Y - CylinderHalfHeight, speed);
        (new Double3(position.X, 0, position.Z) - new Double3(0.4, 0, 0.3)).Length.Should().BeLessThan(1e-3, "the cap is flat, so the ball stays where it landed");
    }

    [Fact]
    public void ABallDroppedOnTheCenterOfACylindersCapRestsOnTheCap()
    {
        // Every segment's facet of the cap has a corner at its center.
        var (position, speed) = Drop(Cylinder, DQuaternion.Identity, new Double3(0, CylinderHalfHeight + BallRadius + 0.1, 0), -Double3.UnitY);

        ExpectRestingOnFacet(position.Y - CylinderHalfHeight, speed);
    }

    [Fact]
    public void ABallPulledTowardTheAxisRestsOnTheMiddleOfAFacetOfACylindersSide()
    {
        // Gravity pulls straight at the middle of segment 0's facet.
        Double3 outward = At(1, Math.PI / CylinderSegments, 0);

        var (position, speed) = Drop(Cylinder, DQuaternion.Identity, (CylinderRadius + BallRadius + 0.1) * outward, -outward);

        double facet = CylinderRadius * Math.Cos(Math.PI / CylinderSegments);
        ExpectRestingOnFacet(Double3.Dot(position, outward) - facet, speed);
    }

    [Fact]
    public void ABallInsideARingOnEdgeRestsOnItsLowestFacet()
    {
        // Turned by half a segment about its axis, the ring has the middle of a facet at its lowest point.
        double facet = RingInner * Math.Cos(Math.PI / RingSegments);

        var (position, speed) = Drop(Ring, OnEdge * AboutY(Math.PI / RingSegments), new Double3(0, -facet + BallRadius + 0.1, 0), -Double3.UnitY);

        ExpectRestingOnFacet(position.Y + facet, speed);
    }

    [Fact]
    public void ABallInsideARingOnEdgeRestsInTheCreaseAtItsLowestJoint()
    {
        // Untwisted, the ring has the joint of segments 191 and 192 at its lowest point. The ball touches both of their
        // facets, whose middles are half a segment to either side.
        var (position, speed) = Drop(Ring, OnEdge, new Double3(0, -RingInner + BallRadius + 0.1, 0), -Double3.UnitY);

        speed.Should().BeLessThan(0.01, "the ball has come to rest");
        double facet = RingInner * Math.Cos(Math.PI / RingSegments);
        foreach (double middle in new[] { 1.5 * Math.PI - Math.PI / RingSegments, 1.5 * Math.PI + Math.PI / RingSegments })
        {
            // The facet's plane is facet from the axis across the direction to its middle, and the ball is on the axis's side.
            var toMiddle = new Double3(Math.Cos(middle), Math.Sin(middle), 0);
            (facet - Double3.Dot(position, toMiddle) - BallRadius).Should().BeInRange(-Groove, OffFacet, "the ball rests in the crease, or in the groove that Jolt's rounding leaves there");
        }
    }

    [Fact]
    public void ABallOnTopOfARingOnEdgeRestsOnItsHighestFacet()
    {
        double facet = RingOuter * Math.Cos(Math.PI / RingSegments);

        var (position, speed) = Drop(Ring, OnEdge * AboutY(Math.PI / RingSegments), new Double3(0, facet + BallRadius + 0.1, 0), -Double3.UnitY);

        ExpectRestingOnFacet(position.Y - facet, speed);
    }

    [Fact]
    public void ABallDroppedOnATorusRestsOnTheFlatTopOfItsTube()
    {
        // The tube's top side is flat, at r cos(π / 24), and the ball lands in the middle of segment 0's facet there.
        double top = TorusMinor * Math.Cos(Math.PI / TorusSides);
        Double3 landing = At(TorusMajor * Math.Cos(Math.PI / TorusSegments), Math.PI / TorusSegments, 0);

        var (position, speed) = Drop(Torus, DQuaternion.Identity, landing + new Double3(0, top + BallRadius + 0.1, 0), -Double3.UnitY);

        ExpectRestingOnFacet(position.Y - top, speed);
        (new Double3(position.X, 0, position.Z) - landing).Length.Should().BeLessThan(1e-3, "the facet is flat, so the ball stays where it landed");
    }

    /// <summary>A query filter that accepts every entity.</summary>
    private sealed class AcceptsEveryEntity : IPhysicsQueryFilter
    {
        public bool Accepts(Entity entity) => true;
    }

    /// <summary>
    /// For each segment of <paramref name="segments"/>, and for three angles inside it, a quarter, a half and three quarters
    /// of the way across, casts the ray that <paramref name="ray"/> gives for the segment's middle angle and the ray's own
    /// angle, against a static body of <paramref name="solid"/> at the origin of a frame without gravity. Then checks that
    /// it hits at the distance and with the normal that <paramref name="expect"/> gives.
    /// </summary>
    private static void ExpectRaysHit(RevolvedSolid solid, int segments,
        Func<double, (Double3 Origin, Double3 Direction)> ray, Func<double, double, (double Distance, Vector3 Normal)> expect)
    {
        using World world = NewWorld(out JoltPhysicsBackend backend);
        FrameHandle frame = world.CreateRootFrame(new FrameSpec(FrameBounds.Sphere(1e6f)));
        world.Spawn(new SpawnDesc { Frame = frame, Pose = DPose.Identity, Body = new BodyDesc(solid.Shape(), BodyMotion.Static) });
        world.Tick();
        var hits = new List<RayCastHit>();
        for (int k = 0; k < segments; k++)
        {
            double middle = (k + 0.5) * Math.Tau / segments;
            foreach (double across in new[] { 0.25, 0.5, 0.75 })
            {
                double angle = (k + across) * Math.Tau / segments;
                (Double3 origin, Double3 direction) = ray(angle);
                (double distance, Vector3 normal) = expect(middle, angle);
                hits.Clear();
                backend.CastRay(frame, origin, direction, 0, 100, new AcceptsEveryEntity(), RayCastKind.Nearest, hits);
                hits.Should().ContainSingle($"the ray at {angle} in segment {k} meets the shape");
                hits[0].Distance.Should().BeApproximately(distance, 1e-4, $"the ray at {angle} in segment {k} meets the facet");
                (hits[0].Normal - normal).Length().Should().BeLessThan(1e-4f, $"the facet of segment {k} faces {normal}");
            }
        }
    }

    [Fact]
    public void RaysFromOutsideACylinderMeetTheFacetsOfItsSideAllAround()
    {
        double facet = CylinderRadius * Math.Cos(Math.PI / CylinderSegments);

        ExpectRaysHit(Cylinder, CylinderSegments,
            angle => (At(5, angle, 0.3), -At(1, angle, 0)),
            (middle, angle) => (5 - facet / Math.Cos(angle - middle), At(1, middle, 0).ToVector3()));
    }

    [Fact]
    public void RaysFromTheAxisOfARingMeetTheFacetsOfItsFloorAllAround()
    {
        double facet = RingInner * Math.Cos(Math.PI / RingSegments);

        ExpectRaysHit(Ring, RingSegments,
            angle => (new Double3(0, 0.7, 0), At(1, angle, 0)),
            (middle, angle) => (facet / Math.Cos(angle - middle), -At(1, middle, 0).ToVector3()));
    }

    [Fact]
    public void RaysFromOutsideARingMeetTheFacetsOfItsOuterFaceAllAround()
    {
        double facet = RingOuter * Math.Cos(Math.PI / RingSegments);

        ExpectRaysHit(Ring, RingSegments,
            angle => (At(30, angle, -1.1), -At(1, angle, 0)),
            (middle, angle) => (30 - facet / Math.Cos(angle - middle), At(1, middle, 0).ToVector3()));
    }

    [Fact]
    public void RaysFromAboveATorusMeetTheFlatTopOfItsTubeAllAround()
    {
        double top = TorusMinor * Math.Cos(Math.PI / TorusSides);

        ExpectRaysHit(Torus, TorusSegments,
            angle => (At(TorusMajor, angle, 5), -Double3.UnitY),
            (_, _) => (5 - top, Vector3.UnitY));
    }

    [Fact]
    public void RaysFromOutsideATorusMeetTheFacetsOfItsOuterSideAllAround()
    {
        // The tube's outermost side is upright, r cos(π / 24) out from the tube's center.
        double facet = (TorusMajor + TorusMinor * Math.Cos(Math.PI / TorusSides)) * Math.Cos(Math.PI / TorusSegments);

        ExpectRaysHit(Torus, TorusSegments,
            angle => (At(10, angle, 0.1), -At(1, angle, 0)),
            (middle, angle) => (10 - facet / Math.Cos(angle - middle), At(1, middle, 0).ToVector3()));
    }

}
