using System.Numerics;
using Tertius.Demo;
using Tertius.Simulation;
using static Tertius.Testing.WorldTesting;

namespace Tertius.Host.Tests;

/// <summary>
/// Bodies at the joints between the segments of a <see cref="RevolvedSolid"/>, on the real World and Jolt, on both of the
/// solid's collision shapes: the compound of one hull of convex radius 0 per segment (<see cref="RevolvedSolid.Shape"/>),
/// and Jolt's mesh shape of its mesh. A body near a joint also meets the edge of the neighbor segment there, and Jolt's
/// contact with that edge tilts away from the joint. Jolt's enhanced internal edge removal, which physics sets on every
/// body whose shape sharp pieces tile, as both of these shapes are, and on every character, drops such contacts beyond a
/// few millimeters from the joint (ter-fb3w). Without it, a ball that lands near a joint moves away from it, a box that
/// slides across the joints of the compound trips over each one, and a character that walks across the joints of the
/// compound is lifted at each one. Angles follow <see cref="RevolvedSolid"/>: θ runs from +X toward -Z, and segment k of n spans 2π k / n to
/// 2π (k + 1) / n.
/// </summary>
public sealed class RevolvedSolidJointTests
{
    public enum Floor
    {
        Hulls,
        Mesh,
    }

    private const int RingSegments = 256;
    private const float RingInner = 20;
    private const int TorusSegments = 64, TorusSides = 24;
    private const float TorusMajor = 3, TorusMinor = 1;

    private static readonly RevolvedSolid Ring = RevolvedSolid.Ring(RingInner, 20.5f, 2, RingSegments);
    private static readonly RevolvedSolid Torus = RevolvedSolid.Torus(TorusMajor, TorusMinor, TorusSegments, TorusSides);

    private const float BallRadius = 0.25f;

    /// <summary>The length of a tick of the test world, in s.</summary>
    private const double TickSeconds = 1.0 / 60;

    /// <summary>
    /// How far, in meters, a ball that lands near a joint may move along the floor. It lands on a level facet, so nothing
    /// but the contact at the joint moves it along the facet. Without edge removal, it moves 17 mm when it lands 10 mm from
    /// a joint of a torus's top, and 5 mm when it lands 20 mm from a joint of a ring's floor. With it, it moves at most about
    /// 0.01 mm. So 0.5 mm is a margin.
    /// </summary>
    private const double Sideways = 0.0005;

    private static ShapeDesc ShapeOf(RevolvedSolid solid, Floor floor)
    {
        if (floor == Floor.Hulls)
            return solid.Shape();
        MeshData mesh = solid.Mesh();
        return new MeshShapeDesc([.. mesh.Vertices.Select(v => v.Position)], [.. mesh.Indices.Select(i => (int)i)]);
    }

    /// <summary>The direction from the axis at the angle <paramref name="theta"/>, in the plane y = 0.</summary>
    private static Double3 Radial(double theta) => new(Math.Cos(theta), 0, -Math.Sin(theta));

    /// <summary>The direction of growing θ at the angle <paramref name="theta"/>, in the plane y = 0.</summary>
    private static Double3 Along(double theta) => new(-Math.Sin(theta), 0, -Math.Cos(theta));

    /// <summary>
    /// Spawns <paramref name="solid"/> as a static body of <paramref name="floor"/> at the origin of a root frame with a
    /// uniform gravity of 9.81 m/s² along <paramref name="down"/>, and drops a ball of radius <see cref="BallRadius"/> from
    /// 10 cm above the place where it would rest on <paramref name="point"/>, a point of a facet whose plane is
    /// perpendicular to <paramref name="down"/>. After 3 s, returns how far the ball's center is from that place along
    /// <paramref name="away"/>, a direction along the facet.
    /// </summary>
    private static double Landing(RevolvedSolid solid, Floor floor, Double3 point, Double3 down, Double3 away)
    {
        using World world = NewWorld(out _);
        FrameHandle frame = world.CreateRootFrame(new FrameSpec(FrameBounds.Sphere(1e6f), GravityField.Uniform((9.81 * down).ToVector3())));
        world.Spawn(new SpawnDesc { Frame = frame, Body = new BodyDesc(ShapeOf(solid, floor), BodyMotion.Static) });
        Double3 rest = point - BallRadius * down;
        Entity ball = world.Spawn(new SpawnDesc { Frame = frame, Pose = new DPose(rest - 0.1 * down, DQuaternion.Identity), Body = new BodyDesc(new SphereShapeDesc(BallRadius), Mass: 1) });
        Tick(world, 180);
        world.Errors.Should().BeEmpty();
        return Double3.Dot(world.PoseIn(ball, frame).Position - rest, away);
    }

    /// <summary>
    /// The top of the torus's tube is flat, at the height r cos(π / 24), and the hulls of two segments meet in the plane
    /// through the axis at θ = 0. The ball lands on segment 0, 3 m from the axis, 10 mm from that joint.
    /// </summary>
    [Theory]
    [InlineData(Floor.Hulls)]
    [InlineData(Floor.Mesh)]
    public void ABallThatLands10MillimetersFromAJointOfATorusTopStaysWhereItLands(Floor floor)
    {
        double theta = 0.010 / TorusMajor;
        Double3 point = TorusMajor * Radial(theta) + new Double3(0, TorusMinor * Math.Cos(Math.PI / TorusSides), 0);

        double side = Landing(Torus, floor, point, -Double3.UnitY, Along(theta));

        side.Should().BeInRange(-Sideways, Sideways, "the top is flat across the joint, and edge removal drops the contact with the neighbor segment's edge");
    }

    /// <summary>
    /// Gravity pulls along the outward normal of facet 0 of the ring's floor, so that facet is level, and facet 255 rises
    /// from their joint at θ = 0 by 2π / 256, a shallow crease. The ball lands on facet 0, 20 mm from the joint, where
    /// it touches only facet 0: it would touch the rising facet only 3.1 mm from the joint.
    /// </summary>
    [Theory]
    [InlineData(Floor.Hulls)]
    [InlineData(Floor.Mesh)]
    public void ABallThatLands20MillimetersFromAJointOfARingsFloorStaysWhereItLands(Floor floor)
    {
        Double3 corner = RingInner * Radial(0), next = RingInner * Radial(Math.Tau / RingSegments);
        Double3 along = (next - corner).Normalized();
        Double3 point = corner + 0.020 * along + new Double3(0, 0.3, 0);

        double side = Landing(Ring, floor, point, Radial(Math.PI / RingSegments), along);

        side.Should().BeInRange(-Sideways, Sideways, "the facet is level, and edge removal drops the contact with the neighbor facet's edge");
    }

    /// <summary>
    /// Holds the speed of its body along the ring's floor, about the Y axis toward growing θ, at 1 m/s, and leaves the
    /// rest of its velocity to Jolt.
    /// </summary>
    private sealed class Pusher : IComponent
    {
        public int Priority => 0;

        public EventMask Wants => EventMask.Of<Tertius.Tick>();

        public void Handle(Self self, scoped ref EventList events, scoped ref Outbox outbox)
        {
            Double3 p = self.PoseIn(self.Frame).Position;
            Double3 along = Along(Math.Atan2(-p.Z, p.X));
            Double3 v = self.VelocityIn(self.Frame);
            self.Body.SetVelocity(v - Double3.Dot(v, along) * along + 1.0 * along);
        }
    }

    /// <summary>
    /// A box of 0.4 m and 10 kg slides at 1 m/s along the ring's floor, from the middle of facet 250 to the middle of facet
    /// 4, across the ten joints between them, with the seam between the last segment and the first in the middle. Gravity
    /// is uniform, 9.81 m/s² along +X, toward the seam. At each tick, the box's lift is the height of the lowest of the
    /// four corners of its bottom face above the facet under that corner, and its pitch is the angle between its up and
    /// the direction to the axis through its center, along the floor. A box on a facet pitches by up to the facet's own
    /// tilt from that direction, π / 256 (0.70°) at the facet's edges, and it sinks into the floor by up to the penetration
    /// slop, so its lift is at most 0 when it stands on the floor. Without edge removal, the box trips at each joint and
    /// tumbles: its lift reaches 40 cm and its pitch 180°. With it, the lift stays at 0 and the pitch below 1.05°.
    /// </summary>
    [Fact]
    public void ABoxThatSlidesAcrossTenJointsOfARingsHullsStaysFlatOnTheFloor()
    {
        const double half = 0.2, rounding = ShapeDesc.DefaultConvexRadius, step = Math.Tau / RingSegments;
        double apothem = RingInner * Math.Cos(Math.PI / RingSegments);
        using World world = NewWorld(out _);
        FrameHandle frame = world.CreateRootFrame(new FrameSpec(FrameBounds.Sphere(1e6f), GravityField.Uniform(new Vector3(9.81f, 0, 0))));
        world.Spawn(new SpawnDesc { Frame = frame, Body = new BodyDesc(ShapeOf(Ring, Floor.Hulls), BodyMotion.Static) });
        double start = -5.5 * step;
        // The box's up, its local +Y, faces the axis; its local +X runs along the axis.
        DQuaternion turn = DQuaternion.FromAxisAngle(Double3.UnitY, start) * DQuaternion.FromAxisAngle(Double3.UnitZ, Math.PI / 2);
        (turn.Rotate(Double3.UnitY) + Radial(start)).Length.Should().BeLessThan(1e-9, "the box's up faces the axis");
        Entity box = world.Spawn(new SpawnDesc
        {
            Frame = frame,
            Pose = new DPose((apothem - half - 0.0002) * Radial(start), turn),
            Body = new BodyDesc(new BoxShapeDesc(new Vector3((float)half)), Mass: 10),
            Components = [new Pusher()],
        });

        // The height of a point above the facet under it, toward the axis.
        double Height(Double3 point)
        {
            double middle = (Math.Floor(Math.Atan2(-point.Z, point.X) / step) + 0.5) * step;
            return apothem - Double3.Dot(new Double3(point.X, 0, point.Z), Radial(middle));
        }

        double lift = double.MinValue, pitch = 0, theta = start;
        for (int t = 0; t < 360 && theta < 4.5 * step; t++)
        {
            world.Tick();
            DPose pose = world.PoseIn(box, frame);
            Double3 p = pose.Position;
            theta = Math.Atan2(-p.Z, p.X);
            Double3 up = pose.Rotation.Rotate(Double3.UnitY), x = pose.Rotation.Rotate(Double3.UnitX), z = pose.Rotation.Rotate(Double3.UnitZ);
            double lowest = double.MaxValue;
            // The bottom face is flat out to the rounding of its edges.
            foreach (double sx in new[] { -1.0, 1.0 })
                foreach (double sz in new[] { -1.0, 1.0 })
                    lowest = Math.Min(lowest, Height(p - half * up + sx * (half - rounding) * x + sz * (half - rounding) * z));
            lift = Math.Max(lift, lowest);
            pitch = Math.Max(pitch, Math.Abs(Math.Atan2(Double3.Dot(up, Along(theta)), -Double3.Dot(up, Radial(theta)))));
        }

        theta.Should().BeGreaterThanOrEqualTo(4.5 * step, "the box has crossed the ten joints");
        lift.Should().BeLessThan(0.001, "the box never leaves the floor");
        (pitch * 180 / Math.PI).Should().BeLessThan(180.0 / RingSegments + 0.5, "the box tilts only with the facets under it");
        world.Errors.Should().BeEmpty();
    }

    /// <summary>Walks its character along -Z at <see cref="PersonControl.WalkSpeed"/> once <see cref="Walking"/> is set, and stands it still before.</summary>
    private sealed class Walker : IComponent
    {
        public bool Walking;

        public int Priority => 0;

        public EventMask Wants => EventMask.Of<Tertius.Tick>();

        public void Handle(Self self, scoped ref EventList events, scoped ref Outbox outbox) =>
            self.Body.MoveCharacter(Walking ? new Vector3(0, 0, -PersonControl.WalkSpeed) : Vector3.Zero);
    }

    /// <summary>
    /// The demo's person, a character capsule of half height 0.6 m and radius 0.3 m and 70 kg, stands still in the middle
    /// of facet 250 of the ring's floor for 1 s, and then walks at the demo's walking speed, 3 m/s, to the middle of facet
    /// 4, across the ten joints between them, with the seam between the last segment and the first in the middle. Gravity
    /// is uniform, 9.81 m/s² along +X, toward the seam, so the person's up is -X, and it walks along -Z, across its up. At
    /// each tick, its lift is the distance from the floor to its lower sphere, whose center is 0.6 m below its own center
    /// along its up: the least distance from that center to the plane of a facet near it, less the radius. A Jolt
    /// character drops the contacts with the edges at a mesh's joints, which its mesh marks inactive, but a compound of
    /// hulls marks none. Without enhanced internal edge removal on the character, on the hulls, it is lifted up to 3.6 mm
    /// above its standing lift at the joints, and not at all on the mesh. With it, it is never lifted above its standing
    /// lift on either floor. Its speed across the up stays within 1.3 % of the walk on either floor, with the flag or
    /// without it on the mesh, and within 2.1 % without it on the hulls.
    /// </summary>
    [Theory]
    [InlineData(Floor.Hulls)]
    [InlineData(Floor.Mesh)]
    public void ACharacterThatWalksAcrossTenJointsOfARingsFloorKeepsToTheFloorAtItsWalkingSpeed(Floor floor)
    {
        const float halfHeight = 0.6f, radius = 0.3f;
        const double step = Math.Tau / RingSegments;
        double apothem = RingInner * Math.Cos(Math.PI / RingSegments);
        using World world = NewWorld(out _);
        FrameHandle frame = world.CreateRootFrame(new FrameSpec(FrameBounds.Sphere(1e6f), GravityField.Uniform(new Vector3(9.81f, 0, 0))));
        world.Spawn(new SpawnDesc { Frame = frame, Body = new BodyDesc(ShapeOf(Ring, floor), BodyMotion.Static) });
        double start = -5.5 * step;
        // The person's up, its local +Y, is -X, and its lower sphere starts 1 cm above the middle of facet 250.
        Double3 lowerCenter = (apothem - radius - 0.01) * Radial(start);
        var walker = new Walker();
        Entity person = world.Spawn(new SpawnDesc
        {
            Frame = frame,
            Pose = new DPose(lowerCenter - halfHeight * Double3.UnitX, DQuaternion.FromAxisAngle(Double3.UnitZ, Math.PI / 2)),
            Body = new BodyDesc(new CapsuleShapeDesc(halfHeight, radius), BodyMotion.Character, Mass: 70),
            Components = [walker],
        });

        // The lift of the lower sphere of a person at the pose, from the facet before its own to the one after.
        double Lift(DPose pose)
        {
            Double3 center = pose.Position - halfHeight * pose.Rotation.Rotate(Double3.UnitY);
            double k = Math.Floor(Math.Atan2(-center.Z, center.X) / step), distance = double.MaxValue;
            for (double j = k - 1; j <= k + 1; j++)
                distance = Math.Min(distance, apothem - Double3.Dot(new Double3(center.X, 0, center.Z), Radial((j + 0.5) * step)));
            return distance - radius;
        }

        Tick(world, 60);
        double standing = Lift(world.PoseIn(person, frame));
        walker.Walking = true;
        double lift = double.MinValue, slowest = double.MaxValue, theta = start;
        Double3 last = world.PoseIn(person, frame).Position;
        for (int t = 0; t < 120 && theta < 4.5 * step; t++)
        {
            world.Tick();
            DPose pose = world.PoseIn(person, frame);
            theta = Math.Atan2(-pose.Position.Z, pose.Position.X);
            lift = Math.Max(lift, Lift(pose));
            // The speed across the up, from the move in the tick's step. The step of the walk's first tick does not know
            // the walk yet.
            Double3 move = pose.Position - last;
            last = pose.Position;
            if (t > 0)
                slowest = Math.Min(slowest, Math.Sqrt(move.Y * move.Y + move.Z * move.Z) / TickSeconds);
        }

        theta.Should().BeGreaterThanOrEqualTo(4.5 * step, "the person has crossed the ten joints");
        (lift - standing).Should().BeLessThan(0.001, "the person is never lifted off the floor at a joint");
        slowest.Should().BeGreaterThan(0.95 * PersonControl.WalkSpeed, "a joint never slows the walk");
        world.Errors.Should().BeEmpty();
    }
}
