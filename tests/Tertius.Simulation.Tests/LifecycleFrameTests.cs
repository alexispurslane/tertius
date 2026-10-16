using AwesomeAssertions;

namespace Tertius.Simulation.Tests;

/// <summary>
/// What happens to the contents of a frame when its owner is destroyed. A frame is an entity with a bubble; the
/// entities and frames inside it keep their place in space and move to the nearest ancestor frame that survives. If
/// no ancestor survives (the frame is a root), they die with it. Poses are in the frame of the entity unless the test
/// says otherwise. All frames here are static, except the moon in <see cref="MoonScene"/>.
/// </summary>
public class LifecycleFrameTests
{
    private static readonly DQuaternion QuarterTurnAboutY = DQuaternion.FromAxisAngle(Double3.UnitY, Math.PI / 2);

    /// <summary>Checks that two positions are at most <paramref name="tolerance"/> metres apart, and shows both if they are not.</summary>
    private static void ShouldBeNear(Double3 actual, Double3 expected, double tolerance = 1e-9) =>
        (actual - expected).Length.Should().BeLessThanOrEqualTo(tolerance, "{0} should be within {2} m of {1}", actual, expected, tolerance);

    // ---- A ship with its crew, its cargo and a pod inside it -------------------------------------------------------

    /// <summary>
    /// A ship (a frame at x = 1000 m, turned a quarter turn about +Y) in the root frame. Inside it: a crew member with a
    /// body at x = 10 who hears of <see cref="FrameChanged"/>, a piece of cargo at z = 10, and a pod (a frame at
    /// x = -20) with a passenger at (1, 2, 3). The world has run one tick, and physics batches are cleared.
    /// </summary>
    private sealed class ShipScene : IDisposable
    {
        public TestWorld T { get; }
        public List<PhysicsChanges> Batches { get; }
        public Recorder CrewLog { get; } = new(EventMask.Of<FrameChanged>());
        public Entity ShipOwner { get; }
        public FrameHandle Ship { get; }
        public Entity Crew { get; }
        public Entity Cargo { get; }
        public FrameHandle Pod { get; }
        public Entity Passenger { get; }

        public ShipScene()
        {
            T = TestWorld.OnJolt(out FaultInjectingBackend faults);
            Batches = faults.AppliedChanges;
            ShipOwner = T.World.Spawn(new SpawnDesc
            {
                Frame = T.Root,
                Pose = new DPose(new Double3(1000, 0, 0), QuarterTurnAboutY),
                AsFrame = new FrameSpec(FrameBounds.Sphere(100), MotionDriver.Static),
            });
            Ship = T.World.GetOwnedFrame(ShipOwner);
            Crew = T.World.Spawn(new SpawnDesc { Frame = Ship, Pose = TestWorld.At(10), Body = TestWorld.Ball, Components = [CrewLog] });
            Cargo = T.World.SpawnAt(Ship, new Double3(0, 0, 10));
            Pod = T.SpawnStaticFrame(TestWorld.At(-20), 5, Ship);
            Passenger = T.World.SpawnAt(Pod, new Double3(1, 2, 3));
            T.Run(1);
            Batches.Clear();
        }

        public void DestroyTheShip() => T.World.Destroy(ShipOwner);

        public void Dispose() => T.Dispose();
    }

    [Fact]
    public void DestroyingAFrameEndsTheFrameButNotWhatWasInsideIt()
    {
        using var s = new ShipScene();

        s.DestroyTheShip();

        s.T.World.Frames.IsAlive(s.Ship).Should().BeFalse();
        s.T.World.IsAlive(s.ShipOwner).Should().BeFalse();
        new[] { s.Crew, s.Cargo, s.Passenger, s.Pod.Entity }.Should().OnlyContain(e => s.T.World.IsAlive(e));
    }

    [Fact]
    public void TheEntitiesOfADestroyedFrameMoveToItsParentFrame()
    {
        using var s = new ShipScene();

        s.DestroyTheShip();

        s.T.World.GetFrame(s.Crew).Should().Be(s.T.Root);
        s.T.World.GetFrame(s.Cargo).Should().Be(s.T.Root);
    }

    [Fact]
    public void TheChildFramesOfADestroyedFrameMoveToItsParentFrame()
    {
        using var s = new ShipScene();

        s.DestroyTheShip();

        s.T.World.Frames.Parent(s.Pod).Should().Be(s.T.Root);
        s.T.World.GetFrame(s.Pod.Entity).Should().Be(s.T.Root);
    }

    [Fact]
    public void WhatWasInsideAChildFrameStaysInThatFrame()
    {
        using var s = new ShipScene();

        s.DestroyTheShip();

        s.T.World.GetFrame(s.Passenger).Should().Be(s.Pod);
    }

    [Fact]
    public void AnEntityOfADestroyedFrameKeepsItsPlaceInSpace()
    {
        using var s = new ShipScene();

        s.DestroyTheShip();

        // A quarter turn about +Y maps local +X to -Z and local +Z to +X. The ship is at x = 1000.
        Double3 crew = s.T.World.PoseInOwnFrame(s.Crew).Position;
        Double3 cargo = s.T.World.PoseInOwnFrame(s.Cargo).Position;
        ShouldBeNear(crew, new Double3(1000, 0, -10));
        ShouldBeNear(cargo, new Double3(1010, 0, 0));
    }

    [Fact]
    public void AChildFrameOfADestroyedFrameKeepsItsPlaceInSpace()
    {
        using var s = new ShipScene();

        s.DestroyTheShip();

        // The pod is at x = -20 in the ship, which is (0, 0, 20) in the root frame's axes, added to the ship's origin.
        Double3 pod = s.T.World.PoseInOwnFrame(s.Pod.Entity).Position;
        ShouldBeNear(pod, new Double3(1000, 0, 20));
    }

    [Fact]
    public void AnEntityOfADestroyedFrameKeepsItsOrientationInSpace()
    {
        using var s = new ShipScene();

        s.DestroyTheShip();

        // The cargo had no turn of its own in the ship, so in the root frame it is turned as the ship was.
        DQuaternion rotation = s.T.World.PoseInOwnFrame(s.Cargo).Rotation;
        rotation.X.Should().BeApproximately(0, 1e-9);
        rotation.Y.Should().BeApproximately(Math.Sin(Math.PI / 4), 1e-9);
        rotation.Z.Should().BeApproximately(0, 1e-9);
        rotation.W.Should().BeApproximately(Math.Cos(Math.PI / 4), 1e-9);
    }

    [Fact]
    public void WhatWasInsideAChildFrameKeepsItsPlaceInSpace()
    {
        using var s = new ShipScene();
        s.T.World.Snapshot.TryGetPoseIn(s.Passenger, s.T.Root, out DPose before).Should().BeTrue();

        s.DestroyTheShip();

        s.T.World.Snapshot.TryGetPoseIn(s.Passenger, s.T.Root, out DPose after).Should().BeTrue();
        ShouldBeNear(after.Position, before.Position);
    }

    [Fact]
    public void PhysicsMovesTheBodyOfAnEntityOfADestroyedFrameToTheParentFramesSystem()
    {
        using var s = new ShipScene();

        s.DestroyTheShip();

        // Physics hears of it as one batch, which applies destroys before creates.
        PhysicsChanges batch = s.Batches.Should().ContainSingle().Subject;
        batch.BodiesDestroyed.Should().Contain(s.Crew);
        batch.BodiesCreated.Should().Contain(create => create.Entity == s.Crew && create.Frame == s.T.Root);
    }

    [Fact]
    public void PhysicsHearsThatTheFrameWasDestroyed()
    {
        using var s = new ShipScene();

        s.DestroyTheShip();

        s.Batches.Should().ContainSingle().Which.FramesDestroyed.Should().Contain(s.Ship);
    }

    [Fact]
    public void TheEntityThatMovedHearsOfItOnTheSecondTickAfterTheDestroy()
    {
        using var s = new ShipScene();
        s.DestroyTheShip();

        // Like a posted message, the FrameChanged of an outside-tick destroy joins the next routing, which is at the sync
        // point of the next tick. The crew member handles it on the tick after that.
        s.T.Run(1);
        s.CrewLog.Seen.Should().BeEmpty();
        s.T.Run(1);

        (long tick, Event e) = s.CrewLog.Seen.Should().ContainSingle().Subject;
        tick.Should().Be(2);
        e.As<FrameChanged>().From.Should().Be(s.Ship);
        e.As<FrameChanged>().To.Should().Be(s.T.Root);
    }

    [Fact]
    public void DestroyingAFrameReportsNoErrors()
    {
        using var s = new ShipScene();

        s.DestroyTheShip();
        s.T.Run(3);

        s.T.World.Errors.Should().BeEmpty();
    }

    // ---- A root frame ---------------------------------------------------------------------------------------------

    /// <summary>A world with a frame inside the root, one entity in the root and one in that frame. It has four live entities.</summary>
    private static TestWorld RootWithContents(out FrameHandle inner, out Entity inRoot, out Entity inInner)
    {
        var t = new TestWorld();
        inner = t.SpawnStaticFrame(TestWorld.At(10), 10);
        inRoot = t.SpawnWith();
        inInner = t.World.SpawnWith(inner);
        return t;
    }

    [Fact]
    public void DestroyingARootFrameDestroysTheEntitiesInIt()
    {
        using TestWorld t = RootWithContents(out _, out Entity inRoot, out Entity inInner);

        t.World.Destroy(t.Root.Entity);

        t.World.IsAlive(inRoot).Should().BeFalse();
        t.World.IsAlive(inInner).Should().BeFalse();
    }

    [Fact]
    public void DestroyingARootFrameDestroysTheFramesInsideIt()
    {
        using TestWorld t = RootWithContents(out FrameHandle inner, out _, out _);

        t.World.Destroy(t.Root.Entity);

        t.World.Frames.IsAlive(inner).Should().BeFalse();
        t.World.Frames.Count.Should().Be(0);
    }

    [Fact]
    public void DestroyingARootFrameLeavesNoLiveEntity()
    {
        using TestWorld t = RootWithContents(out _, out _, out _);
        t.World.Stats.LiveEntities.Should().Be(4);

        t.World.Destroy(t.Root.Entity);

        t.World.Stats.LiveEntities.Should().Be(0);
    }

    [Fact]
    public void TheOwnerOfARootFrameIsInNoFrame()
    {
        using var t = new TestWorld();

        t.World.GetFrame(t.Root.Entity).Should().Be(FrameHandle.Null);
    }

    [Fact]
    public void TheOwnerOfARootFrameOwnsThatFrame()
    {
        using var t = new TestWorld();

        t.World.GetOwnedFrame(t.Root.Entity).Should().Be(t.Root);
    }

    [Fact]
    public void ThePoseOfTheOwnerOfARootFrameIsThePoseOfTheFrame()
    {
        using var t = new TestWorld();

        t.World.PoseIn(t.Root.Entity, t.Root).Should().Be(DPose.Identity);
        t.World.Snapshot.TryGetPoseIn(t.Root.Entity, t.Root, out DPose pose).Should().BeTrue();
        pose.Position.Should().Be(Double3.Zero);
    }

    [Fact]
    public void TheOwnerOfARootFrameIsAtRest()
    {
        using var t = new TestWorld();

        t.World.VelocityIn(t.Root.Entity, t.Root).Should().Be(Double3.Zero);
    }

    // ---- Two frames that die together ------------------------------------------------------------------------------

    /// <summary>
    /// An outer frame at x = 100 and an inner frame at x = 10 inside it, with an entity at x = 1 in the inner one, and
    /// an entity in the root that sends a Destroy to both frame owners on tick 0. The world has run two ticks. Each
    /// entity is in its own chunk.
    /// </summary>
    private static TestWorld NestedFramesAfterTheyDie(out FrameHandle outer, out FrameHandle inner, out Entity x)
    {
        var t = new TestWorld(chunkSize: 1);
        outer = t.SpawnStaticFrame(TestWorld.At(100), 50);
        inner = t.SpawnStaticFrame(TestWorld.At(10), 5, outer);
        x = t.World.SpawnAt(inner, new Double3(1, 0, 0));
        t.SpawnWith(SendOnce.Of(inner.Entity, new Destroy()), SendOnce.Of(outer.Entity, new Destroy()));
        t.Run(2);
        return t;
    }

    [Fact]
    public void NestedFramesThatAreDestroyedTogetherAreBothGone()
    {
        using TestWorld t = NestedFramesAfterTheyDie(out FrameHandle outer, out FrameHandle inner, out _);

        t.World.Frames.IsAlive(outer).Should().BeFalse();
        t.World.Frames.IsAlive(inner).Should().BeFalse();
    }

    [Fact]
    public void AnEntityInNestedFramesThatDieTogetherMovesToTheNearestSurvivor()
    {
        using TestWorld t = NestedFramesAfterTheyDie(out _, out _, out Entity x);

        t.World.IsAlive(x).Should().BeTrue();
        t.World.GetFrame(x).Should().Be(t.Root);
    }

    [Fact]
    public void AnEntityInNestedFramesThatDieTogetherKeepsItsPlaceInSpace()
    {
        using TestWorld t = NestedFramesAfterTheyDie(out _, out _, out Entity x);

        // 100 for the outer frame, 10 for the inner one, 1 for the entity.
        Double3 position = t.World.PoseInOwnFrame(x).Position;
        ShouldBeNear(position, new Double3(111, 0, 0));
    }

    [Fact]
    public void NestedFramesThatAreDestroyedTogetherReportNoErrors()
    {
        using TestWorld t = NestedFramesAfterTheyDie(out _, out _, out _);

        t.World.Errors.Should().BeEmpty();
    }

    // ---- A frame inside two frames that die together ---------------------------------------------------------------

    /// <summary>
    /// A pod inside an inner frame, inside an outer frame, in the root: the outer frame is at x = 100, the inner one at
    /// x = 10 in it and turned a quarter turn about +Y, and the pod at z = 5 in the inner one. The pod's owner hears of
    /// <see cref="FrameChanged"/>, and a passenger is at x = 1 in the pod. Both outer and inner owners get a Destroy
    /// on tick 0 and the world runs three ticks. With <paramref name="innerOwnerHasTheLowerSlot"/> the inner frame's owner
    /// is in the lower slot, so that the sync point destroys it first; without, the outer frame's owner is.
    /// </summary>
    private static TestWorld PodInTwoDyingFrames(bool innerOwnerHasTheLowerSlot, out FrameHandle inner, out FrameHandle pod, out Entity passenger, out Recorder podLog)
    {
        var t = new TestWorld(chunkSize: 1);
        if (innerOwnerHasTheLowerSlot)
        {
            // Free two slots, higher one first, so that the first frame owner spawned below takes the higher slot and the second the lower.
            Entity low = t.SpawnWith(), high = t.SpawnWith();
            t.World.Destroy(high);
            t.World.Destroy(low);
        }
        FrameHandle outer = t.SpawnStaticFrame(TestWorld.At(100), 50);
        inner = t.SpawnStaticFrame(new DPose(new Double3(10, 0, 0), QuarterTurnAboutY), 20, outer);
        pod = t.SpawnStaticFrame(TestWorld.At(0, 0, 5), 2, inner);
        (inner.Entity.Index < outer.Entity.Index).Should().Be(innerOwnerHasTheLowerSlot, "this is the order of death that the test wants");
        podLog = new Recorder(EventMask.Of<FrameChanged>());
        t.World.AddComponent(pod.Entity, podLog);
        passenger = t.World.SpawnAt(pod, new Double3(1, 0, 0));
        t.SpawnWith(SendOnce.Of(outer.Entity, new Destroy()), SendOnce.Of(inner.Entity, new Destroy()));
        t.Run(3);
        return t;
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void AFrameInsideTwoFramesThatDieTogetherMovesToTheNearestSurvivor(bool innerOwnerHasTheLowerSlot)
    {
        using TestWorld t = PodInTwoDyingFrames(innerOwnerHasTheLowerSlot, out _, out FrameHandle pod, out _, out _);

        t.World.Frames.Parent(pod).Should().Be(t.Root);
        t.World.GetFrame(pod.Entity).Should().Be(t.Root);
        t.World.PoseInOwnFrame(pod.Entity).Should().Be(t.World.Frames.LocalPose(pod));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void AFrameInsideTwoFramesThatDieTogetherKeepsItsPlaceInSpace(bool innerOwnerHasTheLowerSlot)
    {
        using TestWorld t = PodInTwoDyingFrames(innerOwnerHasTheLowerSlot, out _, out FrameHandle pod, out _, out _);

        // The inner frame's quarter turn maps the pod's offset of z = 5 to x = 5. With the inner frame's x = 10 and the outer's 100, that is x = 115.
        Double3 position = t.World.PoseInOwnFrame(pod.Entity).Position;
        ShouldBeNear(position, new Double3(115, 0, 0));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void WhatWasInsideThePodStaysInThePodAndKeepsItsPlaceInSpace(bool innerOwnerHasTheLowerSlot)
    {
        using TestWorld t = PodInTwoDyingFrames(innerOwnerHasTheLowerSlot, out _, out FrameHandle pod, out Entity passenger, out _);

        t.World.GetFrame(passenger).Should().Be(pod);
        t.World.Snapshot.TryGetPoseIn(passenger, t.Root, out DPose inRoot).Should().BeTrue();
        // The pod turned with the inner frame, so the passenger's x = 1 offset became z = -1 beside the pod at x = 115.
        ShouldBeNear(inRoot.Position, new Double3(115, 0, -1));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ThePodsOwnerHearsOnceThatItMovedFromTheFrameItWasInToTheSurvivor(bool innerOwnerHasTheLowerSlot)
    {
        using TestWorld t = PodInTwoDyingFrames(innerOwnerHasTheLowerSlot, out FrameHandle inner, out _, out _, out Recorder podLog);

        FrameChanged changed = podLog.Seen.Should().ContainSingle().Subject.Event.As<FrameChanged>();
        changed.From.Should().Be(inner);
        changed.To.Should().Be(t.Root);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void AFrameInsideTwoFramesThatDieTogetherReportsNoErrors(bool innerOwnerHasTheLowerSlot)
    {
        using TestWorld t = PodInTwoDyingFrames(innerOwnerHasTheLowerSlot, out _, out _, out _, out _);

        t.World.Errors.Should().BeEmpty();
    }

    // ---- A child frame that moves ----------------------------------------------------------------------------------

    /// <summary>
    /// A moon on a circular orbit of radius 7000 km around the root, spinning at 0.01 rad/s. A pod (a static frame at
    /// (100, 20, -30)) rides in the moon, with a passenger at x = 1. The world has run three ticks, and the pod's velocity
    /// and spin relative to the root, and the passenger's pose in the root, were read before the moon's owner is destroyed.
    /// </summary>
    private sealed class MoonScene : IDisposable
    {
        public TestWorld T { get; } = new();
        public Entity MoonOwner { get; }
        public FrameHandle Pod { get; }
        public Entity Passenger { get; }
        public Double3 PodVelocityBefore { get; }
        public Double3 PodSpinBefore { get; }
        public DPose PassengerInRootBefore { get; }

        public MoonScene()
        {
            // The moon takes its G·M from the point gravity of the root frame.
            T.GiveRootPointGravity(3.986004418e14);
            MoonOwner = T.World.Spawn(new SpawnDesc
            {
                Frame = T.Root,
                AsFrame = new FrameSpec(FrameBounds.Sphere(1e5f), MotionDriver.Kepler, Orbit: KeplerOrbit.Circular(7e6, spinRate: 0.01)),
            });
            FrameHandle moon = T.World.GetOwnedFrame(MoonOwner);
            Pod = T.SpawnStaticFrame(TestWorld.At(100, 20, -30), 10, moon);
            Passenger = T.World.SpawnAt(Pod, new Double3(1, 0, 0));
            T.Run(3);
            PodVelocityBefore = T.World.Frames.ConvertVelocity(Pod, T.Root, Double3.Zero, Double3.Zero);
            PodSpinBefore = T.World.Frames.AngularVelocityRelative(Pod, T.Root);
            T.World.Snapshot.TryGetPoseIn(Passenger, T.Root, out DPose passengerInRoot).Should().BeTrue();
            PassengerInRootBefore = passengerInRoot;
        }

        public void DestroyTheMoon() => T.World.Destroy(MoonOwner);

        public void Dispose() => T.Dispose();
    }

    [Fact]
    public void TheMoonInTheSceneReallyMovesFast()
    {
        // Guards the tests below against passing for the wrong reason: a circular orbit of radius 7000 km has a speed of
        // sqrt(3.986e14 / 7e6), about 7500 m/s, and the pod rides it.
        using var s = new MoonScene();

        s.PodVelocityBefore.Length.Should().BeGreaterThan(7000);
    }

    [Fact]
    public void AChildFrameOfADestroyedFrameMovesToTheGrandparentFrame()
    {
        using var s = new MoonScene();

        s.DestroyTheMoon();

        s.T.World.Frames.Parent(s.Pod).Should().Be(s.T.Root);
    }

    [Fact]
    public void AChildFrameOfADestroyedFrameKeepsItsVelocityInSpace()
    {
        using var s = new MoonScene();

        s.DestroyTheMoon();

        // The pod's origin moved at the moon's velocity plus the spin's lever arm. Now the root frame is its parent.
        Double3 velocity = s.T.World.Frames.LinearVelocity(s.Pod);
        ShouldBeNear(velocity, s.PodVelocityBefore, 1e-6);
    }

    [Fact]
    public void AChildFrameOfADestroyedFrameKeepsItsSpinInSpace()
    {
        using var s = new MoonScene();

        s.DestroyTheMoon();

        Double3 spin = s.T.World.Frames.AngularVelocity(s.Pod);
        ShouldBeNear(spin, s.PodSpinBefore, 1e-8);
    }

    [Fact]
    public void TheOwnerRowOfAMovedChildFrameMirrorsTheFrameExactly()
    {
        using var s = new MoonScene();

        s.DestroyTheMoon();

        s.T.World.GetFrame(s.Pod.Entity).Should().Be(s.T.Root);
        s.T.World.PoseInOwnFrame(s.Pod.Entity).Should().Be(s.T.World.Frames.LocalPose(s.Pod));
        s.T.World.VelocityInOwnFrame(s.Pod.Entity).Should().Be(s.T.World.Frames.LinearVelocity(s.Pod).ToVector3());
    }

    [Fact]
    public void WhatWasInsideAMovedChildFrameStaysThereAndKeepsItsPlaceInSpace()
    {
        using var s = new MoonScene();

        s.DestroyTheMoon();

        s.T.World.GetFrame(s.Passenger).Should().Be(s.Pod);
        s.T.World.Snapshot.TryGetPoseIn(s.Passenger, s.T.Root, out DPose after).Should().BeTrue();
        ShouldBeNear(after.Position, s.PassengerInRootBefore.Position, 1e-6);
    }

    [Fact]
    public void DestroyingTheMoonReportsNoErrors()
    {
        using var s = new MoonScene();

        s.DestroyTheMoon();
        s.T.Run(2);

        s.T.World.Errors.Should().BeEmpty();
    }

    // ---- A spawn into a frame that dies ----------------------------------------------------------------------------

    /// <summary>
    /// A frame whose owner destroys itself on tick 0, and an entity in the root that spawns a child into that frame on
    /// the same tick. The world has run one tick.
    /// </summary>
    private static TestWorld SpawnIntoAFrameThatDiesInTheSameTick(out FrameHandle doomed, out Entity child)
    {
        var t = new TestWorld();
        doomed = t.World.GetOwnedFrame(t.World.Spawn(new SpawnDesc
        {
            Frame = t.Root,
            AsFrame = new FrameSpec(FrameBounds.Sphere(10), MotionDriver.Static),
            Components = [Lambda.OnTick((self, scoped ref events, scoped ref outbox) => self.Destroy())],
        }));
        FrameHandle target = doomed;
        Entity spawned = default;
        t.SpawnWith(Lambda.OnTick((self, scoped ref events, scoped ref outbox) =>
        {
            if (self.Tick == 0)
                spawned = self.Spawn(new SpawnDesc { Frame = target });
        }));
        t.Run(1);
        child = spawned;
        return t;
    }

    [Fact]
    public void ASpawnIntoAFrameThatDiesInTheSameTickNeverBecomesLive()
    {
        using TestWorld t = SpawnIntoAFrameThatDiesInTheSameTick(out FrameHandle doomed, out Entity child);

        t.World.Frames.IsAlive(doomed).Should().BeFalse();
        t.World.IsAlive(child).Should().BeFalse();
    }

    [Fact]
    public void ASpawnIntoAFrameThatDiesInTheSameTickIsReportedAsDropped()
    {
        using TestWorld t = SpawnIntoAFrameThatDiesInTheSameTick(out _, out Entity child);

        t.World.Errors.Should().ContainSingle().Which.Should().Contain("dropped the spawn").And.Contain(child.ToString());
    }

    [Fact]
    public void TheSlotOfADroppedSpawnIsFreedForLaterSpawns()
    {
        using TestWorld t = SpawnIntoAFrameThatDiesInTheSameTick(out _, out Entity child);

        // Two slots were freed at that sync point: the destroyed frame's owner, then the dropped spawn.
        Entity[] next = [t.SpawnWith(), t.SpawnWith()];

        next.Select(e => e.Index).Should().Contain(child.Index);
    }
}
