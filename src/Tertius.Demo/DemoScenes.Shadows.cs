using System.Numerics;
using Tertius.Frames.Rails;
using Tertius.Host;
using Tertius.Simulation;

namespace Tertius.Demo;

public static partial class DemoScenes
{
    /// <summary>The power of the spot light of <c>spot-box</c>, in W: a cabin lamp's.</summary>
    private const float SpotBoxPower = CabinLampPower;

    /// <summary>The distance between the centers of two neighbor lamps of a lamp hall, in meters.</summary>
    private const double HallSpacing = 4;

    /// <summary>
    /// The height of the lamps of a lamp hall above its floor, in meters: 0.4 m above the tops of its pillars, so that a
    /// moving lamp passes over them, and each pillar casts a long shadow from each lamp.
    /// </summary>
    private const double HallLampHeight = 2.4;

    /// <summary>The half extents of a pillar of a lamp hall, in meters: 0.4 m square and 2 m tall.</summary>
    private static readonly Vector3 HallPillar = new(0.2f, 1, 0.2f);

    /// <summary>
    /// The rate at which the lamps of a moving lamp hall turn about the hall's center, in rad/s. A lamp 2 m from the center
    /// moves 0.5 m/s, so every tile of every lamp is stale in every frame.
    /// </summary>
    private const double HallTurnRate = 0.25;

    /// <summary>The outer half-angle of a spot lamp of a lamp hall: 50 degrees, so each takes one tile of the atlas.</summary>
    private const float HallSpotOuter = 50 * MathF.PI / 180;

    /// <summary>The inner half-angle of a spot lamp of a lamp hall: 35 degrees.</summary>
    private const float HallSpotInner = 35 * MathF.PI / 180;

    /// <summary>The light gray of the pillars and walls of the scenes of the shadows.</summary>
    private static readonly Vector4 PillarGray = new(x: 0.7f, y: 0.7f, z: 0.68f, w: 1);

    /// <summary>The gray of the floors of the scenes of the shadows, as the floor of the surface scenes.</summary>
    private static readonly Vector4 FloorGray = new(x: 0.4f, y: 0.4f, z: 0.38f, w: 1);

    /// <summary>
    /// The colors of the stars of the star scenes, in the order of their scene ids: the Sun's white, an orange, a blue
    /// white and a red, so that each shadow shows which star it hides.
    /// </summary>
    private static readonly Vector3[] StarColors =
    [
        Vector3.One,
        new(x: 1, y: 0.6f, z: 0.35f),
        new(x: 0.55f, y: 0.7f, z: 1),
        new(x: 1, y: 0.4f, z: 0.3f),
    ];

    /// <summary>
    /// The directions from the plane of the star scenes to their stars, in the order of their scene ids: each high over a
    /// different side of the plane, so that their shadows fall apart.
    /// </summary>
    private static readonly Double3[] StarDirections =
    [
        new(30, 50, 20),
        new(-40, 45, 10),
        new(10, 35, -40),
        new(-20, 60, -30),
    ];

    /// <summary>
    /// The numbers of lamps of the lamp halls that the cost measures. A property, not a field, because <see cref="All"/>
    /// reads it while the class's static fields start, in an order between the files of the class that C# does not fix.
    /// </summary>
    private static int[] HallLampCounts => [4, 16, 64];

    /// <summary>The numbers of stars of the star scenes that the cost measures. A property for the reason of <see cref="HallLampCounts"/>.</summary>
    private static int[] StarCounts => [1, 2, 4];

    /// <summary>The scenes of the shadows of every light (docs/plans/local-light-shadows.md, "Part 7").</summary>
    private static IEnumerable<Scene> ShadowScenes =>
        [
            new("spot-box", SpotBox),
            new("pillar-room", PillarRoom),
            .. HallLampCounts.SelectMany(LampHallScenes),
            new("lamp-hall-16-hair", LampHallHair),
            .. StarCounts.Select(stars => new Scene(
                $"stars-{stars}",
                (world, resources) => Stars(world, resources, stars)
            )),
        ];

    /// <summary>The four lamp halls of <paramref name="lamps"/> lamps: of point lamps and of spot lamps, each still and moving.</summary>
    private static Scene[] LampHallScenes(int lamps) =>
        [
            LampHallScene(lamps: lamps, spot: false, moving: false),
            LampHallScene(lamps: lamps, spot: true, moving: false),
            LampHallScene(lamps: lamps, spot: false, moving: true),
            LampHallScene(lamps: lamps, spot: true, moving: true),
        ];

    /// <summary>
    /// The lamp hall of <paramref name="lamps"/> lamps, of spot lamps when <paramref name="spot"/>, which turn when
    /// <paramref name="moving"/>: <c>lamp-hall-N</c>, with <c>-spot</c> and then <c>-moving</c> after it.
    /// </summary>
    private static Scene LampHallScene(int lamps, bool spot, bool moving) =>
        new(
            $"lamp-hall-{lamps}{(spot ? "-spot" : "")}{(moving ? "-moving" : "")}",
            (world, resources) => LampHall(world: world, resources: resources, lamps: lamps, spot: spot, moving: moving)
        );

    /// <summary>
    /// A spot light over a box, the first review scene of the local shadows: in the root frame <c>room</c>, with no sun
    /// and no sky.
    /// <list type="bullet">
    /// <item>The <c>floor</c>, a gray slab 12 m square with its top at y = 0.</item>
    /// <item>The <c>box</c>, an orange cube 1 m on a side, on the floor at the origin.</item>
    /// <item>The <c>spot</c>, a spot light of 10 W with an outer half-angle of 35 degrees and an inner one of 25, 3.2 m
    /// above the floor and 1.8 m to the box's +Z side, aimed at the floor just past the box, so that the box's shadow falls
    /// toward -Z inside the cone, to the right of the box in the camera's view. Its housing, a small dark box, casts no
    /// shadow.</item>
    /// <item>The view <c>camera</c> looks down at the box from its +X side, 2.4 m up and close, so that the lit floor fills
    /// much of the view and the shadow shows beside the box.</item>
    /// </list>
    /// </summary>
    private static SceneInstance SpotBox(World world, IRenderResources resources)
    {
        var scene = new SceneInstance();
        FrameHandle room = world.CreateRootFrame(new(FrameBounds.Sphere(1e4f)));
        scene.Add("room", room.Entity);
        ShadowFloor(world: world, resources: resources, scene: scene, frame: room, halfSize: 6);
        scene.Add(
            "box",
            world.Spawn(
                new()
                {
                    Frame = room,
                    Pose = new(new(0, 0.5, 0), DQuaternion.Identity),
                    Render = Drawn(
                        resources: resources,
                        mesh: Primitives.Box(new(0.5f)),
                        color: new(x: 1, y: 0.45f, z: 0.1f, w: 1),
                        boundingRadius: 0.87f
                    ),
                }
            )
        );
        scene.Add(
            "spot",
            world.Spawn(
                SpotLamp(
                    frame: room,
                    resources: resources,
                    pose: LookingAt(new(0.5, 3.2, 1.8), new(-0.2, 0, -0.7)),
                    light: new SpotLightData(
                        Offset: new(Vector3.Zero, Quaternion.Identity),
                        Color: LampColor,
                        Power: SpotBoxPower,
                        Radius: LampRadius,
                        CastsShadow: true,
                        InnerConeAngle: 25 * MathF.PI / 180,
                        OuterConeAngle: 35 * MathF.PI / 180
                    )
                )
            )
        );
        AddCamera(world: world, scene: scene, frame: room, pose: LookingAt(new(3.2, 2.4, 1.8), new(-0.5, 0.2, -0.6)));
        return scene;
    }

    /// <summary>
    /// A point light in a room with pillars, the second review scene of the local shadows: in the root frame <c>room</c>,
    /// with no sun and no sky.
    /// <list type="bullet">
    /// <item>The room is 12 m square inside and 4 m tall: a gray <c>floor</c> with its top at y = 0, light gray walls and
    /// a light gray ceiling, each 0.2 m thick.</item>
    /// <item>Eight light gray pillars, <c>pillar-0</c> to <c>pillar-7</c>, 0.5 m square, from the floor to the ceiling:
    /// four on the corners of a square 5 m on a side round the room's center, and four 4.5 m from the center on its axes.
    /// An orange cube 0.8 m on a side, the <c>box</c>, stands on the floor 1.5 m from the center.</item>
    /// <item>The <c>lamp</c>, a household bulb of 2.5 W, hangs 2.8 m above the room's center, so each pillar casts its
    /// shadow away from it across the floor and up the walls.</item>
    /// <item>The view <c>camera</c> stands in a corner at eye height and looks across the room.</item>
    /// </list>
    /// </summary>
    private static SceneInstance PillarRoom(World world, IRenderResources resources)
    {
        var scene = new SceneInstance();
        FrameHandle room = world.CreateRootFrame(new(FrameBounds.Sphere(1e4f)));
        scene.Add("room", room.Entity);
        ShadowFloor(world: world, resources: resources, scene: scene, frame: room, halfSize: 6.2f);
        RoomShell(world, resources, room);
        Double3[] pillars =
        [
            new(2.5, 0, 2.5),
            new(-2.5, 0, 2.5),
            new(2.5, 0, -2.5),
            new(-2.5, 0, -2.5),
            new(4.5, 0, 0),
            new(-4.5, 0, 0),
            new(0, 0, 4.5),
            new(0, 0, -4.5),
        ];
        for (int i = 0; i < pillars.Length; i++)
            scene.Add(
                $"pillar-{i}",
                world.Spawn(
                    Pillar(frame: room, resources: resources, foot: pillars[i], halfExtents: new(0.25f, 2, 0.25f))
                )
            );
        scene.Add(
            "box",
            world.Spawn(
                new()
                {
                    Frame = room,
                    Pose = new(new(1.5, 0.4, -0.3), DQuaternion.FromAxisAngle(Double3.UnitY, 0.4)),
                    Render = Drawn(
                        resources: resources,
                        mesh: Primitives.Box(new(0.4f)),
                        color: new(x: 1, y: 0.45f, z: 0.1f, w: 1),
                        boundingRadius: 0.7f
                    ),
                }
            )
        );
        scene.Add("lamp", world.Spawn(GlowLamp(surface: room, resources: resources, position: new(0, 2.8, 0))));
        AddCamera(world: world, scene: scene, frame: room, pose: LookingAt(new(5.6, 1.7, 5.6), new(-1, 1.2, -2)));
        return scene;
    }

    /// <summary>
    /// The lamp hall that measures the cost of the local shadows (docs/plans/local-light-shadows.md, "Cost"): in the root
    /// frame <c>hall</c>, with no sun and no sky.
    /// <list type="bullet">
    /// <item>The <c>floor</c>, a gray slab with its top at y = 0, 1 m wider on each side than the grid of lamps.</item>
    /// <item>The lamps, <c>lamp-0</c> to <c>lamp-(N-1)</c>, on a square grid 4 m apart, centered on the hall's center,
    /// 2.4 m above the floor: household bulbs of 2.5 W, or with <paramref name="spot"/> spot lamps of 2.5 W aimed straight
    /// down with an outer half-angle of 50 degrees, which take one tile each. Every lamp casts shadows. They stand in the
    /// frame <c>lamps</c>, whose owner has no body and sits at the hall's center. With <paramref name="moving"/> that frame
    /// turns about +Y at 0.25 rad/s, so each lamp moves past the pillars in every frame, and every tile is drawn again in
    /// every frame. Without it the lamps stand still, and every tile is cached after the first frame.</item>
    /// <item>The pillars, <c>pillar-0</c> and on, 0.4 m square and 2 m tall, stand on the corners of the grid's cells, so
    /// each lamp has four pillars round it and lights more beyond them.</item>
    /// <item>The view <c>camera</c> stands above a corner of the hall and looks down across it.</item>
    /// </list>
    /// </summary>
    /// <param name="lamps">The number of lamps: a square of a whole number.</param>
    private static SceneInstance LampHall(World world, IRenderResources resources, int lamps, bool spot, bool moving)
    {
        var scene = new SceneInstance();
        FrameHandle hall = world.CreateRootFrame(new(FrameBounds.Sphere(1e4f)));
        scene.Add("hall", hall.Entity);
        int side = (int)Math.Round(Math.Sqrt(lamps));
        double half = side * HallSpacing / 2;
        ShadowFloor(world: world, resources: resources, scene: scene, frame: hall, halfSize: (float)half + 1);
        for (int i = 0; i <= side; i++)
        for (int j = 0; j <= side; j++)
            scene.Add(
                $"pillar-{i * (side + 1) + j}",
                world.Spawn(
                    Pillar(
                        frame: hall,
                        resources: resources,
                        foot: new(i * HallSpacing - half, 0, j * HallSpacing - half),
                        halfExtents: HallPillar
                    )
                )
            );

        FrameHandle carousel = LampCarousel(world: world, scene: scene, hall: hall, moving: moving);
        for (int i = 0; i < side; i++)
        for (int j = 0; j < side; j++)
        {
            var place = new Double3((i + 0.5) * HallSpacing - half, HallLampHeight, (j + 0.5) * HallSpacing - half);
            SpawnDesc lamp = spot
                ? SpotLamp(
                    frame: carousel,
                    resources: resources,
                    pose: new(place, DQuaternion.FromAxisAngle(Double3.UnitX, -Math.PI / 2)),
                    light: new SpotLightData(
                        Offset: new(Vector3.Zero, Quaternion.Identity),
                        Color: LampColor,
                        Power: HouseholdBulbPower,
                        Radius: LampRadius,
                        CastsShadow: true,
                        InnerConeAngle: HallSpotInner,
                        OuterConeAngle: HallSpotOuter
                    )
                )
                : GlowLamp(surface: carousel, resources: resources, position: place);
            scene.Add($"lamp-{i * side + j}", world.Spawn(lamp));
        }

        AddCamera(
            world: world,
            scene: scene,
            frame: hall,
            pose: LookingAt(new(half + 0.5, half / 2 + 4, half + 0.5), new(-half / 4, 0, -half / 4))
        );
        return scene;
    }

    /// <summary>
    /// The frame <c>lamps</c> at the center of <paramref name="hall"/>, whose owner has no body: still, or with
    /// <paramref name="moving"/> turning about +Y at <see cref="HallTurnRate"/> from its first step.
    /// </summary>
    private static FrameHandle LampCarousel(World world, SceneInstance scene, FrameHandle hall, bool moving)
    {
        var turn = new FrameSpin(Tilt: DQuaternion.Identity, Rate: moving ? HallTurnRate : 0);
        Entity owner = world.Spawn(
            new()
            {
                Frame = hall,
                Pose = new(Double3.Zero, turn.RotationAt(world.Time)),
                FirstMove = new(turn.Sample(Double3.Zero, world.TimeAt(world.TickNumber + 1))),
                AsFrame = new(FrameBounds.Sphere(1e3f)),
                Components = moving ? [new SpinMotion { Spin = turn }] : [],
            }
        );
        scene.Add("lamps", owner);
        return world.GetOwnedFrame(owner);
    }

    /// <summary>
    /// A star scene of <paramref name="stars"/> stars over a plane, which measures the cost of the shadows of far lights
    /// (docs/plans/local-light-shadows.md, "Cost"): in the root frame <c>plane</c>, with the dim light of space.
    /// <list type="bullet">
    /// <item>The <c>floor</c>, a gray slab 30 m square with its top at y = 0.</item>
    /// <item>Three light gray pillars, <c>pillar-0</c> to <c>pillar-2</c>, 0.6 m square and 3 m tall, an orange cube 1 m
    /// on a side, the <c>box</c>, and a white ball 1.2 m across, the <c>ball</c>, on the floor round its center.</item>
    /// <item>The stars, <c>star-1</c> to <c>star-N</c>, each a point light of the Sun's power and radius 1 AU from the
    /// plane's center, high over a different side of the plane, in the colors of <see cref="StarColors"/>: white, orange,
    /// blue white and red. Each casts shadows, so each caster casts one shadow per star, in the colors of the other
    /// stars.</item>
    /// <item>The view <c>camera</c> looks at the casters from 9 m away, 4 m up.</item>
    /// </list>
    /// </summary>
    private static SceneInstance Stars(World world, IRenderResources resources, int stars)
    {
        var scene = new SceneInstance();
        FrameHandle plane = world.CreateRootFrame(new(FrameBounds.Sphere(1e4f)));
        scene.Add("plane", plane.Entity);
        world.Ambient = SpaceAmbient;
        ShadowFloor(world: world, resources: resources, scene: scene, frame: plane, halfSize: 15);
        Double3[] pillars = [new(-3, 0, -2), new(0, 0, -3.5), new(3, 0, -1.5)];
        for (int i = 0; i < pillars.Length; i++)
            scene.Add(
                $"pillar-{i}",
                world.Spawn(
                    Pillar(frame: plane, resources: resources, foot: pillars[i], halfExtents: new(0.3f, 1.5f, 0.3f))
                )
            );
        scene.Add(
            "box",
            world.Spawn(
                new()
                {
                    Frame = plane,
                    Pose = new(new(0.5, 0.5, 1), DQuaternion.FromAxisAngle(Double3.UnitY, 0.5)),
                    Render = Drawn(
                        resources: resources,
                        mesh: Primitives.Box(new(0.5f)),
                        color: new(x: 1, y: 0.45f, z: 0.1f, w: 1),
                        boundingRadius: 0.87f
                    ),
                }
            )
        );
        scene.Add(
            "ball",
            world.Spawn(
                new()
                {
                    Frame = plane,
                    Pose = new(new(-2, 0.6, 1.5), DQuaternion.Identity),
                    Render = Drawn(
                        resources: resources,
                        mesh: Primitives.Sphere(0.6f),
                        color: new(x: 0.85f, y: 0.85f, z: 0.85f, w: 1),
                        boundingRadius: 0.6f
                    ),
                }
            )
        );
        for (int i = 0; i < stars; i++)
            scene.Add(
                $"star-{i + 1}",
                world.Spawn(
                    StarAt(frame: plane, place: StarDirections[i].Normalized() * AstronomicalUnit, color: StarColors[i])
                )
            );
        AddCamera(world: world, scene: scene, frame: plane, pose: LookingAt(new(5, 4, 8), new(0, 0.8, -0.5)));
        return scene;
    }

    /// <summary>
    /// The gray <c>floor</c> of a scene of the shadows in <paramref name="frame"/>: a slab 0.2 m thick, square with the
    /// half side <paramref name="halfSize"/> m, with its top at y = 0.
    /// </summary>
    private static void ShadowFloor(
        World world,
        IRenderResources resources,
        SceneInstance scene,
        FrameHandle frame,
        float halfSize
    )
    {
        var halfExtents = new Vector3(halfSize, 0.1f, halfSize);
        scene.Add(
            "floor",
            world.Spawn(
                new()
                {
                    Frame = frame,
                    Pose = new(new(0, -0.1, 0), DQuaternion.Identity),
                    Render = Drawn(
                        resources: resources,
                        mesh: Primitives.Box(halfExtents),
                        color: FloorGray,
                        boundingRadius: halfExtents.Length()
                    ),
                }
            )
        );
    }

    /// <summary>
    /// The walls and the ceiling of <c>pillar-room</c> in <paramref name="room"/>: light gray slabs 0.2 m thick round a
    /// room 12 m square and 4 m tall, with its floor's top at y = 0.
    /// </summary>
    private static void RoomShell(World world, IRenderResources resources, FrameHandle room)
    {
        (Double3 Place, Vector3 HalfExtents)[] slabs =
        [
            (new(0, 4.1, 0), new(6.2f, 0.1f, 6.2f)),
            (new(6.1, 2, 0), new(0.1f, 2, 6.2f)),
            (new(-6.1, 2, 0), new(0.1f, 2, 6.2f)),
            (new(0, 2, 6.1), new(6, 2, 0.1f)),
            (new(0, 2, -6.1), new(6, 2, 0.1f)),
        ];
        foreach ((Double3 place, Vector3 halfExtents) in slabs)
            world.Spawn(Block(frame: room, resources: resources, center: place, halfExtents: halfExtents));
    }

    /// <summary>
    /// A light gray box in <paramref name="frame"/> with its center at <paramref name="center"/> and the half extents
    /// <paramref name="halfExtents"/>, which casts shadows.
    /// </summary>
    private static SpawnDesc Block(
        FrameHandle frame,
        IRenderResources resources,
        Double3 center,
        Vector3 halfExtents
    ) =>
        new()
        {
            Frame = frame,
            Pose = new(center, DQuaternion.Identity),
            Render = Drawn(
                resources: resources,
                mesh: Primitives.Box(halfExtents),
                color: PillarGray,
                boundingRadius: halfExtents.Length()
            ),
        };

    /// <summary>
    /// A light gray pillar in <paramref name="frame"/> with the half extents <paramref name="halfExtents"/>, which stands on
    /// the floor, at y = 0, at <paramref name="foot"/>, and casts shadows.
    /// </summary>
    private static SpawnDesc Pillar(FrameHandle frame, IRenderResources resources, Double3 foot, Vector3 halfExtents) =>
        Block(
            frame: frame,
            resources: resources,
            center: foot + new Double3(0, halfExtents.Y, 0),
            halfExtents: halfExtents
        );

    /// <summary>
    /// A spot lamp in <paramref name="frame"/> at <paramref name="pose"/>, whose local -Z is the axis of its cone: the
    /// spot light <paramref name="light"/> at the pose, in a small dark housing round it. The housing casts no shadow, so
    /// it does not darken its own light, and it does not glow: a spot light sends nothing out of its cone, and the views of
    /// these scenes stand out of the cones.
    /// </summary>
    private static SpawnDesc SpotLamp(FrameHandle frame, IRenderResources resources, DPose pose, SpotLightData light) =>
        new()
        {
            Frame = frame,
            Pose = pose,
            Render = new RenderData
            {
                Mesh = resources.CreateMesh(Primitives.Box(new(0.08f, 0.08f, 0.12f))),
                Material = resources.CreateMaterial(
                    MaterialDesc.Color(new(x: 0.05f, y: 0.05f, z: 0.05f, w: 1), UsualRoughness)
                ),
                BoundingRadius = 0.17f,
                Flags = RenderFlags.Visible | RenderFlags.NoImpostor,
                Light = light,
            },
        };

    /// <summary>A star in <paramref name="frame"/> at <paramref name="place"/>: a point light of the Sun's power and radius in
    /// <paramref name="color"/>, which casts shadows, with no sphere.</summary>
    private static SpawnDesc StarAt(FrameHandle frame, Double3 place, Vector3 color) =>
        new()
        {
            Frame = frame,
            Pose = new(place, DQuaternion.Identity),
            Render = new RenderData
            {
                Light = new PointLightData(
                    Offset: new(Vector3.Zero, Quaternion.Identity),
                    Color: color,
                    Power: (float)SunPower,
                    Radius: (float)SunRadius,
                    CastsShadow: true
                ),
            },
        };

    /// <summary>The view <c>camera</c> in <paramref name="frame"/> at <paramref name="pose"/>, which becomes the world's camera.</summary>
    private static void AddCamera(World world, SceneInstance scene, FrameHandle frame, DPose pose)
    {
        Entity camera = world.Spawn(ViewIn(frame, pose));
        scene.Add("camera", camera);
        scene.AddView("camera");
        world.Camera = camera;
    }
}
