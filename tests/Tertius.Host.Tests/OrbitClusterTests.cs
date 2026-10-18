using System.Globalization;
using System.Text;
using Tertius.Demo;
using Tertius.Frames.Rails;
using Tertius.Host.Scripting;
using Tertius.Simulation;

namespace Tertius.Host.Tests;

/// <summary>
/// Falling numeric frames near the ship (docs/plans/numeric-frames.md): one headless run of the checked-in script
/// scripts/orbit-cluster.json in the scene orbit-cluster (see DemoScenes.OrbitCluster), with a log of every tick. The
/// spacewalker, 15 m out from the hatch in earth, thrusts 10 s along the orbit, coasts 5 s, thrusts back 20 s (10 s to
/// stop and 10 s to come back), coasts 5 s and thrusts 10 s along the orbit to stop. Two crates drift from 50 m above
/// and 50 m below the ship for 5 minutes. A crate thrown from the hatch at 5 m/s flies away. Every test of the class
/// reads that one run.
/// </summary>
public sealed class OrbitClusterRun : CheckedInRun
{
    public OrbitClusterRun() : this(new ClusterLog())
    {
    }

    private OrbitClusterRun(ClusterLog log) : base("orbit-cluster", log.Record)
    {
        Log = log;
    }

    internal ClusterLog Log { get; }
}

/// <summary>A key body's frame and physics home from a tick on.</summary>
/// <param name="Tick">The number of completed ticks when the log saw this place first.</param>
/// <param name="Frame">The scene id of the owner of the body's semantic frame: the frame that gameplay sees.</param>
/// <param name="Home">The frame whose physics system holds the body: its semantic frame or a numeric frame.</param>
/// <param name="HomeName">How the report names the home: "semantic", or "numeric N3, falling".</param>
internal sealed record Place(long Tick, string Frame, FrameHandle Home, string HomeName);

/// <summary>The thrown crate after one tick.</summary>
/// <param name="Home">The crate's physics home.</param>
/// <param name="HullHome">The physics home of the ship's hull.</param>
/// <param name="Distance">The distance of the crate's center from the ship's center, in meters.</param>
internal sealed record ThrownCrateTick(long Tick, FrameHandle Home, FrameHandle HullHome, double Distance);

/// <summary>A key body's state in a row of the report's table.</summary>
/// <param name="Position">The position in the semantic frame, in meters.</param>
/// <param name="Velocity">The velocity relative to the semantic frame, in its axes, in m/s.</param>
/// <param name="OffsetFromShip">The position relative to the ship's center, in the ship's axes, in meters. Null for the ship.</param>
internal sealed record BodyState(string Id, string Frame, Double3 Position, Double3 Velocity, Double3? OffsetFromShip, string HomeName);

/// <summary>One row of the report's table: the key bodies after <paramref name="Tick"/> ticks, at world time <paramref name="Time"/> s.</summary>
internal sealed record TableRow(long Tick, double Time, IReadOnlyList<BodyState> Bodies, int NumericFrames, int Errors);

/// <summary>
/// What a run of orbit-cluster does tick by tick that no dump shows, because gameplay never sees numeric frames: the
/// physics home of each key body and the number of numeric frames. It also keeps a table of the key bodies every
/// <see cref="TableTicks"/> ticks, for the report.
/// </summary>
internal sealed class ClusterLog
{
    public static IReadOnlyList<string> Bodies { get; } = ["spacewalker", "ship", "crate-above", "crate-below", "thrown-crate"];

    /// <summary>The ticks between two rows of the table: 5 s at 60 ticks per second.</summary>
    public const int TableTicks = 300;

    // A number for each numeric frame, in the order in which the log saw it first, so that the report can tell them apart.
    private readonly Dictionary<FrameHandle, int> numericNumbers = new();

    /// <summary>Each key body's places, one for each change of its frame or its physics home, the first at tick 0.</summary>
    public Dictionary<string, List<Place>> Changes { get; } = Bodies.ToDictionary(id => id, _ => new List<Place>());

    /// <summary>The thrown crate after every tick, and at tick 0.</summary>
    public List<ThrownCrateTick> ThrownCrate { get; } = new();

    /// <summary>
    /// The offset of each key body but the ship from the ship's center after every tick, and at tick 0, in the ship's
    /// axes, in meters. The list index is the tick. The ship has none, because its own offset is zero.
    /// </summary>
    public Dictionary<string, List<Double3>> Offsets { get; } = Bodies.Where(id => id != "ship").ToDictionary(id => id, _ => new List<Double3>());

    public List<TableRow> Table { get; } = new();

    /// <summary>The most numeric frames that the world had at once.</summary>
    public int MostNumericFrames { get; private set; }

    public void Record(World world, SceneInstance scene)
    {
        long tick = world.TickNumber;
        FrameHandle shipFrame = world.GetOwnedFrame(scene["ship"]);
        int numericFrames = world.Frames.NumericCount;
        MostNumericFrames = Math.Max(MostNumericFrames, numericFrames);
        foreach (string id in Bodies)
        {
            Entity body = scene[id];
            var place = new Place(tick, scene.IdOf(world.GetFrame(body).Entity), world.PhysicsHomeOf(body), HomeName(world, scene, body));
            List<Place> changes = Changes[id];
            if (changes.Count == 0 || changes[^1].Frame != place.Frame || changes[^1].Home != place.Home)
                changes.Add(place);
        }
        foreach ((string id, List<Double3> offsets) in Offsets)
        {
            if (offsets.Count != tick)
                throw new InvalidOperationException($"The log saw tick {tick} after {offsets.Count} ticks: it must see every tick from tick 0.");
            offsets.Add(world.PoseIn(scene[id], shipFrame).Position);
        }
        Entity thrown = scene["thrown-crate"];
        ThrownCrate.Add(new ThrownCrateTick(tick, world.PhysicsHomeOf(thrown), world.PhysicsHomeOf(scene["ship"]),
            world.PoseIn(thrown, shipFrame).Position.Length));
        if (tick % TableTicks == 0)
            Table.Add(new TableRow(tick, world.Time, [.. Bodies.Select(id => StateOf(world, scene, id, shipFrame))], numericFrames, world.Errors.Count));
    }

    /// <summary>The last tick that the log saw.</summary>
    public long LastTick => ThrownCrate[^1].Tick;

    /// <summary>Each tick after which some key body's frame or physics home differs from the tick before, in order.</summary>
    public IEnumerable<long> ChangeTicks =>
        Changes.Values.SelectMany(places => places.Skip(1)).Select(place => place.Tick).Distinct().Order();

    /// <summary>
    /// How much the step of <paramref name="id"/>'s offset from the ship changes at <paramref name="tick"/>, in meters:
    /// the offset after the next tick, less twice the offset after this one, plus the offset after the tick before. It is
    /// the body's acceleration relative to the ship, in the ship's axes, times the square of a tick. A jump of the body
    /// or of the ship between two ticks adds its size here, at the tick before the jump and the tick after it.
    /// </summary>
    public Double3 ChangeOfStep(string id, long tick)
    {
        List<Double3> offsets = Offsets[id];
        return offsets[(int)tick + 1] - 2 * offsets[(int)tick] + offsets[(int)tick - 1];
    }

    private BodyState StateOf(World world, SceneInstance scene, string id, FrameHandle shipFrame)
    {
        Entity body = scene[id];
        FrameHandle frame = world.GetFrame(body), own = world.GetOwnedFrame(body);
        // A frame's owner moves with its frame, whose velocity the frame tree keeps, as a state dump reads it.
        Double3 velocity = own.IsNull ? world.VelocityIn(body, frame) : world.Frames.LinearVelocity(own);
        return new BodyState(id, scene.IdOf(frame.Entity), world.PoseIn(body, frame).Position, velocity,
            own == shipFrame ? null : world.PoseIn(body, shipFrame).Position, HomeName(world, scene, body));
    }

    private string HomeName(World world, SceneInstance scene, Entity body)
    {
        FrameHandle home = world.PhysicsHomeOf(body);
        if (!world.Frames.IsNumeric(home))
            return home == world.GetFrame(body) ? "semantic" : $"semantic {scene.IdOf(home.Entity)}, not the body's frame";
        if (!numericNumbers.TryGetValue(home, out int number))
            numericNumbers[home] = number = numericNumbers.Count + 1;
        return $"numeric N{number}, {world.Frames.Numeric(home).ToString().ToLowerInvariant()}";
    }
}

[Collection(GpuCollection.Name)]
public sealed class OrbitClusterTests(OrbitClusterRun run) : IClassFixture<OrbitClusterRun>
{
    // The spacewalker's mass in the scene, in kg. Its thrusters push with PersonControl.ThrustForce, 1.5 m/s² on it.
    private const double SpacewalkerMass = 70;

    // A post acts from the third tick after it (see SpacewalkSliceTests): the person reads the key in the tick after the
    // post, and its thrusters wait for physics's report of whether it stands, which comes a tick late. So a key posted
    // after k ticks thrusts from the step that starts after k + 2 ticks. Both the press and the release wait so, so each
    // thrust lasts as many ticks as the script holds the key.
    private const int PostLatencyTicks = 2;

    // Silk.NET's codes of the keys W and S, which thrust along the view and against it.
    private const int KeyW = 87, KeyS = 83;

    // The hull's half extents in meters, 20 by 8 by 60 m, as the orbit scene builds it. Physics bounds it by a sphere
    // through its corners, which the merge test of numeric frames uses.
    private static readonly double HullRadius = new Double3(10, 4, 30).Length;

    // The tolerances of the three expectations, in meters.
    private const double SpacewalkerTolerance = 0.05, DriftTolerance = 0.01;

    // How far the bounding spheres that physics reports, and the crate's velocity relative to the hull, may differ from
    // the values here: the crate's velocity in earth also has the ship's turn times its arm, about 0.06 m/s at 50 m, and
    // the orbit bends its path by under 0.5 m in its first 12 s. In meters.
    private const double SplitSlack = 1;

    // How much a key body's change of step relative to the ship, less any thrust, may differ at a change of frame or
    // physics home from two ticks later, in meters: 0.05 mm. Float rounding of a body's place in a numeric frame moves it
    // by under a micrometer. The thrown crate's change of rule at the ship's bubble, where the Coriolis force that the
    // ship's frame leaves out (2ωv, 0.011 m/s² at 5 m/s) starts to show in the ship's axes, changes its step by about
    // 3 micrometers. A hand-off that went through float coordinates in earth, 6771 km from its center, would move a body
    // by up to 0.5 m.
    private const double JumpTolerance = 5e-5;

    private static readonly string[] Crates = ["crate-above", "crate-below"];
    private static readonly string[] Minutes = ["minute-1", "minute-2", "minute-3", "minute-4", "minute-5"];

    private StateDump Dump(string name) => run.Result.Dump(name);

    private StateDump Start => Dump("start");

    /// <summary>The length of a tick, in seconds, from the world's clock in two dumps.</summary>
    private double TickSeconds => (Dump("minute-5").Time - Start.Time) / (Dump("minute-5").Tick - Start.Tick);

    [Fact]
    public void TheSpacewalkerStaysInEarthThroughItsWholeWalk()
    {
        // The double-precision integration below is in earth's gravity alone. In the ship's frame no gravity would act
        // outside the cabin, so the spacewalker must not pass into the ship's bubble.
        foreach (Place place in run.Log.Changes["spacewalker"].Where(p => p.Tick <= Dump("spacewalker-back").Tick))
            place.Frame.Should().Be("earth", $"the spacewalker is past the ship's bubble at tick {place.Tick}");
    }

    [Fact]
    public void TheSpacewalkersThrustTakesItAbout225MAlongTheOrbitAhead()
    {
        // In free space, 10 s at 1.5 m/s², 5 s of coast at 15 m/s and 10 s of braking take it 75 + 75 + 75 m. The script
        // dumps 2 ticks before the braking ends. The orbit bends the way by a few meters.
        StateDump farthest = Dump("farthest");
        FramePose ship = farthest.Entity("ship").Pose;
        Double3 offset = ship.Rotation.Conjugate().Rotate(farthest.Entity("spacewalker").Pose.Position - ship.Position);

        (offset - DemoScenes.SpacewalkerStart).Z.Should().BeApproximately(-225, 5, "the ship's nose, along the orbit, is its -Z");
    }

    [Theory]
    [InlineData("farthest")]
    [InlineData("spacewalker-back")]
    public void TheSpacewalkerIsWithin5CmOfADoublePrecisionIntegrationOfItsThrust(string dump)
    {
        (Double3 expected, Double3 measured) = SpacewalkerOffset(dump);

        Double3.Distance(measured, expected).Should().BeLessThan(SpacewalkerTolerance,
            $"the spacewalker's offset from the ship in earth's axes in {dump} is {measured}, and a double-precision integration of its thrust puts it at {expected}");
    }

    public static TheoryData<string, string> CrateDumps
    {
        get
        {
            var data = new TheoryData<string, string>();
            foreach (string crate in Crates)
                foreach (string minute in Minutes)
                    data.Add(crate, minute);
            return data;
        }
    }

    [Theory]
    [MemberData(nameof(CrateDumps))]
    public void TheCratesAboveAndBelowTheShipDriftAsTheClohessyWiltshireEquationsSay(string crate, string minute)
    {
        Dump(minute).Entity(crate).Pose.Frame.Should().Be("earth");
        (Double3 expected, Double3 measured) = CrateDrift(crate, minute);

        Double3.Distance(measured, expected).Should().BeLessThan(DriftTolerance,
            $"{crate}'s offset from the ship in earth's axes in {minute} is {measured}, and the Clohessy-Wiltshire equations put it at {expected}");
    }

    [Fact]
    public void TheThrownCrateLeavesTheShipsNumericFrameOnlyBeyondTheMergeReachPlusTheSplitMargin()
    {
        (double low, double high) = SplitWindow();
        ThrownCrateTick split = ThrownCrateSplit() ?? throw new InvalidOperationException("The thrown crate never left the hull's physics home.");

        split.Distance.Should().BeInRange(low - SplitSlack, high + SplitSlack,
            $"the thrown crate left the hull's numeric frame at tick {split.Tick}, {split.Distance:F2} m from the ship's center");
    }

    [Fact]
    public void TheThrownCrateChangesItsPhysicsHomeTwiceAndThenKeepsItsOwnFallingFrame()
    {
        // From the ship's frame, the hand-off past the ship's bubble takes the crate into the frame that holds the hull,
        // a falling numeric frame. Beyond the merge reach it splits off into a falling numeric frame of its own, and
        // nothing that it meets later is near enough to merge with it. A merge and split thrash would add changes.
        List<ThrownCrateTick> ticks = run.Log.ThrownCrate;
        List<ThrownCrateTick> changes = [ticks[0], .. ticks.Where((t, i) => i > 0 && t.Home != ticks[i - 1].Home)];

        changes.Select(c => c.Tick).Should().HaveCount(3, "the crate's home changes twice: into the hull's numeric frame and into its own");
        changes[0].Home.Should().Be(run.World.GetOwnedFrame(run.Scene["ship"]), "the crate starts in the ship's frame");
        changes[1].Home.Should().Be(changes[1].HullHome, "the hand-off takes the crate into the frame that holds the hull");
        changes[2].Home.Should().NotBe(changes[2].HullHome);
        run.World.Frames.IsNumeric(changes[2].Home).Should().BeTrue();
        run.World.Frames.Numeric(changes[2].Home).Should().Be(NumericMode.Falling, "the crate moves at orbital speed in earth");
        ticks[^1].Home.Should().Be(changes[2].Home, "the crate keeps its own frame to the end of the run");
    }

    [Fact]
    public void NoKeyBodyJumpsRelativeToTheShipWhenAFrameOrPhysicsHomeChanges()
    {
        Jumps().Should().NotBeEmpty("the thrown crate changes its frame and its physics home");
        foreach (Jump jump in Jumps())
            jump.Size.Should().BeLessThan(JumpTolerance,
                $"{jump.Id}'s step relative to the ship at tick {jump.Tick}, by the change after tick {jump.Change}, must change as two ticks after the change");
    }

    /// <summary>How much a key body's change of step relative to the ship, at a tick next to a change, differs from it two ticks after the change.</summary>
    /// <param name="Change">The tick after which some key body's frame or physics home changed.</param>
    /// <param name="Tick">The tick before the change or the tick of the change, where a jump would show.</param>
    /// <param name="Size">The length of the difference, in meters.</param>
    private sealed record Jump(long Change, string Id, long Tick, double Size);

    /// <summary>
    /// The jumps of every key body but the ship at every change. A change of physics home moves a body into another
    /// physics system, and a change of frame states it in other axes. Neither may move the body, nor the ship when the
    /// change is the ship's. So across the change, each body's acceleration relative to the ship, less the spacewalker's
    /// thrust, stays nearly what it is two ticks after: earth's tide and the turn of the ship's axes change it slowly.
    /// The thrust starts and stops by the script's keys, so it comes off first.
    /// </summary>
    private IEnumerable<Jump> Jumps()
    {
        ClusterLog log = run.Log;
        int[] keys = KeysAlongBack(run.Script, log.LastTick);
        Double3 Unthrust(string id, long tick) => log.ChangeOfStep(id, tick) - (id == "spacewalker" ? ThrustStep(keys[tick], tick) : Double3.Zero);
        foreach (long change in log.ChangeTicks.Where(change => change + 3 <= log.LastTick))
            foreach (string id in log.Offsets.Keys)
                foreach (long tick in new[] { change - 1, change }.Where(tick => tick >= 1))
                    yield return new Jump(change, id, tick, (Unthrust(id, tick) - Unthrust(id, change + 2)).Length);
    }

    /// <summary>
    /// What the spacewalker's thrust adds to its change of step relative to the ship at <paramref name="tick"/> (see
    /// <see cref="ClusterLog.ChangeOfStep"/>), in the ship's axes, in meters: the thrust over the step after the tick,
    /// times the square of a tick. The step moves the velocity first and then the position, so that step's thrust is all
    /// of it. <paramref name="key"/> is the step's key along the spacewalker's +Z (see <see cref="KeysAlongBack"/>). The
    /// spacewalker keeps the axes that it starts with, the ship's at the start, while the ship's axes turn once per orbit.
    /// </summary>
    private Double3 ThrustStep(int key, long tick)
    {
        DQuaternion walker = DemoScenes.ShipOrbit.RotationAt(Start.Time, DemoScenes.EarthMu);
        DQuaternion ship = DemoScenes.ShipOrbit.RotationAt(Start.Time + tick * TickSeconds, DemoScenes.EarthMu);
        return ship.Conjugate().Rotate(walker.Rotate(new Double3(0, 0, key))) * (PersonControl.ThrustForce / SpacewalkerMass * TickSeconds * TickSeconds);
    }

    [Fact]
    public void TheRunRecordsNoErrors()
    {
        run.World.Errors.Should().BeEmpty();
    }

    [Fact]
    public void WritesTheSceneReportWhereTertiusSceneReportNamesAFile()
    {
        string? path = Environment.GetEnvironmentVariable("TERTIUS_SCENE_REPORT");
        if (string.IsNullOrEmpty(path))
            return;
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
        File.WriteAllText(path, Report());

        new FileInfo(path).Length.Should().BePositive();
    }

    /// <summary>
    /// The spacewalker's offset from the ship's center in earth's axes in the dump <paramref name="name"/>, as a double-precision
    /// integration of the spacewalker's thrust in earth's gravity expects it, and as the run measured it. The integration
    /// starts from the scene's placement at the dump start and steps the ship and the spacewalker with fourth-order
    /// Runge-Kutta, one tick per step, under earth's point gravity. The spacewalker also gets its thrust, held constant
    /// over each tick in which the script holds W or S, along the axes it faces at the start, which free fall keeps.
    /// </summary>
    private (Double3 Expected, Double3 Measured) SpacewalkerOffset(string name)
    {
        StateDump back = Dump(name);
        Double3 measured = back.Entity("spacewalker").Pose.Position - back.Entity("ship").Pose.Position;

        (Double3 shipPosition, Double3 shipVelocity) = DemoScenes.ShipOrbit.StateAt(Start.Time, DemoScenes.EarthMu);
        (DPose walker, Double3 walkerVelocity) = DemoScenes.BesideTheShip(DemoScenes.SpacewalkerStart, Start.Time);
        Double3 walkerPosition = walker.Position;
        int[] keys = KeysAlongBack(run.Script, back.Tick - Start.Tick);
        double dt = TickSeconds;
        for (int step = 0; step < keys.Length; step++)
        {
            Double3 thrust = walker.Rotation.Rotate(new Double3(0, 0, keys[step])) * (PersonControl.ThrustForce / SpacewalkerMass);
            (shipPosition, shipVelocity) = RungeKutta(shipPosition, shipVelocity, Double3.Zero, dt);
            (walkerPosition, walkerVelocity) = RungeKutta(walkerPosition, walkerVelocity, thrust, dt);
        }
        return (walkerPosition - shipPosition, measured);
    }

    /// <summary>
    /// For each step from the start, the keys that thrust the spacewalker along its +Z: 1 while S alone thrusts, -1 while
    /// W alone does, 0 otherwise. The view looks along -Z, so W thrusts along the view. The script's posts set them.
    /// </summary>
    private static int[] KeysAlongBack(Script script, long steps)
    {
        var posts = new List<(long Tick, int Code, bool Down)>();
        long tick = 0;
        foreach (ScriptStep step in script.Steps)
        {
            if (step is RunStep { Ticks: var ticks })
                tick += ticks;
            else if (step is PostStep { Event: "InputEvent", Payload: { } payload } && payload.GetProperty("kind").GetString() is "keyDown" or "keyUp")
                posts.Add((tick, payload.GetProperty("code").GetInt32(), payload.GetProperty("kind").GetString() == "keyDown"));
        }
        var keys = new int[steps];
        bool forward = false, back = false;
        int next = 0;
        for (long s = 0; s < steps; s++)
        {
            for (; next < posts.Count && posts[next].Tick + PostLatencyTicks <= s; next++)
            {
                if (posts[next].Code == KeyW)
                    forward = posts[next].Down;
                else if (posts[next].Code == KeyS)
                    back = posts[next].Down;
            }
            keys[s] = (back ? 1 : 0) - (forward ? 1 : 0);
        }
        return keys;
    }

    /// <summary>One fourth-order Runge-Kutta step of <paramref name="dt"/> s in earth's point gravity plus a constant thrust in m/s².</summary>
    private static (Double3 Position, Double3 Velocity) RungeKutta(Double3 position, Double3 velocity, Double3 thrust, double dt)
    {
        Double3 Acceleration(Double3 at) => at * (-DemoScenes.EarthMu / (at.LengthSquared * at.Length)) + thrust;

        Double3 v1 = velocity, a1 = Acceleration(position);
        Double3 v2 = velocity + a1 * (dt / 2), a2 = Acceleration(position + v1 * (dt / 2));
        Double3 v3 = velocity + a2 * (dt / 2), a3 = Acceleration(position + v2 * (dt / 2));
        Double3 v4 = velocity + a3 * dt, a4 = Acceleration(position + v3 * dt);
        return (position + (v1 + v2 * 2 + v3 * 2 + v4) * (dt / 6), velocity + (a1 + a2 * 2 + a3 * 2 + a4) * (dt / 6));
    }

    /// <summary>
    /// The crate's offset from the ship's center in earth's axes in the dump <paramref name="minute"/>, as the
    /// Clohessy-Wiltshire equations expect it for the ship's circular orbit, and as the run measured it. The equations
    /// work in axes that turn with the orbit: x up along the radius, y along the motion, z along the orbit's normal, at
    /// the orbit's rate n = sqrt(G·M / r³). The crate starts from the scene's placement, at rest relative to the ship.
    /// </summary>
    private (Double3 Expected, Double3 Measured) CrateDrift(string crate, string minute)
    {
        StateDump dump = Dump(minute);
        Double3 measured = dump.Entity(crate).Pose.Position - dump.Entity("ship").Pose.Position;

        (Double3 shipPosition, Double3 shipVelocity) = DemoScenes.ShipOrbit.StateAt(Start.Time, DemoScenes.EarthMu);
        double n = Math.Sqrt(DemoScenes.EarthMu / (shipPosition.LengthSquared * shipPosition.Length));
        Double3 up = shipPosition.Normalized(), normal = Double3.Cross(shipPosition, shipVelocity).Normalized();
        Double3 along = Double3.Cross(normal, up);
        (DPose pose, Double3 velocity) = DemoScenes.BesideTheShip(crate == "crate-above" ? DemoScenes.CrateAboveStart : DemoScenes.CrateBelowStart, Start.Time);
        Double3 offset = pose.Position - shipPosition;
        // The crate's velocity relative to the turning axes.
        Double3 drift = velocity - shipVelocity - Double3.Cross(normal * n, offset);
        double x0 = Double3.Dot(offset, up), y0 = Double3.Dot(offset, along), z0 = Double3.Dot(offset, normal);
        double vx = Double3.Dot(drift, up), vy = Double3.Dot(drift, along), vz = Double3.Dot(drift, normal);

        double t = dump.Time - Start.Time, c = Math.Cos(n * t), s = Math.Sin(n * t);
        double x = (4 - 3 * c) * x0 + s / n * vx + 2 / n * (1 - c) * vy;
        double y = 6 * (s - n * t) * x0 + y0 - 2 / n * (1 - c) * vx + (4 * s - 3 * n * t) / n * vy;
        double z = c * z0 + s / n * vz;
        Double3 upThen = up * c + along * s, alongThen = along * c - up * s;
        return (upThen * x + alongThen * y + normal * z, measured);
    }

    /// <summary>
    /// The distances from the ship's center at which the thrown crate must split off from the hull's numeric frame, in
    /// meters. The merge test reaches to the sum of the two bounding spheres' radii plus their relative speed times the
    /// lookahead time. Two bodies in one numeric frame stay linked to the split margin beyond that, and they split once
    /// they have been apart for the split cooldown. The crate moves at 5 m/s, so it splits between the reach plus the
    /// margin and that plus the way it goes in the cooldown and two ticks.
    /// </summary>
    private (double Low, double High) SplitWindow()
    {
        var options = new NumericFrameOptions();
        double speed = DemoScenes.ThrownCrateVelocity.Length;
        double reach = HullRadius + ((Double3)DemoScenes.ClusterCrateHalfExtents).Length + speed * options.LookaheadTime;
        double low = reach + options.SplitMargin;
        return (low, low + speed * (options.SplitCooldown + 2 * TickSeconds));
    }

    /// <summary>The thrown crate after the tick in which it left the hull's physics home for another, or null if it never did.</summary>
    private ThrownCrateTick? ThrownCrateSplit()
    {
        List<ThrownCrateTick> ticks = run.Log.ThrownCrate;
        for (int i = 1; i < ticks.Count; i++)
            if (ticks[i - 1].Home == ticks[i - 1].HullHome && ticks[i].Home != ticks[i].HullHome)
                return ticks[i];
        return null;
    }

    // The report.

    private const string Purpose = """
        The scene orbit-cluster is the orbit scene with three things near its ship that only falling numeric frames get
        right. Each moves at 7.7 km/s in earth, where a float velocity steps by 0.5 mm/s, so without numeric frames its
        motion relative to the ship drifts through rounding (ticket ter-fa7j: 5 s of thrust brought a person back 1.7 m off).

        - The spacewalker floats in earth 15 m out from the hatch to port, at rest relative to the ship, facing the ship's
          nose along the orbit. The script thrusts it 10 s along the orbit (W), lets it coast 5 s, thrusts it back 20 s (S:
          10 s to stop and 10 s to come back), lets it coast 5 s and thrusts it 10 s along the orbit again to stop. It must
          end within 5 cm of where a double-precision integration of the same thrust in earth's gravity puts it, relative to
          the ship. Earth's frame does not turn, so the spacewalker keeps its axes and thrusts along one fixed direction.
          Its thrusts then cancel, and in 52 s earth's tidal gravity moves it only about 0.2 m from where it started.
        - crate-above and crate-below start 50 m above and 50 m below the ship's center along the radius, at rest relative
          to the ship. Over 5 minutes they must drift relative to the ship as the Clohessy-Wiltshire equations say for the
          ship's circular orbit 400 km up, to within 1 cm.
        - thrown-crate starts in the ship's frame just outside the hatch, at 5 m/s relative to the ship, out and aft. It must
          leave the ship's bubble into the numeric frame that holds the hull, split off into a falling numeric frame of its
          own once it is beyond the merge reach plus the split margin, and keep that frame: two changes of physics home, no
          merge and split thrashing.
        - No key body may jump relative to the ship at a change of its frame or physics home, or of the ship's: see the
          section on continuity below.

        Two known differences from exact Newtonian motion, which no expectation checks, because each comes from a rule of
        the engine and each moves the bodies together or only after the frames have let go of them:

        - The ship's orbit is not a true circle in the run. The physics step and the step of the falling numeric frames
          are semi-implicit Euler at 1/60 s (the velocity first, then the position), which moves the ship on a nearly
          circular orbit whose radius swings by up to about r·n·dt/2 = 64 m over one orbit, about 21 m below 6771 km after
          300 s; energy and angular momentum stay constant. The crates and the spacewalker fall by the same steps, so
          their motion relative to the ship keeps to the Clohessy-Wiltshire solution to well under 1 cm, and every
          expectation measures motion relative to the ship.
        - Only the linear acceleration of a frame acts on its contents, with no Coriolis or centrifugal force
          (ARCHITECTURE.md). For its first 3 s, the thrown crate is in the ship's frame, which turns once per orbit, so it
          flies straight in the ship's turning axes and then falls freely in earth. After 5 minutes its offset from the
          ship differs from exact Newtonian motion from the same start by about 8 m along the radius and 3 m along the
          orbit.
        """;

    /// <summary>What each capture of the script must show.</summary>
    private static readonly Dictionary<string, string> CaptureMeaning = new()
    {
        ["watch-start"] = "From 80 m above the ship and 139 m to port, looking down 30 degrees at a point 95 m ahead of the ship's center, nose to the left. Earth's limb crosses the view at about y 250 px; black space above it, blue Earth with its light 10-degree grid below it. The sun stands straight above the ship, so top faces are sunlit and side faces dark. The hull right of the middle, from about x 880 to 1160 px: its sunlit gray roof (about y 330 to 365 px) and below it its dark port wall (to about y 395 px). The spacewalker, a small glowing yellow upright box, just below the wall near its aft end, at about (1110, 425) px. Two glowing orange crates, about 5 px across: crate-above against black space at about (1080, 160) px, crate-below against Earth at about (960, 505) px, in the hull's shadow. The thrown crate, glowing orange, at the foot of the port wall at about (1070, 390) px. Each position is about, from a pinhole view with a 60-degree vertical field.",
        ["spacewalker-start"] = "The spacewalker's view along the orbit, toward the ship's nose: the ship's port wall to the right, close, running ahead; black space above, Earth below.",
        ["spacewalker-back"] = "Nearly the same view as spacewalker-start, after the walk: the spacewalker is back within about 0.2 m of its start. The ship turns once per orbit and the spacewalker keeps its axes in earth, so the port wall shows turned by about 3.4 degrees about the spacewalker's left-right axis.",
        ["watch-back"] = "As watch-start after 52 s: the spacewalker back where it started, within 1 px. The thrown crate gone from the hatch and out of the view, far off to the right: it has flown about 160 m to port and 210 m aft. The crates above and below within about 2 px of where they were.",
        ["watch-end"] = "As watch-start after 5 minutes: crate-above about 8.6 m higher and 2 m aft, about 40 px higher and 20 px to the right, at about (1100, 120) px; crate-below about as far lower and ahead, about 20 px lower and 15 px to the left, at about (945, 525) px. The thrown crate out of the view. The spacewalker near where it was. The sun now stands about 20 degrees aft of straight above, so the roof is still sunlit and the hull's aft end gets some light.",
    };

    private string Report()
    {
        var text = new StringBuilder();
        text.AppendLine("# orbit-cluster: falling numeric frames near the ship").AppendLine();
        text.AppendLine("## What the scene is and what it must show").AppendLine().AppendLine(Purpose);

        ClusterLog log = run.Log;
        double start = Start.Time;
        text.AppendLine("## The run").AppendLine();
        text.AppendLine(Invariant($"- Ticks: {run.World.TickNumber}, {run.World.TickNumber * TickSeconds:F1} s of simulated time at {1 / TickSeconds:F0} ticks per second."));
        text.AppendLine(Invariant($"- The most numeric frames at once: {log.MostNumericFrames}."));
        text.AppendLine(Invariant($"- World.Errors at the end: {run.World.Errors.Count}."));
        foreach (string error in run.World.Errors.Take(20))
            text.AppendLine($"  - {error}");
        text.AppendLine();

        text.AppendLine("## Changes of frame and physics home").AppendLine();
        text.AppendLine("Each row is a tick after which a body's semantic frame or physics home differs from the tick before. Numeric frames are numbered in the order in which the log first saw them.").AppendLine();
        foreach (string id in ClusterLog.Bodies)
        {
            text.AppendLine($"### {id}").AppendLine().AppendLine("| tick | t (s) | frame | physics home |").AppendLine("|---:|---:|---|---|");
            foreach (Place place in log.Changes[id])
                text.AppendLine(Invariant($"| {place.Tick} | {place.Tick * TickSeconds:F2} | {place.Frame} | {place.HomeName} |"));
            text.AppendLine();
        }

        text.AppendLine("## Continuity at each change").AppendLine();
        text.AppendLine("For each tick k after which some key body's frame or physics home changed, each body's change of step relative to the ship at the ticks around it: |o(t + 1) − 2·o(t) + o(t − 1)|, in mm, where o(t) is the body's offset from the ship's center after tick t, in the ship's axes. It is the body's acceleration relative to the ship times the square of a tick. Earth's tide gives under 0.001 mm, the turn of the ship's axes up to about 0.01 mm more for a body that moves at 15 m/s relative to the ship, and the spacewalker's thrust 0.417 mm (1.5 m/s² times (1/60 s)²). In the ship's frame no Coriolis force acts, so the thrown crate's value rises from about 0 to about 0.003 mm when it leaves the ship's frame at 5 m/s. A jump of a body, or of the ship, between ticks k − 1 and k would add its size at the rows k − 1 and k. The expectation on jumps takes the thrust off and compares each value at k − 1 and k with the one at k + 2.").AppendLine();
        foreach (long change in log.ChangeTicks)
        {
            IEnumerable<string> changed = log.Changes
                .SelectMany(c => c.Value.Where(p => p.Tick == change).Select(p => $"{c.Key} into frame {p.Frame}, home {p.HomeName}"));
            text.AppendLine(Invariant($"### Tick {change}, {change * TickSeconds:F2} s: {string.Join("; ", changed)}")).AppendLine();
            text.AppendLine($"| tick | {string.Join(" | ", log.Offsets.Keys)} |").AppendLine($"|---:|{string.Concat(log.Offsets.Keys.Select(_ => "---:|"))}");
            for (long tick = Math.Max(1, change - 2); tick <= Math.Min(log.LastTick - 1, change + 2); tick++)
                text.AppendLine(Invariant($"| {tick} | {string.Join(" | ", log.Offsets.Keys.Select(id => Invariant($"{log.ChangeOfStep(id, tick).Length * 1000:F4}")))} |"));
            text.AppendLine();
        }

        text.AppendLine(Invariant($"## The key bodies every {ClusterLog.TableTicks * TickSeconds:F0} s")).AppendLine();
        text.AppendLine("Position and velocity are in the body's semantic frame. The offset is from the ship's center, in the ship's axes: +X starboard, +Y up away from Earth, -Z toward the nose, along the orbit.").AppendLine();
        text.AppendLine("| t (s) | numeric frames | World.Errors |").AppendLine("|---:|---:|---:|");
        foreach (TableRow row in log.Table)
            text.AppendLine(Invariant($"| {row.Time - start:F0} | {row.NumericFrames} | {row.Errors} |"));
        text.AppendLine();
        foreach (string id in ClusterLog.Bodies)
        {
            text.AppendLine($"### {id}").AppendLine();
            text.AppendLine("| t (s) | frame | position (m) | velocity (m/s) | offset from the ship (m) | physics home |").AppendLine("|---:|---|---|---|---|---|");
            foreach (TableRow row in log.Table)
            {
                BodyState body = row.Bodies.Single(b => b.Id == id);
                string offset = body.OffsetFromShip is { } at ? Vector(at, "F3") : "-";
                text.AppendLine(Invariant($"| {row.Time - start:F0} | {body.Frame} | {Vector(body.Position, "F3")} | {Vector(body.Velocity, "F4")} | {offset} | {body.HomeName} |"));
            }
            text.AppendLine();
        }

        text.AppendLine("## Expectations").AppendLine();
        text.AppendLine("| expectation | expected | measured | difference | tolerance | result |").AppendLine("|---|---|---|---|---|---|");
        foreach (string line in ExpectationRows())
            text.AppendLine(line);
        text.AppendLine();

        text.AppendLine("## Captures").AppendLine();
        text.AppendLine("The script draws these at 1280 by 720. The headless run with --out writes them as <name>.png.").AppendLine();
        text.AppendLine("| capture | tick | t (s) | camera | what it must show |").AppendLine("|---|---:|---:|---|---|");
        foreach ((string name, long tick, string camera) in CapturesOf(run.Script))
            text.AppendLine(Invariant($"| {name} | {tick} | {tick * TickSeconds:F1} | {camera} | {CaptureMeaning.GetValueOrDefault(name, "(no description)")} |"));
        return text.ToString();
    }

    private IEnumerable<string> ExpectationRows()
    {
        yield return Row("The spacewalker stays in earth through its walk", () =>
        {
            Place[] outside = [.. run.Log.Changes["spacewalker"].Where(p => p.Tick <= Dump("spacewalker-back").Tick && p.Frame != "earth")];
            return ("earth at every tick", outside.Length == 0 ? "earth at every tick" : $"{outside[0].Frame} from tick {outside[0].Tick}", "-", "-", outside.Length == 0);
        });
        foreach (string dump in (string[])["farthest", "spacewalker-back"])
            yield return Row($"Spacewalker against a double-precision integration of its thrust: offset from the ship in earth's axes (m), {dump}", () =>
            {
                (Double3 expected, Double3 measured) = SpacewalkerOffset(dump);
                double difference = Double3.Distance(measured, expected);
                return (Vector(expected, "F4"), Vector(measured, "F4"), Invariant($"{difference:F4} m"), Invariant($"{SpacewalkerTolerance} m"), difference < SpacewalkerTolerance);
            });
        yield return Row("Spacewalker at its farthest: way along the orbit, the ship's -Z, from its start (m)", () =>
        {
            StateDump farthest = Dump("farthest");
            FramePose ship = farthest.Entity("ship").Pose;
            double along = -(ship.Rotation.Conjugate().Rotate(farthest.Entity("spacewalker").Pose.Position - ship.Position) - DemoScenes.SpacewalkerStart).Z;
            return ("225", Invariant($"{along:F3}"), Invariant($"{along - 225:F3} m"), "5 m", Math.Abs(along - 225) <= 5);
        });
        yield return Row("Spacewalker back: offset from the ship's center in the ship's axes, against its start (for information)", () =>
        {
            StateDump back = Dump("spacewalker-back");
            FramePose ship = back.Entity("ship").Pose;
            Double3 offset = ship.Rotation.Conjugate().Rotate(back.Entity("spacewalker").Pose.Position - ship.Position);
            return (Vector(DemoScenes.SpacewalkerStart, "F3"), Vector(offset, "F3"), Invariant($"{Double3.Distance(offset, DemoScenes.SpacewalkerStart):F3} m"), "-", true);
        });
        foreach (string crate in Crates)
            foreach (string minute in Minutes)
                yield return Row($"{crate}: Clohessy-Wiltshire drift, offset from the ship in earth's axes (m), {minute}", () =>
                {
                    (Double3 expected, Double3 measured) = CrateDrift(crate, minute);
                    double difference = Double3.Distance(measured, expected);
                    return (Vector(expected, "F4"), Vector(measured, "F4"), Invariant($"{difference:F4} m"), Invariant($"{DriftTolerance} m"), difference < DriftTolerance);
                });
        yield return Row("thrown-crate: distance from the ship's center when it leaves the hull's numeric frame", () =>
        {
            (double low, double high) = SplitWindow();
            ThrownCrateTick? split = ThrownCrateSplit();
            bool inside = split is { } s && s.Distance >= low - SplitSlack && s.Distance <= high + SplitSlack;
            return (Invariant($"{low:F2} to {high:F2} m"), split is { } at ? Invariant($"{at.Distance:F2} m at tick {at.Tick}") : "never left",
                "-", Invariant($"{SplitSlack} m on each side"), inside);
        });
        yield return Row("thrown-crate: changes of physics home", () =>
        {
            List<ThrownCrateTick> ticks = run.Log.ThrownCrate;
            int changes = ticks.Where((t, i) => i > 0 && t.Home != ticks[i - 1].Home).Count();
            return ("2", Invariant($"{changes}"), "-", "exact", changes == 2);
        });
        yield return Row("No jump relative to the ship at any change of frame or physics home: the largest difference of a change of step from two ticks after the change (mm)", () =>
        {
            Jump largest = Jumps().MaxBy(jump => jump.Size) ?? throw new InvalidOperationException("The run has no change of frame or physics home.");
            return ("0", Invariant($"{largest.Size * 1000:F4}, {largest.Id} at tick {largest.Tick}"), Invariant($"{largest.Size * 1000:F4} mm"),
                Invariant($"{JumpTolerance * 1000} mm"), largest.Size < JumpTolerance);
        });
        yield return Row("World.Errors at the end", () => ("0", Invariant($"{run.World.Errors.Count}"), "-", "exact", run.World.Errors.Count == 0));
    }

    private static string Row(string name, Func<(string Expected, string Measured, string Difference, string Tolerance, bool Pass)> measure)
    {
        try
        {
            (string expected, string measured, string difference, string tolerance, bool pass) = measure();
            return $"| {name} | {expected} | {measured} | {difference} | {tolerance} | {(pass ? "pass" : "FAIL")} |";
        }
        catch (Exception ex)
        {
            return $"| {name} | - | could not measure: {ex.Message.ReplaceLineEndings(" ")} | - | - | FAIL |";
        }
    }

    /// <summary>Each capture of the script, with the ticks before it and the camera that draws it.</summary>
    private static IEnumerable<(string Name, long Tick, string Camera)> CapturesOf(Script script)
    {
        long tick = 0;
        string camera = "spacewalker";
        foreach (ScriptStep step in script.Steps)
        {
            switch (step)
            {
                case RunStep { Ticks: var ticks }:
                    tick += ticks;
                    break;
                case CameraStep cameraStep:
                    camera = cameraStep.Entity;
                    break;
                case CaptureStep capture:
                    yield return (capture.Name, tick, camera);
                    break;
            }
        }
    }

    private static string Vector(Double3 v, string format) =>
        $"({v.X.ToString(format, CultureInfo.InvariantCulture)}, {v.Y.ToString(format, CultureInfo.InvariantCulture)}, {v.Z.ToString(format, CultureInfo.InvariantCulture)})";

    private static string Invariant(FormattableString text) => text.ToString(CultureInfo.InvariantCulture);
}
