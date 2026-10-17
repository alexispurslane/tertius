using System.Numerics;
using JoltPhysicsSharp;
using Tertius.Simulation;
using static Tertius.Testing.ContactNews;
using static Tertius.Testing.WorldTesting;

namespace Tertius.Physics.Tests;

/// <summary>
/// The ghosts of static meshes (ter-dq92), on the World and the real backend. A static mesh has a ghost as an anchor of a
/// falling numeric frame, and as the hull of a parked frame in each bubble that it touches. A ghost stands for its real
/// body, so a body that hits the ghost must meet a body that moves as the real one does relative to the ghost's frame.
/// Numeric frames must not change which bodies a body meets, how it moves, or what gameplay hears, so each scene with
/// numeric frames runs beside the same scene without them.
/// </summary>
public class MeshGhostTests
{
    /// <summary>Membership rules under which no group of bodies ever gets a numeric frame.</summary>
    private static readonly NumericFrameOptions WithoutNumericFrames = new()
    {
        CreateSpeed = double.PositiveInfinity,
        LeaveSpeed = double.PositiveInfinity,
        CreateDistance = double.PositiveInfinity,
        LeaveDistance = double.PositiveInfinity,
    };

    /// <summary>Membership rules under which every body that moves falls in a numeric frame of its own, also a slow one.</summary>
    private static readonly NumericFrameOptions EveryBodyFalls = new() { CreateSpeed = 0, LeaveSpeed = 0 };

    /// <summary>The step at whose start the body of <see cref="FlyAtAMeshWall"/> is its gap from the wall.</summary>
    private const int ContactStep = 40;

    /// <summary>
    /// What a body that flew at a mesh wall did: its place and velocity in the ground frame at the end, what it heard of
    /// the wall, the ticks of its first Collision with the wall and of its first CollisionEnded, and that Collision's impulse.
    /// </summary>
    private sealed record WallRun(string Scene, Double3 Position, Double3 Velocity, List<ContactNews> Heard, long? StartTick, long? EndTick, float Impulse);

    /// <summary>
    /// With no gravity, a body of <paramref name="body"/> flies along +X at <paramref name="speed"/> m/s at a static wall:
    /// a closed mesh cube of 2 m at the origin, whose face toward the body lies at x = -1 m. A static body needs no mass,
    /// so the wall has none, and Jolt cannot compute one for its mesh either. The body's front lies
    /// <paramref name="reach"/> m ahead of its center, and the body starts so that its front is <paramref name="gap"/> m
    /// from the wall at the start of step <see cref="ContactStep"/>. The run lasts 30 steps longer.
    /// With numeric frames, under <paramref name="numeric"/>, the body falls in a numeric frame of its own from the first
    /// sync point on, and the numeric update names the wall an anchor of that frame, which keeps a ghost of the wall. The
    /// ghost comes at the body at the frame's speed. The run checks both before the contact step.
    /// </summary>
    private static WallRun FlyAtAMeshWall(bool numericFrames, NumericFrameOptions numeric, BodyDesc body, double reach, double speed, double gap)
    {
        using World world = NewWorldWithFaults(out FaultInjectingBackend faults,
            configure: options => options.NumericFrames = numericFrames ? numeric : WithoutNumericFrames);
        FrameHandle ground = Ground(world, 0);
        var wallLog = new CollisionLog();
        Entity wall = world.Spawn(new SpawnDesc { Frame = ground, Body = MeshWall, Components = [wallLog] });
        var log = new CollisionLog();
        double start = -1 - reach - gap - ContactStep * speed * world.DeltaTime;
        Entity mover = world.Spawn(new SpawnDesc
        {
            Frame = ground, Pose = At(start), LinearVelocity = new Double3(speed, 0, 0), Body = body, Components = [log],
        });

        Tick(world, ContactStep - 1);
        FrameHandle home = world.PhysicsHomeOf(mover);
        if (numericFrames)
        {
            world.Frames.Numeric(home).Should().Be(NumericMode.Falling, "the body falls in a numeric frame of its own");
            faults.Jolt.HasGhostIn(wall, home).Should().BeTrue("the body's falling frame keeps a ghost of the wall, its anchor");
            // The moving ghost of a mesh is a kinematic body, with a mass that Jolt can use.
            faults.Jolt.Invoking(backend => backend.AssertInvariants()).Should().NotThrow();
        }
        else
        {
            home.Should().Be(ground);
        }
        log.Seen.Should().BeEmpty("the body has not reached the wall yet");
        Tick(world, 31);

        world.Errors.Should().BeEmpty();
        wallLog.NewsOf(mover).Should().Equal(log.NewsOf(wall), "the wall hears what the body hears");
        List<(long Tick, Entity Other, float Impulse)> seen = log.Seen.Where(s => s.Other == wall).ToList();
        List<(long Tick, Entity Other)> ended = log.Ended.Where(e => e.Other == wall).ToList();
        return new WallRun(numericFrames ? "with numeric frames" : "without numeric frames", world.PoseIn(mover, ground).Position, world.VelocityIn(mover, ground), log.NewsOf(wall),
            seen.Count > 0 ? seen[0].Tick : null, ended.Count > 0 ? ended[0].Tick : null, seen.Count > 0 ? seen[0].Impulse : 0);
    }

    /// <summary>A static wall without mass: a closed mesh cube of 2 m.</summary>
    private static readonly BodyDesc MeshWall = new(MeshCube(1), BodyMotion.Static, Mass: 0);

    /// <summary>A crate of 10 kg, a box of 1 m that nothing slows down on its own.</summary>
    private static readonly BodyDesc Crate = new(new BoxShapeDesc(new Vector3(0.5f)), Mass: 10, LinearDamping: 0, AngularDamping: 0);

    /// <summary>A ball of 1 kg with a radius of 0.5 m and a restitution of 0.5, which nothing slows down on its own.</summary>
    private static readonly BodyDesc BouncyBall = new(new SphereShapeDesc(0.5f), Mass: 1, Restitution: 0.5f, LinearDamping: 0, AngularDamping: 0);

    /// <summary>
    /// A crate flies at a static mesh wall (see <see cref="FlyAtAMeshWall"/>) and stops there, with numeric frames as
    /// without them. In the first case the crate falls at 60 m/s and its front is 1 cm from the wall at the start of the
    /// contact step, within Jolt's speculative contact distance, so Jolt stops it at the wall in that step. In the second
    /// every body falls, the crate flies at 20 m/s, and its front is 17 cm from the wall at the start of the contact step,
    /// so it sinks up to 17 cm into the wall in that step, and Jolt pushes it out again. In the crate's falling frame the
    /// ghost of the wall comes at the crate at 60 m/s or 20 m/s. The ghost must be a body that Jolt moves with that
    /// velocity: Jolt does not stop a crate at a static body that the ghost pass moves 1 m or 34 cm into it after each
    /// step, and the crate passes into the hollow mesh.
    /// </summary>
    [Theory]
    [InlineData(false, 60.0, 0.01)]
    [InlineData(true, 20.0, 0.17)]
    public void ACrateInAFallingFrameStopsAtAStaticMeshWallAsItDoesWithoutNumericFrames(bool everyBodyFalls, double speed, double gap)
    {
        NumericFrameOptions numeric = everyBodyFalls ? EveryBodyFalls : new NumericFrameOptions();
        WallRun without = FlyAtAMeshWall(numericFrames: false, numeric, Crate, reach: 0.5, speed, gap);
        WallRun with = FlyAtAMeshWall(numericFrames: true, numeric, Crate, reach: 0.5, speed, gap);

        foreach (WallRun run in new[] { without, with })
        {
            run.Position.X.Should().BeApproximately(-1.5, 0.03, "the crate stops with its front at the wall's face {0}", run.Scene);
            run.Velocity.Length.Should().BeLessThan(0.05, "the crate stopped at the wall {0}", run.Scene);
            run.Heard.Should().Equal([Started], "the crate hears one Collision {0}", run.Scene);
            run.Impulse.Should().BeApproximately((float)(10 * speed), (float)(10 * speed) * 0.05f, "the crate of 10 kg stopped from its speed {0}", run.Scene);
        }
        with.Position.X.Should().BeApproximately(without.Position.X, 0.01);
        with.StartTick.Should().Be(without.StartTick);
        with.Impulse.Should().BeApproximately(without.Impulse, without.Impulse * 0.02f);
    }

    /// <summary>
    /// A ball of restitution 0.5 flies at a static mesh wall at 60 m/s (see <see cref="FlyAtAMeshWall"/>) and bounces off it
    /// at 30 m/s, with numeric frames as without them. Its front is 1 cm from the wall at the start of the contact step,
    /// so Jolt bounces it in that step. Jolt bounces a contact by the approach speed that it simulates, and the minimum
    /// rebound reads it too (see FrameWorld.KeptRestitution). In the ball's falling frame the ball is at rest and the
    /// ghost of the wall comes at it at 60 m/s, so Jolt must see the ghost move: a static ghost that the ghost pass moves
    /// gives no approach, so no bounce. The ball hears one Collision with the wall and, after the bounce, one
    /// CollisionEnded, in the same ticks either way.
    /// </summary>
    [Fact]
    public void ABallInAFallingFrameBouncesOffAStaticMeshWallAsItDoesWithoutNumericFrames()
    {
        var numeric = new NumericFrameOptions();
        WallRun without = FlyAtAMeshWall(numericFrames: false, numeric, BouncyBall, reach: 0.5, speed: 60, gap: 0.01);
        WallRun with = FlyAtAMeshWall(numericFrames: true, numeric, BouncyBall, reach: 0.5, speed: 60, gap: 0.01);

        foreach (WallRun run in new[] { without, with })
        {
            run.Velocity.X.Should().BeApproximately(-30, 0.5, "the ball bounced off the wall at half its speed {0}", run.Scene);
            run.Position.X.Should().BeLessThan(-1.5 - 20 * 0.5, "the ball flew back from the wall {0}", run.Scene);
            run.Heard.Should().Equal([Started, Ended], "the ball hears one Collision and one CollisionEnded {0}", run.Scene);
        }
        with.Velocity.X.Should().BeApproximately(without.Velocity.X, 0.05);
        with.Position.X.Should().BeApproximately(without.Position.X, 0.01);
        with.StartTick.Should().Be(without.StartTick);
        with.EndTick.Should().Be(without.EndTick);
        with.Impulse.Should().BeApproximately(without.Impulse, without.Impulse * 0.02f);
    }

    /// <summary>What <see cref="PushACrateFromRestAtAMeshWall"/> saw: the crate's Collisions with the wall, its place in the ground frame at the end, the motion of the wall's ghost while the crate rested, and v₁ in m/s.</summary>
    private sealed record PushRun(List<(long Tick, Entity Other, float Impulse)> Seen, Double3 Position, MotionType? GhostAtRest, double V1);

    /// <summary>
    /// With no gravity and every body in a falling frame of its own, a crate flies at 3 m/s at the static mesh wall of
    /// <see cref="FlyAtAMeshWall"/>, from 4.5 m before it. About 40 cm before the wall, the crate stops in the ground frame.
    /// Its falling frame drains: it takes on the crate's change of velocity, so the frame now rests in the ground frame,
    /// exactly, and so does the ghost of the wall in it, which is then static. Then the crate speeds up to v₁ ≈ 20 m/s, so
    /// that it is 1.2 cm from the wall at the next sync point, where the frame drains again and moves on at v₁. The ghost
    /// of the wall must then move at v₁ backward in the frame's next step, the step in which the crate meets the wall.
    /// </summary>
    private static PushRun PushACrateFromRestAtAMeshWall(bool numericFrames)
    {
        using World world = NewWorldWithFaults(out FaultInjectingBackend faults,
            configure: options => options.NumericFrames = numericFrames ? EveryBodyFalls : WithoutNumericFrames);
        FrameHandle ground = Ground(world, 0);
        Entity wall = world.Spawn(new SpawnDesc { Frame = ground, Body = MeshWall });
        var log = new CollisionLog();
        var pilot = new Pilot();
        Entity crate = world.Spawn(new SpawnDesc
        {
            Frame = ground, Pose = At(-6), LinearVelocity = new Double3(3, 0, 0), Body = Crate, Components = [log, pilot],
        });
        double Gap() => -1.5 - world.PoseIn(crate, ground).Position.X;

        TickUntil(world, () => Gap() < 0.45, 200);
        pilot.Once = body => body.SetVelocity(Double3.Zero);
        Tick(world, 6);
        world.VelocityIn(crate, ground).Length.Should().BeLessThan(1e-9, "the crate rests in the ground frame");
        MotionType? ghostAtRest = numericFrames ? GhostOf(faults, wall, world.PhysicsHomeOf(crate)).Motion : null;
        // The still ghost of a mesh is a static body that Jolt can turn kinematic.
        faults.Jolt.Invoking(backend => backend.AssertInvariants()).Should().NotThrow();

        // The command acts in the step after the next one, and the crate rests until then.
        double v1 = (Gap() - 0.012) / world.DeltaTime;
        pilot.Once = body => body.SetVelocity(new Double3(v1, 0, 0));
        Tick(world, 20);

        world.Errors.Should().BeEmpty();
        return new PushRun(log.Seen.Where(s => s.Other == wall).ToList(), world.PoseIn(crate, ground).Position, ghostAtRest, v1);
    }

    /// <summary>
    /// A static ghost of a mesh that a drain sets moving moves in the next step (see
    /// <see cref="PushACrateFromRestAtAMeshWall"/>): the crate meets the wall in the same step and with the same impulse as
    /// without numeric frames, and stops there. A shift of the frame gives every ghost in its system the frame's change
    /// of velocity backward, so the static ghost turns kinematic at the shift. A ghost that stayed static until the ghost
    /// pass at the end of that step would let the crate fly 33 cm into the wall, and the contact would start a step late.
    /// </summary>
    [Fact]
    public void AStaticMeshGhostThatADrainSetsMovingStopsACrateInTheNextStepAsWithoutNumericFrames()
    {
        PushRun without = PushACrateFromRestAtAMeshWall(numericFrames: false);
        PushRun with = PushACrateFromRestAtAMeshWall(numericFrames: true);

        with.GhostAtRest.Should().Be(MotionType.Static, "the ghost of the wall rests in the crate's resting frame");
        without.Seen.Should().ContainSingle();
        without.Seen[0].Impulse.Should().BeApproximately((float)(10 * without.V1), (float)(10 * without.V1) * 0.05f, "the crate of 10 kg stopped from v₁");
        with.Seen.Should().ContainSingle();
        with.Seen[0].Tick.Should().Be(without.Seen[0].Tick, "the crate meets the wall in the same step");
        with.Seen[0].Impulse.Should().BeApproximately(without.Seen[0].Impulse, without.Seen[0].Impulse * 0.02f);
        foreach (PushRun run in new[] { without, with })
            run.Position.X.Should().BeApproximately(-1.5, 0.02, "the crate stopped with its front at the wall's face");
        with.Position.X.Should().BeApproximately(without.Position.X, 0.005);
    }

    // ---- The hull of a parked station, whose mesh ghost holds still or moves ----

    private const float G = 9.81f;

    /// <summary>
    /// Space, with no gravity of its own, and a parked station at its origin whose hull is a floor of triangles of 40 m by
    /// 40 m at y = 0, facing up, with a bubble of 3 m. A ship hangs at rest at (12, 20, 0): a hull that physics drives, a
    /// ball of radius 2 m, with a bubble of 30 m and its own gravity of 1 g along -Y. The ship's bubble takes in the
    /// station's hull, so the ship's system holds a ghost of the hull, whose floor lies at y = -20 m in the ship. A crate
    /// in the ship stands on that floor at (0, -19.5, 0) in the ship. A pilot steers the ship.
    /// </summary>
    private sealed record DockScene(FrameHandle Space, Entity Station, Entity ShipOwner, FrameHandle Ship, Pilot ShipPilot, Entity Crate);

    private static DockScene CrateOnAStationsMeshHull(World world)
    {
        FrameHandle space = world.CreateRootFrame(new FrameSpec(FrameBounds.Sphere(1e7f), MotionDriver.Static));
        var hull = new MeshShapeDesc([new(-20, 0, -20), new(20, 0, -20), new(20, 0, 20), new(-20, 0, 20)], [0, 2, 1, 0, 3, 2]);
        Entity station = world.Spawn(new SpawnDesc
        {
            Frame = space,
            Body = new BodyDesc(hull, BodyMotion.Static, InteriorShape: new SphereShapeDesc(1)),
            AsFrame = new FrameSpec(FrameBounds.Sphere(3), MotionDriver.Static),
        });
        var pilot = new Pilot();
        Entity shipOwner = world.Spawn(new SpawnDesc
        {
            Frame = space,
            Pose = At(12, 20),
            Body = new BodyDesc(new SphereShapeDesc(2), Mass: 1000, LinearDamping: 0, AngularDamping: 0, InteriorShape: new SphereShapeDesc(1)),
            AsFrame = new FrameSpec(FrameBounds.Sphere(30), MotionDriver.Physics, GravityField.Uniform(new Vector3(0, -G, 0))),
            Components = [pilot],
        });
        FrameHandle ship = world.GetOwnedFrame(shipOwner);
        Entity crate = world.Spawn(new SpawnDesc { Frame = ship, Pose = At(0, -19.5), Body = Cube() });
        return new DockScene(space, station, shipOwner, ship, pilot, crate);
    }

    /// <summary>The one ghost of <paramref name="entity"/> in the system of <paramref name="frame"/>, as Jolt holds it.</summary>
    private static JoltPhysicsBackend.BodyFacts GhostOf(FaultInjectingBackend faults, Entity entity, FrameHandle frame) =>
        faults.Jolt.BodiesIn(frame).Single(facts => BodyUserData.Kind(facts.UserData) == BodyKind.Ghost && BodyUserData.Names(facts.UserData, entity));

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
        using World world = NewWorldWithFaults(out FaultInjectingBackend faults);
        DockScene scene = CrateOnAStationsMeshHull(world);
        Tick(world, 120);

        GhostOf(faults, scene.Station, scene.Ship).Motion.Should().Be(MotionType.Static, "the ship hangs at rest beside the station");
        faults.Jolt.Invoking(backend => backend.AssertInvariants()).Should().NotThrow();
        IsAwake(faults.Jolt, scene.Ship, scene.Crate).Should().BeFalse("the crate stands on a ghost that holds still");
        world.PoseInOwnFrame(scene.Crate).Position.Y.Should().BeApproximately(-19.5, 0.03);

        scene.ShipPilot.Once = body => body.SetVelocity(new Double3(0, 1, 0));
        Tick(world, 3);
        JoltPhysicsBackend.BodyFacts moving = GhostOf(faults, scene.Station, scene.Ship);
        moving.Motion.Should().Be(MotionType.Kinematic, "the ghost moves in the ship's system");
        faults.Jolt.Invoking(backend => backend.AssertInvariants()).Should().NotThrow();
        ((Double3)moving.LinearVelocity - new Double3(0, -1, 0)).Length.Should().BeLessThan(1e-3, "the hull moves down at 1 m/s in the rising ship");
        IsAwake(faults.Jolt, scene.Ship, scene.Crate).Should().BeTrue("the floor fell away under the crate");

        scene.ShipPilot.Once = body => body.SetVelocity(Double3.Zero);
        Tick(world, 180);
        GhostOf(faults, scene.Station, scene.Ship).Motion.Should().Be(MotionType.Static, "the ship hangs at rest again");
        IsAwake(faults.Jolt, scene.Ship, scene.Crate).Should().BeFalse("the crate stands on the still ghost again");
        world.Errors.Should().BeEmpty();
    }

    /// <summary>
    /// The invariants find a ghost that Jolt holds static although its real body is no mesh: Jolt would hold it at rest
    /// while its real body moves, and only a ghost of a mesh turns kinematic when it starts to move (see
    /// <see cref="CrateOnAStationsMeshHull"/>). The ship's hull, a ball, touches the station's bubble, so the station's
    /// system holds a kinematic ghost of it. The test turns that ghost static in Jolt, as only a bug would.
    /// </summary>
    [Fact]
    public void TheInvariantsFindAGhostThatJoltHoldsStaticThoughItIsNoMesh()
    {
        using World world = NewWorldWithFaults(out FaultInjectingBackend faults);
        DockScene scene = CrateOnAStationsMeshHull(world);
        world.Tick();
        FrameHandle station = world.GetOwnedFrame(scene.Station);
        JoltPhysicsBackend.BodyFacts ghost = GhostOf(faults, scene.ShipOwner, station);
        ghost.Motion.Should().Be(MotionType.Kinematic);
        faults.Jolt.Invoking(backend => backend.AssertInvariants()).Should().NotThrow();

        faults.Jolt.SystemOf(station)!.BodyInterface.SetMotionType(ghost.Id, MotionType.Static, Activation.DontActivate);

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
        using World world = WorldTestingGrowth.NewWorldWithFaultsAndSmallSystems(4, out FaultInjectingBackend faults);
        DockScene scene = CrateOnAStationsMeshHull(world);
        Tick(world, 3);
        if (shipRises)
        {
            scene.ShipPilot.Once = body => body.SetVelocity(new Double3(0, 1, 0));
            Tick(world, 3);
        }
        JoltPhysicsBackend.BodyFacts before = GhostOf(faults, scene.Station, scene.Ship);
        before.Motion.Should().Be(shipRises ? MotionType.Kinematic : MotionType.Static);

        uint capacity = faults.Jolt.CapacityOf(scene.Ship);
        int room = (int)capacity - faults.Jolt.BodiesIn(scene.Ship).Count;
        for (int i = 0; i <= room; i++)
            world.Spawn(new SpawnDesc { Frame = scene.Ship, Pose = At(3 * i - 6, 10), Body = new BodyDesc(new BoxShapeDesc(new Vector3(0.5f)), BodyMotion.Static) });
        Entity marker = world.Spawn(new SpawnDesc { Frame = scene.Ship });
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
