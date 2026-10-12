using Tertius.Host.Scripting;

namespace Tertius.Host.Tests;

/// <summary>
/// The ship's windows in the vertical slice (ticket ter-ka2o): one headless run of the checked-in script
/// scripts/windows.json, at the orbit scene's start. The person walks 2 m aft and then to starboard, into the window 12 m
/// aft of the ship's center, where the glass stops it. It turns to the window, looks 25 degrees down, and sees Earth
/// through the pane, 0.55 m from its eye. The script destroys that pane and captures the same view through the bare hole,
/// so that the test can tell what the pane adds. Then the person walks into the airlock's chamber, cycles the airlock,
/// walks out through the hatch and drifts away from the hull. It turns round toward the port window 4 m forward of the
/// hatch and sees the lamp-lit cabin through its pane. A pane a few meters from the eye with Earth behind it crosses every
/// depth range of the renderer. Every test of the class reads that one run.
/// </summary>
public sealed class WindowsRun() : CheckedInRun("windows");

[Collection(GpuCollection.Name)]
public sealed class WindowsSliceTests(WindowsRun run) : IClassFixture<WindowsRun>
{
    private StateDump Dump(string name) => run.Result.Dump(name);

    [Fact]
    public void ThePersonWalksToAStarboardWindowAndStandsAgainstTheWallBelowIt()
    {
        FramePose atWindow = Dump("at-window").Entity("person").Pose;

        // 200 ticks at 3 m/s would take the person 10 m, past the starboard wall's inner face at x = 9.5 m. The capsule's
        // radius is 0.3 m. The window 12 m aft spans z from 11 m to 13 m, around the person, and starts 1.05 m above the
        // floor, so the wall below it stops the person. ShipWindowTests checks that the glass stops what fits through.
        atWindow.Frame.Should().Be("ship");
        atWindow.Position.X.Should().BeInRange(9.15, 9.2);
        atWindow.Position.Y.Should().BeApproximately(-2.6, 0.005);
        atWindow.Position.Z.Should().BeApproximately(12, 0.01);
    }

    [Fact]
    public void FromInsideThePersonSeesEarthThroughThePaneTintedByIt()
    {
        // The person looks out along the ship's +X and 25 degrees down from 0.3 m inside the wall, so the window fills the
        // top of the image and Earth its middle: its horizon is 19.8 degrees below level. The pane lets most of Earth's
        // light through and adds its own, the lamp light that it reflects. So through the pane Earth is a little lighter
        // in red and green and a little darker in blue than through the bare hole, at the same pixel, 3 ticks later.
        Dump("pane-gone").Entities.Should().NotContain(entity => entity.Id == "pane-starboard-7", "the script destroyed the pane before the second capture");
        (int r, int g, int b) earth = run.Capture("earth-through-pane").Average(640, 400);
        (int r, int g, int b) bare = run.Capture("earth-through-hole").Average(640, 400);

        (bare.b > bare.r + 60).Should().BeTrue($"Earth's blue shows through the bare hole, not {bare}");
        (earth.b > earth.r + 60).Should().BeTrue($"Earth's blue shows through the pane, not {earth}");
        (earth.r > bare.r + 2 && earth.g > bare.g + 2 && earth.b < bare.b).Should().BeTrue($"the pane tints Earth from {bare} to {earth}");
    }

    [Fact]
    public void AgainstBlackSpaceThePaneShowsItsOwnSlightBlueTint()
    {
        // Above Earth's horizon, the bare hole shows black space. The pane there shows only its own lamp-lit color, at its
        // low opacity: a dark blue gray.
        (int r, int g, int b) glass = run.Capture("earth-through-pane").Average(640, 200);
        (int r, int g, int b) space = run.Capture("earth-through-hole").Average(640, 200);

        (space.r + space.g + space.b).Should().BeLessThan(10, $"black space shows through the bare hole, not {space}");
        (glass.r + glass.g + glass.b).Should().BeInRange(20, 90, $"the pane is dark, but not black, against space, not {glass}");
        (glass.b > glass.g && glass.g > glass.r).Should().BeTrue($"the pane's tint is blue, not {glass}");
    }

    [Fact]
    public void FromOutsideThePersonSeesTheLampLitCabinThroughAPortPane()
    {
        // The person drifts 6 m outside the port wall, level with the hatch, and looks back at the window 4 m forward of
        // the hatch, 33 degrees to the left of the hatch and 5 degrees down. The sun stands straight above the ship, so
        // only the dim ambient light reaches the port face. Through the pane, across the cabin, the lamps light the
        // starboard wall and the floor in their warm white. The window spans rows 274 to 375 and columns 595 to 715. The
        // starboard wall shows above row 343, and the floor below row 349. The opposite window's pane, dark against space,
        // spans columns 633 to 669 and rows 297 to 319, so the probe of the wall sits to its right.
        Dump("outside").Entity("person").Pose.Position.X.Should().BeLessThan(-15, "the person is outside the hull, 6 m from its port face");
        var image = run.Capture("cabin-through-pane");
        (int r, int g, int b) hull = image.Average(640, 200), wall = image.Average(692, 308), floor = image.Average(650, 360);

        (hull.r + hull.g + hull.b).Should().BeLessThan(60, $"the port face above the window is dark, not {hull}");
        (wall.r > hull.r + 50 && wall.r > wall.b).Should().BeTrue($"the lamp-lit starboard wall shows through the pane, not {wall}");
        (floor.r > hull.r + 100 && floor.r > floor.b).Should().BeTrue($"the lamp-lit floor shows through the pane, not {floor}");
    }

    [Fact]
    public void TheRunReportsNoError()
    {
        Dump("outside").Errors.Should().BeEmpty();
        Dump("outside").Camera.Should().Be("person");
        run.ValidationMessages.Should().BeEmpty();
    }
}
