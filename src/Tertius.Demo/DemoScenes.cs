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

    /// <summary>The radius of the ship's orbit, 400 km above the surface, in meters.</summary>
    public const double OrbitRadius = EarthRadius + 400e3;

    /// <summary>
    /// The vertical slice's first scene: a ship on a round orbit 400 km above an Earth-sized planet, its nose along the
    /// orbit and its belly toward the planet. The planet is the root frame <c>earth</c>, with Earth's radius and gravity
    /// and a grid of 10-degree lines, so that its motion below the ship shows. The ship is the frame <c>ship</c>, a 20 by 8
    /// by 60 m hull in a bubble of radius 100 m. It starts at (r, 0, 0) and goes round counterclockwise seen from +Y, once
    /// in about 92 minutes.
    /// <list type="bullet">
    /// <item>The view <c>chase</c>, the camera at the start, rides in the ship's frame, 70 m behind it and 12 m above it,
    /// and looks at the ship's center.</item>
    /// <item>The view <c>far</c> stands in the planet's frame, three Earth radii from its center, 30 degrees above the
    /// orbit's plane, and looks at the center. The ship is too small to see from there.</item>
    /// </list>
    /// The frame <c>sun</c>, 1 AU away, is where the light comes from, so the ship has day and night. It has the real sun's
    /// radius, so the light has the real sun's angular size.
    /// </summary>
    private static SceneInstance Orbit(World world, IRenderResources resources)
    {
        var scene = new SceneInstance();
        FrameHandle earth = world.CreateRootFrame(
            new FrameSpec(FrameBounds.Sphere(1e9f), MotionDriver.Static, GravityField.Point(EarthMu)),
            new RenderData
            {
                Mesh = resources.CreateMesh(Primitives.Sphere((float)EarthRadius, 256, 128)),
                Material = resources.CreateMaterial(new MaterialDesc(Vector4.One, SpecularIntensity: 0.1f, Albedo: resources.CreateTexture(EarthGrid()))),
                BoundingRadius = (float)EarthRadius,
                Flags = RenderFlags.Visible | RenderFlags.Celestial,
            });
        scene.Add("earth", earth);

        Entity ship = world.Spawn(new SpawnDesc
        {
            Frame = earth,
            Body = new BodyDesc(new BoxShapeDesc(new Vector3(10, 4, 30)), Mass: 1e5f),
            // The bubble holds the hull and the chase camera, 71 m from the ship's center.
            AsFrame = new FrameSpec(FrameBounds.Sphere(100), MotionDriver.Kepler,
                Orbit: KeplerOrbit.Circular(OrbitRadius, EarthMu) with { Attitude = KeplerAttitude.FacesParent }),
            Render = Drawn(resources, Primitives.Box(new Vector3(10, 4, 30)), new Vector4(0.8f, 0.8f, 0.82f, 1), boundingRadius: 32),
        });
        scene.Add("ship", ship);

        Entity chase = world.Spawn(new SpawnDesc
        {
            Frame = world.GetOwnedFrame(ship),
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
        world.Camera = chase;

        Entity sun = world.Spawn(new SpawnDesc
        {
            Frame = earth,
            Pose = new DPose(new Double3(1, 0.3, 0.6).Normalized() * 1.496e11, DQuaternion.Identity),
            AsFrame = new FrameSpec(FrameBounds.Sphere(1), MotionDriver.Static),
            // The sun's radius gives the light its angular size, and so the width of the penumbra at the terminator. The
            // sun is not drawn.
            Render = new RenderData { BoundingRadius = 6.957e8f },
        });
        scene.Add("sun", sun);
        world.SunFrame = world.GetOwnedFrame(sun);
        // Sunlight above the atmosphere is bright.
        world.SunIntensity = 2.5f;
        return scene;
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
