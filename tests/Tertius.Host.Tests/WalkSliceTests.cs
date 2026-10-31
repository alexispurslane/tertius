using Tertius.Host.Scripting;

namespace Tertius.Host.Tests;

/// <summary>
/// The second step of the vertical slice, "a person walks inside the ship while it orbits": one headless run of the
/// checked-in script scripts/walk.json, at the orbit scene's start. The script holds W for 2 s, turns the view a quarter
/// turn left with the mouse, holds W for 2 s again, jumps, and holds W for 2 s more, into the port wall. Every test of the
/// class reads that one run.
/// </summary>
public sealed class WalkRun() : CheckedInRun("walk");

public sealed class WalkSliceTests(WalkRun run) : IClassFixture<WalkRun>
{
    // The cabin floor is at y = -3.5 m in the ship's axes, and the person's capsule center rests 0.9 m above it. The
    // person starts 5 cm higher.
    private const double Standing = -2.6;

    private Double3 PersonAt(string dump) => run.Result.Dump(dump).Entity("person").Pose.Position;

    private static void ExpectNear(Double3 actual, Double3 expected, double tolerance) =>
        Double3.Distance(actual, expected).Should().BeLessThan(tolerance, $"{actual} is not {expected}");

    [Fact]
    public void ThePersonStandsOnTheCabinFloorInTheShipsFrame()
    {
        FramePose standing = run.Result.Dump("standing").Entity("person").Pose;

        standing.Frame.Should().Be("ship");
        standing.Position.Y.Should().BeApproximately(Standing, 0.005);
        ExpectNear(standing.Position with { Y = 0 }, new(0, 0, 10), 1e-3);
    }

    [Fact]
    public void HoldingWWalksThePersonTowardTheShipsNoseAtWalkingSpeed() =>
        // 120 ticks are 2 s, at 3 m/s. The key's release reaches the person as late after its press as the press came after
        // the post.
        ExpectNear(PersonAt("walked") - PersonAt("standing"), new(0, 0, -6), 0.01);

    [Fact]
    public void AfterTheMouseTurnsTheViewAQuarterTurnLeftWWalksTowardTheShipsLeftSide() =>
        ExpectNear(PersonAt("turned") - PersonAt("walked"), new(-6, 0, 0), 0.01);

    [Fact]
    public void AJumpInTheOrbitingShipRisesStraightOffTheDeckAndLandsWhereItLeft()
    {
        Double3 start = PersonAt("turned"),
            top = PersonAt("jumping");

        // A jump at 4 m/s in 9.81 m/s² peaks 0.41 s later, 0.82 m up. The jump acts in the third tick after its post, so the
        // dump comes after 25 moves. The jump speed is an instant velocity and physics moves the person by the chord of
        // each step, so the person is 4 m/s · t - 9.81 m/s² · t² / 2 = 0.815 m up, t = 25/60 s.
        (top.Y - start.Y)
            .Should()
            .BeApproximately(0.815, 0.005);
        ExpectNear(top with { Y = 0 }, start with { Y = 0 }, 0.01);
        ExpectNear(PersonAt("landed"), start, 0.02);
    }

    [Fact]
    public void ThePortWallStopsThePerson()
    {
        Double3 wall = PersonAt("at-wall");

        // 2 s at 3 m/s would take the person 6 m, from x = -6 m to -12 m. The wall's inner face is at x = -9.5 m, and the
        // capsule's radius is 0.3 m.
        wall.X.Should().BeInRange(-9.2, -9.15);
        ExpectNear(wall with { X = 0 }, PersonAt("landed") with { X = 0 }, 0.01);
    }

    [Fact]
    public void TheShipCarriesThePersonTensOfKilometersRoundEarthWhileItWalksAFewMetersInside()
    {
        static Double3 InEarth(StateDump dump)
        {
            FramePose ship = dump.Entity("ship").Pose,
                person = dump.Entity("person").Pose;
            return ship.Position + ship.Rotation.Rotate(person.Position);
        }

        StateDump first = run.Result.Dump("standing"),
            last = run.Result.Dump("landed");

        // 387 ticks, 6.45 s, at 7.67 km/s.
        Double3.Distance(InEarth(last), InEarth(first)).Should().BeInRange(49e3, 50e3);
        Double3.Distance(PersonAt("landed"), PersonAt("standing")).Should().BeLessThan(9);
        last.Entity("person").Pose.Frame.Should().Be("ship");
    }

    [Fact]
    public void TheRunReportsNoErrorAndShowsThePersonsView()
    {
        StateDump last = run.Result.Dump("at-wall");

        last.Errors.Should().BeEmpty();
        last.Camera.Should().Be("person");
        run.ValidationMessages.Should().BeEmpty();
    }

    [Fact]
    public void FromInsideTheCabinThePersonSeesALampLitFloorAndTheNoseWallAhead()
    {
        // The person looks along the ship's -Z from 1.65 m above the floor, 39.5 m from the nose wall. The sun stands
        // straight above the ship, but the hull keeps its light out: the lamps light the cabin in their warm white. The nose
        // window fills the middle of the image, rows 344 to 375, so the probe of the nose wall sits above it, where the
        // wall spans rows 276 to 343.
        Png cabin = run.Capture("cabin");
        (int r, int g, int b) floor = cabin.Average(640, 650);
        (int r, int g, int b) wall = cabin.Average(640, 310);

        (floor.r > 90 && floor.r > floor.b + 10).Should().BeTrue($"the floor ahead is lamp lit, not {floor}");
        (wall.r > 40 && wall.r > wall.b).Should().BeTrue($"the nose wall is lamp lit, not {wall}");
    }

    [Fact]
    public void ThroughTheNoseWindowAheadThePersonSeesDarkSpace()
    {
        // The nose window is 6 m wide and 2 m tall, centered at eye height, so it is in the middle of the image. The ship's
        // nose points along its motion, level, and Earth's horizon is 19.8 degrees below level, out of the window's view.
        // Space is black, and the pane in the window adds its own lamp-lit color, a dark blue gray. The C# BRDF gives the
        // pane at most 22 % more light here than Blinn-Phong did, mostly because its diffuse part no longer gives up the
        // Fresnel share (docs/plans/pbr-brdf.md, "Decisions", 13). So the bound of 90 grew by that share, to 110: the tone
        // curve and the sRGB curve grow less than in proportion at these levels. Since the lamps reach as far as their power
        // carries (docs/plans/local-light-shadows.md, Part 4), not 10 m, they light the pane at full strength, and at the
        // fixed exposure of 0 stops it shows a lighter gray, (82, 84, 85), still a little blue. So the bound grew to 270.
        (int r, int g, int b) window = run.Capture("cabin").Average(640, 360);

        (window.r + window.g + window.b < 270 && window.b > window.r)
            .Should()
            .BeTrue($"dark space shows through the pane, not {window}");
    }

    [Fact]
    public void AfterTheQuarterTurnThePortWallFillsTheViewWhereTheRoofWasDark()
    {
        // Facing the nose, the top left of the image shows the roof, which the lamps leave dark away from them. After the
        // quarter turn, the person faces the port wall from 3.5 m away, and the wall, lit by the lamps, fills the image.
        // In SI units each capture takes the automatic exposure of a scripted run, which raises the dim cabin, so the roof
        // is dark beside the lamp-lit wall, not dark on an absolute scale. The roof showed (42, 39, 35), under half the
        // wall, while the map held the ceiling panels' glow: that glow lit the floor and the walls a second time but not the
        // roof, which faces away from the panels. The map now leaves a lamp's glow out (ter-ylzk), so the exposure rises
        // by about 1.2 stops, and the roof shows (74, 69, 63), about two thirds of the wall's (109, 103, 95).
        (int r, int g, int b) roof = run.Capture("cabin").Average(300, 100);
        (int r, int g, int b) wall = run.Capture("turned").Average(300, 100);

        (roof.r + roof.g + roof.b < 3 * (wall.r + wall.g + wall.b) / 4)
            .Should()
            .BeTrue($"facing the nose, the top left shows the roof, darker than the lamp-lit wall {wall}, not {roof}");
        (wall.r > 80 && wall.r > wall.b)
            .Should()
            .BeTrue($"after the turn, the top left shows the lamp-lit port wall, not {wall}");
    }
}
