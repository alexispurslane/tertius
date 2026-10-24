using Tertius.Demo;
using Tertius.Host.Scripting;
using Tertius.Physics;
using Tertius.Simulation;
using static Tertius.Host.Tests.HostTesting;
using DemoSurface = Tertius.Demo.DemoScenes.DemoSurface;
using Shuttle = Tertius.Demo.DemoScenes.Shuttle;

namespace Tertius.Host.Tests;

/// <summary>
/// The frames of the checked-in long script, scripts/blue-danube.json, through its film <see cref="LastFilm"/>, in one run
/// on the real World and the real Jolt backend with no renderer: at each frame, the run records what the frame's camera
/// sees of the person and the pen.
/// </summary>
public sealed class BlueDanubeFilmRun : IDisposable
{
    /// <summary>The last film of the run.</summary>
    public const string LastFilm = "take";

    public BlueDanubeFilmRun()
    {
        Script full = Script.Load(Path.Combine(RepositoryRoot(), "scripts", "blue-danube.json"));
        int last = full.Steps.ToList().FindIndex(step => step.Value is FilmStep { Name: LastFilm });
        if (last < 0)
            throw new InvalidOperationException($"scripts/blue-danube.json has no film '{LastFilm}'.");
        Backend = new(JoltBackendOptions.Unpaced);
        World = new(new() { MaxDegreeOfParallelism = 1, DebugChecks = true, DisposePhysics = true }, Backend);
        Scene = DemoScenes.All.Single(s => s.Name == full.Scene).Build(World, RenderResources.None);
        ScriptRunner.Run(script: full with { Steps = [.. full.Steps.Take(last + 1)] }, world: World, scene: Scene, outputFolder: null,
            playback: new Recorder(this));
    }

    public World World { get; }
    public SceneInstance Scene { get; }

    /// <summary>The world's physics backend, which the run asks for rays between ticks.</summary>
    private JoltPhysicsBackend Backend { get; }

    /// <summary>One <see cref="FilmFrame"/> for each frame of the run, in order.</summary>
    public List<FilmFrame> Frames { get; } = [];

    /// <summary>One <see cref="SkyFrame"/> for each frame of the film <c>sky</c>, in order.</summary>
    public List<SkyFrame> SkyFrames { get; } = [];

    /// <summary>
    /// What the camera of a frame of <c>sky</c> sees of the Sun, the Moon and the parked shuttle, in earth-surface, where
    /// <c>sky-camera</c> stands: the projected centers, and whether a ray from the camera toward the Sun's center, and one
    /// toward the Moon's, meets no body of earth-surface within 10 km.
    /// </summary>
    private SkyFrame ReadSky(string name)
    {
        FrameHandle surface = World.GetOwnedFrame(Scene["earth-surface"]);
        DPose view = World.PoseIn(World.Camera, surface);
        Double3 sun = World.PoseIn(Scene["sun"], surface).Position,
            moon = World.PoseIn(Scene["moon"], surface).Position;

        bool Clear(Double3 toward)
        {
            var hits = new List<RayCastHit>();
            Backend.CastRay(frame: surface, origin: view.Position, direction: (toward - view.Position).Normalized(), start: 0, end: 1e4,
                filter: new AcceptsEveryEntity(), kind: RayCastKind.Nearest, hits: hits);
            return hits.Count == 0;
        }

        return new(Name: name, Sun: Picture.Project(view, sun, World.VerticalFov), Moon: Picture.Project(view, moon, World.VerticalFov),
            Shuttle: Picture.Project(view, World.PoseIn(Scene["shuttle"], surface).Position, World.VerticalFov), SunClear: Clear(sun),
            MoonClear: Clear(moon));
    }

    /// <summary>A query filter that accepts every entity.</summary>
    private sealed class AcceptsEveryEntity : IPhysicsQueryFilter
    {
        public bool Accepts(Entity entity) => true;
    }

    private FilmFrame Read(string name)
    {
        Entity person = Scene["person"], pen = Scene["pen"];
        FrameHandle frame = World.GetFrame(person), cabin = World.GetOwnedFrame(Scene["shuttle"]);
        DPose view = World.PoseIn(World.Camera, frame), body = World.PoseIn(person, frame);
        bool penInPicture = World.IsAlive(pen) &&
                            Picture.Contains(
                                Picture.Project(view, World.PoseIn(pen, frame).Position, World.VerticalFov));
        DPose inCabin = World.PoseIn(person, cabin);
        return new(Name: name, PersonCover: Picture.Cover(view: view, body: body, half: DemoSurface.PersonHalf, verticalFov: World.VerticalFov), PenInPicture: penInPicture,
            View: World.PoseIn(World.Camera, cabin), Eye: inCabin.TransformPoint(World.GetRender(person).ViewOffset.Position),
            VerticalFov: World.VerticalFov);
    }

    public void Dispose() => World.Dispose();

    /// <summary>A playback that records what each frame's camera sees, and draws nothing.</summary>
    private sealed class Recorder(BlueDanubeFilmRun run) : IPlayback
    {
        public bool WaitForTick() => true;

        public bool Capture(World world, string name)
        {
            run.Frames.Add(run.Read(name));
            if (name.StartsWith("sky-", StringComparison.Ordinal))
                run.SkyFrames.Add(run.ReadSky(name));
            return true;
        }
    }
}

/// <summary>What the camera of one frame of the long script sees.</summary>
/// <param name="Name">The frame's name, such as <c>fetch-000012</c>.</param>
/// <param name="PersonCover">The share of the picture that the person's box covers, from 0 to 1 (see
/// <see cref="Picture.Cover"/>).</param>
/// <param name="PenInPicture">True when the loose pen is alive and its center lies in the picture.</param>
/// <param name="View">The camera's pose in the shuttle's frame.</param>
/// <param name="Eye">The person's eye in the shuttle's frame, in meters.</param>
/// <param name="VerticalFov">The world's vertical view angle, in radians.</param>
public sealed record FilmFrame(
    string Name,
    double PersonCover,
    bool PenInPicture,
    DPose View,
    Double3 Eye,
    double VerticalFov);

/// <summary>What the camera of one frame of the film <c>sky</c> sees.</summary>
/// <param name="Name">The frame's name, such as <c>sky-000012</c>.</param>
/// <param name="Sun">The Sun's center in the picture (see <see cref="Picture.Project"/>).</param>
/// <param name="Moon">The Moon's center in the picture.</param>
/// <param name="Shuttle">The parked shuttle's center in the picture.</param>
/// <param name="SunClear">True when a ray from the camera toward the Sun meets no body of earth-surface.</param>
/// <param name="MoonClear">True when a ray from the camera toward the Moon meets no body of earth-surface.</param>
public sealed record SkyFrame(string Name, Double3 Sun, Double3 Moon, Double3 Shuttle, bool SunClear, bool MoonClear);

/// <summary>The picture of a camera of the long script: 1280 by 720 pixels, with the world's vertical view angle.</summary>
public static class Picture
{
    /// <summary>
    /// Where <paramref name="point"/> lies in the picture of a camera at <paramref name="view"/>: right of and above the
    /// middle, as shares of the half width and the half height, so the picture spans -1 to 1 in both, and in Z the distance
    /// in front of the camera in meters.
    /// </summary>
    public static Double3 Project(DPose view, Double3 point, double verticalFov)
    {
        double tanY = Math.Tan(verticalFov / 2), tanX = tanY * BlueDanube.Window.Width / BlueDanube.Window.Height;
        Double3 seen = view.Rotation.Conjugate().Rotate(point - view.Position);
        return new(seen.X / -seen.Z / tanX, seen.Y / -seen.Z / tanY, -seen.Z);
    }

    /// <summary>True when the projected point lies in front of the renderer's near plane and inside the picture.</summary>
    public static bool Contains(Double3 projected) =>
        projected.Z > 0.05 && Math.Abs(projected.X) <= 1 && Math.Abs(projected.Y) <= 1;

    /// <summary>
    /// The share of the picture that the box with half extents <paramref name="half"/> around <paramref name="body"/>
    /// covers: the area of the rectangle round its 8 projected corners, cut to the picture, over the picture's area, or 1
    /// when a corner lies behind the renderer's near plane.
    /// </summary>
    public static double Cover(DPose view, DPose body, Double3 half, double verticalFov)
    {
        double left = double.PositiveInfinity,
            right = double.NegativeInfinity,
            bottom = double.PositiveInfinity,
            top = double.NegativeInfinity;
        for (int corner = 0; corner < 8; corner++)
        {
            var local = new Double3((corner & 1) == 0 ? -half.X : half.X, (corner & 2) == 0 ? -half.Y : half.Y,
                (corner & 4) == 0 ? -half.Z : half.Z);
            Double3 at = Project(view, body.TransformPoint(local), verticalFov);
            if (at.Z <= 0.05)
                return 1;
            (left, right, bottom, top) = (Math.Min(left, at.X), Math.Max(right, at.X), Math.Min(bottom, at.Y),
                Math.Max(top, at.Y));
        }

        double width = Math.Max(Math.Min(right, 1) - Math.Max(left, -1), 0),
            height = Math.Max(Math.Min(top, 1) - Math.Max(bottom, -1), 0);
        return width * height / 4;
    }
}

public sealed class BlueDanubeFilmTests(BlueDanubeFilmRun run) : IClassFixture<BlueDanubeFilmRun>
{
    /// <summary>
    /// The film <c>sky</c>, from <c>sky-camera</c> ahead of the parked nose, shows the low Sun and the crescent Moon over
    /// the parked shuttle (docs/plans/blue-danube-demo.md, "The Sun and the Moon in the films"): in every frame of it, the
    /// centers of the Sun, the Moon and the shuttle lie in the picture, and a ray from the camera toward the Sun, and one
    /// toward the Moon, meets no body of earth-surface.
    /// </summary>
    [Fact]
    public void TheSkyCameraShowsTheSunTheMoonAndTheShuttle()
    {
        run.SkyFrames.Should().HaveCount(60, "the film sky lasts 1 s");
        foreach (SkyFrame frame in run.SkyFrames)
        {
            Picture.Contains(frame.Sun).Should().BeTrue($"the Sun is in the picture of {frame.Name}, at {frame.Sun}");
            Picture.Contains(frame.Moon).Should()
                .BeTrue($"the Moon is in the picture of {frame.Name}, at {frame.Moon}");
            Picture.Contains(frame.Shuttle).Should()
                .BeTrue($"the shuttle is in the picture of {frame.Name}, at {frame.Shuttle}");
            frame.SunClear.Should().BeTrue($"nothing on the ground hides the Sun in {frame.Name}");
            frame.MoonClear.Should().BeTrue($"nothing on the ground hides the Moon in {frame.Name}");
        }
    }

    /// <summary>
    /// The film <c>fetch</c>, from <c>pen-camera</c> near the cabin's roof, shows the person floating to the loose pen: in
    /// every frame of it the person's box covers at most half of the picture, and the pen is in the picture. The person
    /// floats toward the camera on its way to the pen, so the film hands over to <c>hand-camera</c> before the person
    /// fills the lens.
    /// </summary>
    [Fact]
    public void TheFetchFilmShowsThePenAndThePersonNeverFillsThePicture()
    {
        List<FilmFrame> fetch =
            [.. run.Frames.Where(frame => frame.Name.StartsWith("fetch-", StringComparison.Ordinal))];

        fetch.Should().NotBeEmpty();
        FilmFrame worst = fetch.MaxBy(frame => frame.PersonCover)!;
        worst.PersonCover.Should().BeLessThanOrEqualTo(0.5,
            $"in {worst.Name} the person covers {worst.PersonCover:P0} of the picture");
        fetch.Should().OnlyContain(frame => frame.PenInPicture,
            "the pen is in the picture in every frame of the fetch");
    }

    /// <summary>
    /// The film <c>seated-outside</c>, from <c>apron-camera</c> on the ground, shows the person at its seat through the
    /// starboard glass: in every frame of it the person's eye lies in the picture, and the line from the camera to it
    /// crosses both skins inside the holes of one pane.
    /// </summary>
    [Fact]
    public void TheApronCameraSeesThePersonThroughTheGlassInSeatedOutside()
    {
        List<FilmFrame> seated =
            [.. run.Frames.Where(frame => frame.Name.StartsWith("seated-outside-", StringComparison.Ordinal))];

        seated.Should().NotBeEmpty();
        foreach (FilmFrame frame in seated)
        {
            Picture.Contains(Picture.Project(frame.View, frame.Eye, frame.VerticalFov)).Should()
                .BeTrue($"the person's eye is in the picture of {frame.Name}");
            Shuttle.Glass.Should().Contain(pane => PaneChecks.Through(pane, frame.View.Position, frame.Eye),
                $"the camera sees the person's eye through the glass in {frame.Name}");
        }
    }
}
