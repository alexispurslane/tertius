using System.Numerics;
using Tertius.Simulation;
using static Tertius.Testing.ContactNews;
using static Tertius.Testing.WorldTesting;

namespace Tertius.Physics.Tests;

/// <summary>
/// Ghosts: the copy of a body that sticks out of the bubble of its frame, which the physics system of the neighbouring
/// frame holds, on the World and the real backend. A test reads the ghosts from the real backend, <see cref="FaultInjectingBackend.Jolt"/>,
/// after a tick.
/// </summary>
public class GhostTests
{
    /// <summary>A ball that nothing slows down and that does not bounce.</summary>
    private static BodyDesc BallBody(float radius) =>
        new(new SphereShapeDesc(radius), Mass: 1, Restitution: 0, LinearDamping: 0, AngularDamping: 0);

    /// <summary>A body of a box with sides of 2 · <paramref name="half"/> m that nothing slows down, and that does not bounce.</summary>
    private static BodyDesc BoxBody(float half) =>
        new(new BoxShapeDesc(new Vector3(half)), Mass: 1, Restitution: 0, LinearDamping: 0, AngularDamping: 0);

    private static FrameHandle Space(World world) =>
        world.CreateRootFrame(new FrameSpec(FrameBounds.Sphere(1e7f)));

    /// <summary>A frame without a body, as a child of <paramref name="parent"/>. Returns the entity that owns it.</summary>
    private static Entity BubbleOwner(World world, FrameHandle parent, DPose pose, float radius) =>
        world.Spawn(new SpawnDesc { Frame = parent, Pose = pose, AsFrame = new FrameSpec(FrameBounds.Sphere(radius)) });

    private static FrameHandle Bubble(World world, FrameHandle parent, DPose pose, float radius) =>
        world.GetOwnedFrame(BubbleOwner(world, parent, pose, radius));

    [Fact]
    public void GhostExistsOnlyWhileTheBodyStraddlesTheBubble()
    {
        using World world = NewWorldWithFaults(out FaultInjectingBackend faults);
        FrameHandle space = Space(world);
        var center = new Double3(50, 0, 0);
        FrameHandle bubble = Bubble(world, space, new DPose(center, DQuaternion.Identity), 20);
        const float radius = 0.5f;
        var pilot = new Pilot();
        Entity ball = world.Spawn(new SpawnDesc
        {
            Frame = space,
            Pose = At(28),
            LinearVelocity = new Vector3(3, 0, 0),
            Body = BallBody(radius),
            Components = [pilot],
        });

        // Outside and not touching: no ghost.
        world.Tick();
        world.GetFrame(ball).Should().Be(space);
        faults.Jolt.GhostCount(ball).Should().Be(0);

        // Entered on first touch. Part of the ball is still outside, so the parent holds a ghost.
        TickUntil(world, () => world.GetFrame(ball) == bubble, 60);
        world.GetFrame(ball).Should().Be(bubble);
        faults.Jolt.HasGhostIn(ball, space).Should().BeTrue();
        faults.Jolt.GhostCount(ball).Should().Be(1);

        // Fully inside: the ghost goes away.
        TickUntil(world, () => world.PoseInOwnFrame(ball).Position.Length < 20 - radius - 0.1, 60);
        // The turn is an intent, which acts in the step of the tick after the one that writes it. So the tick that checks
        // the ghost writes the turn, and the next step starts it.
        pilot.Once = body => body.SetVelocity(new Double3(-3, 0, 0));
        world.Tick();
        faults.Jolt.GhostCount(ball).Should().Be(0);

        // Back out through the boundary: the ghost returns while the ball straddles it...
        TickUntil(world, () => world.PoseInOwnFrame(ball).Position.Length > 20 - radius + 0.05, 60);
        world.GetFrame(ball).Should().Be(bubble);
        faults.Jolt.HasGhostIn(ball, space).Should().BeTrue();

        // ...and after the exit hand-off the real body replaces it. Outside the margin nothing is ghosted.
        TickUntil(world, () => world.GetFrame(ball) == space, 120);
        world.GetFrame(ball).Should().Be(space);
        faults.Jolt.GhostCount(ball).Should().Be(0);
        world.Errors.Should().BeEmpty();
    }

    /// <summary>
    /// With no gravity, a kinematic block in a child frame sticks 1 m out of the bubble, and a ball of 1 kg in the parent
    /// flies at the part that sticks out at 5 m/s. The ball hits the block's ghost, which stops it and does not move the
    /// block. A contact with a ghost is a contact of the two entities: physics reports one contact of the ball and the block,
    /// in the parent frame, and each hears one Collision. A ghost stands for its real body, which is kinematic, so the
    /// impulse is the ball's mass times its approach speed. When the block moves fully inside, its ghost goes, which ends
    /// the contact, and each hears one CollisionEnded.
    /// </summary>
    [Fact]
    public void ABodyInTheParentAndABodyThatSticksOutOfTheChildEachHearTheirContactThroughTheGhost()
    {
        using World world = NewWorldWithFaults(out FaultInjectingBackend faults);
        FrameHandle space = Space(world);
        FrameHandle bubble = Bubble(world, space, DPose.Identity, 10);
        // A kinematic block in the child sits on the bubble's edge and sticks 1 m out of it.
        var blockDesc = new BodyDesc(new BoxShapeDesc(new Vector3(1)), BodyMotion.Kinematic);
        var pilot = new Pilot();
        var blockLog = new CollisionLog();
        Entity block = world.Spawn(new SpawnDesc { Frame = bubble, Pose = At(10), Body = blockDesc, Components = [pilot, blockLog] });
        // A ball in the parent flies at the part that sticks out. It never touches the child's bubble.
        var ballLog = new CollisionLog();
        Entity ball = world.Spawn(new SpawnDesc
        {
            Frame = space,
            Pose = At(16),
            LinearVelocity = new Vector3(-5, 0, 0),
            Body = BallBody(0.25f),
            Components = [ballLog],
        });

        world.Tick();
        faults.Jolt.HasGhostIn(block, space).Should().BeTrue();

        Tick(world, 90);

        world.GetFrame(ball).Should().Be(space);
        DPose pose = world.PoseInOwnFrame(ball);
        pose.Position.X.Should().BeGreaterThan(11.25 - 0.05, "the ball must not go through the ghost");
        world.VelocityInOwnFrame(ball).X.Should().BeGreaterThan(-0.5f);
        faults.Outputs.SelectMany(output => output.HandOffs).Should().BeEmpty();
        // The ghost pushed, but the block did not move: ghosts never push back on the real body.
        (world.PoseInOwnFrame(block).Position - new Double3(10, 0, 0)).Length.Should().BeLessThan(1e-6);
        ContactStarted contact = faults.Outputs.SelectMany(output => output.Contacts).Should().ContainSingle().Subject;
        new[] { contact.A, contact.B }.Should().BeEquivalentTo([block, ball]);
        contact.Frame.Should().Be(space, "the parent's system holds the ghost and the ball");
        contact.Impulse.Should().BeApproximately(5f, 0.25f, "the ball of 1 kg approached the kinematic block at 5 m/s");
        blockLog.NewsOf(ball).Should().Equal([Started], "the block hears the contact of its ghost");
        ballLog.NewsOf(block).Should().Equal([Started]);

        // Move the block fully inside: the ghost goes away, and with it the contact.
        pilot.Once = body => body.Teleport(new DPose(new Double3(5, 0, 0), DQuaternion.Identity));
        Tick(world, 3);
        faults.Jolt.GhostCount(block).Should().Be(0);
        blockLog.NewsOf(ball).Should().Equal(Started, Ended);
        ballLog.NewsOf(block).Should().Equal(Started, Ended);
        world.Errors.Should().BeEmpty();
    }

    [Fact]
    public void GhostsInADestroyedFrameGoWithIt()
    {
        var warnings = new List<string>();
        using World world = NewWorldWithFaults(out FaultInjectingBackend faults, warnings);
        FrameHandle space = Space(world);
        Entity leftOwner = BubbleOwner(world, space, At(-10.6), 10);
        FrameHandle left = world.GetOwnedFrame(leftOwner);
        FrameHandle right = Bubble(world, space, At(10.4), 10);
        // The ball enters the right bubble and sticks out of it: space and the left frame hold ghosts of it.
        Entity ball = world.Spawn(new SpawnDesc { Frame = space, Pose = At(0), Body = BallBody(1f) });
        world.Tick();
        world.GetFrame(ball).Should().Be(right);
        faults.Jolt.HasGhostIn(ball, left).Should().BeTrue();
        faults.Jolt.GhostCount(ball).Should().Be(2);

        world.Destroy(leftOwner).Should().BeTrue();
        Tick(world, 3);

        // The ghost went with the left frame's system; the ball itself is untouched.
        faults.Jolt.HasGhostIn(ball, left).Should().BeFalse();
        faults.Jolt.HasGhostIn(ball, space).Should().BeTrue();
        faults.Jolt.GhostCount(ball).Should().Be(1);
        world.GetFrame(ball).Should().Be(right);
        warnings.Should().BeEmpty();
        world.Errors.Should().BeEmpty();
    }

    [Fact]
    public void GhostMirrorsTheRealBodyInTheOtherFramesCoordinates()
    {
        using World world = NewWorldWithFaults(out FaultInjectingBackend faults);
        FrameHandle space = Space(world);
        var rotation = DQuaternion.FromAxisAngle(Double3.UnitZ, 0.7);
        FrameHandle bubble = Bubble(world, space, new DPose(new Double3(-30, 5, 2), rotation), 10);
        // A kinematic body at the child's edge, sticking out of the bubble.
        var desc = new BodyDesc(new SphereShapeDesc(1), BodyMotion.Kinematic);
        Entity rider = world.Spawn(new SpawnDesc { Frame = bubble, Pose = At(0, 0, 9.8), Body = desc });
        // A probe ball in the parent, placed so that it rests against where the ghost will be.
        Entity probe = world.Spawn(new SpawnDesc
        {
            Frame = space,
            Pose = new DPose(new DPose(new Double3(-30, 5, 2), rotation).TransformPoint(new Double3(0, 0, 11.6)), DQuaternion.Identity),
            LinearVelocity = rotation.Rotate(new Vector3(0, 0, -1)),
            Body = BallBody(0.5f),
        });

        Tick(world, 30);

        // The probe hit the ghost where the real body is, converted into the parent's coordinates, and stopped.
        Double3 probeInChild = world.Frames.PointFromParent(bubble, world.PoseIn(probe, space).Position);
        probeInChild.Z.Should().BeGreaterThan(9.8 + 1.5 - 0.05, "the probe stops against the ghost");
        faults.Jolt.HasGhostIn(rider, space).Should().BeTrue();
        world.Errors.Should().BeEmpty();
    }

    [Fact]
    public void FrameHullsAreGhostedIntoOverlappingSiblingFrames()
    {
        using World world = NewWorldWithFaults(out FaultInjectingBackend faults);
        FrameHandle space = Space(world);
        // A static station whose hull is a triangle mesh: a wall facing -X at x = 15, inside both bubbles. Jolt
        // ignores the back faces of triangles, so the winding must face the ship.
        var wall = new MeshShapeDesc([new(-15, -5, -5), new(-15, 5, -5), new(-15, 5, 5), new(-15, -5, 5)], [0, 2, 1, 0, 3, 2]);
        Entity stationOwner = world.Spawn(new SpawnDesc
        {
            Frame = space,
            Pose = At(30),
            Body = new BodyDesc(wall, BodyMotion.Static, InteriorShape: new SphereShapeDesc(1)),
            AsFrame = new FrameSpec(FrameBounds.Sphere(20)),
        });
        FrameHandle station = world.GetOwnedFrame(stationOwner);
        // A ship next to it, at rest, whose bubble overlaps the station's.
        Entity shipOwner = world.Spawn(new SpawnDesc
        {
            Frame = space,
            Pose = At(0),
            Body = new BodyDesc(new SphereShapeDesc(2), Mass: 1000, InteriorShape: new SphereShapeDesc(1)),
            AsFrame = new FrameSpec(FrameBounds.Sphere(20)),
        });
        FrameHandle ship = world.GetOwnedFrame(shipOwner);
        // A crate in the ship flies at the station's wall. It stays in the ship until it leaves the ship's bubble,
        // so it must hit the wall's ghost in the ship's system.
        Entity crate = world.Spawn(new SpawnDesc { Frame = ship, Pose = At(10), LinearVelocity = new Vector3(4, 0, 0), Body = BoxBody(0.5f) });

        world.Tick();
        faults.Jolt.HasGhostIn(stationOwner, ship).Should().BeTrue();
        faults.Jolt.HasGhostIn(shipOwner, station).Should().BeTrue();

        Tick(world, 120);
        world.GetFrame(crate).Should().Be(ship);
        DPose pose = world.PoseInOwnFrame(crate);
        pose.Position.X.Should().BeInRange(14.5 - 0.6, 14.5 + 0.05);
        world.VelocityInOwnFrame(crate).X.Should().BeLessThan(0.5f);
        world.Errors.Should().BeEmpty();
    }
}
