using System.Numerics;
using Tertius.Frames.Rails;
using Tertius.Host;
using Tertius.Simulation;

namespace Tertius.Demo;

public static partial class DemoScenes
{
    /// <summary>
    /// The station of the Blue Danube demo (docs/plans/blue-danube-demo.md, "The station" and "The kilometre wheel"): a wheel
    /// 4 km across on a circle 400 km up, in the launch plane, which turns about its axis at the rate that gives one
    /// standard gravity on its floor, 2,000 m from the axis. Its shape is <see cref="Layout"/>, in its own axes: +Y is the
    /// axis, about which it turns, and the angle θ about it runs from +X toward -Z. The docking slot is on the +Y side.
    /// <list type="bullet">
    /// <item>The wheel: the floor, the interior shape of the station's own body; the ceiling, three bands with the lift's
    /// opening (see <see cref="StationExit.CeilingBands"/>); and two side walls. They close the wheel's tube, so it looks
    /// closed from outside.</item>
    /// <item>Two spokes, lift shafts, square tubes along +X and -X in the plane y = 0, from the hub's outer face to the
    /// ceiling. One static body, a compound of their 8 wall boxes.</item>
    /// <item>The hub, a box along the axis that holds the bay. The slot leads through its +Y wall, and its -X wall's inner
    /// face is the deck, with the lift's hole. It is built in its own axes, whose +Z is the station's +Y (see
    /// <see cref="HubTurn"/>), because a box opening runs along X or Z only.</item>
    /// <item>Four bay lamps, glowing panels whose faces are rectangle lights, near the slot and on the bay's far end
    /// wall. They leave the deck clear for the landing.</item>
    /// <item>The views <c>rim-camera</c>, <c>bay-camera</c> and <c>hold-camera</c>, which turn with the station, and
    /// <c>far-camera</c>, 7 km out, which does not turn.</item>
    /// <item>The exit's lift, lamps and cameras (see <see cref="StationExit.Spawn"/>), and the coda's loose boxes, floor
    /// marks and cameras on the wheel's floor (see <see cref="WheelCoda.Spawn"/>).</item>
    /// </list>
    /// Every part is drawn with <see cref="RenderFlags.NoImpostor"/>: the spin turns the station, so an impostor's capture
    /// would go stale.
    /// </summary>
    public static class DemoStation
    {
        /// <summary>The hull's mass, in kilograms, which a kinematic hull does not use.</summary>
        public const float Mass = 1e7f;

        /// <summary>
        /// The world time at which the station passes over the pad, in seconds: 38 s, <see cref="DepartureLead"/> after the
        /// departure. It sets the launch window (docs/plans/blue-danube-demo.md, "The orbits, the window and the times").
        /// </summary>
        public const double OverPadAt = 38;

        /// <summary>
        /// How long before the station's pass over the pad the shuttle departs, in seconds: 18 s, the lead of the live
        /// world's ground departure before the station's pass (docs/plans/live-world.md, "Departures"). It sets the
        /// departure, <see cref="DemoSurface.DepartureAt"/>.
        /// </summary>
        public const double DepartureLead = 18;

        /// <summary>How far <c>far-camera</c> stands from the station's center, in meters.</summary>
        public const double FarCameraDistance = 7000;

        /// <summary>The quarter turns about the axis of the two spokes from the spoke along +X: none, and half a turn.</summary>
        private static readonly int[] SpokeTurns = [0, 2];

        /// <summary>The demo station's shape: its floor 2,000 m out, its deck 40 m out, and its bay on the axis.</summary>
        public static StationLayout Layout { get; } = new();

        /// <summary>
        /// The turn from the hub's own axes to the station's: a quarter turn about X that takes the hub's +Z to the station's
        /// +Y and the hub's +Y to the station's -Z, and keeps X, so the deck stays on the -X side.
        /// </summary>
        public static DQuaternion HubTurn { get; } = DQuaternion.FromAxisAngle(Double3.UnitX, -Math.PI / 2);

        /// <summary>
        /// Where <c>bay-camera</c> stands, in the station's axes: beside the slot, 5 m across the axis from its edge on the
        /// side away from the deck and 1.5 m in from the slot's wall, on the bay's middle line. The shuttle's way in and out
        /// to the deck passes clear of it.
        /// </summary>
        public static Double3 BayCamera { get; } = new(Layout.Slot.Max.X + 5, Layout.Bay.Max.Y - 1.5, 0);

        /// <summary>The point that <c>bay-camera</c> looks at, in the station's axes: 4 m above the deck and 4.5 m in from the slot's wall, on the bay's middle line.</summary>
        public static Double3 BayCameraAim { get; } = new(Layout.Deck + 4, Layout.Bay.Max.Y - 4.5, 0);

        /// <summary>
        /// Where <c>hold-camera</c> stands, in the station's axes: 20 m beyond the hold point along the axis and 32 m from the
        /// axis. From here the shuttle at the hold lies 37 m away, and the hub beyond it.
        /// </summary>
        public static Double3 HoldCamera { get; } = Layout.HoldPoint + new Double3(30, 20, 10);

        /// <summary>The point that <c>hold-camera</c> looks at, in the station's axes: on the axis a quarter of the way from the hold point to the hub's middle.</summary>
        public static Double3 HoldCameraAim { get; } =
            Layout.HoldPoint + (new Double3(0, Layout.Hub.Center.Y, 0) - Layout.HoldPoint) / 4;

        /// <summary>
        /// Where <c>far-camera</c> stands relative to the station's center, in the station's axes at the spin's phase 0: 7 km
        /// out, 63 degrees from the axis on the slot's side, toward the Sun's side of the station at the departure. It looks
        /// at the station's center, so the whole wheel reads as an ellipse.
        /// </summary>
        public static Double3 FarCameraOffset { get; } = new Double3(-0.3, 0.45, 0.84).Normalized() * FarCameraDistance;

        /// <summary>
        /// The turn from the station's axes at the spin's phase 0 to earth's: a quarter turn about Z that takes the station's
        /// +Y to +X, then the ground's turn at the departure about earth's +Y. So the axis lies along the departure axes' +X.
        /// </summary>
        public static DQuaternion AxialTilt { get; } =
            DQuaternion.FromAxisAngle(Double3.UnitY, DemoSurface.GroundTurnAtDeparture)
            * DQuaternion.FromAxisAngle(Double3.UnitZ, -Math.PI / 2);

        /// <summary>
        /// The station's orbit in <c>earth</c>, which does not turn and holds Earth's point gravity
        /// (docs/plans/blue-danube-demo.md, "The station's plane"): a polar circle of <see cref="OrbitRadius"/> in the launch
        /// plane, whose normal is <see cref="DemoSurface.LaunchPlaneNormal"/>. In the departure axes, the ground's axes at
        /// <see cref="DemoSurface.DepartureAt"/>, the orbit is the circle whose normal is -X: at mean anomaly 0 the station
        /// is at +Z and moves toward +Y, and it passes over the pad, at mean anomaly 90 degrees, at <see cref="OverPadAt"/>,
        /// moving toward -Z there, the way the ramp points at the departure. The departure axes are earth's turned by
        /// <see cref="DemoSurface.GroundTurnAtDeparture"/> about the pole, so the node turns by that angle, and the axial
        /// tilt turns the station's +Y first to +X and then by that angle about the pole: to the departure axes' +X, the
        /// slot's side. The station spins about that axis at the layout's rate, so the axis stays fixed in earth.
        /// </summary>
        public static KeplerOrbit Orbit { get; } =
            new(
                SemiMajorAxis: OrbitRadius,
                Eccentricity: 0,
                Inclination: Math.PI / 2,
                LongitudeOfAscendingNode: -Math.PI / 2 + DemoSurface.GroundTurnAtDeparture,
                ArgumentOfPeriapsis: 0,
                MeanAnomalyAtEpoch: Math.PI / 2
                    - Math.Sqrt(EarthMu / (OrbitRadius * OrbitRadius * OrbitRadius)) * OverPadAt,
                Epoch: 0,
                Attitude: new Spin(
                    AxialTilt: AxialTilt,
                    Rate: Layout.Rate,
                    PhaseAtEpoch: 0
                )
            );

        /// <summary>The hub's middle in the station's axes: on the axis, where the hub's span along it is centered.</summary>
        private static Double3 HubCenter => new(0, Layout.Hub.Center.Y, 0);

        /// <summary>The hub's half extents in its own axes, whose +Z is the station's +Y.</summary>
        private static Vector3 HubOwnHalf
        {
            get
            {
                Vector3 half = Layout.Hub.Extents;
                return new(half.X, half.Z, half.Y);
            }
        }

        /// <summary>The slot in the hub's own axes: an opening through its +Z wall.</summary>
        private static Opening HubSlot => Opening.ThroughZ(InHubAxes(Layout.Slot));

        /// <summary>The lift's hole in the hub's own axes: an opening through its -X wall, the deck's.</summary>
        private static Opening HubLiftHole => Opening.ThroughX(InHubAxes(Layout.LiftHole));

        /// <summary>
        /// <paramref name="box"/>, in the station's axes, in the hub's own axes: from the hub's middle, with the station's
        /// (x, y, z) at the hub's (x, z, -y), as <see cref="HubTurn"/> turns them.
        /// </summary>
        private static BoundingBox InHubAxes(BoundingBox box)
        {
            var center = HubCenter.ToVector3();
            Vector3 min = box.Min - center,
                max = box.Max - center;
            return new(new(min.X, -max.Z, min.Y), new(max.X, -min.Z, max.Y));
        }

        /// <summary>The spoke at θ = 0 as a solid, along +X from the hub's outer face to the ceiling, in the station's axes.</summary>
        private static BoundingBox SpokeSolid =>
            new(
                new(Layout.Hub.Max.X, -Layout.SpokeHalfWidth, -Layout.SpokeHalfWidth),
                new(Layout.CeilingRadius, Layout.SpokeHalfWidth, Layout.SpokeHalfWidth)
            );

        /// <summary>
        /// The station's hull, in the station's axes: a compound of the wheel, a solid ring from the ceiling to the floor's
        /// outer face in one hull per segment, the two spokes as solid boxes, and the hub as a solid box.
        /// </summary>
        public static ShapeDesc HullShape()
        {
            var wheel = (CompoundShapeDesc)
                RevolvedSolid
                    .Ring(
                        innerRadius: Layout.CeilingRadius,
                        outerRadius: Layout.FloorRadius + Layout.FloorThickness,
                        halfWidth: Layout.HalfWidth,
                        segments: Layout.Segments
                    )
                    .Shape();
            return new CompoundShapeDesc([
                .. wheel.Children,
                .. SpokeTurns.Select(quarter => Box(Turned(SpokeSolid, quarter), Pose.Identity)),
                Box(Layout.Hub, Pose.Identity),
            ]);
        }

        /// <summary>
        /// The boxes of the hub's walls, in its own axes, with the slot and the lift's hole open: the far end wall across Z,
        /// the slot's end wall in four parts round the slot, the walls across X between the end walls, the deck's in four
        /// parts round the lift's hole, and the walls across Y between those. The boxes touch without gaps or overlaps.
        /// </summary>
        public static IEnumerable<BoundingBox> HubWalls()
        {
            Vector3 outer = HubOwnHalf,
                inner = outer - new Vector3(Layout.HubWall);
            BoundingBox slot = HubSlot.Bounds;
            yield return new(-outer, outer with { Z = -inner.Z });
            var end = new BoundingBox(-outer with { Z = inner.Z }, outer);
            yield return end with
            {
                Max = end.Max with { X = slot.Min.X },
            };
            yield return end with
            {
                Min = end.Min with { X = slot.Max.X },
            };
            yield return new(new(slot.Min.X, -outer.Y, inner.Z), new(slot.Max.X, slot.Min.Y, outer.Z));
            yield return new(new(slot.Min.X, slot.Max.Y, inner.Z), new(slot.Max.X, outer.Y, outer.Z));
            foreach (
                BoundingBox part in AroundDoorway(
                    new(new(-outer.X, -outer.Y, -inner.Z), new(-inner.X, outer.Y, inner.Z)),
                    HubLiftHole.Bounds
                )
            )
                yield return part;
            yield return new(new(inner.X, -outer.Y, -inner.Z), new(outer.X, outer.Y, inner.Z));
            yield return new(new(-inner.X, -outer.Y, -inner.Z), new(inner.X, -inner.Y, inner.Z));
            yield return new(new(-inner.X, inner.Y, -inner.Z), new(inner.X, outer.Y, inner.Z));
        }

        /// <summary>
        /// The four wall boxes of the spoke at θ = 0, in the station's axes: the walls across Y over the whole width, and
        /// the walls across Z between them. The tube is open at both ends.
        /// </summary>
        private static IEnumerable<BoundingBox> SpokeWalls()
        {
            BoundingBox solid = SpokeSolid;
            float inner = Layout.SpokeHalfWidth - Layout.SpokeWall;
            yield return solid with
            {
                Min = solid.Min with { Y = inner },
            };
            yield return solid with
            {
                Max = solid.Max with { Y = -inner },
            };
            yield return new(new(solid.Min.X, -inner, inner), new(solid.Max.X, inner, Layout.SpokeHalfWidth));
            yield return new(new(solid.Min.X, -inner, -Layout.SpokeHalfWidth), new(solid.Max.X, inner, -inner));
        }

        /// <summary>
        /// The boxes of the two spokes' walls, in the station's axes: those of the spoke at θ = 0, and the same turned by half
        /// a turn about the axis, the lift's spoke at θ = 180 degrees.
        /// </summary>
        public static IEnumerable<BoundingBox> Spokes() =>
            SpokeTurns.SelectMany(quarter => SpokeWalls().Select(box => Turned(box, quarter)));

        /// <summary>
        /// <paramref name="box"/> turned by <paramref name="quarter"/> quarter turns about +Y, the way θ grows, from +X
        /// toward -Z: a quarter turn takes +X to -Z and +Z to +X. Quarter turns keep a box aligned with the axes.
        /// </summary>
        private static BoundingBox Turned(BoundingBox box, int quarter)
        {
            Vector3 Turn(Vector3 p) =>
                (quarter % 4) switch
                {
                    0 => p,
                    1 => new(p.Z, p.Y, -p.X),
                    2 => new(-p.X, p.Y, -p.Z),
                    _ => new(-p.Z, p.Y, p.X),
                };

            Vector3 a = Turn(box.Min),
                b = Turn(box.Max);
            return new(Vector3.Min(a, b), Vector3.Max(a, b));
        }

        private static CompoundChild Box(BoundingBox box, Pose pose) =>
            new(
                new BoxShapeDesc(box.Extents),
                new(pose.Position + Vector3.Transform(box.Center, pose.Rotation), pose.Rotation)
            );

        private static CompoundShapeDesc Boxes(IEnumerable<BoundingBox> boxes) =>
            new([.. boxes.Select(box => Box(box, Pose.Identity))]);

        private static MeshShapeDesc MeshShape(MeshData mesh) =>
            new([.. mesh.Vertices.Select(v => v.Position)], [.. mesh.Indices.Select(i => (int)i)]);

        /// <summary>A gray-box render row of a station part: it casts shadows, and has no impostor.</summary>
        private static RenderData Part(
            IRenderResources resources,
            MeshData mesh,
            Vector4 color,
            float boundingRadius
        ) =>
            Drawn(resources: resources, mesh: mesh, color: color, boundingRadius: boundingRadius) with
            {
                Flags = RenderFlags.Visible | RenderFlags.CastsShadow | RenderFlags.NoImpostor,
            };

        /// <summary>
        /// Spawns the station in <paramref name="parent"/>, <c>earth</c> of demo-surface and of earth-system, which does not
        /// turn and holds Earth's point gravity, on <see cref="Orbit"/> at the world's time, with its parts, its lamps, its
        /// lift and its cameras, and adds them to <paramref name="scene"/>: the owner <c>station</c>, the ceiling's bands,
        /// <c>station-wall-slot</c> and <c>station-wall-far</c>, the side walls on the slot's side and the other,
        /// <c>station-spokes</c> and <c>station-hub</c>. Returns the station.
        /// </summary>
        /// <remarks>
        /// The spawn carries the first motion intent of the orbit, as <see cref="Station.Build"/> does, so the frame is on
        /// its rails and turns from its first step. A Kepler frame in a parent that does not turn falls freely, so its proper
        /// acceleration is zero. The hull is kinematic, and its <see cref="KeplerMotion"/> moves it. It moves fast, so it
        /// lives in a following frame of its own (ARCHITECTURE.md, "Numeric frames").
        /// </remarks>
        public static Entity Spawn(World world, FrameHandle parent, SceneInstance scene, IRenderResources resources)
        {
            Entity station = SpawnWheel(world: world, parent: parent, resources: resources);
            scene.Add("station", station);
            FrameHandle frame = world.GetOwnedFrame(station);

            SpawnCeiling(world: world, frame: frame, scene: scene, resources: resources);
            SpawnSideWalls(world: world, frame: frame, scene: scene, resources: resources);
            SpawnSpokes(world: world, frame: frame, scene: scene, resources: resources);
            SpawnHub(world: world, frame: frame, scene: scene, resources: resources);
            StationExit.Spawn(world: world, station: frame, scene: scene, resources: resources);
            WheelCoda.Spawn(world: world, station: frame, scene: scene, resources: resources);

            SpawnBayLamps(world: world, frame: frame, scene: scene, resources: resources);
            SpawnCameras(world: world, frame: frame, scene: scene);
            scene.Add("far-camera", SpawnFarCamera(world: world, station: station, parent: parent));
            return station;
        }

        /// <summary>
        /// Spawns the wheel on <see cref="Orbit"/> in <paramref name="parent"/>: the owner of the station's frame, with
        /// the floor as its body's interior and the hull outside, and returns it.
        /// </summary>
        private static Entity SpawnWheel(World world, FrameHandle parent, IRenderResources resources)
        {
            KeplerOrbit orbit = Orbit;
            (Double3 place, Double3 velocity) = orbit.StateAt(world.Time, EarthMu);
            var floor = RevolvedSolid.Ring(
                innerRadius: Layout.FloorRadius,
                outerRadius: Layout.FloorRadius + Layout.FloorThickness,
                halfWidth: Layout.HalfWidth,
                segments: Layout.Segments
            );
            MeshData floorMesh = floor.Mesh();
            ShapeDesc hull = HullShape();
            return world.Spawn(
                new()
                {
                    Frame = parent,
                    Pose = new(place, orbit.RotationAt(world.Time, EarthMu)),
                    LinearVelocity = velocity,
                    AngularVelocity = orbit.AngularVelocityAt(world.Time, EarthMu).ToVector3(),
                    FirstMove = new(orbit.Sample(EarthMu, world.TimeAt(world.TickNumber + 1), world.DeltaTime)),
                    Components = [new KeplerMotion { Orbit = orbit }],
                    Body = new(
                        Shape: hull,
                        Motion: BodyMotion.Kinematic,
                        Mass: Mass,
                        InteriorShape: MeshShape(floorMesh)
                    ),
                    AsFrame = new(Layout.Bubble, Medium: Air.Filling(Layout.Bay)),
                    Render = Part(
                        resources: resources,
                        mesh: floorMesh,
                        color: new(x: 0.62f, y: 0.63f, z: 0.66f, w: 1),
                        boundingRadius: floor.Shape().BoundingRadius()
                    ),
                }
            );
        }

        /// <summary>Spawns the bands of the ceiling in <paramref name="frame"/>.</summary>
        private static void SpawnCeiling(
            World world,
            FrameHandle frame,
            SceneInstance scene,
            IRenderResources resources
        )
        {
            foreach ((string id, RevolvedSolid band, DPose pose) in StationExit.CeilingBands())
            {
                ShapeDesc bandShape = band.Shape();
                scene.Add(
                    id,
                    world.Spawn(
                        new()
                        {
                            Frame = frame,
                            Pose = pose,
                            Body = new(Shape: bandShape, Motion: BodyMotion.Static),
                            Render = Part(
                                resources: resources,
                                mesh: band.Mesh(),
                                color: new(x: 0.7f, y: 0.71f, z: 0.74f, w: 1),
                                boundingRadius: bandShape.BoundingRadius()
                            ),
                        }
                    )
                );
            }
        }

        /// <summary>
        /// Spawns the two side walls in <paramref name="frame"/>, on the slot's side and the far side, which share one
        /// mesh.
        /// </summary>
        private static void SpawnSideWalls(
            World world,
            FrameHandle frame,
            SceneInstance scene,
            IRenderResources resources
        )
        {
            var wall = RevolvedSolid.Ring(
                innerRadius: Layout.CeilingRadius + Layout.CeilingThickness,
                outerRadius: Layout.FloorRadius,
                halfWidth: Layout.SideWallThickness / 2,
                segments: Layout.Segments
            );
            ShapeDesc wallShape = wall.Shape();
            RenderData wallRender = Part(
                resources: resources,
                mesh: wall.Mesh(),
                color: new(x: 0.55f, y: 0.57f, z: 0.62f, w: 1),
                boundingRadius: wallShape.BoundingRadius()
            );
            double side = Layout.HalfWidth - Layout.SideWallThickness / 2;
            foreach ((string id, double y) in new[] { ("station-wall-slot", side), ("station-wall-far", -side) })
                scene.Add(
                    id,
                    world.Spawn(
                        new()
                        {
                            Frame = frame,
                            Pose = new(new(0, y, 0), DQuaternion.Identity),
                            Body = new(Shape: wallShape, Motion: BodyMotion.Static),
                            Render = wallRender,
                        }
                    )
                );
        }

        /// <summary>Spawns the spokes in <paramref name="frame"/>, one static body over all of their boxes.</summary>
        private static void SpawnSpokes(World world, FrameHandle frame, SceneInstance scene, IRenderResources resources)
        {
            BoundingBox[] spokes = [.. Spokes()];
            BoxMesh spokeMesh = new();
            foreach (BoundingBox box in spokes)
                spokeMesh.Box(box);
            CompoundShapeDesc spokeShape = Boxes(spokes);
            scene.Add(
                "station-spokes",
                world.Spawn(
                    new()
                    {
                        Frame = frame,
                        Pose = DPose.Identity,
                        Body = new(Shape: spokeShape, Motion: BodyMotion.Static),
                        Render = Part(
                            resources: resources,
                            mesh: spokeMesh.Build(),
                            color: new(x: 0.5f, y: 0.52f, z: 0.58f, w: 1),
                            boundingRadius: spokeShape.BoundingRadius()
                        ),
                    }
                )
            );
        }

        /// <summary>Spawns the hub in <paramref name="frame"/>: its walls, with the slot and the lift's hole in the deck.</summary>
        private static void SpawnHub(World world, FrameHandle frame, SceneInstance scene, IRenderResources resources)
        {
            Vector3 hubOwn = HubOwnHalf;
            Opening slot = HubSlot,
                hole = HubLiftHole;
            CompoundShapeDesc hubShape = Boxes(HubWalls());
            scene.Add(
                "station-hub",
                world.Spawn(
                    new()
                    {
                        Frame = frame,
                        Pose = new(HubCenter, HubTurn),
                        Body = new(Shape: hubShape, Motion: BodyMotion.Static),
                        Render = Part(
                            resources: resources,
                            mesh: new BoxMesh()
                                .Box(new(-hubOwn, hubOwn), openings: [slot, hole])
                                .Box(
                                    new(-(hubOwn - new Vector3(Layout.HubWall)), hubOwn - new Vector3(Layout.HubWall)),
                                    inward: true,
                                    openings: [slot, hole]
                                )
                                .Sides(slot)
                                .Sides(hole)
                                .Build(),
                            color: new(x: 0.82f, y: 0.82f, z: 0.84f, w: 1),
                            boundingRadius: hubShape.BoundingRadius()
                        ),
                    }
                )
            );
        }

        /// <summary>
        /// Spawns the bay lamps in <paramref name="frame"/>, added to <paramref name="scene"/> as <c>bay-lamp-0</c> to
        /// <c>bay-lamp-3</c>.
        /// </summary>
        private static void SpawnBayLamps(
            World world,
            FrameHandle frame,
            SceneInstance scene,
            IRenderResources resources
        )
        {
            // The bay lamps: glowing panels 1.2 m square, flat on the bay's walls, each a floodlight's rectangle light on its
            // face toward the bay, in the station's axes. Three stand on the side walls 8 m in from the slot, on the axis
            // and halfway to the deck, and one in the middle of the far end wall.
            BoundingBox bay = Layout.Bay;
            float nearSlot = bay.Max.Y - 8,
                halfway = Layout.Deck / 2;
            (Vector3 Wall, Vector3 Inward)[] lamps =
            [
                (new(0, nearSlot, bay.Max.Z), -Vector3.UnitZ),
                (new(0, nearSlot, bay.Min.Z), Vector3.UnitZ),
                (new(halfway, nearSlot, bay.Max.Z), -Vector3.UnitZ),
                (new(0, bay.Min.Y, 0), Vector3.UnitY),
            ];
            MaterialId lampMaterial = LampPanelMaterial(resources: resources, light: BayPanelLight(lamps[0].Inward));
            for (int i = 0; i < lamps.Length; i++)
            {
                (Vector3 at, Vector3 inward) = lamps[i];
                Vector3 half = new Vector3(0.6f) - 0.55f * Vector3.Abs(inward);
                scene.Add(
                    $"bay-lamp-{i}",
                    world.Spawn(
                        new()
                        {
                            Frame = frame,
                            Pose = new(at + inward * half, DQuaternion.Identity),
                            Render = new RenderData
                            {
                                Mesh = resources.CreateMesh(Primitives.Box(half)),
                                Material = lampMaterial,
                                BoundingRadius = 0.85f,
                                Flags = RenderFlags.Visible | RenderFlags.NoImpostor,
                                Light = BayPanelLight(inward),
                            },
                        }
                    )
                );
            }
        }

        /// <summary>
        /// The light of a bay lamp whose panel, 0.1 m thick, lies flat on a wall: its face 1.2 m square toward the bay along
        /// the unit axis <paramref name="inward"/>, with a floodlight's power.
        /// </summary>
        private static RectLightData BayPanelLight(Vector3 inward) =>
            PanelLight(
                offset: inward * 0.05f,
                front: inward,
                // Any axis across the front serves the square face.
                across: MathF.Abs(inward.Y) > 0.5f ? Vector3.UnitX : Vector3.Cross(Vector3.UnitY, inward),
                width: 1.2f,
                height: 1.2f,
                power: FloodlightPower
            );

        /// <summary>Spawns the rim, bay and hold cameras in <paramref name="frame"/>.</summary>
        private static void SpawnCameras(World world, FrameHandle frame, SceneInstance scene)
        {
            // The rim camera: at θ = 45 degrees, between the two spokes, 2 m in from the floor's radius and 2 m off the
            // slot's face of the wheel. It looks at a point on the axis a third of the way from the slot's face to the hold
            // point. Its roof lies across the plane of the axis and the camera, so the axis runs across the picture.
            double theta = Math.PI / 4;
            var across = new Double3(Math.Cos(theta), 0, -Math.Sin(theta));
            Double3 rim = across * (Layout.FloorRadius - 2) + new Double3(0, Layout.HalfWidth + 2, 0);
            var aim = new Double3(0, Layout.SlotFace + (Layout.HoldPoint.Y - Layout.SlotFace) / 3, 0);
            scene.Add(
                "rim-camera",
                world.Spawn(
                    new()
                    {
                        Frame = frame,
                        Pose = new(rim, AttitudeAutopilot.Facing(aim - rim, Double3.Cross(across, Double3.UnitY))),
                        Render = new RenderData(),
                    }
                )
            );

            // The bay camera: still in the bay, in the corner across from the parked place. It looks at a point between the
            // axis and the parked shuttle, with its roof away from the deck, so it sees the slot, the nose as it comes
            // through, the move out and the landing in the corner.
            scene.Add(
                "bay-camera",
                world.Spawn(
                    new()
                    {
                        Frame = frame,
                        Pose = new(BayCamera, AttitudeAutopilot.Facing(BayCameraAim - BayCamera, Double3.UnitX)),
                        Render = new RenderData(),
                    }
                )
            );

            // The hold camera: beyond the hold point and off the axis, looking back past the shuttle at the hold toward the
            // hub. Its roof lies along +Z, across the plane of the axis and the camera, so the axis runs across the picture.
            scene.Add(
                "hold-camera",
                world.Spawn(
                    new()
                    {
                        Frame = frame,
                        Pose = new(HoldCamera, AttitudeAutopilot.Facing(HoldCameraAim - HoldCamera, Double3.UnitZ)),
                        Render = new RenderData(),
                    }
                )
            );
        }

        /// <summary>
        /// Spawns <c>far-camera</c>, attached to <paramref name="station"/>: a <see cref="FrameFollowCamera"/> that keeps
        /// <see cref="FarCameraOffset"/>, turned by <see cref="AxialTilt"/>, in the axes of <paramref name="parent"/>, which does
        /// not turn. So it does not turn with the station's spin, and looks at the station's center with its roof along the
        /// axis.
        /// </summary>
        private static Entity SpawnFarCamera(World world, Entity station, FrameHandle parent)
        {
            Double3 offset = AxialTilt.Rotate(FarCameraOffset);
            return world.Spawn(
                new()
                {
                    AttachTo = station,
                    AttachOffset = new(offset.ToVector3(), Quaternion.Identity),
                    Render = new RenderData(),
                    Components =
                    [
                        new FrameFollowCamera(
                            reference: parent,
                            offset: offset,
                            forward: -offset,
                            up: AxialTilt.Rotate(Double3.UnitY)
                        ),
                    ],
                }
            );
        }
    }
}
