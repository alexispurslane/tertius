using Tertius.Host;
using Tertius.Simulation;

namespace Tertius.Demo;

public static partial class DemoScenes
{
    /// <summary>
    /// The look of the flame's orange puffs: three times as hot as the air, from 0.25 m to 1.2 m over 3 s.
    /// </summary>
    private static PuffLook FlameLook { get; } =
        new(
            Life: 3,
            StartRadius: 0.25f,
            EndRadius: 1.2f,
            TemperatureRatio: 3,
            Shading: PuffShading.Glow(new(5100, 2040, 510))
        );

    /// <summary>
    /// A yard of glow puffs in air, to see the puffs of the engine (ticket ter-4zt6) through the real renderer.
    /// <list type="bullet">
    /// <item>The root frame <c>ground</c> has a uniform gravity of 9.81 m/s² along -Y and air of 1.225 kg/m³ with a drag of
    /// 1.5 per second, so a puff that is hotter than the air rises and one that is colder sinks. A gray floor 100 m on a
    /// side lies at height 0.</item>
    /// <item>The <c>flame</c>, 6 m to the left, blows orange puffs straight up at 6 m/s. Their own gas is three times as hot
    /// as the air, so they rise faster as they leave and spread into a column.</item>
    /// <item>The <c>blue-jet</c>, at the middle, blows blue puffs at 12 m/s to the right and 30 degrees up. Their gas is 1.6
    /// times as hot as the air, so the air bends their path up.</item>
    /// <item>The <c>cold-jet</c>, 6 m to the right, blows faint green puffs straight up at 5 m/s. Their gas is at 0.8 of the
    /// air's temperature, so the push of their weight and the air's drag stop them about a meter up, and they spread into
    /// a low dome over the post as they mix into the air.</item>
    /// <item>The <c>tossed-box</c>, an orange cube 1 m on a side, flies in an arc from the left over the yard and lands on
    /// the floor near the middle. A jet on it leaves a trail of orange puffs that marks its path for 2 s.</item>
    /// <item>The <c>camera</c> stands 22 m in front of the yard, 4 m up, and looks a little down at it.</item>
    /// </list>
    /// </summary>
    private static SceneInstance PuffYard(World world, IRenderResources resources)
    {
        var scene = new SceneInstance();
        FrameHandle ground = world.CreateRootFrame(
            new(FrameBounds.Sphere(1e6f), GravityField.Uniform(new(0, -9.81f, 0)), MediumField.Uniform(1.225f, 1.5f))
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
                        color: new(x: 0.3f, y: 0.3f, z: 0.32f, w: 1),
                        boundingRadius: 71
                    ),
                }
            )
        );

        SpawnPuffJets(world: world, resources: resources, scene: scene, ground: ground);
        SpawnTossedBox(world: world, resources: resources, scene: scene, ground: ground);

        Entity camera = world.Spawn(ViewIn(ground, new(new(0, 4, 22), DQuaternion.FromAxisAngle(new(1, 0, 0), -0.08))));
        scene.Add("camera", camera);
        world.Camera = camera;
        Star(world: world, scene: scene, frame: ground, center: Double3.Zero, direction: new(30, 50, 40));
        // The glow puffs and the ambient light, in W/(m²·sr), keep the share of the Sun's light that the scene was judged
        // with, beside sunlight of 0.4 W/m².
        world.Ambient = new()
        {
            SkyColor = new(270),
            GroundColor = new(100),
            Intensity = 1,
        };
        return scene;
    }

    /// <summary>
    /// The <c>flame</c>, the <c>blue-jet</c> and the <c>cold-jet</c> of the puff yard, each on a post on the floor.
    /// </summary>
    private static void SpawnPuffJets(World world, IRenderResources resources, SceneInstance scene, FrameHandle ground)
    {
        var blue = new PuffLook(
            Life: 2.5f,
            StartRadius: 0.15f,
            EndRadius: 0.8f,
            TemperatureRatio: 1.6f,
            Shading: PuffShading.Glow(new(850, 1700, 5100))
        );
        var cold = new PuffLook(
            Life: 4,
            StartRadius: 0.2f,
            EndRadius: 1f,
            TemperatureRatio: 0.8f,
            Shading: PuffShading.Glow(new(510, 2040, 680))
        );
        scene.Add(
            "flame",
            Nozzle(
                world: world,
                resources: resources,
                frame: ground,
                place: new(-6, 0, 0),
                direction: Double3.UnitY,
                emitter: new(Rate: 60, Speed: 6, Spread: 0.15f, Look: FlameLook)
            )
        );
        scene.Add(
            "blue-jet",
            Nozzle(
                world: world,
                resources: resources,
                frame: ground,
                place: new(0, 0, 0),
                direction: new(Math.Cos(Math.PI / 6), Math.Sin(Math.PI / 6), 0),
                emitter: new(Rate: 80, Speed: 12, Spread: 0.08f, Look: blue)
            )
        );
        scene.Add(
            "cold-jet",
            Nozzle(
                world: world,
                resources: resources,
                frame: ground,
                place: new(6, 0, 0),
                direction: Double3.UnitY,
                emitter: new(Rate: 50, Speed: 5, Spread: 0.25f, Look: cold)
            )
        );
    }

    /// <summary>
    /// The <c>tossed-box</c> of the puff yard, which leaves a trail of orange puffs on its arc over the yard.
    /// </summary>
    private static void SpawnTossedBox(World world, IRenderResources resources, SceneInstance scene, FrameHandle ground)
    {
        RenderData box = Drawn(
            resources: resources,
            mesh: Primitives.Box(new(0.5f)),
            color: new(x: 1, y: 0.45f, z: 0.1f, w: 1),
            boundingRadius: 0.87f
        );
        box.Emitter = new(
            Rate: 90,
            Speed: 1,
            Spread: MathF.PI / 2,
            Look: FlameLook with
            {
                Life = 2,
                EndRadius = 0.8f,
            }
        );
        scene.Add(
            "tossed-box",
            world.Spawn(
                new()
                {
                    Frame = ground,
                    Pose = new(new(-12, 2, -6), DQuaternion.Identity),
                    LinearVelocity = new(7, 9, 2),
                    Body = new(Shape: new BoxShapeDesc(new(0.5f)), Mass: 10),
                    Render = box,
                }
            )
        );
    }

    /// <summary>
    /// A dark post 0.6 m tall at <paramref name="place"/> on the floor, with a jet on its top that blows its puffs along
    /// <paramref name="direction"/>, a unit vector in the frame's axes. A jet blows along its entity's -Z, so the post turns
    /// its -Z onto the direction.
    /// </summary>
    private static Entity Nozzle(
        World world,
        IRenderResources resources,
        FrameHandle frame,
        Double3 place,
        Double3 direction,
        PuffEmitter emitter
    )
    {
        var minusZ = new Double3(0, 0, -1);
        var axis = Double3.Cross(minusZ, direction);
        DQuaternion aim =
            axis.Length < 1e-9
                ? DQuaternion.Identity
                : DQuaternion.FromAxisAngle(
                    axis.Normalized(),
                    Math.Acos(Math.Clamp(Double3.Dot(minusZ, direction), -1, 1))
                );
        world.Spawn(
            new()
            {
                Frame = frame,
                Pose = new(place + new Double3(0, 0.3, 0), DQuaternion.Identity),
                Render = Drawn(
                    resources: resources,
                    mesh: Primitives.Box(new(0.15f, 0.3f, 0.15f)),
                    color: new(x: 0.12f, y: 0.12f, z: 0.12f, w: 1),
                    boundingRadius: 0.4f
                ),
            }
        );
        return world.Spawn(
            new()
            {
                Frame = frame,
                Pose = new(place + new Double3(0, 0.65, 0), aim),
                Render = new RenderData { Emitter = emitter },
            }
        );
    }
}
