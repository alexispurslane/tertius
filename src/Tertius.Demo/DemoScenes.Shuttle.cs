using System.Numerics;
using Tertius.Host;
using Tertius.Simulation;

namespace Tertius.Demo;

public static partial class DemoScenes
{
    /// <summary>
    /// The shuttle of the Blue Danube demo (docs/plans/blue-danube-demo.md): a frame owner whose hull fits a box 6 m wide,
    /// 4 m tall and 16 m long, in the tram shape of <see cref="Loft"/>. In its own axes +Y is up, -Z is the nose and +X is
    /// starboard.
    /// <list type="bullet">
    /// <item>In its parent frame the hull is the convex hull of the loft, <see cref="HullShape"/>. In its own frame it is
    /// hollow: the drawn hull's solid in convex pieces, walls 0.2 m thick round a cabin that is the box of
    /// <see cref="Cabin"/> where the walk, the seat, the pen and the float happen. The <see cref="Hatch"/> leads through the
    /// starboard wall, and the <see cref="Seat"/>, a static box, stands on the cabin floor. Both are part of the interior
    /// shape, <see cref="InteriorShape"/>, the static body of the frame's own physics system.</item>
    /// <item>A glass canopy of 40 panes, <see cref="Glass"/>, runs along both sides from 0.7 m above the cabin floor,
    /// over the roof's bevels and the roof, and across the front above the sill. The glass breaks around the hatch. The
    /// panes have no body: the interior shape is solid at the glass. The cabin's <see cref="Lamps"/> hang on the pillars of
    /// the walls.</item>
    /// <item>The hull has one density, so physics puts its center of mass at <see cref="CenterOfMass"/>, low and aft of
    /// its middle, and gives it the principal axes of <see cref="Inertia"/>.</item>
    /// <item>The frame has no gravity field of its own. So its contents feel only the opposite of its proper acceleration
    /// (ARCHITECTURE.md, "Gravity"), which the engine derives from the frame's motion: the weight of the ground while the
    /// shuttle stands parked on it, and nothing once the shuttle falls freely in orbit.</item>
    /// <item>Its bubble is the smallest ellipsoid that holds the hull's box grown by 2 m on every side, as the orbit scene's
    /// ship has, so a person who walks up to the hatch is in the shuttle's frame before it reaches the hull.</item>
    /// </list>
    /// </summary>
    public static class Shuttle
    {
        /// <summary>The half extents of the box that the hull fits, in meters, in the shuttle's axes.</summary>
        public static Vector3 Hull { get; } = new(3, 2, 8);

        /// <summary>The thickness of the hull's walls, floor and roof, in meters, in the plane of a cross-section.</summary>
        public const float Wall = 0.2f;

        /// <summary>
        /// The half extents of the cabin's box inside the walls, in meters, in the shuttle's axes. The bevels along the
        /// cabin's four long edges and the nose cut it.
        /// </summary>
        public static Vector3 Cabin { get; } = Hull - new Vector3(Wall);

        /// <summary>
        /// The mass of the hull, in kilograms. A parked hull does not use it, but it needs one so that a later
        /// <see cref="BodyHandle.SetMotion"/> can make it dynamic.
        /// </summary>
        public const float Mass = 2e4f;

        /// <summary>
        /// The acceleration that the main engine gives the shuttle at full throttle, in m/s²: three standard gravities,
        /// as a rocket sled pushes.
        /// </summary>
        public const float ThrustAcceleration = 3 * 9.81f;

        /// <summary>
        /// The most force of the <see cref="TranslationThrusters"/> along each of the hull's axes, in newtons: 30,000 N, which
        /// gives the shuttle's <see cref="Mass"/> 1.5 m/s². The docking's move out needs up to 1.2 m/s² on one of the hull's
        /// axes, to hold the hull on its path against the station's spin. The thrusters are one piece of hardware, so every
        /// law of the flight gets this limit (docs/plans/blue-danube-demo.md, "The translation thrusters' force").
        /// </summary>
        public const float TranslationThrust = 30000;

        /// <summary>
        /// The radius of the circle that the ascent autopilot flies the shuttle to, in meters, in <c>earth-surface</c>:
        /// <see cref="ParkingOrbitAltitude"/> above Earth's mean radius.
        /// </summary>
        public const double ParkingOrbitRadius = EarthRadius + ParkingOrbitAltitude;

        /// <summary>
        /// How high the parking orbit lies above Earth's mean radius, in meters: 200 km, above
        /// <see cref="AtmosphereTop"/> and below the station's 400 km.
        /// </summary>
        public const double ParkingOrbitAltitude = 200e3;

        /// <summary>
        /// The shuttle's flight controls, in the order in which they run in a tick: the layers of the flight program, the
        /// <see cref="AscentAutopilot"/> at priority 0, the <see cref="RendezvousAutopilot"/> at 1 and the
        /// <see cref="DockingAutopilot"/> at 2, which add the
        /// <see cref="Throttle"/>, the <see cref="AttitudeTarget"/> and the <see cref="Translation"/> of each tick to the
        /// shuttle's events, then the actuators at 10, the <see cref="AttitudeAutopilot"/>, the <see cref="MainEngine"/>
        /// and the <see cref="TranslationThrusters"/>, which read them and follow the last layer that speaks. The engine
        /// pushes <see cref="Mass"/> with <see cref="ThrustAcceleration"/> at full throttle, and both autopilots plan with
        /// that thrust. The ascent autopilot steers only once the shuttle is <paramref name="climbHeight"/> meters higher
        /// than where it launched, clear of its ramp, and it steers into the plane whose prograde normal is
        /// <paramref name="planeNormal"/>, in the axes of the shuttle's frame. The rendezvous autopilot flies
        /// <see cref="RendezvousPlan.Demo"/> to <paramref name="station"/>, and the docking autopilot docks in it, with the
        /// hull's <see cref="CenterOfMass"/>. The thrusters have <see cref="TranslationThrust"/> on each axis.
        /// </summary>
        public static IComponent[] FlightControls(double climbHeight, Double3 planeNormal, Entity station)
        {
            const float thrust = Mass * ThrustAcceleration;
            return [new AscentAutopilot(thrust, ParkingOrbitRadius, climbHeight, planeNormal), new RendezvousAutopilot(station, RendezvousPlan.Demo, thrust),
                new DockingAutopilot(station, CenterOfMass), new AttitudeAutopilot(Inertia), new MainEngine(thrust), new TranslationThrusters(TranslationThrust)];
        }

        /// <summary>
        /// The hatch, the doorway through the starboard wall, in the shuttle's axes: 1.2 m wide, 2.1 m tall, centered 2 m
        /// aft of the shuttle's center, with a sill 0.1 m above the cabin floor. A person on the ground below the hull
        /// steps 0.3 m up onto the sill, within the 0.4 m that a character climbs as a stair.
        /// </summary>
        public static BoundingBox Hatch { get; } = new(new Vector3(Cabin.X, -Cabin.Y + 0.1f, 1.4f), new Vector3(Hull.X, -Cabin.Y + 2.2f, 2.6f));

        /// <summary>
        /// The seat, in the shuttle's axes: a box 0.6 m wide, 0.5 m tall and 0.6 m deep on the cabin floor, centered 1 m to
        /// starboard and 4.5 m ahead of the shuttle's center.
        /// </summary>
        public static BoundingBox Seat { get; } = new(new Vector3(0.7f, -Cabin.Y, -4.8f), new Vector3(1.3f, -Cabin.Y + 0.5f, -4.2f));

        /// <summary>
        /// The full cross-section of the outer skin, from z = -5 m to the tail, corners 0 to 9 counterclockwise seen from +Z:
        /// the belly, bevels of 0.2 m at 45 degrees up to the sides, the sills at y = -1.2 m, 0.6 m above the cabin floor,
        /// bevels of 0.8 m at 45 degrees up to the roof, and the roof.
        /// </summary>
        private static readonly Vector2[] Full =
            [new(-2.8f, -2), new(2.8f, -2), new(3, -1.8f), new(3, -1.2f), new(3, 1.2f), new(2.2f, 2), new(-2.2f, 2), new(-3, 1.2f), new(-3, -1.2f), new(-3, -1.8f)];

        /// <summary>
        /// The nose's front, at z = -8 m, with the same corners: over the nose's last 3 m the roof falls from y = 2 m to 0.6 m
        /// and the sides close in from x = ±3 m to ±2.4 m, while the belly stays flat. So the front is a flat face 4.8 m wide
        /// and 2.6 m tall, and every edge of it is parallel to the same edge of the full cross-section.
        /// </summary>
        private static readonly Vector2[] NoseFront =
            [new(-2.2f, -2), new(2.2f, -2), new(2.4f, -1.8f), new(2.4f, -1.2f), new(2.4f, -0.2f), new(1.6f, 0.6f), new(-1.6f, 0.6f), new(-2.4f, -0.2f), new(-2.4f, -1.2f), new(-2.4f, -1.8f)];

        /// <summary>Where the nose's loft ends and the full cross-section starts, in meters along the shuttle's Z.</summary>
        private const float NoseStart = -5;

        /// <summary>
        /// The rib places, in meters along the shuttle's Z: every 1.5 m in the nose and every 2 m in the body. One lies at
        /// z = -1 m, where the pen's clip is, and the hatch's bay runs from 1 m to 3 m.
        /// </summary>
        private static readonly float[] Ribs = [-8, -6.5f, -5, -3, -1, 1, 3, 5, 7];

        /// <summary>
        /// The edges of the cross-sections that hold glass, with the names of their panes: the upper sides, the roof's
        /// bevels and the roof. The panes start 0.1 m above the sill, so the band runs from 0.7 m above the cabin floor
        /// over the roof.
        /// </summary>
        private static readonly (int Edge, string Name)[] GlassEdges = [(3, "starboard"), (4, "starboard-bevel"), (5, "roof"), (6, "port-bevel"), (7, "port")];

        /// <summary>The bay of the hatch, from z = 1 m to 3 m, where the starboard upper side is solid round the doorway.</summary>
        private const int HatchBay = 6;

        /// <summary>
        /// The shuttle's hull (docs/plans/blue-danube-demo.md, "The shuttle's tram shape and glass canopy"): the loft of
        /// <see cref="NoseFront"/> at the nose to <see cref="Full"/> at z = -5 m and on to the tail face, with the
        /// <see cref="Hatch"/> through the starboard side in bay 6, and a pocket 1.4 m square and 0.6 m deep for the main
        /// engine's nozzle in the tail face, on the line of thrust through the center of mass. The glass canopy has a pane
        /// in the front above the sill, <c>pane-nose</c>, and one in each cell of the five glass edges in bays 1 to 8, but
        /// the hatch's: 40 panes. Ribs and pillars are 0.2 m wide, mullions 0.12 m, and the glass stays 0.1 m from solid
        /// skin.
        /// </summary>
        public static LoftedHull Loft { get; } = Enumerable.Range(1, Ribs.Length - 1)
            .SelectMany(bay => GlassEdges.Where(glass => !(glass.Edge == 3 && bay == HatchBay)).Select(glass => (glass.Edge, glass.Name, Bay: bay)))
            .Aggregate(
                new LoftedHull([new HullSection(-Hull.Z, NoseFront), new HullSection(NoseStart, Full), new HullSection(Hull.Z, Full)], Wall, Ribs)
                    .Door(Hatch, bay: HatchBay, fromCorner: 2, toCorner: 4)
                    .TailPocket(half: 0.7f, depth: 0.6f, housing: Wall)
                    .Surrounds(rib: 0.1f, fold: 0.06f, solid: 0.1f)
                    .NoseGlass("pane-nose", fromCorner: 3, toCorner: 8),
                (hull, glass) => hull.Glass($"pane-{glass.Name}-{glass.Bay}", glass.Edge, glass.Bay));

        /// <summary>The hull in its parent frame: the convex hull of the loft's corners, with the default convex radius.</summary>
        public static ShapeDesc HullShape { get; } = Loft.Shape();

        /// <summary>
        /// The hull in its own frame: the drawn hull's solid in convex pieces of convex radius 0, solid at the glass and open at
        /// the hatch, and the <see cref="Seat"/>. Every piece is sharp, so physics gives the body edge removal.
        /// </summary>
        public static ShapeDesc InteriorShape { get; } = new CompoundShapeDesc(
            [.. Loft.Interior(), new CompoundChild(new BoxShapeDesc(Seat.Extents, ConvexRadius: 0), new Pose(Seat.Center, Quaternion.Identity))]);

        private static readonly HullMass LoftMass = Loft.MassOf(Mass);

        /// <summary>
        /// The hull's center of mass, in meters, in the shuttle's axes: where physics puts it for <see cref="HullShape"/> at
        /// one density, about 9 cm below and 36 cm aft of the hull's middle. The user chose one density on 2026-10-20, so no
        /// ballast moves it onto the middle line (docs/plans/blue-danube-demo.md, "Engine changes").
        /// </summary>
        public static Vector3 CenterOfMass { get; } = LoftMass.CenterOfMass;

        /// <summary>
        /// The hull's moments of inertia about its principal axes, in kg·m², and those axes in the shuttle's axes, as physics
        /// derives them for <see cref="HullShape"/> with <see cref="Mass"/>. The long axis tilts about 1 degree from Z.
        /// </summary>
        public static PrincipalInertia Inertia { get; } = LoftMass.Inertia;

        /// <summary>
        /// The main engine's nozzle: a ring from 0.5 m to 0.6 m in radius and 0.5 m long, about its own Y axis, in 32
        /// segments. It is drawn only, so its outside stays 0.1 m from the walls of the tail pocket, which reach 0.7 m from
        /// the pocket's middle.
        /// </summary>
        public static RevolvedSolid Nozzle { get; } = RevolvedSolid.Ring(0.5f, 0.6f, 0.25f, 32);

        /// <summary>
        /// Where the nozzle lies, in the shuttle's axes: its axis along Z through the center of mass, where the main engine
        /// pushes, and the tail pocket's middle, and its aft rim 5 cm inside the tail face, so from z = 7.45 m to 7.95 m. A
        /// nozzle off that line would show a thrust that turns the ship, and the ship does not turn.
        /// </summary>
        public static DPose NozzlePose { get; } =
            new(new Double3(CenterOfMass.X, CenterOfMass.Y, Hull.Z - 0.05 - 0.25), DQuaternion.FromAxisAngle(Double3.UnitX, Math.PI / 2));

        /// <summary>
        /// Spawns the nozzle at <see cref="NozzlePose"/> in <paramref name="cabin"/>, the shuttle's frame, drawn in dark gray,
        /// with no body. Its scene id is <c>nozzle</c>, after <paramref name="prefix"/> and a hyphen when the prefix is not
        /// empty, so that each shuttle of a scene has its own.
        /// </summary>
        public static Entity SpawnNozzle(World world, FrameHandle cabin, SceneInstance scene, IRenderResources resources, string prefix = "")
        {
            Entity nozzle = world.Spawn(new SpawnDesc
            {
                Frame = cabin,
                Pose = NozzlePose,
                Render = Drawn(resources, Nozzle.Mesh(), new Vector4(0.18f, 0.18f, 0.2f, 1), boundingRadius: 0.7f),
            });
            scene.Add(prefix.Length == 0 ? "nozzle" : $"{prefix}-nozzle", nozzle);
            return nozzle;
        }

        /// <summary>The hull's body with <paramref name="motion"/>: <see cref="HullShape"/>, <see cref="Mass"/>, no damping, and <see cref="InteriorShape"/>.</summary>
        public static BodyDesc Body(BodyMotion motion) =>
            new(HullShape, motion, Mass: Mass, LinearDamping: 0, AngularDamping: 0, InteriorShape: InteriorShape);

        /// <summary>
        /// Where a person 1.8 m tall stands at the seat, in the shuttle's axes: the center of its capsule, 0.9 m above the
        /// cabin floor, on the seat's line aft of it, with 0.15 m between the capsule, 0.3 m in radius, and the seat. A
        /// character has no sitting pose for now, so a person who leaves its seat walks away from this place.
        /// </summary>
        public static Double3 SeatStand { get; } = new(1, -Cabin.Y + 0.9, Seat.Max.Z + 0.45);

        /// <summary>
        /// Where a person 1.8 m tall stands 3 m outside the hatch, in the shuttle's axes: the center of its capsule, 0.9 m
        /// above the ground under the hull, in line with the hatch. A person who walks from there to
        /// <see cref="InsideHatch"/> walks straight through the middle of the hatch.
        /// </summary>
        public static Double3 OutsideHatch { get; } = new(Hull.X + 3, -Hull.Y + 0.9, Hatch.Center.Z);

        /// <summary>
        /// Where a person 1.8 m tall stands 1.2 m inside the hatch, in the shuttle's axes: the center of its capsule, 0.9 m
        /// above the cabin floor, in line with the hatch, with the whole capsule inside the cabin.
        /// </summary>
        public static Double3 InsideHatch { get; } = new(Cabin.X - 1.2, -Cabin.Y + 0.9, Hatch.Center.Z);

        /// <summary>
        /// The 40 panes of the canopy, from <see cref="Loft"/>, in the shuttle's axes: each pane's scene id, its hole at
        /// both skins, and its slab of glass round its center.
        /// </summary>
        public static IReadOnlyList<HullPane> Glass { get; } = Loft.Panes();

        /// <summary>
        /// The drawn hull of <see cref="Loft"/>, in light gray. It casts shadows and is never an impostor: a flat picture of
        /// the hull alone cannot hold the person inside at its depth, and the hull has few triangles. The scene draws the
        /// seat, the panes, the lamps and the nozzle apart.
        /// </summary>
        public static RenderData DrawnHull(IRenderResources resources)
        {
            RenderData render = Drawn(resources, Loft.Mesh(), new Vector4(0.85f, 0.85f, 0.88f, 1), boundingRadius: MathF.Ceiling(Hull.Length()));
            render.Flags |= RenderFlags.NoImpostor;
            return render;
        }

        /// <summary>
        /// The specular intensity of the canopy's glass: that of glass with an anti-reflection coating, which reflects about
        /// 0.5 % of the light that meets it head-on (F0 = 0.16 times its square, ARCHITECTURE.md, "Shading"), where the orbit
        /// scene's glass reflects 16 %. The window films look head-on through the glass, and the cabin's lamps lie in front
        /// of it. With 16 %, each lamp's highlight in the glass is a white blob beside the station. At a grazing angle, glass
        /// reflects much more, whatever its coating, so the highlights on the side and roof glass in the cabin views stay.
        /// </summary>
        private const float AntiReflective = 0.18f;

        /// <summary>
        /// Spawns each pane of <see cref="Glass"/> in <paramref name="cabin"/>, the shuttle's frame, at its center, with its
        /// scene id after <paramref name="prefix"/> and a hyphen when the prefix is not empty, so that each shuttle of a
        /// scene has its own: the color of the orbit scene's glass, mostly clear with a slight blue tint, with an
        /// anti-reflection coating (see <see cref="AntiReflective"/>). A pane has no body, because the interior shape is
        /// solid at the glass, and it casts no shadow, so sunlight comes in through the holes of the drawn hull. Each pane is
        /// its own instance round its own center, so the renderer sorts it at its real place.
        /// </summary>
        public static void Panes(World world, FrameHandle cabin, SceneInstance scene, IRenderResources resources, string prefix = "")
        {
            MaterialId glass = resources.CreateMaterial(new MaterialDesc(new Vector4(0.45f, 0.65f, 1, 0.2f), SpecularIntensity: AntiReflective, Gloss: 0.9f));
            foreach (HullPane pane in Glass)
            {
                Entity entity = world.Spawn(new SpawnDesc
                {
                    Frame = cabin,
                    Pose = new DPose(pane.Center, DQuaternion.Identity),
                    Render = new RenderData
                    {
                        Mesh = resources.CreateMesh(pane.Mesh),
                        Material = glass,
                        BoundingRadius = pane.Mesh.Vertices.Max(vertex => vertex.Position.Length()),
                        Flags = RenderFlags.Visible | RenderFlags.Transparent,
                    },
                });
                scene.Add(prefix.Length == 0 ? pane.Id : $"{prefix}-{pane.Id}", entity);
            }
        }

        /// <summary>
        /// The cabin's four lamps, each with its scene id and its glowing panel, in the shuttle's axes: on the inner faces
        /// of the pillars at z = -3 m and 3 m, on both walls, 2.4 m above the cabin floor. A panel is 0.05 m thick, 0.5 m tall
        /// and 0.15 m wide, within its pillar's 0.2 m, so the pillar hides it from outside. The roof is glass, so the lamps
        /// hang on the walls.
        /// </summary>
        public static IReadOnlyList<(string Id, BoundingBox Panel)> Lamps { get; } =
        [
            .. new[] { ("port", -1f), ("starboard", 1f) }.SelectMany(wall => new[] { ("fore", -3f), ("aft", 3f) }.Select(place =>
            {
                var center = new Vector3(wall.Item2 * (Cabin.X - LampHalf.X), -Cabin.Y + 2.4f, place.Item2);
                return (Id: $"lamp-{wall.Item1}-{place.Item1}", Panel: new BoundingBox(center - LampHalf, center + LampHalf));
            })),
        ];

        /// <summary>The half extents of a lamp's panel, in meters: 0.05 m thick across the wall, 0.5 m tall and 0.15 m wide.</summary>
        private static Vector3 LampHalf => new(0.025f, 0.25f, 0.075f);

        /// <summary>
        /// Spawns the <see cref="Lamps"/> in <paramref name="cabin"/>, the shuttle's frame, with their scene ids: each a
        /// warm glowing panel with a point light 0.3 m in front of it, toward the cabin's middle, of range 10 m and intensity
        /// 50. The four give the light of the two roof lamps of intensity 100 that the box hull had. A point light casts no
        /// shadow yet, so the lamps also light faces that the hull hides from them.
        /// </summary>
        public static void SpawnLamps(World world, FrameHandle cabin, SceneInstance scene, IRenderResources resources)
        {
            MeshId mesh = resources.CreateMesh(Primitives.Box(LampHalf));
            MaterialId material = resources.CreateMaterial(new MaterialDesc(new Vector4(0, 0, 0, 1), SpecularIntensity: 0, Emissive: new Vector3(6, 5.4f, 4.5f)));
            foreach ((string id, BoundingBox panel) in Lamps)
            {
                scene.Add(id, world.Spawn(new SpawnDesc
                {
                    Frame = cabin,
                    Pose = new DPose(panel.Center, DQuaternion.Identity),
                    Render = new RenderData
                    {
                        Mesh = mesh,
                        Material = material,
                        BoundingRadius = LampHalf.Length(),
                        Flags = RenderFlags.Visible,
                        Light = new PointLightData(new Pose(new Vector3(-MathF.Sign(panel.Center.X) * 0.3f, 0, 0), Quaternion.Identity), new Vector3(1, 0.9f, 0.75f), Intensity: 50, Range: 10),
                    },
                }));
            }
        }

        /// <summary>The radii of the bubble, in meters, in the shuttle's axes.</summary>
        public static Vector3 Bubble { get; } = (Hull + new Vector3(2)) * MathF.Sqrt(3);

        /// <summary>
        /// Spawns the shuttle parked at <paramref name="pose"/> in <paramref name="frame"/>, drawn with
        /// <paramref name="render"/>, with the <paramref name="components"/>, such as its <see cref="FlightControls"/>: its
        /// hull is static, so that a later <see cref="BodyHandle.SetMotion"/> unparks it.
        /// The cabin needs no value of its own for its weight. The engine derives every frame's proper acceleration from
        /// how the frame moved in the step (ARCHITECTURE.md, "How a frame moves"): a frame that holds still in a gravity
        /// field feels that gravity reversed, so the person in the parked cabin feels the weight of the ground. A new frame
        /// reads no proper acceleration during its first step, and the derived value from its second step on.
        /// </summary>
        public static Entity Park(World world, FrameHandle frame, DPose pose, RenderData? render = null, IReadOnlyList<IComponent>? components = null) => world.Spawn(new SpawnDesc
        {
            Frame = frame,
            Pose = pose,
            Body = Body(BodyMotion.Static),
            AsFrame = new FrameSpec(new FrameBounds(Bubble)),
            Render = render,
            Components = components ?? [],
        });
    }
}
