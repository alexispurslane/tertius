using System.Numerics;
using Tertius.Simulation;
using static Tertius.Physics.Tests.Precision.ContactRuns;

namespace Tertius.Physics.Tests.Precision;

/// <summary>
/// The scene of the two false-hit measurements (ticket ter-kugq). In zero gravity, a rear body and a front body move
/// together at the common velocity V along d for 2 s, the front body ahead along d, with a gap g between the front body's
/// back face and the rear body's front. Nothing pushes either, so the exact answer is that the gap stays g and the
/// relative velocity stays zero. A false hit is a hit of a sweep or a cast that tests one body's motion of a whole tick
/// against the other body where it was at the start of the step. The gap is measured along d from the two centers, so the
/// front body's back face is square to d: the front body turns +X onto d. Each run writes three rows, at 0.5 s, 1 s and 2 s.
/// </summary>
internal static class MovingPair
{
    /// <summary>The ticks of each run, 2 s at 60 Hz.</summary>
    private const int Ticks = 120;

    /// <summary>The ticks at which a row is written: 0.5 s, 1 s and 2 s.</summary>
    private static readonly int[] ReportTicks = [30, 60, 120];

    /// <summary>The gaps g, in meters, from 0.05 m to 100 m in steps of a factor of 2 to 2.5.</summary>
    public static readonly double[] Gaps = [0.05, 0.1, 0.2, 0.5, 1, 2, 5, 10, 20, 50, 100];

    /// <summary>The direction of the common velocity along the gap, and the diagonal.</summary>
    private static readonly Direction[] Directions = [AlongX, Diagonal];

    /// <summary>The number of rows that <see cref="Measure"/> writes.</summary>
    public static int RowCount => Directions.Length * (2 * Speeds.Length - 1) * Gaps.Length * ReportTicks.Length;

    /// <summary>One of the two bodies.</summary>
    /// <param name="Body">Its description.</param>
    /// <param name="Reach">How far its surface reaches from its center along the unit vector d, in meters, for each d.</param>
    /// <param name="TurnsOntoD">True when it spawns turned +X onto d, false when it spawns with no rotation.</param>
    public sealed record Member(BodyDesc Body, Func<Double3, double> Reach, bool TurnsOntoD);

    /// <summary>The pair at one report time. Each largest value is over the ends of all ticks so far.</summary>
    /// <param name="GapError">The gap minus g, in meters: the gap is the distance of the centers along d minus both reaches.</param>
    /// <param name="LargestGapError">The largest length of <paramref name="GapError"/>, in meters.</param>
    /// <param name="Across">The length of the part of the front center minus the rear center across d, in meters. It starts at 0.</param>
    /// <param name="LargestAcross">The largest value of <paramref name="Across"/>, in meters.</param>
    /// <param name="RelativeAlong">The part along d of the front body's velocity minus the rear body's, as World.VelocityIn reads them, in m/s.</param>
    /// <param name="RelativeAcross">The length of the part of that relative velocity across d, in m/s.</param>
    /// <param name="LargestRelative">The largest length of that relative velocity, in m/s.</param>
    /// <param name="LargestRearChange">The largest length of the rear body's velocity minus V d, in m/s.</param>
    /// <param name="LargestFrontChange">The largest length of the front body's velocity minus V d, in m/s.</param>
    /// <param name="LargestFrontTilt">The largest angle between the front body's rotation and its rotation at the spawn, in radians.</param>
    /// <param name="RearCollisions">The Collision events about the front body that the rear body heard so far.</param>
    /// <param name="FrontCollisions">The Collision events about the rear body that the front body heard so far.</param>
    /// <param name="RearEnded">The CollisionEnded events about the front body that the rear body heard so far.</param>
    private readonly record struct PairReport(
        double GapError,
        double LargestGapError,
        double Across,
        double LargestAcross,
        double RelativeAlong,
        double RelativeAcross,
        double LargestRelative,
        double LargestRearChange,
        double LargestFrontChange,
        double LargestFrontTilt,
        int RearCollisions,
        int FrontCollisions,
        int RearEnded);

    /// <param name="FirstCollisionTick">The first tick in which either body heard a Collision about the other, or -1.</param>
    private sealed record PairRun(PairReport[] Reports, long FirstCollisionTick, double LargestDistance, int Warnings);

    /// <summary>Runs every common speed, direction and gap, and every distance control, and returns the table.</summary>
    public static Csv Measure(Member rear, Member front)
    {
        var csv = new Csv(
            "run (speed or distance-control)",
            "direction (d)",
            "common_speed_m_per_s",
            "sweep_length_per_tick_m",
            "largest_distance_m",
            "gap_m",
            "time_s",
            "gap_error_m",
            "gap_error_largest_m",
            "offset_across_m",
            "offset_across_largest_m",
            "relative_velocity_along_m_per_s",
            "relative_velocity_across_m_per_s",
            "relative_velocity_largest_m_per_s",
            "rear_velocity_change_largest_m_per_s",
            "front_velocity_change_largest_m_per_s",
            "front_tilt_largest_rad",
            "rear_collision_events",
            "front_collision_events",
            "rear_collision_ended_events",
            "first_collision_tick",
            "warnings");
        float dt = 1f / 60;
        double runTime = Ticks * (double)dt;
        foreach (Direction direction in Directions)
        foreach (double speed in Speeds)
        foreach (double gap in Gaps)
        {
            // The path passes the origin at the middle of the run.
            PairRun run = Run(rear, front, speed, direction.Unit, direction.Unit * (-speed * runTime / 2), gap);
            Write(csv, "speed", direction, speed, speed * dt, gap, run);
            if (speed == 0)
                continue;
            PairRun control = Run(rear, front, 0, direction.Unit, direction.Unit * run.LargestDistance, gap);
            Write(csv, "distance-control", direction, 0, 0, gap, control);
        }

        return csv;
    }

    private static void Write(Csv csv, string kind, Direction direction, double speed, double sweep, double gap,
        PairRun run)
    {
        for (int r = 0; r < ReportTicks.Length; r++)
        {
            PairReport at = run.Reports[r];
            csv.Add(kind, direction.Name, speed, sweep, run.LargestDistance, gap, ReportTicks[r] / 60.0,
                at.GapError, at.LargestGapError, at.Across, at.LargestAcross, at.RelativeAlong, at.RelativeAcross,
                at.LargestRelative,
                at.LargestRearChange, at.LargestFrontChange, at.LargestFrontTilt, at.RearCollisions, at.FrontCollisions,
                at.RearEnded,
                run.FirstCollisionTick, run.Warnings);
        }
    }

    /// <summary>
    /// Runs the pair with the common velocity <paramref name="direction"/> times <paramref name="speed"/>, with the rear
    /// body's center at <paramref name="origin"/> at the start and the gap <paramref name="gap"/>.
    /// </summary>
    private static PairRun Run(Member rear, Member front, double speed, Double3 direction, Double3 origin, double gap)
    {
        var warnings = new List<string>();
        using World world = NewWorld(out _, warnings);
        FrameHandle space = Space(world, Vector3.Zero);
        Double3 velocity = direction * speed;
        DQuaternion turn = TurnXOnto(direction);
        double reaches = rear.Reach(direction) + front.Reach(direction);
        var rearLog = new CollisionLog();
        var frontLog = new CollisionLog();
        Entity rearBody = world.Spawn(new()
        {
            Frame = space,
            Pose = new(origin, rear.TurnsOntoD ? turn : DQuaternion.Identity),
            LinearVelocity = velocity,
            Body = rear.Body,
            Components = [rearLog]
        });
        DQuaternion frontStart = front.TurnsOntoD ? turn : DQuaternion.Identity;
        Entity frontBody = world.Spawn(new()
        {
            Frame = space,
            Pose = new(origin + direction * (reaches + gap), frontStart),
            LinearVelocity = velocity,
            Body = front.Body,
            Components = [frontLog]
        });

        double largestDistance = (origin + direction * (reaches + gap)).Length;
        double largestGapError = 0,
            largestAcross = 0,
            largestRelative = 0,
            largestRearChange = 0,
            largestFrontChange = 0,
            largestTilt = 0;
        var reports = new List<PairReport>();
        for (int tick = 1; tick <= Ticks; tick++)
        {
            world.Tick();
            DPose rearPose = world.PoseIn(rearBody, space);
            DPose frontPose = world.PoseIn(frontBody, space);
            Double3 rearVelocity = world.VelocityIn(rearBody, space);
            Double3 frontVelocity = world.VelocityIn(frontBody, space);
            largestDistance = Math.Max(largestDistance, Math.Max(rearPose.Position.Length, frontPose.Position.Length));
            (double centers, double across) = Split(frontPose.Position - rearPose.Position, direction);
            double gapError = centers - reaches - gap;
            (double relativeAlong, double relativeAcross) = Split(frontVelocity - rearVelocity, direction);
            largestGapError = Math.Max(largestGapError, Math.Abs(gapError));
            largestAcross = Math.Max(largestAcross, across);
            largestRelative = Math.Max(largestRelative, (frontVelocity - rearVelocity).Length);
            largestRearChange = Math.Max(largestRearChange, (rearVelocity - velocity).Length);
            largestFrontChange = Math.Max(largestFrontChange, (frontVelocity - velocity).Length);
            largestTilt = Math.Max(largestTilt, AngleOf(frontStart.Conjugate() * frontPose.Rotation));
            if (Array.IndexOf(ReportTicks, tick) < 0)
                continue;
            reports.Add(new(gapError, largestGapError, across, largestAcross, relativeAlong, relativeAcross,
                largestRelative,
                largestRearChange, largestFrontChange, largestTilt,
                rearLog.CountWith(frontBody), frontLog.CountWith(rearBody),
                rearLog.Ended.Count(ended => ended.Other == frontBody)));
        }

        long[] firstTicks =
        [
            .. rearLog.Seen.Where(seen => seen.Other == frontBody).Select(seen => seen.Tick),
            .. frontLog.Seen.Where(seen => seen.Other == rearBody).Select(seen => seen.Tick)
        ];
        return new([.. reports], firstTicks.Length > 0 ? firstTicks.Min() : -1, largestDistance,
            warnings.Count + world.Errors.Count);
    }
}
