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
                MeshChecks.Crossings(hull, Across(at), Beyond(at)).Should().Be(0, $"the window at {Place(axis, outer, middle)} is open at {at}");
            Vector2 past = half + new Vector2(0.05f);
            foreach (Vector2 at in new[] { middle + new Vector2(past.X, 0), middle - new Vector2(past.X, 0), middle + new Vector2(0, past.Y), middle - new Vector2(0, past.Y) })
                MeshChecks.Crossings(hull, Across(at), Beyond(at)).Should().Be(2, $"the wall round the window at {Place(axis, outer, middle)} is closed at {at}");
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

    /// <summary>
    /// Spawns the free, dynamic hull at the origin of <paramref name="space"/>, unturned, turning at
    /// <paramref name="angular"/>, with the linear velocity at its origin that keeps the point at
    /// <see cref="Shuttle.CenterOfMass"/> still: the turn's velocity at the origin about that point.
    /// </summary>
    private Entity FreeHull(FrameHandle space, Vector3 angular) => world.Spawn(new SpawnDesc
    {
        Frame = space,
        Pose = DPose.Identity,
        LinearVelocity = Double3.Cross(angular, -(Double3)Shuttle.CenterOfMass),
        AngularVelocity = angular,
        Body = Shuttle.Body(BodyMotion.Dynamic),
        AsFrame = new FrameSpec(new FrameBounds(Shuttle.Bubble)),
    });

    /// <summary>
    /// The free hull spins in a root frame with no gravity, about X at 0.5 rad/s and, in another root frame, about its long
    /// axis at 0.3 rad/s. Physics turns a free body about its center of mass, so after 2 s the point at
    /// <see cref="Shuttle.CenterOfMass"/> is where it started, within 1 mm. A center of mass off by d moves that point by
    /// about d in that time, from the turn and from the drift that the spawn velocity gives the true center.
    /// </summary>
    [Fact]
    public void TheCenterOfMassStaysStillWhileTheFreeHullSpins()
    {
        Vector3 longAxis = Vector3.Transform(Vector3.UnitZ, Shuttle.Inertia.Axes);
        (FrameHandle Space, Entity Hull, string Spin)[] cases =
        [
            .. new[] { (Spin: 0.5f * Vector3.UnitX, Name: "about X"), (Spin: 0.3f * longAxis, Name: "about the long axis") }.Select(spin =>
            {
                FrameHandle space = world.CreateRootFrame(new FrameSpec(FrameBounds.Sphere(1e4f)));
                return (space, FreeHull(space, spin.Spin), spin.Name);
            }),
        ];

        Tick(world, 120);

        foreach ((FrameHandle space, Entity hull, string spin) in cases)
        {
            DPose pose = world.PoseIn(hull, space);
            Double3 center = pose.Position + pose.Rotation.Rotate((Double3)Shuttle.CenterOfMass);
            (center - (Double3)Shuttle.CenterOfMass).Length.Should().BeLessThan(1e-3, $"the center of mass stays still while the hull spins {spin}");
            pose.Rotation.AngularVelocityTo(DQuaternion.Identity, 1).Length.Should().BeGreaterThan(0.5, $"the hull has turned {spin}");
        }
    }

    /// <summary>
    /// A torque of 10,000 N·m along each principal axis of <see cref="Shuttle.Inertia"/> acts on the hull at rest for 30
    /// ticks, in a root frame with no gravity and no damping. Its angular velocity along that axis is then the torque times
    /// the time over the moment, within 0.1 %. Across the axis it is less than 0.1 % of that, so the axis is principal.
    /// </summary>
    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    public void ATorqueAlongEachPrincipalAxisTurnsTheHullAboutThatAxisAtItsMoment(int principal)
    {
        const float torque = 10_000;
        const int ticks = 30;
        Vector3 axis = Vector3.Transform(principal switch { 0 => Vector3.UnitX, 1 => Vector3.UnitY, _ => Vector3.UnitZ }, Shuttle.Inertia.Axes);
        float moment = principal switch { 0 => Shuttle.Inertia.Moments.X, 1 => Shuttle.Inertia.Moments.Y, _ => Shuttle.Inertia.Moments.Z };
        FrameHandle space = world.CreateRootFrame(new FrameSpec(FrameBounds.Sphere(1e4f)));
        Entity hull = world.Spawn(new SpawnDesc
        {
            Frame = space,
            Pose = DPose.Identity,
            Body = Shuttle.Body(BodyMotion.Dynamic),
            AsFrame = new FrameSpec(new FrameBounds(Shuttle.Bubble)),
            Components = [new TorqueFor(ticks, torque * axis)],
        });

        Tick(world, ticks + 5);

        Vector3 turning = world.AngularVelocityIn(hull, space);
        float expected = torque * ticks * (float)world.DeltaTime / moment;
        float along = Vector3.Dot(turning, axis);
        along.Should().BeApproximately(expected, expected * 1e-3f, $"the moment about principal axis {principal} is {moment} kg·m²");
        (turning - along * axis).Length().Should().BeLessThan(expected * 1e-3f, $"the hull turns about principal axis {principal} alone");
    }

    /// <summary>
    /// The live world lands the shuttle on its tail (docs/plans/live-world.md, part 3). A dynamic hull stands nose up, its
    /// roof toward +Z, 1 cm above a static pad, in a root frame with 1 g. It settles on its tail face and stays upright,
    /// its nose within 1 degree of straight up, in every tick for 60 s.
    /// </summary>
    [Fact]
    public void TheHullStandsOnItsTailOnAFlatPad()
    {
        FrameHandle ground = world.CreateRootFrame(new FrameSpec(FrameBounds.Sphere(1e4f), GravityField.Uniform(new Vector3(0, -9.81f, 0))));
        world.Spawn(new SpawnDesc { Frame = ground, Pose = At(0, 0.6), Body = new BodyDesc(new BoxShapeDesc(new Vector3(8, 0.6f, 8)), BodyMotion.Static) });
        Entity hull = world.Spawn(new SpawnDesc
        {
            Frame = ground,
            Pose = new DPose(new Double3(0, 1.2 + Shuttle.Hull.Z + 0.01, 0), DQuaternion.FromAxisAngle(Double3.UnitX, Math.PI / 2)),
            Body = Shuttle.Body(BodyMotion.Dynamic),
            AsFrame = new FrameSpec(new FrameBounds(Shuttle.Bubble)),
        });

        double worst = 0;
        for (int tick = 0; tick < 3600; tick++)
        {
            world.Tick();
            Double3 nose = world.PoseIn(hull, ground).Rotation.Rotate(-Double3.UnitZ);
            worst = Math.Max(worst, Math.Acos(Math.Clamp(nose.Y, -1, 1)) * 180 / Math.PI);
        }

        worst.Should().BeLessThan(1, "the hull stands on its tail");
        world.PoseIn(hull, ground).Position.Y.Should().BeApproximately(1.2 + Shuttle.Hull.Z, 0.01, "it rests on its tail face");
    }

    /// <summary>
    /// The tail face is flat across the whole cross-section, as the live world needs to stand the hull on its tail: the
    /// triangles of the drawn hull at its aft end lie at z = 8 and face aft, their area is the full cross-section's less the
    /// pocket's mouth, 1.4 m square, and their outline holds every corner of the full cross-section. The outer collision
    /// shape's points at z = 8 are its eight true corners, so the hull stands on the whole face.
    /// </summary>
    [Fact]
    public void TheTailFaceIsFlatAcrossTheCrossSection()
    {
        Vector2[] full = [new(-2.8f, -2), new(2.8f, -2), new(3, -1.8f), new(3, -1.2f), new(3, 1.2f), new(2.2f, 2), new(-2.2f, 2), new(-3, 1.2f), new(-3, -1.2f), new(-3, -1.8f)];
        double fullArea = 0;
        for (int i = 0; i < full.Length; i++)
            fullArea += (full[i].X * full[(i + 1) % full.Length].Y - full[(i + 1) % full.Length].X * full[i].Y) / 2.0;
        MeshData mesh = Shuttle.Loft.Mesh();

        double area = 0;
        var outline = new HashSet<Vector2>();
        for (int i = 0; i < mesh.Indices.Length; i += 3)
        {
            Vector3 a = mesh.Vertices[mesh.Indices[i]].Position, b = mesh.Vertices[mesh.Indices[i + 1]].Position, c = mesh.Vertices[mesh.Indices[i + 2]].Position;
            if ((a.Z + b.Z + c.Z) / 3 < 7.99f)
                continue;
            foreach (Vector3 corner in new[] { a, b, c })
            {
                corner.Z.Should().Be(8, "the tail face is flat");
                outline.Add(new Vector2(corner.X, corner.Y));
            }
            Vector3 wound = Vector3.Cross(b - a, c - a);
            Vector3.Normalize(wound).Z.Should().BeApproximately(1, 1e-6f, "the tail face faces aft");
            area += wound.Length() / 2;
        }

        area.Should().BeApproximately(fullArea - 1.4 * 1.4, 1e-4, "the face spans the full cross-section round the pocket");
        outline.Should().Contain(full);
        Shuttle.HullShape.Should().BeOfType<ConvexHullShapeDesc>().Which.Points.Where(p => p.Z == 8).Select(p => new Vector2(p.X, p.Y))
            .Should().BeEquivalentTo(new[] { 0, 1, 2, 4, 5, 6, 7, 9 }.Select(c => full[c]));
    }

    /// <summary>
    /// In the scene <c>demo-shuttle</c>, each shuttle carries the main engine's nozzle in its frame: its axis lies along Z
    /// through the x and y of the center of mass, where the engine pushes, and the pocket's middle, (0, -0.090). Its aft rim
    /// lies 5 cm inside the tail face, at z = 7.95, its front at 7.45, and its outside 0.1 m from the walls of the pocket,
    /// which reach 0.7 m from its middle.
    /// </summary>
    [Fact]
    public void TheNozzleIsRecessedOnTheLineOfThrust()
    {
        SceneInstance scene = DemoScenes.All.Single(s => s.Name == "demo-shuttle").Build(world, RenderResources.None);
        MeshData mesh = Shuttle.Nozzle.Mesh();
        foreach (string shuttle in new[] { "standing", "lying" })
        {
            FrameHandle cabin = world.GetOwnedFrame(scene[shuttle]);
            Entity nozzle = scene[$"{shuttle}-nozzle"];
            world.GetFrame(nozzle).Should().Be(cabin, $"the nozzle of {shuttle} lies in its frame");
            DPose pose = world.PoseIn(nozzle, cabin);
            Double3 axis = pose.Rotation.Rotate(Double3.UnitY);
            Math.Abs(axis.Z).Should().BeApproximately(1, 1e-9, "the nozzle's axis lies along Z");
            pose.Position.X.Should().BeApproximately(Shuttle.CenterOfMass.X, 1e-6, "the axis passes through the center of mass");
            pose.Position.Y.Should().BeApproximately(Shuttle.CenterOfMass.Y, 1e-6, "the axis passes through the center of mass");
            pose.Position.Y.Should().BeApproximately(-0.090, 1e-3, "the axis passes through the pocket's middle");

            Double3[] points = [.. mesh.Vertices.Select(vertex => pose.Position + pose.Rotation.Rotate((Double3)vertex.Position))];
            points.Max(p => p.Z).Should().BeApproximately(7.95, 1e-5, "the aft rim lies 5 cm inside the tail face");
            points.Min(p => p.Z).Should().BeApproximately(7.45, 1e-5);
            points.Min(p => 0.7 - Math.Max(Math.Abs(p.X), Math.Abs(p.Y - pose.Position.Y))).Should().BeApproximately(0.1, 1e-5, "the outside lies 0.1 m from the pocket's walls");
        }
    }

    /// <summary>A test component that adds <paramref name="torque"/>, in the axes of its entity's frame, on each of its first <paramref name="ticks"/> ticks.</summary>
    private sealed class TorqueFor(int ticks, Vector3 torque) : IComponent
    {
        private int handled;

        public int Priority => 0;

        public EventMask Wants => EventMask.Of<Tick>();

        public void Handle(Self self, scoped ref EventList events, scoped ref Outbox outbox)
        {
            if (handled++ < ticks)
                self.Body.AddTorque(torque);
        }
    }
}
