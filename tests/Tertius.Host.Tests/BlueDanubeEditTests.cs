using Tertius.Demo;
using Tertius.Host.Scripting;
using static Tertius.Host.Tests.HostTesting;

namespace Tertius.Host.Tests;

/// <summary>
/// The cut list of the Blue Danube demo against the checked-in long script, scripts/blue-danube.json: it names only films
/// of the script, and the edit can cut the script with it.
/// </summary>
public sealed class BlueDanubeEditTests
{
    private static IReadOnlyList<ScriptFilm> Films { get; } = ScriptFilm.Of(Script.Load(Path.Combine(RepositoryRoot(), "scripts", "blue-danube.json")));

    private static int IndexOf(string film) => Films.ToList().FindIndex(f => f.Name == film);

    [Fact]
    public void TheCutListNamesOnlyFilmsOfTheCheckedInScript()
    {
        CutList cuts = BlueDanubeEdit.Cuts;
        IEnumerable<string> named = [.. cuts.Parts.Select(p => p.FirstFilm), .. cuts.Trims.Select(t => t.Film), .. cuts.Panels.SelectMany(p => (string[])[p.FirstFilm, p.LastFilm])];

        named.Should().OnlyContain(film => IndexOf(film) >= 0);
    }

    [Fact]
    public void TheEditCanCutTheCheckedInScriptWithTheCutList()
    {
        List<ReadoutRow> rows = [.. Films.SelectMany(film => Enumerable.Range(0, film.Frames).Select(k => new ReadoutRow($"{film.Name}-{k:000000}", film.FirstTick, new Double3(DemoScenes.EarthRadius, 0, 0), 0, 0, 0)))];

        Action cut = () => EditSubtitles.Film(Stretch.Of(Films, BlueDanubeEdit.Cuts.Trims), rows, BlueDanubeEdit.Cuts);

        cut.Should().NotThrow();
    }

    [Fact]
    public void ThePartsStartWithTheFirstFilmAndFollowTheOrderOfTheScript()
    {
        int[] starts = [.. BlueDanubeEdit.Cuts.Parts.Select(part => IndexOf(part.FirstFilm))];

        starts[0].Should().Be(0);
        starts.Should().BeInAscendingOrder().And.OnlyHaveUniqueItems();
    }

    [Fact]
    public void TheWeightlessPartStartsWithTheFirstFilmInOrbit()
    {
        // The climb ends when the ascent autopilot is in orbit.
        EditPart weightless = BlueDanubeEdit.Cuts.Parts.Single(part => part.Title == "Weightless");

        IndexOf(weightless.FirstFilm).Should().Be(IndexOf("climb-high") + 1);
    }

    [Fact]
    public void TheParkingOrbitPanelSpansTheCoastFromTheInsertionToTheTransferBurn()
    {
        PanelSpan panel = BlueDanubeEdit.Cuts.Panels.Single();

        // The climb ends when the ascent autopilot is in orbit, and the film burn holds the transfer burn.
        IndexOf(panel.FirstFilm).Should().Be(IndexOf("climb-high") + 1);
        IndexOf(panel.LastFilm).Should().Be(IndexOf("burn") - 1);
    }
}
