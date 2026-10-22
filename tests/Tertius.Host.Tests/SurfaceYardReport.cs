using System.Globalization;
using System.Text;
using Tertius.Demo;
using Tertius.Host.Scripting;
using static Tertius.Host.Tests.HostTesting;

namespace Tertius.Host.Tests;

/// <summary>
/// Writes the report of a <see cref="SurfaceYardRun"/> as Markdown, for a reader who did not see the run: what the scene
/// is and what it must show, the yard every 2 s, the push and the fall tick by tick, whether a numeric frame held a body,
/// each expectation with its expected and measured value, and the captures of the script with what each must show.
/// </summary>
internal static class SurfaceYardReport
{
    /// <summary>The environment variable that names the report's file.</summary>
    public const string Variable = "TERTIUS_SCENE_REPORT";

    // The table of the whole run has a row every 120 ticks, 2 s.
    private const int SampleEvery = 120;

    // The scene's camera at the start, before any camera step.
    private const string FirstCamera = "yard-camera";

    private static readonly Dictionary<string, string> CaptureMustShow = new()
    {
        ["start"] =
            "The yard from 10 m along +X and 3 m up, looking along -X and a little down. The image's right is -Z. The gray floor "
            + "fills the image from the bottom up to its far edge, with black space above it. The stack stands right of the middle: two "
            + "brown crates, and the orange crate on top, which sticks out 0.2 m toward the left, the person's side. The blue person "
            + "stands far to the left, 12.25 m from the stack, upright on the floor. Everything is at rest.",
        ["stack-ahead"] =
            "The person's own view, from its eye 1.65 m above the floor, after the 10 m walk, 2.25 m from the stack, "
            + "looking level along -Z at it. The stack stands in the middle of the image, below its center: the orange top crate on "
            + "the two brown ones, its front face nearer than theirs, and its top just below eye height. The floor's far edge, 27 m "
            + "away, is just below the image's center line.",
        ["falling"] =
            "The stack from 4 m along +X, 1.5 m up, looking along -X and a little down. The image's right is -Z, behind the "
            + "stack. The two brown crates stand just left of the middle, where they stood. The orange crate is off the stack, to its "
            + "right, in the air (the row gives its position; the floor's top is at y = 6371000). The person stands at the stack's left "
            + "face.",
        ["end"] =
            "The same view 60 s after the crates came to rest. The two brown crates stand as in 'falling'. The orange crate lies "
            + "on the floor to the right of the stack, at rest. The person stands at the stack's left face.",
        ["end-yard"] =
            "The view of 'start' at the end: the stack of two brown crates, the orange crate on the floor beyond it (to its "
            + "right), and the person against the stack's left face, much nearer the middle of the image than at the start."
    };

    /// <summary>sqrt(2h/g) for h = 1 m, the stack's height, and g = μ/R²: the time of a fall from the stack's top to the floor from rest.</summary>
    private static double RestFallTime => Math.Sqrt(2 * YardMeasurements.DropHeight / YardMeasurements.SurfaceGravity);

    public static void Write(SurfaceYardRun run, string path)
    {
        YardMeasurements m = run.Measured;
        var text = new StringBuilder();
        Line(text, "# Surface yard: scene report");
        Line(text);
        Line(text,
            "Written by `tests/Tertius.Host.Tests/SurfaceYardTests.cs` from one headless run of `scripts/surface-yard.json` on the scene "
            + "`surface-yard` (`src/Tertius.Demo/DemoScenes.SurfaceYard.cs`), in a world on the real Jolt backend in double precision, "
            + $"at 60 ticks a second (one tick is {m.TickSeconds:F9} s). The run has {m.EndTick} ticks, {m.EndTime:F3} s.");
        Line(text);
        Scene(text, m);
        Samples(text, m);
        Fall(text, m);
        NumericFrames(text, m);
        Expectations(text, run, m);
        NotAsserted(text, m);
        Captures(text, m);
        Errors(text, run);
        string? folder = Path.GetDirectoryName(Path.GetFullPath(path));
        if (folder is not null)
            Directory.CreateDirectory(folder);
        File.WriteAllText(path, text.ToString());
    }

    private static void Line(StringBuilder text, string line = "") => text.Append(line).Append('\n');

    private static string F(double value, int digits = 4) => value.ToString("F" + digits, CultureInfo.InvariantCulture);

    private static string G(double value) => value.ToString("G6", CultureInfo.InvariantCulture);

    private static string V(Double3 v, int digits = 4) => $"({F(v.X, digits)}, {F(v.Y, digits)}, {F(v.Z, digits)})";

    private static string Yes(bool value) => value ? "yes" : "no";

    private static string Tick(long? tick) => tick?.ToString(CultureInfo.InvariantCulture) ?? "never";

    private static void Scene(StringBuilder text, YardMeasurements m)
    {
        Line(text, "## The scene");
        Line(text);
        Line(text,
            "- The root frame `earth-surface` has its origin at Earth's center and Earth's point gravity, μ = 3.986004418e14 m³/s². "
            + "It does not turn. Positions below are in its axes, in meters: the yard lies at the north pole, so +Y is up, and the floor's "
            + $"top is at y = R = {F(YardMeasurements.FloorTop, 1)} m, 6,371 km from the origin. The surface gravity there is μ/R² = "
            + $"{F(YardMeasurements.SurfaceGravity, 6)} m/s².");
        Line(text,
            "- `floor`: a static gray slab, 50 by 50 m and 1 m thick, top at y = R. The planet has no ground (ticket ter-7u6t).");
        Line(text,
            "- A stack of three crates, cubes 0.5 m on a side, on the Y axis: `bottom-crate` and `middle-crate`, brown, 100 kg each, "
            + "and `top-crate`, orange, 5 kg, friction 0.1, which sticks out 0.2 m toward +Z. The top crate's bottom is 1 m above the floor. "
            + "The lower crates are heavy so that the push's friction moves the middle crate well under 1 mm. With 40 kg crates, an earlier "
            + "backend woke the stack every 0.5 s while the person stood against it; the physics test "
            + "`WorldCharacterStandingTests.ACharacterThatStandsAgainstASleepingStackDoesNotWakeIt` keeps that case, with 40 kg crates.");
        Line(text,
            "- `person`: a capsule character 1.8 m tall, center 0.9 m above the floor when it stands, starting 12.25 m along +Z from "
            + "the stack and facing it (-Z). It walks at 3 m/s with W and jumps at 4 m/s with Space.");
        Line(text,
            "- The script: 2 s of rest; W for 200 ticks (a 10 m walk); 30 ticks still; one jump; 90 ticks; W for 40 ticks, in which "
            + "the person walks about 1.5 m, reaches the top crate's face, pushes the crate off the far side of the stack, and stops "
            + "against the heavy crates; then rest until 60 s after the 2 s of rest.");
        Line(text,
            "- Every body is slow and 6,371 km from its frame's origin, nearer than the 8,389 km from which a slow group gets a "
            + "resting numeric frame, so the Jolt system of `earth-surface` itself holds every body, 6,371 km from its origin, in double "
            + "precision. The precision measurements (tests/Tertius.Physics.Tests/Precision) found no error from the distance up to 9,000 km.");
        Line(text);
        Line(text, "What the run must show:");
        Line(text);
        Line(text,
            "1. The two crates that stay on the stack fall asleep and move less than 1 mm over the 60 s from the walk's start to the end.");
        Line(text, "2. The person stands while it walks.");
        Line(text,
            "3. The pushed crate falls freely with g = μ/r² the surface gravity: in free flight its velocity changes by g times each step, "
            + "and from the tick at which it has left the stack, its lowest corner reaches the floor within one tick of the time of a free fall "
            + $"from that state. For a crate that left at rest and without spin, that time is sqrt(2h/g); for h = 1 m it is {F(RestFallTime, 4)} s. "
            + "The crate tips over the stack's edge and leaves it spinning, so the time from the edge to the floor is not that of a fall of 1 m from rest.");
        Line(text, "4. No body jitters: over the last 40 s, no tick moves any body by 0.1 mm or more.");
        Line(text,
            "5. No numeric frame holds a body at any tick: the Jolt system of `earth-surface` holds them all, and they rest without jitter there. World.Errors stays empty.");
        Line(text,
            "6. The person and the top crate hear each other (a Collision event) when the person pushes the crate.");
        Line(text);
    }

    private static void Samples(StringBuilder text, YardMeasurements m)
    {
        Line(text, "## The yard every 2 s");
        Line(text);
        Line(text,
            "Positions and velocities are in `earth-surface`. Height is y - R, the height of the body's center above the floor's top.");
        Line(text);
        Line(text, "| t (s) | tick | numeric frames | World.Errors | person standing |");
        Line(text, "|---:|---:|---:|---:|:---:|");
        foreach (TickSample tick in Sampled(m))
            Line(text,
                $"| {F(tick.Time, 3)} | {tick.Tick} | {tick.NumericFrames} | {tick.Errors} | {Yes(tick.PersonStanding)} |");
        Line(text);
        Line(text, "| t (s) | body | position x, y, z (m) | height (m) | velocity (m/s) | asleep |");
        Line(text, "|---:|---|---|---:|---|:---:|");
        foreach (TickSample tick in Sampled(m))
            for (int i = 0; i < YardRecord.Bodies.Length; i++)
            {
                BodySample body = tick.Bodies[i];
                Line(text,
                    $"| {F(tick.Time, 3)} | {YardRecord.Bodies[i]} | {V(body.Pose.Position)} | {F(body.Pose.Position.Y - YardMeasurements.FloorTop)} "
                    + $"| {V(body.Velocity)} | {Yes(YardMeasurements.Asleep(body))} |");
            }

        Line(text);
    }

    private static IEnumerable<TickSample> Sampled(YardMeasurements m)
    {
        for (int tick = 0; tick < m.Ticks.Count; tick += SampleEvery)
            yield return m.Ticks[tick];
        if ((m.Ticks.Count - 1) % SampleEvery != 0)
            yield return m.Ticks[^1];
    }

    private static void Fall(StringBuilder text, YardMeasurements m)
    {
        CrateFall fall = m.Fall;
        Line(text, "## The push and the fall, tick by tick");
        Line(text);
        Line(text,
            $"The walk into the crate starts at tick {m.LandedTick}. The crate first moves (more than 1 mm) at tick {Tick(fall.PushTick)}, "
            + $"its center passes the far edge of the stack (z < -0.25 m) at tick {Tick(fall.TipTick)}, "
            + $"every corner of it is past the stack's far face, so that it has left the stack, at tick {Tick(fall.LeaveTick)}, "
            + $"its lowest corner reaches the floor's top (within 1 mm) at tick {Tick(fall.FloorTick)}, "
            + $"and the floor first changes its velocity by more than half of free fall at tick {Tick(fall.LandingTick)}. "
            + "Free flight means that the crate was more than 5 cm past the stack's far face and above the floor at both ends of the tick's step. "
            + "Acceleration is the change of velocity over the step divided by the step; expected is Earth's field at the crate's center, "
            + "μ/r² toward Earth's center, which is -Y here. Lowest corner is the height of the crate's lowest corner above the floor's top, "
            + "and nearest z is the largest z of its corners, the corner nearest the stack, whose far face is at z = -0.25 m.");
        Line(text);
        Line(text,
            "| tick | t (s) | top crate position (m) | height (m) | lowest corner (m) | nearest z (m) | velocity (m/s) | spin (rad/s) | free flight | acceleration (m/s²) | expected (m/s²) | person z (m) | person standing |");
        Line(text, "|---:|---:|---|---:|---:|---:|---|---:|:---:|---|---|---:|:---:|");
        long from = Math.Max(m.LandedTick + 1, (fall.PushTick ?? m.LandedTick) - 3);
        long to = Math.Min(m.EndTick, (fall.LandingTick ?? m.PushedTick + 40) + 5);
        Dictionary<long, FreeFlightTick> free = fall.FreeFlightTicks.ToDictionary(step => step.Tick);
        for (long tick = from; tick <= to; tick++)
        {
            TickSample sample = m.Ticks[(int)tick];
            BodySample crate = sample.Bodies[YardRecord.TopCrate], person = sample.Bodies[YardRecord.Person];
            BodySample before = m.Ticks[(int)tick - 1].Bodies[YardRecord.TopCrate];
            Double3 acceleration = (crate.Velocity - before.Velocity) / m.TickSeconds;
            (double nearestZ, double lowest) = YardMeasurements.CornerExtremes(crate.Pose);
            Line(text,
                $"| {tick} | {F(sample.Time, 3)} | {V(crate.Pose.Position)} | {F(crate.Pose.Position.Y - YardMeasurements.FloorTop)} "
                + $"| {F(lowest - YardMeasurements.FloorTop)} | {F(nearestZ)} | {V(crate.Velocity)} "
                + $"| {F(crate.AngularVelocity.Length(), 3)} | {Yes(free.ContainsKey(tick))} | {V(acceleration, 5)} | {V(YardMeasurements.Field(before.Pose.Position), 5)} "
                + $"| {F(person.Pose.Position.Z, 3)} | {Yes(sample.PersonStanding)} |");
        }

        Line(text);
    }

    private static void NumericFrames(StringBuilder text, YardMeasurements m)
    {
        Line(text, "## Numeric frames");
        Line(text);
        Line(text,
            $"The most numeric frames that the frame tree held after a tick, from the scene as built (tick 0) to the end: {m.MostNumericFrames}. "
            + "The ticks after which a body's physics home was not `earth-surface` itself:");
        Line(text);
        Line(text, "| body | ticks in another home | first such ticks |");
        Line(text, "|---|---:|---|");
        foreach (BodyHomes homes in m.Homes)
            Line(text,
                $"| {homes.Id} | {homes.TicksNotInEarthSurface} | {(homes.TicksNotInEarthSurface == 0 ? "none" : string.Join(", ", homes.FirstTicksNotInEarthSurface))} |");
        Line(text);
    }

    private static void Expectations(StringBuilder text, SurfaceYardRun run, YardMeasurements m)
    {
        Line(text, "## Expectations");
        Line(text);
        Line(text,
            "Every expected value comes from the scene's geometry and from Earth's gravity, never from the run.");
        Line(text);
        Line(text, "| test | expectation | expected | measured | holds |");
        Line(text, "|---|---|---|---|:---:|");

        void Row(string test, string what, string expected, string measured, bool holds) =>
            Line(text, $"| {test} | {what} | {expected} | {measured} | {Yes(holds)} |");

        const string stack = "TheTwoCratesLeftOnTheStackFallAsleepAndMoveLessThanAMillimeterInSixtySeconds";
        foreach (StackedCrate crate in m.Stacked)
        {
            Row(stack, $"{crate.Id} asleep when the walk starts (t = {F(m.SettledTime, 2)} s)", "yes",
                Yes(crate.AsleepWhenTheWalkStarts), crate.AsleepWhenTheWalkStarts);
            Row(stack, $"{crate.Id} asleep at the end", "yes", Yes(crate.AsleepAtTheEnd), crate.AsleepAtTheEnd);
            Row(stack, $"{crate.Id}: largest distance from where it rested at {F(m.SettledTime, 2)} s, over 60 s",
                "0, below 1 mm",
                $"{G(crate.LargestMove * 1e3)} mm", crate.LargestMove < YardMeasurements.StackDriftLimit);
            Row(stack, $"{crate.Id}: distance at the end from where the scene stacked it",
                "below 2 cm (contacts sink up to 5 mm each)",
                $"{G(crate.OffsetFromWhereStacked * 1e3)} mm",
                crate.OffsetFromWhereStacked < YardMeasurements.StackSinkLimit);
            Row(stack,
                $"{crate.Id}: times it woke after the dump 'pushed' (tick {m.PushedTick}), while the person stands against the stack",
                "0",
                crate.WakesAfterThePush.ToString(CultureInfo.InvariantCulture), crate.WakesAfterThePush == 0);
        }

        const string walk = "ThePersonStandsWhileItWalks";
        PersonWalk w = m.Walk;
        Row(walk,
            $"ticks of the walks ({m.SettledTick} to {m.WalkedTick} and {m.LandedTick} to {m.PushedTick}) in which the person does not stand",
            "none",
            w.TicksNotStanding.Count == 0 ? "none" : string.Join(", ", w.TicksNotStanding),
            w.TicksNotStanding.Count == 0);
        Row(walk, "largest distance of the person's center from 0.9 m above the floor", "below 5 cm",
            $"{G(w.LargestHeightError * 100)} cm",
            w.LargestHeightError < YardMeasurements.StandingHeightTolerance);
        Row(walk, "largest vertical speed of the person, from the change of its height over each tick",
            "below 0.05 m/s", $"{G(w.LargestVerticalSpeed)} m/s",
            w.LargestVerticalSpeed < YardMeasurements.StandingVerticalSpeedLimit);
        Row(walk, "largest vertical part of the velocity that the world reports for the person", "0, below 0.05 m/s",
            $"{G(w.LargestReportedVerticalSpeed)} m/s",
            w.LargestReportedVerticalSpeed < YardMeasurements.StandingVerticalSpeedLimit);
        Row(walk, "distance of the first walk: 200 ticks at 3 m/s",
            $"{F(YardMeasurements.WalkDistance, 3)} m ± {F(YardMeasurements.WalkDistanceTolerance, 3)} m",
            $"{F(w.FirstWalkDistance, 4)} m",
            Math.Abs(w.FirstWalkDistance - YardMeasurements.WalkDistance) <= YardMeasurements.WalkDistanceTolerance);

        const string fallTest = "ThePushedCrateFallsFromTheStackToTheFloorInTheTimeOfAFreeFallWithTheSurfaceGravity";
        CrateFall fall = m.Fall;
        double worstShare = fall.FreeFlightTicks.Count == 0
            ? double.NaN
            : fall.FreeFlightTicks.Max(step => step.AccelerationError / step.ExpectedGravity);
        double worstSpin = fall.FreeFlightTicks.Count == 0
            ? double.NaN
            : fall.FreeFlightTicks.Max(step => step.AngularVelocityChange);
        Row(fallTest, "steps of free flight", $"at least {YardMeasurements.LeastFreeFlightTicks}",
            fall.FreeFlightTicks.Count.ToString(CultureInfo.InvariantCulture),
            fall.FreeFlightTicks.Count >= YardMeasurements.LeastFreeFlightTicks);
        Row(fallTest, "largest error of the crate's acceleration in free flight, as a share of μ/r²", "0, below 0.001",
            G(worstShare),
            worstShare < YardMeasurements.FreeFallRelativeTolerance);
        Row(fallTest, "largest change of the crate's spin in one step of free flight", "0, below 1e-4 rad/s",
            $"{G(worstSpin)} rad/s", worstSpin < YardMeasurements.SpinChangeLimit);
        string leaving = fall.Leaving is { } state
            ? $"at height {F(state.Pose.Position.Y - YardMeasurements.FloorTop)} m, lowest corner {F(YardMeasurements.CornerExtremes(state.Pose).LowestY - YardMeasurements.FloorTop)} m, "
              + $"vertical speed {F(state.Velocity.Y)} m/s, spin {F(state.AngularVelocity.Length(), 3)} rad/s"
            : "never";
        Row(fallTest,
            $"time from tick {Tick(fall.LeaveTick)}, when the crate has left the stack ({leaving}), until its lowest corner reaches the floor",
            fall.PredictedFallTime is double predicted
                ? $"a free fall from that state with g = μ/R² and the spin kept: {F(predicted, 5)} s, in which its center falls {F(fall.PredictedCenterDrop ?? double.NaN)} m; ± one tick ({F(m.TickSeconds, 5)} s)"
                : "a free fall from that state, which does not reach the floor",
            fall.MeasuredFallTime is double measured ? $"{F(measured, 5)} s" : "never",
            fall.MeasuredFallTime is double a && fall.PredictedFallTime is double b &&
            Math.Abs(a - b) <= m.TickSeconds);

        const string push = "ThePersonAndTheTopCrateHearEachOtherWhenThePersonPushesIt";
        foreach (string hearer in new[] { "person", "top-crate" })
        {
            PushCollision? first = m.PushCollisions.FirstOrDefault(collision => collision.To == hearer);
            Row(push, $"the first Collision between the person and the top crate that the {hearer} hears",
                $"at tick {Tick(fall.PushTick)} ± 1, the crate's first move, "
                + $"with an impulse above 0 and at most the head-on {F(YardMeasurements.HeadOnPushImpulse, 3)} N·s (3 m/s times the reduced mass)",
                first is null ? "none" : $"tick {first.Tick}, impulse {F(first.Impulse, 3)} N·s",
                first is not null && fall.PushTick is long pushTick && Math.Abs(first.Tick - pushTick) <= 1 &&
                first.Impulse > 0
                && first.Impulse <= YardMeasurements.HeadOnPushImpulse * 1.001);
        }

        const string jitter = "NoBodyJittersOnceTheYardIsAtRest";
        foreach (RestingBody body in m.AtRest)
        {
            Row(jitter, $"{body.Id}: largest move in one tick, from {F(m.RestFromTime, 2)} s to the end",
                "0, below 0.1 mm", $"{G(body.LargestStep * 1e3)} mm",
                body.LargestStep < YardMeasurements.JitterStepLimit);
            Row(jitter, $"{body.Id}: largest distance from where it was at {F(m.RestFromTime, 2)} s", "0, below 1 mm",
                $"{G(body.LargestMove * 1e3)} mm",
                body.LargestMove < YardMeasurements.RestDriftLimit);
            if (body.Id != "person")
                Row(jitter, $"{body.Id} asleep at the end", "yes", Yes(body.AsleepAtTheEnd), body.AsleepAtTheEnd);
        }

        const string homes = "NoNumericFrameHoldsTheYardsBodiesAndTheyRestInTheJoltSystemOfEarthSurfaceItself";
        Row(homes, "most numeric frames in the frame tree after a tick", "0",
            m.MostNumericFrames.ToString(CultureInfo.InvariantCulture), m.MostNumericFrames == 0);
        foreach (BodyHomes body in m.Homes)
            Row(homes, $"{body.Id}: ticks from 0 on with a physics home other than earth-surface itself", "0",
                body.TicksNotInEarthSurface == 0
                    ? "0"
                    : $"{body.TicksNotInEarthSurface} (first: {string.Join(", ", body.FirstTicksNotInEarthSurface)})",
                body.TicksNotInEarthSurface == 0);
        foreach (RestingBody body in m.AtRest)
            Row(homes,
                $"{body.Id}: largest move in one tick, from {F(m.RestFromTime, 2)} s to the end, 6,371 km from the origin of its Jolt system",
                "0, below 0.1 mm", $"{G(body.LargestStep * 1e3)} mm",
                body.LargestStep < YardMeasurements.JitterStepLimit);

        const string errors = "TheRunRecordsNoErrors";
        Row(errors, "World.Errors at the end", "0", run.World.Errors.Count.ToString(CultureInfo.InvariantCulture),
            run.World.Errors.Count == 0);
        Row(errors, "Vulkan validation messages", "0",
            run.ValidationMessages.Count.ToString(CultureInfo.InvariantCulture), run.ValidationMessages.Count == 0);
        Line(text);
    }

    private static void NotAsserted(StringBuilder text, YardMeasurements m)
    {
        CrateFall fall = m.Fall;
        Line(text, "## Measured, not asserted");
        Line(text);
        if (fall.TipTick is long tip && fall.FloorTick is long floor)
            Line(text,
                $"- From the tick at which the crate's center passed the stack's far edge ({tip}) to the tick at which its lowest corner reached the floor ({floor}): "
                + $"{F((floor - tip) * m.TickSeconds, 4)} s, against sqrt(2h/g) = {F(RestFallTime, 4)} s for a fall of h = 1 m from rest. No test asserts this time: a crate "
                + "pushed off an edge tips over the edge while the edge still holds part of its weight, then leaves it spinning and lands on a corner, "
                + "so its fall from the edge to the floor is not a free fall of 1 m from rest. The test times the free fall from the tick at which "
                + "the crate has left the stack instead.");
        else
            Line(text,
                "- The crate did not both tip over the stack's edge and reach the floor, so the run has no time of fall.");
        if (fall.PushTick is long pushed && fall.LeaveTick is long left)
        {
            IEnumerable<BodySample> onStack = Enumerable.Range((int)pushed, (int)(left - pushed))
                .Select(tick => m.Ticks[tick].Bodies[YardRecord.TopCrate]);
            Line(text,
                $"- From the push (tick {pushed}) until the crate left the stack (tick {left}), its largest upward speed was "
                + $"{F(onStack.Max(body => body.Velocity.Y), 4)} m/s and its largest spin {F(onStack.Max(body => body.AngularVelocity.Length()), 3)} rad/s. "
                + "The person's capsule touches the crate's face above the crate's center, so the push also tips the crate a little on the stack.");
        }

        if (fall.DeepestBelowFloor is double deepest)
            Line(text,
                $"- In the 60 ticks from the tick at which its lowest corner reached the floor, a corner of the crate went at most {F(deepest * 100, 2)} cm below "
                + "the floor's top. The crate moves about 7 cm a tick when it lands, more than Jolt's speculative contact distance of 2 cm, so Jolt can find "
                + "the contact only after the corner sank in; it then pushes the crate back out. Jolt's continuous collision starts only at a move of "
                + "about 0.19 m a tick for this crate.");
        foreach (PushCollision collision in m.PushCollisions)
            Line(text,
                $"- Collision between the person and the top crate, heard by the {collision.To}, at tick {collision.Tick}, impulse {F(collision.Impulse, 3)} N·s.");
        foreach (StackedCrate crate in m.Stacked)
            Line(text, $"- {crate.Id} fell asleep (s) or woke (w) at the ticks: "
                       + (crate.SleepChanges.Count == 0
                           ? "never"
                           : string.Join(", ",
                               crate.SleepChanges.Select(change => $"{change.Tick} {(change.Asleep ? "s" : "w")}"))) +
                       ".");
        double gravity = YardMeasurements.SurfaceGravity;
        double airTime = 2 * PersonControl.JumpSpeed / gravity,
            apex = PersonControl.JumpSpeed * PersonControl.JumpSpeed / (2 * gravity);
        if (m.Jump is { TakeOffTick: long off, LandingTick: long down, Apex: double measuredApex })
            Line(text,
                $"- The jump: physics stopped standing the person at tick {off} and stood it again at tick {down}: {F((down - off) * m.TickSeconds, 4)} s in the air, "
                + $"against 2v/g = {F(airTime, 4)} s for v = 4 m/s. Its center rose {F(measuredApex, 4)} m, against v²/2g = {F(apex, 4)} m.");
        else
            Line(text, "- The person did not both leave the floor and land again in the jump's ticks.");
        Line(text);
    }

    private static void Captures(StringBuilder text, YardMeasurements m)
    {
        Line(text, "## Captures");
        Line(text);
        Line(text,
            "The script takes these captures, 1280 by 720, into the `--out` folder of a headless run, as `<name>.png`. Each row gives the state "
            + "at the capture's tick, so that the image can be checked against it.");
        Line(text);
        Line(text, "| capture | tick | t (s) | camera | top crate position (m) | person position (m) | must show |");
        Line(text, "|---|---:|---:|---|---|---|---|");
        Script script = Script.Parse(File.ReadAllText(Path.Combine(RepositoryRoot(), "scripts", "surface-yard.json")));
        long tick = 0;
        string camera = FirstCamera;
        foreach (ScriptStep step in script.Steps)
            switch (step)
            {
                case RunStep run:
                    tick += run.Ticks;
                    break;
                case CameraStep cameraStep:
                    camera = cameraStep.Entity;
                    break;
                case CaptureStep capture:
                    TickSample sample = m.Ticks[(int)tick];
                    Line(text,
                        $"| {capture.Name} | {tick} | {F(sample.Time, 3)} | {camera} | {V(sample.Bodies[YardRecord.TopCrate].Pose.Position, 3)} "
                        + $"| {V(sample.Bodies[YardRecord.Person].Pose.Position, 3)} | {CaptureMustShow.GetValueOrDefault(capture.Name, "(no description)")} |");
                    break;
                default:
                    // The other steps move neither the tick nor the camera.
                    break;
            }

        Line(text);
    }

    private static void Errors(StringBuilder text, SurfaceYardRun run)
    {
        Line(text, "## World.Errors");
        Line(text);
        if (run.World.Errors.Count == 0)
            Line(text, "None.");
        foreach (string error in run.World.Errors.Take(20))
            Line(text, $"- {error}");
        if (run.World.Errors.Count > 20)
            Line(text, $"- ... and {run.World.Errors.Count - 20} more.");
        Line(text);
    }
}
