using Tertius.Host.Scripting;
using static Tertius.Host.Tests.HostTesting;

namespace Tertius.Host.Tests;

/// <summary>
/// The checked-in script scripts/hallway-walk.json, the film of issue #21: the person turns to the aft wall of the orbit
/// scene's cabin, walks there, turns to the nose and films the walk along the cabin's center line.
/// docs/research/hallway-light-grid/analyze.py reads its frames by number, so the fast test pins the films. The explicit
/// test pins the person's start and end. The analysis also assumes facts that no test here pins: the eye 1.65 m above the
/// floor (DemoScenes), the vertical field of view of 60 degrees (SceneBatcher's default), and a z that falls 0.05 m per
/// frame from frame 31 on.
/// </summary>
public sealed class HallwayWalkTests
{
    [Fact]
    public void TheFilmHoldsThirtyStillFramesThenWalksElevenHundredThenHoldsSixty()
    {
        var script = Script.Parse(File.ReadAllText(Path.Combine(RepositoryRoot(), "scripts", "hallway-walk.json")));

        IEnumerable<(string Name, int Frames, int Every)> films = script
            .Steps.Select(step => step.Value)
            .OfType<FilmStep>()
            .Select(film => (film.Name, film.Frames, film.Every));

        films.Should().Equal(("start", 30, 1), ("walk", 1100, 1), ("end", 60, 1));
    }

    /// <summary>
    /// The person's path along the cabin (tests/STYLE.md, "Long tests"). Explicit, because its one run renders all 1,190
    /// film frames, about 5 minutes. It makes its own run instead of a class fixture, because xUnit builds a class fixture
    /// even when it skips every test of the class.
    /// </summary>
    public sealed class AlongTheCabin
    {
        [Fact(Explicit = true)]
        public void TheFilmedWalkRunsAlongTheCabinsCenterLineFromTheAftWallToShortOfTheNoseWall()
        {
            using var run = new HallwayWalkRun();
            Double3 start = run.Result.Dump("aft-wall").Entity("person").Pose.Position,
                end = run.Result.Dump("nose-wall").Entity("person").Pose.Position;

            // The aft wall's inner face is at z = 29.5 m and the capsule's radius is 0.3 m. The floor is at y = -3.5 m and
            // the capsule's center rests 0.9 m above it. Of the 1,100 filmed ticks at 3 m/s, the first two do not move yet:
            // 1,098 × 0.05 m = 54.9 m. So the person stops 3.78 m from the nose wall's inner face at z = -29.5 m, and the
            // camera never looks out of the nose window. An end within 1 cm of x = 0 after 54.9 m bounds the walk's
            // heading error to 0.0002 rad, so the view faces the nose.
            Double3.Distance(start, new(0, -2.6, 29.18)).Should().BeLessThan(0.01, $"the walk starts at {start}");
            Double3.Distance(end, new(0, -2.6, -25.72)).Should().BeLessThan(0.01, $"the walk ends at {end}");
        }
    }
}

/// <summary>One headless run of scripts/hallway-walk.json, which renders all 1,190 film frames: about 5 minutes.</summary>
public sealed class HallwayWalkRun() : CheckedInRun("hallway-walk");
