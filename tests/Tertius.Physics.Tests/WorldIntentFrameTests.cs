using System.Numerics;
using Tertius.Simulation;
using static Tertius.Testing.WorldTesting;

namespace Tertius.Physics.Tests;

/// <summary>
/// An intent is relative to the frame that its entity was in when gameplay wrote it. Physics applies it in the step of the
/// next tick, and the sync point between the two can move the entity to another frame: the step of the same tick hands the
/// body into a child's bubble or out of it, or the destroy batch moves it out of a frame that dies. The intent must still
/// mean what gameplay meant in the frame that it named. These tests run the World on the real backend. Their frames do not
/// move relative to each other, or move at a steady velocity, so that a component converts a path into its own frame
/// exactly, and the frames' relation at any moment is the right one for the conversion.
/// </summary>
public class WorldIntentFrameTests
{
    /// <summary>A root frame without gravity.</summary>
    private static FrameHandle Space(World world) => world.CreateRootFrame(new FrameSpec(FrameBounds.Sphere(1e6f)));

    /// <summary>A static frame without a body in <paramref name="space"/>, at <paramref name="pose"/>, with a bubble of <paramref name="radius"/> m.</summary>
    private static Entity Room(World world, FrameHandle space, DPose pose, float radius, params IComponent[] components) => world.Spawn(new SpawnDesc
    {
        Frame = space,
        Pose = pose,
        AsFrame = new FrameSpec(FrameBounds.Sphere(radius)),
        Components = components,
    });

    /// <summary>A turn that moves every axis: 90 degrees about Z, then 90 degrees about the new X.</summary>
    private static DQuaternion Turned => DQuaternion.FromAxisAngle(Double3.UnitZ, Math.PI / 2) * DQuaternion.FromAxisAngle(Double3.UnitX, Math.PI / 2);

    /// <summary>A kinematic cube with sides of 1 m.</summary>
    private static BodyDesc KinematicCube => new(new BoxShapeDesc(new Vector3(0.5f)), BodyMotion.Kinematic);

    /// <summary>A dynamic cube with sides of 1 m that nothing slows down.</summary>
    private static BodyDesc DynamicCube => new(new BoxShapeDesc(new Vector3(0.5f)), BodyMotion.Dynamic, LinearDamping: 0, AngularDamping: 0);

    /// <summary>
    /// Moves a body along a straight line in <paramref name="space"/>, with no turn: <paramref name="start"/> at time 0 and
    /// <paramref name="velocity"/> on. In every tick it writes the place where the body must be at the end of the step that
    /// applies it, two steps after the moment that the tick reads, in the frame that the body is in, through the frames'
    /// relation that the tick reads: an end pose for a kinematic body, or with <paramref name="steer"/> a steer target for
    /// a dynamic body.
    /// </summary>
    private sealed class Rail(FrameHandle space, Double3 start, Double3 velocity, bool steer = false) : IComponent
    {
        public Double3 At(double time) => start + velocity * time;

        public int Priority => 0;

        public EventMask Wants => EventMask.Of<Tick>();

        public void Handle(Self self, scoped ref EventList events, scoped ref Outbox outbox)
        {
            var place = DPose.Compose(self.Snapshot.Frames.Relative(space, self.Frame), new DPose(At(self.TimeAt(self.Tick + 2)), DQuaternion.Identity));
            if (steer)
                self.Body.SteerTo(place);
            else
                self.MoveKinematic(place);
        }
    }

    /// <summary>Destroys its entity in the tick <paramref name="tick"/>.</summary>
    private sealed class DestroyAt(long tick) : IComponent
    {
        public int Priority => 0;

        public EventMask Wants => EventMask.Of<Tick>();

        public void Handle(Self self, scoped ref EventList events, scoped ref Outbox outbox)
        {
            if (self.Tick == tick)
                self.Destroy();
        }
    }

    /// <summary>
    /// Ticks <paramref name="ticks"/> times, and after each tick from the second on returns how far the body of
    /// <paramref name="entity"/> lies from where <paramref name="rail"/> puts it at that moment, in meters, at the worst
    /// tick.
    /// </summary>
    private static (double Off, long Tick) WorstOffRail(World world, Entity entity, FrameHandle space, Rail rail, int ticks)
    {
        (double Off, long Tick) worst = (0, 0);
        for (int i = 0; i < ticks; i++)
        {
            world.Tick();
            if (world.TickNumber < 2)
                continue;
            double off = (world.PoseIn(entity, space).Position - rail.At(world.Time)).Length;
            if (off > worst.Off)
                worst = (off, world.TickNumber);
        }
        return worst;
    }

    /// <summary>
    /// A kinematic cube that gameplay drives with end poses through a child's bubble stays on its line at the ticks of the
    /// hand-offs, because the end pose of each hand-off's tick converts into the cube's new frame.
    /// </summary>
    [Fact]
    public void AnEndPoseConvertsWhenItsBodyHandsOff()
    {
        using World world = NewWorld(out _);
        FrameHandle space = Space(world);
        FrameHandle room = world.GetOwnedFrame(Room(world, space, new DPose(new Double3(20, 0, 0), Turned), radius: 5));
        // 6 m/s along +X: the cube touches the bubble at x = 14.1, in the step of tick 91, and leaves it fully at x = 26.7.
        var rail = new Rail(space, new Double3(5, 0, 0), new Double3(6, 0, 0));
        var frames = new FrameChangedLog();
        Entity cube = world.Spawn(new SpawnDesc { Frame = space, Pose = new DPose(rail.At(0), DQuaternion.Identity), Body = KinematicCube, Components = [rail, frames] });

        (double off, long tick) = WorstOffRail(world, cube, space, rail, ticks: 300);

        frames.Seen.Select(change => (change.From, change.To)).Should().Equal([(space, room), (room, space)], "the cube passes through the room's bubble");
        off.Should().BeLessThan(1e-6, $"each end pose means the place that gameplay meant in the frame it named; the cube was {off} m off at tick {tick}");
        world.Errors.Should().BeEmpty();
    }

    /// <summary>
    /// Writes the end pose of its entity on a straight line in the entity's own frame, with no turn: <paramref name="start"/>
    /// at time 0 and <paramref name="velocity"/> on, at the end of the step that applies the intent, two steps after the
    /// moment that the tick reads. <see cref="Written"/> keeps the last end pose that it wrote.
    /// </summary>
    private sealed class Cruise(Double3 start, Double3 velocity) : IComponent
    {
        public DPose Written { get; private set; }

        public int Priority => 0;

        public EventMask Wants => EventMask.Of<Tick>();

        public void Handle(Self self, scoped ref EventList events, scoped ref Outbox outbox)
        {
            Written = new DPose(start + velocity * self.TimeAt(self.Tick + 2), DQuaternion.Identity);
            self.MoveKinematic(Written);
        }
    }

    /// <summary>
    /// A kinematic cube that hands off out of a ship that cruises on end poses at 30 m/s gets the end pose of the hand-off's
    /// tick where the ship ends the step that applies it (docs/plans/spin-gravity.md, "V7, the relative motion of two
    /// frames"): an end pose is a pose at the end of the step, so it converts by the ship's end pose of that step, and not by
    /// where the ship stands at the sync point, half a meter behind.
    /// </summary>
    [Fact]
    public void AnEndPoseThatConvertsAtAHandOffOutOfAMovingFrameConvertsByWhereThatFrameEndsTheStep()
    {
        using World world = NewWorld(out _);
        FrameHandle space = Space(world);
        var cruise = new Cruise(Double3.Zero, new Double3(0, 0, 30));
        Entity shipOwner = Room(world, space, DPose.Identity, radius: 5, cruise);
        FrameHandle ship = world.GetOwnedFrame(shipOwner);
        var outward = new Cruise(new Double3(3, 0, 0), new Double3(6, 0, 0));
        Entity cube = world.Spawn(new SpawnDesc { Frame = ship, Pose = new DPose(new Double3(3, 0, 0), DQuaternion.Identity), Body = KinematicCube, Components = [outward] });

        for (int tick = 0; tick < 120 && world.GetFrame(cube) == ship; tick++)
            world.Tick();

        world.GetFrame(cube).Should().Be(space, "the cube left the ship's bubble");
        world.Entities.NextStepIntent(cube).Move.TryGetValue(out EndPose end).Should().BeTrue();
        world.Entities.NextStepIntent(shipOwner).Move.TryGetValue(out EndPose shipEnd).Should().BeTrue();
        Double3 expected = DPose.Compose(shipEnd.Pose, outward.Written).Position;
        (end.Pose.Position - expected).Length.Should().BeLessThan(1e-9, $"the end pose {end.Pose.Position} is not {expected}");
        world.Errors.Should().BeEmpty();
    }

    /// <summary>
    /// A dynamic cube that gameplay steers through a child's bubble stays on its line at the ticks of the hand-offs, because
    /// the steer target of each hand-off's tick converts into the cube's new frame. Nothing but the steer targets acts on the
    /// cube, so physics carries it onto each one.
    /// </summary>
    [Fact]
    public void ASteerTargetConvertsWhenItsBodyHandsOff()
    {
        using World world = NewWorld(out _);
        FrameHandle space = Space(world);
        FrameHandle room = world.GetOwnedFrame(Room(world, space, new DPose(new Double3(20, 0, 0), Turned), radius: 5));
        var rail = new Rail(space, new Double3(5, 0, 0), new Double3(6, 0, 0), steer: true);
        var frames = new FrameChangedLog();
        Entity cube = world.Spawn(new SpawnDesc { Frame = space, Pose = new DPose(rail.At(0), DQuaternion.Identity), Body = DynamicCube, Components = [rail, frames] });

        (double off, long tick) = WorstOffRail(world, cube, space, rail, ticks: 300);

        frames.Seen.Select(change => (change.From, change.To)).Should().Equal([(space, room), (room, space)], "the cube passes through the room's bubble");
        off.Should().BeLessThan(1e-6, $"each steer target means the place that gameplay meant in the frame it named; the cube was {off} m off at tick {tick}");
        world.Errors.Should().BeEmpty();
    }

    [Fact]
    public void AnEndPoseWrittenInTheTickInWhichTheBodysFrameDiesMovesTheBodyWhereGameplayMeantIt()
    {
        using World world = NewWorld(out _);
        FrameHandle space = Space(world);
        const long doomsday = 20;
        Entity roomOwner = Room(world, space, new DPose(new Double3(0, 0, 30), Turned), radius: 50, new DestroyAt(doomsday));
        FrameHandle room = world.GetOwnedFrame(roomOwner);
        var rail = new Rail(space, new Double3(1, 0, 31), new Double3(0, 3, 0));
        var frames = new FrameChangedLog();
        Entity cube = world.Spawn(new SpawnDesc
        {
            Frame = room,
            Pose = new DPose(world.Frames.Relative(space, room).TransformPoint(rail.At(0)), DQuaternion.Identity),
            Body = KinematicCube,
            Components = [rail, frames],
        });

        (double off, long tick) = WorstOffRail(world, cube, space, rail, ticks: 60);

        world.IsAlive(roomOwner).Should().BeFalse();
        frames.Seen.Should().ContainSingle("the destroy batch moves the cube out of the room that dies").Which.From.Should().Be(room);
        off.Should().BeLessThan(1e-6, $"the end pose of the tick of the destroy means the place that gameplay meant in the room; the cube was {off} m off at tick {tick}");
        world.Errors.Should().BeEmpty();
    }

    [Fact]
    public void AnEndPoseWrittenInTheTickInWhichTheBodyLeavesAFrameThatDiesThenMovesTheBodyWhereGameplayMeantIt()
    {
        using World world = NewWorld(out _);
        FrameHandle space = Space(world);
        // The cube leaves the bubble of 5 m when its center is more than its radius of 0.866 m and an exit margin as large
        // outside: 6.73 m from the room's center. At 6 m/s from the center, the step of tick 67 ends 6.8 m out, and the step
        // before 6.7 m. The room dies at the same sync point.
        const long leaving = 67;
        Entity roomOwner = Room(world, space, new DPose(new Double3(0, 0, 30), Turned), radius: 5, new DestroyAt(leaving));
        FrameHandle room = world.GetOwnedFrame(roomOwner);
        var rail = new Rail(space, new Double3(0, 0, 30), new Double3(0, 6, 0));
        Entity cube = world.Spawn(new SpawnDesc
        {
            Frame = room,
            Pose = new DPose(world.Frames.Relative(space, room).TransformPoint(rail.At(0)), DQuaternion.Identity),
            Body = KinematicCube,
            Components = [rail],
        });
        Tick(world, (int)leaving);
        world.GetFrame(cube).Should().Be(room);

        world.Tick();
        world.IsAlive(roomOwner).Should().BeFalse();
        world.GetFrame(cube).Should().Be(space, "the step of the tick in which the room died handed the cube out of its bubble");
        (double off, long tick) = WorstOffRail(world, cube, space, rail, ticks: 10);

        off.Should().BeLessThan(1e-6, $"the end pose of the tick of the hand-off means the place that gameplay meant in the room; the cube was {off} m off at tick {tick}");
        world.Errors.Should().BeEmpty();
    }

    /// <summary>Holds its entity where it stands, with an end pose at its pose in its own frame in every tick.</summary>
    private sealed class Hold : IComponent
    {
        public int Priority => 0;

        public EventMask Wants => EventMask.Of<Tick>();

        public void Handle(Self self, scoped ref EventList events, scoped ref Outbox outbox) => self.MoveKinematic(self.PoseIn(self.Frame));
    }

    /// <summary>
    /// The cube hands off out of a room into a cabin in it in the step of the tick in which the room dies. The destroy batch
    /// converts the cube's end pose from the room into the cabin, by the cabin's motion over the next step, which its owner's
    /// end pose relative to the room gives. The cabin's owner lives in the room, so the same batch moves it into space and
    /// converts its end pose there. Every motion of the batch comes from the intents as gameplay wrote them, while the tree
    /// still holds the cabin in the room, and the intents convert after, so the cube still lands where gameplay meant.
    /// </summary>
    [Fact]
    public void AnEndPoseWrittenInTheTickInWhichTheBodyEntersAChildOfAFrameThatDiesMovesTheBodyWhereGameplayMeantIt()
    {
        long entering = RunCabinScene(doomsday: long.MaxValue, out _, out _, out _, out _, out _);
        entering.Should().BeGreaterThan(10, "the cube enters the cabin after a while");

        RunCabinScene(entering, out World world, out Entity roomOwner, out FrameHandle cabin, out Entity cube, out Rail rail);
        using (world)
        {
            world.IsAlive(roomOwner).Should().BeFalse();
            world.GetFrame(cube).Should().Be(cabin, "the step of the tick in which the room died handed the cube into the cabin");
            FrameHandle space = world.Frames.Parent(cabin);
            world.Frames.Parent(space).IsNull.Should().BeTrue("the cabin moved into space when the room died");
            (double off, long tick) = WorstOffRail(world, cube, space, rail, ticks: 10);

            off.Should().BeLessThan(1e-6, $"the end pose of the tick of the hand-off means the place that gameplay meant in the room; the cube was {off} m off at tick {tick}");
            world.Errors.Should().BeEmpty();
        }
    }

    /// <summary>
    /// A room in space at (0, 0, 30), turned by <see cref="Turned"/>, that dies in the tick <paramref name="doomsday"/>, and a
    /// cabin held still in it whose center is at (0, 0, 50) in space, with a bubble of 5 m, and a kinematic cube that a rail
    /// drives along +Z at 3 m/s from (0, 0, 40), in the room, into the cabin. Ticks until the cube enters the cabin, and
    /// returns the tick whose step handed it in. With a doomsday, the world stays open for the caller.
    /// </summary>
    private static long RunCabinScene(long doomsday, out World world, out Entity roomOwner, out FrameHandle cabin, out Entity cube, out Rail rail)
    {
        world = NewWorld(out _);
        FrameHandle space = Space(world);
        roomOwner = Room(world, space, new DPose(new Double3(0, 0, 30), Turned), radius: 50, new DestroyAt(doomsday));
        FrameHandle room = world.GetOwnedFrame(roomOwner);
        // Turned carries the room's +Y to space's +Z.
        cabin = world.GetOwnedFrame(Room(world, room, new DPose(new Double3(0, 20, 0), DQuaternion.Identity), radius: 5, new Hold()));
        rail = new Rail(space, new Double3(0, 0, 40), new Double3(0, 0, 3));
        cube = world.Spawn(new SpawnDesc
        {
            Frame = room,
            Pose = new DPose(world.Frames.Relative(space, room).TransformPoint(rail.At(0)), DQuaternion.Identity),
            Body = KinematicCube,
            Components = [rail],
        });
        while (world.GetFrame(cube) != cabin && world.TickNumber < 300)
            world.Tick();
        long entering = world.TickNumber - 1;
        if (doomsday == long.MaxValue)
            world.Dispose();
        return entering;
    }

    /// <summary>
    /// A ship with a dynamic hull in <paramref name="space"/> at (30, 0, 0), turned by <see cref="Turned"/>, that drifts at
    /// 0.5 m/s along +Y, with a bubble of 20 m, and a ball that flies along +X at 12 m/s and touches the bubble in the
    /// step of tick 30, with <paramref name="pilot"/> on it.
    /// </summary>
    private static (FrameHandle Ship, Entity Ball) BallMeetsDriftingShip(World world, FrameHandle space, Pilot pilot)
    {
        Entity hull = world.Spawn(new SpawnDesc
        {
            Frame = space,
            Pose = new DPose(new Double3(30, 0, 0), Turned),
            LinearVelocity = new Double3(0, 0.5, 0),
            Body = new BodyDesc(new BoxShapeDesc(new Vector3(2)), Mass: 1000, LinearDamping: 0, AngularDamping: 0),
            AsFrame = new FrameSpec(FrameBounds.Sphere(20)),
        });
        FrameHandle ship = world.GetOwnedFrame(hull);
        // The ship drifts 0.25 m sideways before the touch, which moves the touch by 2 mm: well inside half a step.
        Scenes.Approach approach = Scenes.ApproachToBubble(world, ship, tick: 30);
        Entity ball = world.Spawn(new SpawnDesc
        {
            Frame = space,
            Pose = approach.Pose,
            LinearVelocity = approach.Velocity,
            Body = new BodyDesc(new SphereShapeDesc(0.5f), Mass: 1, LinearDamping: 0, AngularDamping: 0),
            Components = [pilot],
        });
        return (ship, ball);
    }

    [Fact]
    public void AVelocitySetInTheTickOfAHandOffIsTheVelocityRelativeToTheFrameThatGameplayNamed()
    {
        using World world = NewWorld(out _);
        FrameHandle space = Space(world);
        var pilot = new Pilot();
        (FrameHandle ship, Entity ball) = BallMeetsDriftingShip(world, space, pilot);
        Tick(world, 30);
        world.GetFrame(ball).Should().Be(space);

        var velocity = new Double3(3, 4, 0);
        pilot.Once = body => body.SetVelocity(velocity);
        world.Tick();
        world.GetFrame(ball).Should().Be(ship, "the step of tick 30 handed the ball into the ship");
        world.Tick();

        Double3 read = world.VelocityIn(ball, space);
        (read - velocity).Length.Should().BeLessThan(1e-4, $"the ball moves at the velocity that gameplay set relative to space, not at {read}");
        world.Errors.Should().BeEmpty();
    }

    [Fact]
    public void AnImpulseAddedInTheTickOfAHandOffPushesAlongTheAxesOfTheFrameThatGameplayNamed()
    {
        using World world = NewWorld(out _);
        FrameHandle space = Space(world);
        var pilot = new Pilot();
        (FrameHandle ship, Entity ball) = BallMeetsDriftingShip(world, space, pilot);
        Tick(world, 30);
        Double3 before = world.VelocityIn(ball, space);

        pilot.Once = body => body.AddImpulse(new Vector3(0, 2, 0));
        world.Tick();
        world.GetFrame(ball).Should().Be(ship, "the step of tick 30 handed the ball into the ship");
        world.Tick();

        Double3 change = world.VelocityIn(ball, space) - before;
        (change - new Double3(0, 2, 0)).Length.Should().BeLessThan(1e-4, $"the impulse of 2 N·s along space's +Y gives the ball of 1 kg 2 m/s along it, not {change}");
        world.Errors.Should().BeEmpty();
    }

    [Fact]
    public void AForceAddedInTheTickOfAHandOffPushesAlongTheAxesOfTheFrameThatGameplayNamed()
    {
        using World world = NewWorld(out _);
        FrameHandle space = Space(world);
        var pilot = new Pilot();
        (FrameHandle ship, Entity ball) = BallMeetsDriftingShip(world, space, pilot);
        Tick(world, 30);
        Double3 before = world.VelocityIn(ball, space);

        pilot.Once = body => body.AddForce(new Vector3(0, 0, 120));
        world.Tick();
        world.GetFrame(ball).Should().Be(ship, "the step of tick 30 handed the ball into the ship");
        world.Tick();

        Double3 change = world.VelocityIn(ball, space) - before;
        (change - new Double3(0, 0, 2)).Length.Should().BeLessThan(1e-4, $"120 N along space's +Z for a step of 1/60 s gives the ball of 1 kg 2 m/s along it, not {change}");
        world.Errors.Should().BeEmpty();
    }

    [Fact]
    public void ATeleportWrittenInTheTickOfAHandOffPutsTheBodyWhereGameplayMeantInTheFrameThatItNamed()
    {
        using World world = NewWorld(out _);
        FrameHandle space = Space(world);
        var pilot = new Pilot();
        (FrameHandle ship, Entity ball) = BallMeetsDriftingShip(world, space, pilot);
        Tick(world, 30);

        var place = new Double3(20, 3, -2);
        pilot.Once = body => body.Teleport(new DPose(place, DQuaternion.Identity));
        world.Tick();
        world.GetFrame(ball).Should().Be(ship, "the step of tick 30 handed the ball into the ship");
        world.Tick();

        // A teleport leaves the ball at rest in its frame, so it drifts with the ship for the step after the teleport.
        Double3 expected = place + new Double3(0, 0.5, 0) * world.DeltaTime;
        Double3 read = world.PoseIn(ball, space).Position;
        (read - expected).Length.Should().BeLessThan(1e-4, $"the ball lands where gameplay put it in space, not at {read}");
        world.Errors.Should().BeEmpty();
    }

    [Fact]
    public void AnAngularVelocitySetInTheTickOfAHandOffIsRelativeToTheFrameThatGameplayNamed()
    {
        using World world = NewWorld(out _);
        FrameHandle space = Space(world);
        var pilot = new Pilot();
        (FrameHandle ship, Entity ball) = BallMeetsDriftingShip(world, space, pilot);
        Tick(world, 30);

        var spin = new Vector3(1, 2, 0);
        pilot.Once = body => body.SetAngularVelocity(spin);
        world.Tick();
        world.GetFrame(ball).Should().Be(ship, "the step of tick 30 handed the ball into the ship");
        world.Tick();

        Vector3 read = world.AngularVelocityIn(ball, space);
        (read - spin).Length().Should().BeLessThan(1e-4f, $"the ball spins as gameplay set it relative to space, not at {read}");
        world.Errors.Should().BeEmpty();
    }

    /// <summary>
    /// Sets its body's velocity relative to its frame in the tick <see cref="AtTick"/>, to the velocity that it reads there,
    /// and records it.
    /// </summary>
    private sealed class SetVelocityAt : IComponent
    {
        public long AtTick { get; set; } = -1;

        public Double3 Written { get; private set; }

        public int Priority => 0;

        public EventMask Wants => EventMask.Of<Tick>();

        public void Handle(Self self, scoped ref EventList events, scoped ref Outbox outbox)
        {
            if (self.Tick != AtTick)
                return;
            Written = self.VelocityIn(self.Frame);
            self.Body.SetVelocity(Written);
        }
    }

    /// <summary>
    /// A frame on a fast Kepler orbit lives in a following frame of its own, which moves and does not turn, so the path from
    /// its parent into it has two levels. A ball that flies beside it, in the same following frame, enters its bubble, and
    /// the velocity that gameplay set relative to the parent in the tick of the hand-off converts at step 7b of the sync
    /// point. No level of the path turns or takes a teleport, so the velocity converts as the frame tree converts it level
    /// by level (see <see cref="FrameTree.ConvertVelocity"/>), to the bit, as before the rotation terms. A first run finds
    /// the tick of the hand-off, and a second run sets the velocity in that tick.
    /// </summary>
    [Fact]
    public void AVelocitySetInTheTickOfAHandOffAlongAPathThatDoesNotTurnConvertsAsTheTreeConvertsItToTheBit()
    {
        long handOff = -1;
        for (int run = 0; run < 2; run++)
        {
            using World world = NewWorldWithFaults(out FaultInjectingBackend faults);
            FrameHandle space = Scenes.AsteroidSpace(world);
            // An inclined orbit and a tilted frame, so that no level of the path is a plain shift.
            var orbit = Tertius.Frames.Rails.KeplerOrbit.Circular(5_000, phase: 0.7) with
            {
                Inclination = 0.4, AxialTilt = DQuaternion.FromAxisAngle(new Double3(1, 2, 3).Normalized(), 0.9),
            };
            FrameHandle station = world.GetOwnedFrame(world.Spawn(new SpawnDesc
            {
                Frame = space,
                Body = KinematicCube,
                AsFrame = new FrameSpec(FrameBounds.Sphere(5)),
            }.OnKeplerOrbit(world, orbit)));
            (Double3 place, Double3 velocity) = orbit.StateAt(world.Time, Scenes.AsteroidMu);
            var setter = new SetVelocityAt { AtTick = handOff };
            Entity ball = world.Spawn(new SpawnDesc
            {
                Frame = space,
                Pose = new DPose(place + new Double3(0, 8, 0), DQuaternion.Identity),
                LinearVelocity = velocity + new Double3(0, -2, 0),
                Body = Ball(),
                Components = [setter],
            });
            int ticks = TickUntil(world, () => world.GetFrame(ball) == station, 120);
            ticks.Should().BeLessThan(120, "the ball enters the station's bubble");
            if (run == 0)
            {
                handOff = world.TickNumber - 1;
                continue;
            }
            world.Frames.Numeric(world.Frames.TreeParent(station)).Should().Be(NumericMode.Following);
            world.TickNumber.Should().Be(handOff + 1, "the velocity acts after the step of the hand-off");
            Double3 at = world.Frames.Relative(space, station).InverseTransformPoint(world.PoseInOwnFrame(ball).Position);
            Double3 expected = world.Frames.ConvertVelocity(space, station, at, setter.Written);

            world.Tick();

            Double3 converted = faults.Intents.Last(record => record.Entity == ball).Intent.Velocity;
            converted.Should().Be(expected);
            world.Errors.Should().BeEmpty();
        }
    }

    /// <summary>
    /// The owner of a cabin turns it by a teleport in the tick in which a ball enters the cabin's bubble, and gameplay sets
    /// the ball's velocity relative to space in that tick. The teleport and the velocity both act at the start of the next
    /// step, so the velocity converts into the cabin by the cabin's pose at the teleport, not by its pose in the tree before
    /// it (docs/plans/spin-gravity.md, "V7, the relative motion of two frames"), and the ball moves in space as gameplay set it.
    /// </summary>
    [Fact]
    public void AVelocitySetInTheTickOfAHandOffIntoAFrameThatTeleportsConvertsByTheTeleportsPose()
    {
        using World world = NewWorld(out _);
        FrameHandle space = Space(world);
        var turner = new Pilot();
        Entity owner = world.Spawn(new SpawnDesc
        {
            Frame = space,
            Pose = new DPose(new Double3(30, 0, 0), DQuaternion.Identity),
            Body = KinematicCube,
            AsFrame = new FrameSpec(FrameBounds.Sphere(20)),
            Components = [turner],
        });
        FrameHandle cabin = world.GetOwnedFrame(owner);
        var pilot = new Pilot();
        Scenes.Approach approach = Scenes.ApproachToBubble(world, cabin, tick: 30);
        Entity ball = world.Spawn(new SpawnDesc { Frame = space, Pose = approach.Pose, LinearVelocity = approach.Velocity, Body = Ball(), Components = [pilot] });
        Tick(world, 30);
        world.GetFrame(ball).Should().Be(space);

        var velocity = new Double3(3, 4, 0);
        turner.Once = body => body.Teleport(new DPose(new Double3(30, 0, 0), Turned));
        pilot.Once = body => body.SetVelocity(velocity);
        world.Tick();
        world.GetFrame(ball).Should().Be(cabin, "the step of tick 30 handed the ball into the cabin");
        world.Tick();

        world.Frames.LocalPose(cabin).Rotation.Should().Be(Turned, "the teleport turned the cabin");
        Double3 read = world.VelocityIn(ball, space);
        (read - velocity).Length.Should().BeLessThan(1e-4, $"the ball moves at the velocity that gameplay set relative to space, not at {read}");
        world.Errors.Should().BeEmpty();
    }
}
