using Tertius.Demo;
using Tertius.Frames.Rails;
using Tertius.Simulation;
using static Tertius.Host.Tests.RendezvousAutopilotTests;
using static Tertius.Testing.WorldTesting;
using DemoStation = Tertius.Demo.DemoScenes.DemoStation;

namespace Tertius.Host.Tests;

/// <summary>
/// Where the coast of the <see cref="RendezvousAutopilot"/>'s transfer starts, on the World and the real Jolt backend
/// (docs/plans/blue-danube-demo.md, "The transfer guidance"). In a low orbit, the shuttle turns about 4 degrees in the last
/// minute, so the coast of the last <see cref="TransferGuidance.CoastTime"/> starts well before the transfer angle nears 1
/// degree, where <see cref="KeplerOrbit.Lambert"/> refuses it. <see cref="HighOrbitRun"/> flies a plan where it does not:
/// a station in a geostationary orbit turns only 0.25 degrees a minute.
/// </summary>
public sealed class RendezvousCoastTests(RendezvousCoastTests.HighOrbitRun run)
    : IClassFixture<RendezvousCoastTests.HighOrbitRun>
{
    /// <summary>
    /// The plan's transfer angle falls below Lambert's refused angle about 240 s before the arrival. The autopilot logs no
    /// error through the transfer, and the brake starts.
    /// </summary>
    [Fact]
    public void ATransferWhoseAngleFallsBelowLambertsRefusalBeforeTheLastMinuteFliesWithoutAnError()
    {
        run.Log.Should().Contain(record => record.State is Transfer && record.TimeLeft > TransferGuidance.CoastTime
                                                                    && record.RemainingAngle <
                                                                    KeplerOrbit.LambertRefusedAngle * 180 / Math.PI,
            "the plan must reach the case");
        run.Errors.Should().BeEmpty();
        run.Log.Should().Contain(record => record.State is Brake);
    }

    /// <summary>
    /// The coast starts where the remaining transfer angle falls to 2 degrees, a degree above Lambert's refused angle, more
    /// than a minute before the arrival: the autopilot steers with the thrusters in every tick before, and in no tick
    /// after. The margin of a degree keeps every solve clear of the refusal, whatever the rounding of the two angles. The
    /// tolerance of 0.01 degrees covers the two steps by which the record lags the autopilot's read, about 0.0001 degrees.
    /// </summary>
    [Fact]
    public void TheCoastStartsADegreeBeforeTheAngleThatLambertRefuses()
    {
        double coastDegrees = 2 * KeplerOrbit.LambertRefusedAngle * 180 / Math.PI;
        HighOrbitRun.Record[] transfer = [.. run.Log.Where(record => record.State is Transfer)];
        transfer.Where(record => record.RemainingAngle > coastDegrees + 0.01).Should().NotBeEmpty()
            .And.OnlyContain(record => record.Translates);
        transfer.Where(record => record.RemainingAngle < coastDegrees - 0.01).Should().NotBeEmpty()
            .And.OnlyContain(record => !record.Translates && record.Throttle == null);
        transfer.First(record => record.RemainingAngle < coastDegrees).TimeLeft.Should()
            .BeGreaterThan(TransferGuidance.CoastTime);
    }

    /// <summary>
    /// One flight of <see cref="Plan"/>: a station in a geostationary orbit, 42,164 km from Earth's center, and the shuttle
    /// 5 km behind it on the same circle. The order comes in the second tick, inside the plan's window, and the flight runs
    /// until the brake starts, or for 1,100 s at most. The log keeps a record of every tick.
    /// </summary>
    public sealed class HighOrbitRun : IDisposable
    {
        /// <summary>The radius of the station's orbit, in meters: a geostationary orbit.</summary>
        public const double OrbitRadius = 42_164_000;

        /// <summary>
        /// The plan: a transfer of 1,000 s to an arrival point 400 m out on the axis. The station turns 4.18 degrees in it,
        /// so the transfer angle at the order is about 4.2 degrees, inside the window from 30 down to 2 degrees. The angle
        /// falls to Lambert's refused angle of 1 degree about 240 s before the arrival. The shuttle carries no crew, so
        /// the nose turns at the attitude autopilot's full rate.
        /// </summary>
        public static RendezvousPlan Plan { get; } = new(TransferTime: 1000, ArrivalDistance: 400,
            HoldDistance: DemoStation.HoldPoint.Y,
            WindowOpens: 30 * Math.PI / 180, WindowCloses: 2 * Math.PI / 180,
            WeightlessTurnRate: AttitudeAutopilot.MaxTurnRate);

        /// <summary>The station's orbit: the demo station's, at <see cref="OrbitRadius"/>, at mean anomaly 0 at the epoch.</summary>
        public static KeplerOrbit StationOrbit { get; } =
            DemoStation.Orbit with { SemiMajorAxis = OrbitRadius, MeanAnomalyAtEpoch = 0 };

        private readonly World world = NewWorld();

        public HighOrbitRun()
        {
            FrameHandle earth = Earth(world);
            Entity station = SpawnStation(world, earth);
            KeplerOrbit orbit = StationOrbit;
            (Double3 position, Double3 velocity) =
                (orbit with { MeanAnomalyAtEpoch = orbit.MeanAnomalyAtEpoch - 5000 / OrbitRadius }).StateAt(world.Time,
                    Mu);
            RendezvousAutopilotTests.Recorder recorder = new();
            Entity shuttle = Fly(world, earth, station, Plan, position, velocity, recorder);
            var autopilot = world.GetComponent<RendezvousAutopilot>(shuttle)!;
            world.Post(Event.Create(shuttle, Entity.Null, new Rendezvous()));
            Tick(world, 2);

            for (int tick = 0; tick < 1100 * 60 && autopilot.State.Value is not Brake; tick++)
            {
                world.Tick();
                double timeLeft = double.NaN, remaining = double.NaN;
                if (autopilot.State.TryGetValue(out Transfer transfer))
                {
                    timeLeft = transfer.ArrivalTime - world.Time;
                    (Double3 center, Double3 stationVelocity, Double3 axis) = StationNow(world, station, earth);
                    Double3 arrival = transfer.Guidance
                        .Arrival(center, stationVelocity, axis, Plan.ArrivalDistance, timeLeft).Point;
                    double angle = TransferGuidance.TransferAngle(world.PoseIn(shuttle, earth).Position, arrival,
                        TransferGuidance.OrbitNormal(center, stationVelocity)) * 180 / Math.PI;
                    remaining = Math.Min(angle, 360 - angle);
                }

                Log.Add(new(autopilot.State.Value, timeLeft, remaining, recorder.Translates, recorder.Throttle));
            }

            Errors = [.. world.Errors];
        }

        /// <summary>The records of the ticks after the order, oldest first.</summary>
        public List<Record> Log { get; } = [];

        /// <summary>The world's errors at the end of the flight.</summary>
        public string[] Errors { get; }

        /// <summary>The state of the autopilot after a tick, and what it added in that tick.</summary>
        /// <param name="State">The autopilot's state after the tick.</param>
        /// <param name="TimeLeft">The time left to the arrival time after the tick, in seconds, or NaN without a transfer.</param>
        /// <param name="RemainingAngle">
        /// The transfer angle from the shuttle to the arrival point of the arrival time, as the world reads the states after
        /// the tick, in degrees from 0 to 180: the angle about the station's orbit normal, or 360 degrees less it, whichever
        /// is smaller. NaN without a transfer.
        /// </param>
        /// <param name="Translates">True when the autopilot added a translation in the tick.</param>
        /// <param name="Throttle">The throttle that the autopilot added in the tick, or null.</param>
        public readonly record struct Record(
            object State,
            double TimeLeft,
            double RemainingAngle,
            bool Translates,
            float? Throttle);

        public void Dispose() => world.Dispose();

        /// <summary>
        /// Spawns a small station on <see cref="StationOrbit"/> in <paramref name="frame"/>: a kinematic box with a
        /// <see cref="KeplerMotion"/>, which owns a frame, as the demo's station does, so that its velocity is the instant
        /// velocity of its frame's last step record.
        /// </summary>
        private static Entity SpawnStation(World world, FrameHandle frame)
        {
            KeplerOrbit orbit = StationOrbit;
            (Double3 place, Double3 velocity) = orbit.StateAt(world.Time, Mu);
            return world.Spawn(new()
            {
                Frame = frame,
                Pose = new(place, orbit.RotationAt(world.Time, Mu)),
                LinearVelocity = velocity,
                AngularVelocity = orbit.AngularVelocityAt(world.Time, Mu).ToVector3(),
                FirstMove = new(orbit.Sample(Mu, world.TimeAt(world.TickNumber + 1), world.DeltaTime)),
                Components = [new KeplerMotion { Orbit = orbit }],
                Body = new(new BoxShapeDesc(new(10)), BodyMotion.Kinematic, Mass: 100_000),
                AsFrame = new(FrameBounds.Sphere(30))
            });
        }
    }
}
