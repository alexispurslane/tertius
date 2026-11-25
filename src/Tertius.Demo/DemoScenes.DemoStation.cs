using System.Numerics;
using Tertius.Assets;
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
    /// <item>Pioneer's hub draws the axial entrance and berth 14. The four deck colliders meet its floor
    /// around a 4.10 m lift opening. Without art the scene supplies a holed deck and a five-wall shell.</item>
    /// <item>The imported ring and hub have openings where the working lift shaft passes through their skins.
    /// The shaft liner, cradle and four deck-mounted floodlights remain visible with the art.</item>
    /// <item>The views <c>rim-camera</c>, <c>bay-camera</c>, <c>hold-camera</c> and <c>gallery-camera</c>, which turn with
    /// the station, and
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
        /// The world time at which the station passes over the pad, in seconds: 42 s, <see cref="DepartureLead"/> after the
        /// departure. It sets the launch window (docs/plans/blue-danube-demo.md, "The orbits, the window and the times").
        /// </summary>
        public const double OverPadAt = 42;

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

        /// <summary>The demo station's shape: its floor 2,000 m out, its deck 104.611 m out, and its bay on the axis.</summary>
        public static StationLayout Layout { get; } = new();

        /// <summary>
        /// Where the station's art stands in the station's axes: its origin 14.6 m along +Y, unturned. The middle line of the
        /// art's spokes, round tubes 45 m across, lies 14.6 m below its origin along its axis, so they lie in the wheel's
        /// plane y = 0 round the gray box's spokes. They run along ±X, as the gray box's do. So do its ring's middle, 14.6 m
        /// below its origin, and its core's flat face 104.611 m from the axis along -X.
        /// </summary>
        public static DPose ArtPose { get; } = new(new(0, 14.6, 0), DQuaternion.Identity);

        /// <summary>
        /// The turn from the hub's own axes to the station's: a quarter turn about X that takes the hub's +Z to the station's
        /// +Y and the hub's +Y to the station's -Z, and keeps X, so the deck stays on the -X side.
        /// </summary>
        public static DQuaternion HubTurn { get; } = DQuaternion.FromAxisAngle(Double3.UnitX, -Math.PI / 2);

        /// <summary>
        /// Where <c>bay-camera</c> stands, in the station's axes: in the bay, 1 m in from the slot's wall, 20 m across the
        /// axis on the side away from the deck, and 10 m to -Z, off the slot's edge. The shuttle's way in along the axis
        /// and out to the deck passes 14 m clear of it.
        /// </summary>
        public static Double3 BayCamera { get; } = new(20, Layout.Bay.Max.Y - 1, -10);

        /// <summary>
        /// The point that <c>bay-camera</c> looks at, in the station's axes: 30 m from the axis toward the deck, 17 m in from
        /// the slot's wall and 5 m to +Z. So the slot, the parked hull and the lift's hole lie within 0.77 of the half
        /// picture from its middle.
        /// </summary>
        public static Double3 BayCameraAim { get; } = new(-30, Layout.Bay.Max.Y - 17, 5);

        /// <summary>
        /// Where <c>hold-camera</c> stands, in the station's axes: 40 m beyond the hold point along the axis and 32 m from the
        /// axis. From here the shuttle at the hold lies 51 m away, and the hub beyond it.
        /// </summary>
        public static Double3 HoldCamera { get; } = Layout.HoldPoint + new Double3(30, 40, 10);

        /// <summary>
        /// The point that <c>hold-camera</c> looks at, in the station's axes: on the axis a tenth of the way from the hold
        /// point to the hub's middle. So the hub and the shuttle at the hold lie within 0.69 of the half picture from its
        /// middle, and the shuttle covers at least 9.5 percent of the picture.
        /// </summary>
        public static Double3 HoldCameraAim { get; } =
            Layout.HoldPoint + (new Double3(0, Layout.Hub.Center.Y, 0) - Layout.HoldPoint) / 10;

        /// <summary>
        /// How far the art's +Y face of the ring, which carries the habitat's galleries, lies along the axis, in meters: the
        /// face lies 238.6 m above the art's origin (tools/blue-danube-assets/habitat.py), so 253.2 m with
        /// <see cref="ArtPose"/>.
        /// </summary>
        public static double GalleryFace => ArtPose.Position.Y + 238.6;

        /// <summary>
        /// Where <c>gallery-camera</c> stands, in the station's axes: 12 m off the ring's +Y face and 1,945 m from the axis,
        /// at θ = 15 degrees, above the rooms of a cabin street. The galleries' windows lie from 1,921 m to 1,933 m out.
        /// </summary>
        public static Double3 GalleryCamera => AtTheta(15, 1945, GalleryFace + 12);

        /// <summary>
        /// The point that <c>gallery-camera</c> looks at, in the station's axes: on the ring's +Y face, 1,927 m from the axis,
        /// at θ = 16.5 degrees, about 52 m along the ring. So the camera sees the rows of lit windows recede along the curve.
        /// </summary>
        public static Double3 GalleryCameraAim => AtTheta(16.5, 1927, GalleryFace);

        /// <summary>The point at <paramref name="degrees"/> of θ, <paramref name="radius"/> from the axis and at <paramref name="y"/>.</summary>
        private static Double3 AtTheta(double degrees, double radius, double y)
        {
            double theta = degrees * Math.PI / 180;
            return new(radius * Math.Cos(theta), y, -radius * Math.Sin(theta));
        }

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
                Attitude: new Spin(AxialTilt: AxialTilt, Rate: Layout.Rate, PhaseAtEpoch: 0)
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
        /// The boxes of the deck, the hub's -X wall, in the hub's own axes, in four parts round the lift's hole. They touch
        /// without gaps or overlaps.
        /// </summary>
        public static IEnumerable<BoundingBox> DeckWalls() =>
            AroundDoorway(InHubAxes(Layout.DeckSlab), HubLiftHole.Bounds);

        /// <summary>The hub's outer and inner half extents in its own axes, whose +Z is the station's +Y: the hub's and the bay's.</summary>
        private static (Vector3 Outer, Vector3 Inner) HubOwnBox
        {
            get
            {
                Vector3 bay = Layout.Bay.Extents;
                return (HubOwnHalf, new(bay.X, bay.Z, bay.Y));
            }
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
            Part(
                resources: resources,
                mesh: mesh,
                material: MaterialDesc.Color(color, UsualRoughness),
                boundingRadius: boundingRadius
            );

        /// <summary>A render row of a station part in <paramref name="material"/>: it casts shadows, and has no impostor.</summary>
        private static RenderData Part(
            IRenderResources resources,
            MeshData mesh,
            MaterialDesc material,
            float boundingRadius
        ) =>
            new()
            {
                Mesh = resources.CreateMesh(mesh),
                Material = resources.CreateMaterial(material),
                BoundingRadius = boundingRadius,
                Flags = RenderFlags.Visible | RenderFlags.CastsShadow | RenderFlags.NoImpostor,
            };

        /// <summary>The steel of the deck, the cradle and the shaft: a dark, worn metal.</summary>
        internal static MaterialDesc DeckSteel { get; } =
            new(
                BaseColor: new(x: 0.3f, y: 0.31f, z: 0.33f, w: 1),
                Roughness: 0.62f,
                Model: new StandardShading(Metallic: 0.55f)
            );

        /// <summary>The painted steel of the cradle rail: a dull safety yellow, so that the rail reads beside the hull.</summary>
        private static MaterialDesc RailPaint { get; } =
            new(
                BaseColor: new(x: 0.62f, y: 0.46f, z: 0.08f, w: 1),
                Roughness: 0.55f,
                Model: new StandardShading(Metallic: 0.2f)
            );

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
        public static Entity Spawn(
            World world,
            FrameHandle parent,
            SceneInstance scene,
            IRenderResources resources,
            BlueDanubeModels? models = null
        )
        {
            Entity station = SpawnWheel(world: world, parent: parent, resources: resources);
            scene.Add("station", station);
            FrameHandle frame = world.GetOwnedFrame(station);

            bool grayBox = models is null;
            if (models is not null)
                scene.Add("station-art", world.Spawn(Placed(frame, ArtPose, models.Station)));
            SpawnCeiling(world: world, frame: frame, scene: scene, resources: resources);
            SpawnSideWalls(world: world, frame: frame, scene: scene, resources: resources);
            SpawnSpokes(world: world, frame: frame, scene: scene, resources: resources, grayBox: grayBox);
            SpawnHub(world: world, frame: frame, scene: scene, resources: resources, grayBox: grayBox);
            StationExit.Spawn(world: world, station: frame, scene: scene, resources: resources);
            WheelCoda.Spawn(world: world, station: frame, scene: scene, resources: resources);
            if (models is not null)
                StationProps.Spawn(world: world, station: frame, scene: scene, resources: resources, models: models);

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
            // Running metres around the circumference and along the wheel axis. The tile is symmetric
            // along its first axis, so the two half-circles meet at the spoke without a texture seam.
            foreach (ref Vertex vertex in floorMesh.Vertices.AsSpan())
                vertex.TexCoord = new(
                    MathF.Atan2(MathF.Abs(vertex.Position.Z), vertex.Position.X) * Layout.FloorRadius,
                    vertex.Position.Y
                );
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
                        material: PlatformFloor(resources),
                        boundingRadius: floor.Shape().BoundingRadius()
                    ),
                }
            );
        }

        /// <summary>One metre of grey rubber floor tile, with pressed ribs at a pitch of 12.5 cm.</summary>
        private static MaterialDesc PlatformFloor(IRenderResources resources)
        {
            const int size = 64;
            byte[] rgba = new byte[size * size * 4];
            float[] heights = new float[size * size];
            for (int y = 0; y < size; y++)
            for (int x = 0; x < size; x++)
            {
                int inset = Math.Min(Math.Min(x, size - 1 - x), Math.Min(y, size - 1 - y));
                bool tread = (x % 8 is >= 2 and <= 5) && (y % 8 is 3 or 4);
                byte tone = (inset, tread) switch
                {
                    (0, _) => 91,
                    (_, true) => 137,
                    _ => 126,
                };
                heights[y * size + x] = (inset, tread) switch
                {
                    (0, _) => -0.001f,
                    (_, true) => 0.0018f,
                    _ => 0,
                };
                int i = (y * size + x) * 4;
                (rgba[i], rgba[i + 1], rgba[i + 2], rgba[i + 3]) = (tone, tone, (byte)(tone - 4), 255);
            }
            return new(
                BaseColor: Vector4.One,
                Roughness: 0.79f,
                BaseColorMap: resources.CreateTexture(new(Width: size, Height: size, Rgba: rgba)),
                NormalMap: resources.CreateTexture(ReliefMap(size, heights))
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

        /// <summary>
        /// Spawns the spokes in <paramref name="frame"/>, one static body over all of their boxes. The gray box draws both
        /// spokes whole. With the art, which draws their outsides, only the lift's shaft draws, the inside of the spoke at
        /// θ = 180 degrees.
        /// </summary>
        private static void SpawnSpokes(
            World world,
            FrameHandle frame,
            SceneInstance scene,
            IRenderResources resources,
            bool grayBox
        )
        {
            BoundingBox[] spokes = [.. Spokes()];
            CompoundShapeDesc spokeShape = Boxes(spokes);
            RenderData render = grayBox
                ? Part(
                    resources: resources,
                    mesh: Whole(spokes),
                    color: new(x: 0.5f, y: 0.52f, z: 0.58f, w: 1),
                    boundingRadius: spokeShape.BoundingRadius()
                )
                : Part(
                    resources: resources,
                    mesh: new BoxMesh().Sides(Opening.ThroughX(Shaft)).Build(),
                    material: StationExit.PanelFinish(resources),
                    boundingRadius: spokeShape.BoundingRadius()
                );
            scene.Add(
                "station-spokes",
                world.Spawn(
                    new()
                    {
                        Frame = frame,
                        Pose = DPose.Identity,
                        Body = new(Shape: spokeShape, Motion: BodyMotion.Static),
                        Render = render,
                    }
                )
            );
        }

        /// <summary>
        /// The lift's shaft, the inside of the spoke at θ = 180 degrees, in the station's axes: from the hub's outer face to
        /// the ceiling along -X, inside the spoke's walls.
        /// </summary>
        public static BoundingBox Shaft
        {
            get
            {
                BoundingBox solid = Turned(SpokeSolid, 2);
                float inner = Layout.SpokeHalfWidth - Layout.SpokeWall;
                return new(new(solid.Min.X, -inner, -inner), new(solid.Max.X, inner, inner));
            }
        }

        /// <summary>A mesh of every face of each box of <paramref name="boxes"/>.</summary>
        private static MeshData Whole(IEnumerable<BoundingBox> boxes)
        {
            BoxMesh mesh = new();
            foreach (BoundingBox box in boxes)
                mesh.Box(box);
            return mesh.Build();
        }

        /// <summary>
        /// Spawns the berth deck body and cradle. The imported hub supplies the visible floor.
        /// The fallback supplies the floor and five walls. Both versions draw the reveal through the lift opening.
        /// </summary>
        private static void SpawnHub(
            World world,
            FrameHandle frame,
            SceneInstance scene,
            IRenderResources resources,
            bool grayBox
        )
        {
            Opening hole = HubLiftHole;
            CompoundShapeDesc deckShape = Boxes(DeckWalls());
            (Vector3 outer, Vector3 inner) = HubOwnBox;
            BoundingBox deck = InHubAxes(Layout.DeckSlab);
            scene.Add(
                "station-hub",
                world.Spawn(
                    new()
                    {
                        Frame = frame,
                        Pose = new(HubCenter, HubTurn),
                        Body = new(Shape: deckShape, Motion: BodyMotion.Static),
                        Render = Part(
                            resources: resources,
                            mesh: (grayBox ? new BoxMesh().Box(deck, openings: [hole]) : new BoxMesh())
                                .Sides(hole)
                                .Build(),
                            material: DeckSteel,
                            boundingRadius: deckShape.BoundingRadius()
                        ),
                    }
                )
            );
            BoundingBox cradle = Layout.Cradle;
            scene.Add(
                "station-cradle",
                world.Spawn(
                    new()
                    {
                        Frame = frame,
                        Pose = new(cradle.Center, DQuaternion.Identity),
                        Body = new(Shape: new BoxShapeDesc(cradle.Extents, ConvexRadius: 0), Motion: BodyMotion.Static),
                        Render = Part(
                            resources: resources,
                            mesh: Primitives.Box(cradle.Extents),
                            material: RailPaint,
                            boundingRadius: cradle.Extents.Length()
                        ),
                    }
                )
            );
            if (!grayBox)
                return;

            Opening slot = HubSlot;
            // The fallback has five walls. Its sixth face belongs to the holed deck.
            BoxMesh walls = new BoxMesh()
                .Box(new(new(inner.X, -outer.Y, -outer.Z), outer))
                .Box(new(new(-inner.X, -outer.Y, -outer.Z), new(inner.X, -inner.Y, outer.Z)))
                .Box(new(new(-inner.X, inner.Y, -outer.Z), new(inner.X, outer.Y, outer.Z)))
                .Box(new(new(-inner.X, -inner.Y, -outer.Z), new(inner.X, inner.Y, -inner.Z)))
                .Box(new(new(-inner.X, -inner.Y, inner.Z), new(inner.X, inner.Y, outer.Z)), openings: [slot])
                .Sides(slot);
            scene.Add(
                "station-hub-walls",
                world.Spawn(
                    new()
                    {
                        Frame = frame,
                        Pose = new(HubCenter, HubTurn),
                        Render = Part(
                            resources: resources,
                            mesh: walls.Build(),
                            material: MaterialDesc.Color(new(x: 0.82f, y: 0.82f, z: 0.84f, w: 1), UsualRoughness),
                            boundingRadius: outer.Length()
                        ),
                    }
                )
            );
        }

        /// <summary>
        /// A drawn placement of <paramref name="model"/> at <paramref name="pose"/> in <paramref name="frame"/>, at its own
        /// size, with no body: it casts shadows and has no impostor, since the station's spin would make a capture stale.
        /// </summary>
        private static SpawnDesc Placed(FrameHandle frame, DPose pose, StoredModel model) =>
            new()
            {
                Frame = frame,
                Pose = pose,
                Render = new RenderData
                {
                    Model = model.Id,
                    BoundingRadius = model.BoundingRadius,
                    Flags = RenderFlags.Visible | RenderFlags.CastsShadow | RenderFlags.NoImpostor,
                },
            };

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
            BoundingBox bay = Layout.Bay;
            float height = Layout.Deck + 5;
            (Vector3 Wall, Vector3 Inward)[] lamps =
            [
                (new(height, 26.5f, bay.Max.Z), -Vector3.UnitZ),
                (new(height, 29, bay.Min.Z), Vector3.UnitZ),
                (new(height, 18.5f, bay.Max.Z), -Vector3.UnitZ),
                (new(height, bay.Min.Y, -11), Vector3.UnitY),
            ];
            Vector3 mastHalf = new(2.8f, 0.38f, 0.38f);
            RenderData mast = Part(
                resources: resources,
                mesh: Primitives.Box(mastHalf),
                material: DeckSteel,
                boundingRadius: mastHalf.Length()
            );
            MaterialId lampMaterial = LampPanelMaterial(resources: resources, light: BayPanelLight(lamps[0].Inward));
            for (int i = 0; i < lamps.Length; i++)
            {
                (Vector3 at, Vector3 inward) = lamps[i];
                Vector3 baseAt = at with { X = Layout.Deck + mastHalf.X };
                world.Spawn(
                    new()
                    {
                        Frame = frame,
                        Pose = new(baseAt - inward * mastHalf, DQuaternion.Identity),
                        Body = new(Shape: new BoxShapeDesc(mastHalf), Motion: BodyMotion.Static),
                        Render = mast,
                    }
                );
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

        /// <summary>Spawns the rim, bay, gallery and hold cameras in <paramref name="frame"/>.</summary>
        private static void SpawnCameras(World world, FrameHandle frame, SceneInstance scene)
        {
            // At 225 degrees the imported ring's inner glass crosses the view at radius 1,734 m.
            // The innermost glass vertices are 1,732 m out. Leave 32 m of clearance on the hub side.
            double theta = 5 * Math.PI / 4;
            var across = new Double3(Math.Cos(theta), 0, -Math.Sin(theta));
            Double3 rim = across * 1700 + new Double3(0, Layout.HalfWidth + 2, 0);
            var aim = new Double3(0, Layout.SlotFace + 21.2, 0);
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

            // The bay camera: still in the bay, near the slot's wall, across the axis from the deck. It looks at a point
            // between the axis and the parked shuttle, with its roof away from the deck, so it sees the slot, the nose as it
            // comes through, the move out and the landing in the corner.
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

            // The gallery camera: above the habitat's galleries on the ring's +Y face, looking along the ring. Its roof lies
            // along +Y, away from the face.
            scene.Add(
                "gallery-camera",
                world.Spawn(
                    new()
                    {
                        Frame = frame,
                        Pose = new(
                            GalleryCamera,
                            AttitudeAutopilot.Facing(GalleryCameraAim - GalleryCamera, Double3.UnitY)
                        ),
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
