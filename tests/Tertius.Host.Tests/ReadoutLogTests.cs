using Tertius.Demo;
using Tertius.Host.Scripting;
using Tertius.Simulation;
using static Tertius.Testing.WorldTesting;

namespace Tertius.Host.Tests;

/// <summary>
/// The readout log of the demo edit (docs/plans/blue-danube-demo.md, "The readout log"): a playback that draws nothing and
/// adds one row at each capture of a script, on the real World and the real Jolt backend. The scene of these tests drops a
/// ball, the <c>person</c>, from 5 m in the root frame <c>earth-surface</c>, whose uniform gravity is 9.81 m/s². The
/// <c>station</c> is a static box 30 m to the side at the ball's first height.
/// </summary>
public sealed class ReadoutLogTests
{
    private const float G = 9.81f;

    private static readonly Scene Drop = new("readout-drop", (world, _) =>
    {
        var scene = new SceneInstance();
        FrameHandle surface = Ground(world, G);
        scene.Add("earth-surface", surface.Entity);
        scene.Add("person", world.Spawn(new SpawnDesc { Frame = surface, Pose = At(0, 5), Body = Ball() }));
        scene.Add("station", world.Spawn(new SpawnDesc
        {
            Frame = surface,
            Pose = At(30, 5),
            Body = new BodyDesc(new BoxShapeDesc(new System.Numerics.Vector3(1)), BodyMotion.Static),
        }));
        scene.Add("camera", world.Spawn(new SpawnDesc { Frame = surface, Pose = At(0, 2, 10), Render = new RenderData() }));
        return scene;
    });

    /// <summary>A film of 3 frames every 2 ticks, then a film of 2 frames every tick: frames after ticks 2, 4, 6, 7 and 8.</summary>
    private static readonly Script TwoFilms = new("readout-drop", [new(new CameraStep("camera")), new(new FilmStep("a", 3, 2)), new(new FilmStep("b", 2, 1))], new ScriptWindow(16, 16));

    private static IReadOnlyList<ReadoutRow> Rows => ReadoutLog.Record(TwoFilms, [Drop]);

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
    public void ARowHoldsThePersonsPositionInEarthSurface()
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
    public void ARowHoldsThePersonsSpeedInEarthSurface()
    {
        // The ball falls freely, so its speed grows by g each second from one row to the next. The reads describe the last
        // finished step, so the speed itself lags the tick number, and the test reads only its growth.
        IReadOnlyList<ReadoutRow> rows = Rows;
        rows[0].Speed.Should().BePositive();
        for (int i = 1; i < rows.Count; i++)
            (rows[i].Speed - rows[i - 1].Speed).Should().BeApproximately(G * Seconds(rows[i].Tick - rows[i - 1].Tick), 1e-4);
    }

    [Fact]
    public void ARowHoldsTheDistanceFromThePersonToTheStationsCenter()
    {
        foreach (ReadoutRow row in Rows)
            row.ToStation.Should().BeApproximately(Math.Sqrt(30 * 30 + (5 - row.Position.Y) * (5 - row.Position.Y)), 1e-9);
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

        lines[0].Should().Be("frame,tick,x,y,z,speed,station,felt");
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

        read.Should().Throw<FormatException>().WithMessage("Line 3 of the readout log has 7 cells, not 8.");
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

            int exit = Program.Readouts(["--readouts", script, log], [Drop], output, error);

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

        int exit = Program.Readouts(args, [Drop], new StringWriter(), error);

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
