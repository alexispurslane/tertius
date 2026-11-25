using System.Text.Json.Nodes;
using Tertius.Demo;
using static Tertius.Host.Tests.HostTesting;
using DemoStation = Tertius.Demo.DemoScenes.DemoStation;
using Shuttle = Tertius.Demo.DemoScenes.Shuttle;
using SpinUp = Tertius.Demo.SpinUp;

namespace Tertius.Host.Tests;

/// <summary>
/// The docking in the checked-in long script, scripts/blue-danube.json (docs/plans/blue-danube-demo.md, "The docking"): the
/// run of <see cref="BlueDanubeRendezvousRun"/> goes on from the rendezvous's hold through the film <c>docked</c>, and the
/// exit's films follow it (see <see cref="BlueDanubeExitTests"/>). The
/// shuttle spins up at the hold point, enters the station's turning frame at its bubble, passes the slot, moves out to the
/// corner of the deck and the bay's -Z wall and parks there, static, with the strapped person at its seat.
/// </summary>
[Collection(BlueDanubeRendezvousRun.Collection)]
public sealed class BlueDanubeDockingTests(BlueDanubeRendezvousRun run)
{
    private const double Degree = Math.PI / 180;

    /// <summary>The ticks from the first of the spin-up to the end of the film <c>docked</c>, where the exit starts.</summary>
    private IReadOnlyList<RendezvousTick> Docking { get; } =
    [.. run.ThroughDocked.SkipWhile(tick => tick.Docking.Value is Standby or Waiting)];

    private static double AngleBetween(DQuaternion a, DQuaternion b) => a.AngularVelocityTo(b, 1).Length;

    /// <summary>
    /// The script orders the docking during the rendezvous's hold. In the first tick of the spin-up the rendezvous still
    /// holds, and the shuttle lies within the hold's tolerances in the station's axes: within 0.5 m of the hold point, with
    /// its nose within 1 degree of minus the axis. It is in earth, the frame that holds the station, which does not turn.
    /// </summary>
    [Fact]
    public void TheDockingStartsFromTheRendezvousHold()
    {
        Docking.Should().NotBeEmpty("the docking starts");
        RendezvousTick start = Docking[0];
        start.Docking.Value.Should().BeOfType<SpinUp>();
        start.Rendezvous.Value.Should().BeOfType<Hold>();
        start.ShuttleFrame.Should().Be(run.Earth, "the shuttle docks from earth, which holds the station");
        (start.InStation.Position - DemoStation.Layout.HoldPoint)
            .Length.Should()
            .BeLessThan(RendezvousAutopilot.HoldDistanceTolerance);
        Math.Acos(Math.Clamp(Double3.Dot(start.InStation.Rotation.Rotate(-Double3.UnitZ), -Double3.UnitY), -1, 1))
            .Should()
            .BeLessThan(Degree);
    }

    /// <summary>
    /// The shuttle enters the station's frame, its cabin frame becomes a child of it, and at the end of the film <c>docked</c>
    /// the hull is
    /// static at the parked pose: within 3 cm across the axis, 0.1 m along it and 0.5 degrees, and the docking autopilot is
    /// in <see cref="Docked"/>.
    /// </summary>
    [Fact]
    public void TheShuttleEntersTheStationsFrameAndDocksAtTheParkedPose()
    {
        RendezvousTick last = run.DockedEnd;

        Docking
            .Should()
            .Contain(tick => tick.ShuttleFrame == run.StationFrame, "the shuttle enters the station's frame");
        last.ShuttleFrame.Should().Be(run.StationFrame);
        last.CabinParent.Should().Be(run.StationFrame);
        last.Docking.Value.Should().BeOfType<Docked>();
        last.HullMotion.Should().Be(BodyMotion.Static);
        Double3 off = last.InStation.Position - DemoStation.Layout.ParkedPose.Position;
        Math.Abs(off.X).Should().BeLessThan(0.03);
        Math.Abs(off.Z).Should().BeLessThan(0.03);
        Math.Abs(off.Y).Should().BeLessThan(0.1);
        AngleBetween(last.InStation.Rotation, StationLayout.DockedRotation).Should().BeLessThan(0.5 * Degree);
    }

    /// <summary>
    /// The strapped person stays at its seat through the whole docking: within 0.65 m of the seat stand in every tick. The
    /// belt holds it while the felt gravity is weightless or leans more than 45 degrees from the floor's down, and the floor
    /// holds it otherwise. In the film <c>docked</c> it stands. Changed on purpose from the design's 0.15 m: on the move
    /// out of 35 m to the deck at 40 m from the axis (ter-9wwx, measured on 2026-11-24), the belt lets go in the move out's
    /// weak, tilted gravity, and the person settles on the floor 0.55 m to port of the seat stand, and stays there. The
    /// landing's jolt moves it to 0.62 m for a moment. On the old deck at 8 m it stood 0.31 m to port.
    /// </summary>
    [Fact]
    public void TheStrappedPassengerStaysAtItsSeatThroughTheDocking()
    {
        Docking.Should().OnlyContain(tick => tick.Passenger.Value is Strapped);
        Docking.Max(tick => (tick.PersonInCabin - Shuttle.SeatStand).Length).Should().BeLessThan(0.65);
        run.DockedEnd.PersonStanding.Should().BeTrue("the person stands at its seat in the docked cabin");
        (run.DockedEnd.PersonInCabin - Shuttle.SeatStand)
            .X.Should()
            .BeNegative("the person stands on the port side of the seat stand, down the felt slope");
    }

    /// <summary>The person keeps the drawn copy of the pen in its hand: in every tick of the docking, the copy lies within 5 cm of the hand.</summary>
    [Fact]
    public void ThePenStaysInThePassengersHandThroughTheDocking() =>
        Docking.Should().OnlyContain(tick => tick.PenFromHand != null && tick.PenFromHand < 0.05);

    /// <summary>
    /// The person sits at its seat from the parking orbit through the docking, so its heading holds (docs/plans/blue-danube-demo.md,
    /// "The person's walk"): in every tick to the end of the film <c>docked</c>, it lies within 0.01 radians of its heading in
    /// the first tick of the orbit. This covers the fetch of the pen in orbit, where the person floats, and the move out,
    /// where the belt lets go and the person's feet settle on the floor to port.
    /// </summary>
    [Fact]
    public void ThePersonsHeadingHoldsFromTheOrbitThroughTheDocking()
    {
        double first = run.Ticks[0].PersonHeading;

        run.ThroughDocked.Max(tick => Math.Abs(Math.IEEERemainder(tick.PersonHeading - first, 2 * Math.PI)))
            .Should()
            .BeLessThan(0.01);
    }

    /// <summary>
    /// In every frame of the films <c>slot</c> and <c>landing</c>, from <c>bay-camera</c>, the corners of the slot on both
    /// faces of its wall and the corners of the parked shuttle's box lie in the picture.
    /// </summary>
    [Fact]
    public void TheBayCameraSeesTheSlotAndTheParkedHull()
    {
        RendezvousFrame[] frames = [.. run.Frames.Where(frame => frame.Of("slot") || frame.Of("landing"))];
        Double3[] corners = [.. Corners(DemoStation.Layout.Slot), .. Corners(DemoStation.Layout.ParkedBounds)];

        frames.Should().NotBeEmpty();
        foreach (RendezvousFrame frame in frames)
        foreach (Double3 corner in corners)
            Picture
                .Contains(Picture.Project(frame.ViewInStation, corner, frame.VerticalFov))
                .Should()
                .BeTrue($"the corner {corner} lies in the picture of {frame.Name}");
    }

    /// <summary>
    /// In every frame of the film <c>spin-match</c>, from <c>hold-camera</c>, the corners of the hub's box and of the
    /// shuttle's box lie in the picture, and the shuttle's box covers at least 1 percent of it, so the end of the spin-up
    /// shows both the shuttle and the hub that it turns with.
    /// </summary>
    [Fact]
    public void TheHoldCameraFramesTheHubAndTheShuttleInTheSpinMatch()
    {
        RendezvousFrame[] frames = [.. run.Frames.Where(frame => frame.Of("spin-match"))];
        Double3[] hubCorners = [.. Corners(DemoStation.Layout.Hub)];

        frames.Should().NotBeEmpty();
        foreach (RendezvousFrame frame in frames)
        {
            Double3[] shuttle =
            [
                .. Enumerable
                    .Range(0, 8)
                    .Select(corner =>
                        frame.ShuttleInStation.TransformPoint(
                            new(
                                (corner & 1) == 0 ? Shuttle.Hull.Min.X : Shuttle.Hull.Max.X,
                                (corner & 2) == 0 ? Shuttle.Hull.Min.Y : Shuttle.Hull.Max.Y,
                                (corner & 4) == 0 ? Shuttle.Hull.Min.Z : Shuttle.Hull.Max.Z
                            )
                        )
                    ),
            ];
            foreach (Double3 corner in hubCorners.Concat(shuttle))
                Picture
                    .Contains(Picture.Project(frame.ViewInStation, corner, frame.VerticalFov))
                    .Should()
                    .BeTrue($"the corner {corner} lies in the picture of {frame.Name}");
            Picture
                .Cover(
                    view: frame.ViewInStation,
                    body: frame.ShuttleInStation,
                    box: Shuttle.Hull,
                    verticalFov: frame.VerticalFov
                )
                .Should()
                .BeGreaterThan(0.01, $"the shuttle is no speck in {frame.Name}");
        }
    }

    /// <summary>
    /// In every frame of the film <c>entry</c>, from <c>window-camera</c>, the middle of the slot's outer face lies in the
    /// picture, and the line from the camera to it crosses both skins inside the holes of <c>pane-nose</c>.
    /// </summary>
    [Fact]
    public void TheWindowCameraSeesTheSlotThroughThePaneOfTheNose()
    {
        HullPane nose = Shuttle.Glass.Single(pane => pane.Id == "pane-nose");
        var slot = new Double3(0, DemoStation.Layout.Slot.Max.Y, 0);
        RendezvousFrame[] frames = [.. run.Frames.Where(frame => frame.Of("entry"))];

        frames.Should().NotBeEmpty();
        foreach (RendezvousFrame frame in frames)
        {
            Double3 inCabin = frame.ShuttleInStation.InverseTransformPoint(slot);
            Picture
                .Contains(Picture.Project(frame.View, inCabin, frame.VerticalFov))
                .Should()
                .BeTrue($"the slot is in the picture of {frame.Name}");
            PaneChecks
                .Through(nose, frame.View.Position, inCabin)
                .Should()
                .BeTrue($"the slot lies inside pane-nose in {frame.Name}");
        }
    }

    /// <summary>
    /// The only line that the run logs is the rendezvous autopilot's halt in the tick after the hand-off, when the shuttle
    /// is no longer in the frame that holds its station, as its design says. The docking already overrides it then. The
    /// world records no error.
    /// </summary>
    [Fact]
    public void TheOnlyLogLineOfTheDockingIsTheRendezvousHaltAtTheHandOff()
    {
        run.Logged.Should()
            .ContainSingle()
            .Which.Should()
            .EndWith($"{nameof(RendezvousAutopilot)} halts: the shuttle is not in the frame that holds its station.");
        RendezvousTick first = Docking.First(tick => tick.ShuttleFrame == run.StationFrame);
        Docking
            .TakeWhile(tick => tick != first)
            .Should()
            .OnlyContain(tick => tick.Rendezvous.Value is Hold, "the rendezvous holds until the hand-off");
        Docking.SkipWhile(tick => tick != first).Skip(2).Should().OnlyContain(tick => tick.Rendezvous.Value is Halted);
    }

    [Fact]
    public void TheWorldLogsNoErrors() => run.World.Errors.Should().BeEmpty();

    /// <summary>
    /// scripts/demo-docking.json films the docking alone: it is the long script, with every film before <c>dock</c> run as
    /// plain ticks and the camera steps before it left out, and with nothing after the film <c>docked</c>, so it posts the
    /// same orders at the same ticks, reaches the hold at the same tick, films the docking's sections as the long script
    /// does, and stops before the exit.
    /// </summary>
    [Fact]
    public void TheDockingScriptIsTheLongScriptWithOnlyTheDockingFilmed()
    {
        JsonArray full = JsonNode.Parse(
            File.ReadAllText(Path.Combine(RepositoryRoot(), "scripts", "blue-danube.json"))
        )!["steps"]!.AsArray();
        JsonNode docking = JsonNode.Parse(
            File.ReadAllText(Path.Combine(RepositoryRoot(), "scripts", "demo-docking.json"))
        )!;
        // The docking's first section is its camera step, then its post of the order.
        int first =
            full.ToList()
                .FindIndex(step =>
                    step!["do"]!.GetValue<string>() == "post" && step["event"]!.GetValue<string>() == nameof(Dock)
                ) - 1;
        int last = full.ToList()
            .FindIndex(step =>
                step!["do"]!.GetValue<string>() == "film"
                && step["name"]!.GetValue<string>() == BlueDanubeRendezvousRun.DockedFilm
            );
        var expected = new JsonArray();
        foreach ((JsonNode? step, int index) in full.Select((step, index) => (step, index)).Take(last + 1))
        {
            string kind = step!["do"]!.GetValue<string>();
            if (index >= first || kind == "post")
                expected.Add(step.DeepClone());
            else if (kind == "film")
                expected.Add(
                    new JsonObject
                    {
                        ["do"] = "run",
                        ["ticks"] = step["frames"]!.GetValue<int>() * step["every"]!.GetValue<int>(),
                    }
                );
        }

        first.Should().BePositive("the long script orders the docking");
        last.Should().BeLessThan(full.Count - 1, "the exit's films follow the docking in the long script");

        docking["scene"]!.GetValue<string>().Should().Be(BlueDanube.SceneName);
        JsonNode
            .DeepEquals(docking["steps"], expected)
            .Should()
            .BeTrue("the docking script runs the long script to the hold and films only the docking");
    }

    /// <summary>The eight corners of <paramref name="box"/>.</summary>
    private static IEnumerable<Double3> Corners(BoundingBox box) =>
        Enumerable
            .Range(0, 8)
            .Select(corner => new Double3(
                (corner & 1) == 0 ? box.Min.X : box.Max.X,
                (corner & 2) == 0 ? box.Min.Y : box.Max.Y,
                (corner & 4) == 0 ? box.Min.Z : box.Max.Z
            ));
}
