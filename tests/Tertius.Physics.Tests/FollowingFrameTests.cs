using System.Numerics;
using Tertius.Simulation;
using static Tertius.Testing.WorldTesting;

namespace Tertius.Physics.Tests;

/// <summary>
/// Following frames for motion that is no orbit (test 8 of docs/plans/rails-components.md, ticket ter-zg77): a numeric frame
/// that holds a fast or far kinematic body at its origin, its anchor, and lands at every sync point on the step record of
/// the anchor's motion intent (docs/plans/numeric-frames.md, "Following frames"). A test component drives the anchor along a
/// straight line at 2 km/s with end poses, in a frame without gravity, so the line is the anchor's true path. The world runs
/// on the real backend.
/// </summary>
public class FollowingFrameTests
{
    /// <summary>The velocity of the anchor's drift in space, in m/s: 2 km/s along +X, above the 1,000 m/s from which a free body gets a numeric frame.</summary>
    private static readonly Double3 Drift = new(2_000, 0, 0);

    /// <summary>The radius of the hull's bubble, in meters.</summary>
    private const float BubbleRadius = 16;

    /// <summary>
    /// The hull: a core, a box of 4 m at the frame's origin, and a plate of 16 m by 0.5 m by 16 m outside the bubble, across
    /// the +Y axis, whose face toward the core is 19.75 m from it. Its bounding sphere reaches past the bubble, so a crate that
    /// leaves the bubble along +Y stays within the merge test's reach of the hull. It is kinematic, so only its motion intents
    /// move it.
    /// </summary>
    private static readonly BodyDesc Hull = new(new CompoundShapeDesc([
        new CompoundChild(new BoxShapeDesc(new Vector3(2)), new Pose(Vector3.Zero, Quaternion.Identity)),
        new CompoundChild(new BoxShapeDesc(new Vector3(8, 0.25f, 8)), new Pose(new Vector3(0, 20, 0), Quaternion.Identity)),
    ]), BodyMotion.Kinematic, Mass: 1e5f, Restitution: 0);

    /// <summary>A crate, a box of 50 cm and 2 kg that nothing slows down and that does not bounce.</summary>
    private static readonly BodyDesc Crate = new(new BoxShapeDesc(new Vector3(0.25f)), Mass: 2, Restitution: 0, LinearDamping: 0, AngularDamping: 0);

    /// <summary>
    /// A world on the real backend, a frame without gravity, and in it at the origin the owner of a frame whose kinematic
    /// hull <see cref="KinematicDrift"/> moves at <see cref="Drift"/>. The hull keeps its spawn velocity as its frame's
    /// velocity, and its first motion intent is the end pose of its first step, so it moves from its first step on.
    /// </summary>
    private static (World World, FrameHandle Space, Entity Owner, FrameHandle Frame) DriftingHull()
    {
        World world = NewWorld(out _);
        FrameHandle space = Ground(world, 0);
        Entity owner = world.Spawn(new SpawnDesc
        {
            Frame = space,
            Body = Hull,
            AsFrame = new FrameSpec(FrameBounds.Sphere(BubbleRadius)),
            LinearVelocity = Drift,
            FirstMove = new MotionIntent(new EndPose(new DPose(Drift * world.DeltaTime, DQuaternion.Identity))),
            Components = [new KinematicDrift(Drift)],
        });
        return (world, space, owner, world.GetOwnedFrame(owner));
    }

    /// <summary>
    /// The hull gets a following frame at its first sync point and rests at its origin, with its frame hung there, for ten
    /// seconds of the drift: the frame's place in the following frame is zero and so is its velocity there. Every read of
    /// the hull in space is on the line of the drift, with the drift's velocity.
    /// </summary>
    [Fact]
    public void AHullThatEndPosesDriveAt2KmPerSecondAlongALineRestsAtTheOriginOfAFollowingFrame()
    {
        (World world, FrameHandle space, Entity owner, FrameHandle frame) = DriftingHull();
        using (world)
        {
            world.Tick();
            FrameHandle home = world.PhysicsHomeOf(owner);
            world.Frames.Numeric(home).Should().Be(NumericMode.Orbiting, "the hull moves at 2 km/s, so it lives in a following frame");
            world.Frames.TreeParent(frame).Should().Be(home, "the hull's frame hangs at the origin of the following frame");

            for (int tick = 0; tick < 600; tick++)
            {
                world.Tick();
                world.PhysicsHomeOf(owner).Should().Be(home, "the hull keeps its following frame at tick {0}", world.TickNumber);
                world.Frames.TreeLocalPose(frame).Position.Should().Be(Double3.Zero, "the hull rests at the origin at tick {0}", world.TickNumber);
                world.Frames.TreeLinearVelocity(frame).Should().Be(Double3.Zero, "an end pose's record has no velocity past its chord");
                DPose pose = world.PoseIn(owner, space);
                (pose.Position - Drift * world.Time).Length.Should().BeLessThan(1e-6, "the hull is on the line of the drift at tick {0}", world.TickNumber);
                (world.VelocityIn(owner, space) - Drift).Length.Should().BeLessThan(1e-9, "the hull moves at the drift's velocity");
            }
            world.Errors.Should().BeEmpty();
        }
    }

    /// <summary>
    /// A crate floats in the hull's frame 10 m along +Y from its core and drifts along +Y at 1 m/s relative to it. It leaves
    /// the bubble of 16 m into the frame's tree parent, the hull's following frame, and stays there, linked to the hull, at
    /// a local velocity of about 1 m/s: a numeric frame keeps a body slow when the body moves with the anchor. Its place
    /// relative to the hull stays on the line of its drift.
    /// </summary>
    [Fact]
    public void ACrateThatDriftsOutOfTheHullsFrameJoinsItsFollowingFrame()
    {
        (World world, FrameHandle space, Entity owner, FrameHandle frame) = DriftingHull();
        using (world)
        {
            Entity crate = world.Spawn(new SpawnDesc { Frame = frame, Pose = At(0, 10), LinearVelocity = new Double3(0, 1, 0), Body = Crate });
            var homes = new List<string>();
            double fastest = 0;
            for (int tick = 0; tick < 480; tick++)
            {
                world.Tick();
                FrameHandle home = world.PhysicsHomeOf(crate);
                FrameHandle hullHome = world.PhysicsHomeOf(owner);
                string kind = home == frame ? "hull's frame" : home == hullHome ? "following frame" : world.Frames.Numeric(home).ToString();
                if (homes.Count == 0 || homes[^1] != kind)
                    homes.Add(kind);
                if (home == hullHome)
                    fastest = Math.Max(fastest, (world.VelocityIn(crate, space) - world.Frames.LinearVelocity(home)).Length);
            }
            world.Frames.Numeric(world.PhysicsHomeOf(owner)).Should().Be(NumericMode.Orbiting);
            homes.Should().Equal(["hull's frame", "following frame"], "the crate leaves into the frame's tree parent and stays linked to the hull");
            world.GetFrame(crate).Should().Be(space, "gameplay sees the crate in space");
            fastest.Should().BeInRange(0.9, 1.1, "the crate is slow in the following frame");
            (world.PoseIn(crate, space).Position - world.PoseIn(owner, space).Position - new Double3(0, 10 + 480.0 / 60, 0)).Length
                .Should().BeLessThan(0.01, "the crate drifts on in a straight line relative to the hull, 1 m/s for 480 ticks");
            world.Errors.Should().BeEmpty();
        }
    }
}
