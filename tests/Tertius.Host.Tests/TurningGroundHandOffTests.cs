using System.Numerics;
using Tertius.Demo;
using Tertius.Physics;
using Tertius.Simulation;
using Shuttle = Tertius.Demo.DemoScenes.Shuttle;

namespace Tertius.Host.Tests;

/// <summary>The state of the thrusting shuttle and its crate after one tick of <see cref="TurningGroundHandOffRun"/>.</summary>
/// <param name="Tick">The number of completed ticks.</param>
/// <param name="Frame">The shuttle's home frame.</param>
/// <param name="Position">The shuttle's center in <c>earth</c>, in meters from Earth's center.</param>
/// <param name="Rotation">The shuttle's rotation in <c>earth</c>.</param>
/// <param name="AngularVelocity">The shuttle's angular velocity in <c>earth</c>, in rad/s, in earth's axes.</param>
/// <param name="CabinAcceleration">The proper acceleration of the shuttle's frame, in m/s², in its axes.</param>
/// <param name="CrateFrame">The crate's home frame.</param>
/// <param name="CrateInCabin">The crate's center in the shuttle's frame, in meters.</param>
internal sealed record HandOffTick(
    long Tick,
    FrameHandle Frame,
    Double3 Position,
    DQuaternion Rotation,
    Double3 AngularVelocity,
    Vector3 CabinAcceleration,
    FrameHandle CrateFrame,
    Double3 CrateInCabin);

/// <summary>
/// One run of the scene <c>earth-system</c> with the real shuttle's hull, dynamic, spawned in the turning
/// <c>earth-surface</c> 1 km below the radius at which it leaves the bubble, <see cref="ExitRadius"/>, and 200 km from the
/// axis of its turn (docs/plans/blue-danube-demo.md, "The hand-off"), in a world that starts at
/// <see cref="StartTime"/>, with the ground turned as at the demo's departure. It moves at 1 km/s out from Earth's center and 1 km/s across, along -Z of earth-surface,
/// with its nose along that velocity and its roof away from Earth. A <see cref="ThirdThrottle"/> gives its
/// <see cref="MainEngine"/> a throttle of 1/3 in every tick, about 9.81 m/s² along the nose. A crate of 0.4 m rests on the
/// cabin's aft wall, beside the engine's housing, where the thrust gives it weight. The run takes 120 ticks, and records
/// each.
/// </summary>
public sealed class TurningGroundHandOffRun : IDisposable
{
    /// <summary>The thrust along the nose, in m/s²: a third of the engine's full thrust, over the hull's mass.</summary>
    public const double Thrust = Shuttle.ThrustAcceleration / 3.0;

    /// <summary>
    /// The world's start, in seconds: the demo's departure. The scene earth-system takes the demo's date, so the ground
    /// then stands turned by <see cref="DemoScenes.DemoSurface.GroundTurnAtDeparture"/>, 142.6 degrees. So the axes of
    /// earth-surface and of earth differ by that much at the hand-off, and a thrust or a velocity that the hand-off leaves
    /// in the wrong axes misses by more than its own length.
    /// </summary>
    public const double StartTime = DemoScenes.DemoSurface.DepartureAt;

    /// <summary>The crate's half extents, in meters.</summary>
    public static readonly Vector3 CrateHalf = new(0.2f);

    /// <summary>
    /// Where the crate starts, in the shuttle's axes: 1 mm forward of the aft wall's inner face at z = 7.8 m, and to
    /// starboard of the housing, which reaches 0.9 m from the middle line.
    /// </summary>
    public static readonly Double3 CrateStart = new(1.8, 0, Shuttle.Cabin.Z - CrateHalf.Z - 0.001);

    /// <summary>
    /// How far from Earth's center the shuttle's center lies when it leaves earth-surface, by the engine's rule of the
    /// hand-off (ARCHITECTURE.md, "Hand-off", rule 2): the bubble's radius, plus the shuttle's bounding sphere, plus the exit
    /// margin, the larger of 2 percent of the bubble's radius and the bounding sphere. The bubble ends at
    /// <see cref="DemoScenes.GroundBubbleTop"/>, 50 km up, so the margin is 128.4 km, and the shuttle leaves 178.4 km up.
    /// </summary>
    public static double ExitRadius
    {
        get
        {
            double bubble = DemoScenes.EarthRadius + DemoScenes.GroundBubbleTop, radius = Shuttle.Hull.Length();
            return bubble + Math.Max(0.02 * bubble, radius) + radius;
        }
    }

    public TurningGroundHandOffRun()
    {
        World = new(
            new() { MaxDegreeOfParallelism = 1, DebugChecks = true, DisposePhysics = true, StartTime = StartTime },
            new JoltPhysicsBackend(JoltBackendOptions.Unpaced));
        Scene = DemoScenes.All.Single(s => s.Name == "earth-system").Build(World, RenderResources.None);
        Earth = World.GetOwnedFrame(Scene["earth"]);
        Surface = World.GetOwnedFrame(Scene["earth-surface"]);

        double top = ExitRadius;
        double across = 200e3;
        var place = new Double3(across, Math.Sqrt((top - 1e3) * (top - 1e3) - across * across), 0);
        Double3 up = place.Normalized();
        Double3 velocity = up * 1e3 - Double3.UnitZ * 1e3;
        Hull = World.Spawn(new()
        {
            Frame = Surface,
            Pose = new(place, AttitudeAutopilot.Facing(velocity, up)),
            LinearVelocity = velocity.ToVector3(),
            Body = Shuttle.Body(BodyMotion.Dynamic),
            AsFrame = new(new(Shuttle.Bubble)),
            Components = [new ThirdThrottle(), new MainEngine(Shuttle.Mass * Shuttle.ThrustAcceleration)]
        });
        Cabin = World.GetOwnedFrame(Hull);
        Crate = World.Spawn(new()
        {
            Frame = Cabin,
            Pose = new(CrateStart, DQuaternion.Identity),
            Body = new(Shape: new BoxShapeDesc(CrateHalf), Mass: 5)
        });

        for (int tick = 0; tick < 120; tick++)
        {
            World.Tick();
            Ticks.Add(new(Tick: World.TickNumber, Frame: World.GetFrame(Hull),
                Position: World.PoseIn(Hull, Earth).Position,
                Rotation: World.PoseIn(Hull, Earth).Rotation,
                AngularVelocity: World.AngularVelocityIn(Hull, Earth),
                CabinAcceleration: World.Frames.ProperAcceleration(Cabin), CrateFrame: World.GetFrame(Crate),
                CrateInCabin: World.PoseIn(Crate, Cabin).Position));
        }
    }

    public World World { get; }
    public SceneInstance Scene { get; }
    public FrameHandle Earth { get; }
    public FrameHandle Surface { get; }
    public FrameHandle Cabin { get; }
    public Entity Hull { get; }
    public Entity Crate { get; }
    internal List<HandOffTick> Ticks { get; } = [];

    /// <summary>The index in <see cref="Ticks"/> of the first tick after which the shuttle is in earth, or -1.</summary>
    internal int HandOff => Ticks.FindIndex(tick => tick.Frame == Earth);

    public void Dispose() => World.Dispose();

    /// <summary>A test component that gives its entity's <see cref="MainEngine"/> a throttle of 1/3 in every tick.</summary>
    private sealed class ThirdThrottle : IComponent
    {
        public int Priority => 0;

        public EventMask Wants => EventMask.Of<Tick>();

        public void Handle(Self self, scoped ref EventList events, scoped ref Outbox outbox) =>
            events.Add(new Throttle { Value = 1f / 3 });
    }
}

/// <summary>
/// The first hand-off of a dynamic hull that owns a cabin frame out of a turning frame (docs/plans/blue-danube-demo.md,
/// "The hand-off"): the thrusting shuttle of <see cref="TurningGroundHandOffRun"/> leaves the turning
/// <c>earth-surface</c> through the top of its bubble, past the exit margin, into <c>earth</c>, which does not turn. Across the hand-off its
/// motion in <c>earth</c> stays that of gravity and thrust, its cabin's proper acceleration stays that of the thrust, and
/// the crate on the cabin's aft wall stays in its place. Each check holds in the 10 ticks before the hand-off and the 10
/// after it.
/// </summary>
public sealed class TurningGroundHandOffTests(TurningGroundHandOffRun run) : IClassFixture<TurningGroundHandOffRun>
{
    private IReadOnlyList<HandOffTick> Ticks => run.Ticks;

    /// <summary>The indices in <see cref="Ticks"/> from 10 before the hand-off to 10 after it.</summary>
    private IEnumerable<int> AroundTheHandOff => Enumerable.Range(run.HandOff - 10, 21);

    /// <summary>
    /// The shuttle is in earth-surface after every tick before one, and in earth after that tick and every later one. The
    /// hand-off falls near the middle of the run, once the shuttle's center lies above the top of the bubble.
    /// </summary>
    [Fact]
    public void AThrustingShuttleLeavesTheTurningGroundOnceThroughTheTopOfItsBubble()
    {
        int handOff = run.HandOff;

        handOff.Should().BeInRange(30, 90, "the hand-off falls near the middle of the 120 ticks");
        Ticks.Take(handOff).Should().OnlyContain(tick => tick.Frame == run.Surface);
        Ticks.Skip(handOff).Should().OnlyContain(tick => tick.Frame == run.Earth);
        Ticks[handOff - 1].Position.Length.Should().BeLessThanOrEqualTo(TurningGroundHandOffRun.ExitRadius,
            "the shuttle is still inside the exit radius before the hand-off");
        Ticks[handOff].Position.Length.Should().BeInRange(TurningGroundHandOffRun.ExitRadius,
            TurningGroundHandOffRun.ExitRadius + 30,
            "the shuttle leaves above the top of the bubble, by its bounding sphere and the exit margin");
    }

    /// <summary>
    /// In each tick around the hand-off, the shuttle's acceleration in earth, the second difference of its center over the
    /// tick, lies within 0.01 m/s² of Earth's gravity at its place plus the thrust along its nose. The axes of earth-surface
    /// and of earth differ by about 143 degrees, so a thrust that a step applied in the axes of the wrong frame misses by
    /// more than the thrust itself, and a velocity converted without the ground's turn, 14.6 m/s there, jumps by far more.
    /// </summary>
    [Fact]
    public void TheShuttlesAccelerationInEarthStaysThatOfGravityAndThrustThroughTheHandOff()
    {
        double turned = 2 *
                        Math.Acos(Math.Min(1,
                            Math.Abs(run.World.PoseIn(run.Scene["earth-surface"], run.Earth).Rotation.W))) * 180 /
                        Math.PI;
        turned.Should().BeGreaterThan(90,
            "the axes of the two frames differ by far more than the thrust's tolerance allows");
        double dt = run.World.DeltaTime;
        foreach (int k in AroundTheHandOff)
        {
            HandOffTick tick = Ticks[k];
            Double3 measured = (Ticks[k + 1].Position - 2 * tick.Position + Ticks[k - 1].Position) / (dt * dt);
            Double3 gravity = -tick.Position * (DemoScenes.EarthMu / Math.Pow(tick.Position.Length, 3));
            Double3 expected = gravity + tick.Rotation.Rotate(-Double3.UnitZ) * TurningGroundHandOffRun.Thrust;
            (measured - expected).Length.Should().BeLessThan(0.01,
                $"at tick {tick.Tick}, {k - run.HandOff} from the hand-off: measured {measured}, expected {expected}");
        }
    }

    /// <summary>In each tick around the hand-off, the cabin's proper acceleration lies within 0.01 m/s² of the thrust along its nose, -Z.</summary>
    [Fact]
    public void TheCabinsProperAccelerationStaysThatOfTheThrustThroughTheHandOff()
    {
        foreach (int k in AroundTheHandOff)
        {
            HandOffTick tick = Ticks[k];
            (tick.CabinAcceleration + Double3.UnitZ * TurningGroundHandOffRun.Thrust).Length
                .Should().BeLessThan(0.01,
                    $"at tick {tick.Tick}, {k - run.HandOff} from the hand-off: {tick.CabinAcceleration}");
        }
    }

    /// <summary>
    /// The hull's angular velocity in earth changes by less than 1e-6 rad/s across each tick around the hand-off, and its
    /// nose turns in earth by less than 1e-5 rad in each of those ticks: no torque acts on it, so it keeps its turn in earth,
    /// the ground's 7.3e-5 rad/s with which it was spawned. The bound is tighter than the design's 1e-4 rad/s, so that a
    /// hand-off that drops the ground's turn from the hull's angular velocity fails it.
    /// </summary>
    [Fact]
    public void TheShuttleKeepsItsTurnInEarthThroughTheHandOff()
    {
        foreach (int k in AroundTheHandOff)
        {
            (Ticks[k].AngularVelocity - Ticks[k - 1].AngularVelocity).Length.Should().BeLessThan(1e-6,
                $"at tick {Ticks[k].Tick}, {k - run.HandOff} from the hand-off");
            double turn =
                Math.Acos(Math.Clamp(
                    Double3.Dot(Ticks[k].Rotation.Rotate(-Double3.UnitZ), Ticks[k - 1].Rotation.Rotate(-Double3.UnitZ)),
                    -1, 1));
            turn.Should().BeLessThan(1e-5, $"at tick {Ticks[k].Tick}, {k - run.HandOff} from the hand-off");
        }
    }

    /// <summary>
    /// The crate stays in the cabin's frame in every tick, and across each tick around the hand-off its place in the cabin
    /// changes by less than 1 mm more than in the tick before: the thrust holds it on the aft wall.
    /// </summary>
    [Fact]
    public void ACrateOnTheCabinsAftWallStaysInPlaceThroughTheHandOff()
    {
        Ticks.Should().OnlyContain(tick => tick.CrateFrame == run.Cabin);
        foreach (int k in AroundTheHandOff)
        {
            double step = Double3.Distance(Ticks[k + 1].CrateInCabin, Ticks[k].CrateInCabin),
                before = Double3.Distance(Ticks[k].CrateInCabin, Ticks[k - 1].CrateInCabin);
            step.Should().BeLessThan(before + 1e-3, $"at tick {Ticks[k].Tick}, {k - run.HandOff} from the hand-off");
        }

        Ticks[run.HandOff + 10].CrateInCabin.Z.Should().BeApproximately(
            Shuttle.Cabin.Z - TurningGroundHandOffRun.CrateHalf.Z, 0.01, "the crate rests on the aft wall");
    }

    [Fact]
    public void TheWorldLogsNoErrors() => run.World.Errors.Should().BeEmpty();
}
