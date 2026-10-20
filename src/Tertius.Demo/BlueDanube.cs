using Tertius.Host;
using Tertius.Host.Scripting;
using Tertius.Simulation;
using DemoSurface = Tertius.Demo.DemoScenes.DemoSurface;
using LaunchRamp = Tertius.Demo.DemoScenes.LaunchRamp;
using Shuttle = Tertius.Demo.DemoScenes.Shuttle;

namespace Tertius.Demo;

/// <summary>
/// The Blue Danube demo's one unbroken run, as a script of film steps (docs/plans/blue-danube-demo.md, "The long script"):
/// one <see cref="FilmSection"/> for each part of the sequence, in its order, in the scene <c>demo-surface</c>. Today the
/// sections cover the parts that exist: the walk through the terminal into the shuttle, and the launch to orbit with the
/// pen. Each later part of the demo adds its sections at the end of <see cref="Sections"/>.
/// </summary>
/// <remarks>
/// A section films every tick where the action is fast, and every Nth tick for the long climb and the coast, which gives
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
        new("seated", "cabin-camera", Every: 1, FilmSection.Now, Hold: Second * 3 / 2),
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

        // The coast in orbit, as a time-lapse. The rendezvous with the station follows here.
        new("coast", "chase-camera", Every: 10, FilmSection.Now, Hold: Second * 30),
    ];

    /// <summary>The script of the run, which the generator rehearses in a new world (see <see cref="FilmScript.Generate"/>).</summary>
    public static Script Generate() => FilmScript.Generate(DemoScenes.All.Single(scene => scene.Name == SceneName), Window, Sections);

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
