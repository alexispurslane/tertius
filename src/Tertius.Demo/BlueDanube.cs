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
/// and the rendezvous with the station, to the hold point on its axis. Each later part of the demo adds its sections at the end of <see cref="Sections"/>.
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

    /// <summary>The size of the captures: 1280 by 720 pixels.</summary>
    public static ScriptWindow Window { get; } = new(1280, 720);

    /// <summary>The ticks in one second of the simulation, the engine's fixed rate.</summary>
    private const int Second = 60;

    /// <summary>The sections of the run, in their order.</summary>
    public static IReadOnlyList<FilmSection> Sections { get; } =
    [
        // The surface: the person walks through the terminal, across the pad and into the shuttle, to its seat.
        new("overview", "overview", Every: 1, FilmSection.Now, Hold: Second * 3 / 2),
        new("approach", "follow-camera", Every: 1, (world, scene) => PersonOnTheGround(world, scene).X < DemoSurface.TerminalCenter.X + DemoSurface.TerminalHalf.X - 1),
        new("terminal", "terminal-camera", Every: 1, (world, scene) => PersonOnTheGround(world, scene).X < DemoSurface.TerminalCenter.X - DemoSurface.TerminalHalf.X - 1),
        new("pad", "follow-camera", Every: 1, (world, scene) => Double3.Distance(PersonInCabin(world, scene), Shuttle.OutsideHatch) < 1.5),
        // The follow camera, which looks through the hatch after the person, meets the hull's side and jumps into the
        // cabin, so the apron camera films the boarding until the follow camera is in the cabin.
        new("boarding", "apron-camera", Every: 1, (world, scene) => InsideTheCabin(world.PoseIn(scene["follow-camera"], world.GetOwnedFrame(scene["shuttle"])).Position)),
        new("cabin", "follow-camera", Every: 1, (world, scene) => PassengerOf(world, scene).Value is not Boarding, Hold: Second),
        // The person at its seat, seen from inside, and then from outside through the starboard glass.
        new("seated", "cabin-camera", Every: 1, FilmSection.Now, Hold: Second * 3 / 4),
        new("seated-outside", "apron-camera", Every: 1, FilmSection.Now, Hold: Second * 3 / 4),
        new("pen-on-the-pad", "pen-camera", Every: 1, FilmSection.Now, Hold: Second),
        new("on-the-pad", "pad-camera", Every: 1, FilmSection.Now, Hold: Second),

        // The launch: the ignition, the ramp, and the flight past its top.
        new("ignition", "pad-camera", Every: 1, (world, scene) => Rise(world, scene) > LaunchRamp.Height / 8,
            Posts: [new PostStep(new ToEntity("shuttle"), nameof(Ignition))]),
        new("ramp", "ramp-camera", Every: 1, (world, scene) => Rise(world, scene) > LaunchRamp.Height * 3 / 4),
        new("ramp-top", "ramp-top-camera", Every: 1, (world, scene) => AscentOf(world, scene).Value is Climb or Orbit, Hold: Second),

        // The climb to orbit, as a time-lapse: the ground falls away, the belted person and the pen in the cabin, and the
        // long climb to the circle 200 km up.
        new("climb-low", "climb-camera", Every: 4, (world, scene) => Altitude(world, scene) > 15e3),
        new("climb-cabin", "follow-camera", Every: 4, FilmSection.Now, Hold: Second * 10),
        new("climb-pen", "pen-camera", Every: 4, FilmSection.Now, Hold: Second * 4),
        new("climb-high", "chase-camera", Every: 20, (world, scene) => AscentOf(world, scene).Value is Orbit),

        // Orbit: the clip lets the pen go, and the person floats to it and takes it.
        new("orbit", "chase-camera", Every: 1, FilmSection.Now, Hold: Second),
        new("pen-release", "pen-camera", Every: 1, (world, scene) => PenReleased(world, scene), Hold: Second * 2),
        // The person floats toward the pen camera on its way to the pen, so the hand camera takes over before the person
        // fills the pen camera's lens.
        new("fetch", "pen-camera", Every: 1, (world, scene) => PersonToPenCamera(world, scene) < 1.5 || HandToPen(world, scene) < 1),
        // The hand camera films the end of the fetch, the take and the person holding the pen.
        new("take", "hand-camera", Every: 1, (world, scene) => PassengerOf(world, scene).Value is Holding or Stopped, Hold: Second * 3),

        // The rendezvous (docs/plans/blue-danube-demo.md, "Sections of the long script"): the person floats back to its
        // seat with the pen and straps in. Then the shuttle waits in the parking orbit for its window, burns onto the
        // transfer in Earth's night, flies half an orbit to the arrival point, brakes there, and glides along the station's
        // axis to the hold point. The parts in Earth's night are filmed from the cabin, because the demo's Earth casts no
        // shadow. A section that waits longer than 10 minutes names a limit of twice its length.
        new("belt", "cabin-camera", Every: 1, (world, scene) => PassengerOf(world, scene).Value is Strapped, Hold: Second,
            Posts: [new PostStep(new ToEntity("person"), nameof(FastenBelt))]),
        new("phasing", "chase-camera", Every: 60, InEarthsShadow, Limit: Second * 2100,
            Posts: [new PostStep(new ToEntity("shuttle"), nameof(Rendezvous))]),
        new("night", "cockpit-camera", Every: 120, (world, scene) => RendezvousOf(world, scene).Value is Transfer, Limit: Second * 1200),
        // The cabin is also weightless in the tick before the burn starts, so the end of the burn needs the transfer's
        // apoapsis too.
        new("burn", "cockpit-camera", Every: 2, (world, scene) => Apoapsis(world, scene) > TransferApoapsis && CabinWeightless(world, scene), Hold: Second * 2),
        new("transfer-night", "cockpit-camera", Every: 240, (world, scene) => !InEarthsShadow(world, scene), Limit: Second * 3000),
        new("transfer", "chase-camera", Every: 60, (world, scene) => ToArrivalPoint(world, scene) < 10e3, Limit: Second * 1800),
        new("closing", "bow-camera", Every: 8, (world, scene) => RendezvousOf(world, scene).Value is Brake),
        new("brake", "port-window-camera", Every: 1, (world, scene) => RendezvousOf(world, scene).Value is Glide),
        new("turn", "window-camera", Every: 1, (world, scene) => NoseFromStation(world, scene) < NoseOnStation),
        new("glide", "chase-camera", Every: 6, (world, scene) => ToStation(world, scene) < 700),
        new("glide-window", "window-camera", Every: 4, (world, scene) => ToStation(world, scene) < 400),
        new("glide-rim", "rim-camera", Every: 4, (world, scene) => ToStation(world, scene) < 200),
        new("arrival", "cockpit-camera", Every: 4, (world, scene) => RendezvousOf(world, scene).Value is Hold, Hold: Second * 3),
        // The hold, from the station's rim, and then the person through the starboard glass with the station ahead. The
        // two films take the 5 s that the hold had alone, so the run ends at the same tick.
        new("hold", "rim-camera", Every: 1, FilmSection.Now, Hold: Second * 3),
        new("hold-chase", "chase-camera", Every: 1, FilmSection.Now, Hold: Second * 2),
    ];

    /// <summary>The scene of the exit's short script, which starts at the docking's end state.</summary>
    public const string ExitSceneName = "demo-exit";

    /// <summary>
    /// The sections of the exit (docs/plans/blue-danube-demo.md, "The exit", "Sections of the long script"): the person
    /// opens its belt and walks out of the hatch, crosses the deck, steps into the chute's top, slides down the spoke,
    /// lands on the wheel's floor and walks a few steps. Each cue reads the exit component's case or the person's place in
    /// the station's frame, and none reads the frame that holds the station. They follow the docking's <c>docked</c> in
    /// the long script once the docking merges, and until then they run alone in <c>scripts/demo-exit.json</c>.
    /// </summary>
    public static IReadOnlyList<FilmSection> ExitSections { get; } =
    [
        // The order, the first steps and the walk aft to the doorway, filmed from inside the cabin.
        new("unbelt", "cabin-camera", Every: 1, (world, scene) => PersonInCabin(world, scene).X > Shuttle.Cabin.X - 0.5,
            Posts: [new PostStep(new ToEntity("person"), nameof(LeaveShip))]),
        // The step down onto the deck, the walk along it and the step into the chute's top.
        new("deck", "deck-camera", Every: 1, (world, scene) => ExitOf(world, scene).Value is Sliding, Hold: Second * 3 / 2),
        // The drop through the hole, seen from below in the spoke.
        new("chute-top", "spoke-camera", Every: 1, (world, scene) => PersonInStation(world, scene).X < DemoStation.Deck - 10),
        // The slide down the open trough from behind and above the person, filmed every second tick, so it plays at twice
        // the speed of the simulation.
        new("chute", "slide-camera", Every: 2, (world, scene) => Radius(PersonInStation(world, scene)) > DemoStation.CeilingRadius - 10),
        // The brake run, the landing and the first steps on the floor.
        new("brake", "wheel-camera", Every: 1, (world, scene) => ExitOf(world, scene).Value is Strolling, Hold: Second),
        // The last steps spinward, and the person standing still in a full gravity.
        new("floor", "follow-camera", Every: 1, (world, scene) => ExitOf(world, scene).Value is Arrived, Hold: Second * 2),
    ];

    /// <summary>
    /// The apoapsis above Earth's mean radius at which the burn of the transfer has nearly ended, in meters: 399 km, below
    /// the transfer orbit's 400.5 km.
    /// </summary>
    private const double TransferApoapsis = 399e3;

    /// <summary>How near the direction to the station the nose must point for the turn to end, in radians: 2 degrees.</summary>
    private const double NoseOnStation = 2 * Math.PI / 180;

    /// <summary>The scenes whose sections the generator knows: <see cref="SceneName"/> and <see cref="ExitSceneName"/>.</summary>
    public static IReadOnlyList<string> Scenes { get; } = [SceneName, ExitSceneName];

    /// <summary>
    /// The script of the run of <paramref name="sceneName"/>, the long run unless it names another of
    /// <see cref="Scenes"/>, which the generator rehearses in a new world (see <see cref="FilmScript.Generate"/>). The long
    /// run films <see cref="Sections"/>, and the exit's scene films <see cref="ExitSections"/>. Throws
    /// <see cref="ArgumentException"/> for a scene that is not one of <see cref="Scenes"/>.
    /// </summary>
    public static Script Generate(string sceneName = SceneName)
    {
        IReadOnlyList<FilmSection> sections = sceneName switch
        {
            SceneName => Sections,
            ExitSceneName => ExitSections,
            _ => throw new ArgumentException($"The Blue Danube demo has no sections for the scene {sceneName}.", nameof(sceneName)),
        };
        return FilmScript.Generate(DemoScenes.All.Single(scene => scene.Name == sceneName), Window, sections);
    }

    /// <summary>
    /// True when <paramref name="position"/>, in the shuttle's frame, lies inside the cabin, 0.1 m from its walls, floor
    /// and roof.
    /// </summary>
    public static bool InsideTheCabin(Double3 position) =>
        Math.Abs(position.X) < Shuttle.Cabin.X - 0.1 && Math.Abs(position.Y) < Shuttle.Cabin.Y - 0.1 && Math.Abs(position.Z) < Shuttle.Cabin.Z - 0.1;

    /// <summary>The person's position in the frame of the ground, earth-surface, in which the shuttle stands.</summary>
    private static Double3 PersonOnTheGround(World world, SceneInstance scene) => world.PoseIn(scene["person"], world.GetFrame(scene["shuttle"])).Position;

    /// <summary>The person's position in the shuttle's frame.</summary>
    private static Double3 PersonInCabin(World world, SceneInstance scene) => world.PoseIn(scene["person"], world.GetOwnedFrame(scene["shuttle"])).Position;

    private static PassengerState PassengerOf(World world, SceneInstance scene) => world.GetComponent<Passenger>(scene["person"])!.State;

    private static DisembarkState ExitOf(World world, SceneInstance scene) => world.GetComponent<Disembark>(scene["person"])!.State;

    /// <summary>The person's position in the station's frame.</summary>
    private static Double3 PersonInStation(World world, SceneInstance scene) => world.PoseIn(scene["person"], world.GetOwnedFrame(scene["station"])).Position;

    /// <summary>How far <paramref name="position"/>, in the station's axes, is from the station's axis, +Y, in meters.</summary>
    private static double Radius(Double3 position) => Math.Sqrt(position.X * position.X + position.Z * position.Z);

    private static RendezvousState RendezvousOf(World world, SceneInstance scene) => world.GetComponent<RendezvousAutopilot>(scene["shuttle"])!.State;

    private static AscentState AscentOf(World world, SceneInstance scene) => world.GetComponent<AscentAutopilot>(scene["shuttle"])!.State;

    /// <summary>True once the pen's clip has let the pen go, or the loose pen is gone.</summary>
    private static bool PenReleased(World world, SceneInstance scene) =>
        !world.IsAlive(scene["pen"]) || world.GetComponent<Clip>(scene["pen"])!.State.Value is Released;

    /// <summary>How far the shuttle's center is from Earth's center, less where it stood parked, in meters.</summary>
    private static double Rise(World world, SceneInstance scene) =>
        world.PoseIn(scene["shuttle"], world.GetFrame(scene["shuttle"])).Position.Length - DemoSurface.ShuttleParked.Position.Length;

    /// <summary>How high the shuttle's center is above Earth's mean radius, in meters.</summary>
    private static double Altitude(World world, SceneInstance scene) =>
        world.PoseIn(scene["shuttle"], world.GetFrame(scene["shuttle"])).Position.Length - DemoScenes.EarthRadius;

    /// <summary>
    /// True while the shuttle is in Earth's shadow: its center, in earth-surface, lies on the far side of Earth's center from
    /// the frame <c>sun</c>, less than <see cref="DemoScenes.EarthRadius"/> from the line through Earth's center toward it.
    /// </summary>
    private static bool InEarthsShadow(World world, SceneInstance scene)
    {
        FrameHandle surface = world.GetFrame(scene["shuttle"]);
        Double3 position = world.PoseIn(scene["shuttle"], surface).Position, sun = world.PoseIn(scene["sun"], surface).Position.Normalized();
        double towardTheSun = Double3.Dot(position, sun);
        return towardTheSun < 0 && (position - sun * towardTheSun).Length < DemoScenes.EarthRadius;
    }

    /// <summary>The apoapsis of the two-body orbit of the shuttle's state in earth-surface, in meters above Earth's mean radius.</summary>
    private static double Apoapsis(World world, SceneInstance scene)
    {
        FrameHandle surface = world.GetFrame(scene["shuttle"]);
        Double3 position = world.PoseIn(scene["shuttle"], surface).Position, velocity = world.VelocityIn(scene["shuttle"], surface);
        double r = position.Length, energy = velocity.LengthSquared / 2 - DemoScenes.EarthMu / r, a = -DemoScenes.EarthMu / (2 * energy);
        double h = Double3.Cross(position, velocity).Length;
        double e = Math.Sqrt(Math.Max(0, 1 + 2 * energy * h * h / (DemoScenes.EarthMu * DemoScenes.EarthMu)));
        return a * (1 + e) - DemoScenes.EarthRadius;
    }

    /// <summary>True when the proper acceleration of the shuttle's frame, in its last step, lies below <see cref="FrameTree.ZeroGravityThreshold"/>.</summary>
    private static bool CabinWeightless(World world, SceneInstance scene) =>
        world.Frames.ProperAcceleration(world.GetOwnedFrame(scene["shuttle"])).Length() < FrameTree.ZeroGravityThreshold;

    /// <summary>
    /// How far the shuttle's center is from the arrival point of <see cref="RendezvousPlan.Demo"/>, on the station's axis
    /// from its measured center, in meters.
    /// </summary>
    private static double ToArrivalPoint(World world, SceneInstance scene)
    {
        FrameHandle surface = world.GetFrame(scene["shuttle"]);
        DPose station = world.PoseIn(scene["station"], surface);
        Double3 arrival = station.Position + station.Rotation.Rotate(Double3.UnitY) * RendezvousPlan.Demo.ArrivalDistance;
        return Double3.Distance(world.PoseIn(scene["shuttle"], surface).Position, arrival);
    }

    /// <summary>How far the shuttle's center is from the station's measured center, in meters.</summary>
    private static double ToStation(World world, SceneInstance scene)
    {
        FrameHandle surface = world.GetFrame(scene["shuttle"]);
        return Double3.Distance(world.PoseIn(scene["shuttle"], surface).Position, world.PoseIn(scene["station"], surface).Position);
    }

    /// <summary>The angle between the shuttle's nose, its -Z, and the direction from its center to the station's, in radians.</summary>
    private static double NoseFromStation(World world, SceneInstance scene)
    {
        FrameHandle surface = world.GetFrame(scene["shuttle"]);
        DPose shuttle = world.PoseIn(scene["shuttle"], surface);
        Double3 nose = shuttle.Rotation.Rotate(-Double3.UnitZ), toStation = (world.PoseIn(scene["station"], surface).Position - shuttle.Position).Normalized();
        return Math.Acos(Math.Clamp(Double3.Dot(nose, toStation), -1, 1));
    }

    /// <summary>How far the person's center is from the pen camera, in meters.</summary>
    private static double PersonToPenCamera(World world, SceneInstance scene)
    {
        FrameHandle cabin = world.GetOwnedFrame(scene["shuttle"]);
        return Double3.Distance(world.PoseIn(scene["person"], cabin).Position, world.PoseIn(scene["pen-camera"], cabin).Position);
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
        Double3 hand = world.PoseIn(person, cabin).TransformPoint((Double3)world.GetRender(person).ViewOffset.TransformPoint(Passenger.PenHold));
        return Double3.Distance(hand, world.PoseIn(pen, cabin).Position);
    }
}
