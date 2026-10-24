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

    private const float BallRadius = 0.25f;

    /// <summary>
    /// How far a resting ball may be off the flat facet that the mesh draws, in meters, either way. Jolt would let a
    /// resting body sink into its support by up to the penetration slop, 5 mm in the engine
    /// (<see cref="JoltBackendOptions.PenetrationSlop"/>), but a ball dropped gently onto a flat facet rests within 0.03 mm
    /// of it, across a joint of two segments too. So 0.1 mm is a margin, and a shape off its mesh by more fails. Hulls
    /// that Jolt rounds leave a groove at each joint: under the ball, 4.2 mm deep on a torus for the default convex radius
    /// of 5 cm, and 0.19 mm for a radius of 1 cm.
    /// </summary>
    private const double OffFacet = 0.0001;

    /// <summary>
    /// How far a resting ball in the crease at a joint of a ring's floor may be off each of the two facets, in meters,
    /// either way. The facets meet at 2π / 256 short of flat, so the bottom of the crease is a shallow trough, and the ball
    /// comes to rest up to a few millimeters off its bottom, nearer to one facet's plane: 0.12 mm off it in the test. So
    /// 0.25 mm is a margin. Hulls rounded by the default convex radius of 5 cm leave a groove 3.7 mm deep there.
    /// </summary>
    private const double OffCrease = 0.00025;

    private static readonly RevolvedSolid Cylinder =
        RevolvedSolid.Cylinder(CylinderRadius, CylinderHalfHeight, CylinderSegments);

    private static readonly RevolvedSolid Ring = RevolvedSolid.Ring(innerRadius: RingInner, outerRadius: RingOuter, halfWidth: RingHalfWidth, segments: RingSegments);

    private static readonly RevolvedSolid
        Torus = RevolvedSolid.Torus(majorRadius: TorusMajor, minorRadius: TorusMinor, segments: TorusSegments, sides: TorusSides);

    /// <summary>
    /// A ring standing on edge: its axis along Z, and the point at angle θ (ρ cos θ, ρ sin θ, y), so its lowest point is at
    /// θ = 3π / 2.
    /// </summary>
    private static readonly DQuaternion OnEdge = DQuaternion.FromAxisAngle(Double3.UnitX, Math.PI / 2);

    private static Double3 At(double rho, double theta, double y) =>
        new(rho * Math.Cos(theta), y, -rho * Math.Sin(theta));

    /// <summary>A turn of <paramref name="angle"/> radians about the Y axis, which moves the point at angle θ to θ + angle.</summary>
    private static DQuaternion AboutY(double angle) => DQuaternion.FromAxisAngle(Double3.UnitY, angle);

    /// <summary>
    /// Spawns <paramref name="solid"/> as a static body at the origin of a root frame, turned by <paramref name="turn"/>, in
    /// uniform gravity of 9.81 m/s² along <paramref name="down"/>. Drops a ball of radius <see cref="BallRadius"/> from
    /// <paramref name="from"/>, runs 3 s, and returns where the ball is then and how fast it moves.
    /// </summary>
    private static (Double3 Position, double Speed) Drop(RevolvedSolid solid, DQuaternion turn, Double3 from,
        Double3 down)
    {
        using World world = NewWorld(backend: out _);
        FrameHandle frame =
            world.CreateRootFrame(new(FrameBounds.Sphere(1e6f), GravityField.Uniform((9.81 * down).ToVector3())));
        world.Spawn(new()
            { Frame = frame, Pose = new(Double3.Zero, turn), Body = new(Shape: solid.Shape(), Motion: BodyMotion.Static) });
        Entity ball = world.Spawn(new()
        {
            Frame = frame, Pose = new(from, DQuaternion.Identity), Body = new(Shape: new SphereShapeDesc(BallRadius), Mass: 1)
        });
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
        (height - BallRadius).Should()
            .BeInRange(-OffFacet, OffFacet, "the ball rests on the facet that the mesh draws");
    }

    [Fact]
    public void ABallDroppedOnTheCapOfACylinderRestsOnTheCap()
    {
        (Double3 position, double speed) = Drop(solid: Cylinder, turn: DQuaternion.Identity,
            from: new(0.4, CylinderHalfHeight + BallRadius + 0.1, 0.3), down: -Double3.UnitY);

        ExpectRestingOnFacet(position.Y - CylinderHalfHeight, speed);
        (new Double3(position.X, 0, position.Z) - new Double3(0.4, 0, 0.3)).Length.Should()
            .BeLessThan(1e-3, "the cap is flat, so the ball stays where it landed");
    }

    [Fact]
    public void ABallDroppedOnTheCenterOfACylindersCapRestsOnTheCap()
    {
        // Every segment's triangle of the cap's mesh has a corner at its center.
        (Double3 position, double speed) = Drop(solid: Cylinder, turn: DQuaternion.Identity,
            from: new(0, CylinderHalfHeight + BallRadius + 0.1, 0), down: -Double3.UnitY);

        ExpectRestingOnFacet(position.Y - CylinderHalfHeight, speed);
    }

    [Fact]
    public void ABallDroppedNearTheRimOfACylindersCapRestsOnTheCap()
    {
        // The ball's center is 5 mm inside the rim, across the middle of segment 0, so it rests on the flat cap there.
        // A hull with rounded edges would slope down toward the rim under it, and the ball would roll off.
        Double3 landing = At(CylinderRadius * Math.Cos(Math.PI / CylinderSegments) - 0.005, Math.PI / CylinderSegments,
            0);

        (Double3 position, double speed) = Drop(solid: Cylinder, turn: DQuaternion.Identity,
            from: landing + new Double3(0, CylinderHalfHeight + BallRadius + 0.1, 0), down: -Double3.UnitY);

        ExpectRestingOnFacet(position.Y - CylinderHalfHeight, speed);
        (new Double3(position.X, 0, position.Z) - landing).Length.Should().BeLessThan(1e-3,
            "the cap is flat out to its rim, so the ball stays where it landed");
    }

    [Fact]
    public void ABallPulledTowardTheAxisRestsOnTheMiddleOfAFacetOfACylindersSide()
    {
        // Gravity pulls straight at the middle of segment 0's facet.
        Double3 outward = At(1, Math.PI / CylinderSegments, 0);

        (Double3 position, double speed) = Drop(solid: Cylinder, turn: DQuaternion.Identity,
            from: (CylinderRadius + BallRadius + 0.1) * outward, down: -outward);

        double facet = CylinderRadius * Math.Cos(Math.PI / CylinderSegments);
        ExpectRestingOnFacet(Double3.Dot(position, outward) - facet, speed);
    }

    [Fact]
    public void ABallInsideARingOnEdgeRestsOnItsLowestFacet()
    {
        // Turned by half a segment about its axis, the ring has the middle of a facet at its lowest point.
        double facet = RingInner * Math.Cos(Math.PI / RingSegments);

        (Double3 position, double speed) = Drop(solid: Ring, turn: OnEdge * AboutY(Math.PI / RingSegments),
            from: new(0, -facet + BallRadius + 0.1, 0), down: -Double3.UnitY);

        ExpectRestingOnFacet(position.Y + facet, speed);
    }

    [Fact]
    public void ABallInsideARingOnEdgeRestsInTheCreaseAtItsLowestJoint()
    {
        // Untwisted, the ring has the joint of segments 191 and 192 at its lowest point. The ball touches both of their
        // facets, whose middles are half a segment to either side.
        (Double3 position, double speed) = Drop(solid: Ring, turn: OnEdge, from: new(0, -RingInner + BallRadius + 0.1, 0), down: -Double3.UnitY);

        speed.Should().BeLessThan(0.01, "the ball has come to rest");
        double facet = RingInner * Math.Cos(Math.PI / RingSegments);
        foreach (double middle in new[]
                     { 1.5 * Math.PI - Math.PI / RingSegments, 1.5 * Math.PI + Math.PI / RingSegments })
        {
            // The facet's plane is facet from the axis across the direction to its middle, and the ball is on the axis's side.
            var toMiddle = new Double3(Math.Cos(middle), Math.Sin(middle), 0);
            (facet - Double3.Dot(position, toMiddle) - BallRadius).Should().BeInRange(-OffCrease, OffCrease,
                "the ball rests in the crease, on both facets");
        }
    }

    [Fact]
    public void ABallOnTopOfARingOnEdgeRestsOnItsHighestFacet()
    {
        double facet = RingOuter * Math.Cos(Math.PI / RingSegments);

        (Double3 position, double speed) = Drop(solid: Ring, turn: OnEdge * AboutY(Math.PI / RingSegments),
            from: new(0, facet + BallRadius + 0.1, 0), down: -Double3.UnitY);

        ExpectRestingOnFacet(position.Y - facet, speed);
    }

    [Fact]
    public void ABallDroppedOnATorusRestsOnTheFlatTopOfItsTube()
    {
        // The tube's top side is flat, at r cos(π / 24), and the ball lands in the middle of segment 0's facet there.
        double top = TorusMinor * Math.Cos(Math.PI / TorusSides);
        Double3 landing = At(TorusMajor * Math.Cos(Math.PI / TorusSegments), Math.PI / TorusSegments, 0);

        (Double3 position, double speed) = Drop(solid: Torus, turn: DQuaternion.Identity,
            from: landing + new Double3(0, top + BallRadius + 0.1, 0), down: -Double3.UnitY);

        ExpectRestingOnFacet(position.Y - top, speed);
        (new Double3(position.X, 0, position.Z) - landing).Length.Should()
            .BeLessThan(1e-3, "the facet is flat, so the ball stays where it landed");
    }

    [Fact]
    public void ABallDroppedOnATorusAtAJointRestsOnTheFlatTopOfItsTube()
    {
        // The joint of segments 63 and 0 is on +X. Exactly on the joint, the ball touches both hulls' flat tops alike.
        double top = TorusMinor * Math.Cos(Math.PI / TorusSides);
        Double3 landing = At(TorusMajor, 0, 0);

        (Double3 position, double speed) = Drop(solid: Torus, turn: DQuaternion.Identity,
            from: landing + new Double3(0, top + BallRadius + 0.1, 0), down: -Double3.UnitY);

        ExpectRestingOnFacet(position.Y - top, speed);
        (new Double3(position.X, 0, position.Z) - landing).Length.Should().BeLessThan(1e-3,
            "the top is flat across the joint, so the ball stays where it landed");
    }

    /// <summary>A query filter that accepts every entity.</summary>
    private sealed class AcceptsEveryEntity : IPhysicsQueryFilter
    {
        public bool Accepts(Entity entity) => true;
    }

    /// <summary>
    /// Splits a turn into <paramref name="arcs"/> equal arcs, the first from the angle 0. For each arc, and for three angles
    /// inside it, a quarter, a half and three quarters of the way across, casts the ray that <paramref name="ray"/> gives
    /// for the ray's angle, against a static body of <paramref name="solid"/> at the origin of a frame without gravity.
    /// Then checks that it hits at the distance and with the normal that <paramref name="expect"/> gives for the arc's
    /// middle angle and the ray's angle, or at the distance alone when it gives no normal. Each arc is a segment of the
    /// solid, or a side of a torus's tube.
    /// </summary>
    private static void ExpectRaysHit(RevolvedSolid solid, int arcs,
        Func<double, (Double3 Origin, Double3 Direction)> ray,
        Func<double, double, (double Distance, Vector3? Normal)> expect)
    {
        using World world = NewWorld(backend: out JoltPhysicsBackend backend);
        FrameHandle frame = world.CreateRootFrame(new(FrameBounds.Sphere(1e6f)));
        world.Spawn(new() { Frame = frame, Pose = DPose.Identity, Body = new(Shape: solid.Shape(), Motion: BodyMotion.Static) });
        world.Tick();
        var hits = new List<RayCastHit>();
        for (int k = 0; k < arcs; k++)
        {
            double middle = (k + 0.5) * Math.Tau / arcs;
            foreach (double across in new[] { 0.25, 0.5, 0.75 })
            {
                double angle = (k + across) * Math.Tau / arcs;
                (Double3 origin, Double3 direction) = ray(angle);
                (double distance, Vector3? normal) = expect(middle, angle);
                hits.Clear();
                backend.CastRay(frame: frame, origin: origin, direction: direction, start: 0, end: 100, filter: new AcceptsEveryEntity(), kind: RayCastKind.Nearest, hits: hits);
                hits.Should().ContainSingle($"the ray at {angle} in arc {k} meets the shape");
                hits[0].Distance.Should()
                    .BeApproximately(distance, 1e-4, $"the ray at {angle} in arc {k} meets the facet");
                if (normal is { } facing)
                    (hits[0].Normal - facing).Length().Should()
                        .BeLessThan(1e-4f, $"the facet of arc {k} faces {facing}");
            }
        }
    }

    [Fact]
    public void RaysFromOutsideACylinderMeetTheFacetsOfItsSideAllAround()
    {
        double facet = CylinderRadius * Math.Cos(Math.PI / CylinderSegments);

        ExpectRaysHit(solid: Cylinder, arcs: CylinderSegments,
            ray: angle => (At(5, angle, 0.3), -At(1, angle, 0)),
            expect: (middle, angle) => (5 - facet / Math.Cos(angle - middle), At(1, middle, 0).ToVector3()));
    }

    [Fact]
    public void RaysFromTheAxisOfARingMeetTheFacetsOfItsFloorAllAround()
    {
        double facet = RingInner * Math.Cos(Math.PI / RingSegments);

        ExpectRaysHit(solid: Ring, arcs: RingSegments,
            ray: angle => (new(0, 0.7, 0), At(1, angle, 0)),
            expect: (middle, angle) => (facet / Math.Cos(angle - middle), -At(1, middle, 0).ToVector3()));
    }

    [Fact]
    public void RaysFromOutsideARingMeetTheFacetsOfItsOuterFaceAllAround()
    {
        double facet = RingOuter * Math.Cos(Math.PI / RingSegments);

        ExpectRaysHit(solid: Ring, arcs: RingSegments,
            ray: angle => (At(30, angle, -1.1), -At(1, angle, 0)),
            expect: (middle, angle) => (30 - facet / Math.Cos(angle - middle), At(1, middle, 0).ToVector3()));
    }

    [Fact]
    public void RaysFromAboveATorusMeetTheFlatTopOfItsTubeAllAround()
    {
        double top = TorusMinor * Math.Cos(Math.PI / TorusSides);

        ExpectRaysHit(solid: Torus, arcs: TorusSegments,
            ray: angle => (At(TorusMajor, angle, 5), -Double3.UnitY),
            expect: (_, _) => (5 - top, Vector3.UnitY));
    }

    [Theory]
    [InlineData(1f, 128)]
    [InlineData(0.1f, 44)]
    public void RaysFromOutsideACylinderWhoseSideBendsByJustOver1MillimeterMeetTheFacetsOfItsSideAllAround(float radius,
        int segments)
    {
        // The side bends by 1.2 mm and by 1.02 mm at each edge between two facets, just over the 1 mm within which Jolt
        // drops a corner of a hull. A hull without some corners would put some facets up to the bend inside the mesh.
        double facet = radius * Math.Cos(Math.PI / segments);

        ExpectRaysHit(solid: RevolvedSolid.Cylinder(radius, CylinderHalfHeight, segments), arcs: segments,
            ray: angle => (At(5, angle, 0.3), -At(1, angle, 0)),
            expect: (middle, angle) => (5 - facet / Math.Cos(angle - middle), At(1, middle, 0).ToVector3()));
    }

    [Fact]
    public void RaysAcrossATorusTubeOf128SidesMeetEachSide()
    {
        // The tube of 1 m bends by 1.2 mm at each edge between two sides. The rays lie in the plane through the axis and
        // +X, the joint of the last segment and the first, where the tube's cross-section is its profile: side j runs
        // between the corners at the angles 2π (j + 1/2) / 128 about the tube's center, from +X toward +Y, so the arcs of
        // the helper start half a side later than the sides. Each ray comes in toward the tube's center. It lies in the
        // face that the two hulls share there, so Jolt may give the normal of that face, and only the distance counts.
        const int sides = 128;
        double halfSide = Math.PI / sides, side = TorusMinor * Math.Cos(halfSide);
        Double3 center = new(TorusMajor, 0, 0);
        static Double3 Across(double angle) => new(Math.Cos(angle), Math.Sin(angle), 0);

        ExpectRaysHit(solid: RevolvedSolid.Torus(majorRadius: TorusMajor, minorRadius: TorusMinor, segments: 8, sides: sides), arcs: sides,
            ray: angle => (center + 5 * Across(angle + halfSide), -Across(angle + halfSide)),
            expect: (middle, angle) => (5 - side / Math.Cos(angle - middle), null));
    }

    [Fact]
    public void RaysFromOutsideATorusMeetTheFacetsOfItsOuterSideAllAround()
    {
        // The tube's outermost side is upright, r cos(π / 24) out from the tube's center.
        double facet = (TorusMajor + TorusMinor * Math.Cos(Math.PI / TorusSides)) * Math.Cos(Math.PI / TorusSegments);

        ExpectRaysHit(solid: Torus, arcs: TorusSegments,
            ray: angle => (At(10, angle, 0.1), -At(1, angle, 0)),
            expect: (middle, angle) => (10 - facet / Math.Cos(angle - middle), At(1, middle, 0).ToVector3()));
    }
}
