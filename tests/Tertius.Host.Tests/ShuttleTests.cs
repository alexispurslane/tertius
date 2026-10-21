using System.Numerics;
using Tertius.Demo;
using Tertius.Simulation;
using Tertius.Testing;
using static Tertius.Testing.WorldTesting;
using DemoSurface = Tertius.Demo.DemoScenes.DemoSurface;
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
    /// Physics keeps the glass solid, as in the orbit scene's ship: the interior shape is the drawn hull's solid with the
    /// glass in it. A crate thrown at 5 m/s from 1 m inside the middle of each pane toward it stays in the cabin, on the
    /// inner side of that pane's hole. Through a hole it would be 1.5 m past the glass after 0.5 s.
    /// </summary>
    [Fact]
    public void TheInteriorStaysSolidAtTheGlass()
    {
        FrameHandle space = world.CreateRootFrame(new FrameSpec(FrameBounds.Sphere(1e4f)));
        FrameHandle cabin = world.GetOwnedFrame(Shuttle.Park(world, space, DPose.Identity));
        Shuttle.Glass.Should().HaveCount(40);
        (HullPane Pane, Entity Crate)[] crates =
        [
            .. Shuttle.Glass.Select(pane => (pane, world.Spawn(new SpawnDesc
            {
                Frame = cabin,
                Pose = new DPose((Double3)pane.Center - PaneChecks.Normal(pane), DQuaternion.Identity),
                LinearVelocity = PaneChecks.Normal(pane) * 5,
                Body = new BodyDesc(new BoxShapeDesc(new Vector3(0.15f)), Mass: 5, LinearDamping: 0, AngularDamping: 0),
            }))),
        ];
        Tick(world, 30);

        foreach ((HullPane pane, Entity crate) in crates)
        {
            world.GetFrame(crate).Should().Be(cabin);
            Double3 place = world.PoseIn(crate, cabin).Position;
            Double3.Dot(PaneChecks.Normal(pane), place - pane.Inner[0]).Should().BeLessThan(0, $"the glass of {pane.Id}, like the wall, keeps the crate in the cabin, but it is at {place}");
        }
    }

    /// <summary>
    /// In the scene <c>demo-surface</c>, each of the 40 panes of <see cref="Shuttle.Glass"/> is an entity with the pane's
    /// scene id, in the shuttle's frame at the pane's center, unturned. It is transparent, it casts no shadow, as the orbit
    /// scene's panes are, and it has no body, because the interior shape is solid at the glass.
    /// </summary>
    [Fact]
    public void EachPaneIsGlassThatCastsNoShadow()
    {
        SceneInstance scene = DemoScenes.All.Single(s => s.Name == "demo-surface").Build(world, RenderResources.None);
        FrameHandle cabin = world.GetOwnedFrame(scene["shuttle"]);

        Shuttle.Glass.Should().HaveCount(40);
        foreach (HullPane pane in Shuttle.Glass)
        {
            Entity entity = scene[pane.Id];
            world.GetFrame(entity).Should().Be(cabin, pane.Id);
            DPose pose = world.PoseIn(entity, cabin);
            (pose.Position - (Double3)pane.Center).Length.Should().BeLessThan(1e-4, $"{pane.Id} stands at its center");
            Math.Abs(pose.Rotation.W).Should().BeGreaterThan(1 - 1e-9, $"{pane.Id} is unturned, so its mesh lies in its hole");
            world.GetRender(entity).Flags.Should().Be(RenderFlags.Visible | RenderFlags.Transparent, pane.Id);
            world.GetBody(entity).Should().BeNull(pane.Id);
        }
    }

    /// <summary>
    /// The drawn hull of the shuttle in <c>demo-surface</c> is never an impostor: a flat picture of the hull alone cannot hold
    /// the person inside at its depth.
    /// </summary>
    [Fact]
    public void TheDrawnHullIsNeverAnImpostor()
    {
        SceneInstance scene = DemoScenes.All.Single(s => s.Name == "demo-surface").Build(world, RenderResources.None);

        world.GetRender(scene["shuttle"]).Flags.Should().Be(RenderFlags.Visible | RenderFlags.CastsShadow | RenderFlags.NoImpostor);
    }

    /// <summary>
    /// The clip holds the pen on the pillar at z = -1 m of the port wall: the pen's ends lie between the holes of
    /// <c>pane-port-4</c> and <c>pane-port-5</c>, at both skins, and the line from each end and from the middle of the pen
    /// to the port wall's outer face crosses no pane's hole.
    /// </summary>
    [Fact]
    public void ThePenHangsOnThePortPillar()
    {
        Double3 pen = DemoSurface.PenClip.Position;
        double half = DemoSurface.PenHalf.Z;
        HullPane fore = Shuttle.Glass.Single(pane => pane.Id == "pane-port-4"), aft = Shuttle.Glass.Single(pane => pane.Id == "pane-port-5");
        double pillarStart = fore.Outer.Concat(fore.Inner).Max(corner => corner.Z), pillarEnd = aft.Outer.Concat(aft.Inner).Min(corner => corner.Z);

        (pen.Z - half).Should().BeGreaterThan(pillarStart, "the pen's forward end lies on the pillar");
        (pen.Z + half).Should().BeLessThan(pillarEnd, "the pen's aft end lies on the pillar");
        foreach (double z in new[] { pen.Z - half, pen.Z, pen.Z + half })
        {
            Double3 start = pen with { Z = z }, end = start with { X = -Shuttle.Hull.X - 0.1 };
            Shuttle.Glass.Should().NotContain(pane => PaneChecks.CrossesTheHole(pane.Outer, start, end) || PaneChecks.CrossesTheHole(pane.Inner, start, end),
                $"the wall behind the pen at z = {z} is solid");
        }
    }

    /// <summary>
    /// Four lamps hang on the inner faces of the pillars at z = -3 m and 3 m, on both walls, 2.4 m above the floor: each
    /// a glowing panel 0.05 m thick, 0.5 m tall and 0.15 m wide, within its pillar's 0.2 m, so that the pillar hides it
    /// from outside. The line from the panel's middle out through the wall crosses no pane's hole. In <c>demo-surface</c>
    /// each is an entity with its id in the shuttle's frame, with a point light 0.3 m in front of it, toward the cabin's
    /// middle, of range 10 m and intensity 50.
    /// </summary>
    [Fact]
    public void TheLampsHangOnPillarsClearOfTheGlass()
    {
        SceneInstance scene = DemoScenes.All.Single(s => s.Name == "demo-surface").Build(world, RenderResources.None);
        FrameHandle cabin = world.GetOwnedFrame(scene["shuttle"]);

        Shuttle.Lamps.Should().HaveCount(4);
        Shuttle.Lamps.Select(lamp => (Math.Sign(lamp.Panel.Center.X), lamp.Panel.Center.Z)).Should().BeEquivalentTo(new[] { (-1, -3f), (-1, 3f), (1, -3f), (1, 3f) });
        foreach ((string id, BoundingBox panel) in Shuttle.Lamps)
        {
            float side = MathF.Sign(panel.Center.X), rib = panel.Center.Z;
            (panel.Max - panel.Min - new Vector3(0.05f, 0.5f, 0.15f)).Length().Should().BeLessThan(1e-5f, $"{id} is 0.05 m thick, 0.5 m tall and 0.15 m wide");
            (side > 0 ? panel.Max.X : -panel.Min.X).Should().BeApproximately(Shuttle.Cabin.X, 1e-5f, $"{id} hangs on the wall's inner face");
            panel.Center.Y.Should().BeApproximately(0.6f, 1e-5f, $"{id} hangs 2.4 m above the floor");
            panel.Min.Z.Should().BeGreaterThanOrEqualTo(rib - 0.1f, $"{id} lies within its pillar");
            panel.Max.Z.Should().BeLessThanOrEqualTo(rib + 0.1f, $"{id} lies within its pillar");
            Double3 middle = panel.Center, outside = middle with { X = side * (Shuttle.Hull.X + 0.1) };
            Shuttle.Glass.Should().NotContain(pane => PaneChecks.CrossesTheHole(pane.Outer, middle, outside) || PaneChecks.CrossesTheHole(pane.Inner, middle, outside),
                $"the pillar hides {id} from outside");

            Entity lamp = scene[id];
            world.GetFrame(lamp).Should().Be(cabin);
            DPose pose = world.PoseIn(lamp, cabin);
            (pose.Position - (Double3)panel.Center).Length.Should().BeLessThan(1e-5, id);
            PointLightData light = world.GetRender(lamp).Light is PointLightData point ? point : default;
            light.Range.Should().Be(10, $"{id} carries a point light of range 10 m");
            light.Intensity.Should().Be(50, id);
            ((Double3)light.Offset.Position - new Double3(-side * 0.3, 0, 0)).Length.Should().BeLessThan(1e-6, $"the light of {id} lies 0.3 m in front of it");
        }
    }

    /// <summary>
    /// Each still camera inside the shuttle in <c>demo-surface</c> lies at least 0.2 m inside the inner skin: inside the
    /// cross-section moved in by the wall and 0.2 m more, and 0.2 m from the end walls and the nozzle's housing.
    /// </summary>
    [Theory]
    [InlineData("cabin-camera")]
    [InlineData("pen-camera")]
    [InlineData("hand-camera")]
    [InlineData("window-camera")]
    [InlineData("port-window-camera")]
    [InlineData("cockpit-camera")]
    public void EachCameraInTheShuttleLiesInsideTheCabin(string camera)
    {
        SceneInstance scene = DemoScenes.All.Single(s => s.Name == "demo-surface").Build(world, RenderResources.None);
        FrameHandle cabin = world.GetOwnedFrame(scene["shuttle"]);

        world.GetFrame(scene[camera]).Should().Be(cabin);
        Double3 p = world.PoseIn(scene[camera], cabin).Position;
        LoftedHullTests.Inside(LoftedHullTests.OuterAt(p.Z), p.X, p.Y).Should().BeGreaterThanOrEqualTo(Shuttle.Wall + 0.2, $"{camera} at {p} lies 0.2 m inside the walls");
        Math.Abs(p.Z).Should().BeLessThanOrEqualTo(Shuttle.Cabin.Z - 0.2, $"{camera} at {p} lies 0.2 m inside the end walls");
        bool byTheHousing = Math.Abs(p.X) < 0.9 + 0.2 && Math.Abs(p.Y - Shuttle.CenterOfMass.Y) < 0.9 + 0.2 && p.Z > 7.2 - 0.2;
        byTheHousing.Should().BeFalse($"{camera} at {p} lies 0.2 m from the nozzle's housing");
    }

    /// <summary>
    /// A person stands at <see cref="Shuttle.SeatStand"/>. The line to its eye from <c>chase-camera</c> crosses both skins
    /// inside the holes of <c>pane-starboard-5</c>, from <c>climb-camera</c> inside those of <c>pane-roof-4</c>, and from
    /// <c>apron-camera</c>, on the ground, inside those of <c>pane-starboard-4</c>.
    /// </summary>
    [Theory]
    [InlineData("chase-camera", "pane-starboard-5")]
    [InlineData("climb-camera", "pane-roof-4")]
    [InlineData("apron-camera", "pane-starboard-4")]
    public void TheOutsideCamerasSeeTheSeatedPersonThroughTheGlass(string camera, string pane)
    {
        SceneInstance scene = DemoScenes.All.Single(s => s.Name == "demo-surface").Build(world, RenderResources.None);
        FrameHandle cabin = world.GetOwnedFrame(scene["shuttle"]);
        Double3 eye = Shuttle.SeatStand + (Double3)DemoSurface.Eye;

        Double3 view = world.PoseIn(scene[camera], cabin).Position;

        PaneChecks.Through(Shuttle.Glass.Single(p => p.Id == pane), view, eye).Should().BeTrue($"the line from {camera} at {view} to the eye at {eye} passes through {pane}");
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
    /// <summary>
    /// The engine keeps the center of mass that Jolt computes for the hull's shape, and publishes it in the body facts
    /// (docs/plans/parts.md, "Settled forks"). The demo's own value comes from the integral of the loft. The two agree
    /// within 1 mm, so the demo can take the engine's value for the places of its parts and drop its own.
    /// </summary>
    [Fact]
    public void TheShuttlesHullHasItsCenterOfMassWithinAMillimeterOfTheLoftsIntegral()
    {
        FrameHandle space = world.CreateRootFrame(new FrameSpec(FrameBounds.Sphere(1e4f)));
        Entity hull = FreeHull(space, Vector3.Zero);

        world.Snapshot.TryGetBody(hull, out BodyFacts facts).Should().BeTrue();

        Vector3.Distance(facts.CenterOfMass, Shuttle.CenterOfMass).Should().BeLessThan(1e-3f,
            $"the engine's center of mass {facts.CenterOfMass} is the loft's {Shuttle.CenterOfMass}");
        facts.CenterOfMass.Length().Should().BeGreaterThan(0.1f, "the hull's center of mass is low and aft of its origin");
    }

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
