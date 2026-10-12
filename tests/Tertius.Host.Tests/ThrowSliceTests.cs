using System.Numerics;
using Tertius.Demo;
using Tertius.Host.Scripting;

namespace Tertius.Host.Tests;

/// <summary>
/// The last step of the vertical slice, "the person throws an object from one frame into another" (ticket ter-upyg): one
/// headless run of the checked-in script scripts/throw.json, at the orbit scene's start. The person turns toward the
/// airlock and walks into its chamber. It presses E, which cycles the airlock and picks up the crate on the chamber's
/// floor. When the hatch is open, it faces out of the hatch, holds the left mouse button for a full charge and lets go.
/// The crate flies out through the hatch, leaves the ship's bubble, passes into earth, and falls freely beside the ship.
/// Every test of the class reads that one run.
/// </summary>
public sealed class ThrowRun() : CheckedInRun("throw");

[Collection(GpuCollection.Name)]
public sealed class ThrowSliceTests(ThrowRun run) : IClassFixture<ThrowRun>
{
    // In the ship's axes, a crate's center lies 3.3 m below the ship's center on the floor, and a standing person's eye
    // 1.85 m below it. The cabin's gravity changes a velocity by 9.81 m/s² / 60 = 0.1635 m/s in a tick.
    private const double CrateOnTheFloor = -3.3, Eye = -1.85;
    private const float GravityPerTick = 9.81f / 60;

    // The dumps of the run after the throw, grouped by the frame that the thrown crate is in.
    private static readonly string[] InShip = ["thrown", "flying", "leaving"];
    private static readonly string[] InEarth = ["in-earth", "beside-the-ship"];

    private StateDump Dump(string name) => run.Result.Dump(name);

    /// <summary>The ids in the dump that the dump "in-chamber" does not have: the entities that the person spawned since then.</summary>
    private IEnumerable<string> NewIn(string name) => Dump(name).Entities.Select(e => e.Id).Except(Dump("in-chamber").Entities.Select(e => e.Id));

    /// <summary>The id of the drawn copy, the one entity that the person spawned when it took the crate.</summary>
    private string Copy => NewIn("carrying").Should().ContainSingle().Which;

    /// <summary>The id of the thrown crate, the one entity that the throw spawned.</summary>
    private string Thrown => NewIn("thrown").Should().ContainSingle().Which;

    /// <summary>
    /// The thrown crate's position and velocity relative to the ship, in the ship's axes, in <paramref name="name"/>, as
    /// SpacewalkSliceTests finds the person's. In the ship's frame, these are the crate's own. In earth, they come from the
    /// poses and velocities of the crate and the ship there. The ship's frame turns with the ship, so a point at rest in
    /// it moves at the ship's velocity plus the ship's angular velocity crossed with the point's offset from the ship's
    /// center.
    /// </summary>
    private (Double3 Position, Double3 Velocity) RelativeToShip(string name)
    {
        EntityRecord crate = Dump(name).Entity(Thrown), ship = Dump(name).Entity("ship");
        if (crate.Pose.Frame == "ship")
            return (crate.Pose.Position, crate.Velocity);
        crate.Pose.Frame.Should().Be("earth");
        ship.Pose.Frame.Should().Be("earth");
        Double3 offset = crate.Pose.Position - ship.Pose.Position;
        Double3 velocity = (Double3)crate.Velocity - ship.Velocity - Double3.Cross(ship.AngularVelocity, offset);
        DQuaternion toShip = ship.Pose.Rotation.Conjugate();
        return (toShip.Rotate(offset), toShip.Rotate(velocity));
    }

    private static void ExpectNear(Double3 actual, Double3 expected, double tolerance) =>
        Double3.Distance(actual, expected).Should().BeLessThan(tolerance, $"{actual} is not {expected}");

    [Fact]
    public void PressingEInTheChamberPicksUpItsCrateAndADrawnCopyRidesInFrontOfThePersonsEye()
    {
        Dump("in-chamber").Entity("chamber-crate").Pose.Position.Y.Should().BeApproximately(CrateOnTheFloor, 0.005);
        Dump("carrying").Entities.Should().NotContain(e => e.Id == "chamber-crate", "the crate went when the person took it");
        Dump("carrying").Events.Should().Contain(e => e.Event == nameof(CrateOffer) && e.From == "chamber-crate" && e.To == new ToEntity("person"));
        Dump("carrying").Events.Should().Contain(e => e.Event == nameof(CrateTaken) && e.From == "person" && e.To == new ToEntity("chamber-crate"));

        // The person faces the ship's -X, so its right is the ship's -Z. The copy rides 0.35 m to the right of the eye,
        // 0.5 m below it and 0.9 m ahead. The eye is 0.75 m above the person's center.
        EntityRecord person = Dump("carrying").Entity("person"), copy = Dump("carrying").Entity(Copy);
        copy.Pose.Frame.Should().Be("ship");
        ExpectNear(copy.Pose.Position - person.Pose.Position, new Double3(-0.9, 0.75 - 0.5, -0.35), 1e-4);
    }

    [Fact]
    public void TheThrowLetsGoOfTheDrawnCopy()
    {
        string copy = Copy;

        Dump("thrown").Events.Should().Contain(e => e.Event == "Destroy" && e.From == "person" && e.To == new ToEntity(copy));
        Dump("thrown").Entities.Should().NotContain(e => e.Id == copy);
    }

    [Fact]
    public void AFullChargeThrowsTheCrateOutAt12MetersPerSecondAlongTheView()
    {
        // The person stands still and faces the ship's -X. Physics reports a standing person's velocity with one tick of
        // the cabin's gravity, which the floor takes back in the next step, and the crate starts with the person's velocity.
        // The dump comes after the crate's first step, in which the cabin's gravity pulls it for one more tick.
        Vector3 person = Dump("carrying").Entity("person").Velocity;
        EntityRecord crate = Dump("thrown").Entity(Thrown);

        crate.Pose.Frame.Should().Be("ship");
        ExpectNear(crate.Velocity - person, new Double3(-Carry.MaxThrowSpeed, -GravityPerTick, 0), 1e-3);
        // It starts on the view, level with the eye and in the middle of the hatch's width, 10 m aft of the ship's center.
        crate.Pose.Position.Z.Should().BeApproximately(10, 1e-4);
        crate.Pose.Position.Y.Should().BeApproximately(Eye, 0.01);
    }

    [Fact]
    public void TheThrowPushesTheStandingPersonBackForOneStep()
    {
        EntityRecord before = Dump("carrying").Entity("person"), thrown = Dump("thrown").Entity("person"), after = Dump("flying").Entity("person");

        // 5 kg thrown at 12 m/s along the ship's -X pushes 70 kg back along +X at 5 · 12 / 70 = 0.857 m/s. The person
        // stands, so in its next step physics gives it its walk again, which is none: it moved back 1.4 cm and stopped.
        thrown.Velocity.X.Should().BeApproximately(5 * Carry.MaxThrowSpeed / 70, 1e-4f);
        after.Velocity.X.Should().BeApproximately(0, 1e-4f);
        (after.Pose.Position.X - before.Pose.Position.X).Should().BeApproximately(5 * 12 / 70.0 / 60, 1e-4);
        foreach (EntityRecord person in new[] { before, thrown, after })
            person.Standing.Should().BeTrue();
    }

    [Fact]
    public void TheCrateFliesOutThroughTheHatchWithoutTouchingAnything()
    {
        string crate = Thrown;

        foreach (string name in InShip.Concat(InEarth))
            Dump(name).Events.Should().NotContain(e => e.Event == "Collision" && e.To == new ToEntity(crate), $"nothing touched the crate before {name}");
        RelativeToShip("leaving").Position.X.Should().BeLessThan(-10, "the crate is out of the hull, whose port face is at x = -10 m");
    }

    [Fact]
    public void TheCrateLeavesTheShipsFrameForEarthPastTheShipsBubble()
    {
        foreach (string name in InShip)
            Dump(name).Entity(Thrown).Pose.Frame.Should().Be("ship", $"the crate is in the ship's frame in {name}");
        foreach (string name in InEarth)
            Dump(name).Entity(Thrown).Pose.Frame.Should().Be("earth", $"the crate is past the ship's bubble in {name}");
        // The crate's bounding sphere has a radius of 0.35 m. It leaves the ship's frame once its center is more than
        // 100 + 0.35 + 2 m from the ship's center: the bubble's radius, that sphere's radius and a margin of 2 m.
        RelativeToShip("leaving").Position.Length.Should().BeLessThan(102.35);
        RelativeToShip("in-earth").Position.Length.Should().BeGreaterThan(102.35);
        Dump("in-earth").Events.Should().ContainSingle(e => e.Event == "FrameChanged" && e.To == new ToEntity(Thrown));
    }

    [Fact]
    public void InEarthTheCrateFallsFreelyBesideTheShipAndKeepsItsMotionRelativeToTheShip()
    {
        (Double3 Position, Double3 Velocity) leaving = RelativeToShip("leaving"), inEarth = RelativeToShip("in-earth"), beside = RelativeToShip("beside-the-ship");
        double seconds = Dump("beside-the-ship").Time - Dump("in-earth").Time;

        // Out of the cabin, the crate moves at the 12 m/s of the throw along the ship's -X. It took the 0.16 m/s of the
        // person's reported velocity, and the cabin's gravity pulled it in its five steps before it left the cabin's box.
        leaving.Velocity.X.Should().BeApproximately(-Carry.MaxThrowSpeed, 1e-3);
        leaving.Velocity.Y.Should().BeApproximately(-6 * GravityPerTick, 1e-3);
        leaving.Velocity.Z.Should().BeApproximately(0, 1e-3);
        // The hand-off keeps the crate's motion relative to the ship.
        ExpectNear(inEarth.Velocity, leaving.Velocity, 1e-3);
        // In earth, the crate and the ship fall freely round Earth together, so the crate moves on along a straight line
        // relative to the ship. If Earth's gravity did not pull the crate as it pulls the ship, the crate would end
        // 8.7 m/s² · (5.5 s)² / 2 = 132 m off that line. The ship turns once per orbit about its own X, and in the ship's
        // turning axes the crate's 0.98 m/s along -Y bends 0.012 m/s toward -Z in the 5.5 s, which takes it 4 cm off the
        // line.
        seconds.Should().BeApproximately(5.5, 1e-6);
        ExpectNear(beside.Position, inEarth.Position + inEarth.Velocity * seconds, 0.05);
        ExpectNear(beside.Velocity, inEarth.Velocity, 0.02);
    }

    [Fact]
    public void TheRunReportsNoError()
    {
        Dump("beside-the-ship").Errors.Should().BeEmpty();
        Dump("beside-the-ship").Camera.Should().Be("person");
        run.ValidationMessages.Should().BeEmpty();
    }

    private static void ExpectGreen((int R, int G, int B) color, string what) =>
        (color.G > color.R + 20 && color.G > color.B + 20).Should().BeTrue($"{what} is the green of a crate, not {color}");

    [Fact]
    public void ThePersonSeesTheCarriedCrateLowOnTheRightAndThenTheThrownCrateInTheHatch()
    {
        // In "aim", the person faces the open hatch. The carried copy fills the bottom right of the image: its front face
        // spans columns 774 to 1130 and rows 627 to 720, below its top face. Black space shows through the hatch above
        // Earth. Three ticks after the throw, the crate is 1.5 m ahead in the hatch, against space, and spans columns 544
        // to 736 and rows 283 to 475. The copy is gone from the bottom right, where the chamber's lamp-lit wall shows.
        Png aim = run.Capture("aim"), thrown = run.Capture("throw");
        (int r, int g, int b) space = aim.Average(640, 300), wall = thrown.Average(950, 680);

        ExpectGreen(aim.Average(950, 680), "the front of the carried crate");
        (space.r + space.g + space.b).Should().BeLessThan(30, $"black space shows through the hatch, not {space}");
        ExpectGreen(thrown.Average(640, 380), "the thrown crate in the hatch");
        (wall.r > wall.g && wall.g > wall.b).Should().BeTrue($"the chamber's warm gray wall shows where the copy was, not {wall}");
    }
}
