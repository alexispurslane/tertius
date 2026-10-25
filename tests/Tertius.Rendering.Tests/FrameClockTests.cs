namespace Tertius.Rendering.Tests;

/// <summary>
/// The clock that steps the exposure's approach (docs/plans/auto-exposure.md, "The clock"): a window's clock steps by
/// the wall time between drawn frames, a headless run's clock by the snapshot time, and a time that does not move
/// forward steps by 0.
/// </summary>
public sealed class FrameClockTests
{
    [Fact]
    public void TheFirstStepOfEitherClockIsEndless()
    {
        FrameClock.Window.Step(snapshotTime: 5, wallSeconds: 7).Seconds.Should().Be(float.PositiveInfinity);
        FrameClock.Film.Step(snapshotTime: 5, wallSeconds: 7).Seconds.Should().Be(float.PositiveInfinity);
    }

    [Fact]
    public void AWindowClockStepsByTheWallTimeBetweenDrawnFrames()
    {
        (FrameClock afterFirst, _) = FrameClock.Window.Step(snapshotTime: 0, wallSeconds: 10);

        afterFirst.Step(snapshotTime: 0, wallSeconds: 10.25).Seconds.Should().Be(0.25f);
    }

    [Fact]
    public void AFilmClockStepsByTheSnapshotTimeBetweenDrawnFrames()
    {
        (FrameClock afterFirst, _) = FrameClock.Film.Step(snapshotTime: 3, wallSeconds: 0);

        afterFirst.Step(snapshotTime: 3.5, wallSeconds: 0).Seconds.Should().Be(0.5f);
    }

    [Fact]
    public void AWindowClockIgnoresTheSnapshotTimeAndAFilmClockTheWallTime()
    {
        (FrameClock window, _) = FrameClock.Window.Step(snapshotTime: 1, wallSeconds: 10);
        (FrameClock film, _) = FrameClock.Film.Step(snapshotTime: 1, wallSeconds: 10);

        window.Step(snapshotTime: 100, wallSeconds: 10.5).Seconds.Should().Be(0.5f);
        film.Step(snapshotTime: 1.5, wallSeconds: 100).Seconds.Should().Be(0.5f);
    }

    [Theory]
    [InlineData(2.0)]
    [InlineData(1.0)]
    [InlineData(double.NaN)]
    public void ATimeThatDoesNotMoveForwardStepsByZero(double laterTime)
    {
        (FrameClock afterFirst, _) = FrameClock.Film.Step(snapshotTime: 2, wallSeconds: 0);

        afterFirst.Step(snapshotTime: laterTime, wallSeconds: 0).Seconds.Should().Be(0);
    }

    [Fact]
    public void AClockStepsFromTheFrameBeforeEvenWhenThatFrameWentBackInTime()
    {
        (FrameClock atTwo, _) = FrameClock.Film.Step(snapshotTime: 2, wallSeconds: 0);
        (FrameClock atOne, _) = atTwo.Step(snapshotTime: 1, wallSeconds: 0);

        atOne.Step(snapshotTime: 1.5, wallSeconds: 0).Seconds.Should().Be(0.5f);
    }
}
