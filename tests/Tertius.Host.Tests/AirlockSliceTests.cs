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

    private Vector3 VelocityAt(string dump) => Dump(dump).Entity("person").Velocity;

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
        // Four seconds at 1.5 m/s² take 6 m/s off the drift away from the ship.
        (VelocityAt("turned-back").X - VelocityAt("drifting").X).Should().BeApproximately(6, 0.05f);
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
            Dump(name).Entity("person").Pose.Frame.Should().Be("ship", $"the ship's 100 m bubble holds the person in {name}");
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
    public void FromOutsideTheSunLightsTheHatchSillAboveTheDarkSideOfTheHull()
    {
        // The sun stands above the ship, so it lights the sill's top and not the hull's port face, which looks sideways.
        Png hull = run.Capture("hull");
        (int r, int g, int b) sill = hull.Average(640, 497), side = hull.Average(640, 200);
        (sill.r + sill.g + sill.b).Should().BeGreaterThan(side.r + side.g + side.b + 150, $"the sill {sill} is brighter than the hull's side {side}");
    }
}
