using System.Globalization;
using System.Text;
using Tertius.Demo;
using Tertius.Simulation;
using DeepYard = Tertius.Demo.DemoScenes.DeepYard;

namespace Tertius.Host.Tests;

/// <summary>
/// One headless run of the checked-in script scripts/deep-yard.json in the scene deep-yard (see
/// <see cref="DemoScenes.DeepYard"/>), with the state of its bodies after every tick. crate-a closes the 2 m between
/// itself and crate-b at 0.1 m/s and pushes it. The person floats at rest for 10 s, thrusts toward the crates for 1 s
/// and drifts toward them. Every test of the class reads that one run.
/// </summary>
public sealed class DeepYardRun : CheckedInRun
{
    public DeepYardRun() : this([]) { }

    private DeepYardRun(List<YardSample> samples) : base("deep-yard", (world, scene) => samples.Add(YardSample.Take(world, scene)))
    {
        samples.Add(YardSample.Take(World, Scene));
        Samples = samples;
    }

    /// <summary>The state after each tick of the run, from the start to the last tick, in order.</summary>
    internal IReadOnlyList<YardSample> Samples { get; }
}

/// <summary>The state of the deep yard after a number of ticks.</summary>
/// <param name="Tick">The number of completed ticks.</param>
/// <param name="Time">The world's time, in seconds.</param>
/// <param name="Bodies">The yard's bodies, crate-a, crate-b and person, by scene id.</param>
/// <param name="NumericFrames">Every numeric frame below space, in the order of the tree.</param>
/// <param name="Errors"><c>World.Errors</c>.</param>
internal sealed record YardSample(long Tick, double Time, IReadOnlyDictionary<string, BodySample> Bodies,
    IReadOnlyList<NumericFrameSample> NumericFrames, IReadOnlyList<string> Errors)
{
    public static readonly string[] BodyIds = ["crate-a", "crate-b", "person"];

    public static YardSample Take(World world, SceneInstance scene)
    {
        var numericFrames = new List<NumericFrameSample>();
        AddNumericFrames(world.Frames, world.GetOwnedFrame(scene["space"]), numericFrames);
        return new YardSample(world.TickNumber, world.Time, BodyIds.ToDictionary(id => id, id => BodySample.Take(world, scene[id])),
            numericFrames, [.. world.Errors]);
    }

    private static void AddNumericFrames(FrameTree tree, FrameHandle frame, List<NumericFrameSample> numericFrames)
    {
        var children = new List<FrameHandle>();
        tree.GetTreeChildren(frame, children);
        foreach (FrameHandle child in children)
        {
            if (tree.IsNumeric(child))
                numericFrames.Add(new NumericFrameSample(child, tree.Numeric(child), tree.TreeParent(child), tree.TreeLocalPose(child).Position));
            AddNumericFrames(tree, child, numericFrames);
        }
    }
}

/// <summary>A numeric frame: how it moves, its parent in the tree, and its position in that parent, in meters.</summary>
internal sealed record NumericFrameSample(FrameHandle Frame, NumericMode Mode, FrameHandle TreeParent, Double3 Place);

/// <summary>One body of the deep yard after a tick.</summary>
/// <param name="Position">The position in the body's semantic frame, space, in meters. A light-year out, it rounds to 2 m along Z.</param>
/// <param name="Velocity">The velocity in space, in m/s.</param>
/// <param name="Home">The physics home: the frame whose Jolt system holds the body.</param>
/// <param name="HomeMode">How the home moves: <see cref="NumericMode.None"/> when the home is space itself.</param>
/// <param name="Nest">The nearest nesting frame above the home, or null.</param>
/// <param name="NestPlace">The nest's position in its tree parent, in meters, or zero without a nest.</param>
/// <param name="InNest">The body's position in the nest, in meters, or zero without a nest.</param>
internal sealed record BodySample(Double3 Position, Double3 Velocity, FrameHandle Home, NumericMode HomeMode, FrameHandle Nest,
    Double3 NestPlace, Double3 InNest)
{
    /// <summary>
    /// The body's position relative to the yard's center, in space's axes, in meters. Through a nest that lies in space,
    /// it is the nest's place minus the center plus the position in the nest. The nest's place is whole grid steps, and
    /// it lies within a grid step of the center, so the difference of the two is exact, and only small numbers add. A
    /// read in space would round to 2 m along Z. Without a nest, this is that read minus the center.
    /// </summary>
    public Double3 FromCenter => Nest.IsNull ? Position - DeepYard.Center : NestPlace - DeepYard.Center + InNest;

    public static BodySample Take(World world, Entity entity)
    {
        FrameHandle frame = world.GetFrame(entity), home = world.PhysicsHomeOf(entity), nest = home;
        while (!nest.IsNull && world.Frames.Numeric(nest) != NumericMode.Nesting)
            nest = world.Frames.TreeParent(nest);
        return new BodySample(world.PoseIn(entity, frame).Position, world.VelocityIn(entity, frame), home, world.Frames.Numeric(home), nest,
            nest.IsNull ? Double3.Zero : world.Frames.TreeLocalPose(nest).Position,
            nest.IsNull ? Double3.Zero : world.PoseIn(entity, nest).Position);
    }
}

/// <summary>
/// What the deep yard must show, worked out from the scene's numbers and the script alone, and the measures of a run that
/// the tests and the report compare with them.
/// </summary>
internal static class DeepYardExpectations
{
    /// <summary>The engine's ticks per second.</summary>
    public const double TicksPerSecond = 60;

    /// <summary>How many ticks the script holds W: one second.</summary>
    public const int ThrustTicks = 60;

    /// <summary>The grid of a nesting frame, in meters: 2^20 m.</summary>
    public const double Grid = 1 << 20;

    /// <summary>When the crates touch, in seconds from the start: the 2 m between their faces at 0.1 m/s, 20 s.</summary>
    public static double TouchTime => (DeepYard.CrateSpacing - 2 * DeepYard.CrateHalfSize) / DeepYard.CrateSpeed;

    /// <summary>The crates' momentum, in kg·m/s, in space's axes: crate-a's at the start, 5 kg·m/s along +X.</summary>
    public static Double3 Momentum => new(DeepYard.CrateMass * DeepYard.CrateSpeed, 0, 0);

    /// <summary>The person's acceleration while it thrusts, in m/s²: the thrust's force over the person's mass, 1.5.</summary>
    public static double Thrust => (double)PersonControl.ThrustForce / DeepYard.PersonMass;

    /// <summary>The person's velocity after the second of thrust, in space, in m/s: 1.5 m/s along -Z, toward the crates.</summary>
    public static Double3 ThrustVelocity => new(0, 0, -Thrust * ThrustTicks / TicksPerSecond);

    /// <summary>
    /// How far the second of thrust moves the person from rest, in space, in meters: 0.7625 m along -Z. Each tick adds
    /// the acceleration times the tick to the velocity, then moves the person by the velocity times the tick, so the k-th
    /// tick of thrust moves it k·a·dt², and the 60 ticks a·dt²·60·61/2. A continuous force would move it a·t²/2 = 0.75 m.
    /// The 12.5 mm between the two come from the step, a·t·dt/2, at any distance from the origin.
    /// </summary>
    public static Double3 ThrustDisplacement => new(0, 0, -Thrust / (TicksPerSecond * TicksPerSecond) * ThrustTicks * (ThrustTicks + 1) / 2);

    /// <summary>The motion of a continuous force over the second, a·t²/2, in meters: 0.75 m. For the report only.</summary>
    public static double ContinuousThrustDistance => Thrust / 2;

    /// <summary>
    /// The first tick whose step pushed crate-b: the first sample in which crate-b moves at more than 1e-6 m/s. -1 if
    /// crate-b never moves.
    /// </summary>
    public static int FirstPushOfCrateB(IReadOnlyList<YardSample> samples) =>
        IndexOf(samples, sample => sample.Bodies["crate-b"].Velocity.Length > 1e-6);

    /// <summary>
    /// The last tick before the person's thrust: the sample before the first in which the person moves at more than
    /// 1e-6 m/s. -1 if the person never moves.
    /// </summary>
    public static int LastTickAtRest(IReadOnlyList<YardSample> samples)
    {
        int moving = IndexOf(samples, sample => sample.Bodies["person"].Velocity.Length > 1e-6);
        return moving < 1 ? -1 : moving - 1;
    }

    /// <summary>The momentum of the two crates in a sample, in kg·m/s, in space's axes.</summary>
    public static Double3 CratesMomentum(YardSample sample) =>
        (sample.Bodies["crate-a"].Velocity + sample.Bodies["crate-b"].Velocity) * DeepYard.CrateMass;

    private static int IndexOf(IReadOnlyList<YardSample> samples, Func<YardSample, bool> holds)
    {
        for (int i = 0; i < samples.Count; i++)
            if (holds(samples[i]))
                return i;
        return -1;
    }
}

[Collection(GpuCollection.Name)]
public sealed class DeepYardSliceTests(DeepYardRun run) : IClassFixture<DeepYardRun>
{
    private IReadOnlyList<YardSample> Samples => run.Samples;

    [Fact]
    public void TheCratesTouchWhenTheGapBetweenThemClosesAtCrateAsSpeed()
    {
        int push = DeepYardExpectations.FirstPushOfCrateB(Samples);

        push.Should().BePositive("crate-a reaches crate-b and pushes it");
        // The step of tick k runs from (k - 1)/60 s to k/60 s. Within one tick of the touch time, the first push comes in
        // the step that ends at it, in the one after it, or in the one before it.
        ((double)push).Should().BeApproximately(DeepYardExpectations.TouchTime * DeepYardExpectations.TicksPerSecond, 1,
            "the crates touch when 2 m close at 0.1 m/s, 20 s after the start");
    }

    [Fact]
    public void TheContactKeepsTheMomentumOfTheCrates()
    {
        YardSample last = Samples[^1];
        Double3 expected = DeepYardExpectations.Momentum;

        last.Bodies["crate-b"].Velocity.X.Should().BePositive("crate-a pushed crate-b before the end of the run");
        (Double3.Distance(DeepYardExpectations.CratesMomentum(last), expected) / expected.Length).Should().BeLessThan(1e-6);
    }

    [Fact]
    public void TheSecondOfThrustMovesThePersonAsTheThrustsForceOverItsMassPredicts()
    {
        int rest = DeepYardExpectations.LastTickAtRest(Samples);
        rest.Should().BePositive("the person thrusts");
        BodySample before = Samples[rest].Bodies["person"], after = Samples[rest + DeepYardExpectations.ThrustTicks].Bodies["person"];

        Double3.Distance(after.FromCenter - before.FromCenter, DeepYardExpectations.ThrustDisplacement).Should().BeLessThan(1e-3,
            "the thrust's force over the person's mass moves it 0.7625 m in the 60 ticks of the second");
        // Jolt keeps the velocity as a float, which at 1.5 m/s steps by about 1e-7 m/s, and the thrust rounds it 60 times.
        Double3.Distance(after.Velocity, DeepYardExpectations.ThrustVelocity).Should().BeLessThan(1e-5);
    }

    [Fact]
    public void AfterTheThrustThePersonDriftsAtTheVelocityThatTheThrustGaveIt()
    {
        int end = DeepYardExpectations.LastTickAtRest(Samples) + DeepYardExpectations.ThrustTicks;
        double seconds = (Samples.Count - 1 - end) / DeepYardExpectations.TicksPerSecond;

        Double3 drift = Samples[^1].Bodies["person"].FromCenter - Samples[end].Bodies["person"].FromCenter;

        Double3.Distance(drift, DeepYardExpectations.ThrustVelocity * seconds).Should().BeLessThan(1e-3);
    }

    [Fact]
    public void EveryBodyLiesInARestingFrameUnderOneNestingFrameOnTheGridThatNeverJumps()
    {
        Samples.Select(sample => sample.Tick).Should().Equal(Enumerable.Range(0, Samples.Count).Select(tick => (long)tick),
            "the run sampled the state after every tick");
        BodySample first = Samples[1].Bodies["person"];
        FrameHandle nest = first.Nest, space = run.World.GetOwnedFrame(run.Scene["space"]);

        nest.IsNull.Should().BeFalse("the yard's resting frames lie a light-year out, far past 10^9 m, so they nest");
        run.World.Frames.TreeParent(nest).Should().Be(space);
        Math.IEEERemainder(first.NestPlace.X, DeepYardExpectations.Grid).Should().Be(0);
        Math.IEEERemainder(first.NestPlace.Y, DeepYardExpectations.Grid).Should().Be(0);
        Math.IEEERemainder(first.NestPlace.Z, DeepYardExpectations.Grid).Should().Be(0);
        (first.NestPlace - DeepYard.Center).Length.Should().BeLessThan(DeepYardExpectations.Grid, "the nest lies near the yard");
        foreach (YardSample sample in Samples.Skip(1))
        {
            foreach ((string id, BodySample body) in sample.Bodies)
            {
                body.HomeMode.Should().Be(NumericMode.Resting, $"{id} is slow and a light-year out after tick {sample.Tick}");
                body.Nest.Should().Be(nest, $"one nest holds every frame of the yard after tick {sample.Tick}");
                body.NestPlace.Should().Be(first.NestPlace, $"the nest has not jumped after tick {sample.Tick}");
            }
        }
    }

    [Fact]
    public void TheRunReportsNoError()
    {
        run.World.Errors.Should().BeEmpty();
        run.ValidationMessages.Should().BeEmpty();
    }

    /// <summary>
    /// When the environment variable TERTIUS_SCENE_REPORT names a file, writes the report of the run there (see
    /// <see cref="DeepYardReport"/>). Otherwise it does nothing.
    /// </summary>
    [Fact]
    public void WritesTheReportOfTheRunWhereTertiusSceneReportSays()
    {
        string? path = Environment.GetEnvironmentVariable("TERTIUS_SCENE_REPORT");
        if (string.IsNullOrEmpty(path))
            return;
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
        File.WriteAllText(path, DeepYardReport.Write(run));
    }
}

/// <summary>
/// The report of a run of the deep yard, in Markdown: what the scene is and what it must show, the state every 2 s of
/// simulated time, the nesting frames, each expectation with the expected and the measured value, and the captures.
/// </summary>
internal static class DeepYardReport
{
    /// <summary>The scale of the captures: 360 / tan(30 degrees) pixels per meter at 1 m, for a view 720 pixels and 60 degrees high.</summary>
    private static readonly double Focal = 360 / Math.Tan(Math.PI / 6);

    public static string Write(DeepYardRun run)
    {
        IReadOnlyList<YardSample> samples = run.Samples;
        var report = new StringBuilder();
        int push = DeepYardExpectations.FirstPushOfCrateB(samples), rest = DeepYardExpectations.LastTickAtRest(samples);
        int thrustEnd = rest < 0 ? -1 : rest + DeepYardExpectations.ThrustTicks;

        Line(report, $"# Deep yard: nesting numeric frames a light-year out");
        Line(report, $"");
        Line(report, $"## The scene");
        Line(report, $"");
        Line(report, $"The scene `deep-yard` (src/Tertius.Demo/DemoScenes.DeepYard.cs), run by scripts/deep-yard.json, {samples.Count - 1} ticks of 1/60 s.");
        Line(report, $"");
        Line(report, $"- The root frame `space` has no gravity. Its origin is where the light comes from.");
        Line(report, $"- The yard's center lies one light-year ({DeepYard.LightYear:G6} m) from space's origin along -Z. A double steps by 2 m there.");
        Line(report, $"- `crate-a` (orange) and `crate-b` (green) are crates of 1 m and {DeepYard.CrateMass} kg, {DeepYard.CrateSpacing} m apart center to center along X. crate-a moves toward crate-b at {DeepYard.CrateSpeed} m/s. crate-b rests.");
        Line(report, $"- The `person`, a capsule character of {DeepYard.PersonMass} kg, floats at rest {DeepYard.PersonDistance} m from the yard's center along +Z and faces the crates (-Z). It is the camera.");
        Line(report, $"- The script holds W for {DeepYardExpectations.ThrustTicks} ticks (1 s) from tick 600, so the person thrusts toward the crates with {PersonControl.ThrustForce} N.");
        Line(report, $"");
        Line(report, $"## What it must show");
        Line(report, $"");
        Line(report, $"- The crates touch at {DeepYardExpectations.TouchTime} s (the 2 m between their faces at {DeepYard.CrateSpeed} m/s), within one tick.");
        Line(report, $"- The crates' momentum stays {DeepYardExpectations.Momentum.X} kg·m/s along +X through the contact, within 1e-6 relative.");
        Line(report, $"- The second of thrust moves the person {-DeepYardExpectations.ThrustDisplacement.Z} m toward the crates, within 1 mm: {DeepYardExpectations.Thrust} m/s² in 60 steps of 1/60 s. A continuous force would move it {DeepYardExpectations.ContinuousThrustDistance} m; the 12.5 mm between the two come from the step.");
        Line(report, $"- After the thrust the person drifts at {-DeepYardExpectations.ThrustVelocity.Z} m/s, within 1 mm over the rest of the run.");
        Line(report, $"- Every body lies in a resting numeric frame, under one nesting frame on the grid of 2^20 m, which never jumps. World.Errors stays empty.");
        Line(report, $"");
        Line(report, $"Positions in space round to 2 m along Z, a light-year out. The column \"From the yard's center\" reads each body through its nesting frame instead: the nest's place minus the center, which is exact, plus the body's position in the nest.");
        Line(report, $"");

        Line(report, $"## State every 2 s");
        Line(report, $"");
        Line(report, $"| Tick | Time (s) | Body | Position in space (m) | From the yard's center (m) | Velocity in space (m/s) | Physics home | Numeric frames | Errors |");
        Line(report, $"|---|---|---|---|---|---|---|---|---|");
        var ticks = new SortedSet<int>(Enumerable.Range(0, samples.Count).Where(tick => tick % 120 == 0)) { samples.Count - 1 };
        foreach (int tick in new[] { rest, thrustEnd, push - 1, push })
            if (tick >= 0 && tick < samples.Count)
                ticks.Add(tick);
        foreach (int tick in ticks)
        {
            YardSample sample = samples[tick];
            foreach ((string id, BodySample body) in sample.Bodies)
                Line(report, $"| {sample.Tick} | {sample.Time:F3} | {id} | {Vector(body.Position, "F4")} | {Vector(body.FromCenter, "F4")} | {Vector(body.Velocity, "F6")} | {Home(body)} | {Count(sample)} | {sample.Errors.Count} |");
        }
        Line(report, $"");
        Line(report, $"Rows at other ticks than every 2 s: tick {rest}, the last at rest before the thrust; tick {thrustEnd}, the end of the thrust; ticks {push - 1} and {push}, before and at the first push of crate-b.");
        Line(report, $"");

        Line(report, $"## Nesting frames");
        Line(report, $"");
        var nests = samples.SelectMany(sample => sample.NumericFrames.Select(frame => (sample.Tick, Frame: frame)))
            .Where(seen => seen.Frame.Mode == NumericMode.Nesting).GroupBy(seen => seen.Frame.Frame).ToList();
        if (nests.Count == 0)
            Line(report, $"None: no nesting frame ever held the yard's frames.");
        else
        {
            Line(report, $"| Nesting frame | Ticks | Place in space (m) | Place in grid steps of 2^20 m | Tree parent | Frames below it at the end |");
            Line(report, $"|---|---|---|---|---|---|");
            foreach (var nest in nests)
            {
                NumericFrameSample frame = nest.First().Frame;
                YardSample lastSeen = samples[(int)nest.Last().Tick];
                string below = string.Join(", ", lastSeen.NumericFrames.Where(f => f.TreeParent == frame.Frame).Select(f => $"{Lower(f.Mode)} #{f.Frame.Index}"));
                Line(report, $"| #{frame.Frame.Index} | {nest.First().Tick} to {nest.Last().Tick} | {Vector(frame.Place, "F1")} | {Vector(frame.Place / DeepYardExpectations.Grid, "R")} | #{frame.TreeParent.Index} | {below} |");
            }
        }
        Line(report, $"");
        var jumps = new List<string>();
        for (int i = 1; i < samples.Count; i++)
            foreach (NumericFrameSample frame in samples[i].NumericFrames.Where(f => f.Mode == NumericMode.Nesting))
                if (samples[i - 1].NumericFrames.FirstOrDefault(f => f.Frame == frame.Frame) is { } before && before.Place != frame.Place)
                    jumps.Add(Invariant($"- After tick {samples[i].Tick}, nest #{frame.Frame.Index} jumped by {Vector((frame.Place - before.Place) / DeepYardExpectations.Grid, "R")} grid steps."));
        Line(report, $"Grid jumps: {(jumps.Count == 0 ? "none." : "")}");
        foreach (string jump in jumps)
            report.AppendLine(jump);
        Line(report, $"");

        Line(report, $"## Expectations");
        Line(report, $"");
        Line(report, $"| Expectation | Expected | Measured | Tolerance | Holds |");
        Line(report, $"|---|---|---|---|---|");
        double touchTick = DeepYardExpectations.TouchTime * DeepYardExpectations.TicksPerSecond;
        Line(report, $"| First tick whose step pushes crate-b | {touchTick} (ends at {DeepYardExpectations.TouchTime} s) | {push} (ends at {push / DeepYardExpectations.TicksPerSecond:F4} s) | 1 tick | {Holds(push > 0 && Math.Abs(push - touchTick) <= 1)} |");
        Double3 momentum = DeepYardExpectations.CratesMomentum(samples[^1]);
        double momentumError = Double3.Distance(momentum, DeepYardExpectations.Momentum) / DeepYardExpectations.Momentum.Length;
        Line(report, $"| Crates' momentum after the contact (kg·m/s) | {Vector(DeepYardExpectations.Momentum, "R")} | {Vector(momentum, "R")} (relative error {momentumError:E2}) | 1e-6 relative | {Holds(momentumError < 1e-6)} |");
        if (rest > 0 && thrustEnd < samples.Count)
        {
            BodySample before = samples[rest].Bodies["person"], after = samples[thrustEnd].Bodies["person"], last = samples[^1].Bodies["person"];
            Double3 thrust = after.FromCenter - before.FromCenter;
            Line(report, $"| Person's displacement over the second of thrust, ticks {rest} to {thrustEnd} (m) | {Vector(DeepYardExpectations.ThrustDisplacement, "F6")} | {Vector(thrust, "F6")} | 1 mm | {Holds(Double3.Distance(thrust, DeepYardExpectations.ThrustDisplacement) < 1e-3)} |");
            Line(report, $"| Person's velocity after the thrust (m/s) | {Vector(DeepYardExpectations.ThrustVelocity, "F6")} | {Vector(after.Velocity, "F6")} | 1e-5 m/s | {Holds(Double3.Distance(after.Velocity, DeepYardExpectations.ThrustVelocity) < 1e-5)} |");
            double seconds = (samples.Count - 1 - thrustEnd) / DeepYardExpectations.TicksPerSecond;
            Double3 drift = last.FromCenter - after.FromCenter, expectedDrift = DeepYardExpectations.ThrustVelocity * seconds;
            Line(report, $"| Person's drift from tick {thrustEnd} to the end, {seconds:F4} s (m) | {Vector(expectedDrift, "F6")} | {Vector(drift, "F6")} | 1 mm | {Holds(Double3.Distance(drift, expectedDrift) < 1e-3)} |");
        }
        else
            Line(report, $"| Person's displacement over the second of thrust | {Vector(DeepYardExpectations.ThrustDisplacement, "F6")} | the person never thrust | 1 mm | no |");
        int notResting = samples.Skip(1).Count(sample => sample.Bodies.Values.Any(body => body.HomeMode != NumericMode.Resting || body.Nest.IsNull));
        Line(report, $"| Ticks after the first in which a body is not in a resting frame under a nest | 0 | {notResting} | none | {Holds(notResting == 0)} |");
        Line(report, $"| Grid jumps | 0 | {jumps.Count} | none | {Holds(jumps.Count == 0)} |");
        Line(report, $"| World.Errors at the end | 0 | {run.World.Errors.Count} | none | {Holds(run.World.Errors.Count == 0)} |");
        Line(report, $"");

        Line(report, $"## Captures");
        Line(report, $"");
        Line(report, $"The script takes two captures from the person's eye, 1280 by 720 pixels, 60 degrees high: {Focal:F1} pixels per meter at 1 m. Space is black. The light comes from behind the person, so the crates' faces toward the person are lit.");
        Line(report, $"");
        Line(report, $"| Capture | Tick | What it must show |");
        Line(report, $"|---|---|---|");
        // At tick 30 the person is at rest, 24 m from the crates' centers, and crate-a has moved 0.05 m.
        double yardFaces = DeepYard.PersonDistance - DeepYard.CrateHalfSize;
        Line(report, $"| yard.png | 30 | The two crates side by side, {yardFaces} m ahead: crate-a orange on the left, crate-b green on the right, each about {Pixels(1, yardFaces):F0} px wide, with a dark gap of about {Pixels(1.95, yardFaces):F0} px between them (1.95 m), centered on the middle column, their centers about {Pixels(0.75, yardFaces):F0} px below the middle row (the eye is 0.75 m above the person's center). |");
        // The press acts from the third tick after it, so the person thrusts in ticks 603 to 662, then drifts at 1.5 m/s.
        double travelled = -DeepYardExpectations.ThrustDisplacement.Z + -DeepYardExpectations.ThrustVelocity.Z * (1260 - 662) / DeepYardExpectations.TicksPerSecond;
        double touchingFaces = DeepYard.PersonDistance - travelled - DeepYard.CrateHalfSize;
        Line(report, $"| touching.png | 1260 | The two crates touching, one block 2 m wide, orange on the left and green on the right, about {touchingFaces:F1} m ahead: each crate about {Pixels(1, touchingFaces):F0} px wide, no gap between them. If the contact is inelastic, both moved +X at 0.05 m/s for 1 s, so the block spans x = -1.45 m to 0.55 m: from about {Pixels(1.45, touchingFaces):F0} px left of the middle column to {Pixels(0.55, touchingFaces):F0} px right of it. Its center is about {Pixels(0.75, touchingFaces):F0} px below the middle row. |");
        Line(report, $"");

        Line(report, $"## World.Errors");
        Line(report, $"");
        if (run.World.Errors.Count == 0)
            Line(report, $"None.");
        foreach (string error in run.World.Errors)
            Line(report, $"- {error}");
        Line(report, $"");
        Line(report, $"Validation messages of the headless renderer: {run.ValidationMessages.Count}.");
        return report.ToString();
    }

    private static void Line(StringBuilder report, FormattableString line) => report.AppendLine(Invariant(line));

    private static string Invariant(FormattableString text) => text.ToString(CultureInfo.InvariantCulture);

    private static string Vector(Double3 v, string format) =>
        string.Create(CultureInfo.InvariantCulture, $"({v.X.ToString(format, CultureInfo.InvariantCulture)}, {v.Y.ToString(format, CultureInfo.InvariantCulture)}, {v.Z.ToString(format, CultureInfo.InvariantCulture)})");

    private static string Home(BodySample body) =>
        body.HomeMode == NumericMode.None ? "space (semantic)" : Invariant($"{Lower(body.HomeMode)} #{body.Home.Index}{(body.Nest.IsNull ? ", no nest" : $", in nest #{body.Nest.Index}")}");

    private static string Count(YardSample sample) =>
        sample.NumericFrames.Count == 0 ? "0"
            : Invariant($"{sample.NumericFrames.Count} ({string.Join(", ", sample.NumericFrames.GroupBy(f => f.Mode).Select(g => $"{g.Count()} {Lower(g.Key)}"))})");

    private static string Lower(NumericMode mode) => mode.ToString().ToLowerInvariant();

    private static string Holds(bool holds) => holds ? "yes" : "no";

    private static double Pixels(double meters, double distance) => Focal * meters / distance;
}
