using AwesomeAssertions;
using Tertius.Frames.Rails;

namespace Tertius.Simulation.Tests;

/// <summary>
/// The simulation clock counts whole ticks in doubles: the time of tick N is the start time plus N divided by the tick
/// rate, exactly as that division rounds, so 60 ticks at 60 ticks a second last exactly one second. The tick length that
/// the clock and its readers use is the double 1 / tick rate.
/// </summary>
public sealed class SimulationTimeTests
{
    /// <summary>
    /// 31 ticks also tell the division from a product of the tick count and the double tick length: 31 · (1.0 / 60) rounds
    /// to another double than 31 / 60.0.
    /// </summary>
    [Theory]
    [InlineData(30, 0)]
    [InlineData(60, 0)]
    [InlineData(30, 12.25)]
    [InlineData(31, 0)]
    [InlineData(31, 12.25)]
    public void AfterNTicksAtSixtyTicksASecondTheTimeIsTheStartTimePlusNSixtiethsOfASecond(int ticks, double startTime)
    {
        using var test = new TestWorld(configure: options =>
        {
            options.TickRate = 60;
            options.StartTime = startTime;
        });

        test.Run(ticks);

        test.World.Time.Should().Be(startTime + ticks / 60.0);
    }

    [Fact]
    public void TheTickThatStartsADayLaterAtSixtyTicksASecondStartsExactlyOneDayOfSecondsLater()
    {
        using var test = new TestWorld(configure: options =>
        {
            options.TickRate = 60;
            options.StartTime = 12.25;
        });

        test.World.TimeAt(60L * 86_400)
            .Should()
            .Be(
                12.25 + 86_400.0,
                "a day of ticks counts whole sixtieths of a second, with no rounded tick length summed"
            );
    }

    [Fact]
    public void AWorldKeepsTheTickRateAndTheStartTimeOfItsOptionsWhenTheCallerChangesThemLater()
    {
        var options = new WorldOptions { TickRate = 60, StartTime = 12.25 };
        using var world = new World(options);
        options.TickRate = 50;
        options.StartTime = 0;

        for (int i = 0; i < 60; i++)
            world.Tick();

        world.Time.Should().Be(13.25, "the world reads its options once, when it is created");
    }

    [Fact]
    public void TheTimeThatATickHoldsIsTimeAtOfThatTick()
    {
        using var test = new TestWorld(configure: options => options.StartTime = 12.25);
        var seen = new List<(double time, double timeAt)>();
        test.SpawnWith(
            Lambda.OnTick((self, scoped ref events, scoped ref outbox) => seen.Add((self.Time, self.TimeAt(self.Tick))))
        );

        test.Run(7);

        seen.Should().HaveCount(7).And.OnlyContain(each => each.time == each.timeAt);
    }

    [Fact]
    public void ATickEventCarriesTheTickLengthOneOverTheTickRateInDoubles()
    {
        using var test = new TestWorld(configure: options => options.TickRate = 60);
        var ticker = new Recorder(EventMask.Of<Tick>());
        test.SpawnWith(ticker);

        test.Run(2);

        List<double> tickLengths = [.. ticker.Seen.Select(seen => seen.Event.As<Tick>().DeltaTime)];
        tickLengths.Should().Equal(1.0 / 60, 1.0 / 60);
    }

    /// <summary>
    /// The frame's first motion intent moves it along the orbit over its first step, so after one tick it stands where the
    /// orbit puts it at 1/60 s. A tick length rounded to a float would put it 8.7e-10 s further on, about 1.4e-6 m at the
    /// orbital speed of 1571 m/s.
    /// </summary>
    [Fact]
    public void AKeplerFrameOneTickAfterItsSpawnStandsWhereItsOrbitPutsItOneSixtiethOfASecondLater()
    {
        const double radius = 1000;
        const double periodSeconds = 4;
        double meanMotion = 2 * Math.PI / periodSeconds;
        using var test = new TestWorld(configure: options => options.TickRate = 60);
        test.GiveRootPointGravity(meanMotion * meanMotion * radius * radius * radius);
        Entity moon = test.World.Spawn(
            new SpawnDesc { Frame = test.Root, AsFrame = new(FrameBounds.Sphere(1)) }.OnKeplerOrbit(
                test.World,
                KeplerOrbit.Circular(radius),
                firstMove: true
            )
        );

        test.Run(1);

        double anomaly = meanMotion / 60;
        PoseChecks.AssertNear(
            test.World.Frames.LocalPose(test.World.GetOwnedFrame(moon)).Position,
            new(radius * Math.Cos(anomaly), 0, -radius * Math.Sin(anomaly)),
            1e-9
        );
    }
}
