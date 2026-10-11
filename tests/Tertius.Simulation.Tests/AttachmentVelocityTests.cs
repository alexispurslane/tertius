using System.Numerics;
using AwesomeAssertions;

namespace Tertius.Simulation.Tests;

/// <summary>
/// A child moves as a point fixed to its chain root. If the root moves with velocity v and spins with angular velocity w,
/// and the child is r metres from the root (in the axes of the frame), the child's velocity is v + w x r, and its
/// angular velocity is w. The spawn description of a root with a body gives its velocities. The tests use a physics
/// double that does nothing, so the root keeps the velocities it was spawned with.
/// </summary>
public class AttachmentVelocityTests
{
    private static Pose Offset(float x, float y, float z = 0) => new(new Vector3(x, y, z), Quaternion.Identity);

    private static Entity SpawnRoot(TestWorld test, Vector3 velocity, Vector3 spin, DPose? pose = null) =>
        test.World.Spawn(new SpawnDesc
        {
            Frame = test.Root,
            Pose = pose ?? DPose.Identity,
            LinearVelocity = velocity,
            AngularVelocity = spin,
            Body = TestWorld.Ball,
        });

    [Fact]
    public void AChildMovesWithTheVelocityOfItsParentPlusTheSpinOfItsLeverArm()
    {
        using var test = new TestWorld(TestPhysics.Create());
        Entity parent = SpawnRoot(test, velocity: new Vector3(1, 0, 0), spin: new Vector3(0, 1, 0));
        Entity child = test.World.Spawn(new SpawnDesc { Frame = test.Root, AttachTo = parent, AttachOffset = Offset(0, 0, 2) });

        test.Run(1);

        // v + w x r = (1, 0, 0) + (0, 1, 0) x (0, 0, 2) = (1, 0, 0) + (2, 0, 0) = (3, 0, 0).
        PoseChecks.AssertNear(test.World.GetVelocity(child), new Vector3(3, 0, 0));
    }

    [Fact]
    public void AChildHasTheAngularVelocityOfItsParent()
    {
        using var test = new TestWorld(TestPhysics.Create());
        Entity parent = SpawnRoot(test, velocity: Vector3.Zero, spin: new Vector3(0, 1, 0));
        Entity child = test.World.Spawn(new SpawnDesc { Frame = test.Root, AttachTo = parent, AttachOffset = Offset(0, 0, 2) });

        test.Run(1);

        test.World.GetAngularVelocity(child).Should().Be(new Vector3(0, 1, 0));
    }

    [Fact]
    public void ThePlacementOfAChildOutsideATickAlreadyGivesItsVelocity()
    {
        using var test = new TestWorld(TestPhysics.Create());
        Entity parent = SpawnRoot(test, velocity: new Vector3(1, 0, 0), spin: new Vector3(0, 1, 0));

        Entity child = test.World.Spawn(new SpawnDesc { Frame = test.Root, AttachTo = parent, AttachOffset = Offset(0, 0, 2) });

        PoseChecks.AssertNear(test.World.GetVelocity(child), new Vector3(3, 0, 0));
    }

    [Fact]
    public void TheLeverArmTurnsWithTheParentBecauseItIsMeasuredInTheAxesOfTheFrame()
    {
        using var test = new TestWorld(TestPhysics.Create());
        // The quarter turn about +Y puts the child's 2 m along the parent's +Z on the frame's +X.
        Entity parent = SpawnRoot(test, velocity: new Vector3(1, 0, 0), spin: new Vector3(0, 1, 0), pose: new DPose(Double3.Zero, PoseChecks.QuarterTurnAboutY));
        Entity child = test.World.Spawn(new SpawnDesc { Frame = test.Root, AttachTo = parent, AttachOffset = Offset(0, 0, 2) });

        test.Run(1);

        // The lever arm is (2, 0, 0), and (0, 1, 0) x (2, 0, 0) = (0, 0, -2).
        PoseChecks.AssertNear(test.World.GetVelocity(child), new Vector3(1, 0, -2), 1e-5f);
    }

    [Fact]
    public void AGrandchildsLeverArmRunsFromTheRootThroughTheWholeChain()
    {
        using var test = new TestWorld(TestPhysics.Create());
        Entity root = SpawnRoot(test, velocity: Vector3.Zero, spin: new Vector3(0, 0, 1));
        Entity child = test.World.Spawn(new SpawnDesc { Frame = test.Root, AttachTo = root, AttachOffset = Offset(1, 0) });
        Entity grandchild = test.World.Spawn(new SpawnDesc { Frame = test.Root, AttachTo = child, AttachOffset = Offset(0, 1) });

        test.Run(1);

        // The grandchild is at (1, 1, 0) from the root, and (0, 0, 1) x (1, 1, 0) = (-1, 1, 0).
        PoseChecks.AssertNear(test.World.GetVelocity(grandchild), new Vector3(-1, 1, 0));
    }

    [Fact]
    public void AChildOfAParentWithoutABodyHasNoVelocityOfItsOwn()
    {
        using var test = new TestWorld();
        Entity parent = test.SpawnWith(new Mover(new Double3(1, 0, 0)));
        Entity child = test.World.Spawn(new SpawnDesc { Frame = test.Root, AttachTo = parent, AttachOffset = Offset(0, 0, 2) });

        test.Run(3);

        // A mover changes the pose, not the velocity, and the velocity of a child comes from the velocity of its root.
        test.World.GetVelocity(child).Should().Be(Vector3.Zero);
    }

    [Fact]
    public void TheVelocityOfAChildFollowsTheVelocitiesThatPhysicsReportsForItsParent()
    {
        IPhysicsBackend physics = TestPhysics.Create();
        using var test = new TestWorld(physics);
        Entity parent = SpawnRoot(test, velocity: Vector3.Zero, spin: Vector3.Zero);
        Entity child = test.World.Spawn(new SpawnDesc { Frame = test.Root, AttachTo = parent, AttachOffset = Offset(0, 0, 2) });
        physics.OnStep((context, output) =>
            output.Bodies.Add(new BodyState(parent, test.Root, TestWorld.At(context.Tick), new Vector3(0, 5, 0), new Vector3(0, 1, 0))));

        test.Run(1);

        // (0, 5, 0) + (0, 1, 0) x (0, 0, 2) = (2, 5, 0).
        PoseChecks.AssertNear(test.World.GetVelocity(child), new Vector3(2, 5, 0));
    }
}
