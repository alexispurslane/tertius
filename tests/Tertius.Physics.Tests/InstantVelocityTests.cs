using System.Numerics;
using Tertius.Simulation;
using static Tertius.Testing.WorldTesting;

namespace Tertius.Physics.Tests;

/// <summary>
/// A velocity that gameplay gives, at a spawn, through SetVelocity, with a teleport, by a jump or at a hand-off, is an
/// instant velocity: the velocity at that moment. Jolt reads a stored velocity as a chord: the displacement over the last
/// step divided by the step, which is the velocity half a step earlier. Physics gives Jolt the chord of each instant
/// velocity (ARCHITECTURE.md, "Instant velocities and chords"), so a body moves as its instant velocity says, on the
/// World and the real backend. Every expected path is worked out here in doubles from the instant state: a parabola under
/// uniform gravity, a straight line without gravity, and an exponential decay of a velocity or a spin under damping.
/// </summary>
public class InstantVelocityTests
{
    /// <summary>Half the side of <see cref="Crate"/>, in meters.</summary>
    private const float CrateHalf = 0.1f;

    /// <summary>The strength of the uniform gravity, in m/s², as the frame holds it: a float.</summary>
    private static readonly double G = 9.81f;

    private static readonly Double3 Down = new(0, -G, 0);

    /// <summary>
    /// A crate of 0.2 m that nothing slows down, that does not bounce and that slides without friction. It is small, so
    /// that Jolt's linear cast catches it in the step in which it reaches the floor at the speed of a 2 m fall: a body that
    /// moves less than 0.75 of its inner radius in a step gets no cast, and Jolt then finds the floor only when the gap at
    /// the start of a step is within its speculative contact distance of 0.02 m, which a 1 m crate at 6 m/s can miss by a
    /// step.
    /// </summary>
    private static readonly BodyDesc Crate =
        new(Shape: new BoxShapeDesc(new(CrateHalf)), Mass: 10, Friction: 0, Restitution: 0, LinearDamping: 0,
            AngularDamping: 0);

    /// <summary>A ball of radius 0.2 m that nothing slows down and that does not bounce.</summary>
    private static readonly BodyDesc Ball = new(Shape: new SphereShapeDesc(0.2f), Mass: 1, Restitution: 0,
        LinearDamping: 0,
        AngularDamping: 0);

    private static readonly BodyDesc Person = new(Shape: new CapsuleShapeDesc(0.6f, 0.3f), Motion: BodyMotion.Character,
        Mass: 70);

    /// <summary>A root frame without gravity.</summary>
    private static FrameHandle Space(World world) =>
        world.CreateRootFrame(new(FrameBounds.Sphere(1e6f)));

    /// <summary>
    /// Gives <paramref name="command"/> to the pilot and ticks once. The tick writes the command into the body's intent, so
    /// the step of that tick does not know it yet. The step of the next tick applies it, from the start of that step.
    /// </summary>
    private static void Send(World world, Pilot pilot, BodyCommand command)
    {
        pilot.Once = command;
        world.Tick();
    }

    /// <summary>Where a body is <paramref name="t"/> s after it was at <paramref name="start"/> with the instant velocity
    /// <paramref name="velocity"/>.</summary>
    private static Double3 Path(Double3 start, Double3 velocity, Double3 acceleration, double t) =>
        start + velocity * t + acceleration * (t * t / 2);

    /// <summary>True when the contact names the two entities, in either order.</summary>
    private static bool Between(in ContactStarted contact, Entity a, Entity b) =>
        (contact.A == a && contact.B == b) || (contact.A == b && contact.B == a);

    /// <summary>The angle between two rotations, in radians.</summary>
    private static double AngleBetween(DQuaternion a, DQuaternion b)
    {
        double dot = Math.Abs(a.X * b.X + a.Y * b.Y + a.Z * b.Z + a.W * b.W);
        return 2 * Math.Acos(Math.Min(1, dot));
    }

    /// <summary>
    /// Where the path through three positions at equal steps of <paramref name="dt"/> s crosses the height
    /// <paramref name="height"/> after the last of them: the X of the parabola through the three points at that height.
    /// </summary>
    private static double CrossingX(Double3 p0, Double3 p1, Double3 p2, double dt, double height)
    {
        // Around the middle point, y(τ) = p1.Y + b τ + a τ², and the same for X.
        double a = (p2.Y - 2 * p1.Y + p0.Y) / (2 * dt * dt);
        double b = (p2.Y - p0.Y) / (2 * dt);
        double c = p1.Y - height;
        // The path falls, so a < 0, and the later root is the crossing.
        double tau = (-b - Math.Sqrt(b * b - 4 * a * c)) / (2 * a);
        double ax = (p2.X - 2 * p1.X + p0.X) / (2 * dt * dt);
        double bx = (p2.X - p0.X) / (2 * dt);
        return p1.X + bx * tau + ax * tau * tau;
    }

    [Fact]
    public void ACrateThrownAt3MetersPerSecondLandsWhereAndWhenItsInstantVelocitySays()
    {
        using World world = NewWorldWithFaults(faults: out FaultInjectingBackend faults);
        FrameHandle ground = Ground(world, 9.81f);
        Entity floor = Floor(world, ground);
        var pilot = new Pilot();
        // The crate waits on the floor, far from where it is thrown.
        Entity crate = world.Spawn(new()
            { Frame = ground, Pose = At(-20, CrateHalf), Body = Crate, Components = [pilot] });
        Tick(world, 30);

        // A hand throws it along X at 3 m/s with its bottom 2 m above the floor: a teleport and a velocity in one intent.
        var hand = new Double3(0, 2 + CrateHalf, 0);
        var throwVelocity = new Double3(3, 0, 0);
        Send(world, pilot, body =>
        {
            body.Teleport(new(hand, DQuaternion.Identity));
            body.SetVelocity(throwVelocity);
        });

        double dt = world.DeltaTime;
        // Its bottom falls 2 m in sqrt(2 · 2 m / g) = 0.639 s, while it moves 1.916 m along X.
        double landingTime = Math.Sqrt(2 * 2.0 / G);
        double landingX = throwVelocity.X * landingTime;
        var flight = new List<Double3>();
        int landingTick = -1, landings = 0;
        for (int tick = 1; tick <= 120; tick++)
        {
            world.Tick();
            int touches = faults.Outputs[^1].Contacts.Count(contact => Between(contact, floor, crate));
            landings += touches;
            if (touches > 0 && landingTick < 0)
                landingTick = tick;
            double t = tick * dt;
            if (t < landingTime)
            {
                Double3 position = world.PoseInOwnFrame(crate).Position;
                (position - Path(start: hand, velocity: throwVelocity, acceleration: Down, t: t)).Length.Should()
                    .BeLessThan(1e-4, "the crate flies on its parabola at {0} s", t);
                flight.Add(position);
            }
        }

        CrossingX(p0: flight[^3], p1: flight[^2], p2: flight[^1], dt: dt, height: CrateHalf).Should()
            .BeApproximately(landingX, 1e-4, "the crate lands where the parabola meets the floor");
        (landingTick * dt).Should().BeInRange(landingTime - dt, landingTime + dt,
            "the landing is reported within one tick of when it happens");
        landings.Should().Be(1, "the crate lands once and slides on");
        world.Errors.Should().BeEmpty();
    }

    [Fact]
    public void ABodySpawnedWithAVelocityMovesOnTheParabolaOfThatInstantVelocity()
    {
        using World world = NewWorld(backend: out _);
        FrameHandle ground = Ground(world, 9.81f);
        var start = new Double3(0, 100, 0);
        var velocity = new Double3(2, 5, -1);
        Entity crate = world.Spawn(new()
            { Frame = ground, Pose = new(start, DQuaternion.Identity), LinearVelocity = velocity, Body = Crate });

        double dt = world.DeltaTime;
        for (int tick = 1; tick <= 90; tick++)
        {
            world.Tick();
            (world.PoseInOwnFrame(crate).Position -
             Path(start: start, velocity: velocity, acceleration: Down, t: tick * dt)).Length
                .Should().BeLessThan(1e-4, "the crate is on its parabola after {0} ticks", tick);
        }

        world.Errors.Should().BeEmpty();
    }

    [Fact]
    public void ABodyGivenAVelocityInFlightMovesOnTheParabolaOfThatInstantVelocity()
    {
        using World world = NewWorld(backend: out _);
        FrameHandle ground = Ground(world, 9.81f);
        var pilot = new Pilot();
        var start = new Double3(0, 100, 0);
        // Spawned at rest, it falls from its spawn place: that is also an instant velocity, of zero.
        Entity crate = world.Spawn(new()
            { Frame = ground, Pose = new(start, DQuaternion.Identity), Body = Crate, Components = [pilot] });
        Tick(world, 9);
        var velocity = new Double3(-1, 4, 2.5);
        Send(world, pilot, body => body.SetVelocity(velocity));

        double dt = world.DeltaTime;
        // The step after the command starts 10 ticks after the spawn, and the new velocity holds from its start.
        double set = 10 * dt;
        Double3 from = Path(start: start, velocity: Double3.Zero, acceleration: Down, t: set);
        for (int tick = 1; tick <= 90; tick++)
        {
            world.Tick();
            (world.PoseInOwnFrame(crate).Position -
             Path(start: from, velocity: velocity, acceleration: Down, t: tick * dt)).Length
                .Should().BeLessThan(1e-4, "the crate is on the parabola of its new velocity after {0} ticks", tick);
        }

        world.Errors.Should().BeEmpty();
    }

    [Fact]
    public void ABodyTeleportedIntoTheAirFallsFromRestThere()
    {
        using World world = NewWorld(backend: out _);
        FrameHandle ground = Ground(world, 9.81f);
        Floor(world, ground);
        var pilot = new Pilot();
        Entity crate = world.Spawn(new()
            { Frame = ground, Pose = At(-20, CrateHalf), Body = Crate, Components = [pilot] });
        Tick(world, 30);
        // A teleport leaves the body at rest at the new place: an instant velocity of zero.
        var top = new Double3(0, 50, 0);
        Send(world, pilot, body => body.Teleport(new(top, DQuaternion.Identity)));

        double dt = world.DeltaTime;
        for (int tick = 1; tick <= 60; tick++)
        {
            world.Tick();
            (world.PoseInOwnFrame(crate).Position -
             Path(start: top, velocity: Double3.Zero, acceleration: Down, t: tick * dt)).Length
                .Should().BeLessThan(1e-4, "the crate falls from rest after {0} ticks", tick);
        }

        world.Errors.Should().BeEmpty();
    }

    [Fact]
    public void ACharacterTeleportedIntoTheAirFallsFromRestThere()
    {
        using World world = NewWorld(backend: out _);
        FrameHandle ground = Ground(world, 9.81f);
        var pilot = new Pilot();
        // It falls from high up, and a teleport puts it higher still. (A character teleported off the ground keeps the
        // ground of its old place for one update, so it would hang there for a step.)
        Entity person = world.Spawn(new() { Frame = ground, Pose = At(-20, 200), Body = Person, Components = [pilot] });
        Tick(world, 30);
        var top = new Double3(0, 300, 0);
        Send(world, pilot, body => body.Teleport(new(top, DQuaternion.Identity)));

        double dt = world.DeltaTime;
        for (int tick = 1; tick <= 60; tick++)
        {
            world.Tick();
            (world.PoseInOwnFrame(person).Position -
             Path(start: top, velocity: Double3.Zero, acceleration: Down, t: tick * dt)).Length
                .Should().BeLessThan(1e-4, "the character falls from rest after {0} ticks", tick);
        }

        world.Errors.Should().BeEmpty();
    }

    [Fact]
    public void ASleepingBodyGivenAVelocityOfZeroSleepsOn()
    {
        using World world = NewWorld(backend: out JoltPhysicsBackend backend);
        FrameHandle ground = Ground(world, 9.81f);
        Floor(world, ground);
        var pilot = new Pilot();
        Entity crate =
            world.Spawn(new() { Frame = ground, Pose = At(0, CrateHalf), Body = Crate, Components = [pilot] });
        world.Tick();
        TickUntil(world, () => !IsAwake(backend, ground, crate), 180).Should()
            .BeLessThan(180, "the crate falls asleep on the floor");

        // The chord of an instant velocity of zero under gravity is not zero, but a body that sleeps through the step gets
        // no gravity, so its chord is zero too, and it does not wake.
        Send(world, pilot, body => body.SetVelocity(Double3.Zero));
        world.Tick();

        IsAwake(backend, ground, crate).Should().BeFalse("a velocity of zero leaves a sleeping body asleep");
        world.Errors.Should().BeEmpty();
    }

    [Fact]
    public void ABodySetMovingSlowsAsItsInstantVelocityAndItsDampingSay()
    {
        using World world = NewWorld(backend: out _);
        FrameHandle space = Space(world);
        // Jolt slows a body by its linear damping: v(t) = v₀ e^(−k t), so it travels v₀ (1 − e^(−k t)) / k.
        const float damping = 0.05f;
        var pilot = new Pilot();
        Entity crate = world.Spawn(new()
            { Frame = space, Pose = At(0), Body = Crate with { LinearDamping = damping }, Components = [pilot] });
        world.Tick();
        var velocity = new Double3(2, 0, -1);
        Send(world, pilot, body => body.SetVelocity(velocity));

        double dt = world.DeltaTime;
        double k = damping;
        for (int tick = 1; tick <= 60; tick++)
        {
            world.Tick();
            Double3 expected = velocity * ((1 - Math.Exp(-k * tick * dt)) / k);
            (world.PoseInOwnFrame(crate).Position - expected).Length.Should().BeLessThan(1e-4,
                "the crate has slowed as its damping says after {0} ticks", tick);
        }

        world.Errors.Should().BeEmpty();
    }

    [Fact]
    public void ABallThatFliesThroughARoomWithItsOwnGravityKeepsToItsInstantPathInsideAndAfter()
    {
        using World world = NewWorld(backend: out _);
        FrameHandle space = Space(world);
        // A room 20 m out along X, turned by 0.4 rad about Z, with 9.81 m/s² of gravity along its own -Y. Space has none.
        var roomPose = new DPose(new(20, 0, 0), DQuaternion.FromAxisAngle(Double3.UnitZ, 0.4));
        FrameHandle room = world.GetOwnedFrame(world.Spawn(new()
        {
            Frame = space,
            Pose = roomPose,
            AsFrame = new(FrameBounds.Sphere(5), GravityField.Uniform(new(0, -9.81f, 0)))
        }));
        var start = new Double3(10, 0, 0);
        var velocity = new Double3(6, 1, 0);
        Entity ball = world.Spawn(new()
            { Frame = space, Pose = new(start, DQuaternion.Identity), LinearVelocity = velocity, Body = Ball });

        double dt = world.DeltaTime;
        // In space it moves on a straight line, until a hand-off at the end of a step moves it into the room with its state then.
        int tick = 0;
        while (world.GetFrame(ball) == space && tick < 300)
        {
            world.Tick();
            tick++;
            if (world.GetFrame(ball) == space)
                (world.PoseInOwnFrame(ball).Position - (start + velocity * (tick * dt))).Length.Should()
                    .BeLessThan(1e-4);
        }

        world.GetFrame(ball).Should().Be(room, "the ball enters the room");
        double entered = tick * dt;
        Double3 roomStart = roomPose.InverseTransformPoint(start + velocity * entered);
        Double3 roomVelocity = roomPose.InverseTransformDirection(velocity);

        // In the room it moves on the parabola of that state under the room's gravity, until it leaves.
        while (world.GetFrame(ball) == room && tick < 600)
        {
            world.Tick();
            tick++;
            if (world.GetFrame(ball) == room)
                (world.PoseInOwnFrame(ball).Position - Path(start: roomStart, velocity: roomVelocity,
                        acceleration: Down, t: tick * dt - entered)).Length
                    .Should().BeLessThan(1e-4, "the ball is on its parabola in the room at tick {0}", tick);
        }

        world.GetFrame(ball).Should().Be(space, "the ball leaves the room");
        double left = tick * dt;
        Double3 exitPosition = roomPose.TransformPoint(Path(start: roomStart, velocity: roomVelocity,
            acceleration: Down, t: left - entered));
        Double3 exitVelocity = roomPose.TransformDirection(roomVelocity + Down * (left - entered));

        // Out in space again, it moves on a straight line with its instant velocity at the hand-off.
        for (int after = 0; after <= 30; after++)
        {
            (world.PoseInOwnFrame(ball).Position - (exitPosition + exitVelocity * (after * dt))).Length
                .Should().BeLessThan(1e-4, "the ball moves straight on {0} ticks after it left", after);
            world.Tick();
        }

        world.Errors.Should().BeEmpty();
    }

    [Fact]
    public void AFastBallThatFallsInANumericFrameEntersARoomOnTheParabolaOfItsSpawn()
    {
        using World world = NewWorld(backend: out _);
        FrameHandle ground = Ground(world, 9.81f);
        // A room 100 m out along X and 6 m down, turned about the vertical. It holds still in the ground's gravity, so its
        // contents feel that gravity with no field of the room's own.
        var roomPose = new DPose(new(100, -6, 0), DQuaternion.FromAxisAngle(Double3.UnitY, 0.5));
        FrameHandle room = world.GetOwnedFrame(world.Spawn(new()
        {
            Frame = ground,
            Pose = roomPose,
            AsFrame = new(FrameBounds.Sphere(20))
        }));
        // A group with a body that thrusts falls in a numeric frame from 50 m/s, and a free body only from 1,000 m/s. So the
        // ball carries a gentle thruster, which pushes it up along the vertical, about which the room is turned, so that the
        // thrust is the same in the room's axes as in the ground's. It thrusts from the sync point of its thirtieth tick on
        // (30 ticks of 1/60 s reach the 0.5 s of NumericFrameOptions.ThrustOnset). At 60 m/s it then gets a falling numeric
        // frame of its own, which falls at g, and it enters the room from that frame about 50 ticks later.
        const int onsetTicks = 30;
        var start = new Double3(0, 0, 0);
        var velocity = new Double3(60, 2, 0);
        Entity ball = world.Spawn(new()
        {
            Frame = ground,
            Pose = new(start, DQuaternion.Identity),
            LinearVelocity = velocity,
            Body = Ball,
            Components = [new Thruster(Ball.Mass) { Direction = Vector3.UnitY }]
        });
        // The thrust of a tick acts in the step of the next tick, so the ball runs one step behind this path: by about
        // 1e-4 m/s² · 1/60 s · 2 s = 3e-6 m after 2 s.
        Double3 acceleration = Down + new Double3(0, Thruster.GentleAcceleration, 0);

        double dt = world.DeltaTime;
        int inRoom = 0;
        for (int tick = 1; tick <= 120; tick++)
        {
            world.Tick();
            if (world.GetFrame(ball) == room)
                inRoom++;
            else if (inRoom == 0 && tick >= onsetTicks)
                world.Frames.Numeric(world.PhysicsHomeOf(ball)).Should().Be(NumericMode.Falling,
                    "from its thirtieth tick until it enters the room, the ball falls in a numeric frame (tick {0})",
                    tick);
            (world.PoseIn(ball, ground).Position -
             Path(start: start, velocity: velocity, acceleration: acceleration, t: tick * dt)).Length
                .Should().BeLessThan(1e-4, "the ball is on the parabola of its spawn after {0} ticks", tick);
        }

        inRoom.Should().BeGreaterThan(20, "the ball flies through the room");
        world.Errors.Should().BeEmpty();
    }

    [Fact]
    public void AShipSpawnedWithAVelocityMovesOnTheParabolaOfThatInstantVelocity()
    {
        using World world = NewWorld(backend: out _);
        FrameHandle ground = Ground(world, 9.81f);
        // A frame that physics drives: its hull is a dynamic body in the ground's system, which gets the spawn's velocity.
        var start = new Double3(0, 100, 0);
        var velocity = new Double3(5, 3, 0);
        FrameHandle ship = world.GetOwnedFrame(world.Spawn(new()
        {
            Frame = ground,
            Pose = new(start, DQuaternion.Identity),
            LinearVelocity = velocity,
            Body = new(Shape: new BoxShapeDesc(new(2)), Mass: 1000, LinearDamping: 0, AngularDamping: 0),
            AsFrame = new(FrameBounds.Sphere(10))
        }));

        double dt = world.DeltaTime;
        for (int tick = 1; tick <= 60; tick++)
        {
            world.Tick();
            (world.Frames.LocalPose(ship).Position -
             Path(start: start, velocity: velocity, acceleration: Down, t: tick * dt)).Length
                .Should().BeLessThan(1e-4, "the ship is on its parabola after {0} ticks", tick);
        }

        world.Errors.Should().BeEmpty();
    }

    [Fact]
    public void ACharacterThatWalksOutOfAFrameLeavesItWithItsWalkingVelocity()
    {
        using World world = NewWorldWithFaults(faults: out FaultInjectingBackend faults);
        GravityField gravity = GravityField.Uniform(new(0, -9.81f, 0));
        FrameHandle ground = world.CreateRootFrame(new(FrameBounds.Sphere(1e6f), gravity));
        Floor(world, ground);
        // A building with the same gravity, whose floor reaches past its bubble, so the walker has ground under it all the way.
        FrameHandle building = world.GetOwnedFrame(world.Spawn(new()
        {
            Frame = ground,
            Pose = At(0),
            AsFrame = new(FrameBounds.Sphere(10), gravity)
        }));
        world.Spawn(new()
        {
            Frame = building, Pose = At(0, -0.5),
            Body = new(Shape: new BoxShapeDesc(new(15, 0.5f, 15)), Motion: BodyMotion.Static)
        });
        var pilot = new Pilot { EveryTick = body => body.MoveCharacter(new(4, 0, 0)) };
        world.Spawn(new() { Frame = building, Pose = At(0, 0.95), Body = Person, Components = [pilot] });

        TickUntil(world, () => faults.Outputs.Any(output => output.HandOffs.Count > 0), 300);

        // The ground holds the walker in both frames, so its chord is its instant velocity: no gravity in it.
        HandOff exit = faults.Outputs.SelectMany(output => output.HandOffs).Should().ContainSingle().Which;
        exit.To.Should().Be(ground);
        Vector3.Distance(exit.LinearVelocity, new(4, 0, 0)).Should()
            .BeLessThan(1e-3f, "the walker leaves at its walking velocity");
        world.Errors.Should().BeEmpty();
    }

    [Fact]
    public void ATumblingCubeGivenANewSpinTurnsAsThatInstantAngularVelocitySays()
    {
        using World world = NewWorld(backend: out _);
        FrameHandle space = Space(world);
        // Jolt slows a spin by its angular damping: ω(t) = ω₀ e^(−k t), so the cube turns by |ω₀| (1 − e^(−k t)) / k.
        const float damping = 0.05f;
        var cubeBody = new BodyDesc(Shape: new BoxShapeDesc(new(0.5f)), Mass: 10, LinearDamping: 0,
            AngularDamping: damping);
        var pilot = new Pilot();
        Entity cube = world.Spawn(new()
        {
            Frame = space, Pose = At(0), AngularVelocity = new(0.3f, 2, -0.5f), Body = cubeBody, Components = [pilot]
        });
        Tick(world, 10);

        // A hand catches the tumbling cube at a known attitude and gives it a new spin about a slanted axis, in one intent.
        DQuaternion attitude = DQuaternion.FromAxisAngle(Double3.UnitX, 0.3);
        var spin = new Vector3(1.2f, -0.7f, 1.6f);
        Send(world, pilot, body =>
        {
            body.Teleport(new(Double3.Zero, attitude));
            body.SetAngularVelocity(spin);
        });

        double dt = world.DeltaTime;
        double k = damping;
        Double3 axis = ((Double3)spin).Normalized();
        double rate = ((Double3)spin).Length;
        for (int tick = 1; tick <= 60; tick++)
        {
            world.Tick();
            double angle = rate * (1 - Math.Exp(-k * tick * dt)) / k;
            DQuaternion expected = DQuaternion.FromAxisAngle(axis, angle) * attitude;
            AngleBetween(world.PoseInOwnFrame(cube).Rotation, expected).Should().BeLessThan(1e-4,
                "the cube has turned as its spin says after {0} ticks", tick);
        }

        world.Errors.Should().BeEmpty();
    }

    [Fact]
    public void ACharacterGivenAVelocityInTheAirMovesOnTheParabolaOfThatInstantVelocity()
    {
        using World world = NewWorld(backend: out _);
        FrameHandle ground = Ground(world, 9.81f);
        var pilot = new Pilot();
        // High above everything, so the character is in the air the whole time. Spawned at rest, it falls from there.
        var start = new Double3(0, 200, 0);
        Entity person = world.Spawn(new()
            { Frame = ground, Pose = new(start, DQuaternion.Identity), Body = Person, Components = [pilot] });
        Tick(world, 9);
        var velocity = new Double3(1, 3, -0.5);
        Send(world, pilot, body => body.SetVelocity(velocity));

        double dt = world.DeltaTime;
        double set = 10 * dt;
        Double3 from = Path(start: start, velocity: Double3.Zero, acceleration: Down, t: set);
        for (int tick = 1; tick <= 60; tick++)
        {
            world.Tick();
            (world.PoseInOwnFrame(person).Position -
             Path(start: from, velocity: velocity, acceleration: Down, t: tick * dt)).Length
                .Should()
                .BeLessThan(1e-4, "the character is on the parabola of its new velocity after {0} ticks", tick);
        }

        world.Errors.Should().BeEmpty();
    }

    [Fact]
    public void ACharacterThatJumpsRisesOnTheParabolaOfItsJumpSpeed()
    {
        using World world = NewWorld(backend: out _);
        FrameHandle ground = Ground(world, 9.81f);
        Floor(world, ground);
        var pilot = new Pilot();
        Entity person = world.Spawn(new() { Frame = ground, Pose = At(0, 1.0), Body = Person, Components = [pilot] });
        Tick(world, 60);
        Double3 standing = world.PoseInOwnFrame(person).Position;

        const float jumpSpeed = 5;
        Send(world, pilot, body => body.Jump(jumpSpeed));

        double dt = world.DeltaTime;
        // It leaves the ground with the jump speed at the start of the next step, and is back down after 2 · 5 / g = 1.02 s.
        for (int tick = 1; tick <= 50; tick++)
        {
            world.Tick();
            (world.PoseInOwnFrame(person).Position - Path(start: standing, velocity: new(0, jumpSpeed, 0),
                    acceleration: Down, t: tick * dt)).Length
                .Should().BeLessThan(1e-4, "the character is on the parabola of its jump after {0} ticks", tick);
        }

        world.Errors.Should().BeEmpty();
    }
}
