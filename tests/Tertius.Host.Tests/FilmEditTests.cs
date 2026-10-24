using Tertius.Demo;
using Tertius.Host.Scripting;

namespace Tertius.Host.Tests;

/// <summary>
/// The pure parts of the demo edit (docs/plans/blue-danube-demo.md, "The edit"): where the frames of each film of a script
/// lie in the script's video, and the stretches that the cut list keeps of them.
/// </summary>
public sealed class FilmEditTests
{
    private static readonly Script ThreeFilms = ScriptOf(
        new CameraStep("one"),
        new FilmStep("a", 100, 1),
        new RunStep(7),
        new FilmStep("b", 200, 4),
        new CameraStep("two"),
        new PostStep(new ToEntity("ship"), "Go"),
        new FilmStep("c", 300, 2)
    );

    private static Script ScriptOf(params object[] steps) =>
        new(
            "scene",
            [
                .. steps.Select(step =>
                    step switch
                    {
                        RunStep run => new(run),
                        CameraStep camera => new(camera),
                        FilmStep film => new(film),
                        PostStep post => new(post),
                        OutlinesStep outlines => new(outlines),
                        CaptureStep capture => new ScriptStep(capture),
                        _ => throw new ArgumentException("Not a step.", nameof(steps)),
                    }
                ),
            ]
        );

    [Fact]
    public void AFilmsFirstFrameIsTheCountOfTheFramesOfTheFilmsBeforeIt() =>
        ScriptFilm.Of(ThreeFilms).Select(film => film.FirstFrame).Should().Equal(0, 100, 300);

    [Fact]
    public void AFilmsFirstTickCountsTheTicksOfTheRunsAndTheFilmsBeforeIt() =>
        // a runs 100 ticks, the run step 7, and b 800.
        ScriptFilm.Of(ThreeFilms).Select(film => film.FirstTick).Should().Equal(0, 107, 907);

    [Fact]
    public void AFilmKeepsItsNameItsFramesAndItsTicksPerFrame() =>
        ScriptFilm
            .Of(ThreeFilms)
            .Select(film => (film.Name, film.Frames, film.Every))
            .Should()
            .Equal(("a", 100, 1), ("b", 200, 4), ("c", 300, 2));

    [Fact]
    public void AFilmIsFilmedByTheCameraOfTheLastCameraStepBeforeIt() =>
        ScriptFilm.Of(ThreeFilms).Select(film => film.Camera).Should().Equal("one", "one", "two");

    [Fact]
    public void AFilmShowsOutlinesWhileTheLastOutlinesStepBeforeItTurnedThemOn()
    {
        Script script = ScriptOf(
            new CameraStep("one"),
            new FilmStep("a", 1, 1),
            new OutlinesStep(true),
            new FilmStep("b", 1, 1),
            new FilmStep("c", 1, 1),
            new OutlinesStep(false),
            new FilmStep("d", 1, 1)
        );

        ScriptFilm.Of(script).Select(film => film.Outlines).Should().Equal(false, true, true, false);
    }

    [Fact]
    public void TheEditRefusesAScriptWithACaptureStep()
    {
        Script script = ScriptOf(new CameraStep("one"), new FilmStep("a", 1, 1), new CaptureStep("still"));

        Action of = () => ScriptFilm.Of(script);

        of.Should().Throw<ScriptException>().WithMessage("*capture 'still'*film steps*");
    }

    [Fact]
    public void EveryFilmBecomesOneStretchOfAllItsFramesInTheScriptsOrder()
    {
        IReadOnlyList<Stretch> stretches = Stretch.Of(ScriptFilm.Of(ThreeFilms), trims: []);

        stretches
            .Select(stretch => (stretch.Film.Name, stretch.From, stretch.Count))
            .Should()
            .Equal(("a", 0, 100), ("b", 100, 200), ("c", 300, 300));
    }

    [Fact]
    public void ATrimDropsFramesFromTheHeadAndTheTailOfItsFilm()
    {
        IReadOnlyList<Stretch> stretches = Stretch.Of(ScriptFilm.Of(ThreeFilms), [new("b", Head: 30, Tail: 20)]);

        stretches
            .Select(stretch => (stretch.Film.Name, stretch.From, stretch.Count))
            .Should()
            .Equal(("a", 0, 100), ("b", 130, 150), ("c", 300, 300));
    }

    [Fact]
    public void TheEditRefusesATrimThatLeavesLessThanOneSecondOfItsFilm()
    {
        Action trim = () => Stretch.Of(ScriptFilm.Of(ThreeFilms), [new("a", Head: 21, Tail: 20)]);

        trim.Should().Throw<ArgumentException>().WithMessage("*'a'*59 frames*");
    }

    [Fact]
    public void TheEditKeepsATrimThatLeavesExactlyOneSecondOfItsFilm()
    {
        IReadOnlyList<Stretch> stretches = Stretch.Of(ScriptFilm.Of(ThreeFilms), [new("a", Head: 20, Tail: 20)]);

        stretches[0].Count.Should().Be(60);
    }

    [Fact]
    public void TheEditRefusesATrimOfAFilmThatTheScriptDoesNotHave()
    {
        Action trim = () => Stretch.Of(ScriptFilm.Of(ThreeFilms), [new("d", Head: 1, Tail: 0)]);

        trim.Should().Throw<ArgumentException>().WithMessage("*'d'*");
    }

    [Fact]
    public void TheEditRefusesATrimOfNegativeFrames()
    {
        Action trim = () => Stretch.Of(ScriptFilm.Of(ThreeFilms), [new("b", Head: -1, Tail: 0)]);

        trim.Should().Throw<ArgumentException>();
    }

    /// <summary>One row for each frame of <paramref name="films"/>, named as the runner names the frames.</summary>
    private static List<ReadoutRow> RowsOf(IReadOnlyList<ScriptFilm> films) =>
        [
            .. films.SelectMany(film =>
                Enumerable
                    .Range(0, film.Frames)
                    .Select(k => new ReadoutRow(
                        Frame: $"{film.Name}-{k:000000}",
                        Tick: 0,
                        Position: default,
                        Speed: 0,
                        ToStation: 0,
                        Axis: 0,
                        Felt: 0
                    ))
            ),
        ];

    [Fact]
    public void TheEditTakesALogWithOneRowForEachFrameOfTheScriptNamedAfterIt()
    {
        IReadOnlyList<ScriptFilm> films = ScriptFilm.Of(ThreeFilms);

        Action check = () => ScriptFilm.CheckRows(films, RowsOf(films));

        check.Should().NotThrow();
    }

    [Fact]
    public void TheEditRefusesALogWithMoreOrFewerRowsThanTheScriptHasFrames()
    {
        IReadOnlyList<ScriptFilm> films = ScriptFilm.Of(ThreeFilms);
        List<ReadoutRow> rows = RowsOf(films);

        Action fewer = () => ScriptFilm.CheckRows(films, rows[..^1]);
        Action more = () => ScriptFilm.CheckRows(films, [.. rows, rows[0]]);

        fewer.Should().Throw<ScriptException>().WithMessage("*599 rows*600 frames*");
        more.Should().Throw<ScriptException>().WithMessage("*601 rows*600 frames*");
    }

    [Fact]
    public void TheEditRefusesALogWhoseRowIsNamedForAnotherFrame()
    {
        IReadOnlyList<ScriptFilm> films = ScriptFilm.Of(ThreeFilms);
        List<ReadoutRow> rows = RowsOf(films);
        rows[150] = rows[150] with { Frame = "b-000051" };

        Action check = () => ScriptFilm.CheckRows(films, rows);

        check.Should().Throw<ScriptException>().WithMessage("*150*'b-000051'*'b-000050'*");
    }
}
