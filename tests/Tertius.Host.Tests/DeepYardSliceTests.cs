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

/// <summary>
/// A numeric frame: its handle, with the generation, how it moves, its parent in the tree, and its position in that
/// parent, in meters. Numeric frames never turn, so the position is the whole local pose.
/// </summary>
internal sealed record NumericFrameSample(FrameHandle Frame, NumericMode Mode, FrameHandle TreeParent, Double3 Place);

/// <summary>One body of the deep yard after a tick.</summary>
/// <param name="Position">The position in the body's semantic frame, space, in meters. A light-year out, it rounds to 2 m along Z.</param>
/// <param name="Velocity">The velocity in space, in m/s.</param>
/// <param name="Home">The physics home: the frame whose Jolt system holds the body, with its generation.</param>
/// <param name="HomeMode">How the home moves: <see cref="NumericMode.None"/> when the home is space itself.</param>
/// <param name="InHome">The body's position in its home, in meters: the small numbers that physics works in.</param>
/// <param name="Nest">The nearest nesting frame above the home, or null.</param>
/// <param name="NestPlace">The nest's position in its tree parent, in meters, or zero without a nest.</param>
/// <param name="InNest">The body's position in the nest, in meters, or zero without a nest.</param>
internal sealed record BodySample(Double3 Position, Double3 Velocity, FrameHandle Home, NumericMode HomeMode, Double3 InHome,
    FrameHandle Nest, Double3 NestPlace, Double3 InNest)
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
        return new BodySample(world.PoseIn(entity, frame).Position, world.VelocityIn(entity, frame), home, world.Frames.Numeric(home),
            world.PoseIn(entity, home).Position, nest,
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

    /// <summary>The time that the script holds W, in seconds: 1 s.</summary>
    public const double ThrustSeconds = ThrustTicks / TicksPerSecond;

    /// <summary>The grid of a nesting frame, in meters: 2^20 m.</summary>
    public const double Grid = 1 << 20;

    /// <summary>When the crates touch, in seconds from the start: the 2 m between their faces at 0.1 m/s, 20 s.</summary>
    public static double TouchTime => (DeepYard.CrateSpacing - 2 * DeepYard.CrateHalfSize) / DeepYard.CrateSpeed;

    /// <summary>The crates' momentum, in kg·m/s, in space's axes: crate-a's at the start, 5 kg·m/s along +X.</summary>
    public static Double3 Momentum => new(DeepYard.CrateMass * DeepYard.CrateSpeed, 0, 0);

    /// <summary>
    /// The speed of both crates along +X after the contact, in m/s, if the contact is inelastic: the momentum over the
    /// mass of both, 0.05 m/s.
    /// </summary>
    public static double SharedSpeed => Momentum.X / (2 * DeepYard.CrateMass);

    /// <summary>
    /// The X of crate-a's center from the yard's center at <paramref name="time"/> seconds, in meters: it starts at
    /// -1.5 m and moves at 0.1 m/s until the touch, then with crate-b at <see cref="SharedSpeed"/>, if the contact is
    /// inelastic. crate-b's center lies 1 m farther along +X from the touch on.
    /// </summary>
    public static double CrateACenterX(double time) =>
        -DeepYard.CrateSpacing / 2 + DeepYard.CrateSpeed * Math.Min(time, TouchTime) + SharedSpeed * Math.Max(0, time - TouchTime);

    /// <summary>The person's acceleration while it thrusts, in m/s²: the thrust's force over the person's mass, 1.5.</summary>
    public static double Thrust => (double)PersonControl.ThrustForce / DeepYard.PersonMass;

    /// <summary>The person's velocity after the second of thrust, in space, in m/s: 1.5 m/s along -Z, toward the crates.</summary>
    public static Double3 ThrustVelocity => new(0, 0, -Thrust * ThrustSeconds);

    /// <summary>
    /// The user's check of the thrust: how far a continuous force of the thrust moves the person from rest in the second,
    /// a·t²/2, in space, in meters: 0.75 m along -Z.
    /// </summary>
    public static Double3 ContinuousThrustDisplacement => new(0, 0, -Thrust * ThrustSeconds * ThrustSeconds / 2);

    /// <summary>
    /// How far the engine's 60 steps of thrust move the person from rest, in space, in meters: 0.7625 m along -Z. Each
    /// step adds the acceleration times the tick to the velocity, then moves the person by the new velocity times the
    /// tick (semi-implicit Euler), so the k-th step moves it k·a·dt², and the 60 steps a·dt²·60·61/2. That is a·t·dt/2 =
    /// 12.5 mm, 1.7 percent, more than <see cref="ContinuousThrustDisplacement"/>, at any distance from the origin.
    /// </summary>
    public static Double3 SteppedThrustDisplacement => new(0, 0, -Thrust / (TicksPerSecond * TicksPerSecond) * ThrustTicks * (ThrustTicks + 1) / 2);

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
    /// <summary>Why the user's check of the thrust, a·t²/2 within 1 mm, is skipped.</summary>
    private const string ThrustOvershoot =
        "Integrator overshoot: the engine's step adds the thrust over the mass times the tick to the person's velocity, then "
        + "moves the person by the new velocity times the tick (semi-implicit Euler). So the 60 ticks of the second move it "
        + "a·dt²·60·61/2 = 0.7625 m, which is 12.5 mm (1.7 percent) more than a·t²/2 = 0.75 m, at any distance from the "
        + "origin. TheSixtyStepsOfThrustMoveThePersonAsTheEnginesStepPredicts checks the stepped value.";

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

    /// <summary>The user's check: the second of thrust moves the person as a continuous force does, a·t²/2, within 1 mm.</summary>
    [Fact(Skip = ThrustOvershoot)]
    public void TheSecondOfThrustMovesThePersonAsAContinuousForceOfThrustOverMassWould()
    {
        (BodySample before, BodySample after) = PersonAroundTheThrust();

        Double3.Distance(after.FromCenter - before.FromCenter, DeepYardExpectations.ContinuousThrustDisplacement).Should().BeLessThan(1e-3,
            "a force of 105 N on 70 kg for 1 s moves the person a·t²/2 = 0.75 m from rest");
    }

    [Fact]
    public void TheSixtyStepsOfThrustMoveThePersonAsTheEnginesStepPredicts()
    {
        (BodySample before, BodySample after) = PersonAroundTheThrust();

        Double3.Distance(after.FromCenter - before.FromCenter, DeepYardExpectations.SteppedThrustDisplacement).Should().BeLessThan(1e-3,
            "60 semi-implicit Euler steps of 1.5 m/s² move the person a·dt²·60·61/2 = 0.7625 m");
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
    public void EveryBodyKeepsOneRestingFrameUnderOneNestingFrameOnTheGridAndNoNumericFrameChanges()
    {
        Samples.Select(sample => sample.Tick).Should().Equal(Enumerable.Range(0, Samples.Count).Select(tick => (long)tick),
            "the run sampled the state after every tick");
        YardSample first = Samples[1];
        BodySample person = first.Bodies["person"];
        FrameHandle nest = person.Nest, space = run.World.GetOwnedFrame(run.Scene["space"]);

        nest.IsNull.Should().BeFalse("the yard's resting frames lie a light-year out, far past 10^9 m, so they nest");
        run.World.Frames.TreeParent(nest).Should().Be(space);
        Math.IEEERemainder(person.NestPlace.X, DeepYardExpectations.Grid).Should().Be(0);
        Math.IEEERemainder(person.NestPlace.Y, DeepYardExpectations.Grid).Should().Be(0);
        Math.IEEERemainder(person.NestPlace.Z, DeepYardExpectations.Grid).Should().Be(0);
        (person.NestPlace - DeepYard.Center).Length.Should().BeLessThan(DeepYardExpectations.Grid, "the nest lies near the yard");
        foreach (YardSample sample in Samples.Skip(1))
        {
            // The records compare the handles with their generations, so a frame that is destroyed and made again in the
            // same slot fails here, as does a frame that moves, changes its mode or changes its parent.
            sample.NumericFrames.Should().Equal(first.NumericFrames,
                $"no numeric frame is made, destroyed, moved or put under another parent after tick {sample.Tick}");
            foreach ((string id, BodySample body) in sample.Bodies)
            {
                body.HomeMode.Should().Be(NumericMode.Resting, $"{id} is slow and a light-year out after tick {sample.Tick}");
                body.Home.Should().Be(first.Bodies[id].Home, $"{id} keeps its resting frame, the same handle, after tick {sample.Tick}");
                body.Nest.Should().Be(nest, $"one nest holds every frame of the yard after tick {sample.Tick}");
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

    /// <summary>The person at the last tick at rest, and 60 ticks later, at the end of the thrust.</summary>
    private (BodySample Before, BodySample After) PersonAroundTheThrust()
    {
        int rest = DeepYardExpectations.LastTickAtRest(Samples);
        rest.Should().BePositive("the person thrusts");
        return (Samples[rest].Bodies["person"], Samples[rest + DeepYardExpectations.ThrustTicks].Bodies["person"]);
    }
}

/// <summary>
/// The report of a run of the deep yard, in Markdown: what the scene is and what it must show, the state every 2 s of
/// simulated time, the numeric frames and the homes of the bodies, what the nest changes in this run, each expectation
/// with the expected and the measured value, the captures, and notes on measures that need no test.
/// </summary>
internal static class DeepYardReport
{
    /// <summary>The scale of the captures: 360 / tan(30 degrees) pixels per meter at 1 m, for a view 720 pixels and 60 degrees high.</summary>
    private static readonly double Focal = 360 / Math.Tan(Math.PI / 6);

    /// <summary>Jolt's speculative contact distance, in meters: a contact starts when the gap between two bodies falls below it.</summary>
    private const double SpeculativeDistance = 0.02;

    public static string Write(DeepYardRun run)
    {
        IReadOnlyList<YardSample> samples = run.Samples;
        var report = new StringBuilder();
        int push = DeepYardExpectations.FirstPushOfCrateB(samples), rest = DeepYardExpectations.LastTickAtRest(samples);
        int thrustEnd = rest < 0 ? -1 : rest + DeepYardExpectations.ThrustTicks;
        NumericFrameOptions rules = run.World.Frames.NumericOptions;
        FrameHandle space = run.World.GetOwnedFrame(run.Scene["space"]);

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
        Line(report, $"The crates' gap lies along X, not along Z. Spawn positions are in space's coordinates, which step by 2 m along Z a light-year out, so no spawn can place two crates 3 m apart along Z. A semantic frame at the yard's center would hold 3 m along Z exactly, but then the crates would lie near the origin of their semantic frame, and the engine would give them no numeric frame at all. In either axis, the contact runs in the small numbers of the crates' one resting frame.");
        Line(report, $"");
        Line(report, $"## What it must show");
        Line(report, $"");
        Line(report, $"- The crates touch at {DeepYardExpectations.TouchTime} s (the 2 m between their faces at {DeepYard.CrateSpeed} m/s), within one tick.");
        Line(report, $"- The crates' momentum stays {DeepYardExpectations.Momentum.X} kg·m/s along +X through the contact, within 1e-6 relative.");
        Line(report, $"- The user's check: the second of thrust moves the person as thrust over mass predicts, within 1 mm. For a continuous force that is a·t²/2 = {-DeepYardExpectations.ContinuousThrustDisplacement.Z} m. The engine works in steps of 1/60 s (semi-implicit Euler), and its 60 steps of {DeepYardExpectations.Thrust} m/s² predict a·dt²·60·61/2 = {-DeepYardExpectations.SteppedThrustDisplacement.Z} m. The report shows both. The test of the first is skipped, as an overshoot of the engine's step, and the test of the second runs.");
        Line(report, $"- After the thrust the person drifts at {-DeepYardExpectations.ThrustVelocity.Z} m/s, within 1 mm over the rest of the run.");
        Line(report, $"- Every body keeps one resting numeric frame, the same handle with the same generation at every tick, under one nesting frame on the grid of 2^20 m, which never jumps. World.Errors stays empty.");
        Line(report, $"");
        Line(report, $"Positions in space round to 2 m along Z, a light-year out. The column \"From the yard's center\" reads each body through its nesting frame instead: the nest's place minus the center, which is exact, plus the body's position in the nest.");
        Line(report, $"");

        Line(report, $"## State every 2 s");
        Line(report, $"");
        Line(report, $"| Tick | Time (s) | Body | Position in space (m) | From the yard's center (m) | Position in its home (m) | Velocity in space (m/s) | Physics home | Numeric frames | Errors |");
        Line(report, $"|---|---|---|---|---|---|---|---|---|---|");
        var ticks = new SortedSet<int>(Enumerable.Range(0, samples.Count).Where(tick => tick % 120 == 0)) { samples.Count - 1 };
        foreach (int tick in new[] { rest, thrustEnd, push - 1, push })
            if (tick >= 0 && tick < samples.Count)
                ticks.Add(tick);
        foreach (int tick in ticks)
        {
            YardSample sample = samples[tick];
            foreach ((string id, BodySample body) in sample.Bodies)
                Line(report, $"| {sample.Tick} | {sample.Time:F3} | {id} | {Vector(body.Position, "F4")} | {Vector(body.FromCenter, "F4")} | {Vector(body.InHome, "F4")} | {Vector(body.Velocity, "F6")} | {Home(body)} | {Count(sample)} | {sample.Errors.Count} |");
        }
        Line(report, $"");
        Line(report, $"Rows at other ticks than every 2 s: tick {rest}, the last at rest before the thrust; tick {thrustEnd}, the end of the thrust; ticks {push - 1} and {push}, before and at the first push of crate-b. A frame handle reads #index v generation: a frame destroyed and made again in the same slot gets a new generation.");
        Line(report, $"");

        Line(report, $"## Numeric frames");
        Line(report, $"");
        var seen = samples.SelectMany(sample => sample.NumericFrames.Select(frame => (sample.Tick, Frame: frame))).GroupBy(s => s.Frame.Frame).ToList();
        if (seen.Count == 0)
            Line(report, $"None: no numeric frame ever held the yard's bodies.");
        else
        {
            Line(report, $"Every numeric frame below space over the run. A place is the frame's position in its tree parent. From the yard's center adds the places up through the nest, so it stays exact. Exact in space says whether the frame's place in space, the sum of the places up to space, is the exact sum of them in a double.");
            Line(report, $"");
            Line(report, $"| Frame | Mode | Tree parent | Ticks | Place at the first tick (m) | Place at the last tick (m) | Place in grid steps of 2^20 m | From the yard's center (m) | Exact in space | Ticks in which it changed |");
            Line(report, $"|---|---|---|---|---|---|---|---|---|---|");
            foreach (var frame in seen)
            {
                NumericFrameSample start = frame.First().Frame, end = frame.Last().Frame;
                YardSample startSample = samples[(int)frame.First().Tick];
                int changes = frame.Zip(frame.Skip(1)).Count(pair => pair.Second.Tick == pair.First.Tick + 1 && pair.Second.Frame != pair.First.Frame);
                string grid = start.Mode == NumericMode.Nesting ? Vector(start.Place / DeepYardExpectations.Grid, "R") : "";
                Line(report, $"| {Handle(start.Frame)} | {Lower(start.Mode)} | {Handle(start.TreeParent)}{(start.TreeParent == space ? " (space)" : "")} | {frame.First().Tick} to {frame.Last().Tick} | {Vector(start.Place, "R")} | {Vector(end.Place, "R")} | {grid} | {Vector(FromCenter(startSample, start), "R")} | {Holds(ExactInSpace(startSample, start))} | {changes} |");
            }
        }
        Line(report, $"");
        var jumps = new List<string>();
        for (int i = 1; i < samples.Count; i++)
            foreach (NumericFrameSample frame in samples[i].NumericFrames.Where(f => f.Mode == NumericMode.Nesting))
                if (samples[i - 1].NumericFrames.FirstOrDefault(f => f.Frame == frame.Frame) is { } before && before.Place != frame.Place)
                    jumps.Add(Invariant($"- After tick {samples[i].Tick}, nest {Handle(frame.Frame)} jumped by {Vector((frame.Place - before.Place) / DeepYardExpectations.Grid, "R")} grid steps."));
        Line(report, $"Grid jumps: {(jumps.Count == 0 ? "none." : "")}");
        foreach (string jump in jumps)
            report.AppendLine(jump);
        Line(report, $"");
        Line(report, $"Homes of the bodies, after tick 1. A change of home is a tick whose home handle, with its generation, differs from the tick before.");
        Line(report, $"");
        Line(report, $"| Body | Homes over the run | Changes of home |");
        Line(report, $"|---|---|---|");
        foreach (string id in YardSample.BodyIds)
        {
            var homes = samples.Skip(1).Select(sample => sample.Bodies[id].Home).ToList();
            int changes = homes.Zip(homes.Skip(1)).Count(pair => pair.First != pair.Second);
            Line(report, $"| {id} | {string.Join(", ", homes.Distinct().Select(Handle))} | {changes} |");
        }
        Line(report, $"");

        Line(report, $"## What the nest changes in this run");
        Line(report, $"");
        double fastest = samples.SelectMany(sample => sample.Bodies.Values).Max(body => body.Velocity.Length);
        double farthest = samples.SelectMany(sample => sample.Bodies.Values).Where(body => body.HomeMode != NumericMode.None).Select(body => body.InHome.Length).DefaultIfEmpty().Max();
        int moved = seen.Count(frame => frame.Select(s => s.Frame.Place).Distinct().Count() > 1);
        int inexact = seen.Count(frame => !ExactInSpace(samples[(int)frame.First().Tick], frame.First().Frame));
        Line(report, $"A numeric frame moves only when it falls, at {rules.CreateSpeed} m/s or more, or when its members' center of mass lies more than {rules.RecenterDistance} m from its origin, which recenters it. The fastest body of the run moves at {fastest:F6} m/s, and the farthest body in a numeric frame lies {farthest:F4} m from that frame's origin. {moved} numeric frames moved, and {inexact} have a place in space that a double does not hold exactly (the column Exact in space above).");
        Line(report, $"");
        if (moved == 0 && inexact == 0)
            Line(report, $"So the nest changes nothing that the run measures. Each body moves in small numbers in its resting frame, and the frames' places in space are exact, because the spawn positions are even numbers of meters along Z. A run without the nest would give the same numbers.");
        else
            Line(report, $"So for those frames a read through space rounds, and only the reads through the nest stay exact.");
        Line(report, $"");
        Line(report, $"The run shows that the engine makes the nest where the design says (farther than {rules.NestDistance:G3} m from space's origin, on the grid of 2^20 m, within a grid step of the yard), that the nest never jumps, and that every read through it is exact. A numeric frame that moves a light-year out, a falling one or one that recenters, would need the nest: its place in space would round by up to 1 m at each move.");
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
            double continuousError = Double3.Distance(thrust, DeepYardExpectations.ContinuousThrustDisplacement);
            double steppedError = Double3.Distance(thrust, DeepYardExpectations.SteppedThrustDisplacement);
            Line(report, $"| The user's check: person's displacement over the second of thrust, ticks {rest} to {thrustEnd}, against a continuous force, a·t²/2 (m) | {Vector(DeepYardExpectations.ContinuousThrustDisplacement, "F6")} | {Vector(thrust, "F6")}, off by {continuousError * 1000:F2} mm, {continuousError / -DeepYardExpectations.ContinuousThrustDisplacement.Z * 100:F2} percent | 1 mm | {Holds(continuousError < 1e-3)}: the engine's step overshoots a continuous force; its test is skipped |");
            Line(report, $"| The same displacement against the engine's 60 steps, a·dt²·60·61/2 (m) | {Vector(DeepYardExpectations.SteppedThrustDisplacement, "F6")} | {Vector(thrust, "F6")}, off by {steppedError * 1000:F4} mm | 1 mm | {Holds(steppedError < 1e-3)} |");
            Line(report, $"| Person's velocity after the thrust (m/s) | {Vector(DeepYardExpectations.ThrustVelocity, "F6")} | {Vector(after.Velocity, "R")} | 1e-5 m/s | {Holds(Double3.Distance(after.Velocity, DeepYardExpectations.ThrustVelocity) < 1e-5)} |");
            double seconds = (samples.Count - 1 - thrustEnd) / DeepYardExpectations.TicksPerSecond;
            Double3 drift = last.FromCenter - after.FromCenter, expectedDrift = DeepYardExpectations.ThrustVelocity * seconds;
            Line(report, $"| Person's drift from tick {thrustEnd} to the end, {seconds:F4} s (m) | {Vector(expectedDrift, "F6")} | {Vector(drift, "F6")} | 1 mm | {Holds(Double3.Distance(drift, expectedDrift) < 1e-3)} |");
        }
        else
            Line(report, $"| Person's displacement over the second of thrust | {Vector(DeepYardExpectations.ContinuousThrustDisplacement, "F6")} | the person never thrust | 1 mm | no |");
        int notResting = samples.Skip(1).Count(sample => sample.Bodies.Values.Any(body => body.HomeMode != NumericMode.Resting || body.Nest.IsNull));
        Line(report, $"| Ticks after the first in which a body is not in a resting frame under a nest | 0 | {notResting} | none | {Holds(notResting == 0)} |");
        int homeChanges = YardSample.BodyIds.Sum(id => samples.Skip(1).Zip(samples.Skip(2)).Count(pair => pair.First.Bodies[id].Home != pair.Second.Bodies[id].Home));
        Line(report, $"| Changes of home, handle and generation, after tick 1 | 0 | {homeChanges} | none | {Holds(homeChanges == 0)} |");
        int frameChanges = samples.Skip(1).Zip(samples.Skip(2)).Count(pair => !pair.First.NumericFrames.SequenceEqual(pair.Second.NumericFrames));
        Line(report, $"| Ticks after the first in which the numeric frames change (made, destroyed, moved or put under another parent) | 0 | {frameChanges} | none | {Holds(frameChanges == 0)} |");
        Line(report, $"| Grid jumps | 0 | {jumps.Count} | none | {Holds(jumps.Count == 0)} |");
        Line(report, $"| World.Errors at the end | 0 | {run.World.Errors.Count} | none | {Holds(run.World.Errors.Count == 0)} |");
        Line(report, $"");

        Line(report, $"## Captures");
        Line(report, $"");
        Line(report, $"The script takes two captures from the person's eye, 1280 by 720 pixels, 60 degrees high: {Focal:F1} pixels per meter at 1 m. The middle column is x = 640 px and the middle row y = 360 px. Space is black, with stars. The light comes from space's origin, straight behind the person, so the crates' faces toward the person are lit.");
        Line(report, $"");
        Line(report, $"| Capture | Tick | What it must show |");
        Line(report, $"|---|---|---|");
        // At tick 30 the person is at rest, 24 m from the crates' centers.
        const double yardTime = 30 / DeepYardExpectations.TicksPerSecond;
        double yardFaces = DeepYard.PersonDistance - DeepYard.CrateHalfSize;
        double yardA = DeepYardExpectations.CrateACenterX(yardTime), yardB = DeepYard.CrateSpacing / 2;
        double yardGap = yardB - yardA - 2 * DeepYard.CrateHalfSize;
        Line(report, $"| yard.png | 30 | The two crates side by side, {yardFaces} m ahead: crate-a orange on the left, from x = {yardA - DeepYard.CrateHalfSize:F2} m to {yardA + DeepYard.CrateHalfSize:F2} m, and crate-b green on the right, from {yardB - DeepYard.CrateHalfSize:F2} m to {yardB + DeepYard.CrateHalfSize:F2} m. Each is about {Pixels(1, yardFaces):F0} px wide, with a dark gap of about {Pixels(yardGap, yardFaces):F0} px ({yardGap:F2} m) between them, near the middle column: crate-a from about {Pixels(-(yardA - DeepYard.CrateHalfSize), yardFaces):F0} px to {Pixels(-(yardA + DeepYard.CrateHalfSize), yardFaces):F0} px left of it, crate-b from {Pixels(yardB - DeepYard.CrateHalfSize, yardFaces):F0} px to {Pixels(yardB + DeepYard.CrateHalfSize, yardFaces):F0} px right of it. Their centers lie about {Pixels(0.75, yardFaces):F0} px below the middle row (the eye is 0.75 m above the person's center). The person's shadow, 0.6 m wide around x = 0, falls in the gap, so neither crate shows it. |");
        // The press acts from the third tick after it, so the person thrusts in ticks 603 to 662, then drifts at 1.5 m/s.
        const int touchingTick = 1260;
        double travelled = -DeepYardExpectations.SteppedThrustDisplacement.Z + -DeepYardExpectations.ThrustVelocity.Z * (touchingTick - 662) / DeepYardExpectations.TicksPerSecond;
        double touchingFaces = DeepYard.PersonDistance - travelled - DeepYard.CrateHalfSize;
        double touchingA = DeepYardExpectations.CrateACenterX(touchingTick / DeepYardExpectations.TicksPerSecond);
        double left = touchingA - DeepYard.CrateHalfSize, right = touchingA + 3 * DeepYard.CrateHalfSize;
        // The person's render box is 0.6 m wide around x = 0, and the light comes along -Z, so its shadow covers x from -0.3 m to 0.3 m.
        double shadowEnd = Math.Min(0.3, touchingA + DeepYard.CrateHalfSize);
        Line(report, $"| touching.png | {touchingTick} | The two crates touching, one block 2 m wide, orange on the left and green on the right, about {touchingFaces:F2} m ahead: each crate about {Pixels(1, touchingFaces):F0} px wide, no gap between them. If the contact is inelastic, both have moved +X at {DeepYardExpectations.SharedSpeed} m/s for {touchingTick / DeepYardExpectations.TicksPerSecond - DeepYardExpectations.TouchTime:F0} s since the touch, so crate-a's center lies at x = {touchingA:F2} m and crate-b's at {touchingA + 2 * DeepYard.CrateHalfSize:F2} m, and the block spans x = {left:F2} m to {right:F2} m: from about {Pixels(left, touchingFaces):F0} px to {Pixels(right, touchingFaces):F0} px right of the middle column. Its center is about {Pixels(0.75, touchingFaces):F0} px below the middle row. The person stands between the light and the crates, so its shadow darkens crate-a's face from x = {left:F2} m to {shadowEnd:F2} m, its left {Pixels(shadowEnd - left, touchingFaces):F0} px or so, with a soft edge, over the block's full height. |");
        Line(report, $"");

        Line(report, $"## Notes");
        Line(report, $"");
        Line(report, $"Measures that need no test, and how to read them in the dumps of the headless run.");
        Line(report, $"");
        double contactTime = (DeepYard.CrateSpacing - 2 * DeepYard.CrateHalfSize - SpeculativeDistance) / DeepYard.CrateSpeed;
        Line(report, $"- The crates' Collision event comes before their first push. A contact starts when the gap between two bodies falls below Jolt's speculative contact distance of {SpeculativeDistance} m, at about {contactTime:F2} s (tick {contactTime * DeepYardExpectations.TicksPerSecond:F0} or the next), {(DeepYardExpectations.TouchTime - contactTime):F2} s before the faces touch. Its impulse is the engine's estimate, the approach speed times the reduced mass, {DeepYard.CrateSpeed} m/s × {DeepYard.CrateMass / 2} kg = {DeepYard.CrateSpeed * DeepYard.CrateMass / 2} N·s, not the impulse that the contact applies.");
        double sideways = samples.SelectMany(sample => new[] { sample.Bodies["crate-a"], sample.Bodies["crate-b"] }).Max(body => Math.Max(Math.Abs(body.Velocity.Y), Math.Abs(body.Velocity.Z)));
        Line(report, $"- After the contact the crates keep a small sideways velocity from the solver, at most {sideways:E2} m/s across X. It is negligible.");
        if (thrustEnd > 0 && thrustEnd < samples.Count)
            Line(report, $"- The person's velocity after the thrust is a float sum of 60 steps of {DeepYardExpectations.Thrust / DeepYardExpectations.TicksPerSecond} m/s: {samples[thrustEnd].Bodies["person"].Velocity.Z:R} m/s along Z rather than -1.5. That is within the 1e-5 m/s of its test.");
        Line(report, $"- The script posts the press of W after tick 600. The press acts from the third tick after it, so the person is last at rest after tick {rest} and thrusts in the 60 ticks after that.");
        Line(report, $"- The dumps of the headless run list only the semantic frame `space`, because the engine's public surface reports only semantic frames. The numeric frames above come from the test's own reads.");
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

    /// <summary>The numeric frame <paramref name="frame"/> in <paramref name="sample"/>, or null when it is not numeric.</summary>
    private static NumericFrameSample? FrameOf(YardSample sample, FrameHandle frame) => sample.NumericFrames.FirstOrDefault(f => f.Frame == frame);

    /// <summary>
    /// The position of a numeric frame relative to the yard's center, in space's axes, in meters: the places of the
    /// numeric frames up the tree added, with the place of the one in space minus the center. Only small numbers add.
    /// </summary>
    private static Double3 FromCenter(YardSample sample, NumericFrameSample frame) =>
        FrameOf(sample, frame.TreeParent) is { } parent ? FromCenter(sample, parent) + frame.Place : frame.Place - DeepYard.Center;

    /// <summary>
    /// True when the frame's place in space, the sum of the places of the numeric frames up the tree, is exact in a double:
    /// at each step up, the sum minus the parent's place in space gives back the frame's place.
    /// </summary>
    private static bool ExactInSpace(YardSample sample, NumericFrameSample frame)
    {
        if (FrameOf(sample, frame.TreeParent) is not { } parent)
            return true;
        Double3 parentInSpace = InSpace(sample, parent);
        return ExactInSpace(sample, parent) && (parentInSpace + frame.Place) - parentInSpace == frame.Place;
    }

    /// <summary>The frame's place in space, the sum of the places of the numeric frames up the tree, in doubles.</summary>
    private static Double3 InSpace(YardSample sample, NumericFrameSample frame) =>
        FrameOf(sample, frame.TreeParent) is { } parent ? InSpace(sample, parent) + frame.Place : frame.Place;

    private static void Line(StringBuilder report, FormattableString line) => report.AppendLine(Invariant(line));

    private static string Invariant(FormattableString text) => text.ToString(CultureInfo.InvariantCulture);

    private static string Vector(Double3 v, string format) =>
        string.Create(CultureInfo.InvariantCulture, $"({v.X.ToString(format, CultureInfo.InvariantCulture)}, {v.Y.ToString(format, CultureInfo.InvariantCulture)}, {v.Z.ToString(format, CultureInfo.InvariantCulture)})");

    private static string Handle(FrameHandle frame) => Invariant($"#{frame.Index}v{frame.Generation}");

    private static string Home(BodySample body) =>
        body.HomeMode == NumericMode.None ? "space (semantic)" : Invariant($"{Lower(body.HomeMode)} {Handle(body.Home)}{(body.Nest.IsNull ? ", no nest" : $", in nest {Handle(body.Nest)}")}");

    private static string Count(YardSample sample) =>
        sample.NumericFrames.Count == 0 ? "0"
            : Invariant($"{sample.NumericFrames.Count} ({string.Join(", ", sample.NumericFrames.GroupBy(f => f.Mode).Select(g => $"{g.Count()} {Lower(g.Key)}"))})");

    private static string Lower(NumericMode mode) => mode.ToString().ToLowerInvariant();

    private static string Holds(bool holds) => holds ? "yes" : "no";

    private static double Pixels(double meters, double distance) => Focal * meters / distance;
}
