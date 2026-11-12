using System.Numerics;
using StbImageSharp;
using Tertius.Rendering;
using Tertius.Simulation;
using static Tertius.Host.Tests.HostTesting;

namespace Tertius.Host.Tests;

/// <summary>
/// The cost measurements run alone, after every other test, so that no other test shares the cores and the GPU while they
/// time. The other GPU tests run in parallel: each makes its own headless renderer with its own Vulkan device.
/// </summary>
[CollectionDefinition(Name, DisableParallelization = true)]
public sealed class MeasurementCollection
{
    public const string Name = "Cost measurements";
}

/// <summary>One decoded RGBA image.</summary>
public sealed class Png(byte[] rgba, int width, int height)
{
    public int Width => width;
    public int Height => height;

    public static Png Load(string path)
    {
        var image = ImageResult.FromMemory(File.ReadAllBytes(path), ColorComponents.RedGreenBlueAlpha);
        return new(image.Data, image.Width, image.Height);
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
        int r = 0,
            g = 0,
            b = 0;
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
    public static readonly Scene LitBoxScene = new("lit-box", LitBox);

    public static readonly Scene LitDropScene = new("lit-drop", LitDrop);

    public static readonly Scene DarkAndBrightScene = new("dark-and-bright", DarkAndBright);

    /// <summary>
    /// A validating headless renderer with no impostors, as a scripted run makes one, with no screen tracer, so that the
    /// tests see the ambient light in full and a still draws once, and no environment map. The exposure is fixed at 0
    /// stops unless asked for, so that a pixel does not depend on the frames before it. The skin scatter is off, as in the
    /// renderer tests' fixtures. Local shadows are off, as the impostors are. A still draws once under the fixed exposure.
    /// The direct pass shades every light pair in full, so that a pixel holds the exact light.
    /// </summary>
    public static VulkanRenderer Headless(int width, int height, ExposureSetting exposure = default) =>
        VulkanRenderer.CreateHeadless(
            new()
            {
                Validation = true,
                Width = width,
                Height = height,
                Impostors = false,
                ScreenTrace = new NoScreenTrace(),
                Exposure = exposure,
                Bloom = new NoBloom(),
                Upscaling = new NoUpscaling(),
                LocalShadows = new NoLocalShadows(),
                EnvironmentMap = new NoEnvironmentMap(),
                Subsurface = new NoSubsurface(),
                LightPairs = new EveryLightPair(),
            }
        );

    /// <summary>
    /// A validating headless renderer that shows a demo scene in SI units as a scripted run does: with the renderer's
    /// default automatic exposure, environment map and shadows, so that a still after a long step shows the exposure
    /// settled, the map lights a cabin from what lies around the camera, and a lamp that casts shadows lights no far side
    /// of a wall. The other passes are off, as in <see cref="Headless"/>.
    /// </summary>
    public static VulkanRenderer LikeAScriptedRun(int width, int height) =>
        VulkanRenderer.CreateHeadless(LikeAScriptedRunOptions(width, height));

    /// <summary>
    /// The options of <see cref="LikeAScriptedRun"/>: a scripted run's, which keep the renderer's defaults, with the
    /// passes that the tests leave out turned off.
    /// </summary>
    public static RendererOptions LikeAScriptedRunOptions(int width, int height) =>
        new()
        {
            Validation = true,
            Width = width,
            Height = height,
            Impostors = false,
            ScreenTrace = new NoScreenTrace(),
            Bloom = new NoBloom(),
            Upscaling = new NoUpscaling(),
            Subsurface = new NoSubsurface(),
        };

    private static DQuaternion TurnAboutY(double degrees) =>
        DQuaternion.FromAxisAngle(new(0, 1, 0), degrees * Math.PI / 180);

    private static Entity Camera(World world, FrameHandle frame, DPose pose) =>
        world.Spawn(
            new()
            {
                Frame = frame,
                Pose = pose,
                Render = new RenderData(),
            }
        );

    /// <summary>White ambient light from every side, so that every face shows its material's color, whatever the sun does.</summary>
    private static void LightEvenly(World world) =>
        world.Ambient = new()
        {
            SkyColor = Vector3.One,
            GroundColor = Vector3.One,
            Intensity = 1,
        };

    private static RenderData Drawn(MeshId mesh, MaterialId material) =>
        new()
        {
            Mesh = mesh,
            Material = material,
            BoundingRadius = 1,
            Flags = RenderFlags.Visible,
        };

    /// <summary>
    /// The scene "lit-box", with no physics. In the root frame <c>space</c>, a red 1 m <c>box</c> sits at the origin.
    /// The camera <c>front</c> stands at (0, 0, 4) and looks along -Z at the box. The camera <c>away</c> stands at the
    /// same place, turned 180 degrees about +Y, so it looks away. The scene starts with <c>front</c> as the camera, and
    /// lights every face evenly.
    /// </summary>
    public static SceneInstance LitBox(World world, IRenderResources resources)
    {
        var scene = new SceneInstance();
        FrameHandle space = world.CreateRootFrame(new(FrameBounds.Sphere(1e6f)));
        scene.Add("space", space);
        MaterialId red = resources.CreateMaterial(MaterialDesc.Color(new(x: 1, y: 0, z: 0, w: 1), roughness: 0.38f));
        scene.Add(
            "box",
            world.Spawn(new() { Frame = space, Render = Drawn(resources.CreateMesh(Primitives.Box(new(0.5f))), red) })
        );
        Entity front = Camera(world, space, At(0, 0, 4));
        scene.Add("front", front);
        scene.Add("away", Camera(world, space, new(new(0, 0, 4), TurnAboutY(180))));
        world.Camera = front;
        LightEvenly(world);
        return scene;
    }

    /// <summary>
    /// The scene "dark-and-bright", with no physics. In the root frame <c>space</c>, a nearly black 1 m <c>dark-box</c>
    /// sits at the origin, and a nearly white 1 m <c>bright-box</c> at (0, 0, 8). The camera <c>dark</c> stands at
    /// (0, 0, 4) and looks along -Z at the dark box. The camera <c>bright</c> stands at the same place, turned 180 degrees
    /// about +Y, so it looks at the bright box, about 5 stops brighter. The scene starts with <c>dark</c> as the camera,
    /// and lights every face evenly.
    /// </summary>
    public static SceneInstance DarkAndBright(World world, IRenderResources resources)
    {
        var scene = new SceneInstance();
        FrameHandle space = world.CreateRootFrame(new(FrameBounds.Sphere(1e6f)));
        scene.Add("space", space);
        MeshId box = resources.CreateMesh(Primitives.Box(new(0.5f)));
        MaterialId black = resources.CreateMaterial(
            MaterialDesc.Color(new(x: 0.03f, y: 0.03f, z: 0.03f, w: 1), roughness: 0.38f)
        );
        MaterialId white = resources.CreateMaterial(
            MaterialDesc.Color(new(x: 0.9f, y: 0.9f, z: 0.9f, w: 1), roughness: 0.38f)
        );
        scene.Add("dark-box", world.Spawn(new() { Frame = space, Render = Drawn(box, black) }));
        scene.Add(
            "bright-box",
            world.Spawn(
                new()
                {
                    Frame = space,
                    Pose = At(0, 0, 8),
                    Render = Drawn(box, white),
                }
            )
        );
        Entity dark = Camera(world, space, At(0, 0, 4));
        scene.Add("dark", dark);
        scene.Add("bright", Camera(world, space, new(new(0, 0, 4), TurnAboutY(180))));
        world.Camera = dark;
        LightEvenly(world);
        return scene;
    }

    /// <summary>
    /// The scene "lit-drop", which needs physics. The root frame <c>ground</c> has 9.81 m/s² of gravity along -Y. A gray
    /// static <c>floor</c> has its top at y = 0, and a red 1 m <c>box</c> starts at y = 3, so it lands at y = 0.5 after
    /// about 0.7 s. The camera <c>eye</c> stands at (0, 1.5, 6) and looks along -Z. Every face is lit evenly.
    /// </summary>
    public static SceneInstance LitDrop(World world, IRenderResources resources)
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
                    Pose = At(0, -0.5),
                    Body = new(Shape: new BoxShapeDesc(new(50, 0.5f, 50)), Motion: BodyMotion.Static),
                    Render = Drawn(
                        resources.CreateMesh(Primitives.Box(new(50, 0.5f, 50))),
                        resources.CreateMaterial(
                            MaterialDesc.Color(new(x: 0.3f, y: 0.3f, z: 0.3f, w: 1), roughness: 0.38f)
                        )
                    ) with
                    {
                        BoundingRadius = 71,
                    },
                }
            )
        );
        scene.Add(
            "box",
            world.Spawn(
                new()
                {
                    Frame = ground,
                    Pose = At(0, 3),
                    Body = new(Shape: new BoxShapeDesc(new(0.5f)), Mass: 10),
                    Render = Drawn(
                        resources.CreateMesh(Primitives.Box(new(0.5f))),
                        resources.CreateMaterial(MaterialDesc.Color(new(x: 1, y: 0, z: 0, w: 1), roughness: 0.38f))
                    ),
                }
            )
        );
        Entity eye = Camera(world, ground, At(0, 1.5, 6));
        scene.Add("eye", eye);
        world.Camera = eye;
        LightEvenly(world);
        return scene;
    }

    /// <summary>
    /// The pixel row at which a camera at height 1.5 m, looking along -Z with a vertical field of view of 60 degrees,
    /// sees a point 6 m in front of it at height <paramref name="y"/>.
    /// </summary>
    public static int RowOfHeight(double y, int height) =>
        (int)Math.Round((0.5 - (y - 1.5) / 6 / Math.Tan(Math.PI / 6) * 0.5) * height);
}
