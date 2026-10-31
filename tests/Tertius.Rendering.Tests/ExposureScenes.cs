using System.Numerics;
using Tertius.Testing;

namespace Tertius.Rendering.Tests;

/// <summary>The exposure that one drawn frame of a film used, and the luma that it showed in the middle of the image.</summary>
/// <param name="Stops">The exposure in stops, from <see cref="RenderStats.Exposure"/>.</param>
/// <param name="Luma">The Rec. 709 luma of the middle pixel, from 0 to 1.</param>
internal readonly record struct FilmFrame(float Stops, float Luma);

/// <summary>
/// The scenes and the film of the automatic exposure's tests (docs/plans/auto-exposure.md, "Tests"). The camera is at the
/// render origin with identity rotation, so it looks down −Z. The sun shines from behind the camera, under the ambient
/// light of <see cref="Gpu.Snapshot"/>. No instance casts shadows.
/// </summary>
internal static class ExposureScenes
{
    public const int Width = 160,
        Height = 90;

    /// <summary>The ramp's fall in reflectance from its bright edge to its dark edge, in stops.</summary>
    public const int RampStops = 8;

    /// <summary>A square facing the camera, as the plane primitive faces +Y.</summary>
    public static readonly Quaternion FacingTheCamera = Quaternion.CreateFromAxisAngle(Vector3.UnitX, MathF.PI / 2);

    /// <summary>A square facing a camera that has turned around, behind it.</summary>
    public static readonly Quaternion FacingBehind = Quaternion.CreateFromAxisAngle(Vector3.UnitX, -MathF.PI / 2);

    /// <summary>A camera turned around, so it looks down +Z, away from the sun.</summary>
    public static readonly Quaternion TurnedAround = Quaternion.CreateFromAxisAngle(Vector3.UnitY, MathF.PI);

    /// <summary>Where the squares stand: 3 m ahead on the view axis.</summary>
    public static readonly Vector3 Ahead = new(0, 0, -3);

    /// <summary>Where the square behind the camera stands: 3 m behind it on the view axis.</summary>
    public static readonly Vector3 Behind = new(0, 0, 3);

    /// <summary>The middle pixel of the image.</summary>
    public static readonly Vector2 Middle = new(Width / 2, Height / 2);

    /// <summary>The half size of a square that fills the view at 3 m.</summary>
    public const float FillsTheView = 5;

    /// <summary>
    /// A validating headless renderer with one frame in flight, so each frame that is not captured reports the exposure of
    /// the frame before. A captured frame reports its own.
    /// </summary>
    public static VulkanRenderer Renderer(ExposureSetting exposure) =>
        Gpu.Headless(width: Width, height: Height, framesInFlight: 1, exposure: exposure);

    /// <summary>The snapshot with the sun shining from behind the camera, with no instance.</summary>
    public static RenderSnapshot Sunlit() => Gpu.Snapshot(Vector3.UnitZ);

    /// <summary>A matte gray square of roughness 1 and reflectance 0, facing the camera, so its pixels share one luminance.</summary>
    public static void AddGraySquare(VulkanRenderer renderer, RenderSnapshot snapshot, float halfSize = FillsTheView)
    {
        MaterialId gray = renderer.CreateMaterial(
            new(BaseColor: new(x: 0.5f, y: 0.5f, z: 0.5f, w: 1), Roughness: 1, Reflectance: 0)
        );
        snapshot.Instances.Add(
            Gpu.Instance(
                mesh: renderer.CreateMesh(Primitives.Plane(halfSize)),
                material: gray,
                position: Ahead,
                scale: Vector3.One,
                rotation: FacingTheCamera,
                chainDepth: TestChains.CamerasFrame
            )
        );
    }

    /// <summary>
    /// A matte square of roughness 1 and reflectance 0 that fills the view, whose reflectance falls smoothly by
    /// <see cref="RampStops"/> stops from one edge to the other. Its pixels spread over many bins of the histogram, so the
    /// trimmed mean moves smoothly with the exposure, and a settled exposure does not depend on the frames before it.
    /// </summary>
    public static void AddRampSquare(
        VulkanRenderer renderer,
        RenderSnapshot snapshot,
        Vector3 position,
        Quaternion rotation
    )
    {
        MaterialId ramp = renderer.CreateMaterial(
            new(BaseColor: Vector4.One, Roughness: 1, Reflectance: 0, BaseColorMap: renderer.CreateTexture(Ramp()))
        );
        snapshot.Instances.Add(
            Gpu.Instance(
                mesh: renderer.CreateMesh(Primitives.Plane(FillsTheView)),
                material: ramp,
                position: position,
                scale: Vector3.One,
                rotation: rotation,
                chainDepth: TestChains.CamerasFrame
            )
        );
    }

    /// <summary>A black square of reflectance 0 that emits <paramref name="radiance"/>, facing the camera.</summary>
    public static void AddGlowingSquare(
        VulkanRenderer renderer,
        RenderSnapshot snapshot,
        Vector3 position,
        float halfSize,
        Vector3 radiance
    ) =>
        snapshot.Instances.Add(
            Gpu.Instance(
                mesh: renderer.CreateMesh(Primitives.Plane(halfSize)),
                material: BrdfTesting.Glow(renderer, radiance),
                position: position,
                scale: Vector3.One,
                rotation: FacingTheCamera,
                chainDepth: TestChains.CamerasFrame
            )
        );

    /// <summary>
    /// A sunlit gray square in the middle of the view, 2 m wide, that covers about a fifth of it, in front of a glowing
    /// ground that fills the rest about 8 stops darker. The square's pixels are the brightest fifth of the surface.
    /// </summary>
    public static RenderSnapshot BrightObjectOverDarkGround(VulkanRenderer renderer)
    {
        RenderSnapshot snapshot = Sunlit();
        AddGlowingSquare(
            renderer: renderer,
            snapshot: snapshot,
            position: new(0, 0, -3.2f),
            halfSize: FillsTheView,
            radiance: new(0.002f)
        );
        AddGraySquare(renderer, snapshot, halfSize: 1);
        return snapshot;
    }

    /// <summary>
    /// Films <paramref name="frames"/> captured frames of <paramref name="snapshot"/>, the first at <paramref name="start"/> s
    /// of snapshot time and each next one <paramref name="frameSeconds"/> later, and returns the exposure and the middle
    /// luma of each.
    /// </summary>
    public static FilmFrame[] Film(
        VulkanRenderer renderer,
        RenderSnapshot snapshot,
        double start,
        double frameSeconds,
        int frames
    )
    {
        var film = new FilmFrame[frames];
        for (int frame = 0; frame < frames; frame++)
        {
            snapshot.Time = start + frame * frameSeconds;
            Capture image = Gpu.RenderAndCapture(renderer: renderer, snapshot: snapshot, name: "auto exposure film");
            // The capture waits until its frame has finished, so the stats report the frame's own exposure.
            film[frame] = new(Stops: renderer.LastFrameStats.Exposure!.Value.Stops, Luma: image.Luma(Middle));
        }

        return film;
    }

    /// <summary>The exposure and the luma of one frame of <paramref name="snapshot"/> at <paramref name="time"/>.</summary>
    public static FilmFrame Still(VulkanRenderer renderer, RenderSnapshot snapshot, double time) =>
        Film(renderer: renderer, snapshot: snapshot, start: time, frameSeconds: 0, frames: 1)[0];

    /// <summary>
    /// The exposure at which a new renderer under <paramref name="exposure"/> settles on the view that
    /// <paramref name="build"/> makes: its first frame's step is endless, so it reaches its target.
    /// </summary>
    public static float SettledStops(ExposureSetting exposure, Func<VulkanRenderer, RenderSnapshot> build) =>
        SettledStill(exposure: exposure, build: build).Stops;

    /// <summary>
    /// The first frame of a new renderer under <paramref name="exposure"/> on the view that <paramref name="build"/>
    /// makes, at the exposure on which it settles (<see cref="SettledStops"/>), and its luma.
    /// </summary>
    public static FilmFrame SettledStill(ExposureSetting exposure, Func<VulkanRenderer, RenderSnapshot> build)
    {
        VulkanRenderer renderer = Renderer(exposure);
        try
        {
            return Still(renderer: renderer, snapshot: build(renderer), time: 0);
        }
        finally
        {
            Gpu.DisposeAndAssertClean(renderer);
        }
    }

    /// <summary>S(t) = T + (S₀ − T) e^(−t / τ): the exposure after <paramref name="seconds"/> of the approach.</summary>
    public static double Approached(double from, double target, double seconds, double timeConstant) =>
        target + (from - target) * Math.Exp(-seconds / timeConstant);

    /// <summary>
    /// The ramp's texture: 256 texels wide, each column's reflectance 2^(−RampStops · u) times 0.9, stored with the sRGB
    /// curve.
    /// </summary>
    private static TextureData Ramp()
    {
        const int width = 256,
            height = 4;
        byte[] rgba = new byte[width * height * 4];
        for (int x = 0; x < width; x++)
        {
            double linear = 0.9 * Math.Pow(2, -RampStops * (x + 0.5) / width);
            double srgb = linear <= 0.0031308 ? 12.92 * linear : 1.055 * Math.Pow(linear, 1 / 2.4) - 0.055;
            byte value = (byte)Math.Round(srgb * 255);
            for (int y = 0; y < height; y++)
            {
                int i = (y * width + x) * 4;
                (rgba[i], rgba[i + 1], rgba[i + 2], rgba[i + 3]) = (value, value, value, 255);
            }
        }

        return new(Width: width, Height: height, Rgba: rgba);
    }
}
