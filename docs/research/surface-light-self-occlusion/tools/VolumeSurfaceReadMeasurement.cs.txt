using System.Numerics;
using Tertius.Testing;

namespace Tertius.Rendering.Tests;

/// <summary>MEASUREMENT ONLY (issue #49, stop 1): prints the numbers, asserts nothing.</summary>
[Collection(GpuCollection.Name)]
public sealed class VolumeSurfaceReadTests
{
    private const int Size = 64;

    private static VulkanRenderer CreateVolumeRenderer() =>
        VulkanRenderer.CreateHeadless(
            new RendererOptions
            {
                Width = Size,
                Height = Size,
                WarmUpFrames = 0,
                Validation = true,
                Impostors = false,
                Upscaling = new NoUpscaling(),
                Exposure = new FixedExposure(0),
                EnvironmentMap = new NoEnvironmentMap(),
                ScreenTrace = new NoScreenTrace(),
                LightVolume = true,
            }
        );

    private static RenderInstance Panel(MeshId mesh, MaterialId material, Vector3 position, Vector3 scale, uint entity,
        Quaternion? rotation = null) =>
        Gpu.Instance(
            mesh: mesh,
            material: material,
            position: position,
            scale: scale,
            chainDepth: TestChains.CamerasFrame,
            entity: entity,
            rotation: rotation
        );

    private static void Log(string line)
    {
        TestContext.Current.TestOutputHelper!.WriteLine(line);
        string path = Environment.GetEnvironmentVariable("ISSUE49_LOG") ?? "/dev/null";
        File.AppendAllText(path, line + "\n");
    }

    // The plane point that pixel (px, py) sees on the plane z = -depth.
    private static Vector2 PlanePoint(int px, int py, float depth, float fov = Gpu.Fov)
    {
        float tan = MathF.Tan(fov / 2);
        float ndcX = (px + 0.5f) / Size * 2 - 1;
        float ndcY = 1 - (py + 0.5f) / Size * 2;
        return new(ndcX * tan * depth, ndcY * tan * depth);
    }

    // Irradiance at (x, y) on a plane from a square Lambertian emitter of radiance l, side s, centered on the axis, at
    // distance h on a parallel plane facing it: E = L ∫ h² / r⁴ dA, by 64 × 64 midpoints.
    private static double Irradiance(double x, double y, double l, double s, double h)
    {
        const int n = 64;
        double sum = 0, d = s / n;
        for (int i = 0; i < n; i++)
        for (int j = 0; j < n; j++)
        {
            double ex = -s / 2 + (i + 0.5) * d, ey = -s / 2 + (j + 0.5) * d;
            double r2 = (x - ex) * (x - ex) + (y - ey) * (y - ey) + h * h;
            sum += h * h / (r2 * r2);
        }
        return l * sum * d * d;
    }

    [Theory]
    [InlineData(0.25f)]
    [InlineData(0.5f)]
    [InlineData(1f)]
    public void KnownAnswer(float cell)
    {
        VulkanRenderer renderer = CreateVolumeRenderer();
        try
        {
            MeshId box = renderer.CreateMesh(Primitives.Box(new(0.5f)));
            MeshId plane = renderer.CreateMesh(Primitives.Plane(0.5f));
            MaterialId white = renderer.CreateMaterial(new(BaseColor: Vector4.One, Roughness: 1, Reflectance: 0));
            MaterialId glow = renderer.CreateMaterial(
                new(BaseColor: Vector4.UnitW, Roughness: 1, Reflectance: 0, Emissive: new(8, 0, 0))
            );
            RenderSnapshot scene = Gpu.Snapshot(Vector3.UnitZ);
            scene.Lights.Clear();
            scene.Ambient = default;
            float front = 4.32f * cell, emitterZ = 3.68f * cell, side = cell;
            scene.Instances.Add(Panel(mesh: box, material: white, position: new(0, 0, -front - 0.08f * cell), scale: new(8 * cell, 8 * cell, 0.16f * cell), entity: 1));
            scene.Instances.Add(
                Panel(mesh: plane, material: glow, position: new(0, 0, emitterZ), scale: new(side), entity: 2, rotation: Quaternion.CreateFromAxisAngle(Vector3.UnitX, -MathF.PI / 2))
            );
            renderer.Render(scene, 1f);
            float[] hdr = renderer.ReadHdr();
            double measured = 0, exact = 0, sq = 0;
            for (int py = 0; py < Size; py++)
            for (int px = 0; px < Size; px++)
            {
                Vector2 p = PlanePoint(px: px, py: py, depth: front);
                double e = Irradiance(x: p.X, y: p.Y, l: 8, s: side, h: front + emitterZ);
                double m = Math.PI * hdr[4 * (py * Size + px)];
                measured += m;
                exact += e;
                sq += (m - e) * (m - e);
            }
            measured /= Size * Size;
            exact /= Size * Size;
            double center = Math.PI * hdr[4 * (32 * Size + 32)];
            double exactCenter = Irradiance(x: PlanePoint(px: 32, py: 32, depth: front).X, y: PlanePoint(px: 32, py: 32, depth: front).Y, l: 8, s: side, h: front + emitterZ);
            Log(
                $"known-answer cell {cell}: mean measured {measured:F5} exact {exact:F5} ratio {measured / exact:F4}; center {center:F5} exact {exactCenter:F5} ratio {center / exactCenter:F4}; rms {Math.Sqrt(sq / (Size * Size)):F5}"
            );

            // A chip: a 0.02-cell square facing +z in the open cell just in front of the receiver's center.
            scene.Time += 1.0 / 60;
            scene.Instances.Add(
                Panel(mesh: plane, material: white, position: new(0.1f * cell, 0.1f * cell, -3.6f * cell), scale: new(0.05f * cell), entity: 3,
                    rotation: Quaternion.CreateFromAxisAngle(Vector3.UnitX, MathF.PI / 2))
            );
            renderer.Render(scene, 1f);
            float[] chip = renderer.ReadHdr();
            double before = 0, after = 0, worst = 0;
            for (int py = 24; py < 40; py++)
            for (int px = 24; px < 40; px++)
            {
                int i = 4 * (py * Size + px);
                if (Math.Abs(px - 33) <= 1 && Math.Abs(py - 31) <= 1)
                    continue;
                before += hdr[i];
                after += chip[i];
                worst = Math.Max(worst, Math.Abs(chip[i] - hdr[i]) / hdr[i]);
            }
            Log($"chip cell {cell}: central mean change {(after - before) / before:P3}, worst pixel {worst:P3}");
        }
        finally
        {
            Gpu.DisposeAndAssertClean(renderer);
        }
    }

    [Fact]
    public void ThinWall()
    {
        VulkanRenderer renderer = CreateVolumeRenderer();
        try
        {
            MeshId box = renderer.CreateMesh(Primitives.Box(new(0.5f)));
            MaterialId gray = renderer.CreateMaterial(new(BaseColor: new(x: 0.8f, y: 0.8f, z: 0.8f, w: 1), Roughness: 1, Reflectance: 0));
            RenderSnapshot scene = Gpu.Snapshot(-Vector3.UnitZ);
            scene.Lights.Clear();
            scene.Lights.Add(
                Gpu.StarLight(
                    direction: -Vector3.UnitZ,
                    irradiance: 3,
                    angularRadius: Gpu.StarAngularRadius,
                    color: new(1, 0.4f, 0),
                    chainDepth: TestChains.Root
                )
            );
            scene.Ambient = default;
            scene.Instances.Add(Panel(mesh: box, material: gray, position: new(0, 0, -1.10f), scale: new(1, 1, 0.04f), entity: 1));
            renderer.Render(scene, 1f);
            float[] hdr = renderer.ReadHdr();
            double lit = 0.8 * 3 / Math.PI;
            double face = 0, middle = 0, band = 0, bandMax = 0;
            int nf = 0, nm = 0, nb = 0;
            for (int py = 0; py < Size; py++)
            for (int px = 0; px < Size; px++)
            {
                Vector2 p = PlanePoint(px: px, py: py, depth: 1.08f);
                float r = hdr[4 * (py * Size + px)];
                if (MathF.Abs(p.X) > 0.48f || MathF.Abs(p.Y) > 0.48f)
                    continue;
                face += r;
                nf++;
                if (MathF.Abs(p.X) < 0.25f && MathF.Abs(p.Y) < 0.25f)
                {
                    middle += r;
                    nm++;
                }
                if (MathF.Abs(p.X) > 0.40f && MathF.Abs(p.Y) < 0.25f)
                {
                    band += r;
                    nb++;
                    bandMax = Math.Max(bandMax, r);
                }
            }
            Log(
                $"thin wall: lit face radiance {lit:F4}; unlit face mean {face / nf:F5} ({face / nf / lit:P2} of lit), middle {middle / nm:F5}, edge band (|x|>0.40) mean {band / nb:F5} max {bandMax:F5}"
            );
            string row = string.Join(" ", Enumerable.Range(0, Size).Select(px => hdr[4 * (32 * Size + px)].ToString("F3")));
            Log($"thin wall row 32: {row}");
        }
        finally
        {
            Gpu.DisposeAndAssertClean(renderer);
        }
    }

    [Theory]
    [InlineData(0.25f, 1f, 7f, 4.32f, 3.68f, 120f)]
    [InlineData(0.5f, 1f, 7f, 4.32f, 3.68f, 120f)]
    [InlineData(1f, 1f, 7f, 4.32f, 3.68f, 120f)]
    [InlineData(0.25f, 16f, 8f, 4.32f, 0.32f, 60f)]
    [InlineData(0.5f, 16f, 8f, 4.32f, 0.32f, 60f)]
    [InlineData(1f, 16f, 8f, 4.32f, 0.32f, 60f)]
    public void Wider(float cell, float emitterSide, float receiverHalf, float frontCells, float emitterCells, float fovDegrees)
    {
        VulkanRenderer renderer = CreateVolumeRenderer();
        try
        {
            float fov = fovDegrees * MathF.PI / 180;
            MeshId box = renderer.CreateMesh(Primitives.Box(new(0.5f)));
            MeshId plane = renderer.CreateMesh(Primitives.Plane(0.5f));
            MaterialId white = renderer.CreateMaterial(new(BaseColor: Vector4.One, Roughness: 1, Reflectance: 0));
            MaterialId glow = renderer.CreateMaterial(
                new(BaseColor: Vector4.UnitW, Roughness: 1, Reflectance: 0, Emissive: new(8, 0, 0))
            );
            RenderSnapshot scene = Gpu.Snapshot(Vector3.UnitZ);
            scene.Camera.VerticalFov = fov;
            scene.Lights.Clear();
            scene.Ambient = default;
            float front = frontCells * cell, emitterZ = emitterCells * cell, side = emitterSide * cell;
            scene.Instances.Add(Panel(mesh: box, material: white, position: new(0, 0, -front - 0.08f * cell), scale: new(2 * receiverHalf * cell, 2 * receiverHalf * cell, 0.16f * cell), entity: 1));
            scene.Instances.Add(
                Panel(mesh: plane, material: glow, position: new(0, 0, emitterZ), scale: new(side), entity: 2, rotation: Quaternion.CreateFromAxisAngle(Vector3.UnitX, -MathF.PI / 2))
            );
            renderer.Render(scene, 1f);
            float[] hdr = renderer.ReadHdr();
            double measured = 0, exact = 0, cm = 0, ce = 0;
            int n = 0, nc = 0;
            var profile = new System.Text.StringBuilder();
            for (int py = 0; py < Size; py++)
            for (int px = 0; px < Size; px++)
            {
                Vector2 p = PlanePoint(px: px, py: py, depth: front, fov: fov);
                if (MathF.Abs(p.X) > receiverHalf * cell || MathF.Abs(p.Y) > receiverHalf * cell)
                    continue;
                double e = Irradiance(x: p.X, y: p.Y, l: 8, s: side, h: front + emitterZ);
                double m = Math.PI * hdr[4 * (py * Size + px)];
                measured += m;
                exact += e;
                n++;
                if (Math.Abs(p.X) < cell && Math.Abs(p.Y) < cell)
                {
                    cm += m;
                    ce += e;
                    nc++;
                }
                if (py == 32 && px % 4 == 0)
                    profile.Append($" {p.X / cell:F1}:{m:F3}/{e:F3}");
            }
            Log(
                $"wider cell {cell} emitter {emitterSide} receiver ±{receiverHalf} h {frontCells + emitterCells} fov {fovDegrees}: mean {measured / n:F5} exact {exact / n:F5} ratio {measured / exact:F4}; central ±1 cell {cm / nc:F5} exact {ce / nc:F5} ratio {cm / ce:F4}"
            );
            Log($"  profile x/cell:measured/exact{profile}");
        }
        finally
        {
            Gpu.DisposeAndAssertClean(renderer);
        }
    }
}
