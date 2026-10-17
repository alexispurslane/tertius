using System.Numerics;
using Tertius.Simulation;
using static Tertius.Testing.WorldTesting;
using static Tertius.Testing.NumericFrameTesting;

namespace Tertius.Physics.Tests;

/// <summary>
/// A body that sticks out of the bubble of its frame, a semantic child of another frame, has a ghost in the system of that
/// semantic parent, so that the bodies there collide with it. A ghost is a kinematic copy of a body in the physics system
/// of another frame. Numeric frames are an engine detail with no bubbles, so they must not change which bodies meet it: a
/// body of the semantic parent whose physics home is a numeric frame of its own meets it too, as it does in the semantic
/// parent's own system without numeric frames (ticket ter-2gg0). The merge test runs per semantic frame, so it never links
/// that body to the body that sticks out. So the numeric update names each numeric frame of the semantic parent whose
/// members may touch a body that sticks out of the bubble (see <see cref="FrameTree.StickOutGhosts"/>), and the ghost pass
/// puts the ghost of each such body there too. These tests run the World on the real backend, with the default numeric
/// frame options, and with no numeric frames. Under the defaults, a group of free bodies falls in a numeric frame from
/// 1,000 m/s relative to its semantic frame, and a group with a body that thrusts falls from 50 m/s. So the rocks and ships
/// here that fly at 60 m/s and must fall thrust gently (see <see cref="Thruster"/>), from 0.5 s before their falling frames
/// matter. A slow group rests in a numeric frame from 2^23 m (8,389 km) from its semantic frame's origin, so the scenes
/// whose bodies must rest lie 9,000 km out (see <see cref="FarOut"/>).
/// </summary>
public partial class NumericFrameStickOutGhostTests
{
    /// <summary>A world on the wrapper with the default numeric frames or, when <paramref name="numericFrames"/> is false, with none.</summary>
    private static World NewWorld(bool numericFrames, out FaultInjectingBackend faults) =>
        NewWorldWithFaults(out faults, configure: numericFrames ? null : options => options.NumericFrames = WithoutNumericFrames);

    /// <summary>A hull of 4 m by 4 m by 4 m and 1000 kg that nothing slows down and that does not bounce.</summary>
    private static readonly BodyDesc ShipHull = new(new BoxShapeDesc(new Vector3(2)), Mass: 1000, Restitution: 0, LinearDamping: 0, AngularDamping: 0);

    /// <summary>A crate of 2 m by 2 m by 2 m and 2 kg that nothing slows down and that does not bounce.</summary>
    private static readonly BodyDesc CrateBody = new(new BoxShapeDesc(new Vector3(1)), Mass: 2, Restitution: 0, LinearDamping: 0, AngularDamping: 0);

    /// <summary>
    /// A rock: a ball of radius 0.5 m and 1 kg that nothing slows down and that does not bounce. A ghost is kinematic, and
    /// Jolt makes no contact between a kinematic body and a static one, so the rock is dynamic.
    /// </summary>
    private static readonly BodyDesc RockBody = new(new SphereShapeDesc(0.5f), Mass: 1, Restitution: 0, LinearDamping: 0, AngularDamping: 0);

    /// <summary>
    /// How many ticks <see cref="Build"/> runs a scene before its test starts: the 30 ticks of 1/60 s, 0.5 s, of thrust
    /// after which a body with a <see cref="Thruster"/> thrusts (see <see cref="NumericFrameOptions.ThrustOnset"/>). From the
    /// sync point of the last of them on, a group with that body falls in a numeric frame from 50 m/s.
    /// </summary>
    private const int OnsetTicks = 30;

    /// <summary>
    /// How far along +X from the origin of space the scenes put the bodies that must rest in numeric frames: 9,000 km, past
    /// the 2^23 m (8,389 km) from which a slow group gets a resting frame (see <see cref="NumericFrameOptions.CreateDistance"/>).
    /// </summary>
    private const double FarOut = 9_000_000;

    /// <summary>
    /// The scenes of these tests. <see cref="Build"/> runs each for <see cref="OnsetTicks"/> ticks, so the places here are
    /// those at the spawn, and each layout says where its bodies are when the test starts.
    /// </summary>
    public enum Layout
    {
        /// <summary>
        /// Space, and at (1000, 0, 0) in it a parked station: a frame that physics does not drive, with a bubble of radius
        /// 40 m and no hull. A static hull at its center would be an anchor, which the merge test links to the rock 124 m
        /// before the hull, so the rock would join the system of space and fall in no numeric frame near the station
        /// (ter-dhkf). The station hangs directly in space, with numeric frames or without. A crate floats at (0, 40.5, 0)
        /// in the station and sticks 1.5 m out of its bubble. A rock flies at 60 m/s along -X from (1130, 41, 0) in space,
        /// with a gentle thrust. When the test starts, the rock thrusts and is at (1100, 41, 0), so it falls in a numeric
        /// frame of its own, through the part of the crate that sticks out. The rock's center stays at least 1 m outside
        /// the station's bubble, so it never touches the bubble and never meets the real crate.
        /// </summary>
        StationAndFastRock,

        /// <summary>
        /// Space, and at (<see cref="FarOut"/> + 130, 0, 0) in it a ship that flies at 60 m/s along -X: a hull of 4 m by 4 m
        /// by 4 m and 1000 kg that physics drives, with a gentle thrust, and a bubble of radius 200 m. When the test starts,
        /// the hull thrusts and is 100 m from the rock along X, so it falls in a numeric frame of its own, under which the
        /// ship's frame hangs. A crate floats at (0, -200, 0) in the ship, on the edge of its bubble, and so sticks out of
        /// it. A rock rests at (<see cref="FarOut"/>, -200.6, 0) in space, in the path of the part of the crate that sticks
        /// out, 0.1 m outside the ship's bubble. It is slow and 9,000 km from the origin of space, so it rests in a numeric
        /// frame of its own. The merge test reaches the bounding spheres of the hull and the rock plus 2 s of their relative
        /// speed, about 124 m, and the rock stays at least 200 m from the hull, so the merge test never links them.
        /// </summary>
        ShipAndFarRock,

        /// <summary>
        /// Space, and at (1100, 0, 0) in it the ship of <see cref="ShipAndFarRock"/>, which flies at 60 m/s along -X with a
        /// gentle thrust and so falls in a numeric frame of its own when the test starts. The crate at (0, -200, 0) in the
        /// ship drifts out of its bubble along -Y at 2 m/s. A rock flies beside the ship with the ship's velocity and the
        /// same gentle thrust, from (1100, -202.6, 0) in space, 1.1 m below the crate's face. When the test starts, the rock
        /// thrusts, so it falls in a numeric frame of its own, and the crate has drifted 1 m, to 0.1 m above the rock. The
        /// two move together, so the merge test reaches only the bounding spheres of the hull and the rock, about 4 m, and
        /// never links them.
        /// </summary>
        ShipAndRockBeside,
    }

    /// <summary>
    /// A scene of <see cref="Layout"/>: <see cref="Bubble"/> is the frame that the crate sticks out of and <see cref="Owner"/>
    /// its owner, and <see cref="CrateLog"/> and <see cref="RockLog"/> hear the collisions of the crate and of the rock.
    /// </summary>
    private sealed record Scene(Layout Layout, FrameHandle Space, Entity Owner, FrameHandle Bubble, Entity Crate, Entity Rock,
        CollisionLog CrateLog, CollisionLog RockLog);

    /// <summary>
    /// Builds the scene of <paramref name="layout"/> and runs it for <see cref="OnsetTicks"/> ticks, so that its bodies with
    /// a thruster thrust when the test starts. The crate floats at <paramref name="crateInBubble"/> in its frame at the
    /// spawn instead, when it is given. The rock is <paramref name="rockOffset"/> farther along +Y from where the layout puts
    /// it.
    /// </summary>
    private static Scene Build(World world, Layout layout, Double3? crateInBubble = null, double rockOffset = 0)
    {
        FrameHandle space = world.CreateRootFrame(new FrameSpec(FrameBounds.Sphere(1e7f), MotionDriver.Static));
        (DPose ownerPose, Double3 ownerVelocity, BodyDesc? hull, FrameSpec bubble) = layout switch
        {
            Layout.StationAndFastRock => (At(1000), Double3.Zero, null, new FrameSpec(FrameBounds.Sphere(40), MotionDriver.Static)),
            Layout.ShipAndFarRock => (At(FarOut + 130), new Double3(-60, 0, 0), ShipHull, new FrameSpec(FrameBounds.Sphere(200), MotionDriver.Physics)),
            _ => (At(1100), new Double3(-60, 0, 0), ShipHull, new FrameSpec(FrameBounds.Sphere(200), MotionDriver.Physics)),
        };
        Entity owner = world.Spawn(new SpawnDesc
        {
            Frame = space,
            Pose = ownerPose,
            LinearVelocity = ownerVelocity,
            Body = hull,
            AsFrame = bubble,
            Components = hull is null ? [] : [new Thruster(hull.Mass)],
        });
        FrameHandle frame = world.GetOwnedFrame(owner);
        (Double3 cratePlace, Double3 crateVelocity, Double3 rockPlace, Double3 rockVelocity) = layout switch
        {
            Layout.StationAndFastRock => (new Double3(0, 40.5, 0), Double3.Zero, new Double3(1130, 41, 0), new Double3(-60, 0, 0)),
            Layout.ShipAndFarRock => (new Double3(0, -200, 0), Double3.Zero, new Double3(FarOut, -200.6, 0), Double3.Zero),
            _ => (new Double3(0, -200, 0), new Double3(0, -2, 0), new Double3(1100, -202.6, 0), new Double3(-60, 0, 0)),
        };
        var crateLog = new CollisionLog();
        var rockLog = new CollisionLog();
        Entity crate = world.Spawn(new SpawnDesc
        {
            Frame = frame,
            Pose = new DPose(crateInBubble ?? cratePlace, DQuaternion.Identity),
            LinearVelocity = crateVelocity,
            Body = CrateBody,
            Components = [crateLog],
        });
        Entity rock = world.Spawn(new SpawnDesc
        {
            Frame = space,
            Pose = new DPose(rockPlace + new Double3(0, rockOffset, 0), DQuaternion.Identity),
            LinearVelocity = rockVelocity,
            Body = RockBody,
            Components = layout == Layout.ShipAndFarRock ? [rockLog] : [rockLog, new Thruster(RockBody.Mass)],
        });
        Tick(world, OnsetTicks);
        return new Scene(layout, space, owner, frame, crate, rock, crateLog, rockLog);
    }

    /// <summary>
    /// Checks that with numeric frames the rock is in a numeric frame of space of its own, apart from the ship's hull, and
    /// that the ship's hull falls in one of its own; and that without them the rock is in the system of space.
    /// </summary>
    private static void RockHomeShouldBe(World world, Scene scene, bool numericFrames)
    {
        FrameHandle home = world.PhysicsHomeOf(scene.Rock);
        if (!numericFrames)
        {
            home.Should().Be(scene.Space);
            return;
        }
        world.Frames.Numeric(home).Should().Be(scene.Layout == Layout.ShipAndFarRock ? NumericMode.Resting : NumericMode.Falling,
            "the rock thrusts at 60 m/s, or rests 9,000 km out");
        world.Frames.SemanticOf(home).Should().Be(scene.Space);
        world.PhysicsHomeOf(scene.Owner).Should().NotBe(home, "the merge test does not link the rock to the hull");
        world.Frames.TreeParent(scene.Bubble).Should().NotBe(home);
        if (scene.Layout != Layout.StationAndFastRock)
            world.Frames.Numeric(world.PhysicsHomeOf(scene.Owner)).Should().Be(NumericMode.Falling, "the ship's hull thrusts at 60 m/s");
    }

    /// <summary>
    /// The modes of a body's home after the last two ticks. A contact in the step of a tick becomes a Collision event at
    /// that tick's sync point, and a component hears it in the next tick. That step ran in the home that the sync point of
    /// the tick before it gave the body. So when a component hears a contact at the end of a tick, before the new mode is
    /// pushed, <see cref="OfContactStep"/> is the mode of the home in which the contact happened.
    /// </summary>
    private sealed class HomeModes(NumericMode start)
    {
        private NumericMode last = start;

        /// <summary>The mode after the tick before the last one, or at the start.</summary>
        public NumericMode OfContactStep { get; private set; } = start;

        /// <summary>Records the mode after a tick.</summary>
        public void Push(NumericMode mode)
        {
            OfContactStep = last;
            last = mode;
        }
    }

    /// <summary>
    /// The outcome of a run: the tick of the rock's first Collision with the crate, the rock's place relative to space at the
    /// end of that tick, the mode of the rock's home in the step of the contact, and the rock's state relative to space at
    /// the end.
    /// </summary>
    private sealed record Outcome(long HitTick, Double3 HitPosition, NumericMode HitMode, Double3 Position, Double3 Velocity, List<ContactNews> RockNews,
        List<ContactNews> CrateNews);

    /// <summary>
    /// Builds the scene of <paramref name="layout"/>, runs it for <paramref name="ticks"/> more ticks and returns the
    /// outcome. The world checks its invariants at the end of every tick.
    /// </summary>
    private static Outcome Run(Layout layout, bool numericFrames, int ticks)
    {
        using World world = NewWorld(numericFrames, out _);
        Scene scene = Build(world, layout);
        RockHomeShouldBe(world, scene, numericFrames);

        long hitTick = -1;
        Double3 hitPosition = default;
        NumericMode hitMode = default;
        var modes = new HomeModes(world.Frames.Numeric(world.PhysicsHomeOf(scene.Rock)));
        for (int tick = 0; tick < ticks; tick++)
        {
            world.Tick();
            if (hitTick < 0 && scene.RockLog.CountWith(scene.Crate) > 0)
            {
                hitTick = world.TickNumber;
                hitPosition = world.PoseIn(scene.Rock, scene.Space).Position;
                hitMode = modes.OfContactStep;
            }
            modes.Push(world.Frames.Numeric(world.PhysicsHomeOf(scene.Rock)));
        }

        world.Errors.Should().BeEmpty();
        world.GetFrame(scene.Rock).Should().Be(scene.Space, "the rock never touched the bubble");
        world.GetFrame(scene.Crate).Should().Be(scene.Bubble, "the crate never left the bubble");
        return new Outcome(hitTick, hitPosition, hitMode, world.PoseIn(scene.Rock, scene.Space).Position, world.VelocityIn(scene.Rock, scene.Space),
            scene.RockLog.NewsOf(scene.Crate), scene.CrateLog.NewsOf(scene.Rock));
    }

    [Fact]
    public void AFastRockInANumericFrameOfSpaceHitsACrateThatSticksOutOfAParkedStationAsItDoesWithoutNumericFrames()
    {
        Outcome withNumericFrames = Run(Layout.StationAndFastRock, numericFrames: true, ticks: 150);
        Outcome withoutNumericFrames = Run(Layout.StationAndFastRock, numericFrames: false, ticks: 150);

        // The front of the rock reaches the crate's face at x = 1001 in space after 128.5 m from its spawn, or 2.14 s.
        withoutNumericFrames.HitTick.Should().BeInRange(127, 133, "the rock hits the ghost of the crate in space after about 2.14 s");
        withoutNumericFrames.Velocity.Length.Should().BeLessThan(5, "the ghost of the crate stopped the rock");
        withoutNumericFrames.Position.X.Should().BeGreaterThan(1001, "the rock did not pass through the crate");
        withNumericFrames.HitMode.Should().Be(NumericMode.Falling, "the rock hits the crate in its falling frame");
        withNumericFrames.HitTick.Should().BeInRange(withoutNumericFrames.HitTick - 1, withoutNumericFrames.HitTick + 1,
            "the crate also has a ghost in the rock's numeric frame, whose member may touch a body that sticks out of the station");
        withNumericFrames.Velocity.Length.Should().BeLessThan(5, "the ghost stopped the rock");
        withNumericFrames.Position.X.Should().BeGreaterThan(1001, "the rock did not pass through the crate");
        withNumericFrames.RockNews.Should().Equal(withoutNumericFrames.RockNews);
        withNumericFrames.CrateNews.Should().Equal(withoutNumericFrames.CrateNews);
        withNumericFrames.RockNews.Should().StartWith(ContactNews.Started);
    }

    [Fact]
    public void ACrateThatSticksOutOfAFallingShipPushesARockThatRestsFarOutInANumericFrameOfItsOwnAsItDoesWithoutNumericFrames()
    {
        Outcome withNumericFrames = Run(Layout.ShipAndFarRock, numericFrames: true, ticks: 130);
        Outcome withoutNumericFrames = Run(Layout.ShipAndFarRock, numericFrames: false, ticks: 130);

        // The front of the crate reaches the back of the rock, at x = FarOut + 0.5 in space, after 128.5 m from its spawn, or 2.14 s.
        withoutNumericFrames.HitTick.Should().BeInRange(127, 133, "the ghost of the crate in space reaches the rock after about 2.14 s");
        withoutNumericFrames.Velocity.X.Should().BeLessThan(-10, "the ghost pushed the rock along -X");
        withNumericFrames.HitMode.Should().Be(NumericMode.Resting, "the ghost of the crate hits the rock in its resting frame");
        withNumericFrames.HitTick.Should().Be(withoutNumericFrames.HitTick,
            "the crate also has a ghost in the rock's resting frame, whose member may touch a body that sticks out of the ship");
        (withNumericFrames.HitPosition - withoutNumericFrames.HitPosition).Length.Should().BeLessThan(1e-3);
        (withNumericFrames.Position - withoutNumericFrames.Position).Length.Should().BeLessThan(1e-2, "the ghost pushed the rock to the same place");
        (withNumericFrames.Velocity - withoutNumericFrames.Velocity).Length.Should().BeLessThan(1e-2, "the ghost moves at the same velocity");
        withNumericFrames.RockNews.Should().Equal(withoutNumericFrames.RockNews);
        withNumericFrames.RockNews.Should().StartWith(ContactNews.Started);
    }

    /// <summary>
    /// Without numeric frames, the rock flies at 60 m/s in the system of space, so Jolt sweeps it along its path with a
    /// linear cast. At its first contact with the ghost of the crate, which slides beside it at the same speed, the cast
    /// stops it for most of a step, about 1.1 m along X. In its falling frame the rock is slow and has no cast. So the test
    /// compares the place across the path of the ship, not along it.
    /// </summary>
    [Fact]
    public void ACrateThatDriftsOutOfAFallingShipPushesARockThatFliesBesideItInANumericFrameOfItsOwnAsItDoesWithoutNumericFrames()
    {
        Outcome withNumericFrames = Run(Layout.ShipAndRockBeside, numericFrames: true, ticks: 30);
        Outcome withoutNumericFrames = Run(Layout.ShipAndRockBeside, numericFrames: false, ticks: 30);

        // The crate's face closes the gap of 1.1 m from the spawn at 2 m/s in 0.55 s, or 33 ticks: 3 ticks after the start.
        withoutNumericFrames.HitTick.Should().BeInRange(32, 37, "the ghost of the crate in space reaches the rock after about 0.55 s");
        withoutNumericFrames.Velocity.Y.Should().BeInRange(-2.1, -1.9, "the ghost pushes the rock out along -Y at the crate's speed");
        withNumericFrames.HitMode.Should().Be(NumericMode.Falling, "the ghost of the crate hits the rock in its falling frame");
        withNumericFrames.HitTick.Should().Be(withoutNumericFrames.HitTick,
            "the crate also has a ghost in the rock's falling frame, whose member may touch a body that sticks out of the ship");
        withNumericFrames.Position.Y.Should().BeApproximately(withoutNumericFrames.Position.Y, 0.05, "the ghost pushed the rock out as far");
        withNumericFrames.Position.Z.Should().BeApproximately(withoutNumericFrames.Position.Z, 1e-6);
        (withNumericFrames.Velocity - withoutNumericFrames.Velocity).Length.Should().BeLessThan(1e-2, "the ghost moves at the same velocity");
        withNumericFrames.RockNews.Should().Equal(withoutNumericFrames.RockNews);
        withNumericFrames.RockNews.Should().StartWith(ContactNews.Started);
    }

    /// <summary>
    /// Stands for the list of numeric frames that kept the ghosts of the bodies that stick out of a bubble, which ter-4psc
    /// removed: the merge test now links each body that may touch such a ghost to the bubble's hull. It fails each test that
    /// still reads that list, until the test is rewritten.
    /// </summary>
    private static bool NamesStickOutGhosts(World world, FrameHandle bubble, FrameHandle frame) =>
        throw new NotSupportedException("ter-4psc removed FrameTree.StickOutGhosts; rewrite this test for the places of ghosts.");

    /// <summary>Stands for an empty list of the removed stick-out ghosts (see <see cref="NamesStickOutGhosts"/>).</summary>
    private static bool NoStickOutGhosts(World world) =>
        throw new NotSupportedException("ter-4psc removed FrameTree.StickOutGhosts; rewrite this test for the places of ghosts.");

    /// <summary>A frame with no body, in <paramref name="parent"/>, at <paramref name="pose"/>, with a bubble of <paramref name="radius"/> m.</summary>
    private static FrameHandle EmptyFrame(World world, FrameHandle parent, DPose pose, float radius) =>
        world.GetOwnedFrame(world.Spawn(new SpawnDesc { Frame = parent, Pose = pose, AsFrame = new FrameSpec(FrameBounds.Sphere(radius), MotionDriver.Static) }));

    /// <summary>
    /// The reconcile at the start of a step drops each ghost that the frame tree does not allow. The test runs the
    /// reconcile alone, by applying an empty change batch after a change of the tree far away, and reads the backend before
    /// the next step.
    /// </summary>
    [Theory]
    [InlineData(Layout.StationAndFastRock)]
    [InlineData(Layout.ShipAndFarRock)]
    [InlineData(Layout.ShipAndRockBeside)]
    public void TheGhostOfACrateThatSticksOutInTheNumericFrameOfARockSurvivesAReconcile(Layout layout)
    {
        using World world = NewWorld(numericFrames: true, out FaultInjectingBackend faults);
        Scene scene = Build(world, layout);
        FrameHandle buoy = EmptyFrame(world, scene.Space, At(-50_000), 5);
        FrameHandle depot = EmptyFrame(world, scene.Space, At(50_000), 50);
        Tick(world, 2);
        RockHomeShouldBe(world, scene, numericFrames: true);
        FrameHandle home = world.PhysicsHomeOf(scene.Rock);
        NamesStickOutGhosts(world, scene.Bubble, home).Should().BeTrue("the rock may touch a body that sticks out of the bubble");
        faults.Jolt.HasGhostIn(scene.Crate, home).Should().BeTrue("the crate sticks out of the bubble");

        // A change far away makes the backend reconcile. The bubble is still a semantic child of space.
        world.Frames.Reparent(buoy, depot);
        world.Frames.Update();
        faults.Jolt.ApplyChanges(new PhysicsChanges(), world.Frames);

        faults.Jolt.HasGhostIn(scene.Crate, home).Should().BeTrue(
            "the numeric update names the rock's frame for the bodies that stick out of the bubble, so the reconcile allows the ghost there");
        faults.Jolt.HasGhostIn(scene.Crate, scene.Space).Should().BeTrue();
        world.Tick();
        faults.Jolt.HasGhostIn(scene.Crate, world.PhysicsHomeOf(scene.Rock)).Should().BeTrue();
        world.Errors.Should().BeEmpty();
    }

    /// <summary>
    /// The numeric update names a frame for the bodies that stick out of a bubble only while that frame is a numeric frame
    /// of the bubble's semantic parent. When a change of the tree moves the parked station under a depot far away, the
    /// depot is the station's parent, and the rock's falling frame, a numeric frame of space, is no place for the ghost of
    /// the station's crate. So the tree drops the station's place there at once, before any numeric update, and the
    /// reconcile drops the ghost.
    /// </summary>
    [Fact]
    public void AReconcileDropsTheGhostInTheNumericFrameOfARockWhenTheBubbleMovesToAnotherParent()
    {
        using World world = NewWorld(numericFrames: true, out FaultInjectingBackend faults);
        Scene scene = Build(world, Layout.StationAndFastRock);
        FrameHandle depot = EmptyFrame(world, scene.Space, At(-50_000), 50);
        Tick(world, 2);
        FrameHandle home = world.PhysicsHomeOf(scene.Rock);
        world.Frames.Numeric(home).Should().Be(NumericMode.Falling, "the rock thrusts at 60 m/s");
        NamesStickOutGhosts(world, scene.Bubble, home).Should().BeTrue("the rock may touch the crate that sticks out of the station");
        faults.Jolt.HasGhostIn(scene.Crate, home).Should().BeTrue("the crate sticks out of the station, near the rock's frame");

        world.Frames.Reparent(scene.Bubble, depot);
        world.Frames.Update();
        faults.Jolt.ApplyChanges(new PhysicsChanges(), world.Frames);

        NamesStickOutGhosts(world, scene.Bubble, home).Should().BeFalse("the tree drops the places of a bubble that moves to another semantic parent");
        world.Frames.Invoking(tree => tree.AssertInvariants()).Should().NotThrow();
        faults.Jolt.HasGhostIn(scene.Crate, home).Should().BeFalse("the rock's frame is no numeric frame of the station's new parent");
    }

    /// <summary>
    /// The numeric update names the frames that keep the ghosts of the bodies that stick out of a bubble at the sync point,
    /// and a hand-off in the next step can take the bubble's hull to another semantic frame. Then those frames are no
    /// longer numeric frames of the bubble's semantic parent, and the ghost pass of that step puts no ghost there. The ship
    /// of <see cref="Layout.ShipAndRockBeside"/> flies at 60 m/s along +X from (1870, 0, 0) in space, with a gentle thrust,
    /// into a parked station at (3000, 0, 0) with a bubble of radius 1000 m, which the ship fits in. A crate at (0, -200, 0)
    /// sticks out of the ship. A rock flies beside the ship with its velocity and the same gentle thrust, 203 m from it along
    /// +Y, just outside its bubble. After the <see cref="OnsetTicks"/> ticks of thrust, the ship is at (1900, 0, 0), and the
    /// hull and the rock each fall in a frame of their own, so the update names the rock's frame for the bodies that stick
    /// out of the ship. The ship's hull touches the station's bubble about 0.4 s before the rock does.
    /// </summary>
    [Fact]
    public void AShipThatEntersAStationTakesTheGhostOfItsCrateOutOfTheNumericFrameOfARockInSpace()
    {
        using World world = NewWorld(numericFrames: true, out FaultInjectingBackend faults);
        FrameHandle space = world.CreateRootFrame(new FrameSpec(FrameBounds.Sphere(1e7f), MotionDriver.Static));
        Entity stationOwner = world.Spawn(new SpawnDesc
        {
            Frame = space,
            Pose = At(3000),
            Body = new BodyDesc(new BoxShapeDesc(new Vector3(2)), BodyMotion.Static),
            AsFrame = new FrameSpec(FrameBounds.Sphere(1000), MotionDriver.Static),
        });
        FrameHandle station = world.GetOwnedFrame(stationOwner);
        Entity shipOwner = world.Spawn(new SpawnDesc
        {
            Frame = space,
            Pose = At(1870),
            LinearVelocity = new Double3(60, 0, 0),
            Body = ShipHull,
            AsFrame = new FrameSpec(FrameBounds.Sphere(200), MotionDriver.Physics),
            Components = [new Thruster(ShipHull.Mass)],
        });
        FrameHandle ship = world.GetOwnedFrame(shipOwner);
        Entity crate = world.Spawn(new SpawnDesc { Frame = ship, Pose = At(0, -200), Body = CrateBody });
        Entity rock = world.Spawn(new SpawnDesc
        {
            Frame = space,
            Pose = At(1870, 203),
            LinearVelocity = new Double3(60, 0, 0),
            Body = RockBody,
            Components = [new Thruster(RockBody.Mass)],
        });
        Tick(world, OnsetTicks);
        FrameHandle rockHome = world.PhysicsHomeOf(rock);
        world.Frames.Numeric(rockHome).Should().Be(NumericMode.Falling, "the rock thrusts at 60 m/s");
        world.Frames.Numeric(world.Frames.TreeParent(ship)).Should().Be(NumericMode.Falling, "the ship's hull thrusts at 60 m/s");
        NamesStickOutGhosts(world, ship, rockHome).Should().BeTrue("the rock flies 203 m from the ship's origin");

        int ticks = TickUntil(world, () => world.Frames.Parent(ship) == station, 200);

        ticks.Should().BeInRange(90, 105, "the ship's hull touches the station's bubble after about 1.6 s");
        world.GetFrame(rock).Should().Be(space, "the rock touches the station's bubble later");
        world.PhysicsHomeOf(rock).Should().Be(rockHome);
        faults.Jolt.HasGhostIn(crate, rockHome).Should().BeFalse("the ship is no semantic child of space now, so the rock's frame keeps no ghost of its crate");
        faults.Jolt.HasGhostIn(crate, station).Should().BeTrue("the crate sticks out of the ship into the station");
        world.Errors.Should().BeEmpty();
    }

    /// <summary>
    /// The ghost pass reaches the numeric frames of a bubble's semantic parent by a path of its own, beside the bubble's tree
    /// parent and its semantic parent. No system gets a second ghost of a body, or a ghost beside its real body. With
    /// numeric frames, the rock's numeric frame holds a ghost of the crate at some point; without them, the system of space
    /// does.
    /// </summary>
    [Theory]
    [InlineData(Layout.StationAndFastRock, true)]
    [InlineData(Layout.StationAndFastRock, false)]
    [InlineData(Layout.ShipAndFarRock, true)]
    [InlineData(Layout.ShipAndFarRock, false)]
    [InlineData(Layout.ShipAndRockBeside, true)]
    [InlineData(Layout.ShipAndRockBeside, false)]
    public void NoSystemHoldsTwoBodiesOfOneEntityWhileACrateThatSticksOutMeetsARock(Layout layout, bool numericFrames)
    {
        using World world = NewWorld(numericFrames, out FaultInjectingBackend faults);
        Scene scene = Build(world, layout);
        RockHomeShouldBe(world, scene, numericFrames);
        Entity[] entities = [scene.Owner, scene.Crate, scene.Rock];
        var systems = new List<FrameHandle>();
        bool sawGhostBesideRock = false;
        for (int tick = 0; tick < 120; tick++)
        {
            world.Tick();
            FrameHandle rockHome = world.PhysicsHomeOf(scene.Rock);
            sawGhostBesideRock |= world.Frames.IsNumeric(rockHome) == numericFrames && faults.Jolt.HasGhostIn(scene.Crate, rockHome);
            systems.Clear();
            world.Frames.GetAllInTree(systems);
            foreach (FrameHandle frame in systems)
            {
                List<JoltPhysicsBackend.BodyFacts> bodies = faults.Jolt.BodiesIn(frame);
                foreach (Entity entity in entities)
                    bodies.Count(facts => BodyUserData.Names(facts.UserData, entity)).Should().BeLessThanOrEqualTo(1,
                        $"after tick {tick} the system of {frame} holds the real body of {entity} or one ghost of it, or neither");
            }
        }

        sawGhostBesideRock.Should().BeTrue("the rock's system, a numeric frame with numeric frames, held a ghost of the crate");
        world.Errors.Should().BeEmpty();
    }

    /// <summary>
    /// A crate 100 m inside the edge of the ship's bubble touches no bubble boundary, so it has no ghost, although the
    /// numeric update names the rock's numeric frame for the bodies that stick out of the ship: that frame only keeps the
    /// ghosts of the bodies that stick out.
    /// </summary>
    [Theory]
    [InlineData(Layout.StationAndFastRock, true)]
    [InlineData(Layout.StationAndFastRock, false)]
    [InlineData(Layout.ShipAndFarRock, true)]
    [InlineData(Layout.ShipAndFarRock, false)]
    [InlineData(Layout.ShipAndRockBeside, true)]
    [InlineData(Layout.ShipAndRockBeside, false)]
    public void ACrateFarFromEveryBubbleBoundaryHasNoGhostInTheNumericFrameOfARockNearItsBubble(Layout layout, bool numericFrames)
    {
        using World world = NewWorld(numericFrames, out FaultInjectingBackend faults);
        Double3 deepInside = layout == Layout.StationAndFastRock ? new Double3(0, 20, 0) : new Double3(0, -100, 0);
        Scene scene = Build(world, layout, crateInBubble: deepInside);
        world.Tick();
        RockHomeShouldBe(world, scene, numericFrames);
        if (numericFrames)
            NamesStickOutGhosts(world, scene.Bubble, world.PhysicsHomeOf(scene.Rock)).Should().BeTrue("the rock nears the bubble");

        for (int tick = 0; tick < 60; tick++)
        {
            world.Tick();
            faults.Jolt.GhostCount(scene.Crate).Should().Be(0, $"after tick {tick} the crate is far inside its frame's bubble");
        }
        world.Errors.Should().BeEmpty();
    }

    /// <summary>
    /// A rock 1 km below the path of the ship of <see cref="Layout.ShipAndFarRock"/> rests in a numeric frame of its own,
    /// but stays beyond the reach of the bodies that stick out of the ship, so its frame keeps no ghost of the crate. The
    /// crate still has its ghosts in the ship's tree parent and in space, the ship's semantic parent, which are one frame
    /// without numeric frames.
    /// </summary>
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void ACrateThatSticksOutOfAShipHasNoGhostInTheNumericFrameOfARockFarFromTheShip(bool numericFrames)
    {
        using World world = NewWorld(numericFrames, out FaultInjectingBackend faults);
        Scene scene = Build(world, Layout.ShipAndFarRock, rockOffset: -800);
        world.Tick();
        RockHomeShouldBe(world, scene, numericFrames);

        for (int tick = 0; tick < 120; tick++)
        {
            world.Tick();
            NoStickOutGhosts(world).Should().BeTrue($"after tick {tick} the rock is 800 m beyond the reach of the bodies that stick out of the ship");
            faults.Jolt.GhostCount(scene.Crate).Should().Be(numericFrames ? 2 : 1,
                "the crate has a ghost in the ship's tree parent and one in space, which are one frame without numeric frames");
            faults.Jolt.HasGhostIn(scene.Crate, scene.Space).Should().BeTrue();
            faults.Jolt.HasGhostIn(scene.Crate, world.Frames.TreeParent(scene.Bubble)).Should().BeTrue();
        }
        world.VelocityIn(scene.Rock, scene.Space).Length.Should().BeLessThan(1e-6, "nothing touched the rock");
        world.Errors.Should().BeEmpty();
    }

    // ---- Frames that their drivers move, sensors and hulls ----------------------------------------------------------

    /// <summary>The G·M of the earth, in m³/s².</summary>
    private const double EarthMu = 3.986004418e14;

    /// <summary>
    /// The outcome of a run of a station on an orbit: the tick of the rock's first Collision with the crate, the mode of the
    /// rock's home in the step of the contact, and the rock's place in the station at the end.
    /// </summary>
    private sealed record OrbitOutcome(long HitTick, NumericMode HitMode, Double3 RockInStation, List<ContactNews> RockNews);

    /// <summary>
    /// A planet whose point gravity has the G·M <paramref name="mu"/>, and a station on a circular orbit 7,000 km from its
    /// center, which the Kepler driver moves: a hull of 4 m by 4 m by 4 m and a bubble of radius 40 m, so it hangs directly
    /// in the planet. A crate floats at (0, 40.5, 0) in the station and sticks 1.5 m out of its bubble. A rock co-orbits
    /// beside the station, from (3, 41, 0) in the station's place, and drifts toward the crate at 1 m/s relative to the
    /// station. The rock never touches the bubble, so only the ghost of the crate can stop it. At the earth's G·M the
    /// station orbits at 7,546 m/s, so in one tick of 1/60 s it moves 126 m, more than the 47.7 m that the test of the
    /// bodies that stick out reaches: the rock's 0.5 m, the 45.2 m of the bodies that stick out of the station, and 2 s at
    /// 1 m/s. When <paramref name="rockThrusts"/> is true, the rock thrusts gently (see <see cref="Thruster"/>), so that it
    /// falls in a numeric frame from 50 m/s after the first <see cref="OnsetTicks"/> ticks, 1 m before the crate.
    /// </summary>
    private static OrbitOutcome RunStationOnAnOrbit(bool numericFrames, double mu, bool rockThrusts = false)
    {
        using World world = NewWorld(numericFrames, out _);
        FrameHandle planet = world.CreateRootFrame(new FrameSpec(FrameBounds.Sphere(1e9f), MotionDriver.Static, GravityField.Point(mu)));
        Entity owner = world.Spawn(new SpawnDesc
        {
            Frame = planet,
            Body = ShipHull,
            AsFrame = new FrameSpec(FrameBounds.Sphere(40), MotionDriver.Kepler, Orbit: KeplerOrbit.Circular(7_000_000)),
        });
        FrameHandle station = world.GetOwnedFrame(owner);
        Double3 stationPlace = world.Frames.LocalPose(station).Position;
        Double3 stationVelocity = world.Frames.LinearVelocity(station);
        Entity crate = world.Spawn(new SpawnDesc { Frame = station, Pose = At(0, 40.5), Body = CrateBody });
        var rockLog = new CollisionLog();
        Entity rock = world.Spawn(new SpawnDesc
        {
            Frame = planet,
            Pose = new DPose(stationPlace + new Double3(3, 41, 0), DQuaternion.Identity),
            LinearVelocity = stationVelocity + new Double3(-1, 0, 0),
            Body = RockBody,
            Components = rockThrusts ? [rockLog, new Thruster(RockBody.Mass)] : [rockLog],
        });

        long hitTick = -1;
        NumericMode hitMode = default;
        var modes = new HomeModes(NumericMode.None);
        for (int tick = 0; tick < 150; tick++)
        {
            world.Tick();
            if (hitTick < 0 && rockLog.CountWith(crate) > 0)
            {
                hitTick = world.TickNumber;
                hitMode = modes.OfContactStep;
            }
            modes.Push(world.Frames.Numeric(world.PhysicsHomeOf(rock)));
        }
        world.Errors.Should().BeEmpty();
        world.GetFrame(rock).Should().Be(planet, "the rock never touched the bubble");
        return new OrbitOutcome(hitTick, hitMode, world.PoseIn(rock, station).Position, rockLog.NewsOf(crate));
    }

    [Fact]
    public void ARockThatCoOrbitsWithAStationOnRailsHitsACrateThatSticksOutOfItAsItDoesWithoutNumericFrames()
    {
        OrbitOutcome withNumericFrames = RunStationOnAnOrbit(numericFrames: true, EarthMu);
        OrbitOutcome withoutNumericFrames = RunStationOnAnOrbit(numericFrames: false, EarthMu);

        // The rock's face closes the gap of 1.5 m to the crate's face at 1 m/s in 1.5 s, or 90 ticks.
        withoutNumericFrames.HitTick.Should().BeInRange(87, 94, "the ghost of the crate in the planet's system stops the rock");
        withoutNumericFrames.RockInStation.X.Should().BeGreaterThan(1.4, "the rock did not pass through the crate");
        withNumericFrames.HitMode.Should().Be(NumericMode.Falling, "a free body at 7,546 m/s falls in a numeric frame");
        withNumericFrames.HitTick.Should().BeInRange(withoutNumericFrames.HitTick - 1, withoutNumericFrames.HitTick + 1,
            "the update tests the rock against the station's bubble at the end of the tick, where the step put the rock");
        withNumericFrames.RockInStation.X.Should().BeApproximately(withoutNumericFrames.RockInStation.X, 0.05);
        withNumericFrames.RockNews.Should().Equal(withoutNumericFrames.RockNews);
        withNumericFrames.RockNews.Should().StartWith(ContactNews.Started);
    }

    /// <summary>
    /// The same scene with a G·M at which the station orbits at 60 m/s, so it moves only 1 m in a tick. It shows that the
    /// scene works when the place of the bubble hardly changes over a tick. A free rock at 60 m/s falls in no numeric
    /// frame, so the rock thrusts gently, with numeric frames and without.
    /// </summary>
    [Fact]
    public void ARockThatCoOrbitsWithASlowStationOnRailsHitsACrateThatSticksOutOfItAsItDoesWithoutNumericFrames()
    {
        const double slowMu = 2.52e10;
        OrbitOutcome withNumericFrames = RunStationOnAnOrbit(numericFrames: true, slowMu, rockThrusts: true);
        OrbitOutcome withoutNumericFrames = RunStationOnAnOrbit(numericFrames: false, slowMu, rockThrusts: true);

        withoutNumericFrames.HitTick.Should().BeInRange(87, 94);
        withNumericFrames.HitMode.Should().Be(NumericMode.Falling, "the rock thrusts at 60 m/s");
        withNumericFrames.HitTick.Should().BeInRange(withoutNumericFrames.HitTick - 1, withoutNumericFrames.HitTick + 1);
        withNumericFrames.RockInStation.X.Should().BeApproximately(withoutNumericFrames.RockInStation.X, 0.05);
        withNumericFrames.RockNews.Should().Equal(withoutNumericFrames.RockNews);
    }

    /// <summary>
    /// A parked station <see cref="FarOut"/>, 9,000 km, out in space, a static hull with a bubble of radius 40 m. A crate at
    /// (0, 39, 0) in it drifts out along +Y at 1 m/s, so it sticks out of the bubble. A static sensor ball of radius 1 m
    /// stands in space, 3.5 m outside the bubble and 43.5 m from the hull, beyond the merge test's reach of the hull. A
    /// static sensor meets a kinematic body, such as the ghost of the crate, so without numeric frames it detects the crate
    /// in the system of space. 9,000 km out the sensor rests in a numeric frame of its own, so that frame needs the ghost of
    /// the crate too. The sensor never moves, so the mode of its home at the end is its mode throughout.
    /// </summary>
    private static (List<ContactNews> SensorNews, FrameHandle SensorHome, NumericMode SensorMode, FrameHandle Space) RunStaticSensorBesideAStation(
        bool numericFrames)
    {
        using World world = NewWorld(numericFrames, out _);
        FrameHandle space = world.CreateRootFrame(new FrameSpec(FrameBounds.Sphere(1e7f), MotionDriver.Static));
        Entity station = world.Spawn(new SpawnDesc
        {
            Frame = space,
            Pose = At(FarOut),
            Body = new BodyDesc(new BoxShapeDesc(new Vector3(2)), BodyMotion.Static),
            AsFrame = new FrameSpec(FrameBounds.Sphere(40), MotionDriver.Static),
        });
        FrameHandle bubble = world.GetOwnedFrame(station);
        Entity crate = world.Spawn(new SpawnDesc { Frame = bubble, Pose = At(0, 39), LinearVelocity = new Double3(0, 1, 0), Body = CrateBody });
        var sensorLog = new CollisionLog();
        Entity sensor = world.Spawn(new SpawnDesc
        {
            Frame = space,
            Pose = At(FarOut, 43.5),
            Body = new BodyDesc(new SphereShapeDesc(1f), BodyMotion.Static) { IsSensor = true },
            Components = [sensorLog],
        });
        Tick(world, 200);
        world.Errors.Should().BeEmpty();
        world.GetFrame(crate).Should().Be(bubble, "the crate still sticks out of the station's bubble");
        FrameHandle sensorHome = world.PhysicsHomeOf(sensor);
        return (sensorLog.NewsOf(crate), sensorHome, world.Frames.Numeric(sensorHome), space);
    }

    [Fact]
    public void AStaticSensorFarOutDetectsACrateThatSticksOutOfAParkedStationAsItDoesWithoutNumericFrames()
    {
        var with = RunStaticSensorBesideAStation(numericFrames: true);
        var without = RunStaticSensorBesideAStation(numericFrames: false);

        without.SensorHome.Should().Be(without.Space);
        without.SensorNews.Should().StartWith(ContactNews.Started);
        with.SensorHome.Should().NotBe(with.Space);
        with.SensorMode.Should().Be(NumericMode.Resting, "the static sensor is 9,000 km out, so it rests in a numeric frame of its own");
        with.SensorNews.Should().Equal(without.SensorNews, "numeric frames do not change which events gameplay hears");
    }

    /// <summary>
    /// A parked station <see cref="FarOut"/>, 9,000 km, out in space, a static hull with a bubble of radius 40 m, holds a
    /// radar frame that the Spin driver turns, at (0, 39, 0). The radar's hull is a ball of radius 5 m whose description is
    /// static, which a hull may be: physics gives the hull of a Spin frame the kinematic motion of its driver. So it reaches
    /// 44 m from the station's origin and sticks out of the bubble, and its kinematic ghost in space stops a rock that
    /// drifts at it at 0.3 m/s from 0.05 m above it, outside the bubble. The rock rests in a numeric frame of its own 9,000
    /// km out, so that frame needs the ghost of the radar's hull too. The rock stays slow, so it rests throughout, and the
    /// mode of its home at the end is its mode at the contact.
    /// </summary>
    private static (List<ContactNews> RockNews, Double3 RockPosition, Double3 RockVelocity, FrameHandle RockHome, NumericMode RockMode, FrameHandle Space)
        RunRadarAndRock(bool numericFrames)
    {
        using World world = NewWorld(numericFrames, out _);
        FrameHandle space = world.CreateRootFrame(new FrameSpec(FrameBounds.Sphere(1e7f), MotionDriver.Static));
        Entity station = world.Spawn(new SpawnDesc
        {
            Frame = space,
            Pose = At(FarOut),
            Body = new BodyDesc(new BoxShapeDesc(new Vector3(2)), BodyMotion.Static),
            AsFrame = new FrameSpec(FrameBounds.Sphere(40), MotionDriver.Static),
        });
        Entity radar = world.Spawn(new SpawnDesc
        {
            Frame = world.GetOwnedFrame(station),
            Pose = At(0, 39),
            Body = new BodyDesc(new SphereShapeDesc(5f), BodyMotion.Static),
            AsFrame = new FrameSpec(FrameBounds.Sphere(6), MotionDriver.Spin, Spin: new FrameSpin(DQuaternion.Identity, 0.1)),
        });
        var rockLog = new CollisionLog();
        Entity rock = world.Spawn(new SpawnDesc
        {
            Frame = space,
            Pose = At(FarOut, 44.55),
            LinearVelocity = new Double3(0, -0.3, 0),
            Body = RockBody,
            Components = [rockLog],
        });
        Tick(world, 120);
        world.Errors.Should().BeEmpty();
        world.GetFrame(rock).Should().Be(space, "the rock never touched the station's bubble");
        FrameHandle rockHome = world.PhysicsHomeOf(rock);
        return (rockLog.NewsOf(radar), world.PoseIn(rock, space).Position, world.VelocityIn(rock, space), rockHome, world.Frames.Numeric(rockHome), space);
    }

    [Fact]
    public void ARockFarOutHitsTheHullOfASpinningRadarThatSticksOutOfAParkedStationAsItDoesWithoutNumericFrames()
    {
        var with = RunRadarAndRock(numericFrames: true);
        var without = RunRadarAndRock(numericFrames: false);

        without.RockHome.Should().Be(without.Space);
        without.RockNews.Should().StartWith(ContactNews.Started);
        without.RockPosition.Y.Should().BeGreaterThan(44.4, "the ghost of the radar's hull stopped the rock");
        with.RockHome.Should().NotBe(with.Space);
        with.RockMode.Should().Be(NumericMode.Resting, "the rock is slow and 9,000 km out, so it rests in a numeric frame of its own");
        with.RockNews.Should().Equal(without.RockNews, "numeric frames do not change which events gameplay hears");
        (with.RockPosition - without.RockPosition).Length.Should().BeLessThan(1e-2);
        (with.RockVelocity - without.RockVelocity).Length.Should().BeLessThan(1e-2);
    }
}
