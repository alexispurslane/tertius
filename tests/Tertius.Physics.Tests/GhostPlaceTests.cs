using Tertius.Frames.Rails;
using Tertius.Simulation;
using static Tertius.Testing.WorldTesting;
using static Tertius.Testing.NumericFrameTesting;

namespace Tertius.Physics.Tests;

/// <summary>
/// A ghost is a kinematic copy of a body in the physics system of another frame. No body of a group can move a ghost, so
/// the merge test counts the place of each ghost as infinitely heavy, as it counts an anchor (ticket ter-4psc,
/// docs/plans/numeric-frames.md, "Bubbles and ghosts"). A body that may touch the ghost of a body that sticks out of a
/// semantic child joins the group of the child's hull, because the ghost lives in the home of that hull. A body that may
/// touch a ghost that is fixed in its semantic frame's own system stays in that system. So a body meets every ghost in
/// the system where it lives, and gameplay hears the same contacts as without numeric frames. These tests run the World on
/// the real backend, with the default numeric frame options, and with no numeric frames. Under the defaults, a group of
/// free bodies falls in a numeric frame from 1,000 m/s relative to its semantic frame, and a group with a body that
/// thrusts falls from 50 m/s. So the bodies here that fly at 60 m/s and must fall thrust gently (see
/// <see cref="Thruster"/>), from 0.5 s before their falling frames matter. A slow group rests in a numeric frame from
/// 2^23 m (8,389 km) from its semantic frame's origin, so the scenes whose bodies must rest lie 2^24 m out.
/// </summary>
public class GhostPlaceTests
{
    /// <summary>
    /// The 30 ticks of 1/60 s, 0.5 s, of thrust after which a body with a <see cref="Thruster"/> thrusts (see
    /// <see cref="NumericFrameOptions.ThrustOnset"/>). From the sync point of the last of them on, a group with that body
    /// falls in a numeric frame from 50 m/s.
    /// </summary>
    private const int OnsetTicks = 30;

    // ---- Anchors that move differently --------------------------------------------------------------------------------

    /// <summary>
    /// The G·M of the root frame of <see cref="FarStation"/>, in m³/s². A circular orbit of <see cref="Far"/> in it moves at
    /// 2.44 m/s.
    /// </summary>
    private const double FarMu = 1e8;

    /// <summary>
    /// 2^24 m (16,777 km): far enough from the origin for a slow group to rest in a numeric frame, and for the hull of a
    /// Kepler frame to live in a following frame of its own, from 2^23 m (see <see cref="NumericFrameOptions.CreateDistance"/>
    /// ).
    /// </summary>
    private const double Far = 1 << 24;

    /// <summary>A hull of 4 m by 4 m by 4 m and 1000 kg that nothing slows down and that does not bounce.</summary>
    private static readonly BodyDesc ShipHull = new(new BoxShapeDesc(new(2)), Mass: 1000, Restitution: 0,
        LinearDamping: 0, AngularDamping: 0);

    /// <summary>A static hull of 4 m by 4 m by 4 m, for a frame that physics does not drive.</summary>
    private static readonly BodyDesc StaticHull = new(new BoxShapeDesc(new(2)), BodyMotion.Static);

    /// <summary>A crate of 2 m by 2 m by 2 m and 2 kg that nothing slows down and that does not bounce.</summary>
    private static readonly BodyDesc CrateBody = new(new BoxShapeDesc(new(1)), Mass: 2, Restitution: 0,
        LinearDamping: 0, AngularDamping: 0);

    /// <summary>
    /// A rock: a ball of radius 0.5 m and 1 kg that nothing slows down and that does not bounce. A ghost is kinematic, and
    /// Jolt makes no contact between a kinematic body and a static one, so the rock is dynamic.
    /// </summary>
    private static readonly BodyDesc RockBody = new(new SphereShapeDesc(0.5f), Mass: 1, Restitution: 0,
        LinearDamping: 0, AngularDamping: 0);

    /// <summary>A world on the wrapper with the default numeric frames or, when <paramref name="numericFrames"/> is false, with none.</summary>
    private static World NewWorld(bool numericFrames, out FaultInjectingBackend faults) =>
        NewWorldWithFaults(out faults,
            configure: numericFrames ? null : options => options.NumericFrames = WithoutNumericFrames);

    /// <summary>A root frame without gravity and with a bubble of radius 10,000 km.</summary>
    private static FrameHandle Space(World world) => world.CreateRootFrame(new(FrameBounds.Sphere(1e7f)));

    /// <summary>
    /// The outcome of a run: the tick at whose end the moving body's component first heard a Collision with the other body,
    /// whether the step of that contact ran with the moving body in the home that the scene expects, the mode of the moving
    /// body's home in that step, and the moving body's state relative to a frame of the scene at the end.
    /// </summary>
    private sealed record Outcome(
        long HitTick,
        bool HitInExpectedHome,
        NumericMode HitMode,
        Double3 Position,
        Double3 Velocity,
        List<ContactNews> News);

    /// <summary>
    /// Runs <paramref name="ticks"/> ticks and returns the outcome for <paramref name="mover"/>, whose <paramref name="log"/>
    /// hears its collisions with <paramref name="other"/>. <paramref name="expectedHome"/> gives, after each tick, the home in
    /// which the scene expects the mover to meet the other body. A contact in the step of a tick becomes a Collision event at
    /// that tick's sync point, and the component hears it in the next tick. That step ran in the home that the sync point of
    /// the tick before it gave the mover. So when the component has heard the contact at the end of a tick, the home of the
    /// contact is the one after the tick before the last one. The world checks its invariants at the end of every tick.
    /// </summary>
    private static Outcome Watch(World world, Entity mover, CollisionLog log, Entity other,
        Func<FrameHandle> expectedHome, FrameHandle frame,
        int ticks)
    {
        long hitTick = -1;
        bool hitInExpectedHome = false;
        NumericMode hitMode = default;

        (FrameHandle Home, FrameHandle Expected, NumericMode Mode) Now()
        {
            FrameHandle home = world.PhysicsHomeOf(mover);
            return (home, expectedHome(), world.Frames.Numeric(home));
        }

        (FrameHandle Home, FrameHandle Expected, NumericMode Mode) last = Now();
        (FrameHandle Home, FrameHandle Expected, NumericMode Mode) contactStep = last;
        for (int tick = 0; tick < ticks; tick++)
        {
            world.Tick();
            if (hitTick < 0 && log.CountWith(other) > 0)
            {
                hitTick = world.TickNumber;
                hitInExpectedHome = contactStep.Home == contactStep.Expected;
                hitMode = contactStep.Mode;
            }

            contactStep = last;
            last = Now();
        }

        world.Errors.Should().BeEmpty();
        return new(hitTick, hitInExpectedHome, hitMode, world.PoseIn(mover, frame).Position,
            world.VelocityIn(mover, frame), log.NewsOf(other));
    }

    // ---- A body that sticks out of a semantic child ---------------------------------------------------------------------

    /// <summary>
    /// Space, and at (1000, 0, 0) in it a parked station: a static hull at the origin of a frame that physics does not drive,
    /// with a bubble of radius 200 m. A crate floats at (0, 200.5, 0) in the station and sticks 1.5 m out of its bubble. A
    /// rock flies at 60 m/s along -X from (1200, 201, 0) in space, with a gentle thrust. After the 30 ticks of onset, the rock
    /// thrusts and is at (1170, 201, 0), 170 m from the crate, beyond the merge test's reach of the crate's place: the
    /// bounding spheres of the rock and the crate plus 2 s of their relative speed, about 122 m. So it falls in a numeric
    /// frame of its own. The rock's center stays 1 m outside the station's bubble, so it never touches the bubble and never
    /// meets the real crate, and at least 201 m from the hull, beyond the merge test's reach of the hull, about 124 m. The
    /// expected home of the contact is the system of space, the home of the station's hull, where the ghost of the crate
    /// lives.
    /// </summary>
    private static Outcome RunRockPastAParkedStation(bool numericFrames)
    {
        using World world = NewWorld(numericFrames, out _);
        FrameHandle space = Space(world);
        Entity station = world.Spawn(new()
        {
            Frame = space, Pose = At(1000), Body = StaticHull, AsFrame = new(FrameBounds.Sphere(200))
        });
        FrameHandle bubble = world.GetOwnedFrame(station);
        Entity crate = world.Spawn(new() { Frame = bubble, Pose = At(0, 200.5), Body = CrateBody });
        var rockLog = new CollisionLog();
        Entity rock = world.Spawn(new()
        {
            Frame = space, Pose = At(1200, 201), LinearVelocity = new(-60, 0, 0), Body = RockBody,
            Components = [rockLog, new Thruster(RockBody.Mass)]
        });
        Tick(world, OnsetTicks);
        FrameHandle start = world.PhysicsHomeOf(rock);
        if (numericFrames)
            world.Frames.Numeric(start).Should().Be(NumericMode.Falling,
                "the rock thrusts at 60 m/s, beyond the reach of the crate's place");
        else
            start.Should().Be(space);

        Outcome outcome = Watch(world, rock, rockLog, crate, () => space, space, 200);
        world.GetFrame(rock).Should().Be(space, "the rock never touched the bubble");
        world.GetFrame(crate).Should().Be(bubble, "the crate never left the bubble");
        return outcome;
    }

    [Fact]
    public void
        AFastRockInAFallingFrameHitsACrateThatSticksOutOfAParkedStationInTheSystemOfTheStationsHullAsItDoesWithoutNumericFrames()
    {
        Outcome with = RunRockPastAParkedStation(numericFrames: true);
        Outcome without = RunRockPastAParkedStation(numericFrames: false);

        // The front of the rock reaches the crate's face at x = 1001 after 198.5 m from its spawn, or 3.31 s.
        without.HitTick.Should()
            .BeInRange(197, 203, "the rock hits the ghost of the crate in space after about 3.31 s");
        without.Velocity.Length.Should().BeLessThan(5, "the ghost of the crate stopped the rock");
        without.Position.X.Should().BeGreaterThan(1001, "the rock did not pass through the crate");
        with.HitInExpectedHome.Should()
            .BeTrue("the merge test linked the rock to the crate's place, so it joined the home of the station's hull");
        with.HitMode.Should().Be(NumericMode.None);
        with.HitTick.Should().BeInRange(without.HitTick - 1, without.HitTick + 1);
        with.Velocity.Length.Should().BeLessThan(5, "the ghost stopped the rock");
        with.Position.X.Should().BeGreaterThan(1001, "the rock did not pass through the crate");
        with.News.Should().Equal(without.News);
        with.News.Should().StartWith(ContactNews.Started);
    }

    /// <summary>
    /// Space, and at (180, 0, 0) in it a ship that flies at 60 m/s along -X with a gentle thrust: a hull that physics drives,
    /// with a bubble of radius 200 m. A crate floats at (0, -200, 0) in the ship, on the edge of its bubble, and so sticks
    /// out of it. A rock rests at (0, -200.6, 0) in space, in the path of the part of the crate that sticks out, 0.1 m
    /// outside the ship's bubble. It is slow and near the origin of space, so it rests in the system of space. After the 30
    /// ticks of onset, the hull thrusts at (150, 0, 0), so it falls in a numeric frame of its own, under which the ship's
    /// frame hangs, and the crate is 150 m from the rock, beyond the merge test's reach of the crate's place, about 122 m.
    /// The rock stays at least 200 m from the hull, beyond the merge test's reach of the hull, about 124 m. The expected home
    /// of the contact is the home of the ship's hull, where the ghost of the crate lives.
    /// </summary>
    private static Outcome RunShipOverARock(bool numericFrames,
        Action<World, FaultInjectingBackend, Entity, Entity, FrameHandle>? afterOnset = null)
    {
        using World world = NewWorld(numericFrames, out FaultInjectingBackend faults);
        FrameHandle space = Space(world);
        Entity ship = world.Spawn(new()
        {
            Frame = space, Pose = At(180), LinearVelocity = new(-60, 0, 0), Body = ShipHull,
            AsFrame = new(FrameBounds.Sphere(200)), Components = [new Thruster(ShipHull.Mass)]
        });
        FrameHandle bubble = world.GetOwnedFrame(ship);
        Entity crate = world.Spawn(new() { Frame = bubble, Pose = At(0, -200), Body = CrateBody });
        var rockLog = new CollisionLog();
        Entity rock =
            world.Spawn(new() { Frame = space, Pose = At(0, -200.6), Body = RockBody, Components = [rockLog] });
        Tick(world, OnsetTicks);
        world.PhysicsHomeOf(rock).Should().Be(space,
            "the rock is slow and near the origin, beyond the reach of the crate's place");
        if (numericFrames)
            world.Frames.Numeric(world.PhysicsHomeOf(ship)).Should()
                .Be(NumericMode.Falling, "the ship's hull thrusts at 60 m/s");
        afterOnset?.Invoke(world, faults, ship, crate, space);

        Outcome outcome = Watch(world, rock, rockLog, crate, () => world.PhysicsHomeOf(ship), space, 200);
        world.GetFrame(rock).Should().Be(space, "the rock never touched the bubble");
        world.GetFrame(crate).Should().Be(bubble, "the crate never left the bubble");
        return outcome;
    }

    [Fact]
    public void
        ACrateThatSticksOutOfAFallingShipPushesARockThatRestsInSpaceInTheShipsFallingFrameAsItDoesWithoutNumericFrames()
    {
        Outcome with = RunShipOverARock(numericFrames: true);
        Outcome without = RunShipOverARock(numericFrames: false);

        // The front of the crate reaches the back of the rock, at x = 0.5 in space, after 178.5 m from its spawn, or 2.98 s.
        without.HitTick.Should()
            .BeInRange(177, 183, "the ghost of the crate in space reaches the rock after about 2.98 s");
        without.Velocity.X.Should().BeLessThan(-50, "the ghost pushes the rock along -X at the crate's speed");
        with.HitInExpectedHome.Should()
            .BeTrue("the merge test linked the rock to the crate's place, so it joined the ship's falling frame");
        with.HitMode.Should().Be(NumericMode.Falling);
        with.HitTick.Should().BeInRange(without.HitTick - 1, without.HitTick + 1);
        (with.Velocity - without.Velocity).Length.Should()
            .BeLessThan(0.1, "the ghost pushes the rock at the crate's speed");
        with.Position.Y.Should().BeApproximately(without.Position.Y, 1e-3);
        with.Position.Z.Should().BeApproximately(without.Position.Z, 1e-3);
        with.News.Should().Equal(without.News);
        with.News.Should().StartWith(ContactNews.Started);
    }

    /// <summary>
    /// The ghost of a body that sticks out of a frame lives in the frame's tree parent, the home of the frame's hull. When
    /// that home is a numeric frame, every body of the semantic parent that may touch the ghost joins it (see
    /// <see cref="ACrateThatSticksOutOfAFallingShipPushesARockThatRestsInSpaceInTheShipsFallingFrameAsItDoesWithoutNumericFrames"/>
    /// ),
    /// so the semantic parent's own system needs no second ghost of the body.
    /// </summary>
    [Fact]
    public void ACrateThatSticksOutOfAFallingShipHasItsOneGhostInTheShipsFallingFrameAndNoneInTheSystemOfSpace()
    {
        RunShipOverARock(numericFrames: true, afterOnset: (world, faults, ship, crate, space) =>
        {
            world.Tick();
            FrameHandle falling = world.Frames.TreeParent(world.GetOwnedFrame(ship));
            world.Frames.Numeric(falling).Should().Be(NumericMode.Falling);
            faults.Jolt.HasGhostIn(crate, falling).Should()
                .BeTrue("the crate sticks out of the ship into its tree parent");
            faults.Jolt.HasGhostIn(crate, space).Should()
                .BeFalse("no body of space that may touch the ghost lives in the system of space");
            faults.Jolt.GhostCount(crate).Should().Be(1);
        });
    }

    /// <summary>
    /// The ship of <see cref="RunShipOverARock"/> without the rock. Before the 30 ticks of onset, the hull flies at 60 m/s
    /// in the system of space, so the crate's ghost lives there. At the sync point of the 30th tick, the hull moves into a
    /// falling frame of its own, and the ship's frame with it. From then on the tree allows the crate a ghost in that frame
    /// and none in the system of space, so the move takes the ghost with the hull into the falling frame at once, at the
    /// crate's place and velocity in space (review of ter-4psc). Before, the reconcile of that sync point dropped the ghost
    /// in space, and the ghost pass made the one in the falling frame only at the end of the next step. A parked station at
    /// (2000, 0, 0) in space, with a static hull and a bubble of radius 40 m, holds a second crate at (0, 40.5, 0), which
    /// sticks out of the station into space. Its ghost is in the system of space too, the old home of the ship's hull, and
    /// it stays there: only the ghosts of the bodies that stick out of the ship move with the ship's hull.
    /// </summary>
    [Fact]
    public void
        TheSyncPointThatMovesAShipsHullIntoAFallingFrameMovesTheGhostOfACrateThatSticksOutOfTheShipFromSpaceIntoTheFallingFrame()
    {
        using World world = NewWorld(numericFrames: true, out FaultInjectingBackend faults);
        FrameHandle space = Space(world);
        Entity ship = world.Spawn(new()
        {
            Frame = space, Pose = At(180), LinearVelocity = new(-60, 0, 0), Body = ShipHull,
            AsFrame = new(FrameBounds.Sphere(200)), Components = [new Thruster(ShipHull.Mass)]
        });
        FrameHandle bubble = world.GetOwnedFrame(ship);
        Entity crate = world.Spawn(new() { Frame = bubble, Pose = At(0, -200), Body = CrateBody });
        Entity station = world.Spawn(new()
        {
            Frame = space, Pose = At(2000), Body = StaticHull, AsFrame = new(FrameBounds.Sphere(40))
        });
        Entity stationCrate = world.Spawn(new()
            { Frame = world.GetOwnedFrame(station), Pose = At(0, 40.5), Body = CrateBody });
        Tick(world, 2);
        faults.Jolt.HasGhostIn(crate, space).Should()
            .BeTrue("the hull flies in the system of space, and the crate sticks out of the ship");
        faults.Jolt.HasGhostIn(stationCrate, space).Should()
            .BeTrue("the second crate sticks out of the station into space");

        int ticks = TickUntil(world, () => world.Frames.IsNumeric(world.Frames.TreeParent(bubble)), 40);

        ticks.Should().Be(OnsetTicks - 2, "the hull thrusts from its 30th tick of thrust");
        FrameHandle falling = world.Frames.TreeParent(bubble);
        faults.Jolt.HasGhostIn(crate, space).Should().BeFalse("the ship's frame hangs in the hull's falling frame now");
        faults.Jolt.HasGhostIn(crate, falling).Should().BeTrue("the ghost moved with the hull");
        faults.Jolt.GhostCount(crate).Should().Be(1);
        JoltPhysicsBackend.BodyFacts ghost = faults.Jolt.BodiesIn(falling)
            .Single(facts => BodyUserData.Kind(facts.UserData) == BodyKind.Ghost &&
                             BodyUserData.Names(facts.UserData, crate));
        Double3 ghostInSpace = ghost.Position + world.Frames.Relative(falling, space).Position;
        Double3 crateInSpace = world.PoseIn(crate, space).Position;
        (ghostInSpace - crateInSpace).Length.Should().BeLessThan(1e-3, "the ghost keeps the crate's place in space");
        Double3 ghostVelocity = ghost.LinearVelocity + world.Frames.HomeVelocity(falling, space);
        (ghostVelocity - world.VelocityIn(crate, space)).Length.Should()
            .BeLessThan(1e-3, "the ghost keeps the crate's velocity in space");
        faults.Jolt.HasGhostIn(stationCrate, space).Should()
            .BeTrue("the station's crate does not stick out of the ship");
        faults.Jolt.GhostCount(stationCrate).Should().Be(1);
        faults.Jolt.Invoking(backend => backend.AssertInvariants()).Should().NotThrow();
        world.Tick();
        faults.Jolt.HasGhostIn(crate, falling).Should().BeTrue();
        faults.Jolt.HasGhostIn(crate, space).Should().BeFalse();
        world.Errors.Should().BeEmpty();
    }

    /// <summary>
    /// The ship of <see cref="RunShipOverARock"/>, and a rock that flies at 60 m/s along +X in space from (118, -200.6, 0),
    /// head on at the part of the crate that sticks out of the ship, 0.1 m outside the ship's bubble. The merge test links
    /// the rock to the crate's place from the spawn, within the reach of their bounding spheres plus 2 s of their relative
    /// speed of 120 m/s. Before the onset the hull flies in the system of space, so the rock lives there with it, and so does
    /// the crate's ghost. At the sync point of the 30th tick, the hull moves into a falling frame of its own, the ship's frame
    /// hangs under it, and the rock moves with the hull. Then the rock's front is 0.5 m from the crate's face, and the two
    /// close at 120 m/s, 2 m a step. So the crate's ghost meets the rock in the step right after the move, in the falling
    /// frame. Without numeric frames, it meets the rock in that step in the system of space. The run ends 30 ticks after the
    /// onset, with the rock's place and velocity in the ship's frame.
    /// </summary>
    private static Outcome RunRockAtTheShipsOnset(bool numericFrames)
    {
        using World world = NewWorld(numericFrames, out _);
        FrameHandle space = Space(world);
        Entity ship = world.Spawn(new()
        {
            Frame = space, Pose = At(180), LinearVelocity = new(-60, 0, 0), Body = ShipHull,
            AsFrame = new(FrameBounds.Sphere(200)), Components = [new Thruster(ShipHull.Mass)]
        });
        FrameHandle bubble = world.GetOwnedFrame(ship);
        Entity crate = world.Spawn(new() { Frame = bubble, Pose = At(0, -200), Body = CrateBody });
        var rockLog = new CollisionLog();
        Entity rock = world.Spawn(new()
        {
            Frame = space, Pose = At(118, -200.6), LinearVelocity = new(60, 0, 0), Body = RockBody,
            Components = [rockLog]
        });
        Tick(world, OnsetTicks - 1);
        world.PhysicsHomeOf(rock).Should().Be(world.PhysicsHomeOf(ship),
            "the merge test links the rock to the crate's place, whose target is the ship's hull");
        world.PhysicsHomeOf(ship).Should().Be(space, "the hull thrusts only from its 30th tick of thrust");

        Outcome outcome = Watch(world, rock, rockLog, crate, () => world.PhysicsHomeOf(ship), bubble, 31);
        if (numericFrames)
            world.Frames.Numeric(world.PhysicsHomeOf(ship)).Should()
                .Be(NumericMode.Falling, "the hull thrusts at 60 m/s");
        world.GetFrame(rock).Should().Be(space, "the rock never touched the bubble");
        world.GetFrame(crate).Should().Be(bubble, "the crate never left the bubble");
        return outcome;
    }

    /// <summary>
    /// A moved body keeps its ghosts, and the ghost of a body that sticks out of a frame moves with the frame's hull (review
    /// of ter-4psc). Before, the reconcile of the sync point that moved the hull into its falling frame dropped the crate's
    /// ghost in space, and the ghost pass made the one in the falling frame only at the end of the next step. So in that
    /// step the rock met no ghost and flew 2 m on, into the place of the crate.
    /// </summary>
    [Fact]
    public void
        ARockThatTheGhostOfACrateMeetsInTheStepAfterTheShipsHullMovesIntoAFallingFrameStopsAtItAsWithoutNumericFrames()
    {
        Outcome with = RunRockAtTheShipsOnset(numericFrames: true);
        Outcome without = RunRockAtTheShipsOnset(numericFrames: false);

        without.HitTick.Should()
            .BeInRange(30, 33, "the ghost of the crate meets the rock in the step after the 30th tick");
        without.Position.X.Should()
            .BeLessThan(-1.4, "the rock stays in front of the crate's face, at x = -1 in the ship");
        without.Velocity.Length.Should()
            .BeLessThan(1, "the ghost of the crate stopped the rock and pushes it with the ship");
        with.HitInExpectedHome.Should().BeTrue("the rock moved with the hull into its falling frame");
        with.HitMode.Should().Be(NumericMode.Falling);
        with.HitTick.Should().Be(without.HitTick,
            "the ghost of the crate is in the falling frame in the step after the move");
        (with.Position - without.Position).Length.Should()
            .BeLessThan(1e-3, "the ghost stopped the rock at the same place in the ship");
        (with.Velocity - without.Velocity).Length.Should().BeLessThan(0.1);
        with.News.Should().Equal(without.News);
        with.News.Should().StartWith(ContactNews.Started);
    }

    /// <summary>
    /// The ship of <see cref="RunShipOverARock"/> without the rock. After the onset the hull falls in a numeric frame of its
    /// own. Then the hull and the crate brake together at 50 m/s², so the crate keeps its place in the ship, until the hull
    /// is slower than 20 m/s relative to space, the leave speed of a group that thrusts. At that sync point the hull moves
    /// back into the system of space, and the ship's frame with it. The move takes the crate's ghost out of the falling frame
    /// into space at once, at the crate's place and velocity in space (review of ter-4psc).
    /// </summary>
    [Fact]
    public void TheSyncPointThatMovesAShipsHullOutOfAFallingFrameMovesTheGhostOfACrateThatSticksOutOfTheShipIntoSpace()
    {
        using World world = NewWorld(numericFrames: true, out FaultInjectingBackend faults);
        FrameHandle space = Space(world);
        var hullThruster = new Thruster(ShipHull.Mass);
        Entity ship = world.Spawn(new()
        {
            Frame = space, Pose = At(180), LinearVelocity = new(-60, 0, 0), Body = ShipHull,
            AsFrame = new(FrameBounds.Sphere(200)), Components = [hullThruster]
        });
        FrameHandle bubble = world.GetOwnedFrame(ship);
        var crateThruster = new Thruster(CrateBody.Mass);
        Entity crate = world.Spawn(new()
            { Frame = bubble, Pose = At(0, -200), Body = CrateBody, Components = [crateThruster] });
        Tick(world, OnsetTicks + 1);
        FrameHandle falling = world.Frames.TreeParent(bubble);
        world.Frames.Numeric(falling).Should().Be(NumericMode.Falling, "the hull thrusts at 60 m/s");
        faults.Jolt.HasGhostIn(crate, falling).Should().BeTrue("the crate sticks out of the ship into its tree parent");

        hullThruster.Acceleration = crateThruster.Acceleration = 50;
        int ticks = TickUntil(world, () => world.Frames.TreeParent(bubble) == space, 200);

        ticks.Should().BeLessThan(200, "the hull brakes below 20 m/s after about 0.8 s");
        faults.Jolt.HasGhostIn(crate, space).Should().BeTrue("the ghost moved with the hull");
        faults.Jolt.GhostCount(crate).Should().Be(1);
        JoltPhysicsBackend.BodyFacts ghost = faults.Jolt.BodiesIn(space)
            .Single(facts => BodyUserData.Kind(facts.UserData) == BodyKind.Ghost &&
                             BodyUserData.Names(facts.UserData, crate));
        (ghost.Position - world.PoseIn(crate, space).Position).Length.Should()
            .BeLessThan(1e-3, "the ghost keeps the crate's place in space");
        // The tree keeps the ship's instant velocity, half a step of its braking past its chord, which the ghost follows.
        MotionSample shipRecord = faults.Outputs[^1].Frames.Single(state => state.Frame == bubble).Motion;
        Double3 crateChord = world.VelocityIn(crate, space) - (shipRecord.Velocity - shipRecord.ChordVelocity);
        (ghost.LinearVelocity - crateChord).Length.Should()
            .BeLessThan(1e-3, "the ghost keeps the crate's velocity in space");
        faults.Jolt.Invoking(backend => backend.AssertInvariants()).Should().NotThrow();
        world.Tick();
        faults.Jolt.HasGhostIn(crate, space).Should().BeTrue();
        world.Errors.Should().BeEmpty();
    }

    /// <summary>What a run of <see cref="DestroyAFallingShipWithARoom"/> saw.</summary>
    /// <param name="RoomParent">The tree parent of the room at the end.</param>
    /// <param name="Moved">How far the room moved in space over the 60 ticks after the destroy, in meters.</param>
    /// <param name="CratesGhostInRoomsParent">True when the crate has a ghost in the room's tree parent at the end.</param>
    /// <param name="NumericFrames">The number of numeric frames at the end.</param>
    private sealed record RoomRun(
        FrameHandle RoomParent,
        double Moved,
        bool CratesGhostInRoomsParent,
        int NumericFrames);

    /// <summary>
    /// Space, and at (180, 0, 0) in it a ship that flies at 60 m/s along -X with a gentle thrust: a hull that physics
    /// drives, with a bubble of radius 200 m. The ship holds a room at its origin: a frame with no
    /// body, with a bubble of radius 20 m. A crate floats at (0, -20, 0) in the room, on the edge of its bubble, and so
    /// sticks out of it. After the 30 ticks of onset, the ship's hull falls in a numeric frame of its own. Then the game
    /// destroys the ship, and the run goes on for 60 ticks.
    /// </summary>
    private static RoomRun DestroyAFallingShipWithARoom(bool numericFrames)
    {
        using World world = NewWorld(numericFrames, out FaultInjectingBackend faults);
        FrameHandle space = Space(world);
        Entity ship = world.Spawn(new()
        {
            Frame = space, Pose = At(180), LinearVelocity = new(-60, 0, 0), Body = ShipHull,
            AsFrame = new(FrameBounds.Sphere(200)), Components = [new Thruster(ShipHull.Mass)]
        });
        Entity room = world.Spawn(new()
        {
            Frame = world.GetOwnedFrame(ship), Pose = DPose.Identity, AsFrame = new(FrameBounds.Sphere(20))
        });
        FrameHandle roomFrame = world.GetOwnedFrame(room);
        Entity crate = world.Spawn(new() { Frame = roomFrame, Pose = At(0, -20), Body = CrateBody });
        Tick(world, OnsetTicks);
        if (numericFrames)
            world.Frames.Numeric(world.PhysicsHomeOf(ship)).Should()
                .Be(NumericMode.Falling, "the ship's hull thrusts at 60 m/s");

        world.Destroy(ship).Should().BeTrue();
        Double3 place = world.PoseIn(room, space).Position;
        Tick(world, 60);

        world.Errors.Should().BeEmpty();
        FrameHandle parent = world.Frames.TreeParent(roomFrame);
        return new(parent, (world.PoseIn(room, space).Position - place).Length, faults.Jolt.HasGhostIn(crate, parent),
            world.Frames.NumericCount);
    }

    /// <summary>
    /// A frame without a hull never hangs in a numeric frame: when the frame that holds it dies, it goes to the semantic
    /// frame, as every frame does (review of ter-4psc). Before, a room like this one went to the dying frame's
    /// tree parent, here the ship's falling frame. That frame fell on with the room in it, and no numeric update moved the
    /// room out, because it has no hull. The update also took the places of the crate's ghost to be fixed in the system of
    /// space, while physics kept the ghost in the falling frame.
    /// </summary>
    [Fact]
    public void ARoomWithoutAHullInAShipThatDiesInAFallingFrameMovesToSpaceAndHoldsItsPlaceAsWithoutNumericFrames()
    {
        RoomRun with = DestroyAFallingShipWithARoom(numericFrames: true);
        RoomRun without = DestroyAFallingShipWithARoom(numericFrames: false);

        with.RoomParent.Should().Be(without.RoomParent, "the room goes to space, the ship's semantic parent");
        without.Moved.Should().BeLessThan(1e-6, "nothing moves a frame without a hull in space");
        with.Moved.Should().BeLessThan(1e-6, "the room holds still where it was, as without numeric frames");
        with.CratesGhostInRoomsParent.Should().BeTrue("the crate sticks out of the room into its tree parent");
        without.CratesGhostInRoomsParent.Should().BeTrue();
        with.NumericFrames.Should().Be(0, "the falling frame holds nothing once the ship is gone");
    }

    // ---- A ghost that is fixed in a frame's own system (ter-tenw) -------------------------------------------------------

    /// <summary>
    /// Space, and in it a frame D at the origin with a bubble of radius 100 m and no body, and a parked frame E with a
    /// static hull at (101, 0, 0) and a bubble of radius 300 m. E does not fit in D, so its hull, which reaches 1 m into D's
    /// bubble, never enters D, and it has a ghost in D's own system. A crate in D flies at 60 m/s along +X from (-80, 0, 0),
    /// with a gentle thrust. After the 30 ticks of onset, the crate thrusts at (-50, 0, 0), 151 m from the hull, beyond the
    /// merge test's reach of the hull's place, the bounding spheres of the crate and the hull plus 2 s of their relative speed,
    /// about 125 m. So it falls in a numeric frame of its own. The crate's center stays 2 m inside D's bubble, so it never
    /// leaves D. The expected home of the contact is D's own system, where the ghost of the hull lives.
    /// </summary>
    private static Outcome RunCrateAtAHullThatTouchesItsFrame(bool numericFrames)
    {
        using World world = NewWorld(numericFrames, out _);
        FrameHandle space = Space(world);
        FrameHandle d =
            world.GetOwnedFrame(world.Spawn(new() { Frame = space, AsFrame = new(FrameBounds.Sphere(100)) }));
        Entity e = world.Spawn(new()
        {
            Frame = space, Pose = At(101), Body = StaticHull, AsFrame = new(FrameBounds.Sphere(300))
        });
        var crateLog = new CollisionLog();
        Entity crate = world.Spawn(new()
        {
            Frame = d, Pose = At(-80), LinearVelocity = new(60, 0, 0), Body = CrateBody,
            Components = [crateLog, new Thruster(CrateBody.Mass)]
        });
        Tick(world, OnsetTicks);
        FrameHandle start = world.PhysicsHomeOf(crate);
        if (numericFrames)
            world.Frames.Numeric(start).Should().Be(NumericMode.Falling,
                "the crate thrusts at 60 m/s, beyond the reach of the hull's place");
        else
            start.Should().Be(d);

        Outcome outcome = Watch(world, crate, crateLog, e, () => d, d, 210);
        world.GetFrame(crate).Should().Be(d, "the crate never left D");
        world.GetFrame(e).Should().Be(space, "E does not fit in D");
        return outcome;
    }

    [Fact]
    public void
        AFastCrateInsideAFrameMeetsTheGhostOfAHullThatTouchesTheFramesBubbleWithoutEnteringAsItDoesWithoutNumericFrames()
    {
        Outcome with = RunCrateAtAHullThatTouchesItsFrame(numericFrames: true);
        Outcome without = RunCrateAtAHullThatTouchesItsFrame(numericFrames: false);

        // The front of the crate reaches the hull's face at x = 99 in D after 178 m from its spawn, or 2.97 s.
        without.HitTick.Should().BeInRange(176, 182, "the crate hits the ghost of the hull in D after about 2.97 s");
        without.Velocity.Length.Should().BeLessThan(5, "the ghost of the hull stopped the crate");
        without.Position.X.Should().BeLessThan(98.1, "the crate did not pass into the hull");
        with.HitInExpectedHome.Should()
            .BeTrue("the merge test linked the crate to the hull's place, which is fixed in D's own system");
        with.HitMode.Should().Be(NumericMode.None);
        with.HitTick.Should().BeInRange(without.HitTick - 1, without.HitTick + 1);
        with.Velocity.Length.Should().BeLessThan(5, "the ghost stopped the crate");
        with.Position.X.Should().BeLessThan(98.1, "the crate did not pass into the hull");
        with.News.Should().Equal(without.News);
        with.News.Should().StartWith(ContactNews.Started);
    }

    /// <summary>
    /// A far station: a root frame with a point gravity of <see cref="FarMu"/>, and in it the owner of a Kepler frame on a
    /// circular orbit of <see cref="Far"/>, at 2.44 m/s, with the hull of <see cref="ShipHull"/> and a bubble of radius 40 m.
    /// Its hull lives in a following frame of its own (docs/plans/numeric-frames.md, "Anchors", rule 3). A crate floats at
    /// (0, 40, 0) in the station, on the edge of its bubble, and so sticks out of it. <see cref="CratePlace"/> is the
    /// crate's place in space at the spawn, <see cref="Up"/> the station's +Y in space, and <see cref="Forward"/> the
    /// direction of the station's velocity in space.
    /// </summary>
    private sealed record FarStation(
        FrameHandle Space,
        Entity Owner,
        FrameHandle Station,
        Entity Crate,
        Double3 CratePlace,
        Double3 Up,
        Double3 Forward,
        Double3 Velocity);

    /// <summary>
    /// Spawns the <see cref="FarStation"/>. With <paramref name="firstMove"/> the spawn gives the hull the sample of its first
    /// step as its first motion intent, so the hull is on its orbit from its first step. Without one it holds still in that
    /// step, and its following frame with it (ticket ter-zsku). The merge test reaches as far as two seconds of a body's
    /// speed, so at the sync point after that step it does not link the still hull to a body 8.9 m away, and physics makes
    /// the hull's ghost in that body's home one step later than with a first motion intent. The tests of the ghosts of the
    /// hull look for the ghost after two ticks, so they spawn the hull with a first motion intent. The comparison with numeric
    /// frames off runs both ways.
    /// </summary>
    private static FarStation SpawnFarStation(World world, bool firstMove)
    {
        FrameHandle space = world.CreateRootFrame(new(FrameBounds.Sphere(1e9f), GravityField.Point(FarMu)));
        Entity owner = world.Spawn(new SpawnDesc
        {
            Frame = space, Body = ShipHull with { Motion = BodyMotion.Kinematic }, AsFrame = new(FrameBounds.Sphere(40))
        }.OnKeplerOrbit(world, KeplerOrbit.Circular(Far), firstMove));
        FrameHandle station = world.GetOwnedFrame(owner);
        Entity crate = world.Spawn(new() { Frame = station, Pose = At(0, 40), Body = CrateBody });
        Double3 cratePlace = world.Frames.PointToParent(station, new(0, 40, 0));
        Double3 up = world.Frames.PointToParent(station, new(0, 41, 0)) - cratePlace;
        Double3 velocity = world.Frames.LinearVelocity(station);
        return new(space, owner, station, crate, cratePlace, up, velocity / velocity.Length, velocity);
    }

    /// <summary>True when the last numeric update named a ghost of the anchor <paramref name="anchor"/> in
    /// <paramref name="frame"/>.</summary>
    private static bool NamesAnchorGhost(World world, Entity anchor, FrameHandle frame)
    {
        foreach (NumericGhost ghost in world.Frames.AnchorGhosts)
            if (ghost.Anchor == anchor && ghost.Frame == frame)
                return true;
        return false;
    }

    /// <summary>
    /// The station of <see cref="SpawnFarStation"/>, a pod, a dynamic ball of radius 1 m and 10 kg, 3 m beyond the crate
    /// along the station's +Y, with the station's velocity and 0.5 m/s more toward the crate, which closes the gap of 1 m in
    /// 2 s. A static rock of radius 1 m stands 3.5 m beyond the pod and 4 m ahead of it on its path. The merge test links
    /// the pod to the crate's place, 3 m away at 0.5 m/s, and to the rock, about 5.3 m away at 2.5 m/s, but not to the
    /// hull, 43 m away, and the rock is beyond the reach of the hull, about 47 m away. So the hull and the rock, two
    /// anchors that move differently, are in one group: the group lives in the rock's home, and the hull lives apart in its
    /// following frame, which hangs the station, and the group's home keeps a ghost of the hull. The pod meets the crate there
    /// only through a ghost of the crate in that home. Without numeric frames, everything lies in the system of space, and
    /// the ghost of the crate there meets the pod. The expected home of the contact is the rock's home.
    /// </summary>
    private static Outcome RunPodBesideAFarStation(bool numericFrames, bool firstMove)
    {
        using World world = NewWorld(numericFrames, out _);
        FarStation scene = SpawnFarStation(world, firstMove);
        var podLog = new CollisionLog();
        Entity pod = world.Spawn(new()
        {
            Frame = scene.Space, Pose = new(scene.CratePlace + scene.Up * 3, DQuaternion.Identity),
            LinearVelocity = scene.Velocity - scene.Up * 0.5,
            Body = new(new SphereShapeDesc(1), Mass: 10, Restitution: 0, LinearDamping: 0, AngularDamping: 0),
            Components = [podLog]
        });
        Entity rock = world.Spawn(new()
        {
            Frame = scene.Space,
            Pose = new(scene.CratePlace + scene.Up * 6.5 + scene.Forward * 4, DQuaternion.Identity),
            Body = new(new SphereShapeDesc(1), BodyMotion.Static)
        });
        Tick(world, 2);
        FrameHandle home = world.PhysicsHomeOf(rock);
        world.PhysicsHomeOf(pod).Should().Be(home, "the pod and the rock are in one group");
        if (numericFrames)
        {
            FrameHandle orbit = world.PhysicsHomeOf(scene.Owner);
            world.Frames.Numeric(orbit).Should()
                .Be(NumericMode.Following, "the hull lives in a following frame of its own");
            orbit.Should().NotBe(home);
            NamesAnchorGhost(world, scene.Owner, home).Should()
                .BeTrue("the pod's place links the hull to the group, whose home cannot hold it");
        }
        else
        {
            home.Should().Be(scene.Space);
        }

        Outcome outcome = Watch(world, pod, podLog, scene.Crate, () => world.PhysicsHomeOf(rock), scene.Station, 180);
        world.GetFrame(pod).Should().Be(scene.Space, "the pod never touched the bubble");
        world.GetFrame(scene.Crate).Should().Be(scene.Station, "the crate never left the bubble");
        return outcome;
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void
        ABodyInTheHomeOfAGroupWhoseKeplerHullLivesApartMeetsACrateThatSticksOutOfTheHullsFrameAsItDoesWithoutNumericFrames(
            bool firstMove)
    {
        Outcome with = RunPodBesideAFarStation(numericFrames: true, firstMove);
        Outcome without = RunPodBesideAFarStation(numericFrames: false, firstMove);

        // The pod closes the gap of 1 m to the crate's face at 0.5 m/s in 2 s.
        without.HitTick.Should().BeInRange(118, 126, "the pod hits the ghost of the crate in space after about 2 s");
        without.Position.Y.Should().BeGreaterThan(41.9, "the ghost of the crate stopped the pod");
        with.HitInExpectedHome.Should()
            .BeTrue("the pod meets the crate in the home of its group, not in the hull's following frame");
        with.HitTick.Should().BeInRange(without.HitTick - 1, without.HitTick + 1);
        (with.Position - without.Position).Length.Should()
            .BeLessThan(0.05, "the ghost stopped the pod at the same place in the station");
        with.News.Should().Equal(without.News);
        with.News.Should().StartWith(ContactNews.Started);
    }

    /// <summary>
    /// The station of <see cref="SpawnFarStation"/> passes a static rock of radius 1 m that stands 8 m from its path, on the
    /// side away from the origin, and 4 m ahead of the hull. The merge test links the hull and the rock, about 8.9 m apart
    /// at the station's 2.44 m/s, which it reaches up to their bounding spheres plus 2 s of that speed, about 9.3 m. So the
    /// rock's home keeps a ghost of the hull, and each body that sticks out of the station, the crate, has a ghost there too.
    /// When the hull has flown on beyond the reach, the update stops naming the rock's home, and the ghosts there go at the
    /// same sync point, although nothing else changes at it. So the backend keeps its invariants between ticks, and no ghost
    /// stands for the hull or the crate where the tree allows none. The crate keeps its ghost in the hull's following frame.
    /// </summary>
    [Fact]
    public void
        TheGhostsOfAKeplerHullAndOfTheCrateThatSticksOutOfItsFrameGoAtTheSyncPointWhoseUpdateStopsNamingTheirHome()
    {
        using World world = NewWorld(numericFrames: true, out FaultInjectingBackend faults);
        FarStation scene = SpawnFarStation(world, firstMove: true);
        Double3 hullPlace = world.Frames.LocalPose(scene.Station).Position;
        Entity rock = world.Spawn(new()
        {
            Frame = scene.Space,
            Pose = new(hullPlace + hullPlace / hullPlace.Length * 8 + scene.Forward * 4, DQuaternion.Identity),
            Body = new(new SphereShapeDesc(1), BodyMotion.Static)
        });
        Tick(world, 2);
        FrameHandle home = world.PhysicsHomeOf(rock);
        FrameHandle orbit = world.PhysicsHomeOf(scene.Owner);
        world.Frames.Numeric(orbit).Should().Be(NumericMode.Following);
        NamesAnchorGhost(world, scene.Owner, home).Should().BeTrue("the hull starts 8.9 m from the rock");
        faults.Jolt.HasGhostIn(scene.Owner, home).Should().BeTrue();
        faults.Jolt.HasGhostIn(scene.Crate, home).Should().BeTrue(
            "the crate sticks out of the station, which hangs in the hull's following frame, apart from the rock's home");
        faults.Jolt.HasGhostIn(scene.Crate, orbit).Should()
            .BeTrue("the crate sticks out of the station into its tree parent");
        faults.Jolt.Invoking(backend => backend.AssertInvariants()).Should().NotThrow();

        int ticks = TickUntil(world, () => !NamesAnchorGhost(world, scene.Owner, home), 400);

        ticks.Should().BeInRange(150, 300,
            "the hull flies on for about 3.6 s until the merge test lets go of the rock");
        world.PhysicsHomeOf(rock).Should().Be(home);
        faults.Jolt.HasGhostIn(scene.Owner, home).Should()
            .BeFalse("the numeric update no longer names the rock's home to keep a ghost of the hull");
        faults.Jolt.HasGhostIn(scene.Crate, home).Should()
            .BeFalse("the station's hull has no ghost in the rock's home");
        faults.Jolt.HasGhostIn(scene.Crate, orbit).Should().BeTrue();
        faults.Jolt.Invoking(backend => backend.AssertInvariants()).Should().NotThrow();
        world.Tick();
        world.Errors.Should().BeEmpty();
    }

    /// <summary>
    /// The station and the rock of
    /// <see cref="TheGhostsOfAKeplerHullAndOfTheCrateThatSticksOutOfItsFrameGoAtTheSyncPointWhoseUpdateStopsNamingTheirHome"/>
    /// .
    /// The rock lies far out, so it rests in a numeric frame, the home of the group that links it to the hull, and that
    /// home keeps a ghost of the hull. The game destroys the rock between two ticks. The numeric update that follows
    /// collects the rock's home, which has no member left, and stops naming it to keep the ghost, so one batch takes both
    /// to physics: the home's system goes with the frame, and the withdrawal of the ghost's place finds no system left to
    /// scan. Physics applies the whole batch and reports no error.
    /// </summary>
    [Fact]
    public void AHomeThatKeepsTheGhostOfAKeplerHullDiesInTheBatchThatWithdrawsTheGhostsPlace()
    {
        using World world = NewWorld(numericFrames: true, out FaultInjectingBackend faults);
        FarStation scene = SpawnFarStation(world, firstMove: true);
        Double3 hullPlace = world.Frames.LocalPose(scene.Station).Position;
        Entity rock = world.Spawn(new()
        {
            Frame = scene.Space,
            Pose = new(hullPlace + hullPlace / hullPlace.Length * 8 + scene.Forward * 4, DQuaternion.Identity),
            Body = new(new SphereShapeDesc(1), BodyMotion.Static)
        });
        Tick(world, 2);
        FrameHandle home = world.PhysicsHomeOf(rock);
        world.Frames.Numeric(home).Should().Be(NumericMode.Resting, "the rock lies 16,777 km from the origin of space");
        NamesAnchorGhost(world, scene.Owner, home).Should().BeTrue("the hull starts 8.9 m from the rock");
        faults.Jolt.HasGhostIn(scene.Owner, home).Should().BeTrue();

        world.Destroy(rock);
        world.Tick();

        world.Frames.IsAlive(home).Should().BeFalse("the rock's home has no member left");
        NamesAnchorGhost(world, scene.Owner, home).Should().BeFalse();
        faults.Jolt.HasGhostIn(scene.Owner, home).Should().BeFalse();
        world.Errors.Should().BeEmpty("physics applied the whole batch");
        faults.Jolt.Invoking(backend => backend.AssertInvariants()).Should().NotThrow();
    }
}
