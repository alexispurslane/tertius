using System.Numerics;
using Tertius.Demo;
using Tertius.Simulation;

namespace Tertius.Host.Tests;

/// <summary>
/// One headless run of the checked-in script scripts/surface-yard.json, on the scene surface-yard: a yard on Earth's
/// surface, 6,371 km from the origin of its frame earth-surface, where only resting numeric frames keep the bodies near a
/// Jolt origin. After 2 s, when the crates of the stack rest, the person walks 10 m toward the stack and stops. It jumps
/// once in place and lands. Then it walks on into the top crate, which sticks out toward it, pushes that crate off the
/// far side of the stack, so that the crate falls 1 m to the floor, and stops against the heavy crates below. The run
/// ends 60 s after the crates came to rest. The fixture samples the yard's bodies after every tick, and every test of the
/// class reads that one run.
/// </summary>
public sealed class SurfaceYardRun : CheckedInRun
{
    public SurfaceYardRun() : this(new YardRecord())
    {
    }

    private SurfaceYardRun(YardRecord record) : base("surface-yard", record.Take)
    {
        Record = record;
        Measured = new YardMeasurements(this, record);
    }

    /// <summary>The state of the yard's bodies after every tick of the run.</summary>
    internal YardRecord Record { get; }

    /// <summary>What the tests assert and the report shows, measured once from <see cref="Record"/>.</summary>
    internal YardMeasurements Measured { get; }
}

/// <summary>
/// One body of the yard after one tick, read as the tests may read it. Positions and velocities are in the frame
/// earth-surface, whose origin is Earth's center, with +Y up through the yard.
/// </summary>
/// <param name="Pose">The body's pose in earth-surface, in meters.</param>
/// <param name="Velocity">The body's velocity in earth-surface, in m/s.</param>
/// <param name="AngularVelocity">The body's angular velocity in earth-surface's axes, in rad/s.</param>
/// <param name="Home">The frame whose Jolt system holds the body: earth-surface, or a numeric frame below it.</param>
/// <param name="HomeMode">How the home moves as a numeric frame, or <see cref="NumericMode.None"/> for earth-surface.</param>
internal readonly record struct BodySample(DPose Pose, Double3 Velocity, Vector3 AngularVelocity, FrameHandle Home, NumericMode HomeMode);

/// <summary>The yard after one tick.</summary>
/// <param name="Tick">The number of ticks run so far: 0 for the scene as built.</param>
/// <param name="Time">The world's time, in seconds.</param>
/// <param name="Bodies">The bodies of <see cref="YardRecord.Bodies"/>, in that order.</param>
/// <param name="PersonStanding">True if physics stood the person on walkable ground in the tick's step.</param>
/// <param name="NumericFrames">How many numeric frames the frame tree holds.</param>
/// <param name="Errors">How many errors <see cref="World.Errors"/> holds.</param>
internal sealed record TickSample(long Tick, double Time, BodySample[] Bodies, bool PersonStanding, int NumericFrames, int Errors);

/// <summary>Samples the yard's bodies after every tick. The run calls <see cref="Take"/> once per tick, in tick order.</summary>
internal sealed class YardRecord
{
    /// <summary>The scene ids of the bodies that the record samples, in the order of <see cref="TickSample.Bodies"/>.</summary>
    public static readonly string[] Bodies = ["floor", "bottom-crate", "middle-crate", "top-crate", "person"];

    public const int Floor = 0, BottomCrate = 1, MiddleCrate = 2, TopCrate = 3, Person = 4;

    /// <summary>The samples, one per tick, where the sample at index i is the state after tick i.</summary>
    public List<TickSample> Ticks { get; } = new();

    public void Take(World world, SceneInstance scene)
    {
        FrameHandle surface = world.GetOwnedFrame(scene["earth-surface"]);
        var bodies = new BodySample[Bodies.Length];
        for (int i = 0; i < Bodies.Length; i++)
        {
            Entity entity = scene[Bodies[i]];
            FrameHandle home = world.PhysicsHomeOf(entity);
            bodies[i] = new BodySample(world.PoseIn(entity, surface), world.VelocityIn(entity, surface),
                world.AngularVelocityIn(entity, surface), home, home.IsNull ? NumericMode.None : world.Frames.Numeric(home));
        }
        Ticks.Add(new TickSample(world.TickNumber, world.Time, bodies, world.IsStanding(scene["person"]), world.Frames.NumericCount, world.Errors.Count));
    }
}

[Collection(GpuCollection.Name)]
public sealed class SurfaceYardTests(SurfaceYardRun run) : IClassFixture<SurfaceYardRun>
{
    private YardMeasurements Measured => run.Measured;

    [Fact]
    public void TheTwoCratesLeftOnTheStackFallAsleepAndMoveLessThanAMillimeterInSixtySeconds()
    {
        foreach (StackedCrate crate in Measured.Stacked)
        {
            crate.AsleepWhenTheWalkStarts.Should().BeTrue($"the {crate.Id} has rested on the stack since the start, 2 s before the walk");
            crate.AsleepAtTheEnd.Should().BeTrue($"the {crate.Id} rests on the stack at the end");
            crate.LargestMove.Should().BeLessThan(YardMeasurements.StackDriftLimit,
                $"the {crate.Id} stays where it rests, from {Measured.SettledTime:F2} s to {Measured.EndTime:F2} s, a span of 60 s, through the push");
            crate.OffsetFromWhereStacked.Should().BeLessThan(YardMeasurements.StackSinkLimit,
                $"the {crate.Id} is where the scene stacked it, apart from sinking into its contacts by Jolt's slop of 5 mm");
        }
    }

    [Fact]
    public void ThePersonStandsWhileItWalks()
    {
        PersonWalk walk = Measured.Walk;
        walk.TicksChecked.Should().BeGreaterThan(200, "both walks together last more than 200 ticks");
        walk.TicksNotStanding.Should().BeEmpty("physics stands the person on the floor at every tick of both walks");
        walk.LargestHeightError.Should().BeLessThan(YardMeasurements.StandingHeightTolerance,
            "a standing person's center stays 0.9 m above the floor, half its capsule's height");
        walk.LargestVerticalSpeed.Should().BeLessThan(YardMeasurements.StandingVerticalSpeedLimit, "a standing person neither sinks nor bounces");
        walk.FirstWalkDistance.Should().BeApproximately(YardMeasurements.WalkDistance, YardMeasurements.WalkDistanceTolerance,
            "W held for 200 ticks walks the person 200 ticks times 3 m/s, 10 m, toward the stack");
    }

    /// <remarks>
    /// A crate pushed off an edge tips over it while the edge still holds part of its weight, and it lands on a corner, so
    /// the time from the edge to the floor is not the time of a free fall from rest: a simple simulation puts it 0 to 3
    /// ticks above sqrt(2h/g) for a box that slides off at 2 to 3 m/s. So the test checks the free fall itself: in every
    /// step in which the crate touches nothing, it gains μ/r² toward Earth's center times the step. A fall of h from rest
    /// at the acceleration measured so then takes sqrt(2h/g) within one tick.
    /// </remarks>
    [Fact]
    public void ThePushedCrateFallsOneMeterInTheTimeOfAFreeFallWithTheSurfaceGravity()
    {
        CrateFall fall = Measured.Fall;
        fall.FreeFlightTicks.Count.Should().BeGreaterThanOrEqualTo(YardMeasurements.LeastFreeFlightTicks,
            "the person pushes the top crate off the stack, and it falls clear of the stack and the floor for most of 27 ticks");
        foreach (FreeFlightTick tick in fall.FreeFlightTicks)
        {
            tick.AccelerationError.Should().BeLessThan(YardMeasurements.FreeFallRelativeTolerance * tick.ExpectedGravity,
                $"in the step of tick {tick.Tick}, the crate touches nothing, so its velocity changes by μ/r² toward Earth's center times the step");
            tick.AngularVelocityChange.Should().BeLessThan(YardMeasurements.SpinChangeLimit,
                $"in the step of tick {tick.Tick}, nothing turns the crate, a cube, so its spin stays");
        }
        fall.FallTimeAtMeasuredGravity.Should().BeApproximately(fall.ExpectedFallTime, Measured.TickSeconds,
            "a fall of h = 1 m from rest takes sqrt(2h/g), with g = μ/R² the surface gravity in earth-surface");
    }

    [Fact]
    public void NoBodyJittersOnceTheYardIsAtRest()
    {
        foreach (RestingBody body in Measured.AtRest)
        {
            body.LargestStep.Should().BeLessThan(YardMeasurements.JitterStepLimit,
                $"the {body.Id} rests from {Measured.RestFromTime:F2} s to the end, so no tick moves it");
            body.LargestMove.Should().BeLessThan(YardMeasurements.RestDriftLimit, $"the {body.Id} rests from {Measured.RestFromTime:F2} s to the end");
            if (body.Id != "person")
                body.AsleepAtTheEnd.Should().BeTrue($"the {body.Id} rests on the floor or the stack at the end");
        }
    }

    [Fact]
    public void OnlyRestingNumericFramesHoldTheYardsBodies()
    {
        foreach (BodyHomes homes in Measured.Homes)
            homes.TicksNotResting.Should().Be(0,
                $"the {homes.Id} is slow and 6,371 km from the origin of earth-surface, so a resting numeric frame holds it after every tick from the first (the first ticks without one: {string.Join(", ", homes.FirstTicksNotResting)})");
    }

    [Fact]
    public void TheRunRecordsNoErrors()
    {
        run.World.Errors.Should().BeEmpty();
        run.ValidationMessages.Should().BeEmpty();
    }

    [Fact]
    public void WritesItsReportWhereTertiusSceneReportNamesAFile()
    {
        if (Environment.GetEnvironmentVariable(SurfaceYardReport.Variable) is not { Length: > 0 } path)
            return;
        SurfaceYardReport.Write(run, path);
        File.Exists(path).Should().BeTrue();
    }
}
