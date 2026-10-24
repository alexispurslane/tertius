using Tertius.Demo;
using Tertius.Host.Scripting;

namespace Tertius.Host.Tests;

/// <summary>
/// The doorway scene: one headless run of the checked-in script scripts/doorway.json. The person holds W for 70 ticks,
/// which walks it 3.5 m at 3 m/s, from z = 6 m into the sensor volume in front of the door, and waits there for 1.5 s.
/// Then it holds S for 70 ticks, which walks it back to where it started, and waits for 1.5 s again. The sensor relays the
/// person's touch to the door's topic, so the door opens while the person is inside and shuts after it left. A person
/// ignores input while another entity is the camera, so the script makes the person the camera while it walks, and the
/// side camera for the captures. Every test of the class reads that one run.
/// </summary>
public sealed class DoorwayRun() : CheckedInRun("doorway");

public sealed class DoorwaySliceTests(DoorwayRun run) : IClassFixture<DoorwayRun>
{
    private StateDump Dump(string name) => run.Result.Dump(name);

    private Double3 PersonAt(string dump) => Dump(dump).Entity("person").Pose.Position;

    /// <summary>How far the door is open in <paramref name="dump"/>, from 0 (shut) to 1 (open).</summary>
    private double Open(string dump) =>
        (Dump(dump).Entity("door").Pose.Position.Y - DemoScenes.DoorwayDoorShutY) / DemoScenes.DoorwayDoorRise;

    [Fact]
    public void ThePersonStartsOutsideTheSensorAndTheDoorIsShut()
    {
        PersonAt("standing").Z.Should().BeApproximately(DemoScenes.DoorwayPersonStartZ, 0.01);
        Open("standing").Should().BeApproximately(0, 1e-4);
    }

    [Fact]
    public void WhileThePersonStandsInTheSensorTheDoorIsOpen()
    {
        // 70 ticks at 3 m/s are 3.5 m.
        PersonAt("inside").Z.Should().BeApproximately(DemoScenes.DoorwayPersonStartZ - 3.5, 0.02);
        Math.Abs(PersonAt("inside").Z - DemoScenes.DoorwaySensorZ)
            .Should()
            .BeLessThan(DemoScenes.DoorwaySensorHalfDepth, "the person is in the sensor");
        Open("inside").Should().BeApproximately(1, 1e-4);
    }

    [Fact]
    public void AfterThePersonLeftTheSensorTheDoorIsShutAgain()
    {
        // The capsule's radius is 0.3 m.
        PersonAt("outside").Z.Should().BeApproximately(DemoScenes.DoorwayPersonStartZ, 0.02);
        (PersonAt("outside").Z - DemoScenes.DoorwaySensorZ)
            .Should()
            .BeGreaterThan(DemoScenes.DoorwaySensorHalfDepth + 0.3, "the person left the sensor");
        Open("outside").Should().BeApproximately(0, 1e-4);
    }
}
