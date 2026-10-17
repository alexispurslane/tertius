using System.Numerics;
using Tertius.Demo;
using Tertius.Simulation;

namespace Tertius.Host.Tests;

/// <summary>
/// One headless run of the checked-in script scripts/surface-yard.json, on the scene surface-yard: a yard on Earth's
/// surface, 6,371 km from the origin of its frame earth-surface, whose bodies rest in the Jolt system of earth-surface
/// itself, without a numeric frame. After 2 s, when the crates of the stack rest, the person walks 10 m toward the stack and stops. It jumps
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
            crate.WakesAfterThePush.Should().Be(0,
                $"nothing pushes the {crate.Id} while the person stands against the stack after the push (it fell asleep or woke at: {string.Join(", ", crate.SleepChanges)})");
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
        walk.LargestReportedVerticalSpeed.Should().BeLessThan(YardMeasurements.StandingVerticalSpeedLimit,
            "the floor holds a standing person against gravity, so the world reports no vertical velocity for it");
        walk.FirstWalkDistance.Should().BeApproximately(YardMeasurements.WalkDistance, YardMeasurements.WalkDistanceTolerance,
            "W held for 200 ticks walks the person 200 ticks times 3 m/s, 10 m, toward the stack");
    }

    /// <remarks>
    /// A crate pushed off an edge tips over it while the edge still holds part of its weight. It leaves the edge spinning
    /// and lands on a corner, so the time from the edge to the floor is not the time of a free fall of 1 m from rest, and
    /// no test asserts that time. The test checks the free fall itself. In every step in which the crate touches nothing,
    /// it gains μ/r² toward Earth's center times the step, and its spin stays. And from the tick at which the crate has
    /// left the stack, its lowest corner reaches the floor within one tick of the time that a free fall from that state
    /// takes, with g = μ/R² the surface gravity. For a crate that leaves at rest without spin, that time is sqrt(2h/g).
    /// </remarks>
    [Fact]
    public void ThePushedCrateFallsFromTheStackToTheFloorInTheTimeOfAFreeFallWithTheSurfaceGravity()
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
        fall.LeaveTick.Should().NotBeNull("the crate leaves the stack");
        fall.FloorTick.Should().NotBeNull("the crate reaches the floor");
        fall.PredictedFallTime.Should().NotBeNull("a free fall from where the crate left the stack reaches the floor");
        fall.MeasuredFallTime!.Value.Should().BeApproximately(fall.PredictedFallTime!.Value, Measured.TickSeconds,
            $"from tick {fall.LeaveTick}, when it has left the stack, the crate falls freely with g = μ/R² until its lowest corner reaches the floor");
    }

    [Fact]
    public void ThePersonAndTheTopCrateHearEachOtherWhenThePersonPushesIt()
    {
        CrateFall fall = Measured.Fall;
        fall.PushTick.Should().NotBeNull("the person pushes the top crate");
        foreach (string hearer in new[] { "person", "top-crate" })
        {
            PushCollision? first = Measured.PushCollisions.FirstOrDefault(collision => collision.To == hearer);
            first.Should().NotBeNull($"the {hearer} hears the push");
            first!.Tick.Should().BeInRange(fall.PushTick!.Value - 1, fall.PushTick.Value + 1, "the contact starts in the step in which the crate starts to move");
            first.Impulse.Should().BeGreaterThan(0, "the person walks into the resting crate");
            first.Impulse.Should().BeLessThanOrEqualTo((float)(YardMeasurements.HeadOnPushImpulse * 1.001),
                "the approach along the contact's normal is at most the walk speed");
        }
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

    /// <summary>
    /// The yard lies 6,371 km from the origin of earth-surface, nearer than the 8,389 km from which a slow group gets a
    /// resting numeric frame (<see cref="NumericFrameOptions.CreateDistance"/>), and no body of it is fast. So the Jolt system
    /// of earth-surface itself holds every body, 6,371 km from its origin, in double precision, and the bodies still rest
    /// without jitter. The precision measurements (tests/Tertius.Physics.Tests/Precision) found no error from the distance
    /// up to 9,000 km.
    /// </summary>
    [Fact]
    public void NoNumericFrameHoldsTheYardsBodiesAndTheyRestInTheJoltSystemOfEarthSurfaceItself()
    {
        Measured.MostNumericFrames.Should().Be(0, "no group of the yard is fast or 8,389 km from the origin of earth-surface");
        foreach (BodyHomes homes in Measured.Homes)
            homes.TicksNotInEarthSurface.Should().Be(0,
                $"the Jolt system of earth-surface holds the {homes.Id} after every tick (the first ticks in another home: {string.Join(", ", homes.FirstTicksNotInEarthSurface)})");
        foreach (RestingBody body in Measured.AtRest)
            body.LargestStep.Should().BeLessThan(YardMeasurements.JitterStepLimit,
                $"the {body.Id} rests 6,371 km from the origin of its Jolt system, from {Measured.RestFromTime:F2} s to the end, so no tick moves it");
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
