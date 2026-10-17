using Tertius.Host.Scripting;
using Tertius.Physics;
using Tertius.Simulation;
using Station = Tertius.Demo.DemoScenes.Station;

namespace Tertius.Host.Tests;

/// <summary>
/// One headless run of the checked-in script scripts/station.json in the scene station (see
/// <see cref="Station"/>), with the state of its moving bodies after every tick. The station rides its Kepler orbit 400 km
/// up at 7.67 km/s. Its airlock opens to the outside, and three cargo pods drift out of it at 1 m/s, out of the bubble
/// and along the port side, and bump the radiator. In the cabin, the script throws a crate at 60 m/s after the pods have
/// left the station's frame, and it stops at a thin wall. Every test of the class reads that one run.
/// </summary>
public sealed class StationRun : CheckedInRun
{
    public StationRun() : this([]) { }

    private StationRun(List<StationSample> samples) : base("station", (world, scene) => samples.Add(StationSample.Take(world, scene)))
    {
        Samples = samples;
    }

    /// <summary>The state after each tick of the run, from the start to the last tick, in order.</summary>
    internal IReadOnlyList<StationSample> Samples { get; }
}

/// <summary>The station's moving bodies after a number of ticks.</summary>
/// <param name="Tick">The number of completed ticks.</param>
/// <param name="Time">The world's time, in seconds.</param>
/// <param name="Bodies">The pods and the crate, by scene id.</param>
/// <param name="HullHome">The physics home of the station's hull.</param>
/// <param name="HullHomeMode">How the hull's home moves: <see cref="NumericMode.Orbiting"/> for its orbit frame.</param>
/// <param name="HullOffEarthOrbit">How far the read of the hull's place in earth lies from its orbit's place, in meters.</param>
internal sealed record StationSample(long Tick, double Time, IReadOnlyDictionary<string, StationBody> Bodies, FrameHandle HullHome,
    NumericMode HullHomeMode, double HullOffEarthOrbit)
{
    public static readonly string[] BodyIds = [.. Station.Pods.Select(pod => pod.Id), "crate"];

    public static StationSample Take(World world, SceneInstance scene)
    {
        Entity station = scene["station"];
        FrameHandle frame = world.GetOwnedFrame(station), earth = world.GetOwnedFrame(scene["earth"]), hullHome = world.PhysicsHomeOf(station);
        double mu = world.Frames.Gravity(earth) is PointGravity { Mu: var m } ? m : double.NaN;
        Double3 orbitPlace = world.Frames.Orbit(frame).StateAt(world.Time, mu).Position;
        return new StationSample(world.TickNumber, world.Time,
            BodyIds.ToDictionary(id => id, id => StationBody.Take(world, scene[id], frame, hullHome)), hullHome, world.Frames.Numeric(hullHome),
            (world.PoseIn(station, earth).Position - orbitPlace).Length);
    }
}

/// <summary>One moving body of the station after a tick.</summary>
/// <param name="InStation">The position in the station's frame, in its axes, in meters, read through the frame tree.</param>
/// <param name="VelocityInStation">The velocity relative to the station's frame, in its axes, in m/s.</param>
/// <param name="InStationFrame">True while the body's semantic frame is the station's frame, false when it is earth.</param>
/// <param name="Home">The kind of the body's physics home: "station" for the station frame's own system, "orbit frame" for
/// the home of the station's hull, or else the mode of a numeric frame.</param>
internal sealed record StationBody(Double3 InStation, Double3 VelocityInStation, bool InStationFrame, string Home)
{
    public static StationBody Take(World world, Entity entity, FrameHandle station, FrameHandle hullHome)
    {
        FrameHandle home = world.PhysicsHomeOf(entity);
        string kind = home == station ? "station" : home == hullHome ? "orbit frame" : world.Frames.Numeric(home).ToString();
        return new StationBody(world.PoseIn(entity, station).Position, world.VelocityIn(entity, station), world.GetFrame(entity) == station, kind);
    }
}

[Collection(GpuCollection.Name)]
public sealed class StationSliceTests(StationRun run) : IClassFixture<StationRun>
{
    private const double TicksPerSecond = 60;

    /// <summary>The first tick of the drift that the pods' checks measure from: 1 s after the start, inside the chamber.</summary>
    private const int ReferenceTick = 60;

    private IReadOnlyList<StationSample> Samples => run.Samples;

    /// <summary>Every Collision of the run that <paramref name="id"/> heard, in order: the tick and the other entity.</summary>
    private List<(long Tick, Entity Other)> CollisionsOf(string id) =>
    [
        .. run.Result.Dumps.SelectMany(dump => dump.Events)
            .Where(e => e.To == new ToEntity(id) && e.Payload is Collision)
            .Select(e => (e.Tick, ((Collision)e.Payload).Other)),
    ];

    /// <summary>
    /// When pod <paramref name="index"/> touches the radiator if it drifts on in a straight line relative to the station
    /// from <see cref="ReferenceTick"/>, as a number of ticks: its face toward the nose reaches the radiator's aft face.
    /// </summary>
    private double PredictedBump(int index)
    {
        (string id, _, Double3 velocity) = Station.Pods[index];
        double z = Samples[ReferenceTick].Bodies[id].InStation.Z, contact = Station.Radiator.Max.Z + Station.PodHalfSize;
        return ReferenceTick + (z - contact) / -velocity.Z * TicksPerSecond;
    }

    [Fact]
    public void ThePodsDriftOutOfTheAirlockAndOnInAStraightLineRelativeToTheStationToWithinACentimeterUntilTheyBumpTheRadiator()
    {
        for (int i = 0; i < Station.Pods.Count; i++)
        {
            (string id, Double3 start, Double3 velocity) = Station.Pods[i];
            StationSample reference = Samples[ReferenceTick];
            Double3.Distance(reference.Bodies[id].InStation, start + velocity * reference.Time).Should()
                .BeLessThan(velocity.Length / TicksPerSecond + 1e-3, $"{id} drifts from its start at its velocity, from the first step on");
            double worst = 0;
            long worstTick = 0;
            foreach (StationSample sample in Samples.Where(s => s.Tick >= ReferenceTick && s.Tick < PredictedBump(i) - 3))
            {
                double off = Double3.Distance(sample.Bodies[id].InStation, reference.Bodies[id].InStation + velocity * (sample.Time - reference.Time));
                if (off > worst)
                    (worst, worstTick) = (off, sample.Tick);
            }
            // At 7.67 km/s a float velocity steps by 0.5 mm/s, so in earth's own system each tick's pull of 0.14 m/s
            // would round, and the drift would wander. In the hull's orbit frame both are slow. Earth's tidal pull
            // over the 10 s outside the bubble moves a pod by about 2 mm.
            worst.Should().BeLessThan(0.01, $"{id} keeps its drift relative to the station; it was {worst:F4} m off at tick {worstTick}");
        }
    }

    [Fact]
    public void ThePodsLeaveTheBubbleIntoTheStationHullsOrbitFrameAndDriftThereForSecondsBeforeTheyBump()
    {
        Samples.Skip(1).Should().OnlyContain(s => s.HullHomeMode == NumericMode.Orbiting, "the station's hull rests in its orbit frame");
        Samples.Skip(1).Select(s => s.HullHome).Distinct().Should().ContainSingle("the hull keeps one orbit frame");
        for (int i = 0; i < Station.Pods.Count; i++)
        {
            string id = Station.Pods[i].Id;
            var homes = new List<(bool InStationFrame, string Home)>();
            foreach (StationSample sample in Samples.Skip(1))
                if (homes.Count == 0 || homes[^1] != (sample.Bodies[id].InStationFrame, sample.Bodies[id].Home))
                    homes.Add((sample.Bodies[id].InStationFrame, sample.Bodies[id].Home));
            homes.Should().Equal([(true, "station"), (false, "orbit frame")],
                $"{id} leaves the station's bubble into earth, and its body joins the hull's orbit frame, where both are slow");
            long left = Samples.First(s => !s.Bodies[id].InStationFrame).Tick;
            (PredictedBump(i) - left).Should().BeGreaterThan(5 * TicksPerSecond, $"{id} drifts in the orbit frame for more than 5 s before it bumps");
        }
    }

    [Fact]
    public void EachPodBumpsTheRadiatorOutsideTheBubbleAtTheTickThatItsDriftPredictsAndHearsACollisionWithTheStation()
    {
        Entity station = run.Scene["station"];
        for (int i = 0; i < Station.Pods.Count; i++)
        {
            string id = Station.Pods[i].Id;
            List<(long Tick, Entity Other)> collisions = CollisionsOf(id);

            collisions.Should().NotBeEmpty($"{id} bumps the radiator");
            collisions[0].Other.Should().Be(station, $"the first thing that {id} meets is the station's hull");
            // Jolt reports a contact when the gap falls below its speculative contact distance of 2 cm, which a pod
            // closes in 2 ticks at about 0.6 m/s.
            ((double)collisions[0].Tick).Should().BeApproximately(PredictedBump(i), 3, $"{id} bumps the radiator where its drift takes it");
            StationBody atBump = Samples[(int)collisions[0].Tick].Bodies[id];
            atBump.InStationFrame.Should().BeFalse($"{id} bumps the radiator outside the station's bubble");
            atBump.Home.Should().Be("orbit frame", $"{id} meets the hull as a real body of the hull's orbit frame");
        }
    }

    [Fact]
    public void ThePodsStayAgainstTheRadiatorsAftFaceAfterTheBump()
    {
        StationSample end = Samples[^1];
        foreach ((string id, _, _) in Station.Pods)
        {
            end.Bodies[id].InStation.Z.Should().BeGreaterThan(Station.Radiator.Max.Z + Station.PodHalfSize - 0.02, $"{id} does not pass into the radiator");
            end.Bodies[id].InStation.Z.Should().BeLessThan(Station.Radiator.Max.Z + Station.PodHalfSize + 0.05, $"{id} does not bounce off the radiator");
            // A read in the station's frame of a body in the orbit frame carries half a step of earth's pull, about 7 cm/s
            // along the radius and 2.3 cm/s of it along the station's Z: the orbit frame moves with the orbit's chord over
            // each step, and the station's frame with the orbit's instant velocity. The places agree.
            Math.Abs(end.Bodies[id].VelocityInStation.Z).Should().BeLessThan(0.05, $"the bump stopped {id}'s drift toward the nose");
            end.Bodies[id].InStation.X.Should().BeGreaterThan(Station.Radiator.Min.X, $"{id} is still beside the radiator");
        }
    }

    /// <summary>The first sample after the step in which the script's Launch threw the crate.</summary>
    private int Launched => Samples.ToList().FindIndex(s => s.Bodies["crate"].VelocityInStation.Length > 1);

    [Fact]
    public void TheScriptThrowsTheCrateAfterThePodsHaveLeftTheStationsFrame()
    {
        Launched.Should().BePositive("the script throws the crate");
        Samples.Take(Launched).Should().OnlyContain(s => Double3.Distance(s.Bodies["crate"].InStation, Station.CrateStart) < 1e-6,
            "the crate floats at its start until the script throws it");
        Samples[Launched].Bodies.Where(body => body.Key != "crate").Should().OnlyContain(body => !body.Value.InStationFrame,
            "the pods have left the station's frame, so the crate's speed sets the speed of its group in the merge test");
    }

    [Fact]
    public void TheCrateThrownAtSixtyMetersASecondStopsAtTheThinWallAndHearsItsCollision()
    {
        Entity wall = run.Scene["wall"];
        StationBody flying = Samples[Launched + 10].Bodies["crate"], end = Samples[^1].Bodies["crate"];

        Double3.Distance(flying.VelocityInStation, Station.CrateVelocity).Should().BeLessThan(1e-3, "the crate flies at 60 m/s before it reaches the wall");
        // Jolt pushes two bodies apart only as far as they sink beyond the penetration slop, so the crate may rest that far in.
        double sink = Station.ThinWall.Max.Z + Station.CrateHalfSize - end.InStation.Z;
        sink.Should().BeInRange(-2e-3, new JoltBackendOptions().PenetrationSlop + 2e-3, "the crate stops with its front at the wall's aft face");
        end.VelocityInStation.Length.Should().BeLessThan(0.05, "the crate stopped at the wall");
        CollisionsOf("crate").Should().ContainSingle("the crate hears one Collision").Which.Other.Should().Be(wall);
    }

    /// <summary>
    /// A body enters a frame's bubble when its bounding sphere touches the bubble, so a door that a pod passed close by
    /// would enter the pod's frame. The doors do not move and stay out of the pods' reach (see <see cref="Station"/>).
    /// Before, the outer door slid open as the pods set off, entered pod-2's frame, got its target in the station's frame
    /// applied in the pod's, and changed frames in every tick for more than a second.
    /// </summary>
    [Fact]
    public void TheAirlocksDoorsStayStillInTheStationsFrameAndHearNoFrameChanged()
    {
        foreach (string id in new[] { "airlock-outer", "airlock-inner" })
        {
            run.Result.Dumps.SelectMany(dump => dump.Events).Where(e => e.To == new ToEntity(id) && e.Payload is FrameChanged)
                .Should().BeEmpty($"{id} never enters a pod's bubble");
            FramePose first = run.Result.Dumps[0].Entity(id).Pose;
            first.Frame.Should().Be("station");
            run.Result.Dumps.Should().OnlyContain(dump => dump.Entity(id).Pose.Frame == "station" && Double3.Distance(dump.Entity(id).Pose.Position, first.Position) < 1e-9,
                $"{id} stays where it spawned");
        }
    }

    /// <summary>
    /// The crate is within the merge test's reach of the wall at every sync point, 120 m at 60 m/s for 2 s, so it never
    /// falls in a numeric frame of its own: it shares the wall's home, the station frame's own system, where Jolt sweeps it
    /// against the real wall. Its group, the crate and the airlock's two doors of 1 kg, moves at 55.6 m/s, fast enough for
    /// a falling frame. Before ter-dhkf, the merge test kept a falling group apart from an anchor, so the crate fell in a
    /// numeric frame, met only a kinematic ghost of the wall there, which Jolt gives no linear cast, and passed through it.
    /// </summary>
    [Fact]
    public void TheThrownCrateLivesInTheWallsSystemAtEveryTick()
    {
        Samples.Skip(1).Should().OnlyContain(s => s.Bodies["crate"].Home == "station" && s.Bodies["crate"].InStationFrame);
    }

    [Fact]
    public void EveryReadOfTheStationInEarthIsItsOrbitsPlace()
    {
        Samples.Max(s => s.HullOffEarthOrbit).Should().BeLessThan(1e-6, "the orbit frame puts the hull on its orbit at every sync point");
    }

    [Fact]
    public void TheRunRecordsNoErrors()
    {
        run.World.Errors.Should().BeEmpty();
        run.Result.Dumps.Should().OnlyContain(dump => dump.Errors.Count == 0);
        run.Result.Captures.Should().HaveCount(7);
    }
}

/// <summary>
/// The station scene runs alike in every run, with one gameplay thread and one Jolt thread or with four of each: its
/// numeric frames, the pods' moves into the orbit frame and the crate's link to the wall, are the same at every sync
/// point. Each run plays scripts/station.json without its captures, which need a renderer.
/// </summary>
public sealed class StationDeterminismTests
{
    /// <summary>The dumps of one run of the station's script without captures, on <paramref name="threads"/> threads.</summary>
    private static IReadOnlyList<StateDump> Run(int threads)
    {
        Script script = Script.Parse(File.ReadAllText(Path.Combine(HostTesting.RepositoryRoot(), "scripts", "station.json")));
        script = script with { Steps = [.. script.Steps.Where(step => step is not CaptureStep)] };
        using var world = new World(new WorldOptions { MaxDegreeOfParallelism = threads, DebugChecks = true, DisposePhysics = true },
            new JoltPhysicsBackend(new JoltBackendOptions { WorkerThreads = threads }));
        SceneInstance scene = Tertius.Demo.DemoScenes.All.Single(s => s.Name == script.Scene).Build(world, RenderResources.None);
        RunResult result = ScriptRunner.Run(script, world, scene);
        world.Errors.Should().BeEmpty();
        return result.Dumps;
    }

    [Fact]
    public void TheStationSceneGivesTheSameEventsAndStatesInEveryRunWithOneOrFourThreads()
    {
        IReadOnlyList<StateDump> first = Run(1), second = Run(1), parallel = Run(4);

        first.SelectMany(dump => dump.Events).Should().Contain(e => e.Event == nameof(Collision), "the run has the bumps and the crate's hit");
        foreach (IReadOnlyList<StateDump> other in new[] { second, parallel })
        {
            other.Select(dump => dump.Name).Should().Equal(first.Select(dump => dump.Name));
            for (int i = 0; i < first.Count; i++)
            {
                other[i].Entities.Should().Equal(first[i].Entities, $"every entity has the same state, to the bit, in the dump {first[i].Name}");
                other[i].Events.Should().Equal(first[i].Events, $"the same events reach the same entities, in the dump {first[i].Name}");
            }
        }
    }
}
