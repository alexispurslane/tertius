using Tertius.Host.Scripting;

namespace Tertius.Host.Tests;

/// <summary>
/// The frame names of a film: <see cref="FilmStep.FrameName"/> names only frames that the film has, so every name that it
/// returns is one that <see cref="FilmStep.HasFrameNamed"/> accepts. See docs/plans/blue-danube-demo.md, change 2.
/// </summary>
public sealed class FilmFrameNameTests
{
    [Fact]
    public void EveryFrameNameOfAFilmIsTheFilmsNameADashAndTheFrameNumberInSixDigitsAndTheFilmRecognizesIt()
    {
        var film = new FilmStep("climb", 3, 2);

        string[] names = [.. Enumerable.Range(0, film.Frames).Select(film.FrameName)];

        names.Should().Equal("climb-000000", "climb-000001", "climb-000002");
        names.Should().OnlyContain(name => film.HasFrameNamed(name));
    }

    /// <summary>The film "climb" has 3 frames, numbered 0 to 2, so none of these numbers names a frame of it.</summary>
    [Theory]
    [InlineData(-1)]
    [InlineData(3)]
    [InlineData(1_000_000)]
    public void AFrameNumberOutsideTheFilmIsRefusedInsteadOfGivingANameThatTheFilmDoesNotRecognize(int number)
    {
        var film = new FilmStep("climb", 3, 2);

        Func<string> name = () => film.FrameName(number);

        name.Should().Throw<ArgumentOutOfRangeException>().Which.ParamName.Should().Be("number");
    }
}
