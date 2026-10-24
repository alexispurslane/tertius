using Tertius.Host;
using Tertius.Host.Scripting;
using Tertius.Simulation;
using DemoStation = Tertius.Demo.DemoScenes.DemoStation;
using DemoSurface = Tertius.Demo.DemoScenes.DemoSurface;
using LaunchRamp = Tertius.Demo.DemoScenes.LaunchRamp;
using Shuttle = Tertius.Demo.DemoScenes.Shuttle;

namespace Tertius.Demo;

/// <summary>
/// The Blue Danube demo's one unbroken run, as a script of film steps (docs/plans/blue-danube-demo.md, "The long script"):
/// one <see cref="FilmSection"/> for each part of the sequence, in its order, in the scene <c>demo-surface</c>. Today the
/// sections cover the parts that exist: the walk through the terminal into the shuttle, the launch to orbit with the pen,
/// the rendezvous with the station, to the hold point on its axis, the docking in the station's bay, and the exit down the
/// spoke to the wheel's floor. Each later part of the demo adds its sections at the end of <see cref="Sections"/>.
/// </summary>
/// <remarks>
/// A section films every tick where the action is fast, and every Nth tick for the long climb and the long waits and
/// flights of the rendezvous, which gives
/// the time-lapse. The script is meant for a video at 60 frames per second (<c>--fps 60</c>), so a section that films every
/// tick plays at the speed of the simulation, and one that films every Nth tick plays N times faster. Each cue reads the
/// run, or a place of the scene's constants, never a tick number copied from an earlier run, so the sections follow the
/// scene when it changes.
/// </remarks>
public static class BlueDanube
{
    /// <summary>The scene of the run.</summary>
    public const string SceneName = "demo-surface";

    /// <summary>The ticks in one second of the simulation, the engine's fixed rate.</summary>
    private const int Second = 60;

    /// <summary>The scene of the exit's short script, which starts at the docking's end state.</summary>
    public const string ExitSceneName = "demo-exit";

    /// <summary>How long the film <c>spin-up</c> films the spin-up from outside, in seconds, before the rim camera takes over.</summary>
    private const double SpinUpFilmed = 20;

    /// <summary>How near the slot's outer face the nose comes before the bay camera takes over from the window camera, in meters.</summary>
    private const double NoseToSlot = 10;

    /// <summary>
    /// The apoapsis above Earth's mean radius at which the burn of the transfer has nearly ended, in meters: 399 km, below
    /// the transfer orbit's 400.5 km.
    /// </summary>
    private const double TransferApoapsis = 399e3;

    /// <summary>How near the direction to the station the nose must point for the turn to end, in radians: 2 degrees.</summary>
    private const double NoseOnStation = 2 * Math.PI / 180;

    /// <summary>The size of the captures: 1280 by 720 pixels.</summary>
    public static ScriptWindow Window { get; } = new(1280, 720);

    /// <summary>
    /// The sections of the exit (docs/plans/blue-danube-demo.md, "The exit", "Sections of the long script"): the person
    /// opens its belt and walks out of the hatch, crosses the deck, steps into the chute's top, slides down the spoke,
    /// lands on the wheel's floor and walks a few steps. Each cue reads the exit component's case or the person's place in
    /// the station's frame, and none reads the frame that holds the station. They follow the docking's <c>docked</c> at
    /// the end of <see cref="Sections"/>, and they also run alone in <c>scripts/demo-exit.json</c>, whose scene starts at
    /// the docking's end state. They come before <see cref="Sections"/> in this file, because a static property's
    /// initializer runs in the order of the file, and <see cref="Sections"/> reads them.
    /// </summary>
    public static IReadOnlyList<FilmSection> ExitSections { get; } =
    [
        // The order, the first steps and the walk aft to the doorway, filmed from inside the cabin.
        new(Name: "unbelt", Camera: "cabin-camera", Every: 1, Until: (world, scene) => PersonInCabin(world, scene).X > Shuttle.Cabin.X - 0.5,
            Posts: [new(new ToEntity("person"), nameof(LeaveShip))]),
        // The step down onto the deck, the walk along it and the step into the chute's top.
        new(Name: "deck", Camera: "deck-camera", Every: 1, Until: (world, scene) => ExitOf(world, scene).Value is Sliding,
            Hold: Second * 3 / 2),
        // The drop through the hole, seen from below in the spoke.
        new(Name: "chute-top", Camera: "spoke-camera", Every: 1,
            Until: (world, scene) => PersonInStation(world, scene).X < DemoStation.Deck - 10),
        // The slide down the open trough from behind and above the person, filmed every second tick, so it plays at twice
        // the speed of the simulation.
        new(Name: "chute", Camera: "slide-camera", Every: 2,
            Until: (world, scene) => Radius(PersonInStation(world, scene)) > DemoStation.CeilingRadius - 10),
        // The brake run, the landing and the first steps on the floor.
        new(Name: "brake-run", Camera: "wheel-camera", Every: 1, Until: (world, scene) => ExitOf(world, scene).Value is Strolling,
            Hold: Second),
        // The last steps spinward, and the person standing still in a full gravity.
        new(Name: "floor", Camera: "follow-camera", Every: 1, Until: (world, scene) => ExitOf(world, scene).Value is Arrived,
            Hold: Second * 2)
    ];

    /// <summary>
    /// The sections of the coda (docs/plans/blue-danube-demo.md, "The coda", "Sections of the long script"): the person puts
    /// the pen away and walks into the stack of loose boxes, which falls, then walks to the stand and throws one box
    /// spinward and one antispinward. Each cue reads the person's <see cref="BoxPlay"/> or the flight recorder of a thrown
    /// box. They follow the exit's sections, in the long script and in <c>scripts/demo-exit.json</c>, and come before
    /// <see cref="Sections"/> in this file for the same reason as <see cref="ExitSections"/>.
    /// </summary>
    public static IReadOnlyList<FilmSection> CodaSections { get; } =
    [
        // The order, the walk into the stack, its fall, and the boxes as they settle.
        new(Name: "stack", Camera: "stack-camera", Every: 1, Until: (world, scene) => PlayOf(world, scene).Value is not (Idle or Shoving),
            Hold: Second * 2,
            Posts: [new(new ToEntity("person"), nameof(PlayWithBoxes))]),
        // The walk to the stand, the take and the spinward throw, which lands short of its flat field's mark, until the
        // person takes the second box.
        new(Name: "spinward", Camera: "spinward-camera", Every: 1,
            Until: (world, scene) => PlayOf(world, scene).Value is Taking { Throw: 1 }),
        // The second take and the antispinward throw, which lands long of its mark, and the play's end.
        new(Name: "antispinward", Camera: "antispinward-camera", Every: 1, Until: (world, scene) => PlayOf(world, scene).Value is Played,
            Hold: Second * 2)
    ];

    /// <summary>The sections of the run, in their order.</summary>
    public static IReadOnlyList<FilmSection> Sections { get; } =
    [
        // The surface: the person walks through the terminal, across the pad and into the shuttle, to its seat.
        new(Name: "overview", Camera: "overview", Every: 1, Until: FilmSection.Now, Hold: Second * 3 / 2),
        // The low Sun and the crescent Moon over the parked shuttle. The film takes its second from the walk, whose cue reads
        // the person's place, so every later tick keeps its tick.
        new(Name: "sky", Camera: "sky-camera", Every: 1, Until: FilmSection.Now, Hold: Second),
        new(Name: "approach", Camera: "follow-camera", Every: 1,
            Until: (world, scene) => PersonOnTheGround(world, scene).X <
                              DemoSurface.TerminalCenter.X + DemoSurface.TerminalHalf.X - 1),
        new(Name: "terminal", Camera: "terminal-camera", Every: 1,
            Until: (world, scene) => PersonOnTheGround(world, scene).X <
                              DemoSurface.TerminalCenter.X - DemoSurface.TerminalHalf.X - 1),
        new(Name: "pad", Camera: "follow-camera", Every: 1,
            Until: (world, scene) => Double3.Distance(PersonInCabin(world, scene), Shuttle.OutsideHatch) < 1.5),
        // The follow camera, which looks through the hatch after the person, meets the hull's side and jumps into the
        // cabin, so the apron camera films the boarding until the follow camera is in the cabin. The outlines show the
        // parked shuttle's bubble, which the person walks into.
        new(Name: "boarding", Camera: "apron-camera", Every: 1,
            Until: (world, scene) =>
                InsideTheCabin(world.PoseIn(scene["follow-camera"], world.GetOwnedFrame(scene["shuttle"])).Position),
            Outlines: true),
        new(Name: "cabin", Camera: "follow-camera", Every: 1, Until: (world, scene) => PassengerOf(world, scene).Value is not Boarding,
            Hold: Second),
        // The person at its seat, seen from inside, and then from outside through the starboard glass.
        new(Name: "seated", Camera: "cabin-camera", Every: 1, Until: FilmSection.Now, Hold: Second * 3 / 4),
        new(Name: "seated-outside", Camera: "apron-camera", Every: 1, Until: FilmSection.Now, Hold: Second * 3 / 4),
        new(Name: "pen-on-the-pad", Camera: "pen-camera", Every: 1, Until: FilmSection.Now, Hold: Second),
        new(Name: "on-the-pad", Camera: "pad-camera", Every: 1, Until: FilmSection.Now, Hold: Second),

        // The launch: the ignition, the ramp, and the flight past its top.
        new(Name: "ignition", Camera: "pad-camera", Every: 1, Until: (world, scene) => Rise(world, scene) > LaunchRamp.Height / 8,
            Posts: [new(new ToEntity("shuttle"), nameof(Ignition))]),
        new(Name: "ramp", Camera: "ramp-camera", Every: 1, Until: (world, scene) => Rise(world, scene) > LaunchRamp.Height * 3 / 4),
        new(Name: "ramp-top", Camera: "ramp-top-camera", Every: 1, Until: (world, scene) => AscentOf(world, scene).Value is Climb or Orbit,
            Hold: Second),

        // The climb to orbit, as a time-lapse: the ground falls away, the belted person and the pen in the cabin, and the
        // long climb to the circle 200 km up. The outlines of climb-low show the falling frame that forms round the
        // shuttle's bubble in the film's first 10 s, and those of climb-high the hand-off out of the turning ground's
        // bubble, 178.4 km up.
        new(Name: "climb-low", Camera: "climb-camera", Every: 4, Until: (world, scene) => Altitude(world, scene) > 15e3, Outlines: true),
        new(Name: "climb-cabin", Camera: "follow-camera", Every: 4, Until: FilmSection.Now, Hold: Second * 10),
        new(Name: "climb-pen", Camera: "pen-camera", Every: 4, Until: FilmSection.Now, Hold: Second * 4),
        new(Name: "climb-high", Camera: "chase-camera", Every: 20, Until: (world, scene) => AscentOf(world, scene).Value is Orbit,
            Outlines: true),

        // Orbit: the clip lets the pen go, and the person floats to it and takes it. The outlines of orbit show the shuttle's
        // own frame and its falling frame on the circle.
        new(Name: "orbit", Camera: "chase-camera", Every: 1, Until: FilmSection.Now, Hold: Second, Outlines: true),
        new(Name: "pen-release", Camera: "pen-camera", Every: 1, Until: PenReleased, Hold: Second * 2),
        // The person floats toward the pen camera on its way to the pen, so the hand camera takes over before the person
        // fills the pen camera's lens.
        new(Name: "fetch", Camera: "pen-camera", Every: 1,
            Until: (world, scene) => PersonToPenCamera(world, scene) < 1.5 || HandToPen(world, scene) < 1),
        // The hand camera films the end of the fetch, the take and the person holding the pen.
        new(Name: "take", Camera: "hand-camera", Every: 1, Until: (world, scene) => PassengerOf(world, scene).Value is Holding or Stopped,
            Hold: Second * 3),

        // The rendezvous (docs/plans/blue-danube-demo.md, "Sections of the long script"): the person floats back to its
        // seat with the pen and straps in. Then the shuttle waits in the parking orbit for its window, burns onto the
        // transfer in Earth's night, flies half an orbit to the arrival point, brakes there, and glides along the station's
        // axis to the hold point. Earth casts its shadow, and the parts in Earth's night are filmed from the cabin, where the
        // lamps light the strapped person. A section that waits longer than 10 minutes names a limit of twice its measured
        // length, rounded up to 100 s.
        new(Name: "belt", Camera: "cabin-camera", Every: 1, Until: (world, scene) => PassengerOf(world, scene).Value is Strapped,
            Hold: Second,
            Posts: [new(new ToEntity("person"), nameof(FastenBelt))]),
        new(Name: "phasing", Camera: "chase-camera", Every: 60, Until: InEarthsShadow, Limit: Second * 1000,
            Posts: [new(new ToEntity("shuttle"), nameof(Rendezvous))]),
        new(Name: "night", Camera: "cockpit-camera", Every: 120, Until: (world, scene) => RendezvousOf(world, scene).Value is Transfer,
            Limit: Second * 2300),
        // The first seconds of the transfer burn from outside, in Earth's night, where the main engine's plume glows behind
        // the dark hull (docs/plans/blue-danube-demo.md, "The plumes").
        new(Name: "burn-chase", Camera: "chase-camera", Every: 2, Until: FilmSection.Now, Hold: Second * 6),
        // The cabin is also weightless in the tick before the burn starts, so the end of the burn needs the transfer's
        // apoapsis too.
        new(Name: "burn", Camera: "cockpit-camera", Every: 2,
            Until: (world, scene) => Apoapsis(world, scene) > TransferApoapsis && CabinWeightless(world, scene),
            Hold: Second * 2),
        new(Name: "transfer-night", Camera: "cockpit-camera", Every: 240, Until: (world, scene) => !InEarthsShadow(world, scene),
            Limit: Second * 2000),
        new(Name: "transfer", Camera: "chase-camera", Every: 60, Until: (world, scene) => ToArrivalPoint(world, scene) < 10e3,
            Limit: Second * 2800),
        new(Name: "closing", Camera: "bow-camera", Every: 8, Until: (world, scene) => RendezvousOf(world, scene).Value is Brake),
        new(Name: "brake", Camera: "port-window-camera", Every: 1, Until: (world, scene) => RendezvousOf(world, scene).Value is Glide),
        new(Name: "turn", Camera: "window-camera", Every: 1, Until: (world, scene) => NoseFromStation(world, scene) < NoseOnStation),
        new(Name: "glide", Camera: "chase-camera", Every: 6, Until: (world, scene) => ToStation(world, scene) < 700),
        new(Name: "glide-window", Camera: "window-camera", Every: 4, Until: (world, scene) => ToStation(world, scene) < 400),
        // The outlines show the station's bubble ahead of the shuttle and the shuttle's frames.
        new(Name: "glide-rim", Camera: "rim-camera", Every: 4, Until: (world, scene) => ToStation(world, scene) < 200, Outlines: true),
        new(Name: "arrival", Camera: "cockpit-camera", Every: 4, Until: (world, scene) => RendezvousOf(world, scene).Value is Hold,
            Hold: Second * 3),
        // The hold, from the station's rim, and then the person through the starboard glass with the station ahead. The
        // two films take the 5 s that the hold had alone, so the run ends at the same tick.
        new(Name: "hold", Camera: "rim-camera", Every: 1, Until: FilmSection.Now, Hold: Second * 3),
        new(Name: "hold-chase", Camera: "chase-camera", Every: 1, Until: FilmSection.Now, Hold: Second * 2),

        // The docking (docs/plans/blue-danube-demo.md, "The docking", "Sections of the long script"): the shuttle waits at
        // the hold for the station's turn to bring its deck to the right roll, spins up to the station's rate, enters the
        // slot along the axis, moves out to the corner of the deck and lands there. The cues read the docking autopilot's
        // case and places in the station's frame, because the shuttle's frame changes at the hand-off.
        new(Name: "dock", Camera: "chase-camera", Every: 2, Until: (world, scene) => DockingOf(world, scene).Value is SpinUp,
            Posts: [new(new ToEntity("shuttle"), nameof(Dock))]),
        new(Name: "spin-up", Camera: "chase-camera", Every: 2,
            Until: (world, scene) => DockingOf(world, scene).Value is SpinUp { StartTime: var start } &&
                              world.Time - start >= SpinUpFilmed),
        new(Name: "spin-match", Camera: "hold-camera", Every: 2, Until: (world, scene) => DockingOf(world, scene).Value is Entry),
        // The window camera films the slot ahead until the nose is 10 m from the slot's outer face. The outlines show the
        // shuttle's entry into the station's turning bubble, 120 m from its center.
        new(Name: "entry", Camera: "window-camera", Every: 4,
            Until: (world, scene) => InStation(world, scene).Position.Y < DemoStation.HubHalf.Y + Shuttle.Hull.Z + NoseToSlot,
            Outlines: true),
        new(Name: "slot", Camera: "bay-camera", Every: 2, Until: (world, scene) => DockingOf(world, scene).Value is MoveOut),
        new(Name: "move-out", Camera: "cockpit-camera", Every: 2,
            Until: (world, scene) => Double3.Distance(InStation(world, scene).Position, DemoStation.ParkedPose.Position) < 1),
        new(Name: "landing", Camera: "bay-camera", Every: 1, Until: (world, scene) => DockingOf(world, scene).Value is Docked,
            Hold: Second * 2),
        new(Name: "docked", Camera: "cabin-camera", Every: 1, Until: FilmSection.Now, Hold: Second * 3),

        // The exit: the person leaves the docked shuttle, slides down the spoke and stands on the wheel's floor.
        .. ExitSections,
        .. CodaSections
    ];

    /// <summary>The scenes whose sections the generator knows: <see cref="SceneName"/> and <see cref="ExitSceneName"/>.</summary>
    public static IReadOnlyList<string> Scenes { get; } = [SceneName, ExitSceneName];

    /// <summary>
    /// The short scripts that the generator writes beside the long script, for a quick look at one part of the run: each
    /// file films the long script from its first section through its last (see <see cref="FilmedBetween"/>).
    /// <c>demo-docking.json</c> films the docking alone, from the section <c>dock</c> through <c>docked</c>.
    /// </summary>
    public static IReadOnlyList<(string File, string FirstSection, string LastSection)> Cuts { get; } =
        [("demo-docking.json", "dock", "docked")];

    /// <summary>
    /// The script of the run of <paramref name="sceneName"/>, the long run unless it names another of
    /// <see cref="Scenes"/>, which the generator rehearses in a new world (see <see cref="FilmScript.Generate"/>). The long
    /// run films <see cref="Sections"/>, and the exit's scene films <see cref="ExitSections"/> and <see cref="CodaSections"/>. Throws
    /// <see cref="ArgumentException"/> for a scene that is not one of <see cref="Scenes"/>.
    /// </summary>
    public static Script Generate(string sceneName = SceneName)
    {
        IReadOnlyList<FilmSection> sections = sceneName switch
        {
            SceneName => Sections,
            ExitSceneName => [.. ExitSections, .. CodaSections],
            _ => throw new ArgumentException($"The Blue Danube demo has no sections for the scene {sceneName}.",
                nameof(sceneName))
        };
        return FilmScript.Generate(DemoScenes.All.Single(scene => scene.Name == sceneName), Window, sections);
    }

    /// <summary>
    /// The long script <paramref name="full"/>, filmed only from the section <paramref name="firstSection"/> through the
    /// section <paramref name="lastSection"/>: every film before the first section runs as plain ticks, the camera steps
    /// before it are left out, and the cut ends with the last section's film. So the cut posts the same orders at the same
    /// ticks as the long script, reaches the first section at the same tick, and films the sections from the first through
    /// the last as the long script does. Throws <see cref="ArgumentException"/> when the script has no film of either name,
    /// or when the last film comes before the first.
    /// </summary>
    public static Script FilmedBetween(Script full, string firstSection, string lastSection)
    {
        ArgumentNullException.ThrowIfNull(full);
        List<ScriptStep> steps = [.. full.Steps];
        int film = steps.FindIndex(step => step.Value is FilmStep { Name: var name } && name == firstSection);
        if (film < 0)
            throw new ArgumentException($"The script has no film '{firstSection}'.", nameof(firstSection));
        int last = steps.FindIndex(step => step.Value is FilmStep { Name: var name } && name == lastSection);
        if (last < film)
            throw new ArgumentException($"The script has no film '{lastSection}' at or after '{firstSection}'.",
                nameof(lastSection));
        // The section starts at its camera step, the last before its film.
        int start = steps.Take(film).ToList().FindLastIndex(step => step.Value is CameraStep);
        IEnumerable<ScriptStep> before = steps.Take(start).SelectMany(step => step.Value switch
        {
            FilmStep each => [new RunStep(each.Frames * each.Every)],
            PostStep => [step],
            _ => Array.Empty<ScriptStep>()
        });
        return full with { Steps = [.. before, .. steps.Skip(start).Take(last + 1 - start)] };
    }

    /// <summary>
    /// True when <paramref name="position"/>, in the shuttle's frame, lies inside the cabin, 0.1 m from its walls, floor
    /// and roof.
    /// </summary>
    public static bool InsideTheCabin(Double3 position) =>
        Math.Abs(position.X) < Shuttle.Cabin.X - 0.1 && Math.Abs(position.Y) < Shuttle.Cabin.Y - 0.1 &&
        Math.Abs(position.Z) < Shuttle.Cabin.Z - 0.1;

    /// <summary>The person's position in the frame of the ground, earth-surface, in which the terminal's walls lie.</summary>
    private static Double3 PersonOnTheGround(World world, SceneInstance scene) =>
        world.PoseIn(scene["person"], world.GetOwnedFrame(scene["earth-surface"])).Position;

    /// <summary>
    /// The frame <c>earth</c>, which does not turn and holds Earth's point gravity: the cues of the flight read in it. Each
    /// reads a distance from Earth's center, or a state of the two-body orbit, which a velocity in the turning earth-surface
    /// would miss by the ground's turn.
    /// </summary>
    private static FrameHandle Earth(World world, SceneInstance scene) => world.GetOwnedFrame(scene["earth"]);

    /// <summary>The person's position in the shuttle's frame.</summary>
    private static Double3 PersonInCabin(World world, SceneInstance scene) =>
        world.PoseIn(scene["person"], world.GetOwnedFrame(scene["shuttle"])).Position;

    private static PassengerState PassengerOf(World world, SceneInstance scene) =>
        Part<Passenger>(world, scene["person"]).State;

    /// <summary>The component of type <typeparamref name="T"/> that the scene gave <paramref name="entity"/>.</summary>
    private static T Part<T>(World world, Entity entity) where T : class, IComponent =>
        world.GetComponent<T>(entity) ??
        throw new InvalidOperationException($"The scene gave {entity} no {typeof(T).Name}.");

    private static DisembarkState ExitOf(World world, SceneInstance scene) =>
        Part<Disembark>(world, scene["person"]).State;

    private static BoxPlayState PlayOf(World world, SceneInstance scene) =>
        Part<BoxPlay>(world, scene["person"]).State;

    /// <summary>The person's position in the station's frame.</summary>
    private static Double3 PersonInStation(World world, SceneInstance scene) =>
        world.PoseIn(scene["person"], world.GetOwnedFrame(scene["station"])).Position;

    /// <summary>How far <paramref name="position"/>, in the station's axes, is from the station's axis, +Y, in meters.</summary>
    private static double Radius(Double3 position) => Math.Sqrt(position.X * position.X + position.Z * position.Z);

    private static RendezvousState RendezvousOf(World world, SceneInstance scene) =>
        Part<RendezvousAutopilot>(world, scene["shuttle"]).State;

    private static AscentState AscentOf(World world, SceneInstance scene) =>
        Part<AscentAutopilot>(world, scene["shuttle"]).State;

    private static DockingState DockingOf(World world, SceneInstance scene) =>
        Part<DockingAutopilot>(world, scene["shuttle"]).State;

    /// <summary>The shuttle's pose in the station's frame.</summary>
    private static DPose InStation(World world, SceneInstance scene) =>
        world.PoseIn(scene["shuttle"], world.GetOwnedFrame(scene["station"]));

    /// <summary>True once the pen's clip has let the pen go, or the loose pen is gone.</summary>
    private static bool PenReleased(World world, SceneInstance scene) =>
        !world.IsAlive(scene["pen"]) || Part<Clip>(world, scene["pen"]).State.Value is Released;

    /// <summary>How far the shuttle's center is from Earth's center, less where it stood parked, in meters.</summary>
    private static double Rise(World world, SceneInstance scene) =>
        world.PoseIn(scene["shuttle"], Earth(world, scene)).Position.Length - DemoSurface.ShuttleParked.Position.Length;

    /// <summary>How high the shuttle's center is above Earth's mean radius, in meters.</summary>
    private static double Altitude(World world, SceneInstance scene) =>
        world.PoseIn(scene["shuttle"], Earth(world, scene)).Position.Length - DemoScenes.EarthRadius;

    /// <summary>
    /// True while the shuttle is in Earth's shadow: its center, in earth, lies on the far side of Earth's center from the
    /// Sun's center, less than <see cref="DemoScenes.EarthRadius"/> from the line through Earth's center toward it.
    /// </summary>
    public static bool InEarthsShadow(World world, SceneInstance scene)
    {
        FrameHandle earth = Earth(world, scene);
        Double3 position = world.PoseIn(scene["shuttle"], earth).Position,
            sun = world.PoseIn(scene["sun"], earth).Position.Normalized();
        double towardTheSun = Double3.Dot(position, sun);
        return towardTheSun < 0 && (position - sun * towardTheSun).Length < DemoScenes.EarthRadius;
    }

    /// <summary>The apoapsis of the two-body orbit of the shuttle's state in earth, in meters above Earth's mean radius.</summary>
    private static double Apoapsis(World world, SceneInstance scene)
    {
        FrameHandle earth = Earth(world, scene);
        Double3 position = world.PoseIn(scene["shuttle"], earth).Position,
            velocity = world.VelocityIn(scene["shuttle"], earth);
        double r = position.Length,
            energy = velocity.LengthSquared / 2 - DemoScenes.EarthMu / r,
            a = -DemoScenes.EarthMu / (2 * energy);
        double h = Double3.Cross(position, velocity).Length;
        double e = Math.Sqrt(Math.Max(0, 1 + 2 * energy * h * h / (DemoScenes.EarthMu * DemoScenes.EarthMu)));
        return a * (1 + e) - DemoScenes.EarthRadius;
    }

    /// <summary>True when the proper acceleration of the shuttle's frame, in its last step, lies below
    /// <see cref="FrameTree.ZeroGravityThreshold"/>.</summary>
    private static bool CabinWeightless(World world, SceneInstance scene) =>
        world.Frames.ProperAcceleration(world.GetOwnedFrame(scene["shuttle"])).Length() <
        FrameTree.ZeroGravityThreshold;

    /// <summary>
    /// How far the shuttle's center is from the arrival point of <see cref="RendezvousPlan.Demo"/>, on the station's axis
    /// from its measured center, in meters.
    /// </summary>
    private static double ToArrivalPoint(World world, SceneInstance scene)
    {
        FrameHandle earth = Earth(world, scene);
        DPose station = world.PoseIn(scene["station"], earth);
        Double3 arrival = station.Position +
                          station.Rotation.Rotate(Double3.UnitY) * RendezvousPlan.Demo.ArrivalDistance;
        return Double3.Distance(world.PoseIn(scene["shuttle"], earth).Position, arrival);
    }

    /// <summary>How far the shuttle's center is from the station's measured center, in meters.</summary>
    private static double ToStation(World world, SceneInstance scene)
    {
        FrameHandle earth = Earth(world, scene);
        return Double3.Distance(world.PoseIn(scene["shuttle"], earth).Position,
            world.PoseIn(scene["station"], earth).Position);
    }

    /// <summary>The angle between the shuttle's nose, its -Z, and the direction from its center to the station's, in radians.</summary>
    private static double NoseFromStation(World world, SceneInstance scene)
    {
        FrameHandle earth = Earth(world, scene);
        DPose shuttle = world.PoseIn(scene["shuttle"], earth);
        Double3 nose = shuttle.Rotation.Rotate(-Double3.UnitZ),
            toStation = (world.PoseIn(scene["station"], earth).Position - shuttle.Position).Normalized();
        return Math.Acos(Math.Clamp(Double3.Dot(nose, toStation), -1, 1));
    }

    /// <summary>How far the person's center is from the pen camera, in meters.</summary>
    private static double PersonToPenCamera(World world, SceneInstance scene)
    {
        FrameHandle cabin = world.GetOwnedFrame(scene["shuttle"]);
        return Double3.Distance(world.PoseIn(scene["person"], cabin).Position,
            world.PoseIn(scene["pen-camera"], cabin).Position);
    }

    /// <summary>
    /// How far the person's hand, at <see cref="Passenger.PenHold"/> in its view, is from the loose pen's center, in meters,
    /// or 0 once the loose pen is gone. The hand lines up <see cref="Passenger.LineUpGap"/> behind the pen and then closes in
    /// on it, slower as it comes nearer.
    /// </summary>
    private static double HandToPen(World world, SceneInstance scene)
    {
        Entity person = scene["person"], pen = scene["pen"];
        if (!world.IsAlive(pen))
            return 0;
        FrameHandle cabin = world.GetOwnedFrame(scene["shuttle"]);
        Double3 hand = world.PoseIn(person, cabin)
            .TransformPoint(world.GetRender(person).ViewOffset.TransformPoint(Passenger.PenHold));
        return Double3.Distance(hand, world.PoseIn(pen, cabin).Position);
    }
}
