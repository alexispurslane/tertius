using System.Globalization;
using System.Numerics;
using System.Text;
using Tertius.Demo;
using Tertius.Host.Scripting;
using static Tertius.Host.Tests.HostTesting;

namespace Tertius.Host.Tests;

/// <summary>
/// Writes the report of a <see cref="SurfaceYardRun"/> as Markdown, for a reader who did not see the run: what the scene
/// is and what it must show, the yard every 2 s, the push and the fall tick by tick, the physics homes of the bodies,
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
        ["start"] = "The yard from 10 m along +X and 3 m up, looking along -X and a little down. The image's right is -Z. The gray floor "
            + "fills the image from the bottom up to its far edge, with black space above it. The stack stands right of the middle: two "
            + "brown crates, and the orange crate on top, which sticks out 0.2 m toward the left, the person's side. The blue person "
            + "stands far to the left, 12.25 m from the stack, upright on the floor. Everything is at rest.",
        ["stack-ahead"] = "The person's own view, from its eye 1.65 m above the floor, after the 10 m walk, 2.25 m from the stack, "
            + "looking level along -Z at it. The stack stands in the middle of the image, below its center: the orange top crate on "
            + "the two brown ones, its front face nearer than theirs, and its top just below eye height. The floor's far edge, 27 m "
            + "away, is just below the image's center line.",
        ["falling"] = "The stack from 4 m along +X, 1.5 m up, looking along -X and a little down. The image's right is -Z, behind the "
            + "stack. The two brown crates stand just left of the middle, where they stood. The orange crate is off the stack, to its "
            + "right, in the air (the row gives its position; the floor's top is at y = 6371000). The person stands at the stack's left "
            + "face.",
        ["end"] = "The same view 60 s after the crates came to rest. The two brown crates stand as in 'falling'. The orange crate lies "
            + "on the floor to the right of the stack, at rest. The person stands at the stack's left face.",
        ["end-yard"] = "The view of 'start' at the end: the stack of two brown crates, the orange crate on the floor beyond it (to its "
            + "right), and the person against the stack's left face, much nearer the middle of the image than at the start.",
    };

    public static void Write(SurfaceYardRun run, string path)
    {
        YardMeasurements m = run.Measured;
        FrameHandle surface = run.World.GetOwnedFrame(run.Scene["earth-surface"]);
        Dictionary<FrameHandle, string> labels = Labels(m, surface);
        var text = new StringBuilder();
        Line(text, "# Surface yard: scene report");
        Line(text);
        Line(text, "Written by `tests/Tertius.Host.Tests/SurfaceYardTests.cs` from one headless run of `scripts/surface-yard.json` on the scene "
            + "`surface-yard` (`src/Tertius.Demo/DemoScenes.SurfaceYard.cs`), in a world on the real Jolt backend in double precision, "
            + $"at 60 ticks a second (one tick is {m.TickSeconds:F9} s). The run has {m.EndTick} ticks, {m.EndTime:F3} s.");
        Line(text);
        Scene(text, m);
        Samples(text, m, labels);
        Fall(text, m, labels);
        Homes(text, m, labels);
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

    /// <summary>A short name for each physics home: earth-surface, or N1, N2 and so on in the order that the run first used them.</summary>
    private static Dictionary<FrameHandle, string> Labels(YardMeasurements m, FrameHandle surface)
    {
        var labels = new Dictionary<FrameHandle, string> { [surface] = "earth-surface" };
        foreach (TickSample tick in m.Ticks)
            foreach (BodySample body in tick.Bodies)
                if (!labels.ContainsKey(body.Home))
                    labels[body.Home] = body.Home.IsNull ? "none" : $"N{labels.Count}";
        return labels;
    }

    private static string Home(BodySample body, Dictionary<FrameHandle, string> labels) =>
        body.HomeMode == NumericMode.None ? $"{labels[body.Home]} (semantic)" : $"{labels[body.Home]} ({body.HomeMode.ToString().ToLowerInvariant()})";

    private static void Scene(StringBuilder text, YardMeasurements m)
    {
        Line(text, "## The scene");
        Line(text);
        Line(text, "- The root frame `earth-surface` has its origin at Earth's center and Earth's point gravity, μ = 3.986004418e14 m³/s². "
            + "It does not turn. Positions below are in its axes, in meters: the yard lies at the north pole, so +Y is up, and the floor's "
            + $"top is at y = R = {F(YardMeasurements.FloorTop, 1)} m, 6,371 km from the origin. The surface gravity there is μ/R² = "
            + $"{F(YardMeasurements.SurfaceGravity, 6)} m/s².");
        Line(text, "- `floor`: a static gray slab, 50 by 50 m and 1 m thick, top at y = R. The planet has no ground (ticket ter-7u6t).");
        Line(text, "- A stack of three crates, cubes 0.5 m on a side, on the Y axis: `bottom-crate` and `middle-crate`, brown, 100 kg each, "
            + "and `top-crate`, orange, 5 kg, friction 0.1, which sticks out 0.2 m toward +Z. The top crate's bottom is 1 m above the floor.");
        Line(text, "- `person`: a capsule character 1.8 m tall, center 0.9 m above the floor when it stands, starting 12.25 m along +Z from "
            + "the stack and facing it (-Z). It walks at 3 m/s with W and jumps at 4 m/s with Space.");
        Line(text, "- The script: 2 s of rest; W for 200 ticks (a 10 m walk); 30 ticks still; one jump; 90 ticks; W for 40 ticks, in which "
            + "the person walks about 1.5 m, reaches the top crate's face, pushes the crate off the far side of the stack, and stops "
            + "against the heavy crates; then rest until 60 s after the 2 s of rest.");
        Line(text, "- Every body is slow and 6,371 km from its frame's origin, so only resting numeric frames keep it near a Jolt origin. "
            + "Physics home below means the frame whose Jolt system holds the body: `earth-surface` (semantic), or a numeric frame "
            + "`N1`, `N2`... with its mode.");
        Line(text);
        Line(text, "What the run must show:");
        Line(text);
        Line(text, "1. The two crates that stay on the stack fall asleep and move less than 1 mm over the 60 s from the walk's start to the end.");
        Line(text, "2. The person stands while it walks.");
        Line(text, $"3. The pushed crate falls 1 m as a free fall with g = μ/r²: in free flight its velocity changes by g times each step, "
            + $"so a fall of h = 1 m from rest takes sqrt(2h/g) = {F(m.Fall.ExpectedFallTime, 4)} s, within one tick.");
        Line(text, "4. No body jitters: over the last 40 s, no tick moves any body by 0.1 mm or more.");
        Line(text, "5. A resting numeric frame holds every body after every tick, and World.Errors stays empty.");
        Line(text);
    }

    private static void Samples(StringBuilder text, YardMeasurements m, Dictionary<FrameHandle, string> labels)
    {
        Line(text, "## The yard every 2 s");
        Line(text);
        Line(text, "Positions and velocities are in `earth-surface`. Height is y - R, the height of the body's center above the floor's top.");
        Line(text);
        Line(text, "| t (s) | tick | numeric frames | World.Errors | person standing |");
        Line(text, "|---:|---:|---:|---:|:---:|");
        foreach (TickSample tick in Sampled(m))
            Line(text, $"| {F(tick.Time, 3)} | {tick.Tick} | {tick.NumericFrames} | {tick.Errors} | {Yes(tick.PersonStanding)} |");
        Line(text);
        Line(text, "| t (s) | body | position x, y, z (m) | height (m) | velocity (m/s) | asleep | physics home |");
        Line(text, "|---:|---|---|---:|---|:---:|---|");
        foreach (TickSample tick in Sampled(m))
            for (int i = 0; i < YardRecord.Bodies.Length; i++)
            {
                BodySample body = tick.Bodies[i];
                Line(text, $"| {F(tick.Time, 3)} | {YardRecord.Bodies[i]} | {V(body.Pose.Position)} | {F(body.Pose.Position.Y - YardMeasurements.FloorTop)} "
                    + $"| {V(body.Velocity)} | {Yes(YardMeasurements.Asleep(body))} | {Home(body, labels)} |");
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

    private static void Fall(StringBuilder text, YardMeasurements m, Dictionary<FrameHandle, string> labels)
    {
        CrateFall fall = m.Fall;
        Line(text, "## The push and the fall, tick by tick");
        Line(text);
        Line(text, $"The walk into the crate starts at tick {m.LandedTick}. The crate first moves (more than 1 mm) at tick {fall.PushTick?.ToString(CultureInfo.InvariantCulture) ?? "never"}, "
            + $"its center passes the far edge of the stack (z < -0.25 m) at tick {fall.TipTick?.ToString(CultureInfo.InvariantCulture) ?? "never"}, "
            + $"and the floor first stops it at tick {fall.LandingTick?.ToString(CultureInfo.InvariantCulture) ?? "never"}. "
            + "Free flight means that the crate was more than 5 cm past the stack's far face and above the floor at both ends of the tick's step. "
            + "Acceleration is the change of velocity over the step divided by the step; expected is Earth's field at the crate's center, "
            + "μ/r² toward Earth's center, which is -Y here.");
        Line(text);
        Line(text, "| tick | t (s) | top crate position (m) | height (m) | velocity (m/s) | spin (rad/s) | free flight | acceleration (m/s²) | expected (m/s²) | person z (m) | person standing | crate's home |");
        Line(text, "|---:|---:|---|---:|---|---:|:---:|---|---|---:|:---:|---|");
        long from = Math.Max(m.LandedTick + 1, (fall.PushTick ?? m.LandedTick) - 3);
        long to = Math.Min(m.EndTick, (fall.LandingTick ?? m.PushedTick + 40) + 5);
        var free = fall.FreeFlightTicks.ToDictionary(step => step.Tick);
        for (long tick = from; tick <= to; tick++)
        {
            TickSample sample = m.Ticks[(int)tick];
            BodySample crate = sample.Bodies[YardRecord.TopCrate], person = sample.Bodies[YardRecord.Person];
            BodySample before = m.Ticks[(int)tick - 1].Bodies[YardRecord.TopCrate];
            Double3 acceleration = (crate.Velocity - before.Velocity) / m.TickSeconds;
            Line(text, $"| {tick} | {F(sample.Time, 3)} | {V(crate.Pose.Position)} | {F(crate.Pose.Position.Y - YardMeasurements.FloorTop)} | {V(crate.Velocity)} "
                + $"| {F(crate.AngularVelocity.Length(), 3)} | {Yes(free.ContainsKey(tick))} | {V(acceleration, 5)} | {V(YardMeasurements.Field(before.Pose.Position), 5)} "
                + $"| {F(person.Pose.Position.Z, 3)} | {Yes(sample.PersonStanding)} | {Home(crate, labels)} |");
        }
        Line(text);
    }

    private static void Homes(StringBuilder text, YardMeasurements m, Dictionary<FrameHandle, string> labels)
    {
        Line(text, "## Physics homes: which bodies have resting frames, and when");
        Line(text);
        Line(text, "Each change of a body's physics home, from the scene as built (tick 0) to the end. A body keeps a home until the next row for it.");
        Line(text);
        Line(text, "| body | tick | t (s) | new home |");
        Line(text, "|---|---:|---:|---|");
        foreach (BodyHomes homes in m.Homes)
            foreach (HomeChange change in homes.Changes)
            {
                string home = change.Mode == NumericMode.None ? $"{labels[change.Home]} (semantic)" : $"{labels[change.Home]} ({change.Mode.ToString().ToLowerInvariant()})";
                Line(text, $"| {homes.Id} | {change.Tick} | {F(change.Time, 3)} | {home} |");
            }
        Line(text);
        int fewest = m.Ticks.Skip(1).Min(tick => tick.NumericFrames), most = m.Ticks.Skip(1).Max(tick => tick.NumericFrames);
        Line(text, $"From tick 1 on, the frame tree holds between {fewest} and {most} numeric frames.");
        Line(text);
    }

    private static void Expectations(StringBuilder text, SurfaceYardRun run, YardMeasurements m)
    {
        Line(text, "## Expectations");
        Line(text);
        Line(text, "Every expected value comes from the scene's geometry and from Earth's gravity, never from the run.");
        Line(text);
        Line(text, "| test | expectation | expected | measured | holds |");
        Line(text, "|---|---|---|---|:---:|");
        void Row(string test, string what, string expected, string measured, bool holds) =>
            Line(text, $"| {test} | {what} | {expected} | {measured} | {Yes(holds)} |");

        const string stack = "TheTwoCratesLeftOnTheStackFallAsleepAndMoveLessThanAMillimeterInSixtySeconds";
        foreach (StackedCrate crate in m.Stacked)
        {
            Row(stack, $"{crate.Id} asleep when the walk starts (t = {F(m.SettledTime, 2)} s)", "yes", Yes(crate.AsleepWhenTheWalkStarts), crate.AsleepWhenTheWalkStarts);
            Row(stack, $"{crate.Id} asleep at the end", "yes", Yes(crate.AsleepAtTheEnd), crate.AsleepAtTheEnd);
            Row(stack, $"{crate.Id}: largest distance from where it rested at {F(m.SettledTime, 2)} s, over 60 s", "0, below 1 mm",
                $"{G(crate.LargestMove * 1e3)} mm", crate.LargestMove < YardMeasurements.StackDriftLimit);
            Row(stack, $"{crate.Id}: distance at the end from where the scene stacked it", "below 2 cm (contacts sink up to 5 mm each)",
                $"{G(crate.OffsetFromWhereStacked * 1e3)} mm", crate.OffsetFromWhereStacked < YardMeasurements.StackSinkLimit);
        }

        const string walk = "ThePersonStandsWhileItWalks";
        PersonWalk w = m.Walk;
        Row(walk, $"ticks of the walks ({m.SettledTick} to {m.WalkedTick} and {m.LandedTick} to {m.PushedTick}) in which the person does not stand", "none",
            w.TicksNotStanding.Count == 0 ? "none" : string.Join(", ", w.TicksNotStanding), w.TicksNotStanding.Count == 0);
        Row(walk, "largest distance of the person's center from 0.9 m above the floor", "below 5 cm", $"{G(w.LargestHeightError * 100)} cm",
            w.LargestHeightError < YardMeasurements.StandingHeightTolerance);
        Row(walk, "largest vertical speed of the person, from the change of its height over each tick", "below 0.05 m/s", $"{G(w.LargestVerticalSpeed)} m/s",
            w.LargestVerticalSpeed < YardMeasurements.StandingVerticalSpeedLimit);
        Row(walk, "distance of the first walk: 200 ticks at 3 m/s", $"{F(YardMeasurements.WalkDistance, 3)} m ± {F(YardMeasurements.WalkDistanceTolerance, 3)} m",
            $"{F(w.FirstWalkDistance, 4)} m", Math.Abs(w.FirstWalkDistance - YardMeasurements.WalkDistance) <= YardMeasurements.WalkDistanceTolerance);

        const string fallTest = "ThePushedCrateFallsOneMeterInTheTimeOfAFreeFallWithTheSurfaceGravity";
        CrateFall fall = m.Fall;
        double worstShare = fall.FreeFlightTicks.Count == 0 ? double.NaN : fall.FreeFlightTicks.Max(step => step.AccelerationError / step.ExpectedGravity);
        double worstSpin = fall.FreeFlightTicks.Count == 0 ? double.NaN : fall.FreeFlightTicks.Max(step => step.AngularVelocityChange);
        Row(fallTest, "steps of free flight", $"at least {YardMeasurements.LeastFreeFlightTicks}", fall.FreeFlightTicks.Count.ToString(CultureInfo.InvariantCulture),
            fall.FreeFlightTicks.Count >= YardMeasurements.LeastFreeFlightTicks);
        Row(fallTest, "largest error of the crate's acceleration in free flight, as a share of μ/r²", "0, below 0.001", G(worstShare),
            worstShare < YardMeasurements.FreeFallRelativeTolerance);
        Row(fallTest, "largest change of the crate's spin in one step of free flight", "0, below 1e-4 rad/s", $"{G(worstSpin)} rad/s", worstSpin < YardMeasurements.SpinChangeLimit);
        Row(fallTest, "mean magnitude of the crate's acceleration in free flight", $"μ/R² = {F(YardMeasurements.SurfaceGravity, 6)} m/s²", $"{F(fall.MeanAcceleration, 6)} m/s²",
            Math.Abs(fall.MeanAcceleration - YardMeasurements.SurfaceGravity) < YardMeasurements.FreeFallRelativeTolerance * YardMeasurements.SurfaceGravity);
        Row(fallTest, "time of a fall of h = 1 m from rest at that acceleration", $"sqrt(2h/g) = {F(fall.ExpectedFallTime, 5)} s ± one tick ({F(m.TickSeconds, 5)} s)",
            $"{F(fall.FallTimeAtMeasuredGravity, 5)} s", Math.Abs(fall.FallTimeAtMeasuredGravity - fall.ExpectedFallTime) <= m.TickSeconds);

        const string jitter = "NoBodyJittersOnceTheYardIsAtRest";
        foreach (RestingBody body in m.AtRest)
        {
            Row(jitter, $"{body.Id}: largest move in one tick, from {F(m.RestFromTime, 2)} s to the end", "0, below 0.1 mm", $"{G(body.LargestStep * 1e3)} mm",
                body.LargestStep < YardMeasurements.JitterStepLimit);
            Row(jitter, $"{body.Id}: largest distance from where it was at {F(m.RestFromTime, 2)} s", "0, below 1 mm", $"{G(body.LargestMove * 1e3)} mm",
                body.LargestMove < YardMeasurements.RestDriftLimit);
            if (body.Id != "person")
                Row(jitter, $"{body.Id} asleep at the end", "yes", Yes(body.AsleepAtTheEnd), body.AsleepAtTheEnd);
        }

        const string homes = "OnlyRestingNumericFramesHoldTheYardsBodies";
        foreach (BodyHomes body in m.Homes)
            Row(homes, $"{body.Id}: ticks from 1 on without a resting numeric frame as its home", "0",
                body.TicksNotResting == 0 ? "0" : $"{body.TicksNotResting} (first: {string.Join(", ", body.FirstTicksNotResting)})", body.TicksNotResting == 0);

        const string errors = "TheRunRecordsNoErrors";
        Row(errors, "World.Errors at the end", "0", run.World.Errors.Count.ToString(CultureInfo.InvariantCulture), run.World.Errors.Count == 0);
        Row(errors, "Vulkan validation messages", "0", run.ValidationMessages.Count.ToString(CultureInfo.InvariantCulture), run.ValidationMessages.Count == 0);
        Line(text);
    }

    private static void NotAsserted(StringBuilder text, YardMeasurements m)
    {
        CrateFall fall = m.Fall;
        Line(text, "## Measured, not asserted");
        Line(text);
        if (fall.TipTick is long tip && fall.LandingTick is long landing)
            Line(text, $"- From the tick at which the crate's center passed the stack's far edge ({tip}) to the tick at which the floor first stopped it ({landing}): "
                + $"{F((landing - tip) * m.TickSeconds, 4)} s, against sqrt(2h/g) = {F(fall.ExpectedFallTime, 4)} s. No test asserts this time: a crate pushed off "
                + "an edge tips over the edge while the edge still holds part of its weight, then lands on a corner, so its fall from the edge to "
                + "the floor is not a free fall from rest. A simple simulation of a box that slides off an edge at 2 to 3 m/s puts this time "
                + "between 0 and 3 ticks above sqrt(2h/g). The test checks the free fall itself, tick by tick, instead.");
        else
            Line(text, "- The crate did not both tip over the stack's edge and land, so the run has no time of fall.");
        Line(text, $"- While the person stands in the walks, the world reports a vertical velocity of up to {F(m.Walk.LargestReportedVerticalSpeed, 4)} m/s for it, "
            + $"though its height changes at most at {F(m.Walk.LargestVerticalSpeed, 6)} m/s. One tick of the surface gravity is {F(YardMeasurements.SurfaceGravity * m.TickSeconds, 4)} m/s: "
            + "the physics backend adds gravity times the step to a standing character's velocity, and the floor stops the motion but leaves the velocity.");
        foreach (StackedCrate crate in m.Stacked)
            Line(text, $"- {crate.Id} fell asleep (s) or woke (w) at the ticks: "
                + (crate.SleepChanges.Count == 0 ? "never" : string.Join(", ", crate.SleepChanges.Select(change => $"{change.Tick} {(change.Asleep ? "s" : "w")}"))) + ".");
        double gravity = YardMeasurements.SurfaceGravity;
        double airTime = 2 * PersonControl.JumpSpeed / gravity, apex = PersonControl.JumpSpeed * PersonControl.JumpSpeed / (2 * gravity);
        if (m.Jump is { TakeOffTick: long off, LandingTick: long down, Apex: double measuredApex })
            Line(text, $"- The jump: physics stopped standing the person at tick {off} and stood it again at tick {down}: {F((down - off) * m.TickSeconds, 4)} s in the air, "
                + $"against 2v/g = {F(airTime, 4)} s for v = 4 m/s. Its center rose {F(measuredApex, 4)} m, against v²/2g = {F(apex, 4)} m.");
        else
            Line(text, "- The person did not both leave the floor and land again in the jump's ticks.");
        Line(text);
    }

    private static void Captures(StringBuilder text, YardMeasurements m)
    {
        Line(text, "## Captures");
        Line(text);
        Line(text, "The script takes these captures, 1280 by 720, into the `--out` folder of a headless run, as `<name>.png`. Each row gives the state "
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
                    Line(text, $"| {capture.Name} | {tick} | {F(sample.Time, 3)} | {camera} | {V(sample.Bodies[YardRecord.TopCrate].Pose.Position, 3)} "
                        + $"| {V(sample.Bodies[YardRecord.Person].Pose.Position, 3)} | {CaptureMustShow.GetValueOrDefault(capture.Name, "(no description)")} |");
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
