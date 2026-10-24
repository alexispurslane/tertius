using Tertius.Demo;
using Tertius.Frames.Rails;
using Tertius.Host.Scripting;
using Tertius.Simulation;
using static Tertius.Testing.WorldTesting;

namespace Tertius.Host.Tests;

/// <summary>
/// The readout log of the demo edit (docs/plans/blue-danube-demo.md, "The readout log"): a playback that draws nothing and
/// adds one row at each capture of a script, on the real World and the real Jolt backend. The log reads in the frame
/// <c>earth</c>, which does not turn. The first scene of these tests drops a ball, the <c>person</c>, from 5 m in the root
/// frame <c>earth</c>, whose uniform gravity is 9.81 m/s². The <c>station</c> is a static box 30 m to the side at the
/// ball's first height, turned a quarter turn about earth's -Z, so that its axis, its +Y, lies along earth's +X. The second scene,
/// <see cref="Turning"/>, holds a turning ground in earth.
/// </summary>
public sealed class ReadoutLogTests
{
    private const float G = 9.81f;

    /// <summary>
    /// The turn of <see cref="Turning"/>'s ground about +Y, in rad/s: fast, so that a person who stands still on it 10 m
    /// from the axis moves at 5 m/s in <c>earth</c>.
    /// </summary>
    private const double GroundRate = 0.5;

    private static readonly Scene Drop = new("readout-drop", (world, _) =>
    {
        var scene = new SceneInstance();
        FrameHandle earth = Ground(world, G);
        scene.Add("earth", earth.Entity);
        scene.Add("person", world.Spawn(new() { Frame = earth, Pose = At(0, 5), Body = Ball() }));
        scene.Add("station", world.Spawn(new()
        {
            Frame = earth,
            Pose = new(new(30, 5, 0), DQuaternion.FromAxisAngle(-Double3.UnitZ, Math.PI / 2)),
            Body = new(Shape: new BoxShapeDesc(new(1)), Motion: BodyMotion.Static)
        }));
        scene.Add("camera", world.Spawn(new() { Frame = earth, Pose = At(0, 2, 10), Render = new RenderData() }));
        return scene;
    });

    /// <summary>A film of 3 frames every 2 ticks, then a film of 2 frames every tick: frames after ticks 2, 4, 6, 7 and 8.</summary>
    private static readonly Script TwoFilms = new("readout-drop",
        [new(new CameraStep("camera")), new(new FilmStep("a", 3, 2)), new(new FilmStep("b", 2, 1))], new(16, 16));

    private static IReadOnlyList<ReadoutRow> Rows => ReadoutLog.Record(TwoFilms, [Drop]);

    /// <summary>
    /// A scene of a turning ground, as demo-surface's: the root frame <c>earth</c>, which does not turn, holds the frame
    /// <c>earth-surface</c> at its origin, turned a quarter turn about +Y at the world's start and turning about +Y at
    /// <see cref="GroundRate"/>. The <c>person</c> is a static box that stands still in earth-surface, 10 m from the axis
    /// on its +X. The <c>station</c> is a static box in earth, 10 m out on earth's +X.
    /// </summary>
    private static readonly Scene Turning = new("readout-turning", (world, _) =>
    {
        var scene = new SceneInstance();
        FrameHandle earth = world.CreateRootFrame(new(FrameBounds.Sphere(1e4f)));
        scene.Add("earth", earth.Entity);
        var spin = new FrameSpin(Tilt: DQuaternion.Identity, Rate: GroundRate, PhaseAtEpoch: Math.PI / 2);
        Entity ground = world.Spawn(new()
        {
            Frame = earth,
            Pose = new(Double3.Zero, spin.RotationAt(world.Time)),
            FirstMove = new(spin.Sample(Double3.Zero, world.TimeAt(world.TickNumber + 1), world.DeltaTime)),
            AsFrame = new(FrameBounds.Sphere(1e3f)),
            Components = [new SpinMotion { Spin = spin }]
        });
        scene.Add("earth-surface", ground);
        FrameHandle surface = world.GetOwnedFrame(ground);
        var box = new BodyDesc(Shape: new BoxShapeDesc(new(0.5f)), Motion: BodyMotion.Static);
        scene.Add("person", world.Spawn(new() { Frame = surface, Pose = At(10), Body = box }));
        scene.Add("station", world.Spawn(new() { Frame = earth, Pose = At(10), Body = box }));
        scene.Add("camera", world.Spawn(new() { Frame = earth, Pose = At(0, 2, 20), Render = new RenderData() }));
        return scene;
    });

    /// <summary>The time of a tick, in seconds: the engine's 60 ticks per second.</summary>
    private static double Seconds(long tick) => tick / 60.0;

    [Fact]
    public void TheLogHasOneRowForEachFrameOfTheScriptWithTheFramesNameInOrder()
    {
        Rows.Select(row => row.Frame).Should().Equal("a-000000", "a-000001", "a-000002", "b-000000", "b-000001");
    }

    [Fact]
    public void EachRowHoldsTheTickOfItsFrame()
    {
        Rows.Select(row => row.Tick).Should().Equal(2, 4, 6, 7, 8);
    }

    [Fact]
    public void ARowHoldsThePersonsPositionInEarth()
    {
        foreach (ReadoutRow row in Rows)
        {
            double t = Seconds(row.Tick);
            row.Position.X.Should().Be(0);
            row.Position.Z.Should().Be(0);
            // Free fall from 5 m: the step's integration keeps the drop within 2 mm of half g t² over these 8 ticks.
            row.Position.Y.Should().BeApproximately(5 - G * t * t / 2, 2e-3);
            row.Position.Y.Should().BeLessThan(5);
        }
    }

    [Fact]
    public void ARowHoldsThePersonsSpeedInEarth()
    {
        // The ball falls freely, so its speed grows by g each second from one row to the next. The reads describe the last
        // finished step, so the speed itself lags the tick number, and the test reads only its growth.
        IReadOnlyList<ReadoutRow> rows = Rows;
        rows[0].Speed.Should().BePositive();
        for (int i = 1; i < rows.Count; i++)
            (rows[i].Speed - rows[i - 1].Speed).Should()
                .BeApproximately(G * Seconds(rows[i].Tick - rows[i - 1].Tick), 1e-4);
    }

    /// <summary>
    /// The readouts read in <c>earth</c>, the frame that does not turn, as the guidance does: a person who stands still on
    /// the turning ground, 10 m from its axis, moves at the ground's speed there in earth, 5 m/s, and its place is the place
    /// in earth, a quarter turn from its place on the ground. Read in the turning ground, it would show no speed, and an
    /// orbit's speed would miss the ground's turn by up to 480 m/s.
    /// </summary>
    [Fact]
    public void ARowHoldsThePersonsPlaceAndSpeedInEarthNotInTheTurningGround()
    {
        IReadOnlyList<ReadoutRow> rows = ReadoutLog.Record(TwoFilms with { Scene = "readout-turning" }, [Turning]);

        rows.Should().HaveCount(5);
        foreach (ReadoutRow row in rows)
        {
            row.Speed.Should().BeApproximately(GroundRate * 10, 1e-4,
                $"the person moves with the ground in earth at {row.Frame}");
            row.Position.Length.Should().BeApproximately(10, 1e-6);
            Math.Abs(row.Position.X).Should().BeLessThan(1,
                $"the ground stands a quarter turn from earth's axes at {row.Frame}, so the place in earth is not {row.Position}");
            row.ToStation.Should().BeApproximately(Double3.Distance(row.Position, new(10, 0, 0)), 1e-6,
                "the station's place is read in earth too");
        }
    }

    [Fact]
    public void ARowHoldsTheDistanceFromThePersonToTheStationsCenter()
    {
        foreach (ReadoutRow row in Rows)
            row.ToStation.Should()
                .BeApproximately(Math.Sqrt(30 * 30 + (5 - row.Position.Y) * (5 - row.Position.Y)), 1e-9);
    }

    [Fact]
    public void ARowHoldsTheDistanceFromThePersonToTheStationsAxis()
    {
        // The station's axis runs along earth's +X at the ball's first height, 5 m, so the ball falls away from it.
        foreach (ReadoutRow row in Rows)
            row.Axis.Should().BeApproximately(5 - row.Position.Y, 1e-9);
    }

    [Fact]
    public void ARowHoldsTheFeltGravityAtThePersonsPlace()
    {
        foreach (ReadoutRow row in Rows)
            row.Felt.Should().BeApproximately(G, 1e-5);
    }

    [Fact]
    public void TheCsvOfALogReadsBackToTheSameRows()
    {
        IReadOnlyList<ReadoutRow> rows = Rows;

        IReadOnlyList<ReadoutRow> read = ReadoutLog.FromCsv(ReadoutLog.ToCsv(rows));

        read.Should().Equal(rows);
    }

    [Fact]
    public void TheCsvOfALogHasAHeaderAndOneLineForEachRow()
    {
        string[] lines = ReadoutLog.ToCsv(Rows).TrimEnd('\n').Split('\n');

        lines[0].Should().Be("frame,tick,x,y,z,speed,station,axis,felt," +
                             "throw1_x,throw1_y,throw1_z,throw1_vx,throw1_vy,throw1_vz,throw1_time,throw1_land_x,throw1_land_y,throw1_land_z," +
                             "throw2_x,throw2_y,throw2_z,throw2_vx,throw2_vy,throw2_vz,throw2_time,throw2_land_x,throw2_land_y,throw2_land_z");
        lines.Should().HaveCount(6);
        lines[1].Should().StartWith("a-000000,2,0,");
    }

    [Fact]
    public void ALogWithAnotherHeaderDoesNotRead()
    {
        string csv = ReadoutLog.ToCsv(Rows).Replace("frame,tick,", "frame,time,");

        Action read = () => ReadoutLog.FromCsv(csv);

        read.Should().Throw<FormatException>().WithMessage("*starts with the line*");
    }

    [Fact]
    public void ALogLineWithAMissingCellDoesNotRead()
    {
        string[] lines = ReadoutLog.ToCsv(Rows).TrimEnd('\n').Split('\n');
        lines[2] = lines[2][..lines[2].LastIndexOf(',')];

        Action read = () => ReadoutLog.FromCsv(string.Join('\n', lines));

        read.Should().Throw<FormatException>().WithMessage("Line 3 of the readout log has 28 cells, not 29.");
    }

    /// <summary>
    /// A row before a throw leaves the throw's cells empty, a row after the release fills its release and leaves its
    /// landing empty, and a row after the landing fills both. Each reads back to the same row.
    /// </summary>
    [Fact]
    public void TheCsvHoldsEachThrowFromItsReleaseAndItsLandingFromTheLandingOn()
    {
        var release = new ThrowRelease(new(-98, -2, 3.5), new(4, 0, 6.9));
        ReadoutRow before = Rows[0];
        ReadoutRow flying = before with { FirstThrow = new ThrowReadout(release, null) };
        ReadoutRow landed = before with
        {
            FirstThrow = new ThrowReadout(release, new ThrowLanding(0.83, new(-99.5, -2, 9.4))),
            SecondThrow = new ThrowReadout(release with { Velocity = -release.Velocity }, null)
        };

        string csv = ReadoutLog.ToCsv([before, flying, landed]);

        csv.Split('\n')[1].Should().EndWith(new(',', 20));
        ReadoutLog.FromCsv(csv).Should().Equal(before, flying, landed);
    }

    /// <summary>
    /// The log of the exit's short script, which films the coda, holds each throw of the person's <see cref="BoxPlay"/>
    /// from the frame after its release on, in the station's axes, as its box's <see cref="FlightRecorder"/> records it:
    /// the release from the first frame after the throw and the landing from the first frame after the landing. At the
    /// last frame, both throws have landed where the straight lines of their releases say.
    /// </summary>
    [Fact]
    public void TheLogOfTheExitsScriptHoldsEachThrowFromItsBoxsFlightRecorder()
    {
        Script script =
            Script.Parse(File.ReadAllText(Path.Combine(HostTesting.RepositoryRoot(), "scripts", "demo-exit.json")));

        IReadOnlyList<ReadoutRow> rows = ReadoutLog.Record(script, DemoScenes.All);

        rows.TakeWhile(row => !row.Frame.StartsWith("spinward", StringComparison.Ordinal)).Should()
            .OnlyContain(row => row.FirstThrow == null && row.SecondThrow == null);
        ReadoutRow last = rows[^1];
        foreach (ThrowReadout? readout in new[] { last.FirstThrow, last.SecondThrow })
        {
            ThrowReadout thrown = readout!.Value;
            ThrowLanding line = thrown.Release.Landing(DemoScenes.SpinStation.Rate, DemoScenes.DemoStation.WheelFloor,
                DemoScenes.WheelCoda.BoxHalf);
            Double3.Distance(thrown.Landing!.Value.Place, line.Place).Should().BeLessThan(1e-3);
        }

        static Double3 Spinward(ThrowReadout? thrown) => WheelFloor.SpinwardAt(thrown!.Value.Release.Place);
        Double3.Dot(last.FirstThrow!.Value.Release.Velocity, Spinward(last.FirstThrow)).Should()
            .BePositive("the first throw goes spinward");
        Double3.Dot(last.SecondThrow!.Value.Release.Velocity, Spinward(last.SecondThrow)).Should()
            .BeNegative("the second throw goes antispinward");
        int firstLanded = rows.ToList().FindIndex(row => row.FirstThrow?.Landing != null);
        rows[firstLanded - 1].FirstThrow.Should().NotBeNull("the release comes before the landing");
        rows[firstLanded].Frame.Should().StartWith("spinward");
    }

    [Fact]
    public void TheDemosCommandLineWritesTheLogOfAScript()
    {
        string folder = HostTesting.NewFolder();
        try
        {
            string script = Path.Combine(folder, "two-films.json"), log = Path.Combine(folder, "log.csv");
            File.WriteAllText(script, TwoFilms.ToJson());
            var output = new StringWriter();
            var error = new StringWriter();

            int exit = Program.Readouts(args: ["--readouts", script, log], scenes: [Drop], output: output, error: error);

            exit.Should().Be(ScriptCommand.Ok, error.ToString());
            ReadoutLog.FromCsv(File.ReadAllText(log)).Should().Equal(Rows);
            output.ToString().Trim().Should().Be($"{log}: 5 rows");
        }
        finally
        {
            Directory.Delete(folder, recursive: true);
        }
    }

    [Theory]
    [InlineData("--readouts")]
    [InlineData("--readouts", "a.json")]
    [InlineData("--readouts", "a.json", "b.csv", "c")]
    public void TheCommandLineRefusesReadoutsWithoutAScriptAndALog(params string[] args)
    {
        var error = new StringWriter();

        int exit = Program.Readouts(args: args, scenes: [Drop], output: new StringWriter(), error: error);

        exit.Should().Be(ScriptCommand.CommandLineError);
        error.ToString().Should().Contain("--readouts <script> <log.csv>");
    }

    [Fact]
    public void TheCommandLineSendsReadoutsToTheLog()
    {
        var error = new StringWriter();

        int exit = Program.Run(["--readouts"], new StringWriter(), error);

        exit.Should().Be(ScriptCommand.CommandLineError);
        error.ToString().Should().Contain("--readouts <script> <log.csv>");
    }
}
