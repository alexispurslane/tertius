using System.Numerics;
using Tertius.Frames.Rails;
using Tertius.Host;
using Tertius.Simulation;

namespace Tertius.Demo;

/// <summary>
/// The scenes that scripts can name. The vertical slice adds its scenes here, and the content packages work later moves
/// them into the first content package.
/// </summary>
public static partial class DemoScenes
{
    /// <summary>Earth's mean radius, in meters.</summary>
    public const double EarthRadius = 6.371e6;

    /// <summary>
    /// The roughness of every painted part and lamp: the old default gloss of 0.5, converted so that each highlight keeps
    /// its size (docs/plans/pbr-brdf.md, "The demo materials").
    /// </summary>
    public const float UsualRoughness = 0.38f;

    /// <summary>
    /// The roughness of both glasses: their old gloss of 0.9, converted so that each highlight keeps its size
    /// (docs/plans/pbr-brdf.md, "The demo materials").
    /// </summary>
    public const float GlassRoughness = 0.18f;

    /// <summary>
    /// The top of Earth's air, in meters above <see cref="EarthRadius"/>: 100 km, the Kármán line. Only puffs feel the air
    /// (see <see cref="Air"/>), so nothing drags a ship below it. The demo keeps the periapsis of every orbit above it.
    /// </summary>
    public const double AtmosphereTop = 100e3;

    /// <summary>
    /// The top of the turning ground's bubble, in meters above <see cref="EarthRadius"/>: 50 km, the stratopause. The
    /// bubble of earth-surface ends there. A body leaves that bubble only when its center lies outside it by its bounding
    /// sphere plus the exit margin, 2 percent of the bubble's radius (ARCHITECTURE.md, "Hand-off", rule 2), so a shuttle
    /// that climbs out of it leaves 178.4 km up, below the demo's parking orbit at 200 km
    /// (docs/plans/blue-danube-demo.md, "The hand-off").
    /// </summary>
    public const double GroundBubbleTop = 50e3;

    /// <summary>Earth's G·M, in m³/s².</summary>
    public const double EarthMu = 3.986004418e14;

    /// <summary>The radius of the circle that the ship starts on, 400 km above the surface, in meters.</summary>
    public const double OrbitRadius = EarthRadius + 400e3;

    /// <summary>The tilt of the ship's orbit to Earth's equator, as the ISS flies: 51.64 degrees, in radians.</summary>
    public const double OrbitInclination = 51.64 * Math.PI / 180;

    /// <summary>The sun's G·M, in m³/s².</summary>
    public const double SunMu = 1.32712440018e20;

    /// <summary>The sun's radius, in meters.</summary>
    public const double SunRadius = 6.957e8;

    /// <summary>The astronomical unit, in meters.</summary>
    public const double AstronomicalUnit = 1.495978707e11;

    /// <summary>The Sun's radiant power, in W. It gives 1,361 W/m², the solar constant, at 1 AU.</summary>
    public const double SunPower = 3.828e26;

    /// <summary>
    /// The radius of every lamp's light, in meters: a small bulb. A surface is farther than this from each lamp, so the
    /// radius changes no lit pixel.
    /// </summary>
    private const float LampRadius = 0.05f;

    /// <summary>The radiant power of a cabin's lamp, in W: a panel of about 3,000 lumens.</summary>
    private const float CabinLampPower = 10;

    /// <summary>The radiant power of a floodlight of a station's bay, in W: about 30,000 lumens.</summary>
    private const float FloodlightPower = 100;

    /// <summary>The radiant power of a household bulb of 800 lumens, in W.</summary>
    private const float HouseholdBulbPower = 2.5f;

    /// <summary>The warm white of every lamp, which scales its power per channel.</summary>
    private static readonly Vector3 LampColor = new(1, 0.9f, 0.75f);

    /// <summary>
    /// The hemisphere ambient light of the scenes on Earth's surface, in W/(m²·sr): a sky of 110 and a ground of 40, the
    /// share of the Sun's 1,361 W/m² that the films on the ground were judged with, beside sunlight of 1 W/m².
    /// </summary>
    private static AmbientLight Daylight =>
        new()
        {
            SkyColor = new(110),
            GroundColor = new(40),
            Intensity = 1,
        };

    /// <summary>
    /// The ambient light of the scenes in space, in W/(m²·sr): the same dim light from every direction, since space has no
    /// sky and no ground. 16 is the share of the Sun's 1,361 W/m² that the orbit films were judged with, beside sunlight
    /// of 2.5 W/m².
    /// </summary>
    private static AmbientLight SpaceAmbient =>
        new()
        {
            SkyColor = new(16),
            GroundColor = new(16),
            Intensity = 1,
        };

    /// <summary>
    /// The black material of a lamp's panel whose face of <paramref name="area"/> m² sends a lamp's
    /// <paramref name="power"/> W of <see cref="LampColor"/>: it glows with the radiance color · P / (π A), in
    /// W/(m²·sr), of a flat emitter that looks the same from every side.
    /// </summary>
    private static MaterialId LampPanelMaterial(IRenderResources resources, float power, float area) =>
        resources.CreateMaterial(
            new(
                BaseColor: new(x: 0, y: 0, z: 0, w: 1),
                Roughness: UsualRoughness,
                Reflectance: 0,
                Emissive: LampColor * (power / (MathF.PI * area))
            )
        );

    /// <summary>
    /// A lamp's warm light of <paramref name="power"/> W at <paramref name="offset"/> from its panel, which casts shadows.
    /// Its panel leaves <see cref="RenderFlags.CastsShadow"/> off, so it does not darken its own light.
    /// </summary>
    private static PointLightData LampLight(Vector3 offset, float power) =>
        new(
            Offset: new(offset, Quaternion.Identity),
            Color: LampColor,
            Power: power,
            Radius: LampRadius,
            CastsShadow: true
        );

    /// <summary>
    /// A lamp under a cabin's roof at <paramref name="position"/> in <paramref name="frame"/>: a glowing panel of
    /// <paramref name="mesh"/> and <paramref name="material"/>, with a cabin lamp's light 10 cm below it.
    /// </summary>
    private static SpawnDesc CeilingLamp(FrameHandle frame, Double3 position, MeshId mesh, MaterialId material) =>
        new()
        {
            Frame = frame,
            Pose = new(position, DQuaternion.Identity),
            Render = new RenderData
            {
                Mesh = mesh,
                Material = material,
                BoundingRadius = 0.85f,
                Flags = RenderFlags.Visible,
                Light = LampLight(offset: new(0, -0.1f, 0), power: CabinLampPower),
            },
        };

    /// <summary>
    /// Five <see cref="CeilingLamp"/>s along the cabin's Z axis in <paramref name="frame"/>, <paramref name="spacing"/> m
    /// apart and centered on Z = 0, each a glowing panel 1.2 m square under the roof.
    /// </summary>
    private static void SpawnCabinLamps(World world, IRenderResources resources, FrameHandle frame, double spacing)
    {
        MeshId lampMesh = resources.CreateMesh(Primitives.Box(new(0.6f, 0.05f, 0.6f)));
        MaterialId lampMaterial = LampPanelMaterial(resources: resources, power: CabinLampPower, area: 1.2f * 1.2f);
        for (int i = -2; i <= 2; i++)
            world.Spawn(
                CeilingLamp(
                    frame: frame,
                    position: new(0, Cabin.Y - 0.05, spacing * i),
                    mesh: lampMesh,
                    material: lampMaterial
                )
            );
    }

    /// <summary>Earth's spin relative to the stars, one turn per sidereal day of 86,164.0905 s, in radians per second.</summary>
    public const double SiderealRate = 2 * Math.PI / 86_164.0905;

    /// <summary>The tilt of Earth's axis from the pole of its orbit: 23.4392811 degrees, in radians.</summary>
    public const double Obliquity = 23.4392811 * Math.PI / 180;

    /// <summary>The Moon's mean radius, in meters.</summary>
    public const double MoonRadius = 1.7374e6;

    /// <summary>The tilt of the Moon's orbit to the ecliptic, the plane of Earth's orbit: 5.145 degrees, in radians.</summary>
    public const double MoonInclination = 5.145 * Math.PI / 180;

    /// <summary>
    /// The margin, in meters, of the box that the ship's bubble holds: the hull box grown by this much on each side.
    /// The bubble is the smallest ellipsoid that holds that box (see <see cref="ShipBubble"/>), so it reaches past the box.
    /// It reaches about 10.8, 6.4 and 25.4 m past the hull along its axes. Its surface is at least this far from the hull
    /// everywhere, because the box holds every point that is this near the hull. It touches the box only at the box's
    /// corners, which are about 3.5 m from the hull's corners. In earth, the hull is on the layer
    /// <see cref="BodyLayer.FrameHull"/>, and loose bodies do not collide with it. A loose body in earth has its center
    /// outside the bubble, so at least this far from the hull, and a body up to 2 m in radius cannot touch the hull from
    /// earth.
    /// </summary>
    private const float BubbleShell = 2;

    /// <summary>The thickness of the hull's floor, roof and walls, in meters.</summary>
    private const float HullWall = 0.5f;

    /// <summary>How far aft of the ship's center the airlock stands, in meters: level with the person's start.</summary>
    private const float AirlockZ = 10;

    /// <summary>The thickness of the chamber's walls, in meters.</summary>
    private const float ChamberWall = 0.2f;

    public static IReadOnlyList<Scene> All { get; } =
    [
        new("drop-box", DropBox),
        new("orbit", Orbit),
        new("orbit-cluster", OrbitCluster),
        new("surface-yard", SurfaceYard),
        new("deep-yard", DeepYard.Build),
        new("doorway", Doorway),
        new("station", Station.Build),
        new("weightless-push", WeightlessPush),
        new("demo-meshes", DemoMeshes),
        new("demo-shuttle", DemoShuttle),
        new("demo-surface", DemoSurface.Build),
        new("spin-station", SpinStation.Build),
        new("earth-system", EarthSystem.Build),
        new("demo-exit", DemoExit.Build),
        new("puff-yard", PuffYard),
        new("pbr-spheres", PbrSpheres),
        new("reflections", Reflections),
        new("mirror-balls", MirrorBalls),
        .. ShadowScenes,
    ];

    /// <summary>
    /// The circle that the ship of the orbit scene starts on, in earth: radius <see cref="OrbitRadius"/>, tilted by
    /// <see cref="OrbitInclination"/>. Its state at the world's time places the ship. The ship faces Earth with its belly
    /// and turns once per orbit, so its axes stay those of the orbit: +Y up from Earth, -Z along the motion.
    /// </summary>
    public static KeplerOrbit ShipOrbit { get; } =
        KeplerOrbit.Circular(OrbitRadius) with
        {
            Inclination = OrbitInclination,
            Attitude = KeplerAttitude.FacesParent,
        };

    /// <summary>
    /// The Moon's mean orbit round Earth, in earth's axes: semi-major axis 384,400 km, eccentricity 0.0549, tilted
    /// <see cref="MoonInclination"/> to the ecliptic. Its ascending node on the ecliptic lies on the March equinox direction,
    /// earth's +X, as at the major lunar standstill of 2024 and 2025. Earth's axes lean from the ecliptic by
    /// <see cref="Obliquity"/> about that same line, so the two tilts add: the orbit leans 28.58 degrees from Earth's
    /// equator, with its node on +X. Its perigee lies on the node. At time 0 the Moon stands 45 degrees past it, toward
    /// -Z and north, which at the June solstice is 45 degrees from the Sun as seen from Earth: a waning crescent, three or
    /// four days before new Moon, so that from beside the Moon, Earth shows mostly its day side. Its attitude does not
    /// turn. The Moon has no gravity of its own: only Earth's pulls in earth, so the orbit takes earth's G·M,
    /// <see cref="EarthMu"/>.
    /// </summary>
    public static KeplerOrbit MoonOrbit { get; } =
        new(
            SemiMajorAxis: 3.844e8,
            Eccentricity: 0.0549,
            Inclination: Obliquity + MoonInclination,
            LongitudeOfAscendingNode: 0,
            ArgumentOfPeriapsis: 0,
            MeanAnomalyAtEpoch: MeanAnomaly(Math.PI / 4, 0.0549),
            Epoch: 0,
            AxialTilt: DQuaternion.Identity,
            SpinRate: 0,
            SpinPhaseAtEpoch: 0
        );

    /// <summary>The half extents of the ship's hull, in meters: 20 m wide, 8 m high and 60 m long.</summary>
    private static readonly Vector3 Hull = new(10, 4, 30);

    /// <summary>
    /// The radii of the ship's bubble along the ship's axes, in meters: about 20.8, 10.4 and 55.4. The smallest ellipsoid
    /// that holds a box has the box's half extents times sqrt(3), and touches the box at its corners. So this ellipsoid
    /// holds the hull with a shell of <see cref="BubbleShell"/> around it.
    /// </summary>
    private static readonly Vector3 ShipBubble = (Hull + new Vector3(BubbleShell)) * MathF.Sqrt(3);

    /// <summary>The half extents of the cabin, in meters: the hull less its walls.</summary>
    private static readonly Vector3 Cabin = Hull - new Vector3(HullWall);

    /// <summary>The airlock's chamber, in the ship's axes: against the port wall, 3 m deep and 3 m wide, as tall as the cabin.</summary>
    private static readonly BoundingBox Chamber = new(
        new(-Cabin.X, -Cabin.Y, AirlockZ - 1.5f),
        new(-Cabin.X + 3, Cabin.Y, AirlockZ + 1.5f)
    );

    /// <summary>
    /// The hatch through the hull's port wall, from the airlock's chamber to the outside, in the ship's axes, in meters:
    /// a doorway as thick as the hull's wall. The door <c>airlock-outer</c> closes it.
    /// </summary>
    public static BoundingBox Hatch { get; } = Doorway(-Hull.X, -Cabin.X);

    /// <summary>
    /// The doorway through the chamber's inner wall, from the cabin into the airlock's chamber, in the ship's axes, in
    /// meters: a doorway as thick as the chamber's wall. The door <c>airlock-inner</c> closes it.
    /// </summary>
    public static BoundingBox InnerDoorway { get; } = Doorway(Chamber.Max.X, Chamber.Max.X + ChamberWall);

    /// <summary>The chamber's two side walls, from the port wall to the cabin side of its inner wall.</summary>
    private static readonly BoundingBox[] ChamberSides =
    [
        new(new(-Cabin.X, -Cabin.Y, Chamber.Min.Z - ChamberWall), new(InnerDoorway.Max.X, Cabin.Y, Chamber.Min.Z)),
        new(new(-Cabin.X, -Cabin.Y, Chamber.Max.Z), new(InnerDoorway.Max.X, Cabin.Y, Chamber.Max.Z + ChamberWall)),
    ];

    /// <summary>The chamber's inner wall, between its side walls, which the inner doorway goes through.</summary>
    private static readonly BoundingBox ChamberInnerWall = new(
        new(InnerDoorway.Min.X, -Cabin.Y, Chamber.Min.Z),
        new(InnerDoorway.Max.X, Cabin.Y, Chamber.Max.Z)
    );

    /// <summary>
    /// The places of the windows along each side wall, in meters along the ship's Z: one every 6 m from 24 m ahead of the
    /// ship's center to 24 m aft of it. A pane's scene id numbers its place from the nose, from 1 to 9.
    /// </summary>
    private static readonly float[] SideWindowZ = [-24, -18, -12, -6, 0, 6, 12, 18, 24];

    /// <summary>
    /// The windows through the hull, in the ship's axes, with the scene ids of their panes: a row in each side wall, and
    /// one window in the nose wall, 6 m wide and 2 m tall, centered at eye height. The port wall leaves out the window that
    /// would cut into the airlock's chamber or its walls, 12 m aft. Physics keeps the walls solid, so the interior shape
    /// has no hole at a window, and the glass blocks a person as a wall does.
    /// </summary>
    public static IReadOnlyList<(string Pane, Opening Window)> Windows { get; } =
    [
        .. SideWindowZ
            .Select((z, i) => (Pane: $"pane-port-{i + 1}", Window: Opening.ThroughX(SideWindow(-Hull.X, -Cabin.X, z))))
            .Where(port =>
                port.Window.Bounds.Max.Z < ChamberSides[0].Min.Z || port.Window.Bounds.Min.Z > ChamberSides[1].Max.Z
            ),
        .. SideWindowZ.Select(
            (z, i) => (Pane: $"pane-starboard-{i + 1}", Window: Opening.ThroughX(SideWindow(Cabin.X, Hull.X, z)))
        ),
        (
            Pane: "pane-nose",
            Window: Opening.ThroughZ(new(new(-3, -Cabin.Y + 0.65f, -Hull.Z), new(3, -Cabin.Y + 2.65f, -Cabin.Z)))
        ),
    ];

    /// <summary>
    /// The vertical slice's first scene: a ship falls freely round an Earth that goes round the sun, at the March equinox.
    /// <list type="bullet">
    /// <item>The sun is the root frame <c>sun</c>, with the sun's gravity, drawn with its real radius. Its light, a point
    /// light of the Sun's radius at its center, sends 2.5 W/m² to Earth and casts shadows. Its sphere casts none. Its +Y
    /// is the pole of Earth's orbit, and its +X points from the sun to Earth's place at the September equinox, so the sun,
    /// seen from Earth, stands toward +X at the March equinox.</item>
    /// <item>The frame <c>earth</c> goes round the sun on Earth's real orbit (1.00000261 AU, eccentricity 0.01671123,
    /// perihelion at longitude 102.94 degrees). It does not turn: it leans by Earth's 23.44 degree obliquity, so its +Y is
    /// Earth's north pole and its +X is the March equinox direction, as for the real Earth-centered inertial frame. It
    /// holds Earth's gravity. The ship falls round Earth in it.</item>
    /// <item>The frame <c>earth-surface</c> sits at Earth's center and turns about Earth's pole once per sidereal day. It
    /// draws the planet: Earth's radius, with a grid of 10-degree lines, so its turn shows. It casts Earth's shadow, and
    /// it has Earth's gravity for objects on the ground.</item>
    /// <item>The <c>moon</c> goes round Earth in <c>earth</c> on <see cref="MoonOrbit"/>, at its true size, with no gravity
    /// of its own.</item>
    /// <item>The frame <c>ship</c>, a 20 by 8 by 60 m hull, falls freely in Earth's gravity: Jolt integrates it, with no
    /// damping. Its bubble is an ellipsoid with radii of about 20.8, 10.4 and 55.4 m along its axes: the smallest that holds
    /// the hull grown by 2 m on every side, so its surface is at least 2 m from the hull everywhere. It starts on a circle 400 km up, crossing the equator northward at +X, tilted 51.64
    /// degrees, with its belly toward Earth and its nose along its motion. Its spin, one turn per orbit, keeps it so. At
    /// the start the sun stands straight above it. In Earth's frame its hull is a solid box. In its own frame the hull
    /// is hollow: walls 0.5 m thick around a cabin 19 by 7 by 59 m. Only the cabin has gravity: 9.81 m/s² toward the
    /// belly. Five lamps under the roof light the cabin, because the hull keeps the sunlight out, except through the
    /// windows when the sun stands on their side.</item>
    /// <item>The hull has 18 windows (see <see cref="Windows"/>). Each side wall has a row of them at eye height, 2 m wide
    /// and 1.2 m tall, one every 6 m from 24 m ahead of the ship's center to 24 m aft of it. The port wall leaves out the
    /// one 12 m aft, which would cut into the airlock's chamber. The nose wall has one window 6 m wide and 2 m tall. Each
    /// window holds a pane of glass, <c>pane-port-1</c> to <c>pane-port-9</c> (with no 7), <c>pane-starboard-1</c> to
    /// <c>pane-starboard-9</c>, numbered from the nose, and <c>pane-nose</c>. A pane is a thin box in the middle of its
    /// wall, transparent, with a slight blue tint, and it casts no shadow. The hull casts shadows, so sunlight comes in
    /// through a window when the sun stands on its side. Physics keeps the walls solid at the windows, so the glass stops a
    /// thrown crate as a wall does. A person, 1.8 m tall, does not fit through a window anyway.</item>
    /// <item>The <c>airlock</c> is a chamber 3 by 3 m against the port wall, 10 m aft of the ship's center. The door
    /// <c>airlock-inner</c> leads from the cabin into the chamber, and the door <c>airlock-outer</c> closes a hatch from
    /// the chamber to the outside. Both doorways are 1.2 m wide and 2.1 m tall, with a sill 0.2 m high. The inner door
    /// spawns shut, and the airlock opens it when the scene starts, which takes 1.5 s. A use cycles it: the open door
    /// closes in 1.5 s, and 3 s after it began to close, the other door opens.</item>
    /// <item>The <c>person</c>, a capsule character 1.8 m tall, stands on the cabin floor 10 m aft of the ship's center and
    /// faces the nose. The airlock's inner door is 6.4 m to its left. Its eye, 1.65 m above the floor, is a view. While
    /// that view is on, <see cref="PersonControl"/> steers it and turns its view from the <c>input</c> topic. Wherever
    /// physics does not stand the person on walkable ground, the person is weightless and its thrusters push it: in a jump,
    /// outside the cabin, and past the ship's bubble. There, a hand-off moves the person into <c>earth</c>, where it falls
    /// freely with the ship, once its center lies 2 m outside the bubble: its bounding sphere of 0.9 m and a margin of
    /// 1.1 m. Straight out of the hatch, that is about 24.1 m from the ship's center. It comes back into the ship's frame
    /// when its center lies 0.9 m outside the bubble, 23.2 m from the ship's center. With <see cref="Carry"/>, E also picks
    /// up a crate within reach, and the left mouse button throws it.</item>
    /// <item>Four green <see cref="Crate"/>s, 0.4 m on a side and of 5 kg, lie loose on the floor. Three of them,
    /// <c>crate-1</c> to <c>crate-3</c>, lie aft of the person and to starboard, out of its reach and its way.
    /// <c>chamber-crate</c> lies in the airlock's chamber, in the corner by the hatch on the aft side. A person that
    /// presses E in the middle of the chamber cycles the airlock and picks up that crate with one press.</item>
    /// <item>The view <c>chase</c>, the camera at the start, rides in the ship's frame, 70 m behind it and 12 m above it,
    /// and looks at the ship's center. It is outside the ship's bubble, but it has no body, so no hand-off moves it out of
    /// the ship's frame.</item>
    /// <item>The view <c>far</c> stands in <c>earth</c>, three Earth radii from its center, 30 degrees north of the
    /// equator, and looks at the center. It shows Earth's surface turn. The ship is too small to see from there.</item>
    /// </list>
    /// The scene places everything for the world's current time, so a world that starts later is consistent too.
    /// </summary>
    private static SceneInstance Orbit(World world, IRenderResources resources)
    {
        var scene = new SceneInstance();
        FrameHandle earth = SunAndEarth(world: world, resources: resources, scene: scene, date: EarthDate.MarchEquinox);

        // The circle that the ship starts on gives its state at the world's time. From there, gravity alone moves it. The
        // circle takes the G·M of earth's own field, which is the G·M that pulls the ship.
        // Stryker disable once String : the scene gives earth point gravity above, so nothing reaches this guard of its own construction.
        double earthMu = world.Frames.Gravity(earth) is PointGravity { Mu: var mu }
            ? mu
            : throw new InvalidOperationException("Earth has no point gravity.");
        KeplerOrbit start = ShipOrbit;
        (Double3 position, Double3 velocity) = start.StateAt(world.Time, earthMu);
        Entity ship = world.Spawn(
            new()
            {
                Frame = earth,
                Pose = new(position, start.RotationAt(world.Time, earthMu)),
                LinearVelocity = velocity,
                AngularVelocity = start.AngularVelocityAt(world.Time, earthMu).ToVector3(),
                // In Earth's frame the hull is a solid box. Inside the ship's own frame it is hollow: the person walks on its
                // floor, and goes out through the airlock along its outer faces.
                Body = new(
                    Shape: new BoxShapeDesc(Hull),
                    Motion: BodyMotion.Dynamic,
                    Mass: 1e5f,
                    LinearDamping: 0,
                    AngularDamping: 0,
                    InteriorShape: new CompoundShapeDesc([
                        .. ShipInterior()
                            .Select(box => new CompoundChild(
                                new BoxShapeDesc(box.Extents),
                                new(box.Center, Quaternion.Identity)
                            )),
                    ])
                ),
                // The bubble is the smallest ellipsoid that holds the hull grown by 2 m (see ShipBubble), so a person or a crate
                // that leaves the ship passes into earth near it. The ship falls freely, so only its own field acts inside it: one standard gravity toward its
                // belly, in the cabin alone.
                AsFrame = new(new(ShipBubble), GravityField.Uniform(new(0, -9.81f, 0), new BoundingBox(-Cabin, Cabin))),
                Render = Drawn(
                    resources: resources,
                    mesh: HullMesh(),
                    color: new(x: 0.8f, y: 0.8f, z: 0.82f, w: 1),
                    boundingRadius: 32
                ),
            }
        );
        scene.Add("ship", ship);
        FrameHandle shipFrame = world.GetOwnedFrame(ship);

        // Five lamps under the roof, 12 m apart, light the cabin, because the hull keeps the sunlight out, except through the
        // windows when the sun stands on their side. Each casts shadows within the renderer's budget.
        SpawnCabinLamps(world: world, resources: resources, frame: shipFrame, spacing: 12);

        // A pane of glass in each window: mostly clear, with a slight blue tint. It casts no shadow, so sunlight comes in
        // through a window when the sun stands on its side. It has no body, because the walls of the interior shape are
        // solid at the windows.
        MaterialId glass = resources.CreateMaterial(
            new(BaseColor: new(x: 0.45f, y: 0.65f, z: 1, w: 0.2f), Roughness: GlassRoughness, Reflectance: 1)
        );
        var paneMeshes = new Dictionary<Vector3, MeshId>();
        foreach ((string pane, Opening window) in Windows)
            scene.Add(
                pane,
                world.Spawn(
                    PaneIn(frame: shipFrame, window: window, glass: glass, meshes: paneMeshes, resources: resources)
                )
            );

        // The airlock's doors slide up into the wall above their doorways. Each is 0.1 m thick, in the middle of its wall.
        Entity innerDoor = world.Spawn(DoorIn(shipFrame, InnerDoorway, resources));
        Entity outerDoor = world.Spawn(DoorIn(shipFrame, Hatch, resources));
        scene.Add("airlock-inner", innerDoor);
        scene.Add("airlock-outer", outerDoor);
        // A person uses the airlock within reach of the chamber's center, at the height of a standing person's center.
        scene.Add(
            "airlock",
            world.Spawn(
                new()
                {
                    Frame = shipFrame,
                    Pose = new(new(Chamber.Center.X, -Cabin.Y + 0.95, AirlockZ), DQuaternion.Identity),
                    Components = [new Airlock(innerDoor: innerDoor, outerDoor: outerDoor, cycleSeconds: 3)],
                }
            )
        );

        // The person stands on the cabin floor, 10 m aft of the ship's center, and faces the nose. The capsule is 1.8 m
        // tall, and its center rests 0.9 m above the floor. The eye is 1.65 m above the floor.
        var capsule = new CapsuleShapeDesc(0.6f, 0.3f);
        scene.Add(
            "person",
            world.Spawn(
                new()
                {
                    Frame = shipFrame,
                    Pose = new(new(0, -Cabin.Y + 0.95, AirlockZ), DQuaternion.Identity),
                    Body = new(Shape: capsule, Motion: BodyMotion.Character, Mass: 70),
                    Render = Drawn(
                        resources: resources,
                        mesh: Primitives.Box(new(0.3f, 0.9f, 0.3f)),
                        color: new(x: 0.2f, y: 0.45f, z: 0.8f, w: 1),
                        boundingRadius: 1
                    ),
                    Components = [new PersonControl(new(0, 0.75f, 0)), new Carry(capsule)],
                    Subscriptions = [Topics.Input],
                }
            )
        );

        // Green crates, 0.4 m on a side and of 5 kg, which the person can carry and throw. Three lie loose on the cabin
        // floor, aft of the person and to starboard, away from its ways in the other scripts and out of its reach where it
        // presses E. One lies in the airlock's chamber, in the corner by the hatch on the aft side, beside the person's way
        // in. The person presses E in the middle of the chamber, so it reaches that crate and the airlock with one press.
        var crate = new CrateSpec(
            HalfExtents: new(0.2f),
            Mass: 5,
            Mesh: resources.CreateMesh(Primitives.Box(new(0.2f))),
            Material: resources.CreateMaterial(
                MaterialDesc.Color(new(x: 0.3f, y: 0.6f, z: 0.25f, w: 1), UsualRoughness)
            )
        );
        double onTheFloor = -Cabin.Y + crate.HalfExtents.Y;

        DPose OnTheFloor(double x, double z, double turn) =>
            new(new(x, onTheFloor, z), DQuaternion.FromAxisAngle(Double3.UnitY, turn));

        scene.Add("crate-1", world.Spawn(crate.Spawn(shipFrame, OnTheFloor(3, 14, 0), Vector3.Zero)));
        scene.Add("crate-2", world.Spawn(crate.Spawn(shipFrame, OnTheFloor(3.6, 14.5, 0.4), Vector3.Zero)));
        scene.Add("crate-3", world.Spawn(crate.Spawn(shipFrame, OnTheFloor(3.1, 15.2, -0.6), Vector3.Zero)));
        scene.Add(
            "chamber-crate",
            world.Spawn(crate.Spawn(shipFrame, OnTheFloor(Chamber.Min.X + 0.6, Chamber.Max.Z - 0.5, 0), Vector3.Zero))
        );

        Entity chase = world.Spawn(
            new()
            {
                Frame = shipFrame,
                Pose = new(new(0, 12, 70), DQuaternion.FromAxisAngle(new(1, 0, 0), -Math.Atan2(12, 70))),
                Render = new RenderData(),
            }
        );
        scene.Add("chase", chase);

        Entity far = world.Spawn(
            new()
            {
                Frame = earth,
                Pose = new(
                    new(0, 3 * EarthRadius * Math.Sin(Math.PI / 6), 3 * EarthRadius * Math.Cos(Math.PI / 6)),
                    DQuaternion.FromAxisAngle(new(1, 0, 0), -Math.PI / 6)
                ),
                Render = new RenderData(),
            }
        );
        scene.Add("far", far);
        scene.AddView("chase");
        scene.AddView("far");
        scene.AddView("person");
        world.Camera = chase;
        return scene;
    }

    /// <summary>
    /// Where Earth stands on its orbit and how far the ground is turned, at the world's time 0. The longitude gives the
    /// season, and the ground's turn the time of day.
    /// </summary>
    /// <param name="Longitude">
    /// Earth's longitude, the angle in radians about the Sun's +Y from the Sun's +X, Earth's place at the September equinox,
    /// the way Earth moves, from +X toward -Z.
    /// </param>
    /// <param name="GroundTurn">
    /// The ground's turn, the angle in radians about earth's +Y, the north pole, from earth's axes to the axes of
    /// <c>earth-surface</c>.
    /// </param>
    public readonly record struct EarthDate(double Longitude, double GroundTurn)
    {
        /// <summary>The March equinox: Earth at longitude 180 degrees, on the Sun's -X, so the Sun stands toward +X from Earth, over the equator.</summary>
        public static EarthDate MarchEquinox => new(Math.PI, 0);

        /// <summary>
        /// The June solstice: Earth at longitude 270 degrees, on the Sun's +Z, so the Sun stands toward -Z from Earth, the
        /// way the north pole leans, 23.44 degrees north of the equator. At the north pole it stands that high all day.
        /// </summary>
        public static EarthDate JuneSolstice => new(3 * Math.PI / 2, 0);

        /// <summary>
        /// The spin of <c>earth-surface</c> in <c>earth</c>: the turn <see cref="GroundTurn"/> about +Y at the world's time 0,
        /// and from there the sidereal turn, <see cref="SiderealRate"/>, about +Y.
        /// </summary>
        public FrameSpin Ground => new(Tilt: DQuaternion.Identity, Rate: SiderealRate, PhaseAtEpoch: GroundTurn);
    }

    /// <summary>
    /// Earth's orbit round the Sun, in the Sun's axes, with Earth at <paramref name="date"/> at time 0: Earth's real orbit
    /// (1.00000261 AU, eccentricity 0.01671123, perihelion at longitude 102.94 degrees), with an attitude that does not
    /// turn and leans by <see cref="Obliquity"/>, so that earth's +Y is Earth's north pole and its +X is the March equinox
    /// direction.
    /// </summary>
    public static KeplerOrbit EarthOrbit(EarthDate date)
    {
        // The pole leans from the sun's +Y toward -Z, the direction of the sun from Earth at the June solstice.
        var obliquity = DQuaternion.FromAxisAngle(Double3.UnitX, -Obliquity);
        const double eccentricity = 0.01671123,
            perihelion = 102.93768193 * Math.PI / 180;
        double meanAnomaly = MeanAnomaly(date.Longitude - perihelion, eccentricity);
        return new(
            SemiMajorAxis: 1.00000261 * AstronomicalUnit,
            Eccentricity: eccentricity,
            Inclination: 0,
            LongitudeOfAscendingNode: 0,
            ArgumentOfPeriapsis: perihelion,
            MeanAnomalyAtEpoch: meanAnomaly,
            Epoch: 0,
            AxialTilt: obliquity,
            SpinRate: 0,
            SpinPhaseAtEpoch: 0
        );
    }

    /// <summary>
    /// The Earth system of every scene that has the Sun: the sun, earth, earth-surface and the moon of the orbit scene (see
    /// <see cref="Orbit"/>), added to <paramref name="scene"/>,
    /// with the sunlight and the dim light of space, with Earth at <paramref name="date"/> at the world's time 0: Earth on
    /// its orbit at the date's longitude, and earth-surface on the date's <see cref="EarthDate.Ground"/>. Returns earth's
    /// frame. earth-surface holds Earth's air (see <see cref="Air.Earth"/>), and earth and the sun hold none. The sun's
    /// root entity holds the sunlight (see <see cref="SunRoot"/>).
    /// </summary>
    private static FrameHandle SunAndEarth(World world, IRenderResources resources, SceneInstance scene, EarthDate date)
    {
        KeplerOrbit earthOrbit = EarthOrbit(date);
        Double3 earthPlace = earthOrbit.StateAt(world.Time, SunMu).Position;
        FrameHandle sun = SunRoot(world, resources);
        scene.Add("sun", sun.Entity);
        world.Ambient = SpaceAmbient;

        // Earth starts on its orbit at the world's time. Its first motion intent moves it along the orbit in the step of the
        // next tick, and its KeplerMotion from the second step on. So earth falls freely from its first step, and the
        // station of the station scene, which reads earth's proper acceleration, feels none of the sun's gravity. Earth has
        // no body, so only its motion intents move it, and it spawns with no velocity: its first sync point gives its frame
        // the velocities of its first step.
        double firstStepEnd = world.TimeAt(world.TickNumber + 1);
        Entity earthOwner = world.Spawn(
            new()
            {
                Frame = sun,
                Pose = new(earthPlace, earthOrbit.RotationAt(world.Time, SunMu)),
                FirstMove = new(earthOrbit.Sample(SunMu, firstStepEnd, world.DeltaTime)),
                AsFrame = new(FrameBounds.Sphere(9.25e8f), GravityField.Point(EarthMu)),
                Components = [new KeplerMotion { Orbit = earthOrbit }],
            }
        );
        scene.Add("earth", earthOwner);
        FrameHandle earth = world.GetOwnedFrame(earthOwner);

        scene.Add(
            "earth-surface",
            SpawnEarthSurface(
                world: world,
                resources: resources,
                earth: earth,
                turning: date.Ground,
                firstStepEnd: firstStepEnd
            )
        );
        scene.Add("moon", SpawnMoon(world: world, resources: resources, earth: earth, firstStepEnd: firstStepEnd));
        return earth;
    }

    /// <summary>
    /// The sun's root frame and entity: a point light of the Sun's power and radius that casts shadows, and a sphere that
    /// glows with the light's own surface radiance.
    /// </summary>
    private static FrameHandle SunRoot(World world, IRenderResources resources)
    {
        var sunlight = new PointLightData(
            Offset: new(Vector3.Zero, Quaternion.Identity),
            Color: Vector3.One,
            Power: (float)SunPower,
            Radius: (float)SunRadius,
            CastsShadow: true
        );
        return world.CreateRootFrame(
            new(FrameBounds.Sphere(1e13f), GravityField.Point(SunMu)),
            new RenderData
            {
                Mesh = resources.CreateMesh(Primitives.Sphere((float)SunRadius, 64, 32)),
                // Black, so that only the emission shows: the sun lights the scene through its light, not through its
                // sphere.
                Material = resources.CreateMaterial(
                    new(
                        BaseColor: new(x: 0, y: 0, z: 0, w: 1),
                        Roughness: UsualRoughness,
                        Reflectance: 0,
                        Emissive: sunlight.SurfaceRadiance
                    )
                ),
                BoundingRadius = (float)SunRadius,
                Flags = RenderFlags.Visible | RenderFlags.Celestial,
                Light = sunlight,
            }
        );
    }

    /// <summary>
    /// The earth-surface entity in <paramref name="earth"/>: Earth's sphere and the frame of its ground and air, which
    /// turns by <paramref name="turning"/> and moves from the step that ends at <paramref name="firstStepEnd"/>.
    /// </summary>
    private static Entity SpawnEarthSurface(
        World world,
        IRenderResources resources,
        FrameHandle earth,
        FrameSpin turning,
        double firstStepEnd
    ) =>
        // The ground turns with the sidereal day, from the date's ground turn at time 0, and from its first step, as every
        // frame on rails in these scenes moves from its first step.
        world.Spawn(
            new()
            {
                Frame = earth,
                Pose = new(Double3.Zero, turning.RotationAt(world.Time)),
                FirstMove = new(turning.Sample(Double3.Zero, firstStepEnd)),
                // The bubble ends at the stratopause, so that a shuttle that climbs out of it leaves it below its parking orbit.
                AsFrame = new(
                    FrameBounds.Sphere((float)(EarthRadius + GroundBubbleTop)),
                    GravityField.Point(EarthMu),
                    Air.Earth
                ),
                Components = [new SpinMotion { Spin = turning }],
                Render = new RenderData
                {
                    Mesh = resources.CreateMesh(Primitives.Sphere((float)EarthRadius, 256, 128)),
                    Material = resources.CreateMaterial(
                        new(
                            BaseColor: Vector4.One,
                            Roughness: UsualRoughness,
                            Reflectance: 0.1f,
                            BaseColorMap: resources.CreateTexture(EarthGrid())
                        )
                    ),
                    BoundingRadius = (float)EarthRadius,
                    Flags = RenderFlags.Visible | RenderFlags.Celestial | RenderFlags.CastsShadow,
                },
            }
        );

    /// <summary>
    /// The moon entity in <paramref name="earth"/>, on its orbit from the world's time, which moves from the step that
    /// ends at <paramref name="firstStepEnd"/>.
    /// </summary>
    private static Entity SpawnMoon(World world, IRenderResources resources, FrameHandle earth, double firstStepEnd)
    {
        // The Moon goes round Earth from its place at the world's time, and from its first step, as earth does. It has no
        // body and no frame, the least that a KeplerMotion moves, so only its motion intents move it, and it spawns with no
        // velocity, as earth does.
        Double3 moonPlace = MoonOrbit.StateAt(world.Time, EarthMu).Position;
        return world.Spawn(
            new()
            {
                Frame = earth,
                Pose = new(moonPlace, MoonOrbit.RotationAt(world.Time, EarthMu)),
                FirstMove = new(MoonOrbit.Sample(EarthMu, firstStepEnd, world.DeltaTime)),
                Components = [new KeplerMotion { Orbit = MoonOrbit }],
                Render = new RenderData
                {
                    Mesh = resources.CreateMesh(Primitives.Sphere((float)MoonRadius, 128, 64)),
                    Material = resources.CreateMaterial(
                        new(
                            BaseColor: new(x: 0.55f, y: 0.55f, z: 0.53f, w: 1),
                            Roughness: UsualRoughness,
                            Reflectance: 0.05f
                        )
                    ),
                    BoundingRadius = (float)MoonRadius,
                    Flags = RenderFlags.Visible | RenderFlags.Celestial,
                },
            }
        );
    }

    /// <summary>
    /// A doorway of the airlock through a wall across X, from <paramref name="x0"/> to <paramref name="x1"/>: 1.2 m wide and
    /// 2.1 m tall, with a sill 0.2 m high that the person steps over.
    /// </summary>
    private static BoundingBox Doorway(float x0, float x1) =>
        new(new(x0, -Cabin.Y + 0.2f, AirlockZ - 0.6f), new(x1, -Cabin.Y + 2.3f, AirlockZ + 0.6f));

    /// <summary>
    /// A window through a side wall across X, from <paramref name="x0"/> to <paramref name="x1"/>, centered on
    /// <paramref name="z"/>: 2 m wide and 1.2 m tall, centered at the eye height of a person on the floor, 1.65 m.
    /// </summary>
    private static BoundingBox SideWindow(float x0, float x1, float z) =>
        new(new(x0, -Cabin.Y + 1.05f, z - 1), new(x1, -Cabin.Y + 2.25f, z + 1));

    /// <summary>
    /// The boxes of the hull's interior shape, which physics uses inside the ship's own frame, in the ship's axes, in
    /// meters. The floor and the roof span the whole hull, the side walls stand between them, and the end walls close
    /// the cabin between the side walls. The port wall leaves <see cref="Hatch"/> open. The chamber's walls stand on the
    /// cabin floor, and its inner wall leaves <see cref="InnerDoorway"/> open. The boxes touch without gaps or overlaps.
    /// </summary>
    public static IEnumerable<BoundingBox> ShipInterior()
    {
        var hull = new BoundingBox(-Hull, Hull);
        yield return hull with
        {
            Max = Hull with { Y = -Cabin.Y },
        };
        yield return hull with
        {
            Min = -Hull with { Y = Cabin.Y },
        };
        yield return new(new(Cabin.X, -Cabin.Y, -Hull.Z), new(Hull.X, Cabin.Y, Hull.Z));
        foreach (
            BoundingBox part in AroundDoorway(
                new(new(-Hull.X, -Cabin.Y, -Hull.Z), new(-Cabin.X, Cabin.Y, Hull.Z)),
                Hatch
            )
        )
            yield return part;
        yield return new(new(-Cabin.X, -Cabin.Y, -Hull.Z), new(Cabin.X, Cabin.Y, -Cabin.Z));
        yield return new(new(-Cabin.X, -Cabin.Y, Cabin.Z), new(Cabin.X, Cabin.Y, Hull.Z));
        foreach (BoundingBox side in ChamberSides)
            yield return side;
        foreach (BoundingBox part in AroundDoorway(ChamberInnerWall, InnerDoorway))
            yield return part;
    }

    /// <summary>
    /// The four boxes of a wall across X that leave <paramref name="doorway"/> open: the parts on each side of it along Z,
    /// as tall as the wall, and the lintel above it and the sill below it, as wide as the doorway.
    /// </summary>
    private static BoundingBox[] AroundDoorway(BoundingBox wall, BoundingBox doorway) =>
        [
            wall with
            {
                Max = wall.Max with { Z = doorway.Min.Z },
            },
            wall with
            {
                Min = wall.Min with { Z = doorway.Max.Z },
            },
            new(wall.Min with { Y = doorway.Max.Y, Z = doorway.Min.Z }, wall.Max with { Z = doorway.Max.Z }),
            new(wall.Min with { Z = doorway.Min.Z }, wall.Max with { Y = doorway.Min.Y, Z = doorway.Max.Z }),
        ];

    /// <summary>
    /// The drawn hull, in the ship's axes: the outer box and the cabin seen from inside, joined through the hatch and the
    /// windows, and the chamber's walls.
    /// </summary>
    private static MeshData HullMesh()
    {
        Opening[] openings = [Opening.ThroughX(Hatch), .. Windows.Select(window => window.Window)];
        BoxMesh mesh = new BoxMesh()
            .Box(new(-Hull, Hull), openings: openings)
            .Box(new(-Cabin, Cabin), inward: true, openings: openings);
        foreach (Opening opening in openings)
            mesh.Sides(opening);
        foreach (BoundingBox side in ChamberSides)
            mesh.Box(side);
        var innerDoorway = Opening.ThroughX(InnerDoorway);
        return mesh.Box(ChamberInnerWall, openings: innerDoorway).Sides(innerDoorway).Build();
    }

    /// <summary>
    /// A door of the airlock that closes <paramref name="doorway"/>: a kinematic slab 0.1 m thick in the middle of the
    /// doorway's wall, which slides up into the wall above the doorway in 1.5 s. It slides 0.1 m higher than the doorway is
    /// tall, so that its lower edge hides in the wall and does not share a plane with the doorway's top.
    /// </summary>
    private static SpawnDesc DoorIn(FrameHandle frame, BoundingBox doorway, IRenderResources resources)
    {
        Vector3 half = doorway.Extents with { X = 0.05f };
        return new()
        {
            Frame = frame,
            Pose = new(doorway.Center, DQuaternion.Identity),
            Body = new(Shape: new BoxShapeDesc(half), Motion: BodyMotion.Kinematic),
            Render = Drawn(
                resources: resources,
                mesh: Primitives.Box(half),
                color: new(x: 0.85f, y: 0.45f, z: 0.1f, w: 1),
                boundingRadius: half.Length()
            ),
            Components = [new Door(new(0, 2 * doorway.Extents.Y + 0.1f, 0), openSeconds: 1.5f)],
        };
    }

    /// <summary>
    /// The pane of glass in <paramref name="window"/>: a box 2 cm thick in the middle of the window's wall, 1 cm larger
    /// than the window on each side, so that its edges hide in the wall and do not share a plane with the window's sides.
    /// It is transparent and casts no shadow. Panes of one size share a mesh in <paramref name="meshes"/>.
    /// </summary>
    private static SpawnDesc PaneIn(
        FrameHandle frame,
        Opening window,
        MaterialId glass,
        Dictionary<Vector3, MeshId> meshes,
        IRenderResources resources
    )
    {
        Vector3 half = window.Bounds.Extents + new Vector3(0.01f);
        half = window.Through == Vector3.UnitX ? half with { X = 0.01f } : half with { Z = 0.01f };
        if (!meshes.TryGetValue(half, out MeshId mesh))
            meshes[half] = mesh = resources.CreateMesh(Primitives.Box(half));
        return new()
        {
            Frame = frame,
            Pose = new(window.Bounds.Center, DQuaternion.Identity),
            Render = new RenderData
            {
                Mesh = mesh,
                Material = glass,
                BoundingRadius = half.Length(),
                Flags = RenderFlags.Visible | RenderFlags.Transparent,
            },
        };
    }

    /// <summary>The mean anomaly of an orbit with eccentricity <paramref name="e"/> at true anomaly
    /// <paramref name="trueAnomaly"/>.</summary>
    private static double MeanAnomaly(double trueAnomaly, double e)
    {
        double eccentric = 2 * Math.Atan(Math.Sqrt((1 - e) / (1 + e)) * Math.Tan(trueAnomaly / 2));
        return eccentric - e * Math.Sin(eccentric);
    }

    /// <summary>
    /// A deep blue planet with light lines every 10 degrees of longitude and latitude, 1024 by 512 sRGB. Each line is 0.7
    /// degrees wide, centered on its mark, so the equator line runs under the ship's ground track.
    /// </summary>
    private static TextureData EarthGrid()
    {
        const int width = 1024,
            height = 512;
        const double halfWidth = 0.35;
        static bool OnLine(double degrees) => Math.Abs(degrees - 10 * Math.Round(degrees / 10)) < halfWidth;
        byte[] rgba = new byte[width * height * 4];
        for (int y = 0; y < height; y++)
        for (int x = 0; x < width; x++)
        {
            bool line = OnLine((x + 0.5) * 360.0 / width) || OnLine((y + 0.5) * 180.0 / height);
            int i = (y * width + x) * 4;
            (rgba[i], rgba[i + 1], rgba[i + 2], rgba[i + 3]) = line
                ? ((byte)190, (byte)210, (byte)240, (byte)255)
                : ((byte)35, (byte)85, (byte)170, (byte)255);
        }

        return new(Width: width, Height: height, Rgba: rgba);
    }

    /// <summary>
    /// A 1 m orange <c>box</c> that falls 2.5 m onto a gray static <c>floor</c>, in the root frame <c>ground</c> with
    /// 9.81 m/s² of gravity along -Y. The top of the floor is at y = 0, and the box starts with its center at y = 3. The
    /// entity <c>camera</c> stands at (0, 2.5, 9) and looks along -Z, tilted down by 6.3 degrees toward (0, 1.5, 0). The
    /// light is the star <c>sun</c>, 1 AU from the origin along (30, 50, 40), which sends 1 W/m² there and casts shadows.
    /// The box and the floor cast shadows.
    /// </summary>
    private static SceneInstance DropBox(World world, IRenderResources resources)
    {
        var scene = new SceneInstance();
        FrameHandle ground = world.CreateRootFrame(
            new(FrameBounds.Sphere(1e6f), GravityField.Uniform(new(0, -9.81f, 0)))
        );
        scene.Add("ground", ground);
        scene.Add(
            "floor",
            world.Spawn(
                new()
                {
                    Frame = ground,
                    Pose = new(new(0, -0.5, 0), DQuaternion.Identity),
                    Body = new(Shape: new BoxShapeDesc(new(50, 0.5f, 50)), Motion: BodyMotion.Static),
                    Render = Drawn(
                        resources: resources,
                        mesh: Primitives.Box(new(50, 0.5f, 50)),
                        color: new(x: 0.35f, y: 0.35f, z: 0.35f, w: 1),
                        boundingRadius: 71
                    ),
                }
            )
        );
        scene.Add(
            "box",
            world.Spawn(
                new()
                {
                    Frame = ground,
                    Pose = new(new(0, 3, 0), DQuaternion.Identity),
                    Body = new(Shape: new BoxShapeDesc(new(0.5f)), Mass: 10),
                    Render = Drawn(
                        resources: resources,
                        mesh: Primitives.Box(new(0.5f)),
                        color: new(x: 1, y: 0.45f, z: 0.1f, w: 1),
                        boundingRadius: 0.87f
                    ),
                }
            )
        );
        Entity camera = world.Spawn(
            new()
            {
                Frame = ground,
                Pose = new(new(0, 2.5, 9), DQuaternion.FromAxisAngle(new(1, 0, 0), -Math.Atan(1.0 / 9))),
                Render = new RenderData(),
            }
        );
        scene.Add("camera", camera);
        world.Camera = camera;
        // The star's light comes from this direction: up, to the right and in front of the box.
        Star(world: world, scene: scene, frame: ground, center: Double3.Zero, direction: new(30, 50, 40));
        world.Ambient = Daylight;
        return scene;
    }

    private static RenderData Drawn(IRenderResources resources, MeshData mesh, Vector4 color, float boundingRadius) =>
        new()
        {
            Mesh = resources.CreateMesh(mesh),
            Material = resources.CreateMaterial(MaterialDesc.Color(color, UsualRoughness)),
            BoundingRadius = boundingRadius,
            Flags = RenderFlags.Visible | RenderFlags.CastsShadow,
        };
}
