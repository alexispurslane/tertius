using Tertius.Demo;
using Tertius.Simulation;
using SpinStation = Tertius.Demo.DemoScenes.SpinStation;

namespace Tertius.Host.Tests;

/// <summary>
/// One headless run of the checked-in script scripts/spin-station.json in the scene spin-station (see
/// <see cref="SpinStation"/>), with the state of the station, the person and the crates before every tick. The station
/// turns in place about its +Y, in a root frame without a field whose axes are inertial space. The person stands on the
/// ring's floor, walks spinward and then antispinward, jumps and lands, picks up a crate and throws it. Another crate drops
/// from rest in the station's axes at the start. Every test of the class reads that one run.
/// </summary>
public sealed class SpinStationRun : CheckedInRun
{
    public SpinStationRun() : this([]) { }

    private SpinStationRun(List<SpinStationSample> samples) : base("spin-station", (world, scene) => samples.Add(SpinStationSample.Take(world, scene)))
    {
        Samples = samples;
    }

    /// <summary>The state after each tick of the run, from the start to the last tick, in order: sample n is the state after n ticks.</summary>
    internal IReadOnlyList<SpinStationSample> Samples { get; }
}

/// <summary>A body of the scene after a tick.</summary>
/// <param name="Inertial">Its place in inertial space, the axes of the station's parent frame, in meters.</param>
/// <param name="InStation">Its place in the station's coordinates, in meters.</param>
/// <param name="VelocityInStation">Its velocity relative to the station, in the station's axes, in m/s.</param>
internal readonly record struct SpinStationBody(Double3 Inertial, Double3 InStation, Double3 VelocityInStation)
{
    public static SpinStationBody Take(World world, Entity entity, FrameHandle space, FrameHandle station) =>
        new(world.PoseIn(entity, space).Position, world.PoseIn(entity, station).Position, world.VelocityIn(entity, station));
}

/// <summary>The scene after a number of ticks.</summary>
/// <param name="Tick">The number of completed ticks.</param>
/// <param name="Time">The world's time, in seconds.</param>
/// <param name="Station">The station's pose in inertial space.</param>
/// <param name="Person">The person.</param>
/// <param name="Standing">Whether physics stood the person on walkable ground in its last step.</param>
/// <param name="Dropped">The crate that drops at the start.</param>
/// <param name="Thrown">The crate that the person threw, from the tick that spawned it, or null before.</param>
internal sealed record SpinStationSample(long Tick, double Time, DPose Station, SpinStationBody Person, bool Standing, SpinStationBody Dropped,
    SpinStationBody? Thrown)
{
    private static readonly List<Entity> Live = [];

    public static SpinStationSample Take(World world, SceneInstance scene)
    {
        FrameHandle station = world.GetOwnedFrame(scene["station"]);
        FrameHandle space = world.Frames.Parent(station);
        Entity person = scene["person"];
        // The thrown crate is the one crate that the scene did not spawn.
        Live.Clear();
        world.CopyLiveEntities(Live);
        Entity thrown = Live.FirstOrDefault(e => world.GetComponent<Crate>(e) != null && !scene.TryGetEntity(scene.IdOf(e), out _));
        return new SpinStationSample(world.TickNumber, world.Time, world.Frames.Relative(station, space),
            SpinStationBody.Take(world, person, space, station), world.IsStanding(person), SpinStationBody.Take(world, scene["dropped-crate"], space, station),
            thrown.IsNull ? null : SpinStationBody.Take(world, thrown, space, station));
    }
}

/// <summary>
/// The checked-in run of the spin station against the expected paths of its bodies (docs/plans/spin-gravity.md,
/// "Acceptance tests", the line for ter-n44b). The expected path of a free body is its inertial line with Jolt's damping
/// relative to the station's axes ("Jolt's damping in the shadow"), worked out from inertial space and the station's poses,
/// never from the turns that the engine derives. Angles follow <see cref="RevolvedSolid"/>: θ runs from +X toward -Z, the
/// way the station turns, so spinward is the way of growing θ.
/// </summary>
[Collection(GpuCollection.Name)]
public sealed class SpinStationSliceTests(SpinStationRun run) : IClassFixture<SpinStationRun>
{
    private IReadOnlyList<SpinStationSample> Samples => run.Samples;

    private static readonly double Omega = SpinStation.Rate;

    /// <summary>The angle of one facet of the floor, in radians.</summary>
    private static readonly double Facet = Math.Tau / SpinStation.Segments;

    /// <summary>The length of a tick, in seconds.</summary>
    private double Dt => run.World.DeltaTime;

    /// <summary>The sample of the state at the dump <paramref name="name"/>.</summary>
    private int At(string name) => (int)run.Result.Dump(name).Tick;

    /// <summary>The angle θ of a place in the station's coordinates, from 0 to 2π.</summary>
    private static double Theta(Double3 place)
    {
        double theta = Math.Atan2(-place.Z, place.X);
        return theta < 0 ? theta + Math.Tau : theta;
    }

    /// <summary>The direction from the axis at <paramref name="place"/>, in the station's plane y = 0.</summary>
    private static Double3 Outward(Double3 place) => new Double3(place.X, 0, place.Z).Normalized();

    /// <summary>The outward unit normal of the facet of the floor under <paramref name="place"/>, in the station's axes.</summary>
    private static Double3 FacetNormal(Double3 place)
    {
        double middle = (Math.Floor(Theta(place) / Facet) + 0.5) * Facet;
        return new Double3(Math.Cos(middle), 0, -Math.Sin(middle));
    }

    /// <summary>How far the facet under its middle lies from the axis: the floor's facets lie inside its circle.</summary>
    private static readonly double FacetDistance = SpinStation.FloorRadius * Math.Cos(Facet / 2);

    /// <summary>
    /// The first sample n at or after <paramref name="from"/> whose step left the inertial straight line through samples
    /// n − 2 and n − 1 by more than 0.1 mm: the step in which something touched the body. −1 if there is none.
    /// </summary>
    private static int FirstConstrainedStep(IReadOnlyList<Double3> places, int from)
    {
        for (int n = Math.Max(from, 2); n < places.Count; n++)
            if ((places[n] - places[n - 1] * 2 + places[n - 2]).Length > 1e-4)
                return n;
        return -1;
    }

    /// <summary>
    /// When the inertial line X(s) = <paramref name="start"/> + <paramref name="velocity"/> s, which starts at the station
    /// pose <paramref name="at"/>, first brings a point onto the plane of <paramref name="normal"/> at
    /// <paramref name="distance"/> from the axis, from the side of the axis, after <paramref name="from"/> s and before
    /// <paramref name="until"/> s, while the station turns steadily at <see cref="Omega"/> about +Y: the time after the
    /// start and the place in the station's coordinates.
    /// </summary>
    private static (double Time, Double3 Place) Landing(DPose at, Double3 start, Double3 velocity, Double3 normal, double distance,
        double from, double until)
    {
        Double3 InStation(double s)
        {
            DQuaternion rotation = DQuaternion.FromAxisAngle(Double3.UnitY, Omega * s) * at.Rotation;
            return new DPose(at.Position, rotation).InverseTransformPoint(start + velocity * s);
        }
        double Gap(double s) => Double3.Dot(InStation(s), normal) - distance;
        const double step = 1e-4;
        double lo = from, hi = from + step;
        while (hi < until && !(Gap(lo) < 0 && Gap(hi) >= 0))
        {
            lo = hi;
            hi += step;
        }
        for (int i = 0; i < 100; i++)
        {
            double mid = (lo + hi) / 2;
            if (Gap(mid) < 0)
                lo = mid;
            else
                hi = mid;
        }
        double time = (lo + hi) / 2;
        return (time, InStation(time));
    }

    /// <summary>
    /// The expected path of a free body in the station without a field: its inertial line, with Jolt's linear damping
    /// relative to the station's axes (docs/plans/spin-gravity.md, "Jolt's damping in the shadow"), as
    /// <c>SpinStationBodyTests.ExpectedPath</c> of the Physics tests has it. In each step the chord of the body in the
    /// station's axes is the chord that keeps it on its inertial line, times Jolt's damping factor max(0, 1 − c dt), which
    /// Jolt computes in floats. Without damping it is the inertial line. The path starts at <paramref name="place"/>, which
    /// the body reached by the move <paramref name="lastMove"/> over the step before, both in inertial space.
    /// </summary>
    private sealed class ExpectedPath(Double3 place, float damping, float dt, Double3 lastMove)
    {
        private readonly double factor = MathF.Max(0f, 1f - damping * dt);
        // The body's places in inertial space at the ends of the last two steps.
        private Double3 previous = place - lastMove;

        public Double3 Place { get; private set; } = place;

        /// <summary>Moves the body over one step, in which the station went from <paramref name="start"/> to <paramref name="end"/>.</summary>
        public void Step(in DPose start, in DPose end)
        {
            Double3 from = start.InverseTransformPoint(Place);
            Double3 free = end.InverseTransformPoint(2 * Place - previous);
            previous = Place;
            Place = end.TransformPoint(from + (free - from) * factor);
        }
    }

    // ---- The walk ----

    /// <summary>
    /// The samples of the walk that starts at the dump <paramref name="start"/> and ends at the dump
    /// <paramref name="end"/>. A key posted after a dump joins the sync point of the next tick, the tick after that reads
    /// it, and the step of the tick after that walks, so the walk moves the person in the steps that end at samples
    /// start + 3 to end + 2.
    /// </summary>
    private (int First, int Last) Walk(string start, string end) => (At(start) + 3, At(end) + 2);

    [Theory]
    [InlineData("spinward", "antispinward", 1)]
    [InlineData("antispinward", "stop", -1)]
    public void ThePersonStandsInEveryTickOfItsWalkAndMovesAlongTheFloorAtItsAskedSpeed(string start, string end, int way)
    {
        // The ground holds a standing character against the whole acceleration of its frame, so the walk moves the person
        // at its asked speed along the floor in every step, also across the joints of the facets, where it passes from one
        // facet's plane to the next. The direction along the floor is the circle's, to within the half facet of 0.7 degrees
        // by which a facet's plane leaves it.
        (int first, int last) = Walk(start, end);
        double worstSpeed = 0;
        for (int n = first; n <= last; n++)
        {
            SpinStationSample sample = Samples[n];
            sample.Standing.Should().BeTrue($"the person stands in the step that ends at sample {n}");
            Double3 velocity = sample.Person.VelocityInStation;
            Double3 along = new Double3(-Math.Sin(Theta(sample.Person.InStation)), 0, -Math.Cos(Theta(sample.Person.InStation)));
            (way * Double3.Dot(velocity, along)).Should().BeGreaterThan(0, $"the person walks {start} at sample {n}");
            worstSpeed = Math.Max(worstSpeed, Math.Abs(velocity.Length - PersonControl.WalkSpeed));
        }
        TestContext.Current.TestOutputHelper?.WriteLine($"{start}: {last - first + 1} steps, worst speed error {worstSpeed:E3} m/s");
        worstSpeed.Should().BeLessThan(SpeedBound, "the person walks at its asked speed along the floor");
    }

    /// <summary>
    /// How far the person's speed along the floor may lie from its asked speed, in m/s: the bound of the walk in
    /// SpinStationCharacterTests on a flat floor. Measured on the facets: 6.5e-6 m/s both ways, so no joint slows the walk.
    /// </summary>
    private const double SpeedBound = 1e-4;

    // ---- The weight of the walk ----

    /// <summary>
    /// The mean acceleration toward the axis in inertial space of the person over the walk from the dump
    /// <paramref name="start"/> to the dump <paramref name="end"/>, which only the floor's push gives: the station has no
    /// field. Each value is the second difference of the person's inertial places over two steps of the walk, along the
    /// direction from its middle place to the axis. Also its peak, and the person's mean radius and speed along the floor.
    /// </summary>
    private (double Mean, double Peak, double Radius, double Speed) Inward(string start, string end)
    {
        (int first, int last) = Walk(start, end);
        double sum = 0, peak = 0, radius = 0, speed = 0;
        int count = 0;
        for (int n = first + 1; n <= last; n++)
        {
            Double3 acceleration = (Samples[n].Person.Inertial - 2 * Samples[n - 1].Person.Inertial + Samples[n - 2].Person.Inertial) / (Dt * Dt);
            Double3 middle = Samples[n - 1].Person.Inertial - Samples[n - 1].Station.Position;
            double inward = -Double3.Dot(acceleration, Outward(middle));
            sum += inward;
            peak = Math.Max(peak, inward);
            Double3 inStation = Samples[n - 1].Person.InStation;
            radius += Math.Sqrt(inStation.X * inStation.X + inStation.Z * inStation.Z);
            speed += Samples[n].Person.VelocityInStation.Length;
            count++;
        }
        return (sum / count, peak, radius / count, speed / count);
    }

    /// <summary>
    /// How far the mean inward acceleration of a walk may lie from (ω r ± v)² / r, in m/s². At the walk's 3 m/s on the
    /// floor's 99.1 m, the part v² / r that the turns of the facets add is 0.091 m/s², so a bound below half of it tells
    /// (ω r ± v)² / r from ω² r ± 2 ω v. The mean telescopes: it is the turn of the walk's velocity at the joints that the
    /// window holds, and a window can hold one joint more or less than its share, v (2π / 256) / T, 0.018 m/s² for 4 s.
    /// Measured: 0.0015 m/s² from (ω r ± v)² / r both ways, and 0.092 m/s² from ω² r ± 2 ω v.
    /// </summary>
    private const double WeightBound = 0.03;

    [Theory]
    [InlineData("spinward", "antispinward", 1)]
    [InlineData("antispinward", "stop", -1)]
    public void OverAWalkTheFloorPushesThePersonTowardTheAxisWithTheSquareOfItsInertialSpeedOverItsRadius(string start, string end, int way)
    {
        // The person moves round the axis in inertial space at ω r ± v, so the floor's push gives it (ω r ± v)² / r toward
        // the axis: ω² r and the Coriolis term ± 2 ω v of a flat floor, and v² / r from the facets' turns.
        (double mean, double peak, double radius, double speed) = Inward(start, end);
        double v = way * speed;
        double curved = Math.Pow(Omega * radius + v, 2) / radius, flat = Omega * Omega * radius + 2 * Omega * v;
        TestContext.Current.TestOutputHelper?.WriteLine(
            $"{start}: r {radius:F4} m, v {speed:F5} m/s, mean inward {mean:F5} m/s², (ωr±v)²/r {curved:F5}, ω²r±2ωv {flat:F5}, peak {peak:F4}");

        mean.Should().BeApproximately(curved, WeightBound, "the floor's push holds the whole inertial speed on the circle");
        Math.Abs(mean - flat).Should().BeGreaterThan(WeightBound, "the push holds v² / r more than the flat floor's terms");
    }

    // ---- The jump ----

    [Fact]
    public void TheJumpLandsWhereTheInertialLineOfItsTakeoffMeetsTheFloor()
    {
        // The jump is an instant velocity of PersonControl.JumpSpeed along up, relative to the ground, where the step of the
        // jump starts: the sample after the tick that reads Space, the second tick after the dump (see Walk). In inertial
        // space the person then moves on the straight line through its place there with that velocity plus the velocity of
        // the floor under it, ω × x, until the plane of the facet under it, 0.9 m nearer the axis, meets its center again.
        // The landing that the flight shows is the crossing of that plane by the straight line through its last two free
        // places. The bound is the spike's 7 µm, and the run measured 6e-8 to 8e-8 m.
        int jump = At("jump") + 2;
        SpinStationSample start = Samples[jump];
        start.Standing.Should().BeTrue("the person stands where it jumps");
        Double3 x0 = start.Person.InStation;
        Double3 normal = FacetNormal(x0);
        Double3 velocity = start.Station.Rotation.Rotate(-Outward(x0) * PersonControl.JumpSpeed)
                           + Double3.Cross(new Double3(0, Omega, 0), start.Person.Inertial - start.Station.Position);
        double plane = FacetDistance - SpinStation.StandingHeight;
        (double time, Double3 place) = Landing(start.Station, start.Person.Inertial, velocity, normal, plane, 1e-3, 3);

        List<Double3> places = [.. Samples.Select(s => s.Person.Inertial)];
        int landed = FirstConstrainedStep(places, jump + 3);
        landed.Should().BeGreaterThan(jump + 3, "the person flew and landed");
        SpinStationSample last = Samples[landed - 1];
        (double measuredTime, Double3 measured) = Landing(last.Station, last.Person.Inertial, (last.Person.Inertial - Samples[landed - 2].Person.Inertial) / Dt,
            normal, plane, -Dt, 3 * Dt);

        TestContext.Current.TestOutputHelper?.WriteLine(
            $"jump: lands after {time:F6} s at {place}, measured {last.Time - start.Time + measuredTime:F6} s at {measured}, off by {Double3.Distance(measured, place):E2} m; rests {Double3.Distance(Samples[landed + 30].Person.InStation, place):E2} m from it");
        FacetNormal(place).Should().Be(normal, "the person lands on the facet of its takeoff");
        Double3.Distance(measured, place).Should().BeLessThan(7e-6, "the person flies on the inertial line of its takeoff");
        (last.Time - start.Time + measuredTime).Should().BeApproximately(time, 1e-4);
        Samples.Skip(landed + 2).Take(30).Should().OnlyContain(s => s.Standing, "it stands again after it lands");
        // The ground holds a standing character, so the person stops where it lands: measured 3.6e-5 to 8.4e-5 m from the
        // place.
        Double3.Distance(Samples[landed + 30].Person.InStation, place).Should().BeLessThan(1e-3, "the person rests where its line met the floor");
    }

    // ---- The crates ----

    [Fact]
    public void TheDroppedCrateLandsWhereAndWhenItsExpectedPathSays()
    {
        // The crate spawns at rest in the station's axes with the scene, and the station spawns with its first motion
        // intent, so from its first step the crate moves on the straight line through its place at the inertial velocity
        // ω × r, with Jolt's default damping relative to the station's axes. At rest relative to the station, the chord
        // rule adds no half step of the damping. The crate's face toward the floor stays parallel to the facet under it, so
        // it touches the floor when its center comes within its half size of the facet's plane, in the step in which the
        // expected path crosses that plane.
        BodyDesc body = run.World.GetBody(run.Scene["dropped-crate"])!;
        body.LinearDamping.Should().Be(0.05f, "the dropped crate has Jolt's default damping");
        SpinStationSample release = Samples[0];
        Double3 velocity = Double3.Cross(new Double3(0, Omega, 0), release.Dropped.Inertial - release.Station.Position);
        var path = new ExpectedPath(release.Dropped.Inertial, body.LinearDamping, (float)Dt, velocity * Dt);
        Double3 normal = FacetNormal(release.Dropped.InStation);
        double plane = FacetDistance - SpinStation.CrateHalfSize;

        // The expected path's places in inertial space after each tick, until it has crossed the plane, and the step in
        // which it crosses, at the fraction of the step that the gaps before and after give.
        var expected = new List<Double3> { path.Place };
        int touch = -1;
        double landingTime = double.NaN;
        Double3 landingPlace = default;
        for (int n = 1; n < Samples.Count && touch < 0; n++)
        {
            Double3 before = Samples[n - 1].Station.InverseTransformPoint(path.Place);
            path.Step(Samples[n - 1].Station, Samples[n].Station);
            expected.Add(path.Place);
            Double3 after = Samples[n].Station.InverseTransformPoint(path.Place);
            double gapBefore = Double3.Dot(before, normal) - plane, gapAfter = Double3.Dot(after, normal) - plane;
            if (gapAfter >= 0)
            {
                double fraction = -gapBefore / (gapAfter - gapBefore);
                landingTime = Samples[n - 1].Time + fraction * Dt - release.Time;
                landingPlace = before + (after - before) * fraction;
                touch = n;
            }
        }
        // The first step that leaves the expected path by more than 0.1 mm: the step in which something touched the crate.
        int measuredTouch = Enumerable.Range(1, expected.Count - 1).FirstOrDefault(n => Double3.Distance(Samples[n].Dropped.Inertial, expected[n]) > 1e-4, -1);
        double worst = Enumerable.Range(0, touch).Max(n => Double3.Distance(Samples[n].Dropped.Inertial, expected[n]));
        Double3 rest = Samples[measuredTouch + 60].Dropped.InStation;
        TestContext.Current.TestOutputHelper?.WriteLine(
            $"drop: worst {worst:E2} m off its path before it lands; expected to land at {landingTime:F6} s at {landingPlace}, in step {touch}; touched in step {measuredTouch}; rests at {rest}, {Double3.Distance(rest, landingPlace):F4} m from the landing place, {(Theta(rest) - Theta(release.Dropped.InStation)) * SpinStation.FloorRadius:F4} m spinward of its release");

        touch.Should().BeGreaterThan(10, "the crate falls for a while");
        worst.Should().BeLessThan(1e-5, "the crate follows its expected path until it lands");
        measuredTouch.Should().Be(touch, "the floor stops the crate in the step in which its expected path meets the floor");
        Double3.Dot(rest, normal).Should().BeApproximately(plane, 2e-3, "the crate rests on the floor's facet");
        Double3.Distance(rest, landingPlace).Should().BeLessThan(DropRestBound, "the crate rests where its expected path met the floor");
    }

    /// <summary>
    /// How far the dropped crate may rest from where its expected path met the floor, in meters. It lands at 6 m/s
    /// relative to the floor, nearly along the facet's normal, and friction stops what slides: measured 3.1 to 3.3 mm.
    /// </summary>
    private const double DropRestBound = 0.01;

    [Fact]
    public void TheThrownCrateFollowsTheExpectedPathOfItsInstantVelocityUntilItTouchesTheFloor()
    {
        // Carry spawns the crate with an instant velocity relative to the station: the person's velocity plus the throw's.
        // Before its first step the crate's velocity reads that spawn velocity. In inertial space it then moves on the
        // straight line through its place there with that velocity, turned into inertial axes, plus the velocity of the
        // station's point there, ω × x. A crate of Carry has no damping, so its expected path is that line.
        int spawn = Enumerable.Range(0, Samples.Count).First(n => Samples[n].Thrown != null);
        SpinStationBody start = Samples[spawn].Thrown!.Value;
        Double3 velocity = Samples[spawn].Station.Rotation.Rotate(start.VelocityInStation)
                           + Double3.Cross(new Double3(0, Omega, 0), start.Inertial - Samples[spawn].Station.Position);
        start.VelocityInStation.Length.Should().BeGreaterThan(4, "the person charged the throw for half a second");

        List<Double3> places = [.. Samples.Skip(spawn).Select(s => s.Thrown!.Value.Inertial)];
        int touch = FirstConstrainedStep(places, 2);
        touch.Should().BeGreaterThan(20, "the crate flies for a while");
        double worst = 0;
        for (int n = 0; n < touch; n++)
            worst = Math.Max(worst, Double3.Distance(places[n], start.Inertial + velocity * (Samples[spawn + n].Time - Samples[spawn].Time)));
        Double3 expected = Samples[spawn + touch].Station.InverseTransformPoint(start.Inertial + velocity * (Samples[spawn + touch].Time - Samples[spawn].Time));
        double gap = FacetDistance - Double3.Dot(expected, FacetNormal(expected));
        TestContext.Current.TestOutputHelper?.WriteLine(
            $"throw: {start.VelocityInStation} m/s in the station, flies {touch} steps, worst {worst:E2} m off its line; at the touch its line is {gap:F4} m from the floor");

        worst.Should().BeLessThan(1e-5, "the thrown crate follows the inertial line of its instant velocity");
        // A crate of 0.4 m reaches at most its bounding radius toward the floor, and its line moves it less than a step's
        // way farther in the step of the touch.
        gap.Should().BeLessThan(SpinStation.CrateHalfSize * Math.Sqrt(3) + velocity.Length * Dt, "what the crate touches is the floor");
    }

    [Fact]
    public void TheRunRaisesNoErrorsAndCapturesItsViews()
    {
        run.World.Errors.Should().BeEmpty();
        run.Result.Captures.Should().Contain(["drop", "standing", "walk-spinward", "walk-antispinward", "jump", "landed", "throw", "thrown-landed", "ring"]);
    }
}
