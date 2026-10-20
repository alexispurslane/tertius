using System.Numerics;
using Tertius.Demo;
using Tertius.Simulation;
using Tertius.Testing;
using static Tertius.Testing.WorldTesting;
using Shuttle = Tertius.Demo.DemoScenes.Shuttle;

namespace Tertius.Host.Tests;

/// <summary>
/// The parked shuttle of the demo (<see cref="Shuttle"/>), on the World and the real Jolt backend. It stands at Earth's
/// north pole in a root frame with Earth's point gravity, unturned, so its +Y is up.
/// </summary>
public sealed class ShuttleTests : IDisposable
{
    private readonly World world = HostTesting.NewJoltWorld();

    public void Dispose() => world.Dispose();

    /// <summary>
    /// A ball is let go 1.5 m above the cabin floor. The shuttle's frame has no gravity field of its own, so the ball
    /// feels only the opposite of the frame's proper acceleration, which the engine derives from the parked hull's rest
    /// (ARCHITECTURE.md, "How a frame moves"). Over 15 ticks it gains the velocity of a fall with the gravity at the
    /// shuttle's place, G·M / r², about 9.82 m/s² down. A new frame reads no proper acceleration during its first step,
    /// so the measure starts after two ticks.
    /// </summary>
    [Fact]
    public void ABallInTheParkedCabinFallsWithThePlanetsGravity()
    {
        FrameHandle earth = world.CreateRootFrame(new FrameSpec(FrameBounds.Sphere(1e7f), GravityField.Point(DemoScenes.EarthMu)));
        double r = DemoScenes.EarthRadius + Shuttle.Hull.Y;
        FrameHandle cabin = world.GetOwnedFrame(Shuttle.Park(world, earth, new DPose(new Double3(0, r, 0), DQuaternion.Identity)));
        Entity ball = world.Spawn(new SpawnDesc { Frame = cabin, Pose = At(0, -Shuttle.Cabin.Y + 1.5), Body = Ball(0.1f) });
        Tick(world, 2);
        Double3 before = world.VelocityIn(ball, cabin);

        const int ticks = 15;
        Tick(world, ticks);
        Double3 acceleration = (world.VelocityIn(ball, cabin) - before) / (ticks * world.DeltaTime);

        double g = DemoScenes.EarthMu / (r * r);
        acceleration.Y.Should().BeApproximately(-g, 0.01, "the cabin's contents have the weight of the ground");
        Math.Abs(acceleration.X).Should().BeLessThan(0.01);
        Math.Abs(acceleration.Z).Should().BeLessThan(0.01);
    }

    /// <summary>
    /// The shuttle's windows, as docs/plans/blue-danube-demo.md ("The shuttle's windows and the cameras") gives them, in the
    /// shuttle's axes: the wall that each goes through, as the axis across it and the wall's two faces along that axis, and
    /// the window's middle and half size in the other two axes. Eye height is 1.65 m above the cabin floor, at y = -0.15 m.
    /// The nose window is 4 m wide and 1.4 m tall. The side windows are 1.2 m wide and 0.8 m tall, three in each side wall,
    /// at z = -5.5, -3 and 4.5 m.
    /// </summary>
    private static readonly (int Axis, float Inner, float Outer, Vector2 Middle, Vector2 Half)[] ExpectedWindows =
    [
        (2, -7.8f, -8, new Vector2(0, -0.15f), new Vector2(2, 0.7f)),
        .. new[] { -5.5f, -3, 4.5f }.SelectMany(z => new[]
        {
            (0, -2.8f, -3f, new Vector2(-0.15f, z), new Vector2(0.4f, 0.6f)),
            (0, 2.8f, 3f, new Vector2(-0.15f, z), new Vector2(0.4f, 0.6f)),
        }),
    ];

    /// <summary>
    /// The place in the shuttle's axes at <paramref name="across"/> along the axis <paramref name="axis"/> (0 for X, 2 for
    /// Z), and at <paramref name="other"/> in the two other axes: (Y, Z) for a wall across X, and (X, Y) for a wall across Z.
    /// </summary>
    private static Vector3 Place(int axis, float across, Vector2 other) =>
        axis == 0 ? new Vector3(across, other.X, other.Y) : new Vector3(other.X, other.Y, across);

    /// <summary>How many triangles of <paramref name="mesh"/> the segment from <paramref name="from"/> to <paramref name="to"/> crosses.</summary>
    private static int Crossings(MeshData mesh, Vector3 from, Vector3 to)
    {
        int count = 0;
        Vector3 along = to - from;
        for (int i = 0; i < mesh.Indices.Length; i += 3)
        {
            Vector3 a = mesh.Vertices[mesh.Indices[i]].Position, b = mesh.Vertices[mesh.Indices[i + 1]].Position, c = mesh.Vertices[mesh.Indices[i + 2]].Position;
            // Möller and Trumbore: the segment's parameter t and the triangle's barycentric u and v of the crossing.
            Vector3 ab = b - a, ac = c - a, p = Vector3.Cross(along, ac);
            float det = Vector3.Dot(ab, p);
            if (MathF.Abs(det) < 1e-9f)
                continue;
            Vector3 s = from - a;
            float u = Vector3.Dot(s, p) / det;
            Vector3 q = Vector3.Cross(s, ab);
            float v = Vector3.Dot(along, q) / det, t = Vector3.Dot(ac, q) / det;
            if (u >= 0 && v >= 0 && u + v <= 1 && t >= 0 && t <= 1)
                count++;
        }
        return count;
    }

    /// <summary>
    /// The drawn hull has a hole at each window: a segment across the wall, from 0.5 m inside the cabin to 0.5 m outside
    /// the hull, crosses no triangle at the window's middle or 5 cm inside each of its corners, and crosses the wall's two
    /// faces 5 cm outside each of its four edges. Physics keeps the walls solid there, as in the orbit scene's ship: a crate
    /// thrown at 5 m/s from 1 m inside each window toward its middle stays in the cabin. Through a hole it would be 1.5 m
    /// past the hull after 0.5 s.
    /// </summary>
    [Fact]
    public void TheWindowsAreHolesInTheDrawnHullAndTheInteriorStaysSolid()
    {
        MeshData hull = Shuttle.HullMesh();
        foreach ((int axis, float inner, float outer, Vector2 middle, Vector2 half) in ExpectedWindows)
        {
            float into = MathF.Sign(outer - inner);
            Vector3 Across(Vector2 at) => Place(axis, inner - 0.5f * into, at);
            Vector3 Beyond(Vector2 at) => Place(axis, outer + 0.5f * into, at);
            Vector2 inset = half - new Vector2(0.05f);
            foreach (Vector2 at in new[] { middle, middle + inset, middle - inset, middle + new Vector2(inset.X, -inset.Y), middle + new Vector2(-inset.X, inset.Y) })
                Crossings(hull, Across(at), Beyond(at)).Should().Be(0, $"the window at {Place(axis, outer, middle)} is open at {at}");
            Vector2 past = half + new Vector2(0.05f);
            foreach (Vector2 at in new[] { middle + new Vector2(past.X, 0), middle - new Vector2(past.X, 0), middle + new Vector2(0, past.Y), middle - new Vector2(0, past.Y) })
                Crossings(hull, Across(at), Beyond(at)).Should().Be(2, $"the wall round the window at {Place(axis, outer, middle)} is closed at {at}");
        }

        FrameHandle space = world.CreateRootFrame(new FrameSpec(FrameBounds.Sphere(1e4f)));
        FrameHandle cabin = world.GetOwnedFrame(Shuttle.Park(world, space, DPose.Identity));
        Entity[] crates =
        [
            .. ExpectedWindows.Select(window => world.Spawn(new SpawnDesc
            {
                Frame = cabin,
                Pose = new DPose(Place(window.Axis, window.Inner - MathF.Sign(window.Outer - window.Inner), window.Middle), DQuaternion.Identity),
                LinearVelocity = Place(window.Axis, 5 * MathF.Sign(window.Outer - window.Inner), Vector2.Zero),
                Body = new BodyDesc(new BoxShapeDesc(new Vector3(0.15f)), Mass: 5, LinearDamping: 0, AngularDamping: 0),
            })),
        ];
        Tick(world, 30);

        foreach (Entity crate in crates)
        {
            world.GetFrame(crate).Should().Be(cabin);
            Double3 place = world.PoseIn(crate, cabin).Position;
            (Math.Abs(place.X) < Shuttle.Cabin.X && Math.Abs(place.Z) < Shuttle.Cabin.Z).Should().BeTrue($"the glass, like the wall, keeps the crate in the cabin, but it is at {place}");
        }
    }

    /// <summary>
    /// Each window of the shuttle in the scene <c>demo-surface</c> holds a pane of glass, <c>pane-nose</c> and
    /// <c>pane-port-1</c> to <c>pane-port-3</c> and <c>pane-starboard-1</c> to <c>pane-starboard-3</c>, numbered from the nose:
    /// a thin box in the middle of its wall, in the shuttle's frame, transparent, and it casts no shadow, as the orbit scene's
    /// panes are.
    /// </summary>
    [Fact]
    public void EachWindowHoldsAPaneOfGlassInTheMiddleOfItsWallThatCastsNoShadow()
    {
        SceneInstance scene = DemoScenes.All.Single(s => s.Name == "demo-surface").Build(world, RenderResources.None);
        FrameHandle cabin = world.GetOwnedFrame(scene["shuttle"]);

        string[] names = ["pane-nose", "pane-port-1", "pane-starboard-1", "pane-port-2", "pane-starboard-2", "pane-port-3", "pane-starboard-3"];
        for (int i = 0; i < names.Length; i++)
        {
            (int axis, float inner, float outer, Vector2 middle, _) = ExpectedWindows[i];
            Entity pane = scene[names[i]];
            world.GetFrame(pane).Should().Be(cabin);
            Double3 place = world.PoseIn(pane, cabin).Position;
            Vector3 expected = Place(axis, (inner + outer) / 2, middle);
            (place - (Double3)expected).Length.Should().BeLessThan(1e-4, $"{names[i]} stands in its window at {expected}, not at {place}");
            world.GetRender(pane).Flags.Should().Be(RenderFlags.Visible | RenderFlags.Transparent, names[i]);
        }
    }
}
