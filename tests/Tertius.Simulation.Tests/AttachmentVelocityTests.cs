using System.Numerics;
using AwesomeAssertions;

namespace Tertius.Simulation.Tests;

/// <summary>
/// A child moves as a point fixed to its chain root. If the root moves with velocity v and spins with angular velocity w,
/// and the child is r metres from the root (in the axes of the frame), the child's velocity is v + w x r, and its
/// angular velocity is w. The spawn description of a root with a body gives its velocities. Most tests run a world
/// with no physics backend, so the root keeps the velocities it was spawned with. The last test runs on real Jolt.
/// </summary>
public sealed class AttachmentVelocityTests
{
    private static Pose Offset(float x, float y, float z = 0) => new(new(x, y, z), Quaternion.Identity);

    private static Entity SpawnRoot(TestWorld test, Vector3 velocity, Vector3 spin, DPose? pose = null) =>
        test.World.Spawn(
            new()
            {
                Frame = test.Root,
                Pose = pose ?? DPose.Identity,
                LinearVelocity = velocity,
                AngularVelocity = spin,
                Body = TestWorld.Ball,
            }
        );

    [Fact]
    public void AChildMovesWithTheVelocityOfItsParentPlusTheSpinOfItsLeverArm()
    {
        using var test = new TestWorld();
        Entity parent = SpawnRoot(test: test, velocity: new(1, 0, 0), spin: new(0, 1, 0));
        Entity child = test.World.Spawn(
            new()
            {
                Frame = test.Root,
                AttachTo = parent,
                AttachOffset = Offset(0, 0, 2),
            }
        );

        test.Run(1);

        // v + w x r = (1, 0, 0) + (0, 1, 0) x (0, 0, 2) = (1, 0, 0) + (2, 0, 0) = (3, 0, 0).
        PoseChecks.AssertNear(test.World.VelocityInOwnFrame(child), new(3, 0, 0));
    }

    [Fact]
    public void AChildHasTheAngularVelocityOfItsParent()
    {
        using var test = new TestWorld();
        Entity parent = SpawnRoot(test: test, velocity: Vector3.Zero, spin: new(0, 1, 0));
        Entity child = test.World.Spawn(
            new()
            {
                Frame = test.Root,
                AttachTo = parent,
                AttachOffset = Offset(0, 0, 2),
            }
        );

        test.Run(1);

        test.World.AngularVelocityInOwnFrame(child).Should().Be(new Vector3(0, 1, 0));
    }

    [Fact]
    public void ThePlacementOfAChildOutsideATickAlreadyGivesItsVelocity()
    {
        using var test = new TestWorld();
        Entity parent = SpawnRoot(test: test, velocity: new(1, 0, 0), spin: new(0, 1, 0));

        Entity child = test.World.Spawn(
            new()
            {
                Frame = test.Root,
                AttachTo = parent,
                AttachOffset = Offset(0, 0, 2),
            }
        );

        PoseChecks.AssertNear(test.World.VelocityInOwnFrame(child), new(3, 0, 0));
    }

    [Fact]
    public void TheLeverArmTurnsWithTheParentBecauseItIsMeasuredInTheAxesOfTheFrame()
    {
        using var test = new TestWorld();
        // The quarter turn about +Y puts the child's 2 m along the parent's +Z on the frame's +X.
        Entity parent = SpawnRoot(
            test: test,
            velocity: new(1, 0, 0),
            spin: new(0, 1, 0),
            pose: new DPose(Double3.Zero, PoseChecks.QuarterTurnAboutY)
        );
        Entity child = test.World.Spawn(
            new()
            {
                Frame = test.Root,
                AttachTo = parent,
                AttachOffset = Offset(0, 0, 2),
            }
        );

        test.Run(1);

        // The lever arm is (2, 0, 0), and (0, 1, 0) x (2, 0, 0) = (0, 0, -2).
        PoseChecks.AssertNear(test.World.VelocityInOwnFrame(child), new(1, 0, -2), 1e-5f);
    }

    [Fact]
    public void AGrandchildsLeverArmRunsFromTheRootThroughTheWholeChain()
    {
        using var test = new TestWorld();
        Entity root = SpawnRoot(test: test, velocity: Vector3.Zero, spin: new(0, 0, 1));
        Entity child = test.World.Spawn(
            new()
            {
                Frame = test.Root,
                AttachTo = root,
                AttachOffset = Offset(1, 0),
            }
        );
        Entity grandchild = test.World.Spawn(
            new()
            {
                Frame = test.Root,
                AttachTo = child,
                AttachOffset = Offset(0, 1),
            }
        );

        test.Run(1);

        // The grandchild is at (1, 1, 0) from the root, and (0, 0, 1) x (1, 1, 0) = (-1, 1, 0).
        PoseChecks.AssertNear(test.World.VelocityInOwnFrame(grandchild), new(-1, 1, 0));
    }

    [Fact]
    public void AChildOfAParentWithoutABodyHasNoVelocityOfItsOwn()
    {
        using var test = new TestWorld();
        Entity parent = test.SpawnWith(new Mover(new(1, 0, 0)));
        Entity child = test.World.Spawn(
            new()
            {
                Frame = test.Root,
                AttachTo = parent,
                AttachOffset = Offset(0, 0, 2),
            }
        );

        test.Run(3);

        // A mover changes the pose, not the velocity, and the velocity of a child comes from the velocity of its root.
        test.World.VelocityInOwnFrame(child).Should().Be(Vector3.Zero);
    }

    [Fact]
    public void TheVelocityOfAChildFollowsTheVelocitiesThatPhysicsReportsForItsParent()
    {
        using var test = TestWorld.OnJolt(faults: out FaultInjectingBackend faults);
        Entity parent = test.World.Spawn(
            new()
            {
                Frame = test.Root,
                LinearVelocity = new Vector3(0, 5, 0),
                AngularVelocity = new(0, 1, 0),
                Body = WorldTesting.Ball(),
            }
        );
        Entity child = test.World.Spawn(
            new()
            {
                Frame = test.Root,
                AttachTo = parent,
                AttachOffset = Offset(0, 0, 2),
            }
        );

        test.Run(1);

        // Jolt turned the parent a little in its step, so the lever arm of 2 m along the parent's +Z turned with it. The
        // child takes the velocities and the pose that Jolt reported: v + w x r, with r the lever arm in the axes of the frame.
        BodyState reported = faults.Outputs.Single().Bodies.Single(state => state.Entity == parent);
        reported.LinearVelocity.Should().Be(new Vector3(0, 5, 0), "nothing slows or pushes the ball");
        var leverArm = reported.Pose.Rotation.Rotate(new Double3(0, 0, 2)).ToVector3();
        PoseChecks.AssertNear(
            test.World.VelocityInOwnFrame(child),
            reported.LinearVelocity + Vector3.Cross(reported.AngularVelocity, leverArm)
        );
        test.World.VelocityInOwnFrame(child)
            .X.Should()
            .BeGreaterThan(1.9f, "a spin of 1 rad/s about +Y moves a point 2 m along +Z at about 2 m/s along +X");
    }
}
