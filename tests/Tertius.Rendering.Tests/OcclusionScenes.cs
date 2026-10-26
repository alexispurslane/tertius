using System.Numerics;

namespace Tertius.Rendering.Tests;

/// <summary>
/// The scenes of the ambient occlusion tests (docs/plans/ambient-occlusion.md, "GPU tests of the acceptance criteria").
/// The corner is a floor, a back wall and a side wall that meet, seen from 30° above the horizontal from the render
/// origin. Each surface is a face of a box, so it has an inside behind it, as real geometry has.
/// </summary>
internal static class OcclusionScenes
{
    public const int Width = 640,
        Height = 360;

    /// <summary>The height of the floor's top face, in m.</summary>
    public const float FloorHeight = -1.5f;

    /// <summary>Where the back wall's front face stands, facing +Z.</summary>
    public const float BackWallZ = -5;

    /// <summary>Where the side wall's face stands, facing +X.</summary>
    public const float SideWallX = -2;

    /// <summary>The corner's camera: turned down 30° from looking along −Z.</summary>
    public static readonly Quaternion CornerView = Gpu.PitchDown(MathF.PI / 6);

    /// <summary>A floor point 0.1 m from both walls.</summary>
    public static readonly Vector3 FloorAtTheCorner = new(SideWallX + 0.1f, FloorHeight, BackWallZ + 0.1f);

    /// <summary>A floor point more than 3 m from both walls.</summary>
    public static readonly Vector3 OpenFloor = new(1.2f, FloorHeight, -1.9f);

    /// <summary>A light gray matte non-metal.</summary>
    public static MaterialId Matte(VulkanRenderer renderer) =>
        renderer.CreateMaterial(MaterialDesc.Color(new(x: 0.8f, y: 0.8f, z: 0.8f, w: 1), roughness: 0.8f));

    /// <summary>A snapshot whose only light is the ambient light of one white sky and ground, seen through <paramref name="view"/>.</summary>
    public static RenderSnapshot UnderWhiteAmbientLight(Quaternion view)
    {
        RenderSnapshot snapshot = Gpu.Snapshot(Vector3.UnitY, starIrradiance: 0);
        snapshot.Ambient = new()
        {
            SkyColor = Vector3.One,
            GroundColor = Vector3.One,
            Intensity = 1,
        };
        Look(snapshot, view);
        return snapshot;
    }

    /// <summary>Turns the camera of <paramref name="snapshot"/> to <paramref name="view"/>, with no turn since the tick before.</summary>
    public static void Look(RenderSnapshot snapshot, Quaternion view)
    {
        snapshot.Camera.Rotation = view;
        snapshot.Camera.PreviousRotation = view;
    }

    /// <summary>Adds the floor, the back wall and the side wall of the corner to <paramref name="snapshot"/>, and turns its camera to <see cref="CornerView"/>.</summary>
    public static RenderSnapshot Corner(VulkanRenderer renderer, MaterialId material, RenderSnapshot snapshot)
    {
        MeshId box = UnitBox(renderer);
        Look(snapshot, CornerView);
        AddSlab(
            snapshot: snapshot,
            box: box,
            material: material,
            center: new(0, FloorHeight - 0.5f, BackWallZ + 5),
            size: new(20, 1, 20)
        );
        AddSlab(
            snapshot: snapshot,
            box: box,
            material: material,
            center: new(0, FloorHeight + 5, BackWallZ - 0.5f),
            size: new(20, 20, 1)
        );
        AddSlab(
            snapshot: snapshot,
            box: box,
            material: material,
            center: new(SideWallX - 0.5f, FloorHeight + 5, BackWallZ + 5),
            size: new(1, 20, 20)
        );
        return snapshot;
    }

    /// <summary>A box of 1 m on each side, centered on the origin.</summary>
    public static MeshId UnitBox(VulkanRenderer renderer) => renderer.CreateMesh(Primitives.Box(new(0.5f)));

    /// <summary>Adds an axis-aligned box of <paramref name="size"/> m around <paramref name="center"/>.</summary>
    public static void AddSlab(
        RenderSnapshot snapshot,
        MeshId box,
        MaterialId material,
        Vector3 center,
        Vector3 size,
        RenderFlags flags = RenderFlags.None
    ) =>
        snapshot.Instances.Add(
            Gpu.Instance(
                mesh: box,
                material: material,
                position: center,
                scale: size,
                flags: flags,
                radius: size.Length() / 2
            )
        );

    /// <summary>The pixel that holds the projection of <paramref name="point"/> through <paramref name="view"/>, at 640 × 360.</summary>
    public static Vector2 PixelOf(Vector3 point, Quaternion view) => PixelOf(point, view, size: (Width, Height));

    /// <summary>The pixel that holds the projection of <paramref name="point"/> through <paramref name="view"/>, in an image of <paramref name="size"/>.</summary>
    public static Vector2 PixelOf(Vector3 point, Quaternion view, (int Width, int Height) size) =>
        BrdfTesting.Holding(Gpu.Project(point: point, cameraRotation: view, width: size.Width, height: size.Height));

    /// <summary>The occlusion of <paramref name="pixel"/> in <paramref name="occlusion"/>, an image of <paramref name="width"/> pixels per row.</summary>
    public static float OcclusionAt(float[] occlusion, Vector2 pixel, int width = Width) =>
        occlusion[(int)pixel.Y * width + (int)pixel.X];

    /// <summary>The largest difference of any channel of any pixel in <paramref name="pixels"/> between two captures, in steps of 1/255.</summary>
    public static int LargestDifference(Capture a, Capture b, IEnumerable<(int X, int Y)> pixels)
    {
        int largest = 0;
        foreach ((int x, int y) in pixels)
        {
            int i = (y * a.Width + x) * 4;
            for (int channel = 0; channel < 3; channel++)
                largest = Math.Max(largest, Math.Abs(a.Rgba[i + channel] - b.Rgba[i + channel]));
        }

        return largest;
    }

    /// <summary>Every pixel of a <paramref name="width"/> × <paramref name="height"/> image.</summary>
    public static IEnumerable<(int X, int Y)> EveryPixel(int width = Width, int height = Height)
    {
        for (int y = 0; y < height; y++)
        for (int x = 0; x < width; x++)
            yield return (x, y);
    }
}
