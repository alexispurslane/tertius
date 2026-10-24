using System.Globalization;
using System.Numerics;
using Tertius.Simulation;
using static Tertius.Testing.NumericFrameTesting;

namespace Tertius.Physics.Tests.Precision;

/// <summary>
/// Measures how the contact errors between two free bodies that move together grow with their common speed (ticket
/// ter-kugq). Jolt keeps each body's velocity as a float relative to its system's origin, so the common speed V of a pair
/// sits inside both bodies' floats, and a float near V cannot hold a small difference exactly. Resting objects on a moving
/// platform belong in the platform's own semantic frame, so the contact case that numeric frames must handle is two free
/// bodies that touch while they move together, with no platform. Here one body pushes the other ahead of it, in zero
/// gravity, with numeric frames off, on the real World and the real Jolt backend.
/// </summary>
/// <remarks>
/// <para>
/// Two scenes, each a pusher and a pushed body that start in contact, the pusher behind along +x, both with the common
/// velocity V along a direction d:
/// </para>
/// <list type="bullet">
/// <item>character-pushes-crate: a character (capsule of radius 0.3 m and half height 0.6 m, 70 kg) with a constant thrust
/// of 105 N along +x, and a dynamic crate of 1 m by 1 m by 1 m and 20 kg ahead of it. The two start 0.02 m apart, the
/// gap that a Jolt character keeps from what it touches.</item>
/// <item>box-pushes-box: a dynamic box of 1 m by 1 m by 1 m and 2 kg with a constant force of 10 N along +x, and another
/// such box ahead of it, face to face, with no gap.</item>
/// </list>
/// <para>
/// No body has damping or restitution. Each run lasts 60 s at 60 Hz, starts with the pair's center of mass at
/// -V · 30 s · d, so that its path passes the origin at mid-run, and records its largest distance from the origin. A
/// distance control then runs the same scene with V = 0, with the pair's center of mass starting at the point where the
/// moving run was farthest from the origin. Each row of the CSV is one run at one sample time.
/// </para>
/// <para>
/// Jolt's speed limit is raised from the engine's default of 30 km/s to 1000 km/s, so that the limit never clamps a
/// body's velocity: at the default, a dynamic body above 30 km/s would lose speed to the limit, and the rows would
/// measure the limit, not precision.
/// </para>
/// </remarks>
public sealed class PushedContactMeasurement
{
    /// <summary>How many ticks each run lasts: 60 s at 60 Hz.</summary>
    private const int RunTicks = 3600;

    /// <summary>Jolt's speed limit in these runs, in m/s: far above the fastest body.</summary>
    private const float SpeedLimit = 1_000_000f;

    /// <summary>The CSV header: each column's name ends with its unit, or names a count or a label.</summary>
    private const string Header =
        "scene,direction,run,speed_m_per_s,common_speed_in_run_m_per_s,start_distance_m,largest_distance_in_run_m,time_s,force_steps,"
        + "creep_m,lateral_slip_m,relative_velocity_rms_m_per_s,relative_velocity_max_m_per_s,"
        + "common_velocity_error_m_per_s,common_position_error_m,common_position_error_vs_step_rule_m,"
        + "collision_events_total,collision_events_after_first,collision_ended_events,"
        + "pusher_tilt_deg,pushed_tilt_deg,largest_distance_so_far_m,world_errors,backend_warnings";

    /// <summary>The common speeds V of the moving runs, in m/s.</summary>
    private static readonly double[] Speeds =
    [
        0,
        1,
        10,
        50,
        100,
        200,
        500,
        1000,
        2000,
        5000,
        7672,
        10000,
        20000,
        50000,
        100000,
        300000,
    ];

    /// <summary>The direction d of the common velocity: across the push (the easy case), and along the space diagonal.</summary>
    private static readonly (string Name, Double3 Direction)[] Directions =
    [
        ("y", Double3.UnitY),
        ("diagonal", new Double3(1, 1, 1).Normalized()),
    ];

    /// <summary>The ticks after the start at which a run writes a row: 5 s, 20 s and 60 s at 60 Hz.</summary>
    private static readonly int[] SampleTicks = [300, 1200, 3600];

    private static readonly PairScene CharacterPushesCrate = new(
        Name: "character-pushes-crate",
        Pusher: new(
            Shape: new CapsuleShapeDesc(0.6f, 0.3f),
            Motion: BodyMotion.Character,
            Mass: 70,
            LinearDamping: 0,
            AngularDamping: 0
        ),
        Pushed: new(Shape: new BoxShapeDesc(new(0.5f)), Mass: 20, LinearDamping: 0, AngularDamping: 0),
        Thrust: 105,
        PusherHalfDepth: 0.3,
        PushedHalfDepth: 0.5,
        StartGap: 0.02
    );

    private static readonly PairScene BoxPushesBox = new(
        Name: "box-pushes-box",
        Pusher: new(Shape: new BoxShapeDesc(new(0.5f)), Mass: 2, LinearDamping: 0, AngularDamping: 0),
        Pushed: new(Shape: new BoxShapeDesc(new(0.5f)), Mass: 2, LinearDamping: 0, AngularDamping: 0),
        Thrust: 10,
        PusherHalfDepth: 0.5,
        PushedHalfDepth: 0.5,
        StartGap: 0
    );

    /// <summary>
    /// A pair of bodies in contact along x.
    /// </summary>
    /// <param name="Name">The scene's name in the CSV.</param>
    /// <param name="Pusher">The rear body, which the thrust pushes.</param>
    /// <param name="Pushed">The front body, which only the contact pushes.</param>
    /// <param name="Thrust">The constant force on the pusher along +x, in newtons.</param>
    /// <param name="PusherHalfDepth">The distance from the pusher's center to its front surface along x, in m.</param>
    /// <param name="PushedHalfDepth">The distance from the pushed body's center to its rear surface along x, in m.</param>
    /// <param name="StartGap">The gap between the facing surfaces at the start, in m.</param>
    private sealed record PairScene(
        string Name,
        BodyDesc Pusher,
        BodyDesc Pushed,
        float Thrust,
        double PusherHalfDepth,
        double PushedHalfDepth,
        double StartGap
    )
    {
        /// <summary>The distance between the two centers along x at the start, in m.</summary>
        public double StartSeparation => PusherHalfDepth + StartGap + PushedHalfDepth;

        /// <summary>The mass of both bodies together, in kg.</summary>
        public double TotalMass => Pusher.Mass + Pushed.Mass;
    }

    /// <summary>
    /// Runs every scene, direction and speed, with its distance control, and writes pushed-contact.csv into the folder
    /// that the environment variable TERTIUS_PRECISION_OUT names, or into tertius-precision in the temporary folder.
    /// </summary>
    [Fact(Explicit = true)]
    public void MeasurePushedContactErrorsAgainstCommonSpeed()
    {
        string folder = Environment.GetEnvironmentVariable("TERTIUS_PRECISION_OUT") is { Length: > 0 } named
            ? named
            : Path.Combine(Path.GetTempPath(), "tertius-precision");
        Directory.CreateDirectory(folder);
        string path = Path.Combine(folder, "pushed-contact.csv");

        var lines = new List<string> { Header };
        foreach (PairScene scene in new[] { CharacterPushesCrate, BoxPushesBox })
        foreach ((string directionName, Double3 direction) in Directions)
        foreach (double speed in Speeds)
        {
            Double3 start = direction * (-speed * RunTicks / 60.0 / 2);
            RunResult moving = Run(scene: scene, direction: direction, speed: speed, start: start);
            lines.AddRange(
                moving.Rows(
                    scene: scene.Name,
                    direction: directionName,
                    run: "moving",
                    speed: speed,
                    commonSpeed: speed
                )
            );
            RunResult control = Run(scene: scene, direction: direction, speed: 0, start: moving.FarthestPoint);
            lines.AddRange(
                control.Rows(
                    scene: scene.Name,
                    direction: directionName,
                    run: "distance-control",
                    speed: speed,
                    commonSpeed: 0
                )
            );
        }

        File.WriteAllLines(path, lines);
        TestContext.Current.TestOutputHelper?.WriteLine($"Wrote {lines.Count - 1} rows to {path}.");
    }

    /// <summary>What one sample of a run measured.</summary>
    /// <param name="Tick">The tick after the start, which times the sample.</param>
    /// <param name="ForceSteps">How many physics steps the thrust acted in up to this tick.</param>
    /// <param name="Creep">The gap between the facing surfaces along x now, minus the gap at the start, in m.</param>
    /// <param name="LateralSlip">How far the pushed body moved across x relative to the pusher since the start, in m.</param>
    /// <param name="RelativeVelocityRms">The root mean square of |v_pushed - v_pusher| over ticks 1 to <paramref name="Tick"/>
    /// , in m/s.</param>
    /// <param name="RelativeVelocityMax">The largest |v_pushed - v_pusher| over the same ticks, in m/s.</param>
    /// <param name="CommonVelocityError">|v of the center of mass - (V d + a · force time · x)|, in m/s.</param>
    /// <param name="CommonPositionError">|p of the center of mass - (p0 + V d t + a (force time)² / 2 · x)|, in m.</param>
    /// <param name="CommonPositionErrorVsStepRule">|p of the center of mass - (p0 + V d t + a dt² k (k + 1) / 2 · x)|, in m.</param>
    private readonly record struct Sample(
        int Tick,
        int ForceSteps,
        double Creep,
        double LateralSlip,
        double RelativeVelocityRms,
        double RelativeVelocityMax,
        double CommonVelocityError,
        double CommonPositionError,
        double CommonPositionErrorVsStepRule,
        int CollisionsTotal,
        int CollisionsEnded,
        double PusherTilt,
        double PushedTilt,
        double LargestDistanceSoFar,
        int WorldErrors,
        int BackendWarnings
    );

    /// <summary>The samples of one run, and where the pair's center of mass was farthest from the origin.</summary>
    private sealed record RunResult(
        Double3 StartPoint,
        List<Sample> Samples,
        Double3 FarthestPoint,
        double LargestDistance
    )
    {
        public IEnumerable<string> Rows(string scene, string direction, string run, double speed, double commonSpeed) =>
            Samples.Select(s =>
                string.Join(
                    ',',
                    scene,
                    direction,
                    run,
                    F(speed),
                    F(commonSpeed),
                    F(StartPoint.Length),
                    F(LargestDistance),
                    F(s.Tick / 60.0),
                    s.ForceSteps.ToString(CultureInfo.InvariantCulture),
                    F(s.Creep),
                    F(s.LateralSlip),
                    F(s.RelativeVelocityRms),
                    F(s.RelativeVelocityMax),
                    F(s.CommonVelocityError),
                    F(s.CommonPositionError),
                    F(s.CommonPositionErrorVsStepRule),
                    s.CollisionsTotal.ToString(CultureInfo.InvariantCulture),
                    Math.Max(0, s.CollisionsTotal - 1).ToString(CultureInfo.InvariantCulture),
                    s.CollisionsEnded.ToString(CultureInfo.InvariantCulture),
                    F(s.PusherTilt),
                    F(s.PushedTilt),
                    F(s.LargestDistanceSoFar),
                    s.WorldErrors.ToString(CultureInfo.InvariantCulture),
                    s.BackendWarnings.ToString(CultureInfo.InvariantCulture)
                )
            );

        private static string F(double value) => value.ToString("R", CultureInfo.InvariantCulture);
    }

    /// <summary>
    /// Runs one pair for 60 s with the common velocity <paramref name="speed"/> · <paramref name="direction"/>, with the
    /// pair's center of mass at <paramref name="start"/> at the start.
    /// </summary>
    private static RunResult Run(PairScene scene, Double3 direction, double speed, Double3 start)
    {
        int warnings = 0;
        var backend = new JoltPhysicsBackend(
            new()
            {
                WorkerThreads = 2,
                MaxLinearVelocity = SpeedLimit,
                Log = _ => Interlocked.Increment(ref warnings),
            }
        );
        var options = new WorldOptions
        {
            MaxDegreeOfParallelism = 1,
            DebugChecks = false,
            DisposePhysics = true,
            NumericFrames = WithoutNumericFrames,
        };
        using var world = new World(options, backend);
        FrameHandle root = world.CreateRootFrame(new(FrameBounds.Sphere(1e8f)));

        double pusherMass = scene.Pusher.Mass;
        double pushedMass = scene.Pushed.Mass;
        double totalMass = scene.TotalMass;
        double separation = scene.StartSeparation;
        // The center of mass is at the start point, so the pusher sits behind it and the pushed body ahead of it along x.
        Double3 pusherStart = start - Double3.UnitX * (separation * pushedMass / totalMass);
        Double3 pushedStart = start + Double3.UnitX * (separation * pusherMass / totalMass);
        Double3 velocity = direction * speed;

        int pilotCalls = 0;
        var thrust = new Vector3(scene.Thrust, 0, 0);
        var pilot = new Pilot
        {
            EveryTick = body =>
            {
                body.AddForce(thrust);
                pilotCalls++;
            },
        };
        Entity pusher = world.Spawn(
            new()
            {
                Frame = root,
                Pose = new(pusherStart, DQuaternion.Identity),
                LinearVelocity = velocity,
                Body = scene.Pusher,
                Components = [pilot],
            }
        );
        var pushedLog = new CollisionLog();
        Entity pushed = world.Spawn(
            new()
            {
                Frame = root,
                Pose = new(pushedStart, DQuaternion.Identity),
                LinearVelocity = velocity,
                Body = scene.Pushed,
                Components = [pushedLog],
            }
        );

        double dt = world.DeltaTime;
        double acceleration = scene.Thrust / totalMass;
        var samples = new List<Sample>(SampleTicks.Length);
        Double3 farthestPoint = start;
        double largestDistance = start.Length;
        double relativeSpeedSquares = 0;
        double relativeSpeedMax = 0;
        int nextSample = 0;
        for (int tick = 1; tick <= RunTicks; tick++)
        {
            // A force written during a tick acts in the physics step of the next tick, so every call before this tick
            // acts in a step up to this tick's.
            int forceSteps = pilotCalls;
            world.Tick();

            DPose pusherPose = world.PoseIn(pusher, root);
            DPose pushedPose = world.PoseIn(pushed, root);
            Double3 pusherVelocity = world.VelocityIn(pusher, root);
            Double3 pushedVelocity = world.VelocityIn(pushed, root);

            Double3 center = (pusherPose.Position * pusherMass + pushedPose.Position * pushedMass) / totalMass;
            if (center.Length > largestDistance)
            {
                largestDistance = center.Length;
                farthestPoint = center;
            }

            double relativeSpeed = (pushedVelocity - pusherVelocity).Length;
            relativeSpeedSquares += relativeSpeed * relativeSpeed;
            relativeSpeedMax = Math.Max(relativeSpeedMax, relativeSpeed);

            if (tick != SampleTicks[nextSample])
                continue;
            nextSample = Math.Min(nextSample + 1, SampleTicks.Length - 1);

            double time = tick * dt;
            double forceTime = forceSteps * dt;
            Double3 centerVelocity = (pusherVelocity * pusherMass + pushedVelocity * pushedMass) / totalMass;
            Double3 exactVelocity = velocity + Double3.UnitX * (acceleration * forceTime);
            Double3 coast = start + velocity * time;
            Double3 exactPosition = coast + Double3.UnitX * (acceleration * forceTime * forceTime / 2);
            // The step rule adds a dt of velocity, then moves by the new velocity for dt (semi-implicit Euler).
            Double3 stepRulePosition =
                coast + Double3.UnitX * (acceleration * dt * dt * forceSteps * (forceSteps + 1) / 2);

            Double3 offset = pushedPose.Position - pusherPose.Position;
            int collisions = pushedLog.Seen.Count(seen => seen.Other == pusher);
            int ended = pushedLog.Ended.Count(end => end.Other == pusher);
            samples.Add(
                new(
                    Tick: tick,
                    ForceSteps: forceSteps,
                    Creep: offset.X - separation,
                    LateralSlip: Math.Sqrt(offset.Y * offset.Y + offset.Z * offset.Z),
                    RelativeVelocityRms: Math.Sqrt(relativeSpeedSquares / tick),
                    RelativeVelocityMax: relativeSpeedMax,
                    CommonVelocityError: (centerVelocity - exactVelocity).Length,
                    CommonPositionError: (center - exactPosition).Length,
                    CommonPositionErrorVsStepRule: (center - stepRulePosition).Length,
                    CollisionsTotal: collisions,
                    CollisionsEnded: ended,
                    PusherTilt: TiltDegrees(pusherPose.Rotation),
                    PushedTilt: TiltDegrees(pushedPose.Rotation),
                    LargestDistanceSoFar: largestDistance,
                    WorldErrors: world.Errors.Count,
                    BackendWarnings: Volatile.Read(ref warnings)
                )
            );
        }

        return new(StartPoint: start, Samples: samples, FarthestPoint: farthestPoint, LargestDistance: largestDistance);
    }

    /// <summary>The angle of <paramref name="rotation"/> from the start orientation (the identity), in degrees, from 0 to 180.</summary>
    private static double TiltDegrees(DQuaternion rotation)
    {
        DQuaternion unit = rotation.Normalized();
        return 2 * Math.Acos(Math.Min(1, Math.Abs(unit.W))) * (180 / Math.PI);
    }
}
