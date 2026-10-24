using System.Globalization;
using System.Text;
using Tertius.Demo;
using Tertius.Simulation;
using DeepYard = Tertius.Demo.DemoScenes.DeepYard;

namespace Tertius.Host.Tests;

/// <summary>
/// One headless run of the checked-in script scripts/deep-yard.json in the scene deep-yard (see
/// <see cref="DemoScenes.DeepYard"/>), with the state of its bodies after every tick. The whole yard drifts at 2,000 m/s.
/// In it, crate-a closes the 2 m between itself and crate-b at 0.1 m/s and pushes it. The person floats at rest for 10 s,
/// thrusts toward the crates for 1 s, drifts toward them, and joins their numeric frame. Every test of the class reads
/// that one run.
/// </summary>
public sealed class DeepYardRun : CheckedInRun
{
    public DeepYardRun() : this([])
    {
    }

    private DeepYardRun(List<YardSample> samples) : base("deep-yard",
        (world, scene) => samples.Add(YardSample.Take(world, scene)))
    {
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
internal sealed record YardSample(
    long Tick,
    double Time,
    IReadOnlyDictionary<string, DeepYardBody> Bodies,
    IReadOnlyList<NumericFrameSample> NumericFrames,
    IReadOnlyList<string> Errors)
{
    public static readonly string[] BodyIds = ["crate-a", "crate-b", "person"];

    /// <summary>
    /// The position of body <paramref name="id"/> in the yard, in meters: relative to the yard's center, which moves at
    /// <see cref="DeepYard.Drift"/> from <see cref="DeepYard.Center"/>, in space's axes. It is the read through the nest
    /// (<see cref="DeepYardBody.FromCenter"/>) minus the drift since the start, so only small numbers enter it.
    /// </summary>
    public Double3 InYard(string id) => Bodies[id].FromCenter - DeepYard.Drift * Time;

    /// <summary>The velocity of body <paramref name="id"/> relative to the yard, in m/s: its velocity in space minus the drift.</summary>
    public Double3 VelocityInYard(string id) => Bodies[id].Velocity - DeepYard.Drift;

    public static YardSample Take(World world, SceneInstance scene)
    {
        var numericFrames = new List<NumericFrameSample>();
        AddNumericFrames(world.Frames, world.GetOwnedFrame(scene["space"]), numericFrames);
        return new(Tick: world.TickNumber, Time: world.Time,
            Bodies: BodyIds.ToDictionary(id => id, id => DeepYardBody.Take(world, scene[id])),
            NumericFrames: numericFrames, Errors: [.. world.Errors]);
    }

    private static void AddNumericFrames(FrameTree tree, FrameHandle frame, List<NumericFrameSample> numericFrames)
    {
        var children = new List<FrameHandle>();
        tree.GetTreeChildren(frame, children);
        foreach (FrameHandle child in children)
        {
            if (tree.IsNumeric(child))
                numericFrames.Add(new(Frame: child, Mode: tree.Numeric(child), TreeParent: tree.TreeParent(child),
                    Place: tree.TreeLocalPose(child).Position));
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
/// <param name="Velocity">The velocity in space, in m/s. It holds the yard's drift.</param>
/// <param name="Home">The physics home: the frame whose Jolt system holds the body, with its generation.</param>
/// <param name="HomeMode">How the home moves: <see cref="NumericMode.None"/> when the home is space itself.</param>
/// <param name="InHome">The body's position in its home, in meters: the small numbers that physics works in.</param>
/// <param name="Nest">The nearest nesting frame above the home, or null.</param>
/// <param name="NestPlace">The nest's position in its tree parent, in meters, or zero without a nest.</param>
/// <param name="InNest">The body's position in the nest, in meters, or zero without a nest.</param>
internal sealed record DeepYardBody(
    Double3 Position,
    Double3 Velocity,
    FrameHandle Home,
    NumericMode HomeMode,
    Double3 InHome,
    FrameHandle Nest,
    Double3 NestPlace,
    Double3 InNest)
{
    /// <summary>
    /// The body's position relative to the yard's center at the start, in space's axes, in meters. Through a nest that
    /// lies in space, it is the nest's place minus the center plus the position in the nest. The nest's place is whole
    /// grid steps, and it lies within a grid step of the center, so the difference of the two is exact, and only small
    /// numbers add. A read in space would round to 2 m along Z. Without a nest, this is that read minus the center.
    /// </summary>
    public Double3 FromCenter => Nest.IsNull ? Position - DeepYard.Center : NestPlace - DeepYard.Center + InNest;

    public static DeepYardBody Take(World world, Entity entity)
    {
        FrameHandle frame = world.GetFrame(entity), home = world.PhysicsHomeOf(entity), nest = home;
        while (!nest.IsNull && world.Frames.Numeric(nest) != NumericMode.Nesting)
            nest = world.Frames.TreeParent(nest);
        return new(Position: world.PoseIn(entity, frame).Position, Velocity: world.VelocityIn(entity, frame), Home: home,
            HomeMode: world.Frames.Numeric(home),
            InHome: world.PoseIn(entity, home).Position, Nest: nest,
            NestPlace: nest.IsNull ? Double3.Zero : world.Frames.TreeLocalPose(nest).Position,
            InNest: nest.IsNull ? Double3.Zero : world.PoseIn(entity, nest).Position);
    }
}

/// <summary>
/// What the deep yard must show, worked out from the scene's numbers and the script alone, and the measures of a run that
/// the tests and the report compare with them. Positions and velocities "in the yard" are relative to the yard's center,
/// which drifts at <see cref="DeepYard.Drift"/>.
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

    /// <summary>How far ahead in time the merge test of numeric frames looks, in seconds (docs/plans/numeric-frames.md, "Starting values").</summary>
    public const double LookaheadTime = 2;

    /// <summary>The radius of the sphere around a crate's center that bounds it, in meters: half its diagonal.</summary>
    public static double CrateRadius => DeepYard.CrateHalfSize * Math.Sqrt(3);

    /// <summary>The radius of the sphere around the person's center that bounds its capsule, in meters: 0.9.</summary>
    public static double PersonRadius => DeepYard.PersonShape.HalfHeight + DeepYard.PersonShape.Radius;

    /// <summary>When the crates touch, in seconds from the start: the 2 m between their faces at 0.1 m/s, 20 s.</summary>
    public static double TouchTime => (DeepYard.CrateSpacing - 2 * DeepYard.CrateHalfSize) / DeepYard.CrateSpeed;

    /// <summary>The crates' momentum in the yard, in kg·m/s, in space's axes: crate-a's at the start, 5 kg·m/s along +X.</summary>
    public static Double3 Momentum => new(DeepYard.CrateMass * DeepYard.CrateSpeed, 0, 0);

    /// <summary>
    /// The speed of both crates along +X in the yard after the contact, in m/s, if the contact is inelastic: the momentum
    /// over the mass of both, 0.05 m/s.
    /// </summary>
    public static double SharedSpeed => Momentum.X / (2 * DeepYard.CrateMass);

    /// <summary>The person's acceleration while it thrusts, in m/s²: the thrust's force over the person's mass, 1.5.</summary>
    public static double Thrust => (double)PersonControl.ThrustForce / DeepYard.PersonMass;

    /// <summary>The person's velocity in the yard after the second of thrust, in m/s: 1.5 m/s along -Z, toward the crates.</summary>
    public static Double3 ThrustVelocity => new(0, 0, -Thrust * ThrustSeconds);

    /// <summary>
    /// How far a force of the thrust that acts without a break for the second moves the person from rest, a·t²/2, in
    /// meters: 0.75 m along -Z. The engine works in ticks, so this is a reference, not what the engine must give: see
    /// <see cref="SteppedThrustDisplacement"/>.
    /// </summary>
    public static Double3 ContinuousThrustDisplacement => new(0, 0, -Thrust * ThrustSeconds * ThrustSeconds / 2);

    /// <summary>
    /// How far the 60 ticks of thrust move the person from rest in the yard, in meters: 0.7625 m along -Z. Each tick the
    /// engine adds the thrust over the mass times the tick to the velocity, then moves the person by the new velocity times
    /// the tick (semi-implicit Euler, the scheme that Jolt uses for every rigid body and the engine for every character
    /// and every falling frame). So the k-th tick moves it k·a·dt², and the 60 ticks a·dt²·60·61/2. That is a·t·dt/2 =
    /// 12.5 mm more than <see cref="ContinuousThrustDisplacement"/>: the force acts on the velocity at the start of each
    /// tick, so on average half a tick early.
    /// </summary>
    public static Double3 SteppedThrustDisplacement => new(0, 0,
        -Thrust / (TicksPerSecond * TicksPerSecond) * ThrustTicks * (ThrustTicks + 1) / 2);

    /// <summary>
    /// The X of crate-a's center in the yard at <paramref name="time"/> seconds, in meters: it starts at -1.5 m and moves
    /// at 0.1 m/s until the touch, then with crate-b at <see cref="SharedSpeed"/>, if the contact is inelastic. crate-b's
    /// center lies 1 m farther along +X from the touch on.
    /// </summary>
    public static double CrateACenterX(double time) =>
        -DeepYard.CrateSpacing / 2 + DeepYard.CrateSpeed * Math.Min(time, TouchTime) +
        SharedSpeed * Math.Max(0, time - TouchTime);

    /// <summary>
    /// The first tick whose step pushed crate-b: the first sample in which crate-b moves at more than 1e-6 m/s in the
    /// yard. -1 if crate-b never moves.
    /// </summary>
    public static int FirstPushOfCrateB(IReadOnlyList<YardSample> samples) =>
        IndexOf(samples, sample => sample.VelocityInYard("crate-b").Length > 1e-6);

    /// <summary>
    /// The last tick before the person's thrust: the sample before the first in which the person moves at more than
    /// 1e-6 m/s in the yard. -1 if the person never moves.
    /// </summary>
    public static int LastTickAtRest(IReadOnlyList<YardSample> samples)
    {
        int moving = IndexOf(samples, sample => sample.VelocityInYard("person").Length > 1e-6);
        return moving < 1 ? -1 : moving - 1;
    }

    /// <summary>The first sample in which the person's physics home is the crates' home. -1 if it never is.</summary>
    public static int PersonJoinsTheCrates(IReadOnlyList<YardSample> samples) =>
        IndexOf(samples, sample => sample.Tick > 0 && sample.Bodies["person"].Home == sample.Bodies["crate-a"].Home);

    /// <summary>
    /// The first sample in which the merge test of the design holds between the person and a crate: the spheres that bound
    /// them, grown by their relative speed times <see cref="LookaheadTime"/>, touch. -1 if it never holds.
    /// </summary>
    public static int MergeTestHolds(IReadOnlyList<YardSample> samples) =>
        IndexOf(samples,
            sample => sample.Tick > 0 && new[] { "crate-a", "crate-b" }.Any(id => MergeGap(sample, id) <= 0));

    /// <summary>
    /// How far the person's bounding sphere is from crate <paramref name="id"/>'s in <paramref name="sample"/>, in meters,
    /// less the reach of the merge test: the relative speed times <see cref="LookaheadTime"/>. At zero or below, the test
    /// holds. The separation is read through the nest, so it is exact.
    /// </summary>
    public static double MergeGap(YardSample sample, string id)
    {
        DeepYardBody person = sample.Bodies["person"], crate = sample.Bodies[id];
        double reach = PersonRadius + CrateRadius + (person.Velocity - crate.Velocity).Length * LookaheadTime;
        return (crate.FromCenter - person.FromCenter).Length - reach;
    }

    /// <summary>The momentum of the two crates in the yard in a sample, in kg·m/s, in space's axes.</summary>
    public static Double3 CratesMomentum(YardSample sample) =>
        (sample.VelocityInYard("crate-a") + sample.VelocityInYard("crate-b")) * DeepYard.CrateMass;

    private static int IndexOf(IReadOnlyList<YardSample> samples, Func<YardSample, bool> holds)
    {
        for (int i = 0; i < samples.Count; i++)
            if (holds(samples[i]))
                return i;
        return -1;
    }
}

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
        int push = DeepYardExpectations.FirstPushOfCrateB(Samples);
        push.Should().BePositive("crate-a reaches crate-b and pushes it");
        Double3 before = DeepYardExpectations.CratesMomentum(Samples[push - 1]);
        Double3 after = DeepYardExpectations.CratesMomentum(Samples[^1]);

        // The spawn narrows crate-a's velocity in its frame to a float, so the momentum before the contact is about 5.
        Double3.Distance(before, DeepYardExpectations.Momentum).Should()
            .BeLessThan(1e-6, "crate-a alone carries 50 kg at 0.1 m/s");
        Samples[^1].VelocityInYard("crate-b").X.Should().BePositive("crate-a pushed crate-b before the end of the run");
        (Double3.Distance(after, before) / before.Length).Should().BeLessThan(1e-6,
            "the contact moves momentum between the crates and keeps their sum");
    }

    /// <summary>
    /// The user's check of the thrust, thrust over mass over the second within 1 mm, for the engine's ticks. A force that
    /// acts without a break moves the person a·t²/2 = 0.75 m; the engine's semi-implicit Euler step moves it 12.5 mm
    /// farther (see <see cref="DeepYardExpectations.SteppedThrustDisplacement"/>). The engine keeps that step on purpose:
    /// Jolt steps every rigid body that way, so a character stepped in another way would drift away from a crate under the
    /// same force. A step of explicit Euler (0.7375 m) or of the trapezoidal rule (0.75 m) fails here.
    /// </summary>
    [Fact]
    public void TheSixtyTicksOfThrustMoveThePersonAsThrustOverMassPredictsForTheEnginesStep()
    {
        (int before, int after) = AroundTheThrust();

        Double3.Distance(Samples[after].InYard("person") - Samples[before].InYard("person"),
                DeepYardExpectations.SteppedThrustDisplacement)
            .Should().BeLessThan(1e-3,
                "60 semi-implicit Euler steps of 1.5 m/s² move the person a·dt²·60·61/2 = 0.7625 m");
        // Jolt keeps the velocity in the person's frame as a float, which at 1.5 m/s steps by about 1e-7 m/s, and the
        // thrust rounds it in every tick.
        Double3.Distance(Samples[after].VelocityInYard("person"), DeepYardExpectations.ThrustVelocity).Should()
            .BeLessThan(1e-5);
    }

    [Fact]
    public void AfterTheThrustThePersonDriftsAtTheVelocityThatTheThrustGaveItAlsoThroughItsMerge()
    {
        (_, int end) = AroundTheThrust();
        double seconds = Samples[^1].Time - Samples[end].Time;

        Double3 drift = Samples[^1].InYard("person") - Samples[end].InYard("person");

        DeepYardExpectations.PersonJoinsTheCrates(Samples).Should()
            .BeInRange(end + 1, Samples.Count - 1, "the drift spans the person's merge");
        Double3.Distance(drift, DeepYardExpectations.ThrustVelocity * seconds).Should().BeLessThan(1e-3);
    }

    /// <summary>
    /// The yard drifts 33.33 m along -Z in each tick. A frame whose place were a double in space a light-year out would
    /// round each step to a multiple of 2 m, 34 m, so the bodies at rest in the yard would move 0.67 m farther along -Z than
    /// its center in each tick. Through the nest they stay where they were spawned.
    /// </summary>
    [Fact]
    public void TheBodiesAtRestInTheYardKeepTheirPlacesWhileTheYardDriftsTwoKilometersASecond()
    {
        int push = DeepYardExpectations.FirstPushOfCrateB(Samples), rest = DeepYardExpectations.LastTickAtRest(Samples);
        push.Should().BePositive();
        rest.Should().BePositive();
        var crateB = new Double3(DeepYard.CrateSpacing / 2, 0, 0);
        var person = new Double3(0, 0, DeepYard.PersonDistance);

        foreach (YardSample sample in Samples.Take(push))
            Double3.Distance(sample.InYard("crate-b"), crateB).Should().BeLessThan(1e-6,
                $"crate-b rests in the yard until crate-a reaches it, after tick {sample.Tick}");
        foreach (YardSample sample in Samples.Take(rest + 1))
            Double3.Distance(sample.InYard("person"), person).Should().BeLessThan(1e-6,
                $"the person rests in the yard until it thrusts, after tick {sample.Tick}");
        foreach (YardSample sample in Samples)
        foreach (string id in new[] { "crate-a", "crate-b" })
            sample.InYard(id).Z.Should()
                .BeApproximately(0, 1e-5, $"{id} moves only along X in the yard, after tick {sample.Tick}");
        (Samples[^1].Time * DeepYard.DriftSpeed).Should()
            .BeGreaterThan(40_000, "the yard drifts more than 40 km over the run");
    }

    [Fact]
    public void AReadInSpaceIsTheExactPositionRoundedToTheTwoMeterStepOfADouble()
    {
        foreach (YardSample sample in Samples)
        foreach ((string id, DeepYardBody body) in sample.Bodies)
        {
            Double3 rounding = body.Position - DeepYard.Center - body.FromCenter;
            Math.Abs(rounding.Z).Should().BeLessThanOrEqualTo(1,
                $"{id}'s Z in space rounds to the nearest 2 m, after tick {sample.Tick}");
            Math.Abs(rounding.X).Should()
                .BeLessThan(1e-9, $"{id}'s X in space is a small number, after tick {sample.Tick}");
            Math.Abs(rounding.Y).Should()
                .BeLessThan(1e-9, $"{id}'s Y in space is a small number, after tick {sample.Tick}");
        }
    }

    [Fact]
    public void EveryBodyFallsWithTheYardInAFrameUnderOneNestingFrameThatStaysOnItsGridPoint()
    {
        Samples.Select(sample => sample.Tick).Should().Equal(
            Enumerable.Range(0, Samples.Count).Select(tick => (long)tick),
            "the run sampled the state after every tick");
        FrameHandle space = run.World.GetOwnedFrame(run.Scene["space"]);
        NumericFrameSample nest = Samples[1].NumericFrames.Should()
            .ContainSingle(frame => frame.Mode == NumericMode.Nesting).Which;

        nest.TreeParent.Should().Be(space);
        Math.IEEERemainder(nest.Place.X, DeepYardExpectations.Grid).Should().Be(0);
        Math.IEEERemainder(nest.Place.Y, DeepYardExpectations.Grid).Should().Be(0);
        Math.IEEERemainder(nest.Place.Z, DeepYardExpectations.Grid).Should().Be(0);
        (nest.Place - DeepYard.Center).Length.Should()
            .BeLessThan(DeepYardExpectations.Grid, "the nest lies near the yard");
        foreach (YardSample sample in Samples.Skip(1))
        {
            // The record compares the handle with its generation and the place, so a nest that is made again or jumps fails here.
            sample.NumericFrames.Where(frame => frame.Mode == NumericMode.Nesting).Should().Equal([nest],
                $"one nest, which never moves, holds the yard after tick {sample.Tick}");
            sample.NumericFrames.Where(frame => frame.Mode != NumericMode.Nesting).Should().OnlyContain(
                frame => frame.Mode == NumericMode.Falling && frame.TreeParent == nest.Frame,
                $"every other numeric frame falls with the yard, in the nest, after tick {sample.Tick}");
            foreach ((string id, DeepYardBody body) in sample.Bodies)
            {
                body.HomeMode.Should().Be(NumericMode.Falling,
                    $"{id} moves at {DeepYard.DriftSpeed} m/s in space after tick {sample.Tick}");
                body.Nest.Should().Be(nest.Frame, $"{id}'s frame lies in the nest after tick {sample.Tick}");
            }
        }
    }

    [Fact]
    public void TheCratesKeepOneFrameAndThePersonJoinsItOnceAndStays()
    {
        FrameHandle crates = Samples[1].Bodies["crate-a"].Home;
        List<FrameHandle> personHomes = [.. Samples.Skip(1).Select(sample => sample.Bodies["person"].Home)];

        foreach (YardSample sample in Samples.Skip(1))
        {
            sample.Bodies["crate-a"].Home.Should().Be(crates,
                $"crate-a keeps its frame, the same handle, after tick {sample.Tick}");
            sample.Bodies["crate-b"].Home.Should()
                .Be(crates, $"crate-b shares crate-a's frame after tick {sample.Tick}");
        }

        personHomes[0].Should().NotBe(crates, "the person starts 24 m from the crates, beyond the merge test");
        personHomes.Zip(personHomes.Skip(1)).Count(pair => pair.First != pair.Second).Should().Be(1,
            "the person joins the crates' frame once and never leaves it");
        personHomes[^1].Should().Be(crates);
    }

    [Fact]
    public void ThePersonJoinsTheCratesFrameWhenTheMergeTestFirstHolds()
    {
        (_, int end) = AroundTheThrust();
        int predicted = DeepYardExpectations.MergeTestHolds(Samples);

        predicted.Should().BeGreaterThan(end, "the person nears the crates only after its thrust");
        // The sync point that ends a tick moves the bodies whose merge test holds after that tick's step, and the sample
        // after the tick sees the move. One tick of slack covers the order of the sync point.
        DeepYardExpectations.PersonJoinsTheCrates(Samples).Should().BeInRange(predicted - 1, predicted + 1);
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

    /// <summary>The last sample with the person at rest in the yard, and the sample 60 ticks later, at the end of the thrust.</summary>
    private (int Before, int After) AroundTheThrust()
    {
        int rest = DeepYardExpectations.LastTickAtRest(Samples);
        rest.Should().BePositive("the person thrusts");
        return (rest, rest + DeepYardExpectations.ThrustTicks);
    }
}

/// <summary>
/// The report of a run of the deep yard, in Markdown: what the scene is and what it must show, the state every 2 s of
/// simulated time, the numeric frames and the homes of the bodies, what the nest changes in this run, each expectation
/// with the expected and the measured value, the captures, and notes on measures that need no test.
/// </summary>
internal static class DeepYardReport
{
    /// <summary>Jolt's speculative contact distance, in meters: a contact starts when the gap between two bodies falls below it.</summary>
    private const double SpeculativeDistance = 0.02;

    /// <summary>The scale of the captures: 360 / tan(30 degrees) pixels per meter at 1 m, for a view 720 pixels and 60 degrees high.</summary>
    private static readonly double Focal = 360 / Math.Tan(Math.PI / 6);

    public static string Write(DeepYardRun run)
    {
        IReadOnlyList<YardSample> samples = run.Samples;
        var report = new StringBuilder();
        int push = DeepYardExpectations.FirstPushOfCrateB(samples), rest = DeepYardExpectations.LastTickAtRest(samples);
        int thrustEnd = rest < 0 ? -1 : rest + DeepYardExpectations.ThrustTicks;
        int merge = DeepYardExpectations.PersonJoinsTheCrates(samples),
            mergeTest = DeepYardExpectations.MergeTestHolds(samples);
        NumericFrameOptions rules = run.World.Frames.NumericOptions;
        FrameHandle space = run.World.GetOwnedFrame(run.Scene["space"]);
        double tick = samples.Count > 1 ? samples[1].Time - samples[0].Time : 1 / DeepYardExpectations.TicksPerSecond;

        Line(report, $"# Deep yard: nesting numeric frames a light-year out");
        Line(report, $"");
        Line(report, $"## The scene");
        Line(report, $"");
        Line(report,
            $"The scene `deep-yard` (src/Tertius.Demo/DemoScenes.DeepYard.cs), run by scripts/deep-yard.json, {samples.Count - 1} ticks of {tick:R} s (1/60 s as a float).");
        Line(report, $"");
        Line(report, $"- The root frame `space` has no gravity. Its origin is where the light comes from.");
        Line(report,
            $"- The yard's center starts one light-year ({DeepYard.LightYear:G6} m) from space's origin along -Z. A double steps by 2 m there.");
        Line(report,
            $"- The whole yard drifts through space at {DeepYard.DriftSpeed} m/s along -Z, away from the light: every body spawns with that velocity added. Below, \"in the yard\" means relative to the yard's center, which moves at that velocity.");
        Line(report,
            $"- `crate-a` (orange) and `crate-b` (green) are crates of 1 m and {DeepYard.CrateMass} kg, {DeepYard.CrateSpacing} m apart center to center along X. crate-a moves toward crate-b at {DeepYard.CrateSpeed} m/s in the yard. crate-b rests in the yard.");
        Line(report,
            $"- The `person`, a capsule character of {DeepYard.PersonMass} kg, floats at rest in the yard {DeepYard.PersonDistance} m from its center along +Z and faces the crates (-Z). It is the camera.");
        Line(report,
            $"- The script holds W for {DeepYardExpectations.ThrustTicks} ticks (1 s) from tick 600, so the person thrusts toward the crates with {PersonControl.ThrustForce} N. It then drifts toward them and, after tick {merge}, joins their numeric frame. The run ends at tick {samples.Count - 1}, before the person reaches the crates.");
        Line(report, $"");
        Line(report,
            $"Why the drift: a numeric frame moves only when it falls or recenters. A yard at rest in space would rest in resting frames that never move, and every place would be an even number of meters, which a double holds a light-year out, so a run without the nest would give the same numbers. At {DeepYard.DriftSpeed} m/s the yard's frames fall and move {DeepYard.DriftSpeed * tick:F4} m along -Z in each tick. A double in space, a light-year out, would round each such step to a multiple of 2 m. The section \"What the nest changes in this run\" works that out from the run's own steps.");
        Line(report, $"");
        Line(report,
            $"The crates' gap lies along X, not along Z. Spawn positions are in space's coordinates, which step by 2 m along Z a light-year out, so no spawn can place two crates 3 m apart along Z. The engine has no way to spawn a body more precisely than a double of its semantic frame's coordinates. Along X the coordinates are small.");
        Line(report, $"");
        Line(report, $"## What it must show");
        Line(report, $"");
        Line(report,
            $"- The crates touch at {DeepYardExpectations.TouchTime} s (the 2 m between their faces at {DeepYard.CrateSpeed} m/s), within one tick.");
        Line(report,
            $"- The crates' momentum in the yard stays what it was before the contact, about {DeepYardExpectations.Momentum.X} kg·m/s along +X, within 1e-6 relative.");
        Line(report,
            $"- The second of thrust moves the person as thrust over mass predicts, within 1 mm. The engine works in ticks of 1/60 s with semi-implicit Euler, the scheme that Jolt uses for every rigid body, and its 60 ticks of {DeepYardExpectations.Thrust} m/s² predict a·dt²·60·61/2 = {-DeepYardExpectations.SteppedThrustDisplacement.Z} m. A force that acts without a break would give a·t²/2 = {-DeepYardExpectations.ContinuousThrustDisplacement.Z} m, 12.5 mm less. The report shows both; the test checks the first.");
        Line(report,
            $"- After the thrust the person drifts at {-DeepYardExpectations.ThrustVelocity.Z} m/s in the yard, within 1 mm over the rest of the run, through its merge into the crates' frame.");
        Line(report,
            $"- The bodies at rest in the yard keep their places, within 1e-6 m, while the yard drifts more than 40 km.");
        Line(report,
            $"- Every body falls in a numeric frame under one nesting frame on the grid of 2^20 m, which never jumps. The crates keep one frame. The person joins it once, when the merge test first holds, and stays. World.Errors stays empty.");
        Line(report, $"");
        Line(report,
            $"Positions in space round to 2 m along Z, a light-year out. The column \"In the yard\" reads each body through its nesting frame instead: the nest's place minus the yard's starting center, which is exact, plus the body's position in the nest, minus the drift since the start.");
        Line(report, $"");

        Line(report, $"## State every 2 s");
        Line(report, $"");
        Line(report,
            $"| Tick | Time (s) | Body | Position in space (m) | In the yard (m) | Position in its home (m) | Velocity in space (m/s) | Velocity in the yard (m/s) | Physics home | Numeric frames | Errors |");
        Line(report, $"|---|---|---|---|---|---|---|---|---|---|---|");
        var ticks = new SortedSet<int>(Enumerable.Range(0, samples.Count).Where(t => t % 120 == 0))
            { samples.Count - 1 };
        foreach (int t in new[] { rest, thrustEnd, push - 1, push, merge - 1, merge })
            if (t >= 0 && t < samples.Count)
                ticks.Add(t);
        foreach (int t in ticks)
        {
            YardSample sample = samples[t];
            foreach ((string id, DeepYardBody body) in sample.Bodies)
                Line(report,
                    $"| {sample.Tick} | {sample.Time:F3} | {id} | {Vector(body.Position, "F4")} | {Vector(sample.InYard(id), "F6")} | {Vector(body.InHome, "F4")} | {Vector(body.Velocity, "F6")} | {Vector(sample.VelocityInYard(id), "F6")} | {Home(body)} | {Count(sample)} | {sample.Errors.Count} |");
        }

        Line(report, $"");
        Line(report,
            $"Rows at other ticks than every 2 s: tick {rest}, the last at rest before the thrust; tick {thrustEnd}, the end of the thrust; ticks {push - 1} and {push}, before and at the first push of crate-b; ticks {merge - 1} and {merge}, before and at the person's merge into the crates' frame. A frame handle reads #index v generation: a frame destroyed and made again in the same slot gets a new generation.");
        Line(report, $"");

        Line(report, $"## Numeric frames");
        Line(report, $"");
        List<IGrouping<FrameHandle, (long Tick, NumericFrameSample Frame)>> seen =
        [
            .. samples
                .SelectMany(sample => sample.NumericFrames.Select(frame => (sample.Tick, Frame: frame)))
                .GroupBy(s => s.Frame.Frame)
        ];
        if (seen.Count == 0)
        {
            Line(report, $"None: no numeric frame ever held the yard's bodies.");
        }
        else
        {
            Line(report,
                $"Every numeric frame below space over the run. A place is the frame's position in its tree parent. From the yard's start center adds the places up through the nest, minus the starting center, so it stays exact. Exact in space says whether the frame's place in space, the sum of the places up to space, is the exact sum of them in a double.");
            Line(report, $"");
            Line(report,
                $"| Frame | Mode | Tree parent | Ticks | Place at the first tick (m) | Place at the last tick (m) | Moved over its ticks (m) | Place in grid steps of 2^20 m | From the yard's start center at the first tick (m) | Exact in space at the first tick |");
            Line(report, $"|---|---|---|---|---|---|---|---|---|---|");
            foreach (IGrouping<FrameHandle, (long Tick, NumericFrameSample Frame)> frame in seen)
            {
                NumericFrameSample start = frame.First().Frame, end = frame.Last().Frame;
                YardSample startSample = samples[(int)frame.First().Tick];
                string grid = start.Mode == NumericMode.Nesting
                    ? Vector(start.Place / DeepYardExpectations.Grid, "R")
                    : "";
                Line(report,
                    $"| {Handle(start.Frame)} | {Lower(start.Mode)} | {Handle(start.TreeParent)}{(start.TreeParent == space ? " (space)" : "")} | {frame.First().Tick} to {frame.Last().Tick} | {Vector(start.Place, "R")} | {Vector(end.Place, "R")} | {Vector(end.Place - start.Place, "F6")} | {grid} | {Vector(FromCenter(startSample, start), "F6")} | {Holds(ExactInSpace(startSample, start))} |");
            }
        }

        Line(report, $"");
        var jumps = new List<string>();
        for (int i = 1; i < samples.Count; i++)
            foreach (NumericFrameSample frame in samples[i].NumericFrames.Where(f => f.Mode == NumericMode.Nesting))
                if (samples[i - 1].NumericFrames.FirstOrDefault(f => f.Frame == frame.Frame) is { } before &&
                    before.Place != frame.Place)
                    jumps.Add(Invariant(
                        $"- After tick {samples[i].Tick}, nest {Handle(frame.Frame)} jumped by {Vector((frame.Place - before.Place) / DeepYardExpectations.Grid, "R")} grid steps."));
        double farthestInNest = samples
            .SelectMany(sample => sample.NumericFrames.Where(f => f.Mode != NumericMode.Nesting))
            .Select(f => f.Place.Length).DefaultIfEmpty().Max();
        Line(report, $"Grid jumps: {(jumps.Count == 0 ? "none." : "")}");
        foreach (string jump in jumps)
            report.AppendLine(jump);
        Line(report,
            $"A nest jumps only when one of its frames lies more than {rules.NestDistance:G3} m from it. The farthest that a frame of this run gets from its nest is {farthestInNest:F1} m, so no jump can come in this run: at {DeepYard.DriftSpeed} m/s the first would come after about {rules.NestDistance / DeepYard.DriftSpeed / 86400:F0} days.");
        Line(report, $"");
        Line(report,
            $"Homes of the bodies, after tick 1. A change of home is a tick whose home handle, with its generation, differs from the tick before.");
        Line(report, $"");
        Line(report, $"| Body | Homes over the run, in order | Changes of home | Ticks of the changes |");
        Line(report, $"|---|---|---|---|");
        foreach (string id in YardSample.BodyIds)
        {
            List<(long Tick, FrameHandle Home)> homes =
                [.. samples.Skip(1).Select(sample => (sample.Tick, sample.Bodies[id].Home))];
            List<long> changes =
            [
                .. homes.Zip(homes.Skip(1)).Where(pair => pair.First.Home != pair.Second.Home)
                    .Select(pair => pair.Second.Tick)
            ];
            Line(report,
                $"| {id} | {string.Join(", ", homes.Select(h => h.Home).Distinct().Select(Handle))} | {changes.Count} | {string.Join(", ", changes)} |");
        }

        Line(report, $"");

        Line(report, $"## What the nest changes in this run");
        Line(report, $"");
        Line(report,
            $"Each falling frame steps its place in its tree parent by its velocity times the tick, in doubles. Under the nest that place is a small number. The table adds the same steps, the differences of the frame's place in the nest from one tick to the next, to the frame's place in space at its first tick, as a double in space would hold it without the nest.");
        Line(report, $"");
        Line(report,
            $"| Frame | Ticks | Moved through the nest (m) | Speed through the nest (m/s) | The same steps added in space (m) | Speed in space (m/s) |");
        Line(report, $"|---|---|---|---|---|---|");
        var speedups = new List<double>();
        foreach (IGrouping<FrameHandle, (long Tick, NumericFrameSample Frame)> frame in seen.Where(f =>
                     f.First().Frame.Mode == NumericMode.Falling))
        {
            List<(long Tick, NumericFrameSample Frame)> steps = [.. frame];
            Double3 inSpace = InSpace(samples[(int)steps[0].Tick], steps[0].Frame), first = inSpace;
            for (int i = 1; i < steps.Count; i++)
                inSpace += steps[i].Frame.Place - steps[i - 1].Frame.Place;
            Double3 moved = steps[^1].Frame.Place - steps[0].Frame.Place, movedInSpace = inSpace - first;
            double seconds = samples[(int)steps[^1].Tick].Time - samples[(int)steps[0].Tick].Time;
            if (moved.Length > 0)
                speedups.Add(movedInSpace.Length / moved.Length);
            Line(report,
                $"| {Handle(steps[0].Frame.Frame)} | {steps[0].Tick} to {steps[^1].Tick} | {Vector(moved, "F6")} | {(seconds > 0 ? moved.Length / seconds : 0):F6} | {Vector(movedInSpace, "F6")} | {(seconds > 0 ? movedInSpace.Length / seconds : 0):F6} |");
        }

        Line(report, $"");
        string speedup = speedups.Count == 0
            ? "no falling frame moved"
            : string.Join(" and ", speedups.Select(r => Invariant($"{r:F4}")));
        Line(report,
            $"The ratio of the move in space to the move through the nest, for each falling frame: {speedup}. So without the nest the yard's frames would move at that many times the speed that their members' velocities say, and the bodies at rest in the yard would move away from their places in it. Through the nest they keep their places within 1e-6 m (the test TheBodiesAtRestInTheYardKeepTheirPlacesWhileTheYardDriftsTwoKilometersASecond). The merge test between the person's frame and the crates' frame also reads the separation of the two frames through the nest, from small numbers.");
        Line(report, $"");

        Line(report, $"## Expectations");
        Line(report, $"");
        Line(report, $"| Expectation | Expected | Measured | Tolerance | Holds |");
        Line(report, $"|---|---|---|---|---|");
        double touchTick = DeepYardExpectations.TouchTime * DeepYardExpectations.TicksPerSecond;
        Line(report,
            $"| First tick whose step pushes crate-b | {touchTick} (ends at {DeepYardExpectations.TouchTime} s) | {push} (ends at {(push >= 0 ? samples[push].Time : double.NaN):F4} s) | 1 tick | {Holds(push > 0 && Math.Abs(push - touchTick) <= 1)} |");
        if (push > 0)
        {
            Double3 before = DeepYardExpectations.CratesMomentum(samples[push - 1]),
                after = DeepYardExpectations.CratesMomentum(samples[^1]);
            double momentumError = Double3.Distance(after, before) / before.Length;
            Line(report,
                $"| Crates' momentum in the yard at the end, against tick {push - 1}, before the contact (kg·m/s) | {Vector(before, "R")} | {Vector(after, "R")} (relative error {momentumError:E2}) | 1e-6 relative | {Holds(momentumError < 1e-6)} |");
        }

        if (rest > 0 && thrustEnd < samples.Count)
        {
            Double3 thrust = samples[thrustEnd].InYard("person") - samples[rest].InYard("person");
            double continuousError = Double3.Distance(thrust, DeepYardExpectations.ContinuousThrustDisplacement);
            double steppedError = Double3.Distance(thrust, DeepYardExpectations.SteppedThrustDisplacement);
            Line(report,
                $"| Person's displacement in the yard over the second of thrust, ticks {rest} to {thrustEnd}, against the engine's 60 ticks, a·dt²·60·61/2 (m) | {Vector(DeepYardExpectations.SteppedThrustDisplacement, "F6")} | {Vector(thrust, "F6")}, off by {steppedError * 1000:F4} mm | 1 mm | {Holds(steppedError < 1e-3)} |");
            Line(report,
                $"| For reference, not an expectation: the same displacement against a force without a break, a·t²/2 (m) | {Vector(DeepYardExpectations.ContinuousThrustDisplacement, "F6")} | {Vector(thrust, "F6")}, off by {continuousError * 1000:F2} mm, {continuousError / -DeepYardExpectations.ContinuousThrustDisplacement.Z * 100:F2} percent | | the half tick of semi-implicit Euler |");
            Line(report,
                $"| Person's velocity in the yard after the thrust (m/s) | {Vector(DeepYardExpectations.ThrustVelocity, "F6")} | {Vector(samples[thrustEnd].VelocityInYard("person"), "R")} | 1e-5 m/s | {Holds(Double3.Distance(samples[thrustEnd].VelocityInYard("person"), DeepYardExpectations.ThrustVelocity) < 1e-5)} |");
            double seconds = samples[^1].Time - samples[thrustEnd].Time;
            Double3 drift = samples[^1].InYard("person") - samples[thrustEnd].InYard("person"),
                expectedDrift = DeepYardExpectations.ThrustVelocity * seconds;
            Line(report,
                $"| Person's drift in the yard from tick {thrustEnd} to the end, {seconds:F4} s, through its merge at tick {merge} (m) | {Vector(expectedDrift, "F6")} | {Vector(drift, "F6")}, off by {Double3.Distance(drift, expectedDrift) * 1000:F4} mm | 1 mm | {Holds(Double3.Distance(drift, expectedDrift) < 1e-3)} |");
        }
        else
        {
            Line(report,
                $"| Person's displacement over the second of thrust | {Vector(DeepYardExpectations.SteppedThrustDisplacement, "F6")} | the person never thrust | 1 mm | no |");
        }

        if (push > 0 && rest > 0)
        {
            double crateB = samples.Take(push).Max(sample =>
                Double3.Distance(sample.InYard("crate-b"), new(DeepYard.CrateSpacing / 2, 0, 0)));
            double person = samples.Take(rest + 1).Max(sample =>
                Double3.Distance(sample.InYard("person"), new(0, 0, DeepYard.PersonDistance)));
            double crateZ = samples.Max(sample =>
                Math.Max(Math.Abs(sample.InYard("crate-a").Z), Math.Abs(sample.InYard("crate-b").Z)));
            Line(report,
                $"| Farthest that crate-b gets from its place in the yard before the push, and the person before the thrust (m) | 0 | {crateB:E2} and {person:E2} | 1e-6 m | {Holds(crateB < 1e-6 && person < 1e-6)} |");
            Line(report,
                $"| Largest Z of a crate in the yard over the run (m) | 0 | {crateZ:E2} | 1e-5 m | {Holds(crateZ < 1e-5)} |");
        }

        double spaceRounding = samples.SelectMany(sample => sample.Bodies.Values)
            .Max(body => Math.Abs((body.Position - DeepYard.Center - body.FromCenter).Z));
        Line(report,
            $"| Largest difference along Z between a read in space and the read through the nest (m) | at most 1 | {spaceRounding:F6} | half the 2 m step | {Holds(spaceRounding <= 1)} |");
        int notFalling = samples.Skip(1).Count(sample =>
            sample.Bodies.Values.Any(body => body.HomeMode != NumericMode.Falling || body.Nest.IsNull));
        Line(report,
            $"| Ticks after the first in which a body is not in a falling frame under a nest | 0 | {notFalling} | none | {Holds(notFalling == 0)} |");
        int crateChanges = new[] { "crate-a", "crate-b" }.Sum(id =>
            samples.Skip(1).Zip(samples.Skip(2))
                .Count(pair => pair.First.Bodies[id].Home != pair.Second.Bodies[id].Home));
        int personChanges = samples.Skip(1).Zip(samples.Skip(2))
            .Count(pair => pair.First.Bodies["person"].Home != pair.Second.Bodies["person"].Home);
        Line(report,
            $"| Changes of home, handle and generation, after tick 1: the crates, the person | 0, 1 | {crateChanges}, {personChanges} | none | {Holds(crateChanges == 0 && personChanges == 1)} |");
        Line(report,
            $"| The person's merge into the crates' frame against the first tick in which the merge test holds (radii {DeepYardExpectations.PersonRadius} m and {DeepYardExpectations.CrateRadius:F4} m, plus {DeepYardExpectations.LookaheadTime} s of relative speed) | {mergeTest} | {merge} | 1 tick | {Holds(merge > 0 && mergeTest > 0 && Math.Abs(merge - mergeTest) <= 1)} |");
        Line(report, $"| Grid jumps | 0 | {jumps.Count} | none | {Holds(jumps.Count == 0)} |");
        Line(report,
            $"| World.Errors at the end | 0 | {run.World.Errors.Count} | none | {Holds(run.World.Errors.Count == 0)} |");
        Line(report, $"");
        if (mergeTest > 0)
            Line(report,
                $"At tick {mergeTest}, the gap between the person's bounding sphere and crate-a's, less the reach of 2 s of their relative speed, is {DeepYardExpectations.MergeGap(samples[mergeTest], "crate-a"):F4} m, and at the tick before {DeepYardExpectations.MergeGap(samples[mergeTest - 1], "crate-a"):F4} m. crate-b is farther.");
        Line(report, $"");

        Line(report, $"## Captures");
        Line(report, $"");
        Line(report,
            $"The script takes two captures from the person's eye, 1280 by 720 pixels, 60 degrees high: {Focal:F1} pixels per meter at 1 m. The middle column is x = 640 px and the middle row y = 360 px. Space is black, with stars. The light comes from space's origin, straight behind the person, so the crates' faces toward the person are lit. The whole yard drifts together, so the drift does not show in the captures.");
        Line(report, $"");
        Line(report, $"| Capture | Tick | What it must show |");
        Line(report, $"|---|---|---|");
        // At tick 30 the person is at rest, 24 m from the crates' centers.
        const double yardTime = 30 / DeepYardExpectations.TicksPerSecond;
        double yardFaces = DeepYard.PersonDistance - DeepYard.CrateHalfSize;
        double yardA = DeepYardExpectations.CrateACenterX(yardTime), yardB = DeepYard.CrateSpacing / 2;
        double yardGap = yardB - yardA - 2 * DeepYard.CrateHalfSize;
        Line(report,
            $"| yard.png | 30 | The two crates side by side, {yardFaces} m ahead: crate-a orange on the left, from x = {yardA - DeepYard.CrateHalfSize:F2} m to {yardA + DeepYard.CrateHalfSize:F2} m, and crate-b green on the right, from {yardB - DeepYard.CrateHalfSize:F2} m to {yardB + DeepYard.CrateHalfSize:F2} m. Each is about {Pixels(1, yardFaces):F0} px wide, with a dark gap of about {Pixels(yardGap, yardFaces):F0} px ({yardGap:F2} m) between them, near the middle column: crate-a from about {Pixels(-(yardA - DeepYard.CrateHalfSize), yardFaces):F0} px to {Pixels(-(yardA + DeepYard.CrateHalfSize), yardFaces):F0} px left of it, crate-b from {Pixels(yardB - DeepYard.CrateHalfSize, yardFaces):F0} px to {Pixels(yardB + DeepYard.CrateHalfSize, yardFaces):F0} px right of it. Their centers lie about {Pixels(0.75, yardFaces):F0} px below the middle row (the eye is 0.75 m above the person's center). The person's shadow, 0.6 m wide around x = 0, falls in the gap, so neither crate shows it. |");
        const int touchingTick = 1260;
        int thrustLast = thrustEnd > 0 ? thrustEnd : 662;
        double travelled = -DeepYardExpectations.SteppedThrustDisplacement.Z + -DeepYardExpectations.ThrustVelocity.Z *
            (touchingTick - thrustLast) / DeepYardExpectations.TicksPerSecond;
        double touchingFaces = DeepYard.PersonDistance - travelled - DeepYard.CrateHalfSize;
        double touchingA = DeepYardExpectations.CrateACenterX(touchingTick / DeepYardExpectations.TicksPerSecond);
        double left = touchingA - DeepYard.CrateHalfSize, right = touchingA + 3 * DeepYard.CrateHalfSize;
        // The person's render box is 0.6 m wide around x = 0, and the light comes along -Z, so its shadow covers x from -0.3 m to 0.3 m.
        double shadowEnd = Math.Min(0.3, touchingA + DeepYard.CrateHalfSize);
        Line(report,
            $"| touching.png | {touchingTick} | The two crates touching, one block 2 m wide, orange on the left and green on the right, about {touchingFaces:F2} m ahead: each crate about {Pixels(1, touchingFaces):F0} px wide, no gap between them. If the contact is inelastic, both have moved +X in the yard at {DeepYardExpectations.SharedSpeed} m/s for {touchingTick / DeepYardExpectations.TicksPerSecond - DeepYardExpectations.TouchTime:F0} s since the touch, so crate-a's center lies at x = {touchingA:F2} m and crate-b's at {touchingA + 2 * DeepYard.CrateHalfSize:F2} m, and the block spans x = {left:F2} m to {right:F2} m: from about {Pixels(left, touchingFaces):F0} px to {Pixels(right, touchingFaces):F0} px right of the middle column. Its center is about {Pixels(0.75, touchingFaces):F0} px below the middle row. The person stands between the light and the crates, so its shadow darkens crate-a's face from x = {left:F2} m to {shadowEnd:F2} m, its left {Pixels(shadowEnd - left, touchingFaces):F0} px or so, with a soft edge, over the block's full height. |");
        Line(report, $"");

        Line(report, $"## Notes");
        Line(report, $"");
        Line(report, $"Measures that need no test, and how to read them in the dumps of the headless run.");
        Line(report, $"");
        double contactTime = (DeepYard.CrateSpacing - 2 * DeepYard.CrateHalfSize - SpeculativeDistance) /
                             DeepYard.CrateSpeed;
        Line(report,
            $"- The crates' Collision event comes before their first push. Jolt starts a contact when the gap between two bodies falls below its speculative contact distance of {SpeculativeDistance} m, at about {contactTime:F2} s (tick {contactTime * DeepYardExpectations.TicksPerSecond:F0} or the next), {DeepYardExpectations.TouchTime - contactTime:F2} s before the faces touch at {DeepYard.CrateSpeed} m/s. The engine reports that contact as it starts. At a walking speed of a few m/s the lead is under a tick. Its impulse is the engine's estimate, the approach speed times the reduced mass, {DeepYard.CrateSpeed} m/s × {DeepYard.CrateMass / 2} kg = {DeepYard.CrateSpeed * DeepYard.CrateMass / 2} N·s, not the impulse that the contact applies.");
        double sideways = samples.Max(sample =>
            Math.Max(
                Math.Max(Math.Abs(sample.VelocityInYard("crate-a").Y), Math.Abs(sample.VelocityInYard("crate-a").Z)),
                Math.Max(Math.Abs(sample.VelocityInYard("crate-b").Y), Math.Abs(sample.VelocityInYard("crate-b").Z))));
        Line(report,
            $"- After the contact the crates keep a small sideways velocity in the yard from the solver, at most {sideways:E2} m/s across X, and an angular velocity of the same size. Their rotation stays exactly the identity because Jolt skips a rotation step smaller than 1e-6 rad, and such an angular velocity turns a crate by about 1e-10 rad in a tick.");
        if (thrustEnd > 0 && thrustEnd < samples.Count)
            Line(report,
                $"- The person's velocity in the yard after the thrust is {samples[thrustEnd].VelocityInYard("person").Z:R} m/s along Z rather than -1.5: each tick adds {DeepYardExpectations.Thrust / DeepYardExpectations.TicksPerSecond} m/s to a float in the person's frame. When that local velocity passes {rules.DrainSpeed} m/s, the frame takes it over (a drain), so the float starts again from zero. That is within the 1e-5 m/s of its test.");
        Line(report,
            $"- The script posts the press of W after tick 600. The press acts from the fourth tick after it, because the person sends its force to its back jet, so the person is last at rest after tick {rest} and thrusts in the 60 ticks after that.");
        Line(report,
            $"- The dumps of the headless run list only the semantic frame `space`, because no dump can tell that a numeric frame exists (docs/plans/numeric-frames.md, \"What gameplay sees\"). The numeric frames above come from the test's own reads. In the dumps, each body's position in space advances by about {DeepYard.DriftSpeed * 2:F0} m along -Z every 2 s, rounded to the 2 m step of a double, and its velocity in space holds the drift of {DeepYard.Drift.Z} m/s along Z.");
        Line(report,
            $"- The faces of the crates look paler than their colors: the light comes from straight behind the person, so the Blinn-Phong highlight covers each face that looks at the person and adds white to it. The scene keeps the light there so that the person's shadow falls on crate-a in touching.png.");
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
    private static NumericFrameSample? FrameOf(YardSample sample, FrameHandle frame) =>
        sample.NumericFrames.FirstOrDefault(f => f.Frame == frame);

    /// <summary>
    /// The position of a numeric frame relative to the yard's center at the start, in space's axes, in meters: the places
    /// of the numeric frames up the tree added, with the place of the one in space minus the center. Only small numbers add.
    /// </summary>
    private static Double3 FromCenter(YardSample sample, NumericFrameSample frame) =>
        FrameOf(sample, frame.TreeParent) is { } parent
            ? FromCenter(sample, parent) + frame.Place
            : frame.Place - DeepYard.Center;

    /// <summary>
    /// True when the frame's place in space, the sum of the places of the numeric frames up the tree, is exact in a double:
    /// at each step up, the sum minus the parent's place in space gives back the frame's place.
    /// </summary>
    private static bool ExactInSpace(YardSample sample, NumericFrameSample frame)
    {
        if (FrameOf(sample, frame.TreeParent) is not { } parent)
            return true;
        Double3 parentInSpace = InSpace(sample, parent);
        return ExactInSpace(sample, parent) && parentInSpace + frame.Place - parentInSpace == frame.Place;
    }

    /// <summary>The frame's place in space, the sum of the places of the numeric frames up the tree, in doubles.</summary>
    private static Double3 InSpace(YardSample sample, NumericFrameSample frame) =>
        FrameOf(sample, frame.TreeParent) is { } parent ? InSpace(sample, parent) + frame.Place : frame.Place;

    private static void Line(StringBuilder report, FormattableString line) => report.AppendLine(Invariant(line));

    private static string Invariant(FormattableString text) => text.ToString(CultureInfo.InvariantCulture);

    private static string Vector(Double3 v, string format) =>
        string.Create(CultureInfo.InvariantCulture,
            $"({v.X.ToString(format, CultureInfo.InvariantCulture)}, {v.Y.ToString(format, CultureInfo.InvariantCulture)}, {v.Z.ToString(format, CultureInfo.InvariantCulture)})");

    private static string Handle(FrameHandle frame) => Invariant($"#{frame.Index}v{frame.Generation}");

    private static string Home(DeepYardBody body) =>
        body.HomeMode == NumericMode.None
            ? "space (semantic)"
            : Invariant(
                $"{Lower(body.HomeMode)} {Handle(body.Home)}{(body.Nest.IsNull ? ", no nest" : $", in nest {Handle(body.Nest)}")}");

    private static string Count(YardSample sample) =>
        sample.NumericFrames.Count == 0
            ? "0"
            : Invariant(
                $"{sample.NumericFrames.Count} ({string.Join(", ", sample.NumericFrames.GroupBy(f => f.Mode).Select(g => $"{g.Count()} {Lower(g.Key)}"))})");

    private static string Lower(NumericMode mode) => mode.ToString().ToLowerInvariant();

    private static string Holds(bool holds) => holds ? "yes" : "no";

    private static double Pixels(double meters, double distance) => Focal * meters / distance;
}
