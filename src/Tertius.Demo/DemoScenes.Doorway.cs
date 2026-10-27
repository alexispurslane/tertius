using System.Numerics;
using Tertius.Host;
using Tertius.Simulation;

namespace Tertius.Demo;

public static partial class DemoScenes
{
    /// <summary>The name of the topic that the doorway's sensor relays to, and that its door hears.</summary>
    public const string DoorwayTopic = "doorway";

    /// <summary>Where the person of the doorway scene starts, in meters along +Z from the door.</summary>
    public const double DoorwayPersonStartZ = 6;

    /// <summary>The center of the doorway's sensor volume, in meters along +Z from the door.</summary>
    public const double DoorwaySensorZ = 2.5;

    /// <summary>The half depth of the doorway's sensor volume along Z, in meters.</summary>
    public const float DoorwaySensorHalfDepth = 0.75f;

    /// <summary>The height of the center of the doorway's door when it is shut, in meters above the floor.</summary>
    public const double DoorwayDoorShutY = 1.25;

    /// <summary>How far the doorway's door rises when it opens, in meters.</summary>
    public const float DoorwayDoorRise = 2.5f;

    /// <summary>
    /// A door that opens for a person who walks up to it, in a root frame with gravity.
    /// <list type="bullet">
    /// <item>The root frame <c>ground</c> has uniform gravity of 9.81 m/s² along -Y. A static gray <c>floor</c>, 50 by 50 m
    /// and 1 m thick, has its top at y = 0.</item>
    /// <item>The <c>door</c> is a kinematic slab 2 m wide, 2.5 m tall and 0.1 m thick, standing on the floor at the origin
    /// across the Z axis. It is a <see cref="Door"/> that rises 2.5 m in one second, and it subscribes to the topic
    /// <see cref="DoorwayTopic"/>.</item>
    /// <item>The <c>doorway-sensor</c> is an invisible static sensor box in front of the door, 2 m wide, 2 m tall and 1.5 m
    /// deep, from z = 1.75 m to 3.25 m. Its <see cref="Sensor"/> relays to <see cref="DoorwayTopic"/>, so the door opens
    /// while something is inside it. A blue <c>mat</c> on the floor marks it.</item>
    /// <item>The <c>person</c>, the capsule character of the orbit scene, 1.8 m tall, stands on the floor 6 m from the
    /// door along +Z and faces it, along -Z. With W it walks at 3 m/s toward the door, and with S away from it.</item>
    /// <item>The view <c>camera</c>, the camera at the start, stands 8 m to the side along +X, 3 m up, and looks at the
    /// sensor and the door.</item>
    /// <item>The light is the star <c>sun</c>, 1 AU from the origin along (30, 50, 20), up and to the side, which sends
    /// 1 W/m² there and casts shadows.</item>
    /// </list>
    /// </summary>
    private static SceneInstance Doorway(World world, IRenderResources resources)
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
                    Body = new(Shape: new BoxShapeDesc(new(25, 0.5f, 25)), Motion: BodyMotion.Static),
                    Render = Drawn(
                        resources: resources,
                        mesh: Primitives.Box(new(25, 0.5f, 25)),
                        color: new(x: 0.4f, y: 0.4f, z: 0.38f, w: 1),
                        boundingRadius: 36
                    ),
                }
            )
        );

        SpawnDoorAndSensor(world: world, resources: resources, scene: scene, ground: ground);

        // The capsule is 1.8 m tall, and its center rests 0.9 m above the floor. The eye is 1.65 m above the floor.
        var capsule = new CapsuleShapeDesc(0.6f, 0.3f);
        scene.Add(
            "person",
            world.Spawn(
                new()
                {
                    Frame = ground,
                    Pose = new(new(0, 0.95, DoorwayPersonStartZ), DQuaternion.Identity),
                    Body = new(Shape: capsule, Motion: BodyMotion.Character, Mass: YardPersonMass),
                    Render = Drawn(
                        resources: resources,
                        mesh: Primitives.Box(new(0.3f, 0.9f, 0.3f)),
                        color: new(x: 0.2f, y: 0.45f, z: 0.8f, w: 1),
                        boundingRadius: 1
                    ),
                    Components = [new PersonControl(new(0, 0.75f, 0))],
                    Subscriptions = [Topics.Input],
                }
            )
        );

        Entity camera = world.Spawn(ViewIn(ground, LookingAt(new(8, 3, 3), new(0, 1, 2))));
        scene.Add("camera", camera);

        Star(world: world, scene: scene, frame: ground, center: Double3.Zero, direction: new(30, 50, 20));
        world.Ambient = Daylight;

        scene.AddView("camera");
        scene.AddView("person");
        world.Camera = camera;
        return scene;
    }

    /// <summary>
    /// The kinematic <c>door</c> of the doorway, which its topic opens, the <c>doorway-sensor</c> in front of it, which
    /// sends on that topic, and the <c>mat</c> that marks the sensor.
    /// </summary>
    private static void SpawnDoorAndSensor(
        World world,
        IRenderResources resources,
        SceneInstance scene,
        FrameHandle ground
    )
    {
        var doorHalf = new Vector3(1, (float)DoorwayDoorShutY, 0.05f);
        scene.Add(
            "door",
            world.Spawn(
                new()
                {
                    Frame = ground,
                    Pose = new(new(0, DoorwayDoorShutY, 0), DQuaternion.Identity),
                    Body = new(Shape: new BoxShapeDesc(doorHalf), Motion: BodyMotion.Kinematic),
                    Render = Drawn(
                        resources: resources,
                        mesh: Primitives.Box(doorHalf),
                        color: new(x: 0.6f, y: 0.35f, z: 0.2f, w: 1),
                        boundingRadius: 1.6f
                    ),
                    Components = [new Door(new(0, DoorwayDoorRise, 0), openSeconds: 1)],
                    Subscriptions = [Topics.Get(DoorwayTopic)],
                }
            )
        );

        var sensorHalf = new Vector3(1, 1, DoorwaySensorHalfDepth);
        scene.Add(
            "doorway-sensor",
            world.Spawn(
                new()
                {
                    Frame = ground,
                    Pose = new(new(0, 1, DoorwaySensorZ), DQuaternion.Identity),
                    Body = new(Shape: new BoxShapeDesc(sensorHalf), Motion: BodyMotion.Static) { IsSensor = true },
                    Components = [new Sensor { Topic = DoorwayTopic }],
                }
            )
        );
        // The mat is only drawn: it has no body, so nothing touches it.
        var matHalf = new Vector3(1, 0.005f, DoorwaySensorHalfDepth);
        scene.Add(
            "mat",
            world.Spawn(
                new()
                {
                    Frame = ground,
                    Pose = new(new(0, 0.005, DoorwaySensorZ), DQuaternion.Identity),
                    Render = Drawn(
                        resources: resources,
                        mesh: Primitives.Box(matHalf),
                        color: new(x: 0.2f, y: 0.4f, z: 0.8f, w: 1),
                        boundingRadius: 1.3f
                    ),
                }
            )
        );
    }
}
