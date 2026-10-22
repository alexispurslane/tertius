using System.Numerics;
using Tertius.Simulation;
using static Tertius.Physics.Tests.Precision.ContactRuns;

namespace Tertius.Physics.Tests.Precision;

/// <summary>
/// Measures how a slow collision changes when both bodies move at a common speed (ticket ter-kugq). In zero gravity, box A
/// moves along +X toward box B at the approach speed u relative to it, and both have the common velocity V along d. The
/// boxes have sides of 1 m and 2 kg, no damping, and the restitution e. Their facing sides start 0.5 m apart, so they touch
/// after 0.5 m / u: after 1 s at 0.5 m/s, after 0.25 s at 2 m/s. Each run lasts 4 s. The expectation is the same run at
/// V = 0, shifted by V t. Writes slow-collision.csv.
/// </summary>
public sealed class SlowCollisionMeasurement
{
    /// <summary>The ticks of each run, 4 s at 60 Hz.</summary>
    private const int Ticks = 240;

    /// <summary>The gap between the facing sides of the two boxes at the start, in meters.</summary>
    private const double StartGap = 0.5;

    /// <summary>The ticks at which a row is written: 2 s and 4 s.</summary>
    private static readonly int[] ReportTicks = [120, 240];

    /// <summary>How fast box A approaches box B, in m/s.</summary>
    private static readonly double[] ApproachSpeeds = [0.5, 2];

    private static readonly float[] Restitutions = [0, 0.5f];

    /// <summary>
    /// The directions of the common velocity: along the approach, across it, and the diagonal. The collision has no
    /// gravity or thrust, so both axes count as the easy case.
    /// </summary>
    private static readonly Direction[] Directions = [AlongX, AlongZ, Diagonal];

    /// <summary>A box with sides of 1 m and 2 kg that nothing slows down, with the restitution <paramref name="restitution"/>.</summary>
    private static BodyDesc BoxBody(float restitution) =>
        new(new BoxShapeDesc(new(0.5f)), Mass: 2, Restitution: restitution, LinearDamping: 0, AngularDamping: 0);

    /// <summary>The state of the pair at one report time.</summary>
    /// <param name="MovedA">How far box A moved since its spawn, in meters.</param>
    /// <param name="MovedB">How far box B moved since its spawn, in meters.</param>
    /// <param name="Relative">The velocity of A minus the velocity of B, as World.VelocityIn reads them, in m/s.</param>
    /// <param name="Collisions">The Collision events that box A heard so far.</param>
    /// <param name="Ended">The CollisionEnded events that box A heard so far.</param>
    /// <param name="LargestOverlap">
    /// The largest value so far, over the ends of all ticks, of 1 m minus the distance of the two centers along X, in
    /// meters: the depth of the penetration when it is positive, and the smallest gap when it is negative.
    /// </param>
    /// <param name="LargestTilt">The largest angle of either box's rotation so far, in radians. The overlap assumes that the boxes stay square to X.</param>
    private readonly record struct PairReport(
        Double3 MovedA,
        Double3 MovedB,
        Double3 Relative,
        int Collisions,
        int Ended,
        double LargestOverlap,
        double LargestTilt);

    /// <param name="FirstCollisionTick">The tick in which box A heard its first Collision, or -1.</param>
    /// <param name="FirstImpulse">The impulse of that Collision, in N·s, or 0.</param>
    private sealed record PairRun(
        PairReport[] Reports,
        long FirstCollisionTick,
        double FirstImpulse,
        double LargestDistance,
        int Warnings);

    [Fact(Explicit = true)]
    public void SlowCollision()
    {
        var csv = new Csv(
            "run (speed or distance-control)",
            "direction (d)",
            "common_speed_m_per_s",
            "largest_distance_m",
            "approach_speed_m_per_s",
            "restitution",
            "time_s",
            "relative_velocity_along_x_m_per_s",
            "relative_velocity_across_m_per_s",
            "relative_velocity_analytic_along_x_m_per_s",
            "relative_velocity_error_along_x_m_per_s",
            "relative_velocity_error_across_m_per_s",
            "relative_position_error_along_x_m",
            "relative_position_error_across_m",
            "mean_position_error_along_x_m",
            "mean_position_error_across_m",
            "first_collision_tick",
            "first_collision_tick_minus_v0",
            "first_collision_impulse_n_s",
            "collision_events",
            "collision_ended_events",
            "largest_overlap_m",
            "largest_overlap_minus_v0_m",
            "largest_tilt_rad",
            "warnings");

        int rowSets = 0;
        foreach (double approach in ApproachSpeeds)
        foreach (float restitution in Restitutions)
        {
            PairRun reference = Run(0, Double3.UnitX, Double3.Zero, approach, restitution);
            foreach (Direction direction in Directions)
            foreach (double speed in Speeds)
            {
                PairRun run = speed == 0
                    ? reference
                    : Run(speed, direction.Unit, direction.Unit * (-speed * Ticks * (1f / 60) / 2), approach,
                        restitution);
                Write(csv, "speed", direction, speed, approach, restitution, run, reference);
                rowSets++;
                if (speed == 0)
                    continue;
                PairRun control = Run(0, direction.Unit, direction.Unit * run.LargestDistance, approach, restitution);
                Write(csv, "distance-control", direction, 0, approach, restitution, control, reference);
                rowSets++;
            }
        }

        csv.Save("slow-collision");
        csv.Rows.Should().Be(rowSets * ReportTicks.Length).And.Be(ApproachSpeeds.Length * Restitutions.Length *
                                                                  Directions.Length * (2 * Speeds.Length - 1) *
                                                                  ReportTicks.Length);
    }

    private static void Write(Csv csv, string kind, Direction direction, double speed, double approach,
        float restitution, PairRun run, PairRun reference)
    {
        // Jolt bounces a contact only when it approaches faster than 1 m/s (MinVelocityForRestitution), and in zero gravity
        // the engine keeps every bounce. Equal masses that bounce part with e times the approach speed.
        double analytic = approach > FrameWorld.JoltMinVelocityForRestitution ? -restitution * approach : 0;
        // In zero gravity, the exact motion at V is the motion at V = 0 plus V t. Box A and box B start at the same
        // offsets from the scene's origin in every run, so their motion since the spawn is comparable.
        for (int r = 0; r < ReportTicks.Length; r++)
        {
            PairReport at = run.Reports[r];
            PairReport expected = reference.Reports[r];
            double time = ReportTicks[r] * (double)(1f / 60);
            Double3 shift = direction.Unit * (speed * time);
            (double relativeAlong, double relativeAcross) = Split(at.Relative, Double3.UnitX);
            (double velocityErrorAlong, double velocityErrorAcross) =
                Split(at.Relative - expected.Relative, Double3.UnitX);
            (double positionErrorAlong, double positionErrorAcross) =
                Split(at.MovedA - at.MovedB - (expected.MovedA - expected.MovedB), Double3.UnitX);
            Double3 meanError = (at.MovedA + at.MovedB) / 2 - shift - (expected.MovedA + expected.MovedB) / 2;
            (double meanErrorAlong, double meanErrorAcross) = Split(meanError, Double3.UnitX);
            csv.Add(kind, direction.Name, speed, run.LargestDistance, approach, restitution, time,
                relativeAlong, relativeAcross, analytic, velocityErrorAlong, velocityErrorAcross,
                positionErrorAlong, positionErrorAcross, meanErrorAlong, meanErrorAcross,
                run.FirstCollisionTick, run.FirstCollisionTick - reference.FirstCollisionTick, run.FirstImpulse,
                at.Collisions, at.Ended, at.LargestOverlap, at.LargestOverlap - expected.LargestOverlap, at.LargestTilt,
                run.Warnings);
        }
    }

    /// <summary>
    /// Runs the scene with the common velocity <paramref name="direction"/> times <paramref name="speed"/>, with the
    /// middle between the two boxes at <paramref name="origin"/> at the start.
    /// </summary>
    private static PairRun Run(double speed, Double3 direction, Double3 origin, double approach, float restitution)
    {
        var warnings = new List<string>();
        using World world = NewWorld(out _, warnings);
        FrameHandle space = Space(world, Vector3.Zero);
        Double3 common = direction * speed;
        double half = (1 + StartGap) / 2;
        Double3 startA = origin - Double3.UnitX * half;
        Double3 startB = origin + Double3.UnitX * half;
        var logA = new CollisionLog();
        Entity a = world.Spawn(new()
        {
            Frame = space, Pose = At(startA), LinearVelocity = common + Double3.UnitX * approach,
            Body = BoxBody(restitution), Components = [logA]
        });
        Entity b = world.Spawn(new()
            { Frame = space, Pose = At(startB), LinearVelocity = common, Body = BoxBody(restitution) });

        double largest = Math.Max(startA.Length, startB.Length);
        double largestOverlap = double.NegativeInfinity;
        double largestTilt = 0;
        var reports = new List<PairReport>();
        for (int tick = 1; tick <= Ticks; tick++)
        {
            world.Tick();
            DPose poseA = world.PoseIn(a, space);
            DPose poseB = world.PoseIn(b, space);
            largest = Math.Max(largest, Math.Max(poseA.Position.Length, poseB.Position.Length));
            largestOverlap = Math.Max(largestOverlap, 1 - (poseB.Position.X - poseA.Position.X));
            largestTilt = Math.Max(largestTilt, Math.Max(AngleOf(poseA.Rotation), AngleOf(poseB.Rotation)));
            if (Array.IndexOf(ReportTicks, tick) < 0)
                continue;
            int collisions = logA.Seen.Count(seen => seen.Other == b);
            int ended = logA.Ended.Count(seen => seen.Other == b);
            reports.Add(new(poseA.Position - startA, poseB.Position - startB,
                world.VelocityIn(a, space) - world.VelocityIn(b, space), collisions, ended, largestOverlap,
                largestTilt));
        }

        (long Tick, Entity Other, float Impulse) first = logA.Seen.FirstOrDefault(seen => seen.Other == b);
        bool hit = logA.Seen.Any(seen => seen.Other == b);
        return new([.. reports], hit ? first.Tick : -1, hit ? first.Impulse : 0, largest,
            warnings.Count + world.Errors.Count);
    }
}
