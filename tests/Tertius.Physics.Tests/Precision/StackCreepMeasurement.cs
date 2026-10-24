using Tertius.Simulation;
using static Tertius.Physics.Tests.Precision.ContactRuns;

namespace Tertius.Physics.Tests.Precision;

/// <summary>
/// Measures how a resting stack creeps and jitters when it moves at a common speed (ticket ter-kugq). A kinematic
/// platform, a box with sides of 10 m whose top is the scene's floor, moves at the common velocity V along d, in a
/// uniform field of 9.81 m/s² along -Y. Three dynamic boxes with sides of 1 m and 10 kg rest on it in a stack, with the
/// same velocity. The exact answer is that the stack rests on the platform: its creep and its jitter are zero. The run at
/// V = 0 is Jolt's own answer, and there the stack can sleep, which freezes it. Writes stack-creep.csv.
/// </summary>
public sealed class StackCreepMeasurement
{
    private const float Gravity = 9.81f;

    /// <summary>The ticks before the measurement starts, 2 s at 60 Hz, so that the stack settles.</summary>
    private const int SettleTicks = 120;

    /// <summary>The ticks of the measurement after the settle, 60 s at 60 Hz.</summary>
    private const int MeasureTicks = 3600;

    private const int BoxCount = 3;

    /// <summary>Half the side of the platform, in meters.</summary>
    private const double PlatformHalf = 5;

    /// <summary>The ticks after the settle at which a row is written: 5 s, 20 s and 60 s.</summary>
    private static readonly int[] ReportTicks = [300, 1200, 3600];

    /// <summary>
    /// A cube with sides of 10 m, which the engine moves with its velocity. No numeric frame and no target moves it, so
    /// Jolt keeps its spawn velocity.
    /// </summary>
    private static readonly BodyDesc PlatformBody = new(
        Shape: new BoxShapeDesc(new((float)PlatformHalf)),
        Motion: BodyMotion.Kinematic
    );

    /// <summary>
    /// A cube with sides of 1 m and 10 kg, with the default friction (0.5), restitution (0) and angular damping (0.05). It
    /// has no linear damping, because Jolt damps the whole velocity relative to its system's origin, so a damping would
    /// slow a stack that moves at V by a real force, which is no rounding error.
    /// </summary>
    private static readonly BodyDesc BoxBody = new(Shape: new BoxShapeDesc(new(0.5f)), Mass: 10, LinearDamping: 0);

    /// <summary>What one box shows at one report time.</summary>
    /// <param name="Offset">Its position minus the platform's, in meters.</param>
    /// <param name="Creep">How far <paramref name="Offset"/> moved since the end of the settle, in meters.</param>
    /// <param name="ReadVelocity">Its velocity minus the platform's, both as World.VelocityIn reads them, in m/s.</param>
    /// <param name="JitterRms">The root mean square of its velocity relative to the platform since the end of the settle, in m/s.</param>
    /// <param name="JitterRmsUp">The same of the vertical part only.</param>
    /// <param name="JitterRmsAcross">The same of the horizontal part only.</param>
    /// <param name="JitterMax">The largest length of its velocity relative to the platform since the end of the settle, in m/s.</param>
    /// <param name="Tilt">The angle of its rotation, in radians.</param>
    /// <param name="Asleep">True when Jolt had the box asleep at the report time.</param>
    /// <param name="Slept">True when Jolt had the box asleep at the end of any tick so far, the settle included.</param>
    private readonly record struct BoxReport(
        Double3 Offset,
        Double3 Creep,
        Double3 ReadVelocity,
        double JitterRms,
        double JitterRmsUp,
        double JitterRmsAcross,
        double JitterMax,
        double Tilt,
        bool Asleep,
        bool Slept
    );

    /// <summary>What the stack shows at one report time.</summary>
    /// <param name="Stands">True when every box rests on the one below, as <see cref="Stands"/> says.</param>
    /// <param name="FirstSleepTick">The first tick at whose end Jolt had any body asleep, or -1.</param>
    private sealed record StackReport(
        BoxReport[] Boxes,
        bool Stands,
        bool PlatformAsleep,
        bool PlatformSlept,
        long FirstSleepTick
    );

    /// <param name="LargestDistance">The largest distance of any body's center from the origin of the space during the run, in meters.</param>
    /// <param name="Warnings">The warnings of the backend plus the errors of the world during the run.</param>
    private sealed record StackRun(StackReport[] Reports, double LargestDistance, int Warnings);

    [Fact(Explicit = true)]
    public void StackCreep()
    {
        var csv = new Csv(
            "run (speed or distance-control)",
            "direction (d)",
            "common_speed_m_per_s",
            "largest_distance_m",
            "box (1 = bottom)",
            "time_after_settle_s",
            "creep_x_m",
            "creep_y_up_m",
            "creep_z_m",
            "creep_m",
            "offset_from_v0_up_m",
            "offset_from_v0_m",
            "jitter_rms_m_per_s",
            "jitter_rms_up_m_per_s",
            "jitter_rms_across_m_per_s",
            "jitter_max_m_per_s",
            "read_velocity_up_m_per_s",
            "read_velocity_across_m_per_s",
            "tilt_rad",
            "stack_stands",
            "box_asleep",
            "box_slept_so_far",
            "platform_asleep",
            "platform_slept_so_far",
            "first_sleep_tick",
            "warnings",
            "note"
        );

        StackRun reference = Run(0, Double3.UnitX, Double3.Zero);
        double totalTime = (SettleTicks + MeasureTicks) * (double)(1f / 60);
        foreach (Direction direction in new[] { AlongX, Diagonal })
        foreach (double speed in Speeds)
        {
            // The path passes the origin at the middle of the run.
            StackRun run =
                speed == 0 ? reference : Run(speed, direction.Unit, direction.Unit * (-speed * totalTime / 2));
            Write(
                csv: csv,
                kind: "speed",
                direction: direction,
                speed: speed,
                run: run,
                reference: reference,
                note: speed == 0 ? "V = 0: the stack can sleep, which freezes its creep and jitter" : ""
            );
            if (speed == 0)
                continue;
            StackRun control = Run(0, direction.Unit, direction.Unit * run.LargestDistance);
            Write(
                csv: csv,
                kind: "distance-control",
                direction: direction,
                speed: 0,
                run: control,
                reference: reference,
                note: $"V = 0, the scene placed at the largest distance of the run at {speed} m/s: the stack can sleep"
            );
        }

        csv.Save("stack-creep");
        csv.Rows.Should().Be(2 * (2 * Speeds.Length - 1) * BoxCount * ReportTicks.Length);
    }

    private static void Write(
        Csv csv,
        string kind,
        Direction direction,
        double speed,
        StackRun run,
        StackRun reference,
        string note
    )
    {
        for (int r = 0; r < ReportTicks.Length; r++)
        {
            StackReport report = run.Reports[r];
            for (int k = 0; k < BoxCount; k++)
            {
                BoxReport box = report.Boxes[k];
                Double3 fromReference = box.Offset - reference.Reports[r].Boxes[k].Offset;
                (double up, double across) = Split(box.ReadVelocity, Double3.UnitY);
                csv.Add(
                    kind,
                    direction.Name,
                    speed,
                    run.LargestDistance,
                    k + 1,
                    ReportTicks[r] / 60.0,
                    box.Creep.X,
                    box.Creep.Y,
                    box.Creep.Z,
                    box.Creep.Length,
                    fromReference.Y,
                    fromReference.Length,
                    box.JitterRms,
                    box.JitterRmsUp,
                    box.JitterRmsAcross,
                    box.JitterMax,
                    up,
                    across,
                    box.Tilt,
                    report.Stands,
                    box.Asleep,
                    box.Slept,
                    report.PlatformAsleep,
                    report.PlatformSlept,
                    report.FirstSleepTick,
                    run.Warnings,
                    note
                );
            }
        }
    }

    /// <summary>
    /// Runs the scene with the common velocity <paramref name="direction"/> times <paramref name="speed"/>, with the top
    /// center of the platform at <paramref name="origin"/> at the start.
    /// </summary>
    private static StackRun Run(double speed, Double3 direction, Double3 origin)
    {
        var warnings = new List<string>();
        using World world = NewWorld(out JoltPhysicsBackend backend, warnings);
        FrameHandle space = Space(world, new(0, -Gravity, 0));
        Double3 velocity = direction * speed;
        Entity platform = world.Spawn(
            new()
            {
                Frame = space,
                Pose = At(origin + new Double3(0, -PlatformHalf, 0)),
                LinearVelocity = velocity,
                Body = PlatformBody,
            }
        );
        var boxes = new Entity[BoxCount];
        for (int k = 0; k < BoxCount; k++)
            boxes[k] = world.Spawn(
                new()
                {
                    Frame = space,
                    Pose = At(origin + new Double3(0, 0.5 + k, 0)),
                    LinearVelocity = velocity,
                    Body = BoxBody,
                }
            );
        Entity[] bodies = [platform, .. boxes];

        double largest = 0;
        bool[] slept = new bool[bodies.Length];
        bool[] asleep = new bool[bodies.Length];
        long firstSleep = -1;

        void Step()
        {
            world.Tick();
            foreach (Entity body in bodies)
                largest = Math.Max(largest, world.PoseIn(body, space).Position.Length);
            Asleep(backend: backend, space: space, bodies: bodies, asleep: asleep);
            for (int i = 0; i < bodies.Length; i++)
            {
                slept[i] |= asleep[i];
                if (asleep[i] && firstSleep < 0)
                    firstSleep = world.TickNumber;
            }
        }

        for (int i = 0; i < SettleTicks; i++)
            Step();
        var settled = new Double3[BoxCount];
        var last = new Double3[BoxCount];
        for (int k = 0; k < BoxCount; k++)
            settled[k] = last[k] = Offset(world: world, space: space, box: boxes[k], platform: platform);

        double[] sumSquares = new double[BoxCount];
        double[] sumSquaresUp = new double[BoxCount];
        double[] sumSquaresAcross = new double[BoxCount];
        double[] max = new double[BoxCount];
        var reports = new List<StackReport>();
        double dt = world.DeltaTime;
        for (int tick = 1; tick <= MeasureTicks; tick++)
        {
            Step();
            var offsets = new Double3[BoxCount];
            for (int k = 0; k < BoxCount; k++)
            {
                offsets[k] = Offset(world: world, space: space, box: boxes[k], platform: platform);
                // The velocity relative to the platform over the step, from the positions: the motion itself. A read of
                // an awake body's velocity adds half a step of gravity to it (the instant velocity at the end of the step).
                Double3 relative = (offsets[k] - last[k]) / dt;
                last[k] = offsets[k];
                (double up, double across) = Split(relative, Double3.UnitY);
                sumSquares[k] += relative.LengthSquared;
                sumSquaresUp[k] += up * up;
                sumSquaresAcross[k] += across * across;
                max[k] = Math.Max(max[k], relative.Length);
            }

            if (Array.IndexOf(ReportTicks, tick) < 0)
                continue;
            var boxReports = new BoxReport[BoxCount];
            for (int k = 0; k < BoxCount; k++)
            {
                Double3 read = world.VelocityIn(boxes[k], space) - world.VelocityIn(platform, space);
                boxReports[k] = new(
                    Offset: offsets[k],
                    Creep: offsets[k] - settled[k],
                    ReadVelocity: read,
                    JitterRms: Math.Sqrt(sumSquares[k] / tick),
                    JitterRmsUp: Math.Sqrt(sumSquaresUp[k] / tick),
                    JitterRmsAcross: Math.Sqrt(sumSquaresAcross[k] / tick),
                    JitterMax: max[k],
                    Tilt: AngleOf(world.PoseIn(boxes[k], space).Rotation),
                    Asleep: asleep[k + 1],
                    Slept: slept[k + 1]
                );
            }

            reports.Add(
                new(
                    Boxes: boxReports,
                    Stands: Stands(offsets),
                    PlatformAsleep: asleep[0],
                    PlatformSlept: slept[0],
                    FirstSleepTick: firstSleep
                )
            );
        }

        return new([.. reports], largest, warnings.Count + world.Errors.Count);
    }

    /// <summary>The position of <paramref name="box"/> minus the position of the platform's top center, in meters.</summary>
    private static Double3 Offset(World world, FrameHandle space, Entity box, Entity platform) =>
        world.PoseIn(box, space).Position - (world.PoseIn(platform, space).Position + new Double3(0, PlatformHalf, 0));

    /// <summary>
    /// True when each box rests on the one below it, by the geometry of the boxes alone: the bottom box's center is above
    /// the platform's top and less than 1 m (one box) above it, and within the platform's top face across; each other box's
    /// center is 0.5 m to 1.5 m (half a box to one and a half) above the center of the box below, and within 0.5 m of it
    /// along X and along Z, so over that box's top face.
    /// </summary>
    /// <param name="offsets">The offset of each box from the platform's top center, bottom box first.</param>
    private static bool Stands(Double3[] offsets)
    {
        Double3 bottom = offsets[0];
        if (bottom.Y <= 0 || bottom.Y >= 1 || Math.Abs(bottom.X) >= PlatformHalf || Math.Abs(bottom.Z) >= PlatformHalf)
            return false;
        for (int k = 1; k < offsets.Length; k++)
        {
            Double3 step = offsets[k] - offsets[k - 1];
            if (step.Y <= 0.5 || step.Y >= 1.5 || Math.Abs(step.X) >= 0.5 || Math.Abs(step.Z) >= 0.5)
                return false;
        }

        return true;
    }

    /// <summary>Fills <paramref name="asleep"/> with whether Jolt has each body of <paramref name="bodies"/> asleep, from one read of the system.</summary>
    private static void Asleep(JoltPhysicsBackend backend, FrameHandle space, Entity[] bodies, bool[] asleep)
    {
        foreach (JoltPhysicsBackend.BodyFacts facts in backend.BodiesIn(space))
        {
            if (BodyUserData.Kind(facts.UserData) != BodyKind.Primary)
                continue;
            for (int i = 0; i < bodies.Length; i++)
                if (BodyUserData.Names(facts.UserData, bodies[i]))
                    asleep[i] = !facts.Active;
        }
    }
}
