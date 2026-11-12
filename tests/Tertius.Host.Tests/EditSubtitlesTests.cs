using Tertius.Demo;
using Tertius.Host.Scripting;

namespace Tertius.Host.Tests;

/// <summary>
/// The subtitle files of the demo edit, in the ASS format that libass draws (docs/plans/blue-danube-demo.md, "The readouts
/// on the frame", "The cut list" and "The title cards and the transitions"). Frame k of the master shows from k / 60 s,
/// rounded down to the hundredth, to the next frame's time.
/// </summary>
public sealed class EditSubtitlesTests
{
    private static readonly Script Films = new(
        "scene",
        [
            new(new CameraStep("one")),
            new(new FilmStep("a", 100, 1)),
            new(new OutlinesStep(true)),
            new(new FilmStep("b", 120, 4)),
            new(new OutlinesStep(false)),
            new(new FilmStep("c", 80, 1)),
        ]
    );

    private static IReadOnlyList<ScriptFilm> FilmsOf => ScriptFilm.Of(Films);

    /// <summary>One row for each frame of <see cref="Films"/>, at the frame's tick, with a speed of 1,000 m/s plus its index.</summary>
    private static IReadOnlyList<ReadoutRow> Rows =>
        [
            .. FilmsOf
                .SelectMany(film => Enumerable.Range(0, film.Frames).Select(k => (film, k)))
                .Select(
                    (frame, index) =>
                        new ReadoutRow(
                            Frame: $"{frame.film.Name}-{frame.k:000000}",
                            Tick: frame.film.FirstTick + (frame.k + 1L) * frame.film.Every,
                            Position: new(DemoScenes.EarthRadius + 200e3, 0, 0),
                            Speed: 1000 + index,
                            ToStation: 2e6,
                            Axis: 0,
                            Felt: 0
                        )
                ),
        ];

    private static readonly TitleCard Card = new(
        2,
        [new("Tertius", 96), new("", 24), new("A {gray} line", 24, Gray: true)]
    );

    private static CutList Cuts(
        IReadOnlyList<EditTrim>? trims = null,
        IReadOnlyList<EditPart>? parts = null,
        IReadOnlyList<PanelSpan>? panels = null
    ) => new(Parts: parts ?? [], Trims: trims ?? [], Panels: panels ?? [], Opening: Card, Closing: Card);

    private static string Subtitles(CutList cuts) => EditSubtitles.Film(Stretch.Of(FilmsOf, cuts.Trims), Rows, cuts);

    /// <summary>The events of the style <paramref name="style"/>: their start, their end and their text.</summary>
    private static List<(string Start, string End, string Text)> Events(string ass, string style) =>
        [
            .. ass.Split('\n')
                .Where(line => line.StartsWith("Dialogue: ", StringComparison.Ordinal))
                .Select(line => line["Dialogue: ".Length..].Split(',', 10))
                .Where(cells => cells[3] == style)
                .Select(cells => (cells[1], cells[2], cells[9])),
        ];

    [Theory]
    [InlineData(0, "0:00:00.00")]
    [InlineData(1, "0:00:00.01")]
    [InlineData(2, "0:00:00.03")]
    [InlineData(3, "0:00:00.05")]
    [InlineData(59, "0:00:00.98")]
    [InlineData(60, "0:00:01.00")]
    [InlineData(216_000, "1:00:00.00")]
    public void AFramesTimeIsItsIndexOver60RoundedDownToTheHundredth(int frame, string time) =>
        EditSubtitles.Time(frame).Should().Be(time);

    [Fact]
    public void TheFilmHasOneReadoutEventForEachKeptFrameFromItsTimeToTheNextFramesTime()
    {
        List<(string Start, string End, string Text)> readouts = Events(Subtitles(Cuts([new("b", 10, 0)])), "Readout");

        int[] kept = [.. Enumerable.Range(0, 100), .. Enumerable.Range(110, 110), .. Enumerable.Range(220, 80)];
        readouts
            .Select(e => (e.Start, e.End))
            .Should()
            .Equal(kept.Select(k => (EditSubtitles.Time(k), EditSubtitles.Time(k + 1))));
    }

    [Fact]
    public void AReadoutEventHoldsTheReadoutsOfItsFramesRow()
    {
        List<(string Start, string End, string Text)> readouts = Events(Subtitles(Cuts()), "Readout");

        readouts[150]
            .Text.Should()
            .Contain("SPEED")
            .And.Contain("1,150.0\\hm/s")
            .And.Contain("200.00\\hkm")
            .And.Contain("2,000.0\\hkm")
            .And.Contain("0.00\\hg");
        readouts[150].Text.Split("\\N").Should().HaveCount(4);
    }

    [Fact]
    public void AClockEventHoldsTheSimClockOfItsFramesRowAndTheRateOfItsFilm()
    {
        List<(string Start, string End, string Text)> clocks = Events(Subtitles(Cuts()), "Clock");

        // Frame 150 is frame 50 of b, which starts at tick 100 and films every 4th tick: tick 304, 5.0 s.
        clocks.Should().HaveCount(300);
        clocks[150].Text.Should().Contain("T+\\h0:00:05.0").And.Contain("TIME-LAPSE\\h×4");
        clocks[0].Text.Should().Contain("REAL\\hTIME");
    }

    [Fact]
    public void TheRateOfAFilmOfEveryTickIsGrayAndOfATimeLapseAmber()
    {
        List<(string Start, string End, string Text)> clocks = Events(Subtitles(Cuts()), "Clock");

        // Frame 0 is in a, of every tick, and frame 150 in b, of every 4th tick.
        clocks[0].Text.Should().Contain("{\\c&HA0A0A0&}REAL\\hTIME");
        clocks[150].Text.Should().Contain("{\\c&H00B0FF&}TIME-LAPSE\\h×4");
    }

    [Fact]
    public void APartsTitleShowsForFourSecondsFromTheFirstKeptFrameOfItsFirstFilm()
    {
        string ass = Subtitles(Cuts(trims: [new("b", 10, 0)], parts: [new("b", "The launch", "Up the ramp.")]));

        List<(string Start, string End, string Text)> titles = Events(ass, "Title");

        titles.Should().ContainSingle();
        titles[0].Start.Should().Be(EditSubtitles.Time(110));
        titles[0].End.Should().Be(EditSubtitles.Time(110 + 240));
        titles[0].Text.Should().Contain("The launch").And.Contain("Up the ramp.").And.Contain("\\fad(600,600)");
    }

    [Fact]
    public void ACheckPanelShowsOverTheKeptFramesFromItsFirstFilmThroughItsLastFilm()
    {
        static string[] panel(ReadoutRow first, ReadoutRow row) => [$"from {first.Frame} at {row.Frame}"];

        List<(string Start, string End, string Text)> panels = Events(
            Subtitles(Cuts(trims: [new("b", 10, 0)], panels: [new("b", "c", panel)])),
            "Panel"
        );

        panels.Should().HaveCount(110 + 80);
        panels[0].Start.Should().Be(EditSubtitles.Time(110));
        panels[^1].End.Should().Be(EditSubtitles.Time(300));
        // The panel measures from the first frame of its first film, also when a trim drops that frame.
        panels[0].Text.Should().Be("from\\hb-000000\\hat\\hb-000010");
    }

    [Fact]
    public void TheLegendOfTheFramesShowsOverTheStretchesOfTheFilmsWithOutlines()
    {
        List<(string Start, string End, string Text)> legends = Events(Subtitles(Cuts([new("b", 10, 0)])), "Legend");

        legends.Should().ContainSingle();
        legends[0].Start.Should().Be(EditSubtitles.Time(110));
        legends[0].End.Should().Be(EditSubtitles.Time(220));
        legends[0].Text.Should().Contain("falling\\hframe");
    }

    [Fact]
    public void TheFilmIsDrawnAt1280By720()
    {
        string ass = Subtitles(Cuts());

        ass.Should().Contain("PlayResX: 1280\n").And.Contain("PlayResY: 720\n");
    }

    [Fact]
    public void ACardShowsItsLinesForItsSeconds()
    {
        string ass = EditSubtitles.Card(Card);

        List<(string Start, string End, string Text)> lines = Events(ass, "Card");
        lines.Should().ContainSingle();
        lines[0].Start.Should().Be("0:00:00.00");
        lines[0].End.Should().Be("0:00:02.00");
        lines[0].Text.Should().StartWith("{\\q0}{\\fs96\\c&HF2F2F2&}Tertius\\N", "a card wraps a line that is wider than its margins");
    }

    [Fact]
    public void ACardKeepsBracesOfItsTextOutOfTheOverrideCodes()
    {
        string text = Events(EditSubtitles.Card(Card), "Card")[0].Text;

        text.Should().Contain("A (gray) line");
    }

    [Fact]
    public void TheEditRefusesAPartOfAFilmThatTheScriptDoesNotHave()
    {
        Action subtitles = () => Subtitles(Cuts(parts: [new("d", "Title", "Subtitle")]));

        subtitles.Should().Throw<ArgumentException>().WithMessage("*'d'*");
    }

    [Fact]
    public void TheEditRefusesAPanelOfAFilmThatTheScriptDoesNotHave()
    {
        static string[] panel(ReadoutRow _1, ReadoutRow _2) => ["x"];

        Action subtitles = () => Subtitles(Cuts(panels: [new("a", "d", panel)]));

        subtitles.Should().Throw<ArgumentException>().WithMessage("*'d'*");
    }
}
