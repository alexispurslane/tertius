using System.Numerics;
using Tertius.Host.Scripting;

namespace Tertius.Host.Tests;

/// <summary>
/// Control past the ship's bubble (ticket ter-l6oo): one headless run of the checked-in script scripts/spacewalk.json,
/// at the orbit scene's start. The person goes out through the airlock as in scripts/airlock.json, with the same tap of
/// C against the upward push of the sill's edge, and drifts away from the ship along the ship's X axis. Near the edge
/// of the ship's bubble, which fits just around the hull, it brakes, and it drifts slowly out of the bubble into the
/// frame earth. There it falls freely with the ship, so it is weightless (User). It turns round, sees the ship's port
/// face and the open hatch close by, and thrusts toward them. It drifts back into the ship's frame, thrusts toward the
/// hatch, and drifts in through the hatch onto the chamber floor, where it stands. Every test of the class reads that
/// one run.
/// </summary>
public sealed class SpacewalkRun() : CheckedInRun("spacewalk");

/// <summary>
/// The ship's bubble in the orbit scene, as the slice tests SpacewalkSliceTests and ThrowSliceTests expect it: an
/// ellipsoid along the ship's axes that holds the hull, 20 by 8 by 60 m, with a shell of 2 m around it. The smallest
/// ellipsoid that holds a box has the box's half extents times sqrt(3), so the radii are (12, 6, 32) m times sqrt(3).
/// </summary>
internal static class ShipBubble
{
    /// <summary>The bubble's radii along the ship's X, Y and Z, in meters: about 20.8, 10.4 and 55.4.</summary>
    public static Double3 Radii { get; } = new Double3(12, 6, 32) * Math.Sqrt(3);

    /// <summary>2 percent of the bubble's largest radius, about 1.11 m: the margin past the bubble of a body up to that radius.</summary>
    public static double Margin => 0.02 * Radii.Z;

    /// <summary>
    /// How far a point lies outside the bubble, in meters, negative inside, measured as the hand-off measures it: along the
    /// ray from the ship's center. <paramref name="position"/> is in the ship's axes, relative to its center. The ray meets
    /// the bubble's surface at the point divided by the length of the point in units of the radii.
    /// </summary>
    public static double Outside(Double3 position) => position.Length * (1 - 1 / (position / Radii).Length);
}

[Collection(GpuCollection.Name)]
public sealed class SpacewalkSliceTests(SpacewalkRun run) : IClassFixture<SpacewalkRun>
{
    // In the ship's axes, a person's center is 2.6 m below the ship's center when it stands on the chamber floor, and 2.4 m
    // below it when it floats at the height of the hatch's sill. The hatch is 1.2 m wide, centered on z = 10 m, so the
    // capsule of radius 0.3 m passes through it only within 0.3 m of z = 10 m. The chamber spans x from -9.5 m to -6.5 m.
    private const double OnTheFloor = -2.6, OnTheSill = -2.4, HatchZ = 10;

    // The radius of the person's bounding sphere, which the hand-off uses, in meters: the capsule is 1.8 m tall.
    private const double PersonRadius = 0.9;

    // The dumps of the run in order, grouped by the frame that the person is in.
    private static readonly string[] InShipGoingOut = ["outside", "leaving"];
    private static readonly string[] InEarth = ["in-earth", "turned-back", "returning"];
    private static readonly string[] InShipComingBack = ["back-in-ship", "returned"];

    private StateDump Dump(string name) => run.Result.Dump(name);

    /// <summary>
    /// The person's position and velocity relative to the ship, in the ship's axes, in <paramref name="name"/>. In the
    /// ship's frame, these are the person's own. In earth, they come from the poses and velocities of the person and the
    /// ship there. The ship's frame turns with the ship, so a point at rest in it moves at the ship's velocity plus the
    /// ship's angular velocity crossed with the point's offset from the ship's center.
    /// </summary>
    private (Double3 Position, Double3 Velocity) RelativeToShip(string name)
    {
        EntityRecord person = Dump(name).Entity("person"), ship = Dump(name).Entity("ship");
        if (person.Pose.Frame == "ship")
            return (person.Pose.Position, person.Velocity);
        person.Pose.Frame.Should().Be("earth");
        ship.Pose.Frame.Should().Be("earth");
        Double3 offset = person.Pose.Position - ship.Pose.Position;
        Double3 velocity = person.Velocity - ship.Velocity - Double3.Cross(ship.AngularVelocity, offset);
        DQuaternion toShip = ship.Pose.Rotation.Conjugate();
        return (toShip.Rotate(offset), toShip.Rotate(velocity));
    }

    /// <summary>The person's up, its +Y, in earth's axes, in <paramref name="name"/>. Earth's frame does not turn.</summary>
    private Double3 UpInEarth(string name)
    {
        FramePose person = Dump(name).Entity("person").Pose;
        Double3 up = person.Rotation.Rotate(Double3.UnitY);
        return person.Frame == "ship" ? Dump(name).Entity("ship").Pose.Rotation.Rotate(up) : up;
    }

    /// <summary>How far the ship turned between two dumps, in radians.</summary>
    private double ShipTurn(string from, string to)
    {
        DQuaternion turn = Dump(from).Entity("ship").Pose.Rotation.Conjugate() * Dump(to).Entity("ship").Pose.Rotation;
        return 2 * Math.Atan2(Math.Sqrt(turn.X * turn.X + turn.Y * turn.Y + turn.Z * turn.Z), Math.Abs(turn.W));
    }

    private static double Angle(Double3 a, Double3 b) => Math.Atan2(Double3.Cross(a, b).Length, Double3.Dot(a, b));

    [Fact]
    public void ThePersonDriftsOutOfTheShipsFrameIntoEarthAndBack()
    {
        foreach (string name in InShipGoingOut.Concat(InShipComingBack))
            Dump(name).Entity("person").Pose.Frame.Should().Be("ship", $"the person is in the ship's frame in {name}");
        foreach (string name in InEarth)
            Dump(name).Entity("person").Pose.Frame.Should()
                .Be("earth", $"the person is past the ship's bubble in {name}");
        // The capsule's radius is 0.3 m, but the hand-off uses the radius of the capsule's bounding sphere, 0.9 m. The
        // person leaves the ship's frame once its center lies more than 0.9 m plus the margin outside the bubble. The
        // margin is the larger of 2 percent of the bubble's largest radius, 1.11 m, and that sphere's radius. The person
        // comes back into the ship's frame on its first touch of the bubble, once its center lies 0.9 m or less outside it.
        ShipBubble.Outside(RelativeToShip("leaving").Position).Should().BeLessThan(PersonRadius + ShipBubble.Margin);
        ShipBubble.Outside(RelativeToShip("in-earth").Position).Should()
            .BeGreaterThan(PersonRadius + ShipBubble.Margin);
        ShipBubble.Outside(RelativeToShip("returning").Position).Should().BeGreaterThan(PersonRadius);
        ShipBubble.Outside(RelativeToShip("back-in-ship").Position).Should().BeLessThanOrEqualTo(PersonRadius);
        // So the person passes into earth and back within 25 m of the ship's center.
        RelativeToShip("in-earth").Position.Length.Should().BeLessThan(25);
    }

    [Fact]
    public void ThePersonMovesAlongTheShipsXAxisFromTheHatchsSillWithOnlyASmallSteadyDriftAcrossIt()
    {
        // The ship's X axis is the normal of its orbit. Along it, the motion relative to the ship in earth has no sideways
        // drift. The hand-offs keep the person's motion relative to the ship too. The person leaves the sill with a small
        // upward drift, which the sill's rounded inner edge gave its rigid form (ticket ter-1w06) and one tick of C made
        // smaller (see AirlockSliceTests.OutsideTheHullThePersonIsWeightlessAndDriftsAwayFromTheShip). Nothing changes that drift, so
        // the person's height grows by it in proportion to the time since the dump "outside", at 60 ticks a second.
        (Double3 start, Double3 drift) = RelativeToShip("outside");
        start.Y.Should().BeApproximately(OnTheSill, 0.02);
        drift.Y.Should().BeInRange(0, 0.01);
        foreach (string name in InShipGoingOut.Concat(InEarth).Append("back-in-ship"))
        {
            Double3 at = RelativeToShip(name).Position;
            double seconds = (Dump(name).Tick - Dump("outside").Tick) / 60.0;

            at.Y.Should().BeApproximately(start.Y + drift.Y * seconds, 0.01, $"in {name}");
            at.Z.Should().BeApproximately(HatchZ, 0.05, $"in {name}");
        }
    }

    [Fact]
    public void InEarthThePersonFallsFreelySoItDoesNotStand()
    {
        foreach (string name in InEarth)
            Dump(name).Entity("person").Standing.Should().BeFalse($"in {name}");
    }

    [Fact]
    public void InEarthAThrustTowardTheShipTurnsTheDriftAwayFromItAround()
    {
        Double3 before = RelativeToShip("in-earth").Velocity, after = RelativeToShip("turned-back").Velocity;

        before.X.Should().BeApproximately(-0.3, 0.005, "the person drifts slowly away from the ship");
        after.X.Should().BeApproximately(0.3, 0.005, "the person drifts slowly back toward the ship");
        // Between the two dumps, the script turns the view round and runs 3 ticks. Then it posts W's press, runs 24 ticks,
        // posts W's release and runs 3 more ticks. A post acts from the fourth tick after it, because the person sends its
        // force to its back jet, so the person thrusts in the last 24 ticks before the second dump. At
        // 1.5 m/s², that adds 0.6 m/s along the person's view, the ship's +X. One tick of thrust adds 0.025 m/s. Jolt keeps
        // velocities as floats, and at the 7.7 km/s of the orbit a float has steps of 0.5 mm/s. While the person thrusts,
        // each tick rounds the person's velocity and the ship's differently, so the thrust turns about 0.003 m/s sideways.
        (after.X - before.X).Should().BeApproximately(24 * 1.5 / 60, 2e-3);
        Double3.Distance(after - before, new(24 * 1.5 / 60, 0, 0)).Should().BeLessThan(0.01);
    }

    [Fact]
    public void InFreeFallInEarthThePersonKeepsItsUpWhileTheShipTurns()
    {
        Double3 up = UpInEarth(InEarth[0]);

        // The ship turns once per orbit, about 0.006 rad between the first and the last dump in earth. A person whose up
        // turned against Earth's gravity would turn as much.
        ShipTurn(InEarth[0], InEarth[^1]).Should().BeGreaterThan(5e-3);
        foreach (string name in InEarth)
            Angle(UpInEarth(name), up).Should().BeLessThan(1e-5, $"in {name}");
    }

    [Theory]
    [InlineData("leaving", "in-earth")]
    [InlineData("returning", "back-in-ship")]
    public void AHandOffTurnsThePersonsUpIntoTheAxesOfItsNewFrame(string before, string after)
    {
        // In the ship's frame, the person's up turns with the ship. In earth, it stays where it is. So if a hand-off
        // turns the person's rotation into the axes of its new frame, the person's up in earth's axes turns at most as
        // far as the ship does between a dump before the hand-off and a dump after it. In this scene the ship's +Y
        // points along the radius from Earth's center to within 2e-5 rad, so a hand-off that took the person's up from
        // Earth's gravity would pass here too. The physics test CharacterOrbitHandOffTests covers that, with a ship
        // whose +Y is not along the radius.
        Angle(UpInEarth(before), UpInEarth(after)).Should().BeLessThan(ShipTurn(before, after) + 1e-5);
    }

    [Fact]
    public void ThePersonDriftsInThroughTheHatchAndStandsOnTheChamberFloor()
    {
        EntityRecord person = Dump("returned").Entity("person");
        Double3 at = person.Pose.Position;

        person.Pose.Frame.Should().Be("ship");
        RelativeToShip("back-in-ship").Velocity.X.Should()
            .BeInRange(0.2, 0.4, "the person came back into the ship's frame slowly");
        at.X.Should().BeInRange(-9.2, -6.8, "the capsule of radius 0.3 m is inside the chamber");
        at.Y.Should().BeApproximately(OnTheFloor, 0.005);
        at.Z.Should().BeInRange(HatchZ - 0.3, HatchZ + 0.3, "the capsule fits through the hatch only there");
        person.Standing.Should().BeTrue();
        new Vector2((float)person.Velocity.X, (float)person.Velocity.Z).Length().Should()
            .BeLessThan(1e-3f, "the person landed and stands still");
    }

    [Fact]
    public void ThePersonComesBackTiltedByTheTurnOfTheShipAndStandsUprightOnTheChamberFloor()
    {
        double TiltInShip(string name) =>
            Angle(Dump(name).Entity("person").Pose.Rotation.Rotate(Double3.UnitY), Double3.UnitY);

        // While the person fell freely in earth, the ship turned under it, about 0.008 rad. The person keeps that tilt in the
        // ship's frame until the cabin's gravity stands it on the floor. Physics leaves a tilt of less than 1e-3 rad alone.
        TiltInShip("back-in-ship").Should()
            .BeInRange(ShipTurn(InEarth[0], InEarth[^1]), ShipTurn("leaving", "back-in-ship"));
        TiltInShip("returned").Should().BeLessThan(1e-3);
    }

    [Fact]
    public void TheRunReportsNoError()
    {
        Dump("returned").Errors.Should().BeEmpty();
        Dump("returned").Camera.Should().Be("person");
        run.ValidationMessages.Should().BeEmpty();
    }

    /// <summary>
    /// The rows and the columns of <paramref name="image"/> that the ship covers, above row 520, where the carried crate
    /// and Earth begin. A pixel is lit if its channels add up to more than 15: space is black, and the hull's port face,
    /// which only the dim ambient light reaches, is a dark gray. The stars light at most a few dozen pixels of a row or a
    /// column. The ship turns under the person in earth, so in the view its edges lean by a few pixels across the image.
    /// So a row of the ship is a row with more than half of its pixels lit, and a column of it has more than 30 lit pixels.
    /// </summary>
    private static (int Rows, int Columns) ShipExtent(Png image)
    {
        int[] litInRow = new int[image.Height], litInColumn = new int[image.Width];
        for (int y = 0; y < 520; y++)
        for (int x = 0; x < image.Width; x++)
        {
            (int r, int g, int b) = image.At(x, y);
            if (r + g + b > 15)
            {
                litInRow[y]++;
                litInColumn[x]++;
            }
        }

        return (litInRow.Count(count => count > image.Width / 2), litInColumn.Count(count => count > 30));
    }

    /// <summary>
    /// The first and the last column of <paramref name="image"/> with a pixel whose channels add up to more than 150,
    /// between rows 300 and 470, and from column <paramref name="from"/> up to column <paramref name="to"/>. Those rows lie
    /// inside the hull's port face, which hides the stars. Only what the lamps light is that bright there: the top of the
    /// hatch's sill, which reaches the port face, and the chamber's floor beyond it, which shows through the open hatch.
    /// The windows of the port wall show the lamp-lit cabin too, so the columns must leave them out.
    /// </summary>
    private static (int First, int Last) LitThroughTheHatch(Png image, int from, int to)
    {
        int first = image.Width, last = -1;
        for (int y = 300; y < 470; y++)
        for (int x = from; x < to; x++)
        {
            (int r, int g, int b) = image.At(x, y);
            if (r + g + b > 150)
            {
                first = Math.Min(first, x);
                last = Math.Max(last, x);
            }
        }

        return (first, last);
    }

    [Fact]
    public void FromEarthThePersonSeesThePortFaceAndTheHatchAtTheirSizeAtItsDistanceAboveEarth()
    {
        // The person looks along the ship's +X at its port face, the hull's outer face at x = -10 m, 60 m long and 8 m tall.
        // The hatch through it is 1.2 m wide and centered on the person's z, 10 m. The capture comes 3 ticks after the dump.
        // The view is 720 pixels high and 60 degrees high, and 1280 pixels wide.
        (Double3 at, Double3 velocity) = RelativeToShip("in-earth");
        double distance = -10 - (at.X + velocity.X * 3 / 60);
        double pixelsPerMeter = 360 / Math.Tan(run.World.VerticalFov / 2) / distance;
        Png image = run.Capture("ship-from-earth");

        // The nearest window, 4 m forward of the hatch, ends 3 m from the hatch's center, so the search for the hatch keeps
        // within 2.5 m of the image's middle column.
        int reach = (int)Math.Round(2.5 * pixelsPerMeter);
        (int rows, int columns) = ShipExtent(image);
        (int first, int last) = LitThroughTheHatch(image, image.Width / 2 - reach, image.Width / 2 + reach);

        distance.Should().BeLessThan(15, "the person passed into earth close to the ship");
        rows.Should().BeCloseTo((int)Math.Round(8 * pixelsPerMeter), 2, "the hull is 8 m tall");
        columns.Should().Be(image.Width, "the hull is 60 m long, longer than the view is wide at that distance");
        (first + last).Should().BeCloseTo(image.Width - 1, 2, "the hatch is in the middle of the image");
        (last - first + 1).Should()
            .BeCloseTo((int)Math.Round(1.2 * pixelsPerMeter), 2, "the hatch's sill is 1.2 m wide");
        (int r, int g, int b) earth = image.Average(640, 690);
        (earth.b > earth.r + 60).Should().BeTrue($"Earth's blue fills the bottom of the image, not {earth}");
    }
}
