using System.Numerics;

namespace Tertius.Physics.Tests;

/// <summary>
/// Physics follows the frame tree. At the start of each step, and in ApplyChanges, the backend reconciles with the tree:
/// it drops the hand-offs of the last step and puts each frame hull in the physics system of its frame's parent in the
/// tree. These tests pin down what the reconcile guarantees, also when it cannot place a hull.
/// </summary>
public class ReconcileTests
{
    /// <summary>
    /// Probe P1 of the review, with physics systems of at most 8 bodies. A hangar bubble of radius 30 m, 1 km along +X,
    /// and a ship 40 m beyond the hangar's center that flies into it at 60 m/s. Far along -X, a storage frame whose
    /// system is full with 8 static crates, and a static skiff. The backend reconciles frame owners from the last one
    /// created, so it reaches the skiff's hull before the ship's.
    /// </summary>
    private sealed class FullStorage : IDisposable
    {
        public FullStorage()
        {
            H = new PhysicsHarness(new JoltBackendOptions { MaxBodiesPerFrame = 8 });
            Space = H.AddFrame(FrameHandle.Null, DPose.Identity, FrameBounds.Sphere(1e7f), MotionDriver.Static);
            Hangar = H.AddFrame(Space, Shapes.At(1000, 0, 0), FrameBounds.Sphere(30), MotionDriver.Static);
            Ship = H.AddFrameWithBody(Space, Shapes.At(1040, 0, 0), FrameBounds.Sphere(10), MotionDriver.Physics,
                Shapes.Box(2, mass: 1000), velocity: new Vector3(-60, 0, 0));
            Storage = H.AddFrame(Space, Shapes.At(-5000, 0, 0), FrameBounds.Sphere(50), MotionDriver.Static);
            for (int i = 0; i < 8; i++)
                Crates.Add(H.AddBody(Storage, Shapes.Box(0.5f, motion: BodyMotion.Static), Shapes.At(3 * i - 12, 0, 0)));
            Skiff = H.AddFrameWithBody(Space, Shapes.At(-3000, 0, 0), FrameBounds.Sphere(5), MotionDriver.Static,
                new BodyDesc(new BoxShapeDesc(new Vector3(1)), BodyMotion.Static));
        }

        public PhysicsHarness H { get; }
        public FrameHandle Space { get; }
        public FrameHandle Hangar { get; }
        public FrameHandle Ship { get; }
        public FrameHandle Storage { get; }
        public List<Entity> Crates { get; } = new();
        public FrameHandle Skiff { get; }

        /// <summary>
        /// Steps until physics hands the ship into the hangar. Then, between steps and with no change batch, the game
        /// moves the skiff into storage, the ship back into space and the hangar under the ship. Every move is legal.
        /// </summary>
        public void MoveFramesAfterTheShipEntersTheHangar()
        {
            H.StepUntil(() => H.HandOffs.Count > 0, 60);
            H.HandOffs.Should().ContainSingle().Which.To.Should().Be(Hangar);
            H.Frames.Reparent(Skiff, Storage);
            H.Frames.Reparent(Ship, Space);
            H.Frames.Reparent(Hangar, Ship);
            H.Frames.Update();
        }

        public void Dispose() => H.Dispose();
    }

    [Fact]
    public void AHullThatCannotBePlacedDoesNotStopTheOtherHullsFromFollowingTheTree()
    {
        using var scene = new FullStorage();
        PhysicsHarness h = scene.H;
        scene.MoveFramesAfterTheShipEntersTheHangar();

        // At 60 m/s the ship's hull needs about 70 steps to cross the hangar's bubble. A hull that the reconcile left in
        // the hangar's system would then leave it for the hangar's parent, which is the ship's own frame.
        for (int i = 0; i < 90; i++)
        {
            h.Invoking(x => x.Step()).Should().NotThrow("a full system is no reason to stop the step {0}", i);
            h.Backend.TryGetBodyFrame(scene.Ship.Entity, out FrameHandle hullFrame).Should().BeTrue();
            hullFrame.Should().Be(h.Frames.Parent(scene.Ship), "the ship's hull follows the tree after step {0}", i);
        }

        h.HandOffs.Should().ContainSingle("the ship has no other frame to enter or leave");
        h.Backend.TryGetBodyFrame(scene.Skiff.Entity, out _).Should().BeFalse("the storage system has no room for the skiff's hull");
        h.Warnings.Should().Contain(warning => warning.Contains(scene.Skiff.Entity.ToString()) && warning.Contains(scene.Storage.ToString()));
    }

    [Fact]
    public void AHullThatCouldNotBePlacedGetsItsBodyOnceTheSystemHasRoom()
    {
        using var scene = new FullStorage();
        PhysicsHarness h = scene.H;
        scene.MoveFramesAfterTheShipEntersTheHangar();
        h.Step();
        h.Backend.TryGetBodyFrame(scene.Skiff.Entity, out _).Should().BeFalse();

        // A crate leaves storage. Nothing in the frame tree changes.
        h.Destroy(scene.Crates[0]);
        h.Step();

        h.Backend.TryGetBodyFrame(scene.Skiff.Entity, out FrameHandle hullFrame).Should().BeTrue();
        hullFrame.Should().Be(scene.Storage);
    }

    [Fact]
    public void TheInvariantsFailWhileAHullHasNoBodyAndHoldOnceTheReconcilePlacesIt()
    {
        using var scene = new FullStorage();
        PhysicsHarness h = scene.H;
        scene.MoveFramesAfterTheShipEntersTheHangar();
        h.Reconcile();

        h.Backend.Invoking(backend => backend.AssertInvariants()).Should().Throw<ContractException>("the skiff's frame has a parent, but its hull has no body");

        h.Destroy(scene.Crates[0]);
        h.Reconcile();

        h.Backend.Invoking(backend => backend.AssertInvariants()).Should().NotThrow();
    }

    [Fact]
    public void AfterARefusedHandOffTheReconcileRestoresTheInvariants()
    {
        using var h = new PhysicsHarness();
        FrameHandle space = h.AddFrame(FrameHandle.Null, DPose.Identity, FrameBounds.Sphere(1e7f), MotionDriver.Static);
        FrameHandle carrier = h.AddFrame(space, Shapes.At(1000, 0, 0), FrameBounds.Sphere(200), MotionDriver.Static);
        FrameHandle fighter = h.AddFrameWithBody(space, Shapes.At(1260, 0, 0), FrameBounds.Sphere(10), MotionDriver.Physics,
            Shapes.Box(2, mass: 1000), velocity: new Vector3(-60, 0, 0));
        h.RefusesHandOff = handOff => handOff.Entity == fighter.Entity;
        h.StepUntil(() => h.RefusedHandOffs.Count > 0, 120);
        h.RefusedHandOffs.Should().ContainSingle().Which.To.Should().Be(carrier);

        // The step left the fighter's hull in the carrier's system, and the tree refused to follow.
        h.Backend.Invoking(backend => backend.AssertInvariants()).Should().Throw<ContractException>();

        h.Reconcile();

        h.Backend.Invoking(backend => backend.AssertInvariants()).Should().NotThrow();
        h.Backend.TryGetBodyFrame(fighter.Entity, out FrameHandle hullFrame).Should().BeTrue();
        hullFrame.Should().Be(space);
    }
}
