using System.Numerics;
using AwesomeAssertions;

namespace Tertius.Simulation.Tests;

/// <summary>
/// Physics follows the frame tree, so it never reports a hand-off that would put a frame inside itself, a hand-off into a
/// frame that is gone, or a body state in a frame that is gone. Only a bug in the backend reports one. In Debug builds
/// that breaks a contract. In Release builds the world skips the whole update of that entity for the step, says so once
/// in <see cref="World.Errors"/>, and stays coherent: every invariant holds, and nothing about the entity changes. The tests
/// run the world on real Jolt and add the report that only a bug produces to the output of the first step, through
/// <see cref="FaultInjectingBackend"/>.
/// </summary>
public sealed class InvariantHandOffTests
{
#if TERTIUS_CONTRACTS
    [Fact]
    public void ABodyStateInAFrameThatIsGoneBreaksAContract()
    {
        using TestWorld test = TestWorld.OnJolt(faults: out FaultInjectingBackend faults);
        World world = test.World;
        FrameHandle gone = world.SpawnStaticFrame(test.Root, TestWorld.At(-500), radius: 5);
        Entity body = world.Spawn(new() { Frame = test.Root, Body = TestWorld.Ball });
        faults.EditNextOutput =
            output => output.Bodies.Add(new(Entity: body, Frame: gone, Pose: TestWorld.At(3),
                LinearVelocity: Vector3.Zero, AngularVelocity: Vector3.Zero));
        world.Destroy(gone.Entity).Should().BeTrue();

        Action tick = world.Tick;

        tick.Should().Throw<ContractException>();
    }
#else
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
            Test = TestWorld.OnJolt(faults: out FaultInjectingBackend faults);
            // The edit runs on the physics thread during the first tick, after the constructor has set the fields it reads.
            faults.EditNextOutput = output =>
                output.HandOffs.Add(new HandOff(Entity: HullOwner, From: Test.Root, To: handOffTo(Hull, Inner), Pose: TestWorld.At(1), LinearVelocity: Vector3.Zero, AngularVelocity: Vector3.Zero));
            HullOwner = Test.World.Spawn(new SpawnDesc
            {
                Frame = Test.Root,
                Pose = TestWorld.At(100),
                AsFrame = new FrameSpec(FrameBounds.Sphere(50)),
                Body = new BodyDesc(Shape: new SphereShapeDesc(5), Layer: BodyLayer.FrameHull),
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
        scene.World.Errors.Should().ContainSingle(error => error.Contains("refused the hand-off"));
        scene.World.AssertInvariants();
    }

    [Fact]
    public void AHullHandedOffIntoItselfIsRefusedAndStaysWhereItWas()
    {
        using var scene = new HullWorld((hull, inner) => hull);

        scene.World.Tick();

        scene.World.Frames.Parent(scene.Hull).Should().Be(scene.Test.Root);
        scene.World.GetFrame(scene.HullOwner).Should().Be(scene.Test.Root);
        scene.World.Errors.Should().ContainSingle(error => error.Contains("refused the hand-off"));
        scene.World.AssertInvariants();
    }

    [Fact]
    public void ARefusedHandOffLeavesTheVelocityOfTheHullAsItWas()
    {
        using var scene = new HullWorld((hull, inner) => inner);

        scene.World.Tick();

        scene.World.Frames.LinearVelocity(scene.Hull).Should().Be(new Double3(1, 2, 3));
        scene.World.VelocityInOwnFrame(scene.HullOwner).Should().Be(new Vector3(1, 2, 3));
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
        using var test = TestWorld.OnJolt(faults: out FaultInjectingBackend faults);
        World world = test.World;
        FrameHandle gone = world.SpawnStaticFrame(test.Root, TestWorld.At(-500), radius: 5);
        Entity body = world.Spawn(new SpawnDesc { Frame = test.Root, Body = TestWorld.Ball });
        faults.EditNextOutput =
 output => output.Bodies.Add(new BodyState(Entity: body, Frame: gone, Pose: TestWorld.At(3), LinearVelocity: Vector3.Zero, AngularVelocity: Vector3.Zero));
        world.Destroy(gone.Entity).Should().BeTrue();

        Action tick = () => world.Tick();

        tick.Should().NotThrow("the report names a frame that was destroyed, and a bad report must not break the world");
        world.GetFrame(body).Should().Be(test.Root);
        world.AssertInvariants();
    }

    [Fact]
    public void ABodyStateInAFrameThatIsGoneIsReportedAndLeavesTheBodyWhereItWas()
    {
        using var test = TestWorld.OnJolt(faults: out FaultInjectingBackend faults);
        World world = test.World;
        FrameHandle gone = world.SpawnStaticFrame(test.Root, TestWorld.At(-500), radius: 5);
        Entity body = world.Spawn(new SpawnDesc { Frame = test.Root, Pose = TestWorld.At(7), LinearVelocity =
 new Vector3(1, 2, 3), Body = TestWorld.Ball });
        faults.EditNextOutput =
 output => output.Bodies.Add(new BodyState(Entity: body, Frame: gone, Pose: TestWorld.At(3), LinearVelocity: new Vector3(4, 5, 6), AngularVelocity: new Vector3(0, 1, 0)));
        world.Destroy(gone.Entity).Should().BeTrue();

        world.Tick();

        // The pose and velocities are relative to the frame that is gone, so none of them can be used in the root frame.
        world.Errors.Should().ContainSingle(error => error.Contains("stale frame"));
        world.PoseInOwnFrame(body).Should().Be(TestWorld.At(7));
        world.VelocityInOwnFrame(body).Should().Be(new Vector3(1, 2, 3));
        world.AngularVelocityInOwnFrame(body).Should().Be(Vector3.Zero);
    }

    [Fact]
    public void ABodyStateInAFrameThatIsGoneAlsoSkipsTheHandOffAndTheFrameStateOfItsEntity()
    {
        using var test = TestWorld.OnJolt(faults: out FaultInjectingBackend faults);
        World world = test.World;
        FrameHandle station = world.SpawnStaticFrame(test.Root, TestWorld.At(-500), radius: 100);
        FrameHandle gone = world.SpawnStaticFrame(test.Root, TestWorld.At(500), radius: 5);
        Entity owner = world.Spawn(new SpawnDesc
        {
            Frame = test.Root,
            Pose = TestWorld.At(100),
            AsFrame = new FrameSpec(FrameBounds.Sphere(50)),
            Body = new BodyDesc(Shape: new SphereShapeDesc(5), Layer: BodyLayer.FrameHull),
            LinearVelocity = new Vector3(1, 2, 3),
        });
        FrameHandle hull = world.GetOwnedFrame(owner);
        // Jolt sends the state of the hull's frame in this step, because the hull moves. The edit adds the broken body
        // state and a legal hand-off next to it.
        faults.EditNextOutput = output =>
        {
            output.Bodies.Add(new BodyState(Entity: owner, Frame: gone, Pose: TestWorld.At(1), LinearVelocity: Vector3.Zero, AngularVelocity: Vector3.Zero));
            output.HandOffs.Add(new HandOff(Entity: owner, From: test.Root, To: station, Pose: TestWorld.At(1), LinearVelocity: Vector3.Zero, AngularVelocity: Vector3.Zero));
        };
        world.Destroy(gone.Entity).Should().BeTrue();

        world.Tick();

        // Jolt did send the state of the hull's frame. The hand-off to the station is legal, but it comes from the same
        // broken report as the body state.
        faults.Outputs[0].Frames.Should().Contain(state => state.Frame == hull);
        world.Errors.Should().ContainSingle().Which.Should().Contain("stale frame");
        world.Frames.Parent(hull).Should().Be(test.Root);
        world.Frames.LocalPose(hull).Should().Be(TestWorld.At(100));
        world.Frames.LinearVelocity(hull).Should().Be(new Double3(1, 2, 3));
        world.GetFrame(owner).Should().Be(test.Root);
        world.PoseInOwnFrame(owner).Should().Be(TestWorld.At(100));
        world.AssertInvariants();
    }
#endif
}
