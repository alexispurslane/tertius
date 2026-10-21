namespace Tertius.Demo;

/// <summary>
/// The cut list of the Blue Danube demo (docs/plans/blue-danube-demo.md, "The cut list"): the parts of the sequence, its
/// trims, the check panel of the parking orbit, and the two title cards. It names films of
/// <see cref="BlueDanube.Sections"/>, never frame numbers, so it follows the script when the sections change.
/// </summary>
public static class BlueDanubeEdit
{
    /// <summary>The cut list.</summary>
    public static CutList Cuts { get; } = new(
        Parts:
        [
            new("overview", "Earth's surface", "The person walks through the terminal and boards the shuttle."),
            new("ignition", "The launch", "Up the ramp and into an orbit 200 km up."),
            new("orbit", "Weightless", "The ship's frame falls freely, so the cabin is weightless by itself."),
            new("belt", "The rendezvous", "A transfer of 170 degrees to the station, 400 km up."),
        ],
        // No trims: the user keeps every frame where any part of the shuttle or the station shows, however dark
        // (2026-10-20), and a measure of the master found one of them in every frame (docs/plans/blue-danube-demo.md,
        // "The dark frames").
        Trims: [],
        // The coast in the parking orbit, from the insertion to the transfer burn.
        Panels: [new("orbit", "night", CheckPanels.ParkingOrbit)],
        Opening: new TitleCard(5,
        [
            new("Tertius", 96),
            new("The Blue Danube", 40),
            new("", 24),
            new("One unbroken run of the engine at 60 ticks per second, recorded headless with its real renderer.", 24, Gray: true),
        ]),
        Closing: new TitleCard(8,
        [
            new("Gray-box meshes. No textures, no sky, no anti-aliasing.", 32),
            new("The engine is unfinished. The physics is the point.", 32),
            new("", 24),
        ]));
}
