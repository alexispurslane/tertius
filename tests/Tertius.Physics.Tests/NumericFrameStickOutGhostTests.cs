using System.Numerics;
using Tertius.Simulation;
using static Tertius.Testing.WorldTesting;

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
/// frame options, under which a body that flies at 50 m/s or more relative to its frame gets a falling numeric frame, and
/// a slow body 10 km or more from its frame's origin gets a resting one, and with no numeric frames.
/// </summary>
public class NumericFrameStickOutGhostTests
{
    /// <summary>Membership rules under which no group of bodies ever gets a numeric frame.</summary>
    private static readonly NumericFrameOptions WithoutNumericFrames = new()
    {
        CreateSpeed = double.PositiveInfinity,
        LeaveSpeed = double.PositiveInfinity,
        CreateDistance = double.PositiveInfinity,
        LeaveDistance = double.PositiveInfinity,
    };

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

    /// <summary>The scenes of these tests.</summary>
    public enum Layout
    {
        /// <summary>
        /// Space, and at (1000, 0, 0) in it a parked station: a frame that physics does not drive, with a bubble of radius
        /// 40 m around a static hull of 4 m by 4 m by 4 m at its center. The station hangs directly in space, with numeric
        /// frames or without. A crate floats at (0, 40.5, 0) in the station and sticks 1.5 m out of its bubble. A rock flies
        /// at 60 m/s along -X from (1100, 41, 0) in space, so it falls in a numeric frame of its own, through the part of
        /// the crate that sticks out. The rock's center stays at least 1 m outside the station's bubble, so it never touches
        /// the bubble and never meets the real crate.
        /// </summary>
        StationAndFastRock,

        /// <summary>
        /// Space, and at (20100, 0, 0) in it a ship that flies at 60 m/s along -X: a hull of 4 m by 4 m by 4 m and 1000 kg
        /// that physics drives, with a bubble of radius 200 m. Its hull falls in a numeric frame of its own, under which the
        /// ship's frame hangs. A crate floats at (0, -200, 0) in the ship, on the edge of its bubble, and so sticks out of
        /// it. A rock rests at (20000, -200.6, 0) in space, in the path of the part of the crate that sticks out, 0.1 m
        /// outside the ship's bubble. It is slow and 20 km from the origin of space, so it rests in a numeric frame of its
        /// own. The merge test reaches the bounding spheres of the hull and the rock plus 2 s of their relative speed, about
        /// 124 m, and the rock stays at least 200 m from the hull, so the merge test never links them.
        /// </summary>
        ShipAndFarRock,

        /// <summary>
        /// Space, and at (1100, 0, 0) in it the ship of <see cref="ShipAndFarRock"/>, which flies at 60 m/s along -X in a
        /// numeric frame of its own. The crate at (0, -200, 0) in the ship drifts out of its bubble along -Y at 2 m/s. A rock
        /// flies beside the ship with the ship's velocity, from (1100, -201.6, 0) in space, 0.1 m below the crate's face, so
        /// it falls in a numeric frame of its own. The two move together, so the merge test reaches only the bounding
        /// spheres of the hull and the rock, about 4 m, and never links them.
        /// </summary>
        ShipAndRockBeside,
    }

    /// <summary>
    /// A scene: <see cref="Bubble"/> is the frame that the crate sticks out of and <see cref="Owner"/> its owner, and
    /// <see cref="CrateLog"/> and <see cref="RockLog"/> hear the collisions of the crate and of the rock.
    /// </summary>
    private sealed record Scene(FrameHandle Space, Entity Owner, FrameHandle Bubble, Entity Crate, Entity Rock, CollisionLog CrateLog,
        CollisionLog RockLog);

    /// <summary>
    /// Builds the scene of <paramref name="layout"/>. The crate floats at <paramref name="crateInBubble"/> in its frame
    /// instead, when it is given. The rock is <paramref name="rockOffset"/> farther along +Y from where the layout puts it.
    /// </summary>
    private static Scene Build(World world, Layout layout, Double3? crateInBubble = null, double rockOffset = 0)
    {
        FrameHandle space = world.CreateRootFrame(new FrameSpec(FrameBounds.Sphere(1e7f), MotionDriver.Static));
        (DPose ownerPose, Double3 ownerVelocity, BodyDesc hull, FrameSpec bubble) = layout switch
        {
            Layout.StationAndFastRock => (At(1000), Double3.Zero, new BodyDesc(new BoxShapeDesc(new Vector3(2)), BodyMotion.Static),
                new FrameSpec(FrameBounds.Sphere(40), MotionDriver.Static)),
            Layout.ShipAndFarRock => (At(20_100), new Double3(-60, 0, 0), ShipHull, new FrameSpec(FrameBounds.Sphere(200), MotionDriver.Physics)),
            _ => (At(1100), new Double3(-60, 0, 0), ShipHull, new FrameSpec(FrameBounds.Sphere(200), MotionDriver.Physics)),
        };
        Entity owner = world.Spawn(new SpawnDesc { Frame = space, Pose = ownerPose, LinearVelocity = ownerVelocity, Body = hull, AsFrame = bubble });
        FrameHandle frame = world.GetOwnedFrame(owner);
        (Double3 cratePlace, Double3 crateVelocity, Double3 rockPlace, Double3 rockVelocity) = layout switch
        {
            Layout.StationAndFastRock => (new Double3(0, 40.5, 0), Double3.Zero, new Double3(1100, 41, 0), new Double3(-60, 0, 0)),
            Layout.ShipAndFarRock => (new Double3(0, -200, 0), Double3.Zero, new Double3(20_000, -200.6, 0), Double3.Zero),
            _ => (new Double3(0, -200, 0), new Double3(0, -2, 0), new Double3(1100, -201.6, 0), new Double3(-60, 0, 0)),
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
            Components = [rockLog],
        });
        return new Scene(space, owner, frame, crate, rock, crateLog, rockLog);
    }

    /// <summary>
    /// Checks that with numeric frames the rock is in a numeric frame of space of its own, apart from the ship's hull, and
    /// that without them it is in the system of space.
    /// </summary>
    private static void RockHomeShouldBe(World world, Scene scene, bool numericFrames)
    {
        FrameHandle home = world.PhysicsHomeOf(scene.Rock);
        if (!numericFrames)
        {
            home.Should().Be(scene.Space);
            return;
        }
        world.Frames.IsNumeric(home).Should().BeTrue("the rock is fast, or slow and far out");
        world.Frames.SemanticOf(home).Should().Be(scene.Space);
        world.PhysicsHomeOf(scene.Owner).Should().NotBe(home, "the merge test does not link the rock to the hull");
        world.Frames.TreeParent(scene.Bubble).Should().NotBe(home);
    }

    /// <summary>The outcome of a run: the tick of the rock's first Collision with the crate, and the rock's state relative to space then and at the end.</summary>
    private sealed record Outcome(long HitTick, Double3 HitPosition, Double3 Position, Double3 Velocity, List<ContactNews> RockNews, List<ContactNews> CrateNews);

    /// <summary>
    /// Runs the scene of <paramref name="layout"/> for <paramref name="ticks"/> ticks and returns the outcome. The world
    /// checks its invariants at the end of every tick.
    /// </summary>
    private static Outcome Run(Layout layout, bool numericFrames, int ticks)
    {
        using World world = NewWorld(numericFrames, out _);
        Scene scene = Build(world, layout);
        world.Tick();
        RockHomeShouldBe(world, scene, numericFrames);

        long hitTick = -1;
        Double3 hitPosition = default;
        for (int tick = 0; tick < ticks; tick++)
        {
            world.Tick();
            if (hitTick < 0 && scene.RockLog.CountWith(scene.Crate) > 0)
            {
                hitTick = world.TickNumber;
                hitPosition = world.PoseIn(scene.Rock, scene.Space).Position;
            }
        }

        world.Errors.Should().BeEmpty();
        world.GetFrame(scene.Rock).Should().Be(scene.Space, "the rock never touched the bubble");
        world.GetFrame(scene.Crate).Should().Be(scene.Bubble, "the crate never left the bubble");
        return new Outcome(hitTick, hitPosition, world.PoseIn(scene.Rock, scene.Space).Position, world.VelocityIn(scene.Rock, scene.Space),
            scene.RockLog.NewsOf(scene.Crate), scene.CrateLog.NewsOf(scene.Rock));
    }

    [Fact]
    public void AFastRockInANumericFrameOfSpaceHitsACrateThatSticksOutOfAParkedStationAsItDoesWithoutNumericFrames()
    {
        Outcome withNumericFrames = Run(Layout.StationAndFastRock, numericFrames: true, ticks: 150);
        Outcome withoutNumericFrames = Run(Layout.StationAndFastRock, numericFrames: false, ticks: 150);

        // The front of the rock reaches the crate's face at x = 1001 in space after 98.5 m, or 1.64 s.
        withoutNumericFrames.HitTick.Should().BeInRange(97, 103, "the rock hits the ghost of the crate in space after about 1.64 s");
        withoutNumericFrames.Velocity.Length.Should().BeLessThan(5, "the ghost of the crate stopped the rock");
        withoutNumericFrames.Position.X.Should().BeGreaterThan(1001, "the rock did not pass through the crate");
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

        // The front of the crate reaches the back of the rock, at x = 20000.5 in space, after 98.5 m, or 1.64 s.
        withoutNumericFrames.HitTick.Should().BeInRange(97, 103, "the ghost of the crate in space reaches the rock after about 1.64 s");
        withoutNumericFrames.Velocity.X.Should().BeLessThan(-10, "the ghost pushed the rock along -X");
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

        // The crate's face closes the gap of 0.1 m at 2 m/s in 0.05 s, or 3 ticks.
        withoutNumericFrames.HitTick.Should().BeInRange(2, 7, "the ghost of the crate in space reaches the rock after about 0.05 s");
        withoutNumericFrames.Velocity.Y.Should().BeInRange(-2.1, -1.9, "the ghost pushes the rock out along -Y at the crate's speed");
        withNumericFrames.HitTick.Should().Be(withoutNumericFrames.HitTick,
            "the crate also has a ghost in the rock's falling frame, whose member may touch a body that sticks out of the ship");
        withNumericFrames.Position.Y.Should().BeApproximately(withoutNumericFrames.Position.Y, 0.05, "the ghost pushed the rock out as far");
        withNumericFrames.Position.Z.Should().BeApproximately(withoutNumericFrames.Position.Z, 1e-6);
        (withNumericFrames.Velocity - withoutNumericFrames.Velocity).Length.Should().BeLessThan(1e-2, "the ghost moves at the same velocity");
        withNumericFrames.RockNews.Should().Equal(withoutNumericFrames.RockNews);
        withNumericFrames.RockNews.Should().StartWith(ContactNews.Started);
    }

    /// <summary>True when the last numeric update named the frame <paramref name="frame"/> to keep the ghosts of the bodies that stick out of <paramref name="bubble"/>.</summary>
    private static bool NamesStickOutGhosts(World world, FrameHandle bubble, FrameHandle frame)
    {
        foreach (StickOutGhost ghost in world.Frames.StickOutGhosts)
            if (ghost.Bubble == bubble && ghost.Frame == frame)
                return true;
        return false;
    }

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
    /// The reconcile allows the ghost of a body that sticks out of a bubble in a numeric frame that the numeric update named
    /// for the bubble only while that frame is a numeric frame of the bubble's semantic parent. When a change of the tree
    /// moves the parked station under a depot far away, the depot is the station's parent, and the rock's falling frame, a
    /// numeric frame of space, is no place for the ghost of the station's crate, although the last update named it.
    /// </summary>
    [Fact]
    public void AReconcileDropsTheGhostInTheNumericFrameOfARockWhenTheBubbleMovesToAnotherParent()
    {
        using World world = NewWorld(numericFrames: true, out FaultInjectingBackend faults);
        Scene scene = Build(world, Layout.StationAndFastRock);
        FrameHandle depot = EmptyFrame(world, scene.Space, At(-50_000), 50);
        Tick(world, 2);
        FrameHandle home = world.PhysicsHomeOf(scene.Rock);
        faults.Jolt.HasGhostIn(scene.Crate, home).Should().BeTrue("the crate sticks out of the station, near the rock's frame");

        world.Frames.Reparent(scene.Bubble, depot);
        world.Frames.Update();
        faults.Jolt.ApplyChanges(new PhysicsChanges(), world.Frames);

        NamesStickOutGhosts(world, scene.Bubble, home).Should().BeTrue("no numeric update ran since the change");
        faults.Jolt.HasGhostIn(scene.Crate, home).Should().BeFalse("the rock's frame is no numeric frame of the station's new parent");
    }

    /// <summary>
    /// The numeric update names the frames that keep the ghosts of the bodies that stick out of a bubble at the sync point,
    /// and a hand-off in the next step can take the bubble's hull to another semantic frame. Then those frames are no
    /// longer numeric frames of the bubble's semantic parent, and the ghost pass of that step puts no ghost there. The ship
    /// of <see cref="Layout.ShipAndRockBeside"/> flies at 60 m/s along +X from (1900, 0, 0) in space into a parked station
    /// at (3000, 0, 0) with a bubble of radius 1000 m, which the ship fits in. A crate at (0, -200, 0) sticks out of the
    /// ship. A rock flies beside the ship with its velocity, 203 m from it along +Y, just outside its bubble, in a falling
    /// frame of its own, so the update names that frame for the bodies that stick out of the ship. The ship's hull touches
    /// the station's bubble about 0.4 s before the rock does.
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
            Pose = At(1900),
            LinearVelocity = new Double3(60, 0, 0),
            Body = ShipHull,
            AsFrame = new FrameSpec(FrameBounds.Sphere(200), MotionDriver.Physics),
        });
        FrameHandle ship = world.GetOwnedFrame(shipOwner);
        Entity crate = world.Spawn(new SpawnDesc { Frame = ship, Pose = At(0, -200), Body = CrateBody });
        Entity rock = world.Spawn(new SpawnDesc { Frame = space, Pose = At(1900, 203), LinearVelocity = new Double3(60, 0, 0), Body = RockBody });
        world.Tick();
        FrameHandle rockHome = world.PhysicsHomeOf(rock);
        world.Frames.Numeric(rockHome).Should().Be(NumericMode.Falling);
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
    /// parent and its semantic parent. No system gets a second ghost of a body, or a ghost beside its real body.
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
        Entity[] entities = [scene.Owner, scene.Crate, scene.Rock];
        var systems = new List<FrameHandle>();
        bool sawGhostBesideRock = false;
        for (int tick = 0; tick < 120; tick++)
        {
            world.Tick();
            sawGhostBesideRock |= faults.Jolt.HasGhostIn(scene.Crate, world.PhysicsHomeOf(scene.Rock));
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

        sawGhostBesideRock.Should().BeTrue("the rock's system held a ghost of the crate");
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
            world.Frames.StickOutGhosts.IsEmpty.Should().BeTrue($"after tick {tick} the rock is 800 m beyond the reach of the bodies that stick out of the ship");
            faults.Jolt.GhostCount(scene.Crate).Should().Be(numericFrames ? 2 : 1,
                "the crate has a ghost in the ship's tree parent and one in space, which are one frame without numeric frames");
            faults.Jolt.HasGhostIn(scene.Crate, scene.Space).Should().BeTrue();
            faults.Jolt.HasGhostIn(scene.Crate, world.Frames.TreeParent(scene.Bubble)).Should().BeTrue();
        }
        world.VelocityIn(scene.Rock, scene.Space).Length.Should().BeLessThan(1e-6, "nothing touched the rock");
        world.Errors.Should().BeEmpty();
    }
}
