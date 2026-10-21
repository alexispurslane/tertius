namespace Tertius.Demo;

/// <summary>
/// The cut list of the Blue Danube demo (docs/plans/blue-danube-demo.md, "The cut list"): the parts of the sequence, its
/// trims, the check panels of the parking orbit, of the wheel's spin gravity and of the coda's throws, and the two title cards. It names films of
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
            new("dock", "The docking", "The shuttle matches the station's spin and lands on its deck."),
            new(BlueDanube.ExitSections[0].Name, "The wheel", "Down a spoke to the wheel's floor, in full spin gravity."),
            new(BlueDanube.CodaSections[0].Name, "Loose boxes", "In a turning wheel, a thrown box flies straight and the floor turns under it."),
        ],
        // No trims: the user keeps every frame where any part of the shuttle or the station shows, however dark
        // (2026-10-20), and a measure of the master found one of them in every frame (docs/plans/blue-danube-demo.md,
        // "The dark frames").
        Trims: [],
        Panels:
        [
            // The coast in the parking orbit, from the insertion to the transfer burn.
            new("orbit", "night", CheckPanels.ParkingOrbit),
            // The brake run at the chute's end, the landing and the person on the wheel's floor, to the end of the exit.
            new("brake-run", BlueDanube.ExitSections[^1].Name, CheckPanels.SpinGravity),
            // The two throws of the coda, from the walk to the stand to the end of the run.
            new(BlueDanube.CodaSections[1].Name, BlueDanube.CodaSections[^1].Name, CheckPanels.Throws),
        ],
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
