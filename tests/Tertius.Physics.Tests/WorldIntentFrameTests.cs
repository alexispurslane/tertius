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
                self.Body.MoveKinematic(place);
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
}
