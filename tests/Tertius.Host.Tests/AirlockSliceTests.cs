using System.Numerics;
using Tertius.Host.Scripting;

namespace Tertius.Host.Tests;

/// <summary>
/// The third step of the vertical slice, "the person goes through an airlock to the outside": one headless run of the
/// checked-in script scripts/airlock.json, at the orbit scene's start. The person turns toward the airlock, walks into its
/// chamber, presses E, and walks out through the hatch when it opens. Outside, where it is weightless, it drifts away
/// from the hull, turns round, and thrusts back toward the ship until it drifts in through the hatch and lands in the
/// chamber. It presses E again and walks back into the cabin. Every test of the class reads that one run.
/// </summary>
public sealed class AirlockRun() : CheckedInRun("airlock");

[Collection(GpuCollection.Name)]
public sealed class AirlockSliceTests(AirlockRun run) : IClassFixture<AirlockRun>
{
    // In the ship's axes, a person's center stands 2.6 m below the ship's center on the cabin floor, and 2.4 m below it on
    // a sill. The chamber spans x from -9.5 m to -6.5 m, and the hull's outer face is at x = -10 m. Each door's center is
    // 2.25 m below the ship's center when it is shut, and 2.2 m higher when it is open.
    private const double Standing = -2.6, OnTheSill = -2.4, DoorShut = -2.25, DoorRise = 2.2;

    private StateDump Dump(string name) => run.Result.Dump(name);

    private Double3 PersonAt(string dump) => Dump(dump).Entity("person").Pose.Position;

    private Vector3 VelocityAt(string dump) => Dump(dump).Entity("person").Velocity.ToVector3();

    /// <summary>How far <paramref name="door"/> is open in <paramref name="dump"/>, from 0 (shut) to 1 (open).</summary>
    private double Open(string dump, string door) => (Dump(dump).Entity(door).Pose.Position.Y - DoorShut) / DoorRise;

    private static void ExpectInTheChamber(Double3 person)
    {
        // The capsule's radius is 0.3 m.
        person.X.Should().BeInRange(-9.2, -6.8);
        person.Y.Should().BeApproximately(Standing, 0.005);
        person.Z.Should().BeApproximately(10, 0.01);
    }

    [Fact]
    public void TheAirlockStartsWithItsInnerDoorOpenAndItsHatchShut()
    {
        Open("facing-airlock", "airlock-inner").Should().BeApproximately(1, 1e-4);
        Open("facing-airlock", "airlock-outer").Should().BeApproximately(0, 1e-4);
    }

    [Fact]
    public void ThePersonWalksThroughTheInnerDoorIntoTheChamber()
    {
        ExpectInTheChamber(PersonAt("in-chamber"));
    }

    [Fact]
    public void PressingEInTheChamberShutsTheInnerDoorAndOpensTheHatch()
    {
        Open("cycled", "airlock-inner").Should().BeApproximately(0, 1e-4);
        Open("cycled", "airlock-outer").Should().BeApproximately(1, 1e-4);
    }

    [Fact]
    public void OutsideTheHullThePersonIsWeightlessAndDriftsAwayFromTheShip()
    {
        Double3 outside = PersonAt("outside");
        Vector3 drift = VelocityAt("outside");

        outside.X.Should().BeLessThan(-10.3, "the whole capsule is past the hull's outer face");
        // The person stood on the hatch's sill when it became weightless, and nothing pulls it down out there.
        outside.Y.Should().BeApproximately(OnTheSill, 0.01);
        outside.Z.Should().BeApproximately(10, 0.01);
        drift.X.Should().BeLessThan(-2.9f, "it walked out at 3 m/s and thrust on for a moment");
        drift.Y.Should().BeApproximately(0, 1e-4f, "the sill stopped the step of gravity that pressed the person onto it");
        drift.Z.Should().BeApproximately(0, 1e-4f);
    }

    [Fact]
    public void WithNoKeyHeldThePersonDriftsOnAtTheSameVelocity()
    {
        Vector3 drift = VelocityAt("outside");

        // The two dumps are 60 ticks, one second, apart.
        Vector3.Distance(VelocityAt("drifting"), drift).Should().BeLessThan(1e-4f);
        Double3.Distance(PersonAt("drifting"), PersonAt("outside") + (Double3)drift).Should().BeLessThan(0.01);
    }

    [Fact]
    public void ThrustTowardTheShipTurnsTheDriftAroundAndBringsThePersonBackOntoTheChamberFloor()
    {
        // A key acts from the third tick after its post: the post joins the routing at the end of the next tick, the person
        // handles it in the tick after that, and physics moves the person in the step of the third tick. So 238 of the 240
        // ticks between W's down post and the dump thrust at 1.5 m/s², and they take 238 · 1.5 m/s² / 60 = 5.95 m/s off
        // the drift away from the ship. One tick of thrust adds 0.025 m/s, so the margin counts the ticks exactly.
        (VelocityAt("turned-back").X - VelocityAt("drifting").X).Should().BeApproximately(238 * 1.5f / 60, 1e-3f);
        ExpectInTheChamber(PersonAt("returned"));
        new Vector2(VelocityAt("returned").X, VelocityAt("returned").Z).Length().Should().BeLessThan(1e-3f, "the person landed and stands still");
    }

    [Fact]
    public void PressingEAgainShutsTheHatchAndOpensTheInnerDoorAndThePersonWalksBackIntoTheCabin()
    {
        Open("recycled", "airlock-inner").Should().BeApproximately(1, 1e-4);
        Open("recycled", "airlock-outer").Should().BeApproximately(0, 1e-4);
        Double3 home = PersonAt("home");
        home.X.Should().BeGreaterThan(-6, "the person is out of the chamber, in the cabin");
        home.Y.Should().BeApproximately(Standing, 0.005);
    }

    [Fact]
    public void ThePersonStaysInTheShipsFrameAndTheRunReportsNoError()
    {
        foreach (string name in new[] { "standing", "in-chamber", "outside", "drifting", "turned-back", "returned", "home" })
            Dump(name).Entity("person").Pose.Frame.Should().Be("ship",
                $"the person has not left the ship's frame in {name}, so its center is less than 2 m outside the ship's bubble, an ellipsoid with radii of about 20.8, 10.4 and 55.4 m");
        Dump("home").Errors.Should().BeEmpty();
        Dump("home").Camera.Should().Be("person");
        run.ValidationMessages.Should().BeEmpty();
    }

    private static void ExpectOrange((int R, int G, int B) color, string what) =>
        (color.R > color.G + 20 && color.G > color.B + 10).Should().BeTrue($"{what} is the orange of a door, not {color}");

    [Fact]
    public void FromTheCabinThePersonSeesTheShutHatchThroughTheOpenInnerDoor()
    {
        // The image's center row passes through the inner doorway and ends on the hatch's door.
        ExpectOrange(run.Capture("inner-door").Average(640, 385), "the middle of the inner doorway");
    }

    [Fact]
    public void TheShutHatchIsADoorAndTheOpenHatchShowsSpaceAboveEarth()
    {
        ExpectOrange(run.Capture("hatch-shut").Average(640, 400), "the shut hatch");
        Png open = run.Capture("hatch-open");
        (int r, int g, int b) space = open.Average(640, 300), earth = open.Average(640, 680);
        (space.r + space.g + space.b).Should().BeLessThan(60, $"black space with a few stars shows through the hatch, not {space}");
        (earth.b > earth.r + 60).Should().BeTrue($"Earth's blue shows low in the hatch, not {earth}");
    }

    [Fact]
    public void FromOutsideEarthShowsBelowTheHullAndTheOpenHatchShowsTheLampLitChamberFloor()
    {
        // The person looks back at the hull's port face. The sun stands straight above the ship, so it does not light that
        // face, which looks sideways. Earth fills the bottom of the image, below the hull. Through the open hatch the person
        // sees the chamber's floor. Point lights cast no shadows yet, so the cabin's lamps light the chamber's floor through
        // the chamber's walls and its shut inner door. Each probe sits well inside its region: the hull ends at row 596, above
        // Earth, and the chamber floor spans rows 479 to 512 and columns 584 to 695.
        Png hull = run.Capture("hull");
        (int r, int g, int b) earth = hull.Average(640, 690), floor = hull.Average(640, 495), side = hull.Average(640, 200);
        (earth.b > earth.r + 60).Should().BeTrue($"Earth's blue shows below the hull, not {earth}");
        (floor.r + floor.g + floor.b).Should().BeGreaterThan(side.r + side.g + side.b + 100, $"the lamp-lit chamber floor {floor} is brighter than the hull's port face {side}");
    }
}
