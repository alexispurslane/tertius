using System.Numerics;
using Tertius.Simulation;
using static Tertius.Testing.WorldTesting;

namespace Tertius.Physics.Tests;

/// <summary>
/// The landing case of numeric frames inside a turning frame, on the World and the real Jolt backend
/// (docs/plans/spin-gravity.md, "The landing case"). The spike found one failure: a person crossed the middle of a spinning
/// station at 40 m/s, got a falling frame five ticks before it landed, landed 0.7 m deep in the floor and went through it.
/// A free path through the axis at 40 m/s meets the floor at sqrt(40² + 31.3²) = 50.8 m/s relative to the station, past the
/// 50 m/s from which a group with a body that thrusts gets a falling frame.
/// <para>
/// The station of the tests is a frame without a body and without a field, which turns in place in a root frame without a
/// field about its +Y at 0.31321 rad/s, the rate that gives 9.81 m/s² at 100 m, from its first step. Its floor is a ring of
/// 64 static slabs whose faces toward the axis lie 100 m from it. Each body starts 60 m from the axis on the station's -X,
/// with the velocity in the station whose inertial line runs through the axis at 40 m/s.
/// </para>
/// <para>
/// Since ticket ter-dhkf, the merge test of numeric frames links every group to every static body within the reach of
/// their bounding spheres, grown by their relative speed times the lookahead time, and the group takes the static body's
/// home (docs/plans/numeric-frames.md, "Anchors"). A slab's bounding sphere has a radius of 11 m, so a body that falls in a
/// numeric frame joins the station's own system about 11 m before it can touch the floor, with any lookahead time, and
/// Jolt sweeps it against the real floor there. These tests pin that.
/// </para>
/// </summary>
public sealed class SpinStationLandingTests
{
    private const double StationRate = 0.31321;
    private const double FloorRadius = 100;
    private const int FloorFaces = 64;

    /// <summary>
    /// The largest distance between a body's places with numeric frames and without them, before it lands, in m. Both runs
    /// are free motion on the same chords until the landing, which Jolt rounds differently in different systems, so they
    /// differ by about a float step of a place 100 m from the axis, 7.6e-6 m.
    /// </summary>
    private const double BeforeLanding = 3e-5;

    /// <summary>
    /// <see cref="BeforeLanding"/> for a walking person, in m. The update of a walking character keeps its chord only as
    /// Jolt's float velocity, which it rounds in every step, where a dynamic body's update follows a double shadow of its
    /// chord. At 40 m/s to 50 m/s in the station's own system that rounding moved the person 2.7e-5 m from its exact
    /// inertial line in the 238 ticks before it landed, and a crate 9.4e-6 m. In the falling frame, where the person moves at
    /// 0.3 m/s, both strayed 1.0e-5 m. So the two runs of the person differed by 3.2e-5 m.
    /// </summary>
    private const double PersonBeforeLanding = 6e-5;

    private static readonly Double3 Omega = new(0, StationRate, 0);

    /// <summary>A person: a capsule with a half height of 0.6 m and a radius of 0.3 m, of 70 kg.</summary>
    private static readonly BodyDesc Person = new(Shape: new CapsuleShapeDesc(0.6f, 0.3f), Motion: BodyMotion.Character,
        Mass: 70);

    /// <summary>An undamped crate, a box of 1 m and 10 kg, so that its path is its inertial line until it lands.</summary>
    private static readonly BodyDesc Crate = new(Shape: new BoxShapeDesc(new(0.5f)), Mass: 10, LinearDamping: 0,
        AngularDamping: 0);

    /// <summary>
    /// A force on the crate of 1 mN along the station's +Z, 1e-4 m/s² on 10 kg, above the 1e-3/60 m/s² from which a tick of
    /// force counts as thrust. After half a second of it the crate thrusts, so it gets a falling frame from 50 m/s, as a
    /// person does. It moves the crate by 1.3 mm in five seconds.
    /// </summary>
    private static readonly Vector3 Thrust = new(0, 0, 1e-3f);

    /// <summary>A slab of the floor, 1 m thick, 20 m wide along +Y, and as long as a face along the ring with 5 cm of overlap with its neighbors.</summary>
    private static readonly BoxShapeDesc Slab = new(new(0.5f, 10,
        (float)(FloorRadius * Math.Tan(Math.PI / FloorFaces)) + 0.05f));

    private sealed record Station(FrameHandle Space, FrameHandle Frame);

    /// <summary>What the station's floor is.</summary>
    public enum FloorKind
    {
        /// <summary>A ring of <see cref="FloorFaces"/> static slabs in a station without a body.</summary>
        Slabs,

        /// <summary>
        /// The interior of the station's owner: a ring mesh of <see cref="FloorFaces"/> faces, as ticket ter-n44b gives a
        /// station. The owner's kinematic hull, which turns in the root frame, is the same ring of slabs as one compound.
        /// </summary>
        Interior
    }

    /// <summary>
    /// The station and its floor: <see cref="FloorFaces"/> static slabs, 1 m thick and 20 m wide along +Y, or the owner's
    /// interior mesh with the same faces toward the axis.
    /// </summary>
    private static Station SpawnStation(World world, FloorKind floor = FloorKind.Slabs)
    {
        FrameHandle space = world.CreateRootFrame(new(FrameBounds.Sphere(1e6f)));
        var desc = new SpawnDesc
        {
            Frame = space, AsFrame = new(FrameBounds.Sphere(150)),
            Body = floor == FloorKind.Interior
                ? new BodyDesc(
                    Shape: new CompoundShapeDesc([
                        .. Enumerable.Range(0, FloorFaces).Select(i => new CompoundChild(Slab, SlabPose(i).ToPose()))
                    ]),
                    Motion: BodyMotion.Kinematic, InteriorShape: RingMesh())
                : null
        };
        Entity owner =
            world.Spawn(desc.Spinning(world, new(Tilt: DQuaternion.Identity, Rate: StationRate), firstMove: true));
        FrameHandle frame = world.GetOwnedFrame(owner);
        if (floor == FloorKind.Slabs)
            for (int i = 0; i < FloorFaces; i++)
                world.Spawn(new()
                    { Frame = frame, Pose = SlabPose(i), Body = new(Shape: Slab, Motion: BodyMotion.Static) });
        return new(space, frame);
    }

    /// <summary>The pose of slab <paramref name="i"/> in the station, with its face toward the axis on the face of
    /// <see cref="FaceNormal"/>.</summary>
    private static DPose SlabPose(int i) =>
        new(FaceNormal(i) * (FloorRadius + 0.5), DQuaternion.FromAxisAngle(new(0, 1, 0), -FaceAngle(i)));

    /// <summary>
    /// The ring mesh of the floor: one quad of two triangles for each face, 20 m wide along +Y, whose middle lies
    /// <see cref="FloorRadius"/> from the axis on <see cref="FaceNormal"/>, wound so that its front faces the axis.
    /// </summary>
    private static MeshShapeDesc RingMesh()
    {
        double corner = FloorRadius / Math.Cos(Math.PI / FloorFaces);
        var vertices = new List<Vector3>();
        var indices = new List<int>();
        for (int i = 0; i < FloorFaces; i++)
        {
            double angle = FaceAngle(i) - Math.PI / FloorFaces;
            vertices.Add(new((float)(corner * Math.Cos(angle)), -10, (float)(corner * Math.Sin(angle))));
            vertices.Add(new((float)(corner * Math.Cos(angle)), 10, (float)(corner * Math.Sin(angle))));
        }

        for (int i = 0; i < FloorFaces; i++)
        {
            int low = 2 * i, high = low + 1, nextLow = 2 * ((i + 1) % FloorFaces), nextHigh = nextLow + 1;
            indices.AddRange([low, nextLow, high, nextLow, nextHigh, high]);
        }

        return new([.. vertices], [.. indices]);
    }

    /// <summary>The angle of face <paramref name="i"/> about the station's +Y, from its +X toward its +Z, in radians.</summary>
    private static double FaceAngle(int i) => 2 * Math.PI * i / FloorFaces;

    /// <summary>The direction from the axis to the middle of face <paramref name="i"/>: the face's normal, into its slab.</summary>
    private static Double3 FaceNormal(int i) => new(Math.Cos(FaceAngle(i)), 0, Math.Sin(FaceAngle(i)));

    /// <summary>
    /// How deep the body at <paramref name="pose"/> in the station reaches into the floor, in meters: the largest distance,
    /// over the three faces nearest to it, from the face toward the axis to the body's farthest point along that face's
    /// normal. Negative while the body is clear of the floor.
    /// </summary>
    private static double Depth(DPose pose, bool person)
    {
        double angle = Math.Atan2(pose.Position.Z, pose.Position.X);
        int nearest = (int)Math.Round(angle / (2 * Math.PI) * FloorFaces);
        double deepest = double.NegativeInfinity;
        for (int i = nearest - 1; i <= nearest + 1; i++)
        {
            Double3 normal = FaceNormal((i % FloorFaces + FloorFaces) % FloorFaces);
            Double3 local = pose.Rotation.Conjugate().Rotate(normal);
            // The support of the body's shape along the normal: the capsule's radius plus its half height along its axis, or
            // the box's half sides along its axes.
            double support = person
                ? 0.3 + 0.6 * Math.Abs(local.Y)
                : 0.5 * (Math.Abs(local.X) + Math.Abs(local.Y) + Math.Abs(local.Z));
            deepest = Math.Max(deepest, Double3.Dot(pose.Position, normal) + support - FloorRadius);
        }

        return deepest;
    }

    /// <summary>
    /// A body <paramref name="radius"/> m from the axis on the station's -X, <paramref name="height"/> m along its +Y, with the
    /// velocity in the station whose inertial line runs through the axis at 40 m/s: 40 m/s along +X less the station's
    /// velocity at the place, ω × x. That is 44.2 m/s in the station at 60 m, and 50.4 m/s at 98 m. With
    /// <paramref name="thrusts"/> it has the force <see cref="Thrust"/> in every tick.
    /// </summary>
    private static Entity SpawnCrosser(World world, Station station, BodyDesc body, double radius = 60,
        double height = 0,
        bool thrusts = false, IComponent? log = null)
    {
        var place = new Double3(-radius, height, 0);
        var pilot = new Pilot();
        if (thrusts)
            pilot.EveryTick = handle => handle.AddForce(Thrust);
        return world.Spawn(new()
        {
            Frame = station.Frame,
            Pose = new(place, DQuaternion.Identity),
            LinearVelocity = new Double3(40, 0, 0) - Double3.Cross(Omega, place),
            Body = body,
            Components = log is null ? [pilot] : [pilot, log]
        });
    }

    /// <summary>What a run of one body across the station shows at one sync point.</summary>
    /// <param name="Place">The body's place in the station.</param>
    /// <param name="Depth">How deep the body reaches into the floor (see <see cref="Depth"/>).</param>
    /// <param name="Falling">Whether the body's physics home is a numeric frame.</param>
    /// <param name="Speed">The body's speed relative to the station, in m/s.</param>
    /// <param name="HomeSpeed">The body's speed relative to its physics home, in m/s.</param>
    private readonly record struct Moment(Double3 Place, double Depth, bool Falling, double Speed, double HomeSpeed);

    /// <summary>What a run of one body across the station shows, at each sync point.</summary>
    private sealed record Crossing(List<Moment> Moments)
    {
        /// <summary>The index of the first sync point at which the body touches the floor, within 5 cm, or -1.</summary>
        public int Landing => Moments.FindIndex(moment => moment.Depth > -0.05);

        /// <summary>The deepest that the body reaches into the floor, in m.</summary>
        public double Deepest => Moments.Max(moment => moment.Depth);

        /// <summary>
        /// The largest distance between the body's places in this run and in <paramref name="other"/>, in m, over the sync
        /// points from <paramref name="from"/> up to <paramref name="to"/>, without it.
        /// </summary>
        public double WorstDistance(Crossing other, int from, int to) =>
            Enumerable.Range(from, to - from).Max(i => Double3.Distance(Moments[i].Place, other.Moments[i].Place));
    }

    /// <summary>
    /// Runs one body across the station under <paramref name="rules"/> for <paramref name="seconds"/> s, from
    /// <paramref name="radius"/> m from the axis (see <see cref="SpawnCrosser"/>).
    /// </summary>
    private static Crossing Cross(NumericFrameOptions rules, BodyDesc body, bool thrusts, double seconds,
        double radius = 60,
        FloorKind floor = FloorKind.Slabs)
    {
        using World world = NewWorld(backend: out _, debugChecks: false,
            configure: options => options.NumericFrames = rules);
        Station station = SpawnStation(world, floor);
        Entity crosser = SpawnCrosser(world: world, station: station, body: body, radius: radius, thrusts: thrusts);
        var crossing = new Crossing([]);
        for (int tick = 0; tick < seconds * world.Options.TickRate; tick++)
        {
            world.Tick();
            DPose pose = world.PoseIn(crosser, station.Frame);
            FrameHandle home = world.PhysicsHomeOf(crosser);
            crossing.Moments.Add(new(Place: pose.Position, Depth: Depth(pose, body.Motion == BodyMotion.Character),
                Falling: world.Frames.IsNumeric(home),
                Speed: world.VelocityIn(crosser, station.Frame).Length,
                HomeSpeed: world.VelocityIn(crosser, home).Length));
            world.GetFrame(crosser).Should().Be(station.Frame, "the body stays inside the station");
        }

        world.Errors.Should().BeEmpty();
        return crossing;
    }

    // ---- The landing case ----

    /// <summary>
    /// The person leaves the far side of the floor, 98 m from the axis and 1.7 m above the floor, at 50.4 m/s relative to the
    /// station, lands on the floor and stays on it, with the default numeric frames and without numeric frames, on a floor of
    /// static slabs or on the station owner's interior mesh. With the default rules a person, who always thrusts, gets a
    /// falling frame from 50 m/s, but the merge test's reach of 2 s at its speed links it to the slabs, or to the interior,
    /// all the way across the station, so it stays in the station's own system, and Jolt's sweep of the person meets the real
    /// floor. In the spike, before ter-dhkf, it landed 0.7 m deep. Without the link it falls through the floor.
    /// <para>
    /// A walking character in the air feels the rotation terms (ticket ter-1b21), so the person follows its inertial line
    /// through the axis at 40 m/s and meets the floor above 50 m/s, the spike's crossing, which
    /// <see cref="APersonWhoLeavesTheFloorAt50MetersPerSecondCrossesNearTheAxisAndMeetsTheFarFloorAbove50MetersPerSecond"/>
    /// pins. Before that ticket it moved on a straight line in the station's axes at its spawn velocity: a chord 59.7 m from
    /// the axis.
    /// </para>
    /// </summary>
    [Theory]
    [InlineData(FloorKind.Slabs, true)]
    [InlineData(FloorKind.Slabs, false)]
    [InlineData(FloorKind.Interior, true)]
    [InlineData(FloorKind.Interior, false)]
    public void APersonWhoLeavesTheFloorAt50MetersPerSecondAcrossTheStationLandsOnTheFarSideAndStaysOnIt(
        FloorKind floor, bool numericFrames)
    {
        Crossing crossing = Cross(rules: numericFrames ? new() : NumericFrameTesting.WithoutNumericFrames, body: Person,
            thrusts: false, seconds: 7, radius: 98, floor: floor);

        int landing = crossing.Landing;
        landing.Should().BeGreaterThan(0, "the person reaches the floor");
        crossing.Deepest.Should().BeLessThan(0.05, "the person never sinks into the floor; in the spike it sank 0.7 m");
        crossing.Moments.Skip(landing).Should().OnlyContain(moment => moment.Depth > -0.25,
            "the person stays on the floor after it lands");
        (crossing.Moments.Count - landing).Should()
            .BeGreaterThan(120, "the run watches the person for 2 s after it lands");
        crossing.Moments[0].Speed.Should().BeGreaterThan(50, "the person moves fast enough for a falling frame");
        crossing.Moments.Should().OnlyContain(moment => !moment.Falling,
            "the merge test links the person to the floor, so it stays in the station's own system");
    }

    /// <summary>
    /// The spike's crossing: a person in the air feels the rotation terms, so the person that leaves the far side of the
    /// floor at 50.4 m/s follows its inertial line, which runs through the axis at 40 m/s, and meets the floor at
    /// sqrt(40² + 31.3²) = 50.8 m/s relative to the station, past the 50 m/s from which a person gets a falling frame. With
    /// numeric frames and without, it passes within 1 m of the axis and is faster than 50 m/s in the tick before it lands.
    /// A person that felt no rotation terms crossed on a straight chord 59.7 m from the axis.
    /// </summary>
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void APersonWhoLeavesTheFloorAt50MetersPerSecondCrossesNearTheAxisAndMeetsTheFarFloorAbove50MetersPerSecond(
        bool numericFrames)
    {
        Crossing crossing = Cross(rules: numericFrames ? new() : NumericFrameTesting.WithoutNumericFrames, body: Person,
            thrusts: false, seconds: 7, radius: 98);

        int landing = crossing.Landing;
        landing.Should().BeGreaterThan(1, "the person reaches the floor");
        double nearest = crossing.Moments.Take(landing).Min(moment =>
            Math.Sqrt(moment.Place.X * moment.Place.X + moment.Place.Z * moment.Place.Z));
        nearest.Should().BeLessThan(1, "the person follows its inertial line through the axis");
        crossing.Moments[landing - 1].Speed.Should()
            .BeGreaterThan(50, "the person meets the floor at the spike's 50.8 m/s");
        TestContext.Current.TestOutputHelper?.WriteLine(
            $"numericFrames={numericFrames}: nearest the axis {nearest:F3} m, "
            + $"speed before the landing {crossing.Moments[landing - 1].Speed:F3} m/s");
    }

    /// <summary>
    /// A crate that thrusts gets a falling frame from 50 m/s, as a person does, and a dynamic body follows its inertial line
    /// in the station, so it crosses the middle at 40 m/s and passes 50 m/s relative to the station 4 m before the floor, the
    /// spike's case. It lands where and when it lands without numeric frames, because the merge test keeps it in the
    /// station's own system up to the floor: before it lands within a float step at its speed, and after it lands within the
    /// contact noise of Jolt's push out of the floor.
    /// </summary>
    [Fact]
    public void ACrateThatThrustsAndCrossesTheMiddleOfTheStationAt40MetersPerSecondLandsAsWithoutNumericFrames()
    {
        Crossing numeric = Cross(rules: new(), body: Crate, thrusts: true, seconds: 5);
        Crossing plain = Cross(rules: NumericFrameTesting.WithoutNumericFrames, body: Crate, thrusts: true, seconds: 5);

        int landing = plain.Landing;
        landing.Should().BeGreaterThan(0, "the crate reaches the floor");
        plain.Moments.Take(landing).Max(moment => moment.Speed).Should().BeGreaterThan(50,
            "the crate passes the speed at which a group that thrusts falls");
        numeric.Landing.Should().Be(landing, "the crate lands at the same sync point");
        // Jolt lets a box that hits a face at 50 m/s overlap it by 7.5 cm for a tick, without numeric frames too, and then
        // pushes it out. The spike's person sank 0.7 m and went through.
        plain.Deepest.Should().BeLessThan(0.1, "Jolt keeps the crate out of the floor in the station's own system");
        numeric.Deepest.Should()
            .BeApproximately(plain.Deepest, 1e-3, "the crate sinks no deeper than without numeric frames");
        double before = numeric.WorstDistance(plain, 0, landing),
            after = numeric.WorstDistance(plain, landing, plain.Moments.Count);
        before.Should().BeLessThan(BeforeLanding, "the crate flies as it does without numeric frames");
        after.Should().BeLessThan(1e-3, "the crate lands and rests as it does without numeric frames");
        numeric.Moments[landing].Falling.Should()
            .BeFalse("the merge test links the crate to the floor, so it lands in the station's own system");
        TestContext.Current.TestOutputHelper?.WriteLine(
            $"thrusting crate: worst distance {before:E2} m before the landing, {after:E2} m after it");
    }

    /// <summary>
    /// Under rules by which every group falls, with no lookahead, a body falls in a numeric frame below the station for most
    /// of its crossing, and joins the station's own system only where its bounding sphere meets a slab's, 11 m from the floor.
    /// It moves as it does in the station's own system all the way, within a float step at its speed before it lands, and
    /// lands there. In the falling frame the origin moves on the inertial line of a free body at its place, and the body feels
    /// only the difference of its rotation terms and the origin's (docs/plans/spin-gravity.md, "The origin of a falling
    /// frame"), so a crate, which feels them, keeps a small velocity relative to its home while it falls: it starts at
    /// 0.21 m/s, half a step of the crate's rotation terms of 25.7 m/s², by which a chord and an instant velocity differ, and
    /// grows by less than 1 mm/s a tick as the crate and the origin drift apart, far below the drain speed of 1 m/s at which
    /// the frame takes the members' velocity over. Without the origin's term it grows by 0.46 m/s a tick. A walking person in
    /// the air feels the rotation terms as a crate does (ticket ter-1b21), so the same bounds hold for it. Before that
    /// ticket it felt none, moved on a straight line in the station's axes, and reached 0.89 m/s relative to its falling
    /// frame.
    /// </summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ABodyThatCrossesTheMiddleOfTheStationInAFallingFrameMovesAsInTheStationsOwnSystemAndLandsThere(
        bool person)
    {
        NumericFrameOptions falls = NumericFrameTesting.EveryGroupFalls with { LookaheadTime = 0 };
        BodyDesc body = person ? Person : Crate;
        Crossing numeric = Cross(rules: falls, body: body, thrusts: false, seconds: 5);
        Crossing plain = Cross(rules: NumericFrameTesting.WithoutNumericFrames, body: body, thrusts: false, seconds: 5);

        int landing = plain.Landing;
        landing.Should().BeGreaterThan(0, "the body reaches the floor");
        numeric.Moments.Take(landing).Count(moment => moment.Falling).Should().BeGreaterThan(landing / 2,
            "the body falls in a numeric frame for most of its crossing");
        numeric.Moments[landing].Falling.Should()
            .BeFalse("the body joins the station's own system before it meets the floor");
        numeric.Landing.Should().Be(landing, "the body lands at the same sync point");
        plain.Deepest.Should().BeLessThan(0.1, "Jolt keeps the body out of the floor in the station's own system");
        numeric.Deepest.Should()
            .BeApproximately(plain.Deepest, 1e-3, "the body sinks no deeper than without numeric frames");
        double before = numeric.WorstDistance(plain, 0, landing),
            after = numeric.WorstDistance(plain, landing, plain.Moments.Count);
        before.Should().BeLessThan(person ? PersonBeforeLanding : BeforeLanding,
            "the body flies as it does without numeric frames");
        after.Should().BeLessThan(1e-3, "the body lands and rests as it does without numeric frames");
        double homeSpeed = numeric.Moments.Where(moment => moment.Falling).Max(moment => moment.HomeSpeed);
        double homeSpeedChange = Enumerable.Range(1, landing - 1)
            .Where(i => numeric.Moments[i].Falling && numeric.Moments[i - 1].Falling)
            .Max(i => Math.Abs(numeric.Moments[i].HomeSpeed - numeric.Moments[i - 1].HomeSpeed));
        homeSpeed.Should().BeLessThan(0.5 * falls.DrainSpeed,
            "the falling frame never has to take the body's velocity over");
        homeSpeedChange.Should().BeLessThan(5e-3, "the body's velocity relative to its falling frame changes slowly");
        TestContext.Current.TestOutputHelper?.WriteLine(
            $"person={person}: worst distance {before:E2} m before the landing at tick {landing}, "
            + $"{after:E2} m after it, deepest {plain.Deepest:F4} m, fastest relative to a falling home {homeSpeed:E2} m/s, "
            + $"largest change of that in a tick {homeSpeedChange:E2} m/s");
    }

    // ---- Determinism ----

    /// <summary>
    /// The landing scene with a person, a crate that thrusts and a crate that does not, each crossing the middle 5 m apart
    /// along +Y, under rules by which every group falls with no lookahead, so that all three fall in numeric frames below
    /// the station and land in its own system. Returns, for each of 300 ticks, one line with the tick, each body's physics
    /// home and a hash of the bits of each body's place and velocity in the station, and then one line for each collision
    /// that a body heard.
    /// </summary>
    private static List<string> LandingTrace(int threads)
    {
        var backend = new JoltPhysicsBackend(new() { WorkerThreads = threads });
        var options = new WorldOptions
        {
            MaxDegreeOfParallelism = threads, InitialCapacity = TestCapacity, DebugChecks = true, DisposePhysics = true
        };
        options.NumericFrames = NumericFrameTesting.EveryGroupFalls with { LookaheadTime = 0 };
        using var world = new World(options, backend);
        Station station = SpawnStation(world);
        var logs = new List<CollisionLog> { new(), new(), new() };
        Entity[] bodies =
        [
            SpawnCrosser(world: world, station: station, body: Person, height: -5, log: logs[0]),
            SpawnCrosser(world: world, station: station, body: Crate, height: 0, thrusts: true, log: logs[1]),
            SpawnCrosser(world: world, station: station, body: Crate, height: 5, log: logs[2])
        ];

        var lines = new List<string>();
        for (int tick = 0; tick < 300; tick++)
        {
            world.Tick();
            var hash = new HashCode();
            var homes = new List<string>();
            foreach (Entity body in bodies)
            {
                FrameHandle home = world.PhysicsHomeOf(body);
                homes.Add(home == station.Frame ? "station" : world.Frames.Numeric(home).ToString());
                Double3 position = world.PoseIn(body, station.Frame).Position;
                Double3 velocity = world.VelocityIn(body, station.Frame);
                foreach (double value in new[]
                             { position.X, position.Y, position.Z, velocity.X, velocity.Y, velocity.Z })
                    hash.Add(BitConverter.DoubleToInt64Bits(value));
            }

            lines.Add($"{tick} {string.Join(" ", homes)} {hash.ToHashCode()}");
        }

        for (int i = 0; i < logs.Count; i++)
            lines.AddRange(logs[i].Seen.Select(seen =>
                $"body {i} hit {seen.Other} at {seen.Tick} with {BitConverter.SingleToInt32Bits(seen.Impulse)}"));
        world.Errors.Should().BeEmpty();
        return lines;
    }

    [Fact]
    public void TheLandingSceneGivesTheSameEventsAndStateInEveryRunWithOneOrFourThreads()
    {
        List<string> first = LandingTrace(1);
        List<string> second = LandingTrace(1);
        List<string> parallel = LandingTrace(4);

        first.Should().Contain(line => line.Contains("Falling"), "the bodies fall in numeric frames below the station");
        first.Should().Contain(line => line.StartsWith("body 1 hit") || line.StartsWith("body 2 hit"),
            "the crates land on the floor");
        second.Should().Equal(first);
        parallel.Should().Equal(first);
    }
}
