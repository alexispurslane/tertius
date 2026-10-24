using JoltPhysicsSharp;
using Tertius.Simulation;
using static Tertius.Testing.ContactNews;
using static Tertius.Testing.WorldTesting;
using static Tertius.Testing.NumericFrameTesting;

namespace Tertius.Physics.Tests;

/// <summary>
/// The ghosts of static meshes (ter-dq92), on the World and the real backend. A static mesh has a ghost as the hull of a
/// parked frame in each bubble that it touches. Before ter-dhkf it also had a ghost as an anchor of a falling numeric
/// frame; now the merge test links a fast body to a static mesh, and the body joins the mesh's home (docs/plans/
/// numeric-frames.md, "Anchors"), so the scenes of a body that flies at a mesh wall check that. A ghost stands for its real
/// body, so a body that hits the ghost must meet a body that moves as the real one does relative to the ghost's frame.
/// Numeric frames must not change which bodies a body meets, how it moves, or what gameplay hears, so each scene with
/// numeric frames runs beside the same scene without them.
/// </summary>
public class MeshGhostTests
{
    /// <summary>The step at whose start the body of <see cref="FlyAtAMeshWall"/> is its gap from the wall.</summary>
    private const int ContactStep = 40;

    // ---- The hull of a parked station, whose mesh ghost holds still or moves ----

    private const float G = 9.81f;

    /// <summary>A static wall without mass: a closed mesh cube of 2 m.</summary>
    private static readonly BodyDesc MeshWall = new(Shape: MeshCube(1), Motion: BodyMotion.Static, Mass: 0);

    /// <summary>A crate of 10 kg, a box of 1 m that nothing slows down on its own.</summary>
    private static readonly BodyDesc Crate = new(Shape: new BoxShapeDesc(new(0.5f)), Mass: 10, LinearDamping: 0,
        AngularDamping: 0);

    /// <summary>A ball of 1 kg with a radius of 0.5 m and a restitution of 0.5, which nothing slows down on its own.</summary>
    private static readonly BodyDesc BouncyBall = new(Shape: new SphereShapeDesc(0.5f), Mass: 1, Restitution: 0.5f,
        LinearDamping: 0, AngularDamping: 0);

    /// <summary>
    /// What a body that flew at a mesh wall did: its place and velocity in the ground frame at the end, what it heard of
    /// the wall, the ticks of its first Collision with the wall and of its first CollisionEnded, and that Collision's impulse.
    /// </summary>
    private sealed record WallRun(
        string Scene,
        Double3 Position,
        Double3 Velocity,
        List<ContactNews> Heard,
        long? StartTick,
        long? EndTick,
        float Impulse);

    /// <summary>
    /// With no gravity, a body of <paramref name="body"/> flies along +X at <paramref name="speed"/> m/s at a static wall:
    /// a closed mesh cube of 2 m at the origin, whose face toward the body lies at x = -1 m. A static body needs no mass,
    /// so the wall has none, and Jolt cannot compute one for its mesh either. The body's front lies
    /// <paramref name="reach"/> m ahead of its center, and the body starts so that its front is <paramref name="gap"/> m
    /// from the wall at the start of step <see cref="ContactStep"/>. The run lasts 30 steps longer. The body thrusts gently
    /// along its flight from its spawn on (see <see cref="Thruster"/>), so under the default rules it is fast enough for a
    /// falling frame of its own from tick 30 on, before the contact step. A twin of the body, with the same velocity and
    /// thruster, flies 1 km to the side, out of the wall's reach.
    /// With numeric frames, under <paramref name="numeric"/>, the twin falls in a numeric frame of its own, and so would the
    /// body, but the merge test links it to the wall, an anchor, from the first sync point on, so the body joins the wall's
    /// home, the ground frame's own system, and meets the real wall there (ter-dhkf). No frame keeps a ghost of the wall. The
    /// run checks all three before the contact step.
    /// </summary>
    private static WallRun FlyAtAMeshWall(bool numericFrames, NumericFrameOptions numeric, BodyDesc body, double reach,
        double speed, double gap)
    {
        using World world = NewWorldWithFaults(faults: out FaultInjectingBackend faults,
            configure: options => options.NumericFrames = numericFrames ? numeric : WithoutNumericFrames);
        FrameHandle ground = Ground(world, 0);
        var wallLog = new CollisionLog();
        Entity wall = world.Spawn(new() { Frame = ground, Body = MeshWall, Components = [wallLog] });
        var log = new CollisionLog();
        double start = -1 - reach - gap - ContactStep * speed * world.DeltaTime;
        Entity mover = world.Spawn(new()
        {
            Frame = ground, Pose = At(start), LinearVelocity = new(speed, 0, 0), Body = body,
            Components = [log, new Thruster(body.Mass)]
        });
        Entity twin = world.Spawn(new()
        {
            Frame = ground, Pose = At(start, 1_000), LinearVelocity = new(speed, 0, 0), Body = body,
            Components = [new Thruster(body.Mass)]
        });

        Tick(world, ContactStep - 1);
        world.Frames.Numeric(world.PhysicsHomeOf(twin)).Should().Be(
            numericFrames ? NumericMode.Falling : NumericMode.None,
            "the twin of the body, which no anchor links, falls in a numeric frame of its own, when there are numeric frames");
        world.PhysicsHomeOf(mover).Should()
            .Be(ground, "the body joins the wall's home, with numeric frames or without");
        faults.Jolt.GhostCount(wall).Should().Be(0, "the wall is a real body of the body's system");
        log.Seen.Should().BeEmpty("the body has not reached the wall yet");
        Tick(world, 31);

        world.Errors.Should().BeEmpty();
        wallLog.NewsOf(mover).Should().Equal(log.NewsOf(wall), "the wall hears what the body hears");
        List<(long Tick, Entity Other, float Impulse)> seen = [.. log.Seen.Where(s => s.Other == wall)];
        List<(long Tick, Entity Other)> ended = [.. log.Ended.Where(e => e.Other == wall)];
        return new(Scene: numericFrames ? "with numeric frames" : "without numeric frames",
            Position: world.PoseIn(mover, ground).Position, Velocity: world.VelocityIn(mover, ground),
            Heard: log.NewsOf(wall),
            StartTick: seen.Count > 0 ? seen[0].Tick : null, EndTick: ended.Count > 0 ? ended[0].Tick : null,
            Impulse: seen.Count > 0 ? seen[0].Impulse : 0);
    }

    /// <summary>
    /// A crate flies at a static mesh wall (see <see cref="FlyAtAMeshWall"/>) and stops there, with numeric frames as
    /// without them. In the first case the default rules hold, the crate flies at 60 m/s, and its front is 1 cm from the
    /// wall at the start of the contact step, within Jolt's speculative contact distance, so Jolt stops it at the wall in
    /// that step. In the second every body that moves would fall, the crate flies at 20 m/s, and its front is 17 cm from the
    /// wall at the start of the contact step, so it sinks up to 17 cm into the wall in that step, and Jolt pushes it out
    /// again. The crate joins the wall's home, so it meets the real wall. Before ter-dhkf, the crate fell in a frame of its
    /// own, where the ghost of the wall came at it at 60 m/s or 20 m/s, and that ghost had to be a body that Jolt moves with
    /// that velocity.
    /// </summary>
    [Theory]
    [InlineData(false, 60.0, 0.01)]
    [InlineData(true, 20.0, 0.17)]
    public void ACrateThatFliesAtAStaticMeshWallJoinsItsHomeAndStopsAtItAsItDoesWithoutNumericFrames(
        bool everyBodyFalls, double speed, double gap)
    {
        NumericFrameOptions numeric = everyBodyFalls ? EveryGroupFalls : new();
        WallRun without = FlyAtAMeshWall(numericFrames: false, numeric: numeric, body: Crate, reach: 0.5, speed: speed,
            gap: gap);
        WallRun with = FlyAtAMeshWall(numericFrames: true, numeric: numeric, body: Crate, reach: 0.5, speed: speed,
            gap: gap);

        foreach (WallRun run in new[] { without, with })
        {
            run.Position.X.Should().BeApproximately(-1.5, 0.03, "the crate stops with its front at the wall's face {0}",
                run.Scene);
            run.Velocity.Length.Should().BeLessThan(0.05, "the crate stopped at the wall {0}", run.Scene);
            run.Heard.Should().Equal([Started], "the crate hears one Collision {0}", run.Scene);
            run.Impulse.Should().BeApproximately((float)(10 * speed), (float)(10 * speed) * 0.05f,
                "the crate of 10 kg stopped from its speed {0}", run.Scene);
        }

        with.Position.X.Should().BeApproximately(without.Position.X, 0.01);
        with.StartTick.Should().Be(without.StartTick);
        with.Impulse.Should().BeApproximately(without.Impulse, without.Impulse * 0.02f);
    }

    /// <summary>
    /// A ball of restitution 0.5 flies at a static mesh wall at 60 m/s (see <see cref="FlyAtAMeshWall"/>) and bounces off it
    /// at 30 m/s, with numeric frames as without them. Its front is 1 cm from the wall at the start of the contact step,
    /// so Jolt bounces it in that step. Jolt bounces a contact by the approach speed that it simulates, and the minimum
    /// rebound reads it too (see FrameWorld.KeptRestitution). The ball joins the wall's home, so it meets the real wall.
    /// Before ter-dhkf, the ball rested in a falling frame of its own and the ghost of the wall came at it at 60 m/s, so
    /// Jolt had to see the ghost move. The ball hears one Collision with the wall and, after the bounce, one
    /// CollisionEnded, in the same ticks either way.
    /// </summary>
    [Fact]
    public void ABallThatFliesAtAStaticMeshWallJoinsItsHomeAndBouncesOffItAsItDoesWithoutNumericFrames()
    {
        var numeric = new NumericFrameOptions();
        WallRun without = FlyAtAMeshWall(numericFrames: false, numeric: numeric, body: BouncyBall, reach: 0.5,
            speed: 60, gap: 0.01);
        WallRun with = FlyAtAMeshWall(numericFrames: true, numeric: numeric, body: BouncyBall, reach: 0.5, speed: 60,
            gap: 0.01);

        foreach (WallRun run in new[] { without, with })
        {
            run.Velocity.X.Should()
                .BeApproximately(-30, 0.5, "the ball bounced off the wall at half its speed {0}", run.Scene);
            run.Position.X.Should().BeLessThan(-1.5 - 20 * 0.5, "the ball flew back from the wall {0}", run.Scene);
            run.Heard.Should().Equal([Started, Ended], "the ball hears one Collision and one CollisionEnded {0}",
                run.Scene);
        }

        with.Velocity.X.Should().BeApproximately(without.Velocity.X, 0.05);
        with.Position.X.Should().BeApproximately(without.Position.X, 0.01);
        with.StartTick.Should().Be(without.StartTick);
        with.EndTick.Should().Be(without.EndTick);
        with.Impulse.Should().BeApproximately(without.Impulse, without.Impulse * 0.02f);
    }

    /// <summary>What <see cref="PushACrateFromRestAtAMeshWall"/> saw: the crate's Collisions with the wall, its place in the ground frame at the end, and v₁ in m/s.</summary>
    private sealed record PushRun(List<(long Tick, Entity Other, float Impulse)> Seen, Double3 Position, double V1);

    /// <summary>
    /// With no gravity and every body that moves in a falling frame of its own, a crate flies at 3 m/s at the static mesh
    /// wall of <see cref="FlyAtAMeshWall"/>, from 4.5 m before it. About 40 cm before the wall, the crate stops in the ground
    /// frame. Then the crate speeds up to v₁ ≈ 20 m/s, so that it is 1.2 cm from the wall at the next sync point, and it
    /// meets the wall in the next step. The merge test links the crate to the wall, so the crate stays in the wall's home
    /// all the time (ter-dhkf). Before, the crate fell in a frame of its own, whose drains stopped and restarted the ghost
    /// of the wall in it, which a drain had to turn from static to kinematic at once.
    /// </summary>
    private static PushRun PushACrateFromRestAtAMeshWall(bool numericFrames)
    {
        using World world = NewWorldWithFaults(faults: out FaultInjectingBackend faults,
            configure: options => options.NumericFrames = numericFrames ? EveryGroupFalls : WithoutNumericFrames);
        FrameHandle ground = Ground(world, 0);
        Entity wall = world.Spawn(new() { Frame = ground, Body = MeshWall });
        var log = new CollisionLog();
        var pilot = new Pilot();
        Entity crate = world.Spawn(new()
        {
            Frame = ground, Pose = At(-6), LinearVelocity = new(3, 0, 0), Body = Crate, Components = [log, pilot]
        });
        double Gap() => -1.5 - world.PoseIn(crate, ground).Position.X;

        TickUntil(world, () => Gap() < 0.45, 200);
        pilot.Once = body => body.SetVelocity(Double3.Zero);
        Tick(world, 6);
        world.VelocityIn(crate, ground).Length.Should().BeLessThan(1e-9, "the crate rests in the ground frame");
        world.PhysicsHomeOf(crate).Should().Be(ground, "the crate stays in the wall's home");
        faults.Jolt.GhostCount(wall).Should().Be(0);

        // The command acts in the step after the next one, and the crate rests until then.
        double v1 = (Gap() - 0.012) / world.DeltaTime;
        pilot.Once = body => body.SetVelocity(new(v1, 0, 0));
        Tick(world, 20);

        world.Errors.Should().BeEmpty();
        world.PhysicsHomeOf(crate).Should().Be(ground);
        return new([.. log.Seen.Where(s => s.Other == wall)], world.PoseIn(crate, ground).Position, v1);
    }

    /// <summary>
    /// A crate that starts again from rest just before a static mesh wall (see <see cref="PushACrateFromRestAtAMeshWall"/>)
    /// meets the wall in the same step and with the same impulse as without numeric frames, and stops there. Before
    /// ter-dhkf, the crate's falling frame kept a ghost of the wall, which rested and was static while the crate rested;
    /// the drain that set the frame moving again had to turn that ghost kinematic at once, or the crate flew 33 cm into
    /// the wall and the contact started a step late. Now the crate joins the wall's home and meets the real wall.
    /// </summary>
    [Fact]
    public void ACrateThatStartsFromRestBeforeAStaticMeshWallStopsAtItInTheSameStepAsWithoutNumericFrames()
    {
        PushRun without = PushACrateFromRestAtAMeshWall(numericFrames: false);
        PushRun with = PushACrateFromRestAtAMeshWall(numericFrames: true);

        without.Seen.Should().ContainSingle();
        without.Seen[0].Impulse.Should().BeApproximately((float)(10 * without.V1), (float)(10 * without.V1) * 0.05f,
            "the crate of 10 kg stopped from v₁");
        with.Seen.Should().ContainSingle();
        with.Seen[0].Tick.Should().Be(without.Seen[0].Tick, "the crate meets the wall in the same step");
        with.Seen[0].Impulse.Should().BeApproximately(without.Seen[0].Impulse, without.Seen[0].Impulse * 0.02f);
        foreach (PushRun run in new[] { without, with })
            run.Position.X.Should().BeApproximately(-1.5, 0.02, "the crate stopped with its front at the wall's face");
        with.Position.X.Should().BeApproximately(without.Position.X, 0.005);
    }

    /// <summary>
    /// Space, with no gravity of its own, and a parked station at its origin whose hull is a floor of triangles of 40 m by
    /// 40 m at y = 0, facing up, with a bubble of 3 m. A ship hangs at rest at (12, 20, 0): a hull that physics drives, a
    /// ball of radius 2 m, with a bubble of 30 m and its own gravity of 1 g along -Y. The ship's bubble takes in the
    /// station's hull, so the ship's system holds a ghost of the hull, whose floor lies at y = -20 m in the ship. A crate
    /// in the ship stands on that floor at (0, -19.5, 0) in the ship. A pilot steers the ship.
    /// </summary>
    private sealed record DockScene(
        FrameHandle Space,
        Entity Station,
        Entity ShipOwner,
        FrameHandle Ship,
        Pilot ShipPilot,
        Entity Crate);

    private static DockScene CrateOnAStationsMeshHull(World world)
    {
        FrameHandle space = world.CreateRootFrame(new(FrameBounds.Sphere(1e7f)));
        var hull = new MeshShapeDesc([new(-20, 0, -20), new(20, 0, -20), new(20, 0, 20), new(-20, 0, 20)],
            [0, 2, 1, 0, 3, 2]);
        Entity station = world.Spawn(new()
        {
            Frame = space,
            Body = new(Shape: hull, Motion: BodyMotion.Static, InteriorShape: new SphereShapeDesc(1)),
            AsFrame = new(FrameBounds.Sphere(3))
        });
        var pilot = new Pilot();
        Entity shipOwner = world.Spawn(new()
        {
            Frame = space,
            Pose = At(12, 20),
            Body = new(Shape: new SphereShapeDesc(2), Mass: 1000, LinearDamping: 0, AngularDamping: 0,
                InteriorShape: new SphereShapeDesc(1)),
            AsFrame = new(FrameBounds.Sphere(30), GravityField.Uniform(new(0, -G, 0))),
            Components = [pilot]
        });
        FrameHandle ship = world.GetOwnedFrame(shipOwner);
        Entity crate = world.Spawn(new() { Frame = ship, Pose = At(0, -19.5), Body = Cube() });
        return new(Space: space, Station: station, ShipOwner: shipOwner, Ship: ship, ShipPilot: pilot, Crate: crate);
    }

    /// <summary>The one ghost of <paramref name="entity"/> in the system of <paramref name="frame"/>, as Jolt holds it.</summary>
    private static JoltPhysicsBackend.BodyFacts
        GhostOf(FaultInjectingBackend faults, Entity entity, FrameHandle frame) =>
        faults.Jolt.BodiesIn(frame).Single(facts =>
            BodyUserData.Kind(facts.UserData) == BodyKind.Ghost && BodyUserData.Names(facts.UserData, entity));

    /// <summary>
    /// The ghost of a mesh is static while it holds still in its system, as its real body does, so that Jolt never
    /// simulates it, and kinematic while it moves, so that Jolt moves it with its velocity (see
    /// <see cref="CrateOnAStationsMeshHull"/>). While the ship hangs at rest beside the station, the ghost of the station's
    /// hull holds still, and the crate on it falls asleep, as on the real hull. When the ship rises at 1 m/s, the ghost
    /// moves down at 1 m/s in the ship, and the crate wakes. When the ship stops again, the ghost holds still again, and
    /// the crate falls asleep again.
    /// </summary>
    [Fact]
    public void TheGhostOfAMeshIsStaticWhileItHoldsStillAndKinematicWhileItMoves()
    {
        using World world = NewWorldWithFaults(faults: out FaultInjectingBackend faults);
        DockScene scene = CrateOnAStationsMeshHull(world);
        Tick(world, 120);

        GhostOf(faults, scene.Station, scene.Ship).Motion.Should()
            .Be(MotionType.Static, "the ship hangs at rest beside the station");
        faults.Jolt.Invoking(backend => backend.AssertInvariants()).Should().NotThrow();
        IsAwake(faults.Jolt, scene.Ship, scene.Crate).Should().BeFalse("the crate stands on a ghost that holds still");
        world.PoseInOwnFrame(scene.Crate).Position.Y.Should().BeApproximately(-19.5, 0.03);

        scene.ShipPilot.Once = body => body.SetVelocity(new(0, 1, 0));
        Tick(world, 3);
        JoltPhysicsBackend.BodyFacts moving = GhostOf(faults, scene.Station, scene.Ship);
        moving.Motion.Should().Be(MotionType.Kinematic, "the ghost moves in the ship's system");
        faults.Jolt.Invoking(backend => backend.AssertInvariants()).Should().NotThrow();
        (moving.LinearVelocity - new Double3(0, -1, 0)).Length.Should()
            .BeLessThan(1e-3, "the hull moves down at 1 m/s in the rising ship");
        IsAwake(faults.Jolt, scene.Ship, scene.Crate).Should().BeTrue("the floor fell away under the crate");

        scene.ShipPilot.Once = body => body.SetVelocity(Double3.Zero);
        Tick(world, 180);
        GhostOf(faults, scene.Station, scene.Ship).Motion.Should()
            .Be(MotionType.Static, "the ship hangs at rest again");
        IsAwake(faults.Jolt, scene.Ship, scene.Crate).Should().BeFalse("the crate stands on the still ghost again");
        world.Errors.Should().BeEmpty();
    }

    /// <summary>
    /// The invariants find a ghost that Jolt holds static although its real body can move: Jolt would hold it at rest
    /// while its real body moves, and only a ghost of a static body turns static while it holds still (see
    /// <see cref="CrateOnAStationsMeshHull"/>). The ship's hull, a ball, touches the station's bubble, so the station's
    /// system holds a kinematic ghost of it. The test turns that ghost static in Jolt, as only a bug would.
    /// </summary>
    [Fact]
    public void TheInvariantsFindAGhostThatJoltHoldsStaticThoughItsRealBodyCanMove()
    {
        using World world = NewWorldWithFaults(faults: out FaultInjectingBackend faults);
        DockScene scene = CrateOnAStationsMeshHull(world);
        world.Tick();
        FrameHandle station = world.GetOwnedFrame(scene.Station);
        JoltPhysicsBackend.BodyFacts ghost = GhostOf(faults, scene.ShipOwner, station);
        ghost.Motion.Should().Be(MotionType.Kinematic);
        faults.Jolt.Invoking(backend => backend.AssertInvariants()).Should().NotThrow();

        faults.Jolt.SystemOf(station)!.BodyInterface.SetMotionType(ghost.Id, MotionType.Static,
            Activation.DontActivate);

        faults.Jolt.Invoking(backend => backend.AssertInvariants()).Should().Throw<ContractException>();
    }

    /// <summary>
    /// A growth of the ship's system keeps the ghost of the station's hull as Jolt held it: static while the ship hangs at
    /// rest, kinematic with its velocity while the ship rises, and in both cases a body that Jolt can turn kinematic (see
    /// <see cref="CrateOnAStationsMeshHull"/>). The ship's system holds at most 4 bodies at first, and static crates fill it.
    /// </summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void AGrowthKeepsTheMotionAndTheStateOfAMeshGhost(bool shipRises)
    {
        using World world =
            WorldTestingGrowth.NewWorldWithFaultsAndSmallSystems(maxBodiesPerFrame: 4,
                faults: out FaultInjectingBackend faults);
        DockScene scene = CrateOnAStationsMeshHull(world);
        Tick(world, 3);
        if (shipRises)
        {
            scene.ShipPilot.Once = body => body.SetVelocity(new(0, 1, 0));
            Tick(world, 3);
        }

        JoltPhysicsBackend.BodyFacts before = GhostOf(faults, scene.Station, scene.Ship);
        before.Motion.Should().Be(shipRises ? MotionType.Kinematic : MotionType.Static);

        uint capacity = faults.Jolt.CapacityOf(scene.Ship);
        int room = (int)capacity - faults.Jolt.BodiesIn(scene.Ship).Count;
        for (int i = 0; i <= room; i++)
            world.Spawn(new()
            {
                Frame = scene.Ship, Pose = At(3 * i - 6, 10),
                Body = new(Shape: new BoxShapeDesc(new(0.5f)), Motion: BodyMotion.Static)
            });
        Entity marker = world.Spawn(new() { Frame = scene.Ship });
        world.Destroy(marker).Should().BeTrue();

        faults.Jolt.CapacityOf(scene.Ship).Should().Be(2 * capacity, "the crates filled the ship's system");
        JoltPhysicsBackend.BodyFacts after = GhostOf(faults, scene.Station, scene.Ship);
        (after.Position - before.Position).Length.Should().BeLessThan(1e-12);
        after.Should().Be(before with { Position = after.Position });
        faults.Jolt.Invoking(backend => backend.AssertInvariants()).Should().NotThrow();
        Tick(world, 2);
        world.Errors.Should().BeEmpty();
    }
}
