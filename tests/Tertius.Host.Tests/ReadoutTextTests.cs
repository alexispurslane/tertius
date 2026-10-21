using Tertius.Demo;

namespace Tertius.Host.Tests;

/// <summary>
/// The text that the demo edit draws on each frame from the readout log (docs/plans/blue-danube-demo.md, "The readouts on
/// the frame" and "The textbook checks"): the numbers, the sim clock, the rate of a film and the check panel.
/// </summary>
public sealed class ReadoutTextTests
{
    /// <summary>A row of the person at <paramref name="altitude"/> above Earth's mean radius, on the X axis.</summary>
    private static ReadoutRow At(double altitude, long tick = 0, double speed = 0, double toStation = 0, double axis = 0, double felt = 0) =>
        new("f-000000", tick, new Double3(DemoScenes.EarthRadius + altitude, 0, 0), speed, toStation, axis, felt);

    [Theory]
    [InlineData(0.0, "0.0 m/s")]
    [InlineData(7788.149, "7,788.1 m/s")]
    [InlineData(12.36, "12.4 m/s")]
    public void SpeedShowsInMetersPerSecondWithOneDecimal(double speed, string text)
    {
        ReadoutText.Speed(speed).Should().Be(text);
    }

    [Theory]
    [InlineData(0.93, "0.9 m")]
    [InlineData(9_999.94, "9,999.9 m")]
    [InlineData(10_000, "10.00 km")]
    [InlineData(199_984.4, "199.98 km")]
    public void AltitudeShowsInMetersBelow10KilometersAndInKilometersAbove(double altitude, string text)
    {
        ReadoutText.Altitude(At(altitude).Position).Should().Be(text);
    }

    [Theory]
    [InlineData(152.34, "152.3 m")]
    [InlineData(9_999.94, "9,999.9 m")]
    [InlineData(10_000, "10.0 km")]
    [InlineData(1_523_440, "1,523.4 km")]
    public void TheDistanceToTheStationShowsInMetersBelow10KilometersAndInKilometersAbove(double distance, string text)
    {
        ReadoutText.Distance(distance).Should().Be(text);
    }

    [Theory]
    [InlineData(0.0, "0.00 g")]
    [InlineData(9.80665, "1.00 g")]
    [InlineData(29.4, "3.00 g")]
    [InlineData(490.3325, "50.00 g")]
    [InlineData(0.04, "0.00 g")]
    public void FeltGravityShowsInStandardGravitiesWithTwoDecimals(double felt, string text)
    {
        ReadoutText.Gravity(felt).Should().Be(text);
    }

    [Theory]
    [InlineData(0L, "T+ 0:00:00.0")]
    [InlineData(5L, "T+ 0:00:00.0")]
    [InlineData(6L, "T+ 0:00:00.1")]
    [InlineData(3_599L, "T+ 0:00:59.9")]
    [InlineData(20_430L, "T+ 0:05:40.5")]
    [InlineData(216_000L, "T+ 1:00:00.0")]
    [InlineData(291_942L, "T+ 1:21:05.7")]
    public void TheSimClockShowsTheTimeSinceTick0InHoursMinutesAndTenthsOfASecond(long tick, string text)
    {
        ReadoutText.Clock(tick).Should().Be(text);
    }

    [Theory]
    [InlineData(1, "REAL TIME")]
    [InlineData(4, "TIME-LAPSE ×4")]
    [InlineData(240, "TIME-LAPSE ×240")]
    public void TheRateOfAFilmOfEveryTickIsRealTimeAndOfEveryNthTickATimeLapseOfN(int every, string text)
    {
        ReadoutText.Rate(every).Should().Be(text);
    }

    [Fact]
    public void TheReadoutsAreFourLabeledLinesOfOneWidth()
    {
        string[] lines = ReadoutText.Lines(At(199_984.4, speed: 7788.149, toStation: 1_523_440, felt: 0));

        lines.Should().HaveCount(4);
        lines.Select(line => line.Length).Distinct().Should().ContainSingle();
        lines[0].Should().StartWith("SPEED").And.EndWith("7,788.1 m/s");
        lines[1].Should().StartWith("ALTITUDE").And.EndWith("199.98 km");
        lines[2].Should().StartWith("TO STATION").And.EndWith("1,523.4 km");
        lines[3].Should().StartWith("FELT GRAVITY").And.EndWith("0.00 g");
    }

    [Fact]
    public void TheParkingOrbitPanelNamesTheHeightOfTheRow()
    {
        string[] panel = CheckPanels.ParkingOrbit(At(200e3), At(199_960, tick: 600));

        panel[0].TrimEnd().Should().Contain("parking orbit").And.EndWith("200.0 km up");
    }

    [Fact]
    public void TheParkingOrbitPanelSetsTheCircularSpeedAtTheRowsRadiusBesideTheRunsSpeed()
    {
        // √(GM / r) at 200 km: 7,788.5 m/s.
        string[] panel = CheckPanels.ParkingOrbit(At(200e3), At(200e3, tick: 600, speed: 7788.12));

        panel[1].TrimEnd().Should().Contain("√(GM/r)").And.Contain("7,788.5 m/s").And.EndWith("run 7,788.1 m/s");
    }

    [Fact]
    public void TheParkingOrbitPanelSetsThePeriodOfACircleAtTheRowsRadiusBesideThePeriodOfTheAngleThatTheRunSwept()
    {
        // A circle at 200 km takes 2π √(r³ / GM) = 5,301.0 s, 88.4 min. A quarter turn in 1,350 s is a turn in 5,400 s, 90.0 min.
        double r = DemoScenes.EarthRadius + 200e3;
        var start = new ReadoutRow("orbit-000000", 1000, new Double3(r, 0, 0), 0, 0, 0, 0);
        var row = new ReadoutRow("night-000010", 1000 + 1350 * 60, new Double3(0, 0, r), 0, 0, 0, 0);

        string[] panel = CheckPanels.ParkingOrbit(start, row);

        panel[2].TrimEnd().Should().Contain("2π√(r³/GM)").And.Contain("88.4 min").And.EndWith("run 90.0 min");
    }

    [Fact]
    public void TheParkingOrbitPanelShowsMeasuringForTheRunsPeriodInTheFirst10Seconds()
    {
        double r = DemoScenes.EarthRadius + 200e3;
        var start = new ReadoutRow("orbit-000000", 1000, new Double3(r, 0, 0), 0, 0, 0, 0);
        var row = new ReadoutRow("orbit-000599", 1599, new Double3(r, 100, 0), 0, 0, 0, 0);
        var later = row with { Tick = 1600 };

        CheckPanels.ParkingOrbit(start, row)[2].TrimEnd().Should().EndWith("run measuring");
        CheckPanels.ParkingOrbit(start, later)[2].TrimEnd().Should().NotEndWith("run measuring");
    }

    [Fact]
    public void TheLinesOfTheParkingOrbitPanelAreOfOneWidth()
    {
        string[] panel = CheckPanels.ParkingOrbit(At(200e3), At(200e3, tick: 6000, speed: 7788.12));

        panel.Should().HaveCount(3);
        panel.Select(line => line.Length).Distinct().Should().ContainSingle();
    }

    [Fact]
    public void TheSpinGravityPanelNamesTheStationsSpinRateAndTheRowsDistanceFromTheAxis()
    {
        string[] panel = CheckPanels.SpinGravity(At(400e3), At(400e3, axis: 99.104));

        panel[0].TrimEnd().Should().Contain("spin gravity");
        panel[1].TrimEnd().Should().Contain("ω 0.31321 rad/s").And.Contain("r 99.10 m");
    }

    [Theory]
    [InlineData(100.0, "9.81 m/s²")]
    [InlineData(50.0, "4.91 m/s²")]
    public void TheSpinGravityPanelSetsOmegaSquaredRAtTheRowsDistanceFromTheAxisBesideTheRunsFeltGravity(double axis, string textbook)
    {
        // ω² r with ω = 0.31321 rad/s: 9.81 m/s² at 100 m from the axis, and half of it at 50 m.
        string[] panel = CheckPanels.SpinGravity(At(400e3), At(400e3, axis: axis, felt: 9.7213));

        panel[2].TrimEnd().Should().Contain("ω²r").And.Contain(textbook).And.EndWith("run 9.72 m/s²");
    }

    [Fact]
    public void TheLinesOfTheSpinGravityPanelAreOfOneWidth()
    {
        string[] panel = CheckPanels.SpinGravity(At(400e3), At(400e3, axis: 99.1, felt: 9.72));

        panel.Should().HaveCount(3);
        panel.Select(line => line.Length).Distinct().Should().ContainSingle();
    }
    private static WheelFloor Floor => DemoScenes.DemoStation.WheelFloor;

    /// <summary>
    /// A release a micrometer over a box's half size above the middle of facet 128, at 8 m/s and 30° up, spinward or
    /// antispinward. Over a flat floor in ω² R = 9.810 m/s², it is back down after 2 · 4 / 9.810 = 0.8155 s, 6.928 · 0.8155
    /// = 5.65 m along. The micrometer keeps the release off the floor, so that its landing is the one after its flight.
    /// </summary>
    private static ThrowRelease Release(int direction)
    {
        Double3 place = Floor.OnFacet(128, DemoScenes.WheelCoda.BoxHalf + 1e-6, -2);
        return new ThrowRelease(place, WheelFloor.UpAt(place) * 4 + WheelFloor.SpinwardAt(place) * (direction * 8 * Math.Cos(Math.PI / 6)));
    }

    /// <summary>How far along the floor, in the throw's direction, the straight line of <paramref name="release"/> lands, in two decimals.</summary>
    private static string LineLanding(ThrowRelease release)
    {
        ThrowLanding line = release.Landing(DemoScenes.SpinStation.Rate, Floor, DemoScenes.WheelCoda.BoxHalf);
        return Math.Abs(Floor.Spinward(release.Place, line.Place)).ToString("F2", System.Globalization.CultureInfo.InvariantCulture) + " m";
    }

    private static ReadoutRow Coda(ThrowReadout? first = null, ThrowReadout? second = null) => At(400e3, axis: 99.1, felt: 9.72) with { FirstThrow = first, SecondThrow = second };

    [Fact]
    public void TheThrowsPanelShowsEachThrowAsWaitingBeforeItsRelease()
    {
        string[] panel = CheckPanels.Throws(Coda(), Coda());

        panel[0].TrimEnd().Should().Contain("TEXTBOOK CHECK").And.Contain("thrown in the wheel");
        panel[2].TrimEnd().Should().StartWith("spinward").And.EndWith("waiting");
        panel[3].TrimEnd().Should().StartWith("antispinward").And.EndWith("waiting");
    }

    [Fact]
    public void TheThrowsPanelSetsTheStraightLinesLandingBesideTheFlatFieldsFromTheRelease()
    {
        ThrowRelease spinward = Release(1), antispinward = Release(-1);

        string[] panel = CheckPanels.Throws(Coda(), Coda(new ThrowReadout(spinward, null), new ThrowReadout(antispinward, null)));

        panel[1].Should().Contain("straight line").And.Contain("run").And.Contain("uniform 1 g");
        panel[2].TrimEnd().Should().Contain(LineLanding(spinward)).And.Contain("in the air").And.EndWith("5.65 m");
        panel[3].TrimEnd().Should().Contain(LineLanding(antispinward)).And.Contain("in the air").And.EndWith("5.65 m");
        LineLanding(spinward).Should().NotBe(LineLanding(antispinward), "the wheel's turn bends the two throws apart");
    }

    [Fact]
    public void TheThrowsPanelSetsTheRunsLandingAlongTheFloorInTheThrowsDirection()
    {
        ThrowRelease spinward = Release(1), antispinward = Release(-1);
        double theta = WheelFloor.Theta(spinward.Place);
        var ahead = new ThrowLanding(0.8, Floor.Above(theta + 0.0612, 0.25, -2));
        var behind = new ThrowLanding(1.7, Floor.Above(theta - 0.1154, 0.25, -2));

        string[] panel = CheckPanels.Throws(Coda(), Coda(new ThrowReadout(spinward, ahead), new ThrowReadout(antispinward, behind)));

        panel[2].TrimEnd().Should().Contain(" 6.12 m ").And.NotContain("in the air");
        panel[3].TrimEnd().Should().Contain(" 11.54 m ").And.NotContain("in the air");
    }

    [Fact]
    public void TheThrowsPanelNamesEachReleasesSpeedAndAngleAboveTheFloorInTheStationsAxes()
    {
        string[] panel = CheckPanels.Throws(Coda(), Coda(new ThrowReadout(Release(1), null)));

        panel[2].Should().Contain("8.00 m/s 30.0°");
    }

    /// <summary>
    /// The throws panel writes no "fi" and no "fl". libass draws each pair as one glyph of Menlo's, a ligature, so a line
    /// that holds one draws narrower than its count of characters, and the panel's right-aligned lines fall out of line.
    /// The first render of 2026-10-21 showed it, with "flat" and "in flight".
    /// </summary>
    [Fact]
    public void TheThrowsPanelWritesNoPairThatMenloDrawsAsOneGlyph()
    {
        ReadoutRow landed = Coda(new ThrowReadout(Release(1), new ThrowLanding(0.8, Release(1).Place)), new ThrowReadout(Release(-1), null));

        foreach (ReadoutRow row in new[] { Coda(), landed })
            CheckPanels.Throws(Coda(), row).Should().OnlyContain(line => !line.Contains("fi", StringComparison.Ordinal) && !line.Contains("fl", StringComparison.Ordinal));
    }

    [Fact]
    public void TheLinesOfTheThrowsPanelAreOfOneWidth()
    {
        foreach (ReadoutRow row in new[] { Coda(), Coda(new ThrowReadout(Release(1), null)), Coda(new ThrowReadout(Release(1), new ThrowLanding(0.8, Release(1).Place)), new ThrowReadout(Release(-1), null)) })
        {
            string[] panel = CheckPanels.Throws(Coda(), row);

            panel.Should().HaveCount(4);
            panel.Select(line => line.Length).Distinct().Should().ContainSingle();
        }
    }
}
