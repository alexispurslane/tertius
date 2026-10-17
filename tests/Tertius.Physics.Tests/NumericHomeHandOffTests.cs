using System.Numerics;
using Tertius.Simulation;
using static Tertius.Testing.WorldTesting;
using static Tertius.Testing.NumericFrameTesting;

namespace Tertius.Physics.Tests;

/// <summary>
/// A body whose physics home is a numeric frame meets the bubbles that it meets in the system of its home's semantic frame
/// (ticket ter-kz89). A numeric frame is a physics-only frame that the engine makes for fast, falling or resting bodies,
/// so that Jolt's float positions stay small. It has no bubble, so it does not change which bubbles a body meets. A crate
/// that thrusts and is thrown at 60 m/s inside a ship falls in a numeric frame below the ship, whose semantic frame is the
/// ship. When the crate sticks out of the ship's bubble, it has a ghost in the ship's parent and in each sibling of the ship
/// whose bubble it touches. A ghost is a kinematic copy of a body in the physics system of another frame, so that the
/// bodies there collide with it. When the crate is fully outside the ship's bubble plus the exit margin, it moves into the
/// ship's parent: its semantic frame changes, and it hears FrameChanged. These tests run the World on the real backend,
/// with the default numeric frame options but a merge test without lookahead (see <see cref="WithoutLookahead"/>), and with
/// no numeric frames, where the crate stays in the system of the ship. They assert the same outcome in both.
/// <para>
/// Under the default options, a group of free bodies falls in a numeric frame only from 1,000 m/s, and a group with a body
/// that thrusts from 50 m/s. A body other than a character thrusts after 0.5 s (30 ticks) of thrust. So the crate carries a
/// gentle thruster (<see cref="Thruster"/>), waits at rest in the ship for those 30 ticks, and only then is thrown (see
/// <see cref="WarmUpAndThrow"/>). From the throw on, every tick and place is as it was when a free crate at 60 m/s fell.
/// </para>
/// </summary>
public class NumericHomeHandOffTests
{
    /// <summary>The radius of the ship's bubble, in meters.</summary>
    private const float ShipRadius = 30;

    /// <summary>
    /// How fast the crate is thrown in the ship, in m/s. It is more than the 50 m/s from which a group with a body that
    /// thrusts gets a falling frame, and less than the 1,000 m/s from which a group of free bodies gets one.
    /// </summary>
    private const float ThrowSpeed = 60;

    /// <summary>
    /// How many ticks of thrust at 60 Hz a body needs before it thrusts: the first count of ticks of 1/60 s that reaches the
    /// 0.5 s of <see cref="NumericFrameOptions.ThrustOnset"/>.
    /// </summary>
    private const int OnsetTicks = 30;

    /// <summary>How many ticks each run lasts after the first one, 1 s at 60 Hz.</summary>
    private const int Ticks = 60;

    /// <summary>
    /// The default membership rules, but with a merge test that reaches only the bounding spheres, without the lookahead of
    /// 2 s and the split margin of 10 m. The ship's interior is a static body at the ship's origin, an anchor that keeps
    /// every body that the merge test links to it in the ship's own system (docs/plans/numeric-frames.md, "Anchors"). With
    /// the default reach, 2 s of the crate's 60 m/s, the interior would keep the crate there until it left the bubble. With
    /// this reach, the crate is clear of the interior after the step of the throw, 1 m beyond the 5.2 m where their spheres
    /// touch, so it falls in a numeric frame of its own below the ship, which is the case that these tests are about.
    /// </summary>
    private static readonly NumericFrameOptions WithoutLookahead = new() { LookaheadTime = 0, SplitMargin = 0 };

    /// <summary>
    /// A world on the wrapper with the numeric frames of <see cref="WithoutLookahead"/> or, when
    /// <paramref name="numericFrames"/> is false, with none.
    /// </summary>
    private static World NewWorld(bool numericFrames, out FaultInjectingBackend faults) =>
        NewWorldWithFaults(out faults, configure: options => options.NumericFrames = numericFrames ? WithoutLookahead : WithoutNumericFrames);

    /// <summary>A hull of 4 m by 4 m by 4 m and 1000 kg that nothing slows down and that does not bounce.</summary>
    private static readonly BodyDesc ShipHull = new(new BoxShapeDesc(new Vector3(2)), Mass: 1000, Restitution: 0, LinearDamping: 0, AngularDamping: 0);

    /// <summary>A crate of 2 m by 2 m by 2 m and 2 kg that nothing slows down and that does not bounce.</summary>
    private static readonly BodyDesc CrateBody = new(new BoxShapeDesc(new Vector3(1)), Mass: 2, Restitution: 0, LinearDamping: 0, AngularDamping: 0);

    /// <summary>The ship scene.</summary>
    /// <param name="Station">The parked station beside the ship, or null when the scene has none.</param>
    /// <param name="StationCrate">The crate at rest in the station, or null when the scene has none.</param>
    /// <param name="CrateFrames">Every FrameChanged that the thrown crate hears.</param>
    /// <param name="CratePilot">The pilot that throws the crate (see <see cref="WarmUpAndThrow"/>).</param>
    /// <param name="ThrowVelocity">The velocity of the throw relative to the ship, in m/s.</param>
    private sealed record ShipScene(FrameHandle Space, Entity ShipOwner, FrameHandle Ship, Entity Crate, FrameChangedLog CrateFrames,
        FrameHandle Station, Entity StationCrate, Pilot CratePilot, Double3 ThrowVelocity);

    /// <summary>How the ship moves in space.</summary>
    /// <param name="Velocity">The ship's velocity in space, in m/s.</param>
    /// <param name="Thrusts">True when the ship's hull carries a gentle thruster along its velocity, so that it thrusts from
    /// the sync point of its thirtieth tick on.</param>
    private readonly record struct ShipMotion(Vector3 Velocity, bool Thrusts);

    /// <summary>
    /// Space, with no gravity, and in it a ship: a hull of 4 m by 4 m by 4 m and 1000 kg that physics drives, at the origin
    /// of space, with the velocity of <paramref name="shipMotion"/> in space and a gentle thruster when it says so, a bubble
    /// of radius 30 m around it, and the field <paramref name="shipGravity"/> inside, or none. A crate of 2 m by 2 m by 2 m
    /// and 2 kg rests 5 m from the ship's center along <paramref name="throwDirection"/>, with a gentle thruster along it.
    /// <see cref="WarmUpAndThrow"/> throws it along that direction at 60 m/s relative to the ship. Its bounding sphere has a
    /// radius of about 1.73 m. So it sticks out of the bubble from about 0.39 s after the throw on, and it is fully outside
    /// the bubble plus the exit margin of 1.73 m after about 0.47 s. When <paramref name="withStation"/> holds, a parked
    /// station sits at (60, 0, 0) in space: a frame that physics does not drive, with a static hull of 4 m by 4 m by 4 m at
    /// its center and a bubble of radius 40 m, which overlaps the ship's. A crate rests at (-28, 0, 0) in the station, 32 m
    /// from the ship's center, outside the ship's bubble.
    /// </summary>
    private static ShipScene CrateThrownInAShip(World world, ShipMotion shipMotion, Double3 throwDirection, bool withStation = false,
        GravityField shipGravity = default)
    {
        FrameHandle space = world.CreateRootFrame(new FrameSpec(FrameBounds.Sphere(1e7f), MotionDriver.Static));
        Entity shipOwner = world.Spawn(new SpawnDesc
        {
            Frame = space,
            Pose = At(0),
            LinearVelocity = shipMotion.Velocity,
            Body = ShipHull,
            AsFrame = new FrameSpec(FrameBounds.Sphere(ShipRadius), MotionDriver.Physics, shipGravity),
            Components = shipMotion.Thrusts ? [new Thruster(ShipHull.Mass) { Direction = Vector3.Normalize(shipMotion.Velocity) }] : [],
        });
        FrameHandle ship = world.GetOwnedFrame(shipOwner);
        var crateFrames = new FrameChangedLog();
        var cratePilot = new Pilot();
        Entity crate = world.Spawn(new SpawnDesc
        {
            Frame = ship,
            Pose = new DPose(throwDirection * 5, DQuaternion.Identity),
            Body = CrateBody,
            Components = [crateFrames, new Thruster(CrateBody.Mass) { Direction = throwDirection.ToVector3() }, cratePilot],
        });
        FrameHandle station = FrameHandle.Null;
        Entity stationCrate = Entity.Null;
        if (withStation)
        {
            Entity stationOwner = world.Spawn(new SpawnDesc
            {
                Frame = space,
                Pose = At(60),
                Body = new BodyDesc(new BoxShapeDesc(new Vector3(2)), BodyMotion.Static),
                AsFrame = new FrameSpec(FrameBounds.Sphere(40), MotionDriver.Static),
            });
            station = world.GetOwnedFrame(stationOwner);
            stationCrate = world.Spawn(new SpawnDesc { Frame = station, Pose = At(-28), Body = CrateBody });
        }
        return new ShipScene(space, shipOwner, ship, crate, crateFrames, station, stationCrate, cratePilot, throwDirection * ThrowSpeed);
    }

    /// <summary>
    /// Runs the ship scene for <see cref="OnsetTicks"/> ticks, at whose last sync point the crate, and the ship's hull when
    /// it carries a thruster, start to thrust. Then it ticks once more, in which the crate's pilot sets its velocity to the
    /// throw. The step of the next tick throws the crate, and the sync point of that tick gives it a falling frame: the
    /// caller's first tick after this one is the tick of the throw.
    /// </summary>
    private static void WarmUpAndThrow(World world, ShipScene scene)
    {
        Tick(world, OnsetTicks);
        Double3 throwVelocity = scene.ThrowVelocity;
        scene.CratePilot.Once = body => body.SetVelocity(throwVelocity);
        world.Tick();
    }

    /// <summary>Checks that the thrown crate falls in a numeric frame, as it does from the tick of the throw on.</summary>
    private static void CrateShouldFall(World world, ShipScene scene, string because) =>
        world.Frames.Numeric(world.PhysicsHomeOf(scene.Crate)).Should().Be(NumericMode.Falling, because);

    /// <summary>
    /// Checks that the thrown crate falls in a numeric frame below the ship with numeric frames, and is in the system of
    /// the ship without.
    /// </summary>
    private static void CrateHomeShouldBe(World world, ShipScene scene, bool numericFrames)
    {
        FrameHandle home = world.PhysicsHomeOf(scene.Crate);
        if (numericFrames)
        {
            world.Frames.Numeric(home).Should().Be(NumericMode.Falling,
                "the crate thrusts and flies at 60 m/s relative to the ship, so it gets a falling numeric frame");
            world.Frames.SemanticOf(home).Should().Be(scene.Ship, "the crate's falling frame is a frame of the ship");
        }
        else
        {
            home.Should().Be(scene.Ship);
        }
        world.GetFrame(scene.Crate).Should().Be(scene.Ship);
    }


    /// <summary>What a run of the ship scene saw, tick by tick, after its first tick.</summary>
    /// <param name="ShipTreeParent">The ship's tree parent after the first tick: space, or a numeric frame of space.</param>
    /// <param name="GhostTicks">The ticks after which the crate had a ghost in space.</param>
    /// <param name="TreeParentGhostTicks">The ticks after which the crate had a ghost in the ship's tree parent, when that is not space.</param>
    /// <param name="LeftAt">The tick after which the crate was first in space, or -1.</param>
    /// <param name="Position">The crate's position in space at the end of the run.</param>
    /// <param name="Velocity">The crate's velocity in space at the end of the run.</param>
    /// <param name="Seen">Every FrameChanged that the crate heard.</param>
    /// <param name="HandOffsOut">The hand-offs of the crate into space or into the ship's tree parent.</param>
    private sealed record Run(FrameHandle Space, FrameHandle Ship, FrameHandle ShipTreeParent, List<int> GhostTicks,
        List<int> TreeParentGhostTicks, int LeftAt, Double3 Position, Double3 Velocity, List<FrameChanged> Seen, List<HandOff> HandOffsOut);

    /// <summary>
    /// Throws the crate in the ship scene (see <see cref="WarmUpAndThrow"/>), runs it for 61 ticks from the throw on, and
    /// records, after each tick but the first, whether the crate has a ghost in space, whether it has a ghost in the ship's
    /// tree parent when that is a numeric frame, and whether it has left the ship. It checks after each tick that the crate
    /// has no ghost in the ship, its own semantic frame, and, with numeric frames, that it falls in a numeric frame until it
    /// leaves.
    /// </summary>
    private static Run ThrowTheCrate(bool numericFrames, ShipMotion shipMotion, Double3 throwDirection, GravityField shipGravity = default)
    {
        using World world = NewWorld(numericFrames, out FaultInjectingBackend faults);
        ShipScene scene = CrateThrownInAShip(world, shipMotion, throwDirection, shipGravity: shipGravity);
        WarmUpAndThrow(world, scene);
        world.Tick();
        CrateHomeShouldBe(world, scene, numericFrames);
        FrameHandle shipTreeParent = world.Frames.TreeParent(scene.Ship);

        var ghostTicks = new List<int>();
        var treeParentGhostTicks = new List<int>();
        int leftAt = -1;
        for (int tick = 1; tick <= Ticks; tick++)
        {
            world.Tick();
            faults.Jolt.HasGhostIn(scene.Crate, scene.Ship).Should().BeFalse(
                $"after tick {tick} the crate is in the ship or in space, and the bubble that it sticks out of is the ship's, not a sibling's");
            if (faults.Jolt.HasGhostIn(scene.Crate, scene.Space))
                ghostTicks.Add(tick);
            if (shipTreeParent != scene.Space && faults.Jolt.HasGhostIn(scene.Crate, shipTreeParent))
                treeParentGhostTicks.Add(tick);
            if (leftAt < 0 && world.GetFrame(scene.Crate) == scene.Space)
                leftAt = tick;
            if (numericFrames && leftAt < 0)
                CrateShouldFall(world, scene, $"after tick {tick} the crate still falls in its numeric frame below the ship");
        }

        world.Errors.Should().BeEmpty();
        List<HandOff> handOffsOut = faults.Outputs.SelectMany(output => output.HandOffs)
            .Where(h => h.Entity == scene.Crate && (h.To == scene.Space || h.To == shipTreeParent)).ToList();
        return new Run(scene.Space, scene.Ship, shipTreeParent, ghostTicks, treeParentGhostTicks, leftAt,
            world.PoseIn(scene.Crate, scene.Space).Position, world.VelocityIn(scene.Crate, scene.Space), scene.CrateFrames.Seen, handOffsOut);
    }

    /// <summary>The ship rests at the origin of space.</summary>
    private static readonly ShipMotion AtRest = new(Vector3.Zero, Thrusts: false);

    /// <summary>
    /// The ship drifts at 20 m/s along +Y without thrust, slower than the 1,000 m/s from which a free hull falls, so its hull
    /// stays in the system of space.
    /// </summary>
    private static readonly ShipMotion Drift = new(new Vector3(0, 20, 0), Thrusts: false);

    /// <summary>
    /// The ship flies at 60 m/s along -X with a gentle thrust along it, faster than the 50 m/s from which a hull that thrusts
    /// falls, so its hull falls in a numeric frame of space from the sync point of its thirtieth tick on, before the throw.
    /// </summary>
    private static readonly ShipMotion Fall = new(new Vector3(-60, 0, 0), Thrusts: true);

    /// <summary>Checks that the crate had a ghost in space exactly while it stuck out of the ship's bubble, before it left.</summary>
    private static void GhostInSpaceWhileItSticksOut(Run run)
    {
        // The crate sticks out from 28.27 m from the ship's center on, after 0.39 s, and leaves after 0.47 s.
        run.GhostTicks.Should().NotBeEmpty("the crate sticks out of the ship's bubble before it leaves");
        run.GhostTicks.Count.Should().BeInRange(4, 6, "the crate sticks out for about 0.09 s");
        run.GhostTicks.Should().Equal(Enumerable.Range(run.GhostTicks[0], run.GhostTicks.Count), "the crate sticks out until it leaves");
        run.GhostTicks[^1].Should().Be(run.LeftAt - 1, "the real crate replaces its ghost in space when it leaves");
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void ACrateThrownFastInsideAShipLeavesTheShipsBubbleIntoSpaceAndHearsFrameChanged(bool numericFrames)
    {
        Run run = ThrowTheCrate(numericFrames, Drift, new Double3(1, 0, 0));

        // The crate is fully outside the bubble plus the exit margin, 33.46 m from the ship's center, after 0.47 s.
        run.LeftAt.Should().BeInRange(28, 30, "the crate leaves the ship's bubble after about 0.47 s");
        run.Seen.Should().ContainSingle("the crate's semantic frame changes once");
        run.Seen[0].From.Should().Be(run.Ship);
        run.Seen[0].To.Should().Be(run.Space);
        run.HandOffsOut.Should().ContainSingle("one hand-off moves the crate out of the ship")
            .Which.To.Should().Be(run.Space, "the ship's hull is in the system of space");
        GhostInSpaceWhileItSticksOut(run);
        (run.Velocity - new Double3(ThrowSpeed, 20, 0)).Length.Should().BeLessThan(1e-2, "nothing slows the crate down");
    }

    /// <summary>The ship's field: uniform gravity of 9.81 m/s² along -Y.</summary>
    private static readonly GravityField ShipGravity = GravityField.Uniform(new Vector3(0, -9.81f, 0));

    /// <summary>
    /// The hand-off uses instant velocities. In the ship's gravity, the crate and its falling frame accelerate, so their
    /// instant velocity at the end of a step is half a step of gravity, about 0.08 m/s, faster than their chord, the change
    /// of position over the step divided by its length. Space has no gravity, so the crate keeps the velocity that it
    /// leaves with.
    /// </summary>
    [Fact]
    public void ACrateThrownFastInsideAShipLeavesItAtTheSameTickPlaceAndVelocityAsWithoutNumericFrames()
    {
        Run withNumericFrames = ThrowTheCrate(numericFrames: true, Drift, new Double3(1, 0, 0), ShipGravity);
        Run withoutNumericFrames = ThrowTheCrate(numericFrames: false, Drift, new Double3(1, 0, 0), ShipGravity);

        withoutNumericFrames.Velocity.Y.Should().BeInRange(20 - 9.81 * 0.55, 20 - 9.81 * 0.4, "the crate fell in the ship's gravity for about 0.5 s");
        withoutNumericFrames.LeftAt.Should().BeInRange(28, 30);
        withNumericFrames.LeftAt.Should().Be(withoutNumericFrames.LeftAt,
            "the crate leaves the bubble of its home's semantic frame, although its home is a numeric frame");
        withNumericFrames.GhostTicks.Should().Equal(withoutNumericFrames.GhostTicks, "the crate sticks out of the same bubble");
        (withNumericFrames.Position - withoutNumericFrames.Position).Length.Should().BeLessThan(1e-3);
        (withNumericFrames.Velocity - withoutNumericFrames.Velocity).Length.Should().BeLessThan(1e-2);
    }

    /// <summary>The one ghost of <paramref name="entity"/> in the system of <paramref name="frame"/>.</summary>
    private static JoltPhysicsBackend.BodyFacts GhostOf(FaultInjectingBackend faults, Entity entity, FrameHandle frame) =>
        faults.Jolt.BodiesIn(frame).Single(facts => BodyUserData.Kind(facts.UserData) == BodyKind.Ghost && BodyUserData.Names(facts.UserData, entity));

    /// <summary>
    /// A ghost follows the chord of its real body. In the ship's gravity, the crate's falling frame accelerates, so its
    /// instant velocity at the end of a step is about 0.08 m/s faster than its chord.
    /// </summary>
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void TheGhostInSpaceOfACrateThatSticksOutOfAShipHasTheCratesPlaceAndItsVelocityOverTheLastStep(bool numericFrames)
    {
        using World world = NewWorld(numericFrames, out FaultInjectingBackend faults);
        ShipScene scene = CrateThrownInAShip(world, Drift, new Double3(1, 0, 0), shipGravity: ShipGravity);
        WarmUpAndThrow(world, scene);
        world.Tick();
        CrateHomeShouldBe(world, scene, numericFrames);
        TickUntil(world, () => faults.Jolt.HasGhostIn(scene.Crate, scene.Space), Ticks);
        if (numericFrames)
            CrateShouldFall(world, scene, "the crate that sticks out of the ship falls in its numeric frame");

        JoltPhysicsBackend.BodyFacts ghost = GhostOf(faults, scene.Crate, scene.Space);
        Double3 velocity = world.VelocityIn(scene.Crate, scene.Space);
        velocity.Y.Should().BeLessThan(20 - 3, "the crate has fallen in the ship's gravity for about 0.4 s");
        (ghost.Position - world.PoseIn(scene.Crate, scene.Space).Position).Length.Should().BeLessThan(1e-5,
            "the ghost is where the crate is in space");
        ((Double3)ghost.LinearVelocity - velocity).Length.Should().BeLessThan(1e-4, "the ghost moves in space with the crate's chord");
        world.Errors.Should().BeEmpty();
    }

    /// <summary>
    /// The ship flies in a falling frame of space. The crate's falling frame hangs below the ship, which hangs below the
    /// hull's falling frame. The crate that sticks out of the ship has a ghost in that falling frame, the ship's tree
    /// parent, and in space, the ship's semantic parent, whose own system holds the bodies that the merge test does not
    /// link to the hull. It leaves into the ship's tree parent, as a body in the ship's own system does.
    /// </summary>
    [Fact]
    public void ACrateThrownFastInsideAFallingShipHasAGhostInSpaceAndLeavesTheShipAsItDoesWithoutNumericFrames()
    {
        Run withNumericFrames = ThrowTheCrate(numericFrames: true, Fall, new Double3(0, 1, 0));
        Run withoutNumericFrames = ThrowTheCrate(numericFrames: false, Fall, new Double3(0, 1, 0));

        withoutNumericFrames.ShipTreeParent.Should().Be(withoutNumericFrames.Space);
        withoutNumericFrames.LeftAt.Should().BeInRange(28, 30);
        GhostInSpaceWhileItSticksOut(withoutNumericFrames);
        withNumericFrames.ShipTreeParent.Should().NotBe(withNumericFrames.Space, "the ship's hull falls in a numeric frame");
        withNumericFrames.LeftAt.Should().Be(withoutNumericFrames.LeftAt);
        withNumericFrames.GhostTicks.Should().Equal(withoutNumericFrames.GhostTicks, "the crate has a ghost in space while it sticks out");
        withNumericFrames.TreeParentGhostTicks.Should().Equal(withNumericFrames.GhostTicks,
            "the crate is partly in the ship's tree parent while it sticks out");
        withNumericFrames.HandOffsOut.Should().ContainSingle().Which.To.Should().Be(withNumericFrames.ShipTreeParent,
            "the crate leaves into the ship's tree parent");
        withNumericFrames.Seen.Should().ContainSingle().Which.To.Should().Be(withNumericFrames.Space);
        withoutNumericFrames.Seen.Should().ContainSingle().Which.To.Should().Be(withoutNumericFrames.Space);
        (withNumericFrames.Position - withoutNumericFrames.Position).Length.Should().BeLessThan(1e-3);
        (withNumericFrames.Velocity - withoutNumericFrames.Velocity).Length.Should().BeLessThan(1e-2);
    }

    /// <summary>
    /// Runs the ship scene with the ship at rest and the station beside it, with the crate thrown along +X at the
    /// station's crate, until the station's crate moves at more than 10 m/s relative to the station, at most 60 ticks after
    /// the tick of the throw. With numeric frames, it checks after each of those ticks until the push that the thrown crate
    /// falls in its numeric frame. Returns the ticks that it ran, the state of the station's crate then, relative to the
    /// station, and the place of the thrown crate there.
    /// </summary>
    private static (int Ticks, Double3 Position, Double3 Velocity, Double3 PusherPosition) PushTheStationsCrate(bool numericFrames)
    {
        using World world = NewWorld(numericFrames, out _);
        ShipScene scene = CrateThrownInAShip(world, AtRest, new Double3(1, 0, 0), withStation: true);
        WarmUpAndThrow(world, scene);
        world.Tick();
        CrateHomeShouldBe(world, scene, numericFrames);

        int ticks = TickUntil(world, () =>
        {
            bool pushed = world.VelocityIn(scene.StationCrate, scene.Station).Length > 10;
            if (numericFrames && !pushed)
                CrateShouldFall(world, scene, "the thrown crate falls in its numeric frame until its ghost pushes the station's crate");
            return pushed;
        }, Ticks);

        world.Errors.Should().BeEmpty();
        return (ticks, world.PoseIn(scene.StationCrate, scene.Station).Position, world.VelocityIn(scene.StationCrate, scene.Station),
            world.PoseIn(scene.Crate, scene.Station).Position);
    }

    [Fact]
    public void ACrateThatSticksOutOfAShipFromANumericFramePushesACrateInAStationBesideTheShipAsItDoesWithoutNumericFrames()
    {
        var withNumericFrames = PushTheStationsCrate(numericFrames: true);
        var withoutNumericFrames = PushTheStationsCrate(numericFrames: false);

        // The front of the thrown crate reaches the back of the station's, 31 m from the ship's center, 0.42 s after the
        // throw. The thrown crate then sticks out of the ship's bubble and touches the station's, a sibling of the ship.
        withoutNumericFrames.Ticks.Should().BeInRange(22, 28, "the ghost of the thrown crate in the station reaches the station's crate after about 0.42 s");
        withoutNumericFrames.Velocity.X.Should().BeGreaterThan(10, "the ghost pushed the crate along +X");
        (withoutNumericFrames.Position.X - withoutNumericFrames.PusherPosition.X).Should().BeGreaterThan(1.9,
            "the station's crate is in front of the thrown one, which did not pass through it");
        withNumericFrames.Ticks.Should().Be(withoutNumericFrames.Ticks,
            "the thrown crate has a ghost at the same place in the station, a semantic sibling of its home's semantic frame");
        (withNumericFrames.Position - withoutNumericFrames.Position).Length.Should().BeLessThan(1e-3, "the ghost pushed the crate to the same place");
        (withNumericFrames.Velocity - withoutNumericFrames.Velocity).Length.Should().BeLessThan(1e-2, "the ghost moves at the same velocity");
    }

    /// <summary>A frame with no body, in <paramref name="parent"/>, at <paramref name="pose"/>, with a bubble of <paramref name="radius"/> m.</summary>
    private static FrameHandle EmptyFrame(World world, FrameHandle parent, DPose pose, float radius) =>
        world.GetOwnedFrame(world.Spawn(new SpawnDesc { Frame = parent, Pose = pose, AsFrame = new FrameSpec(FrameBounds.Sphere(radius), MotionDriver.Static) }));

    /// <summary>
    /// The reconcile at the start of a step drops each ghost that the frame tree does not allow, and the ghost pass at the
    /// end of the step makes a missing ghost again, so the step would run without it. The test runs the reconcile alone, by
    /// applying an empty change batch after a change of the tree far away, and reads the backend before the next step. The
    /// crate sticks out of a ship at rest beside the station, or out of a falling ship.
    /// </summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void TheGhostsOfACrateThatSticksOutOfAShipFromANumericFrameSurviveAReconcile(bool shipFalls)
    {
        using World world = NewWorld(numericFrames: true, out FaultInjectingBackend faults);
        ShipScene scene = shipFalls
            ? CrateThrownInAShip(world, Fall, new Double3(0, 1, 0))
            : CrateThrownInAShip(world, AtRest, new Double3(1, 0, 0), withStation: true);
        FrameHandle buoy = EmptyFrame(world, scene.Space, At(-5000), 5);
        FrameHandle depot = EmptyFrame(world, scene.Space, At(5000), 50);
        WarmUpAndThrow(world, scene);
        world.Tick();
        CrateHomeShouldBe(world, scene, numericFrames: true);
        // The ship's tree parent, or the station beside the ship.
        FrameHandle other = shipFalls ? world.Frames.TreeParent(scene.Ship) : scene.Station;
        if (shipFalls)
            world.Frames.Numeric(other).Should().Be(NumericMode.Falling, "the ship's hull thrusts at 60 m/s, so it falls in a numeric frame");
        bool HasBothGhosts() => faults.Jolt.HasGhostIn(scene.Crate, scene.Space) && faults.Jolt.HasGhostIn(scene.Crate, other);
        TickUntil(world, HasBothGhosts, Ticks);
        HasBothGhosts().Should().BeTrue("about 0.4 s after the throw the crate sticks out of the ship's bubble");
        CrateShouldFall(world, scene, "the crate that sticks out of the ship falls in its numeric frame");

        // A change far away makes the backend reconcile. The ship is still a semantic child of space.
        world.Frames.Reparent(buoy, depot);
        world.Frames.Update();
        faults.Jolt.ApplyChanges(new PhysicsChanges(), world.Frames);

        faults.Jolt.HasGhostIn(scene.Crate, scene.Space).Should().BeTrue(
            "the crate sticks out of the ship, the semantic frame of its home, so it is partly in space, the ship's semantic parent");
        faults.Jolt.HasGhostIn(scene.Crate, other).Should().BeTrue(shipFalls
            ? "the crate is partly in the ship's tree parent"
            : "the crate touches the bubble of the station, a semantic sibling of the ship");
        world.Tick();
        world.Errors.Should().BeEmpty();
    }
}
