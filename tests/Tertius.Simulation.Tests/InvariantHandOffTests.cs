using System.Numerics;
using AwesomeAssertions;
using NSubstitute;

namespace Tertius.Simulation.Tests;

/// <summary>
/// Physics reports where bodies went, and the report can be wrong: a hand-off that would put a frame inside itself, or
/// into a frame that is gone. The world must refuse such a hand-off, say so in <see cref="World.Errors"/>, and stay
/// coherent: every invariant holds, and nothing about the refused entity changes.
/// </summary>
public class InvariantHandOffTests
{
    /// <summary>
    /// A world with a hull: a frame <see cref="Hull"/> that physics drives, whose owner has a body and a velocity of
    /// (1, 2, 3), in the root frame, and a static frame <see cref="Inner"/> inside the hull.
    /// </summary>
    private sealed class HullWorld : IDisposable
    {
        public readonly TestWorld Test;
        public readonly Entity HullOwner;
        public readonly FrameHandle Hull;
        public readonly FrameHandle Inner;

        /// <param name="handOffTo">Chooses where physics hands the hull off at the first step. It gets the hull and the inner frame.</param>
        public HullWorld(Func<FrameHandle, FrameHandle, FrameHandle> handOffTo)
        {
            bool reported = false;
            FrameHandle root = default;
            // The script runs on the physics thread during the first tick, after the constructor has set the fields it reads.
            IPhysicsBackend physics = TestPhysics.Create((context, output) =>
            {
                if (reported)
                    return;
                reported = true;
                output.HandOffs.Add(new HandOff(HullOwner, root, handOffTo(Hull, Inner), TestWorld.At(1), Vector3.Zero, Vector3.Zero));
            });
            Test = new TestWorld(physics);
            root = Test.Root;
            HullOwner = Test.World.Spawn(new SpawnDesc
            {
                Frame = Test.Root,
                Pose = TestWorld.At(100),
                AsFrame = new FrameSpec(FrameBounds.Sphere(50), MotionDriver.Physics),
                Body = new BodyDesc(new SphereShapeDesc(5), Layer: BodyLayer.FrameHull),
                LinearVelocity = new Vector3(1, 2, 3),
            });
            Hull = Test.World.GetOwnedFrame(HullOwner);
            Inner = Test.World.SpawnStaticFrame(Hull, TestWorld.At(10), radius: 5);
        }

        public World World => Test.World;

        public void Dispose() => Test.Dispose();
    }

    [Fact]
    public void AHullHandedOffIntoAFrameInsideItIsRefusedAndStaysWhereItWas()
    {
        using var scene = new HullWorld((hull, inner) => inner);

        scene.World.Tick();

        scene.World.Frames.Parent(scene.Hull).Should().Be(scene.Test.Root, "a frame cannot become a child of its own descendant");
        scene.World.GetFrame(scene.HullOwner).Should().Be(scene.Test.Root, "the owner's row follows the frame's parent");
        scene.World.Errors.Should().ContainSingle(error => error.Contains("cannot move frame"));
        scene.World.AssertInvariants();
    }

    [Fact]
    public void AHullHandedOffIntoItselfIsRefusedAndStaysWhereItWas()
    {
        using var scene = new HullWorld((hull, inner) => hull);

        scene.World.Tick();

        scene.World.Frames.Parent(scene.Hull).Should().Be(scene.Test.Root);
        scene.World.GetFrame(scene.HullOwner).Should().Be(scene.Test.Root);
        scene.World.Errors.Should().ContainSingle(error => error.Contains("cannot move frame"));
        scene.World.AssertInvariants();
    }

    [Fact]
    public void ARefusedHandOffLeavesTheVelocityOfTheHullAsItWas()
    {
        using var scene = new HullWorld((hull, inner) => inner);

        scene.World.Tick();

        scene.World.Frames.LinearVelocity(scene.Hull).Should().Be(new Double3(1, 2, 3));
        scene.World.GetVelocity(scene.HullOwner).Should().Be(new Vector3(1, 2, 3));
    }

    [Fact]
    public void AHandOffIntoAFrameThatIsGoneIsIgnoredAndTheBodyStaysWhereItWas()
    {
        FrameHandle gone = default;
        using var scene = new HullWorld((hull, inner) => gone);
        FrameHandle doomed = scene.World.SpawnStaticFrame(scene.Test.Root, TestWorld.At(-500), radius: 5);
        gone = doomed;
        scene.World.Destroy(doomed.Entity).Should().BeTrue();

        scene.World.Tick();

        scene.World.Frames.IsAlive(gone).Should().BeFalse();
        scene.World.Frames.Parent(scene.Hull).Should().Be(scene.Test.Root);
        scene.World.GetFrame(scene.HullOwner).Should().Be(scene.Test.Root);
        scene.World.Errors.Should().ContainSingle(error => error.Contains("stale frame"));
        scene.World.AssertInvariants();
    }

    [Fact]
    public void ABodyStateInAFrameThatIsGoneDoesNotPutTheBodyInThatFrame()
    {
        FrameHandle gone = default;
        Entity body = default;
        using var test = new TestWorld(TestPhysics.Create((context, output) =>
            output.Bodies.Add(new BodyState(body, gone, TestWorld.At(3), Vector3.Zero, Vector3.Zero))));
        World world = test.World;
        gone = world.SpawnStaticFrame(test.Root, TestWorld.At(-500), radius: 5);
        body = world.Spawn(new SpawnDesc { Frame = test.Root, Body = TestWorld.Ball });
        world.Destroy(gone.Entity).Should().BeTrue();

        Action tick = () => world.Tick();

        tick.Should().NotThrow("the report names a frame that was destroyed, and a bad report must not break the world");
        world.GetFrame(body).Should().Be(test.Root);
        world.AssertInvariants();
    }

    [Fact]
    public void ABodyStateInAFrameThatIsGoneIsReportedAndLeavesTheBodyWhereItWas()
    {
        FrameHandle gone = default;
        Entity body = default;
        using var test = new TestWorld(TestPhysics.Create((context, output) =>
            output.Bodies.Add(new BodyState(body, gone, TestWorld.At(3), new Vector3(4, 5, 6), new Vector3(0, 1, 0)))));
        World world = test.World;
        gone = world.SpawnStaticFrame(test.Root, TestWorld.At(-500), radius: 5);
        body = world.Spawn(new SpawnDesc { Frame = test.Root, Pose = TestWorld.At(7), LinearVelocity = new Vector3(1, 2, 3), Body = TestWorld.Ball });
        world.Destroy(gone.Entity).Should().BeTrue();

        world.Tick();

        // The pose and velocities are relative to the frame that is gone, so none of them can be used in the root frame.
        world.Errors.Should().ContainSingle(error => error.Contains("stale frame"));
        world.GetPose(body).Should().Be(TestWorld.At(7));
        world.GetVelocity(body).Should().Be(new Vector3(1, 2, 3));
        world.GetAngularVelocity(body).Should().Be(Vector3.Zero);
    }
}
