using Tertius.Host;
using Tertius.Simulation;

namespace Tertius.Demo;

public static partial class DemoScenes
{
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
        FrameHandle ground = world.CreateRootFrame(new(FrameBounds.Sphere(1e6f),
            GravityField.Uniform(new(0, -9.81f, 0)),
            MediumField.Uniform(1.225f, 1.5f)));
        scene.Add("ground", ground);
        scene.Add("floor", world.Spawn(new()
        {
            Frame = ground,
            Pose = new(new(0, -0.5, 0), DQuaternion.Identity),
            Body = new(new BoxShapeDesc(new(50, 0.5f, 50)), BodyMotion.Static),
            Render = Drawn(resources, Primitives.Box(new(50, 0.5f, 50)), new(0.3f, 0.3f, 0.32f, 1), boundingRadius: 71)
        }));

        var flame = new PuffLook(Life: 3, StartRadius: 0.25f, EndRadius: 1.2f, TemperatureRatio: 3,
            PuffShading.Glow(new(1.5f, 0.6f, 0.15f)));
        var blue = new PuffLook(Life: 2.5f, StartRadius: 0.15f, EndRadius: 0.8f, TemperatureRatio: 1.6f,
            PuffShading.Glow(new(0.25f, 0.5f, 1.5f)));
        var cold = new PuffLook(Life: 4, StartRadius: 0.2f, EndRadius: 1f, TemperatureRatio: 0.8f,
            PuffShading.Glow(new(0.15f, 0.6f, 0.2f)));
        scene.Add("flame", Nozzle(world, resources, ground, new(-6, 0, 0), Double3.UnitY, new(60, 6, 0.15f, flame)));
        scene.Add("blue-jet", Nozzle(world, resources, ground, new(0, 0, 0),
            new(Math.Cos(Math.PI / 6), Math.Sin(Math.PI / 6), 0),
            new(80, 12, 0.08f, blue)));
        scene.Add("cold-jet", Nozzle(world, resources, ground, new(6, 0, 0), Double3.UnitY, new(50, 5, 0.25f, cold)));

        RenderData box = Drawn(resources, Primitives.Box(new(0.5f)), new(1, 0.45f, 0.1f, 1), boundingRadius: 0.87f);
        box.Emitter = new(90, 1, MathF.PI / 2, flame with { Life = 2, EndRadius = 0.8f });
        scene.Add("tossed-box", world.Spawn(new()
        {
            Frame = ground,
            Pose = new(new(-12, 2, -6), DQuaternion.Identity),
            LinearVelocity = new(7, 9, 2),
            Body = new(new BoxShapeDesc(new(0.5f)), Mass: 10),
            Render = box
        }));

        Entity camera = world.Spawn(new()
        {
            Frame = ground,
            Pose = new(new(0, 4, 22), DQuaternion.FromAxisAngle(new(1, 0, 0), -0.08)),
            Render = new RenderData()
        });
        scene.Add("camera", camera);
        world.Camera = camera;
        Entity sun = world.Spawn(new()
        {
            Frame = ground,
            Pose = new(new(30, 50, 40), DQuaternion.Identity),
            AsFrame = new(FrameBounds.Sphere(1))
        });
        scene.Add("sun", sun);
        world.SunFrame = world.GetOwnedFrame(sun);
        world.SunIntensity = 0.4f;
        return scene;
    }

    /// <summary>
    /// A dark post 0.6 m tall at <paramref name="place"/> on the floor, with a jet on its top that blows its puffs along
    /// <paramref name="direction"/>, a unit vector in the frame's axes. A jet blows along its entity's -Z, so the post turns
    /// its -Z onto the direction.
    /// </summary>
    private static Entity Nozzle(World world, IRenderResources resources, FrameHandle frame, Double3 place,
        Double3 direction, PuffEmitter emitter)
    {
        var minusZ = new Double3(0, 0, -1);
        Double3 axis = Double3.Cross(minusZ, direction);
        DQuaternion aim = axis.Length < 1e-9
            ? DQuaternion.Identity
            : DQuaternion.FromAxisAngle(axis.Normalized(),
                Math.Acos(Math.Clamp(Double3.Dot(minusZ, direction), -1, 1)));
        world.Spawn(new()
        {
            Frame = frame,
            Pose = new(place + new Double3(0, 0.3, 0), DQuaternion.Identity),
            Render = Drawn(resources, Primitives.Box(new(0.15f, 0.3f, 0.15f)), new(0.12f, 0.12f, 0.12f, 1),
                boundingRadius: 0.4f)
        });
        return world.Spawn(new()
        {
            Frame = frame,
            Pose = new(place + new Double3(0, 0.65, 0), aim),
            Render = new RenderData { Emitter = emitter }
        });
    }
}
