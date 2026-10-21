using System.Text.Json;
using Tertius.Demo;
using Tertius.Host.Scripting;

namespace Tertius.Host.Tests;

/// <summary>
/// The cue sheet of the demo edit (docs/plans/blue-danube-demo.md, "The cue sheet"): where each film of the script, each
/// part's title and each card lie in the final video, for the score. The script films <c>a</c>, 100 frames of every tick,
/// <c>b</c>, 120 frames of every 4th tick, and <c>c</c>, 80 frames of every tick. The cut list trims 10 frames from the
/// head of <c>b</c> and 20 from its tail. The opening card lasts 2 s and the closing card 3 s.
/// </summary>
public sealed class CueSheetTests
{
    private static readonly Script Films = new("scene",
        [new(new CameraStep("one")), new(new FilmStep("a", 100, 1)), new(new FilmStep("b", 120, 4)), new(new FilmStep("c", 80, 1))]);

    private static readonly CutList Cuts = new(
        [new EditPart("a", "Earth", "The walk."), new EditPart("b", "The launch", "Up the ramp.")],
        [new EditTrim("b", Head: 10, Tail: 20)],
        [],
        new TitleCard(2, [new CardLine("Tertius", 96)]),
        new TitleCard(3, [new CardLine("The end", 32)]));

    private static CueSheet Sheet => CueSheet.Of(Stretch.Of(ScriptFilm.Of(Films), Cuts.Trims), Cuts);

    /// <summary>The time of frame <paramref name="frame"/> of the final video, in seconds.</summary>
    private static double At(int frame) => frame / 60.0;

    [Fact]
    public void TheOpeningCardStartsTheVideoAndTheFirstFilmStartsWhenItEnds()
    {
        CueSheet sheet = Sheet;

        sheet.Opening.Should().Be(new CardCue(0, 2));
        sheet.Films[0].Start.Should().Be(2);
    }

    [Fact]
    public void EachFilmStartsWhereTheFilmBeforeItEndsAndLastsItsKeptFramesAt60FramesPerSecond()
    {
        IReadOnlyList<FilmCue> films = Sheet.Films;

        films.Select(film => (film.Start, film.End)).Should().Equal((At(120), At(220)), (At(220), At(310)), (At(310), At(390)));
    }

    [Fact]
    public void EachFilmNamesItsFirstAndLastKeptFrameOfTheMaster()
    {
        // b's first frame in the master is 100, so its kept frames run from 110 to 199 after the trims of 10 and 20.
        Sheet.Films.Select(film => (film.Name, film.FirstFrame, film.LastFrame)).Should().Equal(("a", 0, 99), ("b", 110, 199), ("c", 220, 299));
    }

    [Fact]
    public void APartsTitleStartsWithTheFirstKeptFrameOfItsFirstFilm()
    {
        Sheet.Parts.Should().Equal(new PartCue("Earth", "a", At(120)), new PartCue("The launch", "b", At(220)));
    }

    [Fact]
    public void TheClosingCardStartsWhenTheLastFilmEndsAndEndsTheVideo()
    {
        CueSheet sheet = Sheet;

        sheet.Closing.Should().Be(new CardCue(At(390), At(390 + 180)));
        sheet.Length.Should().Be(At(570));
        sheet.FramesPerSecond.Should().Be(60);
    }

    [Fact]
    public void TheJsonOfTheSheetNamesEachFilmWithItsFramesAndTimes()
    {
        using JsonDocument json = JsonDocument.Parse(Sheet.ToJson());
        JsonElement root = json.RootElement;

        JsonElement b = root.GetProperty("films")[1];
        b.GetProperty("name").GetString().Should().Be("b");
        b.GetProperty("firstFrame").GetInt32().Should().Be(110);
        b.GetProperty("lastFrame").GetInt32().Should().Be(199);
        b.GetProperty("start").GetDouble().Should().Be(At(220));
        b.GetProperty("end").GetDouble().Should().Be(At(310));
        root.GetProperty("parts")[1].GetProperty("title").GetString().Should().Be("The launch");
        root.GetProperty("parts")[1].GetProperty("start").GetDouble().Should().Be(At(220));
        root.GetProperty("closing").GetProperty("start").GetDouble().Should().Be(At(390));
        root.GetProperty("length").GetDouble().Should().Be(At(570));
        root.GetProperty("framesPerSecond").GetInt32().Should().Be(60);
    }
}
