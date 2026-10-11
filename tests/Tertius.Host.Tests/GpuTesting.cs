using System.Numerics;
using StbImageSharp;
using Tertius.Rendering;
using Tertius.Simulation;
using static Tertius.Host.Tests.HostTesting;

namespace Tertius.Host.Tests;

/// <summary>GPU tests run one at a time: they share one physical device.</summary>
[CollectionDefinition(Name)]
public sealed class GpuCollection
{
    public const string Name = "GPU";
}

/// <summary>One decoded RGBA image.</summary>
public sealed class Png(byte[] rgba, int width, int height)
{
    public int Width => width;
    public int Height => height;

    public static Png Load(string path)
    {
        ImageResult image = ImageResult.FromMemory(File.ReadAllBytes(path), ColorComponents.RedGreenBlueAlpha);
        return new Png(image.Data, image.Width, image.Height);
    }

    /// <summary>The color at a pixel, 0 to 255 per channel.</summary>
    public (int R, int G, int B) At(int x, int y)
    {
        int i = (y * width + x) * 4;
        return (rgba[i], rgba[i + 1], rgba[i + 2]);
    }

    /// <summary>The mean color of the 5 by 5 pixels centered on a pixel, 0 to 255 per channel.</summary>
    public (int R, int G, int B) Average(int x, int y)
    {
        int r = 0, g = 0, b = 0;
        for (int dy = -2; dy <= 2; dy++)
            for (int dx = -2; dx <= 2; dx++)
            {
                (int pr, int pg, int pb) = At(x + dx, y + dy);
                r += pr;
                g += pg;
                b += pb;
            }
        return (r / 25, g / 25, b / 25);
    }

    /// <summary>True if the pixel is clearly orange: red well above blue, and green above blue.</summary>
    public bool IsOrange(int x, int y)
    {
        (int r, int g, int b) = At(x, y);
        return r > 40 && r > 2 * b && g > b;
    }

    /// <summary>True if the pixel is clearly red: the red channel stands well above the other two.</summary>
    public bool IsRed(int x, int y)
    {
        (int r, int g, int b) = At(x, y);
        return r > 40 && r > 2 * g && r > 2 * b;
    }
}

/// <summary>Headless renderers and lit scenes for the GPU tests of scripted runs.</summary>
public static class GpuTesting
{
    /// <summary>A validating headless renderer with no impostors, as a scripted run makes one.</summary>
    public static VulkanRenderer Headless(int width, int height) =>
        VulkanRenderer.CreateHeadless(new RendererOptions { Validation = true, Width = width, Height = height, Impostors = false });

    private static DQuaternion TurnAboutY(double degrees) => DQuaternion.FromAxisAngle(new Double3(0, 1, 0), degrees * Math.PI / 180);

    private static Entity Camera(World world, FrameHandle frame, DPose pose) =>
        world.Spawn(new SpawnDesc { Frame = frame, Pose = pose, Render = new RenderData() });

    /// <summary>White ambient light from every side, so that every face shows its material's color, whatever the sun does.</summary>
    private static void LightEvenly(World world) =>
        world.Ambient = new AmbientLight { SkyColor = Vector3.One, GroundColor = Vector3.One, Intensity = 1 };

    private static RenderData Drawn(MeshId mesh, MaterialId material) =>
        new() { Mesh = mesh, Material = material, BoundingRadius = 1, Flags = RenderFlags.Visible };

    /// <summary>
    /// The scene "lit-box", with no physics. In the root frame <c>space</c>, a red 1 m <c>box</c> sits at the origin.
    /// The camera <c>front</c> stands at (0, 0, 4) and looks along -Z at the box. The camera <c>away</c> stands at the
    /// same place, turned 180 degrees about +Y, so it looks away. The scene starts with <c>front</c> as the camera, and
    /// lights every face evenly.
    /// </summary>
    public static SceneInstance LitBox(World world, IRenderResources resources)
    {
        var scene = new SceneInstance();
        FrameHandle space = world.CreateRootFrame(new FrameSpec(FrameBounds.Sphere(1e6f), MotionDriver.Static));
        scene.Add("space", space);
        MaterialId red = resources.CreateMaterial(MaterialDesc.Color(new Vector4(1, 0, 0, 1)));
        scene.Add("box", world.Spawn(new SpawnDesc { Frame = space, Render = Drawn(resources.CreateMesh(Primitives.Box(new Vector3(0.5f))), red) }));
        Entity front = Camera(world, space, At(0, 0, 4));
        scene.Add("front", front);
        scene.Add("away", Camera(world, space, new DPose(new Double3(0, 0, 4), TurnAboutY(180))));
        world.Camera = front;
        LightEvenly(world);
        return scene;
    }

    public static readonly Scene LitBoxScene = new("lit-box", LitBox);

    /// <summary>
    /// The scene "lit-drop", which needs physics. The root frame <c>ground</c> has 9.81 m/s² of gravity along -Y. A gray
    /// static <c>floor</c> has its top at y = 0, and a red 1 m <c>box</c> starts at y = 3, so it lands at y = 0.5 after
    /// about 0.7 s. The camera <c>eye</c> stands at (0, 1.5, 6) and looks along -Z. Every face is lit evenly.
    /// </summary>
    public static SceneInstance LitDrop(World world, IRenderResources resources)
    {
        var scene = new SceneInstance();
        FrameHandle ground = world.CreateRootFrame(new FrameSpec(FrameBounds.Sphere(1e6f), MotionDriver.Static, GravityField.Uniform(new Vector3(0, -9.81f, 0))));
        scene.Add("ground", ground);
        scene.Add("floor", world.Spawn(new SpawnDesc
        {
            Frame = ground,
            Pose = At(0, -0.5),
            Body = new BodyDesc(new BoxShapeDesc(new Vector3(50, 0.5f, 50)), BodyMotion.Static),
            Render = Drawn(resources.CreateMesh(Primitives.Box(new Vector3(50, 0.5f, 50))), resources.CreateMaterial(MaterialDesc.Color(new Vector4(0.3f, 0.3f, 0.3f, 1)))) with { BoundingRadius = 71 },
        }));
        scene.Add("box", world.Spawn(new SpawnDesc
        {
            Frame = ground,
            Pose = At(0, 3),
            Body = new BodyDesc(new BoxShapeDesc(new Vector3(0.5f)), Mass: 10),
            Render = Drawn(resources.CreateMesh(Primitives.Box(new Vector3(0.5f))), resources.CreateMaterial(MaterialDesc.Color(new Vector4(1, 0, 0, 1)))),
        }));
        Entity eye = Camera(world, ground, At(0, 1.5, 6));
        scene.Add("eye", eye);
        world.Camera = eye;
        LightEvenly(world);
        return scene;
    }

    public static readonly Scene LitDropScene = new("lit-drop", LitDrop);

    /// <summary>
    /// The pixel row at which a camera at height 1.5 m, looking along -Z with a vertical field of view of 60 degrees,
    /// sees a point 6 m in front of it at height <paramref name="y"/>.
    /// </summary>
    public static int RowOfHeight(double y, int height) =>
        (int)Math.Round((0.5 - (y - 1.5) / 6 / Math.Tan(Math.PI / 6) * 0.5) * height);
}
