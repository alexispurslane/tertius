using System.Numerics;
using Tertius.Frames.Rails;
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
public class SpinStationLandingTests
{
    private const double StationRate = 0.31321;
    private const double FloorRadius = 100;
    private const int FloorFaces = 64;
    private static readonly Double3 Omega = new(0, StationRate, 0);

    /// <summary>A person: a capsule with a half height of 0.6 m and a radius of 0.3 m, of 70 kg.</summary>
    private static readonly BodyDesc Person = new(new CapsuleShapeDesc(0.6f, 0.3f), BodyMotion.Character, Mass: 70);

    /// <summary>An undamped crate, a box of 1 m and 10 kg, so that its path is its inertial line until it lands.</summary>
    private static readonly BodyDesc Crate = new(new BoxShapeDesc(new Vector3(0.5f)), Mass: 10, LinearDamping: 0, AngularDamping: 0);

    /// <summary>
    /// A force on the crate of 1 mN along the station's +Z, 1e-4 m/s² on 10 kg, above the 1e-3/60 m/s² from which a tick of
    /// force counts as thrust. After half a second of it the crate thrusts, so it gets a falling frame from 50 m/s, as a
    /// person does. It moves the crate by 1.3 mm in five seconds.
    /// </summary>
    private static readonly Vector3 Thrust = new(0, 0, 1e-3f);

    private sealed record Station(FrameHandle Space, FrameHandle Frame);

    /// <summary>The station and its floor of <see cref="FloorFaces"/> static slabs, 1 m thick and 20 m wide along +Y.</summary>
    private static Station SpawnStation(World world)
    {
        FrameHandle space = world.CreateRootFrame(new FrameSpec(FrameBounds.Sphere(1e6f)));
        Entity owner = world.Spawn(new SpawnDesc { Frame = space, AsFrame = new FrameSpec(FrameBounds.Sphere(150)) }
            .Spinning(world, new FrameSpin(DQuaternion.Identity, StationRate), firstMove: true));
        FrameHandle frame = world.GetOwnedFrame(owner);
        // Half the width of a face along the ring, with 5 cm of overlap with its neighbors.
        float halfWidth = (float)(FloorRadius * Math.Tan(Math.PI / FloorFaces)) + 0.05f;
        for (int i = 0; i < FloorFaces; i++)
        {
            world.Spawn(new SpawnDesc
            {
                Frame = frame,
                Pose = new DPose(FaceNormal(i) * (FloorRadius + 0.5), DQuaternion.FromAxisAngle(new Double3(0, 1, 0), -FaceAngle(i))),
                Body = new BodyDesc(new BoxShapeDesc(new Vector3(0.5f, 10, halfWidth)), BodyMotion.Static),
            });
        }
        return new Station(space, frame);
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
            Double3 normal = FaceNormal(((i % FloorFaces) + FloorFaces) % FloorFaces);
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
    private static Entity SpawnCrosser(World world, Station station, BodyDesc body, double radius = 60, double height = 0,
        bool thrusts = false, IComponent? log = null)
    {
        var place = new Double3(-radius, height, 0);
        var pilot = new Pilot();
        if (thrusts)
            pilot.EveryTick = handle => handle.AddForce(Thrust);
        return world.Spawn(new SpawnDesc
        {
            Frame = station.Frame,
            Pose = new DPose(place, DQuaternion.Identity),
            LinearVelocity = new Double3(40, 0, 0) - Double3.Cross(Omega, place),
            Body = body,
            Components = log is null ? [pilot] : [pilot, log],
        });
    }

    /// <summary>What a run of one body across the station shows, at each sync point.</summary>
    /// <param name="Places">The body's place in the station.</param>
    /// <param name="Depths">How deep the body reaches into the floor (see <see cref="Depth"/>).</param>
    /// <param name="Falling">Whether the body's physics home was a numeric frame.</param>
    /// <param name="Speeds">The body's speed relative to the station, in m/s.</param>
    private sealed record Crossing(List<Double3> Places, List<double> Depths, List<bool> Falling, List<double> Speeds)
    {
        /// <summary>The index of the first sync point at which the body touches the floor, within 5 cm, or -1.</summary>
        public int Landing => Depths.FindIndex(depth => depth > -0.05);
    }

    /// <summary>
    /// Runs one body across the station under <paramref name="rules"/> for <paramref name="seconds"/> s, from
    /// <paramref name="radius"/> m from the axis (see <see cref="SpawnCrosser"/>).
    /// </summary>
    private static Crossing Cross(NumericFrameOptions rules, BodyDesc body, bool thrusts, double seconds, double radius = 60)
    {
        using World world = NewWorld(out _, debugChecks: false, configure: options => options.NumericFrames = rules);
        Station station = SpawnStation(world);
        Entity crosser = SpawnCrosser(world, station, body, radius, thrusts: thrusts);
        var crossing = new Crossing([], [], [], []);
        for (int tick = 0; tick < seconds * world.Options.TickRate; tick++)
        {
            world.Tick();
            DPose pose = world.PoseIn(crosser, station.Frame);
            crossing.Places.Add(pose.Position);
            crossing.Depths.Add(Depth(pose, body.Motion == BodyMotion.Character));
            crossing.Falling.Add(world.Frames.IsNumeric(world.PhysicsHomeOf(crosser)));
            crossing.Speeds.Add(world.VelocityIn(crosser, station.Frame).Length);
            world.GetFrame(crosser).Should().Be(station.Frame, "the body stays inside the station");
        }
        world.Errors.Should().BeEmpty();
        return crossing;
    }

    // ---- The landing case ----

    /// <summary>
    /// The person leaves the far side of the floor, 98 m from the axis and 1.7 m above the floor, at 50.4 m/s relative to the
    /// station, lands on the floor and stays on it, with the default numeric frames and without numeric frames. With the
    /// default rules a person, who always thrusts, gets a falling frame from 50 m/s, but the merge test's reach of 2 s at its
    /// speed links it to the floor's slabs all the way across the station, so it stays in the station's own system, and
    /// Jolt's sweep of the person meets the real floor. In the spike, before ter-dhkf, it landed 0.7 m deep. Without the
    /// link it falls through the floor.
    /// <para>
    /// Until ticket ter-1b21 gives a walking character the rotation terms, the person moves on a straight line in the
    /// station's axes at its spawn velocity, with numeric frames and without. From that ticket on it follows the inertial line
    /// through the axis at 40 m/s and meets the floor at 50.8 m/s, the spike's case. The test asks only that it lands and
    /// stays.
    /// </para>
    /// </summary>
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void APersonWhoCrossesTheMiddleOfTheStationAt40MetersPerSecondLandsOnTheFloorAndStaysOnIt(bool numericFrames)
    {
        Crossing crossing = Cross(numericFrames ? new NumericFrameOptions() : NumericFrameTesting.WithoutNumericFrames, Person,
            thrusts: false, seconds: 7, radius: 98);

        int landing = crossing.Landing;
        landing.Should().BeGreaterThan(0, "the person reaches the floor");
        crossing.Depths.Max().Should().BeLessThan(0.05, "the person never sinks into the floor; in the spike it sank 0.7 m");
        crossing.Depths.Skip(landing).Should().OnlyContain(depth => depth > -0.25, "the person stays on the floor after it lands");
        (crossing.Places.Count - landing).Should().BeGreaterThan(120, "the run watches the person for 2 s after it lands");
        crossing.Speeds[0].Should().BeGreaterThan(50, "the person moves fast enough for a falling frame");
        crossing.Falling.Should().OnlyContain(falling => !falling, "the merge test links the person to the floor, so it stays in the station's own system");
    }

    /// <summary>
    /// A crate that thrusts gets a falling frame from 50 m/s, as a person does, and a dynamic body follows its inertial line
    /// in the station, so it crosses the middle at 40 m/s and passes 50 m/s relative to the station 4 m before the floor, the
    /// spike's case. It lands where and when it lands without numeric frames, at every sync point within a float step at its
    /// speed, because the merge test keeps it in the station's own system up to the floor.
    /// </summary>
    [Fact]
    public void ACrateThatThrustsAndCrossesTheMiddleOfTheStationAt40MetersPerSecondLandsAsWithoutNumericFrames()
    {
        Crossing numeric = Cross(new NumericFrameOptions(), Crate, thrusts: true, seconds: 5);
        Crossing plain = Cross(NumericFrameTesting.WithoutNumericFrames, Crate, thrusts: true, seconds: 5);

        int landing = plain.Landing;
        landing.Should().BeGreaterThan(0, "the crate reaches the floor");
        plain.Speeds.Take(landing).Max().Should().BeGreaterThan(50, "the crate passes the speed at which a group that thrusts falls");
        numeric.Landing.Should().Be(landing, "the crate lands at the same sync point");
        // Jolt lets a box that hits a face at 50 m/s overlap it by 7.5 cm for a tick, without numeric frames too, and then
        // pushes it out. The spike's person sank 0.7 m and went through.
        plain.Depths.Max().Should().BeLessThan(0.1, "Jolt keeps the crate out of the floor in the station's own system");
        numeric.Depths.Max().Should().BeApproximately(plain.Depths.Max(), 1e-3, "the crate sinks no deeper than without numeric frames");
        double worst = numeric.Places.Zip(plain.Places, Double3.Distance).Max();
        worst.Should().BeLessThan(1e-3, "the crate moves as it does without numeric frames");
        numeric.Falling[landing].Should().BeFalse("the merge test links the crate to the floor, so it lands in the station's own system");
    }

    /// <summary>
    /// Under rules by which every group falls, with no lookahead, a body falls in a numeric frame below the station for most
    /// of its crossing, and joins the station's own system only where its bounding sphere meets a slab's, 11 m from the floor.
    /// It moves as it does in the station's own system all the way, within a float step at its speed, and lands there.
    /// In the falling frame the origin moves on the inertial line of a free body at its place, and the body feels only the
    /// difference of its rotation terms and the origin's (docs/plans/spin-gravity.md, "The origin of a falling frame"). Until
    /// ticket ter-1b21 a walking person feels none of the rotation terms in either system, so it moves on a straight line in
    /// the station's axes in both.
    /// </summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ABodyThatCrossesTheMiddleOfTheStationInAFallingFrameMovesAsInTheStationsOwnSystemAndLandsThere(bool person)
    {
        NumericFrameOptions falls = NumericFrameTesting.EveryGroupFalls with { LookaheadTime = 0 };
        BodyDesc body = person ? Person : Crate;
        Crossing numeric = Cross(falls, body, thrusts: false, seconds: 5);
        Crossing plain = Cross(NumericFrameTesting.WithoutNumericFrames, body, thrusts: false, seconds: 5);

        int landing = plain.Landing;
        landing.Should().BeGreaterThan(0, "the body reaches the floor");
        numeric.Falling.Take(landing).Count(falling => falling).Should().BeGreaterThan(landing / 2, "the body falls in a numeric frame for most of its crossing");
        numeric.Falling[landing].Should().BeFalse("the body joins the station's own system before it meets the floor");
        numeric.Landing.Should().Be(landing, "the body lands at the same sync point");
        plain.Depths.Max().Should().BeLessThan(0.1, "Jolt keeps the body out of the floor in the station's own system");
        numeric.Depths.Max().Should().BeApproximately(plain.Depths.Max(), 1e-3, "the body sinks no deeper than without numeric frames");
        double worst = numeric.Places.Zip(plain.Places, Double3.Distance).Max();
        worst.Should().BeLessThan(1e-3, "the body moves as it does without numeric frames");
        TestContext.Current.TestOutputHelper?.WriteLine($"person={person}: worst distance {worst:E2} m, landing tick {landing}, deepest {plain.Depths.Max():F4} m");
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
        var backend = new JoltPhysicsBackend(new JoltBackendOptions { WorkerThreads = threads });
        var options = new WorldOptions { MaxDegreeOfParallelism = threads, DebugChecks = true, DisposePhysics = true };
        options.NumericFrames = NumericFrameTesting.EveryGroupFalls with { LookaheadTime = 0 };
        using var world = new World(options, backend);
        Station station = SpawnStation(world);
        var logs = new List<CollisionLog> { new(), new(), new() };
        Entity[] bodies =
        [
            SpawnCrosser(world, station, Person, height: -5, log: logs[0]),
            SpawnCrosser(world, station, Crate, height: 0, thrusts: true, log: logs[1]),
            SpawnCrosser(world, station, Crate, height: 5, log: logs[2]),
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
                foreach (double value in new[] { position.X, position.Y, position.Z, velocity.X, velocity.Y, velocity.Z })
                    hash.Add(BitConverter.DoubleToInt64Bits(value));
            }
            lines.Add($"{tick} {string.Join(" ", homes)} {hash.ToHashCode()}");
        }
        for (int i = 0; i < logs.Count; i++)
            lines.AddRange(logs[i].Seen.Select(seen => $"body {i} hit {seen.Other} at {seen.Tick} with {BitConverter.SingleToInt32Bits(seen.Impulse)}"));
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
        first.Should().Contain(line => line.StartsWith("body 1 hit") || line.StartsWith("body 2 hit"), "the crates land on the floor");
        second.Should().Equal(first);
        parallel.Should().Equal(first);
    }
}
