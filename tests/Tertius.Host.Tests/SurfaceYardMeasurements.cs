using System.Numerics;
using Tertius.Demo;
using Tertius.Host.Scripting;

namespace Tertius.Host.Tests;

/// <summary>A crate that stays on the yard's stack, as the run measured it.</summary>
/// <param name="Id">The crate's scene id.</param>
/// <param name="AsleepWhenTheWalkStarts">True if Jolt holds the crate asleep, with zero velocity, when the walk starts, 2 s into the run.</param>
/// <param name="AsleepAtTheEnd">True if Jolt holds the crate asleep at the end.</param>
/// <param name="LargestMove">The largest distance of the crate from where it rested when the walk started, over the 60 s after that, in meters.</param>
/// <param name="OffsetFromWhereStacked">The distance of the crate at the end from where the scene stacked it, in meters.</param>
/// <param name="SleepChanges">The first twelve ticks at which the crate fell asleep or woke, with true for asleep. No test asserts them.</param>
/// <param name="WakesAfterThePush">How many times the crate woke after the dump "pushed", while the person stands against the stack.</param>
internal sealed record StackedCrate(string Id, bool AsleepWhenTheWalkStarts, bool AsleepAtTheEnd, double LargestMove, double OffsetFromWhereStacked,
    IReadOnlyList<(long Tick, bool Asleep)> SleepChanges, int WakesAfterThePush);

/// <summary>The person during its two walks: toward the stack before the jump, and into the top crate after it.</summary>
/// <param name="TicksChecked">How many ticks the walks span, including the ticks in which the person stands still between them and the jump.</param>
/// <param name="TicksNotStanding">The ticks of the walks in which physics did not stand the person on the floor, at most the first ten.</param>
/// <param name="LargestHeightError">The largest distance of the person's center from 0.9 m above the floor, in meters.</param>
/// <param name="LargestVerticalSpeed">The largest vertical speed of the person, from the change of its height over each tick, in m/s.</param>
/// <param name="FirstWalkDistance">How far the first walk took the person, in meters.</param>
/// <param name="LargestReportedVerticalSpeed">The largest vertical part of the velocity that the world reports for the person, in m/s.</param>
internal sealed record PersonWalk(int TicksChecked, IReadOnlyList<long> TicksNotStanding, double LargestHeightError, double LargestVerticalSpeed,
    double FirstWalkDistance, double LargestReportedVerticalSpeed);

/// <summary>One step in which the pushed crate touched nothing.</summary>
/// <param name="Tick">The tick whose step it is.</param>
/// <param name="Acceleration">The crate's change of velocity over the step, divided by the step, in m/s², in earth-surface's axes.</param>
/// <param name="ExpectedAcceleration">Earth's field at the crate's center at the start of the step: μ/r² toward Earth's center.</param>
/// <param name="AccelerationError">The length of the difference of the two, in m/s².</param>
/// <param name="AngularVelocityChange">How much the crate's angular velocity changed over the step, in rad/s.</param>
internal sealed record FreeFlightTick(long Tick, Double3 Acceleration, Double3 ExpectedAcceleration, double AccelerationError, double AngularVelocityChange)
{
    /// <summary>The magnitude of the expected acceleration, μ/r², in m/s².</summary>
    public double ExpectedGravity => ExpectedAcceleration.Length;
}

/// <summary>The top crate's fall from the stack to the floor.</summary>
/// <param name="FreeFlightTicks">The steps of the fall in which the crate was clear of the stack and of the floor by <see cref="YardMeasurements.Clearance"/> at both ends.</param>
/// <param name="MeanAcceleration">The mean magnitude of the crate's acceleration over those steps, in m/s². No test asserts it.</param>
/// <param name="PushTick">The first tick after the person landed from its jump at which the crate had moved more than 1 mm.</param>
/// <param name="TipTick">The first tick at which the crate's center was past the far edge of the stack, where it starts to tip.</param>
/// <param name="LeaveTick">
/// The first tick after the push at which every corner of the crate was past the far face of the stack's lower crates,
/// at z = -0.25 m: nothing of the stack is under the crate any more, and it falls freely.
/// </param>
/// <param name="Leaving">The crate after <paramref name="LeaveTick"/>.</param>
/// <param name="FloorTick">The first tick after <paramref name="LeaveTick"/> at which the crate's lowest corner was within <see cref="YardMeasurements.FloorReach"/> of the floor's top or below it.</param>
/// <param name="MeasuredFallTime">
/// The time from <paramref name="LeaveTick"/> to the moment at which the lowest corner reached the floor, in seconds: the
/// height of the lowest corner is interpolated between the tick before <paramref name="FloorTick"/> and that tick.
/// </param>
/// <param name="PredictedFallTime">
/// The time in which a free fall from the state of <paramref name="Leaving"/>, under the surface gravity μ/R² and with
/// its spin kept, brings the crate's lowest corner down to the floor, in seconds. For a crate that left at rest and
/// without spin, it is sqrt(2h/g), with h the height of its lowest corner above the floor.
/// </param>
/// <param name="PredictedCenterDrop">How far the crate's center falls in <paramref name="PredictedFallTime"/>, in meters.</param>
/// <param name="LandingTick">The first tick after the free flight whose step changed the crate's velocity by more than half of free fall: the floor stopped it.</param>
/// <param name="DeepestBelowFloor">The deepest that a corner of the crate went below the floor's top in the 60 ticks from <paramref name="FloorTick"/>, in meters. No test asserts it.</param>
internal sealed record CrateFall(IReadOnlyList<FreeFlightTick> FreeFlightTicks, double MeanAcceleration, long? PushTick, long? TipTick,
    long? LeaveTick, BodySample? Leaving, long? FloorTick, double? MeasuredFallTime, double? PredictedFallTime, double? PredictedCenterDrop,
    long? LandingTick, double? DeepestBelowFloor);

/// <summary>One Collision that the dump "pushed" recorded between the person and the top crate.</summary>
/// <param name="Tick">The tick of the sync point that routed it.</param>
/// <param name="To">The scene id of the entity that heard it.</param>
/// <param name="Impulse">The impulse that physics estimated for the contact, in N·s.</param>
internal sealed record PushCollision(long Tick, string To, float Impulse);

/// <summary>A body over the last 40 s of the run, when the whole yard should rest.</summary>
/// <param name="Id">The body's scene id.</param>
/// <param name="LargestStep">The largest distance that one tick moved the body, in meters.</param>
/// <param name="LargestMove">The largest distance of the body from where it was at the start of the span, in meters.</param>
/// <param name="AsleepAtTheEnd">True if the body's velocity and angular velocity are exactly zero at the end: Jolt holds it asleep.</param>
internal sealed record RestingBody(string Id, double LargestStep, double LargestMove, bool AsleepAtTheEnd);

/// <summary>A change of a body's physics home.</summary>
/// <param name="Tick">The tick after which the body had the new home.</param>
/// <param name="Time">The world's time then, in seconds.</param>
/// <param name="Home">The new home.</param>
/// <param name="Mode">How the new home moves as a numeric frame, or None for earth-surface itself.</param>
internal sealed record HomeChange(long Tick, double Time, FrameHandle Home, NumericMode Mode);

/// <summary>The physics homes of one body over the run.</summary>
/// <param name="Id">The body's scene id.</param>
/// <param name="TicksNotResting">How many ticks from the first on ended with a frame other than a resting numeric frame holding the body.</param>
/// <param name="FirstTicksNotResting">The first ten of those ticks.</param>
/// <param name="Changes">Each change of the body's home, from tick 0 on.</param>
/// <param name="LargestDistanceFromHomeOrigin">The largest distance of the body from its home's origin, from tick 1 on, in meters: its largest position in its Jolt system.</param>
internal sealed record BodyHomes(string Id, int TicksNotResting, IReadOnlyList<long> FirstTicksNotResting, IReadOnlyList<HomeChange> Changes,
    double LargestDistanceFromHomeOrigin);

/// <summary>The person's jump, which the report shows and no test asserts.</summary>
/// <param name="TakeOffTick">The first tick after the first walk at which physics did not stand the person.</param>
/// <param name="LandingTick">The first tick after that at which it stood the person again.</param>
/// <param name="Apex">The largest height of the person's center above where it stood before the jump, in meters.</param>
internal sealed record JumpFacts(long? TakeOffTick, long? LandingTick, double? Apex);

/// <summary>
/// What the tests of <see cref="SurfaceYardTests"/> assert and <see cref="SurfaceYardReport"/> shows, measured once from
/// the samples of a <see cref="SurfaceYardRun"/>. Every expected value comes from the scene's geometry and from Earth's
/// gravity, never from the run.
/// </summary>
internal sealed class YardMeasurements
{
    /// <summary>The largest distance that a crate on the stack may move over the 60 s after it comes to rest: 1 mm.</summary>
    public const double StackDriftLimit = 1e-3;

    /// <summary>How far a stacked crate may lie from where the scene stacked it: Jolt lets each contact sink by up to 5 mm.</summary>
    public const double StackSinkLimit = 0.02;

    /// <summary>The height of a standing person's center above the floor: half the capsule's 1.8 m, in meters.</summary>
    public const double StandingHeight = 0.9;

    /// <summary>How far a standing person's center may be from <see cref="StandingHeight"/>: Jolt's character keeps a gap of a few cm.</summary>
    public const double StandingHeightTolerance = 0.05;

    /// <summary>The largest vertical speed of a standing person, in m/s.</summary>
    public const double StandingVerticalSpeedLimit = 0.05;

    /// <summary>How far the first walk takes the person: 200 ticks at 3 m/s, in meters.</summary>
    public const double WalkDistance = 200 * PersonControl.WalkSpeed / 60.0;

    /// <summary>Half of one tick of walking, in meters.</summary>
    public const double WalkDistanceTolerance = 0.5 * PersonControl.WalkSpeed / 60.0;

    /// <summary>The fewest steps of free flight that the fall must have. It lasts about 25 ticks, from the stack's edge to the floor.</summary>
    public const int LeastFreeFlightTicks = 10;

    /// <summary>How close to the floor's top the crate's lowest corner must come to have reached the floor, in meters: 1 mm.</summary>
    public const double FloorReach = 1e-3;

    /// <summary>The step of the search for the time of the predicted fall, in seconds.</summary>
    private const double PredictionStep = 1e-5;

    /// <summary>The largest error of the crate's acceleration in free flight, as a share of μ/r².</summary>
    public const double FreeFallRelativeTolerance = 1e-3;

    /// <summary>The largest change of the crate's spin in one step of free flight, in rad/s.</summary>
    public const double SpinChangeLimit = 1e-4;

    /// <summary>The largest distance that one tick may move a resting body: 0.1 mm.</summary>
    public const double JitterStepLimit = 1e-4;

    /// <summary>The largest distance that a resting body may move over the last 40 s: 1 mm.</summary>
    public const double RestDriftLimit = 1e-3;

    /// <summary>How far the pushed crate must be from the stack and the floor for a step to count as free flight, in meters.</summary>
    public const double Clearance = 0.05;

    /// <summary>How far the pushed crate falls: from the top of the stack, two crates high, to the floor, in meters.</summary>
    public const double DropHeight = 4 * DemoScenes.YardCrateHalfSize;

    /// <summary>The span at the end of the run in which every body of the yard should rest, in ticks: 40 s.</summary>
    public const int RestTicks = 40 * 60;

    /// <summary>
    /// How far a body may be from the origin of its home's Jolt system: the distance beyond which a numeric frame moves
    /// its origin to its members' center of mass, 1 km. The yard is 6,371 km from the origin of earth-surface.
    /// </summary>
    public static double NearJoltOrigin => new NumericFrameOptions().RecenterDistance;

    /// <summary>The height of the floor's top above Earth's center, in meters.</summary>
    public const double FloorTop = DemoScenes.EarthRadius;

    /// <summary>The surface gravity at the floor, μ/R², in m/s².</summary>
    public static double SurfaceGravity => DemoScenes.EarthMu / (FloorTop * FloorTop);

    private const double Half = DemoScenes.YardCrateHalfSize;

    // The coordinates of a crate's corners along each of its axes, from its center.
    private static readonly double[] Corners = [-Half, Half];

    public YardMeasurements(SurfaceYardRun run, YardRecord record)
    {
        Ticks = record.Ticks;
        for (int i = 0; i < Ticks.Count; i++)
            if (Ticks[i].Tick != i)
                throw new InvalidOperationException($"The sample at index {i} is of tick {Ticks[i].Tick}: the run sampled a tick twice or skipped one.");
        TickSeconds = run.World.DeltaTime;
        StateDump pushed = run.Result.Dump("pushed");
        SettledTick = run.Result.Dump("settled").Tick;
        WalkedTick = run.Result.Dump("walked").Tick;
        LandedTick = run.Result.Dump("landed").Tick;
        PushedTick = pushed.Tick;
        EndTick = run.Result.Dump("end").Tick;
        RestFromTick = EndTick - RestTicks;
        Stacked = [StackedCrateAt(YardRecord.BottomCrate, 0), StackedCrateAt(YardRecord.MiddleCrate, 1)];
        Walk = MeasureWalk();
        Fall = MeasureFall();
        AtRest = [.. new[] { YardRecord.BottomCrate, YardRecord.MiddleCrate, YardRecord.TopCrate, YardRecord.Person }.Select(MeasureRest)];
        Homes = [.. Enumerable.Range(0, YardRecord.Bodies.Length).Select(MeasureHomes)];
        Jump = MeasureJump();
        PushCollisions = MeasurePushCollisions(pushed, run.Scene);
    }

    public IReadOnlyList<TickSample> Ticks { get; }

    /// <summary>The length of one tick, in seconds.</summary>
    public double TickSeconds { get; }

    /// <summary>The tick of the dump "settled", when the walk starts: 2 s into the run.</summary>
    public long SettledTick { get; }

    /// <summary>The tick of the dump "walked", 30 ticks after the first walk, just before the jump.</summary>
    public long WalkedTick { get; }

    /// <summary>The tick of the dump "landed", 90 ticks after the jump, when the walk into the top crate starts.</summary>
    public long LandedTick { get; }

    /// <summary>The tick of the dump "pushed", 22 ticks after the walk into the top crate ends.</summary>
    public long PushedTick { get; }

    /// <summary>The last tick, 60 s after <see cref="SettledTick"/>.</summary>
    public long EndTick { get; }

    /// <summary>The first tick of the last 40 s, in which every body should rest.</summary>
    public long RestFromTick { get; }

    public double SettledTime => Ticks[(int)SettledTick].Time;

    public double EndTime => Ticks[(int)EndTick].Time;

    public double RestFromTime => Ticks[(int)RestFromTick].Time;

    public IReadOnlyList<StackedCrate> Stacked { get; }

    public PersonWalk Walk { get; }

    public CrateFall Fall { get; }

    public IReadOnlyList<RestingBody> AtRest { get; }

    public IReadOnlyList<BodyHomes> Homes { get; }

    public JumpFacts Jump { get; }

    /// <summary>Every Collision between the person and the top crate that the dump "pushed" recorded, from the dump "landed" on.</summary>
    public IReadOnlyList<PushCollision> PushCollisions { get; }

    /// <summary>
    /// The largest impulse that physics may estimate when the person walks into the resting top crate: the walk speed
    /// times the reduced mass of the two, which stops a head-on approach.
    /// </summary>
    public static double HeadOnPushImpulse => PersonControl.WalkSpeed / (1.0 / DemoScenes.YardPersonMass + 1.0 / DemoScenes.LightCrateMass);

    /// <summary>Where the scene stacks the crate at <paramref name="level"/>, 0 at the bottom, on the Y axis.</summary>
    public static Double3 StackedPosition(int level) => new(0, FloorTop + Half * (2 * level + 1), 0);

    /// <summary>Earth's field at <paramref name="position"/> in earth-surface: μ/r² toward Earth's center, in m/s².</summary>
    public static Double3 Field(Double3 position) => position * (-DemoScenes.EarthMu / Math.Pow(position.Length, 3));

    /// <summary>True if Jolt holds the body asleep: a sleeping body's velocities are exactly zero.</summary>
    public static bool Asleep(BodySample body) => body.Velocity == Double3.Zero && body.AngularVelocity == Vector3.Zero;

    private BodySample At(long tick, int body) => Ticks[(int)tick].Bodies[body];

    private StackedCrate StackedCrateAt(int body, int level)
    {
        Double3 rested = At(SettledTick, body).Pose.Position;
        double largest = 0;
        for (long tick = SettledTick; tick <= EndTick; tick++)
            largest = Math.Max(largest, (At(tick, body).Pose.Position - rested).Length);
        var sleep = new List<(long Tick, bool Asleep)>();
        for (long tick = 1; tick <= EndTick && sleep.Count < 12; tick++)
            if (Asleep(At(tick, body)) != Asleep(At(tick - 1, body)))
                sleep.Add((tick, Asleep(At(tick, body))));
        int wakes = 0;
        for (long tick = PushedTick + 1; tick <= EndTick; tick++)
            if (Asleep(At(tick - 1, body)) && !Asleep(At(tick, body)))
                wakes++;
        return new StackedCrate(YardRecord.Bodies[body], Asleep(At(SettledTick, body)), Asleep(At(EndTick, body)), largest,
            (At(EndTick, body).Pose.Position - StackedPosition(level)).Length, sleep, wakes);
    }

    private PersonWalk MeasureWalk()
    {
        var notStanding = new List<long>();
        double heightError = 0, verticalSpeed = 0, reportedVerticalSpeed = 0;
        int count = 0;
        foreach ((long from, long to) in new[] { (SettledTick, WalkedTick), (LandedTick, PushedTick) })
            for (long tick = from; tick <= to; tick++)
            {
                count++;
                if (!Ticks[(int)tick].PersonStanding)
                    notStanding.Add(tick);
                BodySample person = At(tick, YardRecord.Person);
                heightError = Math.Max(heightError, Math.Abs(person.Pose.Position.Y - FloorTop - StandingHeight));
                verticalSpeed = Math.Max(verticalSpeed, Math.Abs(person.Pose.Position.Y - At(tick - 1, YardRecord.Person).Pose.Position.Y) / TickSeconds);
                reportedVerticalSpeed = Math.Max(reportedVerticalSpeed, Math.Abs(person.Velocity.Y));
            }
        double distance = (At(WalkedTick, YardRecord.Person).Pose.Position - At(SettledTick, YardRecord.Person).Pose.Position).Length;
        return new PersonWalk(count, [.. notStanding.Take(10)], heightError, verticalSpeed, distance, reportedVerticalSpeed);
    }

    /// <summary>
    /// The largest Z of the corners of a crate at <paramref name="pose"/>, the corner nearest the stack once the crate
    /// moves off it toward -Z, and the smallest Y, its lowest corner.
    /// </summary>
    public static (double HighestZ, double LowestY) CornerExtremes(DPose pose)
    {
        double highestZ = double.MinValue, lowestY = double.MaxValue;
        foreach (double x in Corners)
            foreach (double y in Corners)
                foreach (double z in Corners)
                {
                    Double3 corner = pose.Position + pose.Rotation.Rotate(new Double3(x, y, z));
                    highestZ = Math.Max(highestZ, corner.Z);
                    lowestY = Math.Min(lowestY, corner.Y);
                }
        return (highestZ, lowestY);
    }

    /// <summary>
    /// True if every corner of the top crate is farther than <see cref="Clearance"/> past the far face of the stack's
    /// lower crates, at z = -0.25 m, and higher than <see cref="Clearance"/> above the floor. Jolt makes contacts only
    /// between shapes closer than 2 cm at the start of a step, so a step that starts and ends so placed touches nothing.
    /// </summary>
    public static bool ClearOfStackAndFloor(BodySample crate)
    {
        (double highestZ, double lowestY) = CornerExtremes(crate.Pose);
        return highestZ < -Half - Clearance && lowestY > FloorTop + Clearance;
    }

    /// <summary>
    /// The time in which a free fall from the state of <paramref name="crate"/> brings the crate's lowest corner within
    /// <see cref="FloorReach"/> of the floor's top, in seconds, and how far its center falls in that time. The fall is
    /// the exact one of a body that nothing touches, in the surface gravity μ/R² along -Y, which is the field at the yard
    /// to 1 part in 10^6 over the 1.25 m of the fall. The crate is a cube, so its spin stays as it is. Null if the corner
    /// does not come down within 2 s.
    /// </summary>
    public static (double Time, double CenterDrop)? PredictFall(BodySample crate)
    {
        var spin = (Double3)crate.AngularVelocity;
        double rate = spin.Length;
        Double3 axis = rate > 0 ? spin / rate : Double3.UnitY;
        double level = FloorTop + FloorReach;
        for (double time = 0; time < 2; time += PredictionStep)
        {
            double drop = -crate.Velocity.Y * time + 0.5 * SurfaceGravity * time * time;
            var pose = new DPose(new Double3(crate.Pose.Position.X, crate.Pose.Position.Y - drop, crate.Pose.Position.Z),
                DQuaternion.FromAxisAngle(axis, rate * time) * crate.Pose.Rotation);
            if (CornerExtremes(pose).LowestY <= level)
                return (time, drop);
        }
        return null;
    }

    private FreeFlightTick StepOf(long tick)
    {
        BodySample before = At(tick - 1, YardRecord.TopCrate), after = At(tick, YardRecord.TopCrate);
        Double3 acceleration = (after.Velocity - before.Velocity) / TickSeconds;
        Double3 expected = Field(before.Pose.Position);
        return new FreeFlightTick(tick, acceleration, expected, (acceleration - expected).Length, (after.AngularVelocity - before.AngularVelocity).Length());
    }

    private CrateFall MeasureFall()
    {
        Double3 resting = At(LandedTick, YardRecord.TopCrate).Pose.Position;
        long? push = null, tip = null, leave = null, floor = null, landing = null;
        var flight = new List<FreeFlightTick>();
        for (long tick = LandedTick + 1; tick <= EndTick; tick++)
        {
            BodySample crate = At(tick, YardRecord.TopCrate);
            (double highestZ, double lowestY) = CornerExtremes(crate.Pose);
            if (push is null && (crate.Pose.Position - resting).Length > 1e-3)
                push = tick;
            if (tip is null && crate.Pose.Position.Z < -Half)
                tip = tick;
            if (push is not null && leave is null && highestZ < -Half)
                leave = tick;
            if (leave is not null && floor is null && lowestY <= FloorTop + FloorReach)
                floor = tick;
            bool free = ClearOfStackAndFloor(At(tick - 1, YardRecord.TopCrate)) && ClearOfStackAndFloor(crate);
            if (free && landing is null)
            {
                flight.Add(StepOf(tick));
                continue;
            }
            // The first step after the free flight in which the crate's velocity changed by more than half of free fall.
            if (flight.Count > 0 && landing is null && StepOf(tick).AccelerationError > 0.5 * SurfaceGravity)
                landing = tick;
            if (landing is not null && floor is not null)
                break;
        }
        double mean = flight.Count > 0 ? flight.Average(step => step.Acceleration.Length) : 0;
        BodySample? leaving = leave is long left ? At(left, YardRecord.TopCrate) : null;
        (double Time, double CenterDrop)? predicted = leaving is { } state ? PredictFall(state) : null;
        double? measured = null, deepest = null;
        if (leave is long from && floor is long reached)
        {
            // The lowest corner moves at a nearly steady 4 m/s in the last tick, so a linear interpolation finds the moment.
            double above = CornerExtremes(At(reached - 1, YardRecord.TopCrate).Pose).LowestY - FloorTop - FloorReach;
            double below = CornerExtremes(At(reached, YardRecord.TopCrate).Pose).LowestY - FloorTop - FloorReach;
            double share = above > 0 ? above / (above - below) : 0;
            measured = (reached - 1 - from + share) * TickSeconds;
            deepest = Enumerable.Range(0, 60).Select(i => reached + i).Where(tick => tick <= EndTick)
                .Max(tick => FloorTop - CornerExtremes(At(tick, YardRecord.TopCrate).Pose).LowestY);
        }
        return new CrateFall(flight, mean, push, tip, leave, leaving, floor, measured, predicted?.Time, predicted?.CenterDrop, landing, deepest);
    }

    private RestingBody MeasureRest(int body)
    {
        Double3 start = At(RestFromTick, body).Pose.Position;
        double step = 0, move = 0;
        for (long tick = RestFromTick + 1; tick <= EndTick; tick++)
        {
            step = Math.Max(step, (At(tick, body).Pose.Position - At(tick - 1, body).Pose.Position).Length);
            move = Math.Max(move, (At(tick, body).Pose.Position - start).Length);
        }
        return new RestingBody(YardRecord.Bodies[body], step, move, Asleep(At(EndTick, body)));
    }

    private BodyHomes MeasureHomes(int body)
    {
        var notResting = new List<long>();
        var changes = new List<HomeChange>();
        double farthest = 0;
        for (int tick = 0; tick < Ticks.Count; tick++)
        {
            BodySample sample = Ticks[tick].Bodies[body];
            if (tick >= 1)
                farthest = Math.Max(farthest, sample.PositionInHome.Length);
            if (tick >= 1 && sample.HomeMode != NumericMode.Resting)
                notResting.Add(tick);
            if (tick == 0 || sample.Home != Ticks[tick - 1].Bodies[body].Home || sample.HomeMode != Ticks[tick - 1].Bodies[body].HomeMode)
                changes.Add(new HomeChange(tick, Ticks[tick].Time, sample.Home, sample.HomeMode));
        }
        return new BodyHomes(YardRecord.Bodies[body], notResting.Count, [.. notResting.Take(10)], changes, farthest);
    }

    private JumpFacts MeasureJump()
    {
        long? takeOff = null, landing = null;
        double standing = At(WalkedTick, YardRecord.Person).Pose.Position.Y, apex = double.MinValue;
        for (long tick = WalkedTick + 1; tick <= LandedTick; tick++)
        {
            bool stands = Ticks[(int)tick].PersonStanding;
            if (takeOff is null && !stands)
                takeOff = tick;
            else if (takeOff is not null && landing is null && stands)
                landing = tick;
            apex = Math.Max(apex, At(tick, YardRecord.Person).Pose.Position.Y - standing);
        }
        return new JumpFacts(takeOff, landing, takeOff is null ? null : apex);
    }

    private static List<PushCollision> MeasurePushCollisions(StateDump pushed, SceneInstance scene)
    {
        Entity person = scene["person"], crate = scene["top-crate"];
        var collisions = new List<PushCollision>();
        foreach (EventRecord record in pushed.Events)
        {
            if (record.Payload is not Collision collision)
                continue;
            if (record.To == new ToEntity("person") && collision.Other == crate)
                collisions.Add(new PushCollision(record.Tick, "person", collision.Impulse));
            else if (record.To == new ToEntity("top-crate") && collision.Other == person)
                collisions.Add(new PushCollision(record.Tick, "top-crate", collision.Impulse));
        }
        return collisions;
    }
}
