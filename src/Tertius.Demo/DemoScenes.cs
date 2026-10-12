using System.Numerics;
using Tertius.Host;
using Tertius.Simulation;

namespace Tertius.Demo;

/// <summary>
/// The scenes that scripts can name. The vertical slice adds its scenes here, and the content packages work later moves
/// them into the first content package.
/// </summary>
public static class DemoScenes
{
    public static IReadOnlyList<Scene> All { get; } = [new Scene("drop-box", DropBox), new Scene("orbit", Orbit)];

    /// <summary>Earth's mean radius, in meters.</summary>
    public const double EarthRadius = 6.371e6;

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

    /// <summary>Earth's spin relative to the stars, one turn per sidereal day of 86,164.0905 s, in radians per second.</summary>
    public const double SiderealRate = 2 * Math.PI / 86_164.0905;

    /// <summary>The tilt of Earth's axis from the pole of its orbit: 23.4392811 degrees, in radians.</summary>
    public const double Obliquity = 23.4392811 * Math.PI / 180;

    /// <summary>
    /// The vertical slice's first scene: a ship falls freely round an Earth that goes round the sun, at the March equinox.
    /// <list type="bullet">
    /// <item>The sun is the root frame <c>sun</c>, with the sun's gravity, drawn with its real radius. It is the light, and
    /// it casts no shadow. Its +Y is the pole of Earth's orbit, and its +X points from the sun to Earth's place at the
    /// September equinox, so the sun, seen from Earth, stands toward +X at the March equinox.</item>
    /// <item>The frame <c>earth</c> goes round the sun on Earth's real orbit (1.00000261 AU, eccentricity 0.01671123,
    /// perihelion at longitude 102.94 degrees). It does not turn: it leans by Earth's 23.44 degree obliquity, so its +Y is
    /// Earth's north pole and its +X is the March equinox direction, as for the real Earth-centered inertial frame. It
    /// holds Earth's gravity. The ship falls round Earth in it.</item>
    /// <item>The frame <c>earth-surface</c> sits at Earth's center and turns about Earth's pole once per sidereal day. It
    /// draws the planet: Earth's radius, with a grid of 10-degree lines, so its turn shows. It casts Earth's shadow, and
    /// it has Earth's gravity for objects on the ground.</item>
    /// <item>The frame <c>ship</c>, a 20 by 8 by 60 m hull in a bubble of radius 100 m, falls freely in Earth's gravity:
    /// Jolt integrates it, with no damping. It starts on a circle 400 km up, crossing the equator northward at +X, tilted
    /// 51.64 degrees, with its belly toward Earth and its nose along its motion. Its spin, one turn per orbit, keeps it so.
    /// At the start the sun stands straight above it. In Earth's frame its hull is a solid box. In its own frame the hull
    /// is hollow: walls 0.5 m thick around a cabin 19 by 7 by 59 m. Only the cabin has gravity: 9.81 m/s² toward the
    /// belly. Five lamps under the roof light the cabin, because the hull keeps the sunlight out.</item>
    /// <item>The <c>airlock</c> is a chamber 3 by 3 m against the port wall, 10 m aft of the ship's center. The door
    /// <c>airlock-inner</c> leads from the cabin into the chamber, and the door <c>airlock-outer</c> closes a hatch from
    /// the chamber to the outside. Both doorways are 1.2 m wide and 2.1 m tall, with a sill 0.2 m high. The airlock
    /// starts with the inner door open. A use cycles it: the open door closes in 1.5 s, and 3 s after it began to close,
    /// the other door opens.</item>
    /// <item>The <c>person</c>, a capsule character 1.8 m tall, stands on the cabin floor 10 m aft of the ship's center and
    /// faces the nose. The airlock's inner door is 6.4 m to its left. Its eye, 1.65 m above the floor, is a view. While
    /// that view is on, <see cref="PersonControl"/> steers it and turns its view from the <c>input</c> topic. Outside the
    /// cabin, the person is weightless and its thrusters push it.</item>
    /// <item>The view <c>chase</c>, the camera at the start, rides in the ship's frame, 70 m behind it and 12 m above it,
    /// and looks at the ship's center.</item>
    /// <item>The view <c>far</c> stands in <c>earth</c>, three Earth radii from its center, 30 degrees north of the
    /// equator, and looks at the center. It shows Earth's surface turn. The ship is too small to see from there.</item>
    /// </list>
    /// The scene places everything for the world's current time, so a world that starts later is consistent too.
    /// </summary>
    private static SceneInstance Orbit(World world, IRenderResources resources)
    {
        var scene = new SceneInstance();
        FrameHandle sun = world.CreateRootFrame(
            new FrameSpec(FrameBounds.Sphere(1e13f), MotionDriver.Static, GravityField.Point(SunMu)),
            new RenderData
            {
                Mesh = resources.CreateMesh(Primitives.Sphere((float)SunRadius, 64, 32)),
                // Black, so that only the emission shows: the sun lights the scene through world.SunFrame, not through
                // its sphere.
                Material = resources.CreateMaterial(new MaterialDesc(new Vector4(0, 0, 0, 1), SpecularIntensity: 0, Emissive: new Vector3(60, 55, 45))),
                BoundingRadius = (float)SunRadius,
                Flags = RenderFlags.Visible | RenderFlags.Celestial,
            });
        scene.Add("sun", sun.Entity);
        world.SunFrame = sun;
        // Sunlight above the atmosphere is bright. Space has no sky and no ground, so a face that the sun does not reach gets
        // the same dim light from every direction.
        world.SunIntensity = 2.5f;
        world.Ambient = new AmbientLight { SkyColor = new Vector3(0.03f), GroundColor = new Vector3(0.03f), Intensity = 1 };

        // The pole leans from the sun's +Y toward -Z, the direction of the sun from Earth at the June solstice.
        DQuaternion obliquity = DQuaternion.FromAxisAngle(Double3.UnitX, -Obliquity);
        const double eccentricity = 0.01671123, perihelion = 102.93768193 * Math.PI / 180;
        // At the March equinox, Earth is at longitude 180 degrees from the sun.
        double meanAnomaly = MeanAnomaly(Math.PI - perihelion, eccentricity);
        Entity earthOwner = world.Spawn(new SpawnDesc
        {
            Frame = sun,
            AsFrame = new FrameSpec(FrameBounds.Sphere(9.25e8f), MotionDriver.Kepler, GravityField.Point(EarthMu),
                new KeplerOrbit(1.00000261 * AstronomicalUnit, eccentricity, 0, 0, perihelion, meanAnomaly, 0, SunMu, obliquity, 0, 0)),
        });
        scene.Add("earth", earthOwner);
        FrameHandle earth = world.GetOwnedFrame(earthOwner);

        scene.Add("earth-surface", world.Spawn(new SpawnDesc
        {
            Frame = earth,
            // The bubble holds the air, 100 km up, and ends below the ship's orbit.
            AsFrame = new FrameSpec(FrameBounds.Sphere((float)(EarthRadius + 100e3)), MotionDriver.Spin, GravityField.Point(EarthMu),
                Spin: new FrameSpin(DQuaternion.Identity, SiderealRate)),
            Render = new RenderData
            {
                Mesh = resources.CreateMesh(Primitives.Sphere((float)EarthRadius, 256, 128)),
                Material = resources.CreateMaterial(new MaterialDesc(Vector4.One, SpecularIntensity: 0.1f, Albedo: resources.CreateTexture(EarthGrid()))),
                BoundingRadius = (float)EarthRadius,
                Flags = RenderFlags.Visible | RenderFlags.Celestial | RenderFlags.CastsShadow,
            },
        }));

        // The circle that the ship starts on gives its state at the world's time. From there, gravity alone moves it.
        KeplerOrbit start = KeplerOrbit.Circular(OrbitRadius, EarthMu) with { Inclination = OrbitInclination, Attitude = KeplerAttitude.FacesParent };
        (Double3 position, Double3 velocity) = start.StateAt(world.Time);
        Entity ship = world.Spawn(new SpawnDesc
        {
            Frame = earth,
            Pose = new DPose(position, start.RotationAt(world.Time)),
            LinearVelocity = velocity.ToVector3(),
            AngularVelocity = start.AngularVelocityAt(world.Time).ToVector3(),
            // In Earth's frame the hull is a solid box. Inside the ship's own frame it is hollow: the person walks on its
            // floor, and goes out through the airlock along its outer faces.
            Body = new BodyDesc(new BoxShapeDesc(Hull), Mass: 1e5f, LinearDamping: 0, AngularDamping: 0,
                InteriorShape: new CompoundShapeDesc([.. InteriorBoxes().Select(box => new CompoundChild(new BoxShapeDesc(box.Extents), new Pose(box.Center, Quaternion.Identity)))])),
            // The bubble holds the hull and the chase camera, 71 m from the ship's center. The ship falls freely, so only
            // its own field acts inside it: one standard gravity toward its belly, in the cabin alone.
            AsFrame = new FrameSpec(FrameBounds.Sphere(100), MotionDriver.Physics,
                GravityField.Uniform(new Vector3(0, -9.81f, 0), new BoundingBox(-Cabin, Cabin))),
            Render = Drawn(resources, HullMesh(), new Vector4(0.8f, 0.8f, 0.82f, 1), boundingRadius: 32),
        });
        scene.Add("ship", ship);
        FrameHandle shipFrame = world.GetOwnedFrame(ship);

        // Five lamps under the roof, 12 m apart, light the cabin, because the hull keeps the sunlight out. Local lights
        // cast no shadows yet, but the cabin is one box, so nothing stands between a lamp and a face that it lights, and
        // the outer faces of the hull face away from every lamp. Each lamp is a glowing panel, with its light just below.
        MeshId lampMesh = resources.CreateMesh(Primitives.Box(new Vector3(0.6f, 0.05f, 0.6f)));
        MaterialId lampMaterial = resources.CreateMaterial(new MaterialDesc(new Vector4(0, 0, 0, 1), SpecularIntensity: 0, Emissive: new Vector3(6, 5.4f, 4.5f)));
        for (int i = -2; i <= 2; i++)
            world.Spawn(new SpawnDesc
            {
                Frame = shipFrame,
                Pose = new DPose(new Double3(0, Cabin.Y - 0.05, 12 * i), DQuaternion.Identity),
                Render = new RenderData
                {
                    Mesh = lampMesh,
                    Material = lampMaterial,
                    BoundingRadius = 0.85f,
                    Flags = RenderFlags.Visible,
                    Light = new LightData { Enabled = true, Color = new Vector3(1, 0.9f, 0.75f), Intensity = 100, Range = 20, Offset = new Pose(new Vector3(0, -0.1f, 0), Quaternion.Identity) },
                },
            });

        // The airlock's doors slide up into the wall above their doorways. Each is 0.1 m thick, in the middle of its wall.
        Entity innerDoor = world.Spawn(DoorIn(shipFrame, InnerDoorway, resources));
        Entity outerDoor = world.Spawn(DoorIn(shipFrame, Hatch, resources));
        scene.Add("airlock-inner", innerDoor);
        scene.Add("airlock-outer", outerDoor);
        // A person uses the airlock within reach of the chamber's center, at the height of a standing person's center.
        scene.Add("airlock", world.Spawn(new SpawnDesc
        {
            Frame = shipFrame,
            Pose = new DPose(new Double3(Chamber.Center.X, -Cabin.Y + 0.95, AirlockZ), DQuaternion.Identity),
            Components = [new Airlock(innerDoor, outerDoor, cycleSeconds: 3)],
        }));

        // The person stands on the cabin floor, 10 m aft of the ship's center, and faces the nose. The capsule is 1.8 m
        // tall, and its center rests 0.9 m above the floor. The eye is 1.65 m above the floor.
        scene.Add("person", world.Spawn(new SpawnDesc
        {
            Frame = shipFrame,
            Pose = new DPose(new Double3(0, -Cabin.Y + 0.95, AirlockZ), DQuaternion.Identity),
            Body = new BodyDesc(new CapsuleShapeDesc(0.6f, 0.3f), BodyMotion.Character, Mass: 70),
            Render = Drawn(resources, Primitives.Box(new Vector3(0.3f, 0.9f, 0.3f)), new Vector4(0.2f, 0.45f, 0.8f, 1), boundingRadius: 1),
            Components = [new PersonControl(new Vector3(0, 0.75f, 0))],
            Subscriptions = [Topics.Input],
        }));

        Entity chase = world.Spawn(new SpawnDesc
        {
            Frame = shipFrame,
            Pose = new DPose(new Double3(0, 12, 70), DQuaternion.FromAxisAngle(new Double3(1, 0, 0), -Math.Atan2(12, 70))),
            Render = new RenderData(),
        });
        scene.Add("chase", chase);

        Entity far = world.Spawn(new SpawnDesc
        {
            Frame = earth,
            Pose = new DPose(new Double3(0, 3 * EarthRadius * Math.Sin(Math.PI / 6), 3 * EarthRadius * Math.Cos(Math.PI / 6)),
                DQuaternion.FromAxisAngle(new Double3(1, 0, 0), -Math.PI / 6)),
            Render = new RenderData(),
        });
        scene.Add("far", far);
        scene.AddView("chase");
        scene.AddView("far");
        scene.AddView("person");
        world.Camera = chase;
        return scene;
    }

    /// <summary>The half extents of the ship's hull, in meters: 20 m wide, 8 m high and 60 m long.</summary>
    private static readonly Vector3 Hull = new(10, 4, 30);

    /// <summary>The thickness of the hull's floor, roof and walls, in meters.</summary>
    private const float HullWall = 0.5f;

    /// <summary>The half extents of the cabin, in meters: the hull less its walls.</summary>
    private static readonly Vector3 Cabin = Hull - new Vector3(HullWall);

    /// <summary>How far aft of the ship's center the airlock stands, in meters: level with the person's start.</summary>
    private const float AirlockZ = 10;

    /// <summary>The airlock's chamber, in the ship's axes: against the port wall, 3 m deep and 3 m wide, as tall as the cabin.</summary>
    private static readonly BoundingBox Chamber = new(new Vector3(-Cabin.X, -Cabin.Y, AirlockZ - 1.5f), new Vector3(-Cabin.X + 3, Cabin.Y, AirlockZ + 1.5f));

    /// <summary>The thickness of the chamber's walls, in meters.</summary>
    private const float ChamberWall = 0.2f;

    /// <summary>The hatch through the hull's port wall, from the chamber to the outside.</summary>
    private static readonly BoundingBox Hatch = Doorway(-Hull.X, -Cabin.X);

    /// <summary>The doorway through the chamber's inner wall, from the cabin into the chamber.</summary>
    private static readonly BoundingBox InnerDoorway = Doorway(Chamber.Max.X, Chamber.Max.X + ChamberWall);

    /// <summary>
    /// A doorway of the airlock through a wall across X, from <paramref name="x0"/> to <paramref name="x1"/>: 1.2 m wide and
    /// 2.1 m tall, with a sill 0.2 m high that the person steps over.
    /// </summary>
    private static BoundingBox Doorway(float x0, float x1) =>
        new(new Vector3(x0, -Cabin.Y + 0.2f, AirlockZ - 0.6f), new Vector3(x1, -Cabin.Y + 2.3f, AirlockZ + 0.6f));

    /// <summary>The chamber's two side walls, from the port wall to the cabin side of its inner wall.</summary>
    private static readonly BoundingBox[] ChamberSides =
    [
        new(new Vector3(-Cabin.X, -Cabin.Y, Chamber.Min.Z - ChamberWall), new Vector3(InnerDoorway.Max.X, Cabin.Y, Chamber.Min.Z)),
        new(new Vector3(-Cabin.X, -Cabin.Y, Chamber.Max.Z), new Vector3(InnerDoorway.Max.X, Cabin.Y, Chamber.Max.Z + ChamberWall)),
    ];

    /// <summary>The chamber's inner wall, between its side walls, which the inner doorway goes through.</summary>
    private static readonly BoundingBox ChamberInnerWall = new(new Vector3(InnerDoorway.Min.X, -Cabin.Y, Chamber.Min.Z), new Vector3(InnerDoorway.Max.X, Cabin.Y, Chamber.Max.Z));

    /// <summary>
    /// The boxes of the hull's interior shape, in the ship's axes. The floor and the roof span the whole hull, the side
    /// walls stand between them, and the end walls close the cabin between the side walls. The port wall leaves the
    /// hatch open. The chamber's walls stand on the cabin floor. The boxes touch without gaps or overlaps.
    /// </summary>
    private static IEnumerable<BoundingBox> InteriorBoxes()
    {
        var hull = new BoundingBox(-Hull, Hull);
        yield return hull with { Max = Hull with { Y = -Cabin.Y } };
        yield return hull with { Min = (-Hull) with { Y = Cabin.Y } };
        yield return new BoundingBox(new Vector3(Cabin.X, -Cabin.Y, -Hull.Z), new Vector3(Hull.X, Cabin.Y, Hull.Z));
        foreach (BoundingBox part in AroundDoorway(new BoundingBox(new Vector3(-Hull.X, -Cabin.Y, -Hull.Z), new Vector3(-Cabin.X, Cabin.Y, Hull.Z)), Hatch))
            yield return part;
        yield return new BoundingBox(new Vector3(-Cabin.X, -Cabin.Y, -Hull.Z), new Vector3(Cabin.X, Cabin.Y, -Cabin.Z));
        yield return new BoundingBox(new Vector3(-Cabin.X, -Cabin.Y, Cabin.Z), new Vector3(Cabin.X, Cabin.Y, Hull.Z));
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
        wall with { Max = wall.Max with { Z = doorway.Min.Z } },
        wall with { Min = wall.Min with { Z = doorway.Max.Z } },
        new(wall.Min with { Y = doorway.Max.Y, Z = doorway.Min.Z }, wall.Max with { Z = doorway.Max.Z }),
        new(wall.Min with { Z = doorway.Min.Z }, wall.Max with { Y = doorway.Min.Y, Z = doorway.Max.Z }),
    ];

    /// <summary>The drawn hull, in the ship's axes: the outer box and the cabin seen from inside, joined through the hatch, and the chamber's walls.</summary>
    private static MeshData HullMesh()
    {
        BoxMesh mesh = new BoxMesh()
            .Box(new BoundingBox(-Hull, Hull), doorway: Hatch)
            .Box(new BoundingBox(-Cabin, Cabin), inward: true, doorway: Hatch)
            .Doorway(Hatch);
        foreach (BoundingBox side in ChamberSides)
            mesh.Box(side);
        return mesh.Box(ChamberInnerWall, doorway: InnerDoorway).Doorway(InnerDoorway).Build();
    }

    /// <summary>
    /// A door of the airlock that closes <paramref name="doorway"/>: a kinematic slab 0.1 m thick in the middle of the
    /// doorway's wall, which slides up into the wall above the doorway in 1.5 s. It slides 0.1 m higher than the doorway is
    /// tall, so that its lower edge hides in the wall and does not share a plane with the doorway's top.
    /// </summary>
    private static SpawnDesc DoorIn(FrameHandle frame, BoundingBox doorway, IRenderResources resources)
    {
        Vector3 half = doorway.Extents with { X = 0.05f };
        return new SpawnDesc
        {
            Frame = frame,
            Pose = new DPose(doorway.Center, DQuaternion.Identity),
            Body = new BodyDesc(new BoxShapeDesc(half), BodyMotion.Kinematic),
            Render = Drawn(resources, Primitives.Box(half), new Vector4(0.85f, 0.45f, 0.1f, 1), boundingRadius: half.Length()),
            Components = [new Door(new Vector3(0, 2 * doorway.Extents.Y + 0.1f, 0), openSeconds: 1.5f)],
        };
    }

    /// <summary>The mean anomaly of an orbit with eccentricity <paramref name="e"/> at true anomaly <paramref name="trueAnomaly"/>.</summary>
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
        const int width = 1024, height = 512;
        const double halfWidth = 0.35;
        static bool OnLine(double degrees) => Math.Abs(degrees - 10 * Math.Round(degrees / 10)) < halfWidth;
        var rgba = new byte[width * height * 4];
        for (int y = 0; y < height; y++)
        {
            for (int x = 0; x < width; x++)
            {
                bool line = OnLine((x + 0.5) * 360.0 / width) || OnLine((y + 0.5) * 180.0 / height);
                int i = (y * width + x) * 4;
                (rgba[i], rgba[i + 1], rgba[i + 2], rgba[i + 3]) = line ? ((byte)190, (byte)210, (byte)240, (byte)255) : ((byte)35, (byte)85, (byte)170, (byte)255);
            }
        }
        return new TextureData(width, height, rgba);
    }

    /// <summary>
    /// A 1 m orange <c>box</c> that falls 2.5 m onto a gray static <c>floor</c>, in the root frame <c>ground</c> with
    /// 9.81 m/s² of gravity along -Y. The top of the floor is at y = 0, and the box starts with its center at y = 3. The
    /// entity <c>camera</c> stands at (0, 2.5, 9) and looks along -Z, tilted down by 6.3 degrees toward (0, 1.5, 0). The
    /// frame <c>sun</c> at (30, 50, 40) is where the light comes from. The box and the floor cast shadows.
    /// </summary>
    private static SceneInstance DropBox(World world, IRenderResources resources)
    {
        var scene = new SceneInstance();
        FrameHandle ground = world.CreateRootFrame(new FrameSpec(FrameBounds.Sphere(1e6f), MotionDriver.Static, GravityField.Uniform(new Vector3(0, -9.81f, 0))));
        scene.Add("ground", ground);
        scene.Add("floor", world.Spawn(new SpawnDesc
        {
            Frame = ground,
            Pose = new DPose(new Double3(0, -0.5, 0), DQuaternion.Identity),
            Body = new BodyDesc(new BoxShapeDesc(new Vector3(50, 0.5f, 50)), BodyMotion.Static),
            Render = Drawn(resources, Primitives.Box(new Vector3(50, 0.5f, 50)), new Vector4(0.35f, 0.35f, 0.35f, 1), boundingRadius: 71),
        }));
        scene.Add("box", world.Spawn(new SpawnDesc
        {
            Frame = ground,
            Pose = new DPose(new Double3(0, 3, 0), DQuaternion.Identity),
            Body = new BodyDesc(new BoxShapeDesc(new Vector3(0.5f)), Mass: 10),
            Render = Drawn(resources, Primitives.Box(new Vector3(0.5f)), new Vector4(1, 0.45f, 0.1f, 1), boundingRadius: 0.87f),
        }));
        Entity camera = world.Spawn(new SpawnDesc
        {
            Frame = ground,
            Pose = new DPose(new Double3(0, 2.5, 9), DQuaternion.FromAxisAngle(new Double3(1, 0, 0), -Math.Atan(1.0 / 9))),
            Render = new RenderData(),
        });
        scene.Add("camera", camera);
        world.Camera = camera;
        // The sun frame's origin is where the light comes from: up, to the right and in front of the box.
        Entity sun = world.Spawn(new SpawnDesc
        {
            Frame = ground,
            Pose = new DPose(new Double3(30, 50, 40), DQuaternion.Identity),
            AsFrame = new FrameSpec(FrameBounds.Sphere(1), MotionDriver.Static),
        });
        scene.Add("sun", sun);
        world.SunFrame = world.GetOwnedFrame(sun);
        return scene;
    }

    private static RenderData Drawn(IRenderResources resources, MeshData mesh, Vector4 color, float boundingRadius) => new()
    {
        Mesh = resources.CreateMesh(mesh),
        Material = resources.CreateMaterial(MaterialDesc.Color(color)),
        BoundingRadius = boundingRadius,
        Flags = RenderFlags.Visible | RenderFlags.CastsShadow,
    };
}
