using System.Numerics;
using BenchmarkDotNet.Attributes;
using Tertius.Rendering;

namespace Tertius.Benchmarks;

/// <summary>
/// The wall time of one headless <see cref="VulkanRenderer.Render"/> at 1280 × 720 with glow puffs (ter-4zt6, task 5), as
/// <see cref="OutlineRenderBenchmarks"/> measures it. The renderer keeps one frame in flight, so each call first waits for
/// the fence of the frame before it: in a steady run, one call takes the CPU time and the GPU time of one frame. Validation
/// is off, and impostors are off, so that every frame draws the same.
/// </summary>
/// <remarks>
/// The scene is the one of <see cref="OutlineRenderBenchmarks"/>: a gray ground and 100 boxes from 10 m to 200 m ahead, lit
/// by the sun. <see cref="Puffs"/> glow puffs of one look join it. Small puffs have radii from 0.2 m to 1 m and spread over
/// the view from 5 m to 100 m ahead, so each covers a few pixels to a few dozen. Puffs that fill the screen have a radius
/// of 8 m and lie 3 m to 4 m ahead, so each one covers every pixel: the worst case of the fill. 0 is the cost with no puff,
/// which must equal the cost before the renderer drew puffs.
/// </remarks>
[MemoryDiagnoser]
public class PuffRenderBenchmarks
{
    private VulkanRenderer renderer = null!;
    private RenderSnapshot snapshot = null!;

    [Params(0, 100, 1000, 4096)]
    public int Puffs;

    [Params(false, true)]
    public bool FillTheScreen;

    [GlobalSetup]
    public void Setup()
    {
        renderer = VulkanRenderer.CreateHeadless(
            new()
            {
                Validation = false,
                Width = 1280,
                Height = 720,
                FramesInFlight = 1,
                Impostors = false,
            }
        );
        MeshId box = renderer.CreateMesh(Primitives.Box(new(0.5f)));
        MeshId plane = renderer.CreateMesh(Primitives.Plane(1f));
        MaterialId gray = renderer.CreateMaterial(
            MaterialDesc.Color(new(x: 0.55f, y: 0.55f, z: 0.55f, w: 1), gloss: 0.3f)
        );
        snapshot = new()
        {
            Camera = new()
            {
                Rotation = Quaternion.Identity,
                PreviousRotation = Quaternion.Identity,
                VerticalFov = MathF.PI / 3,
            },
            Sun = new()
            {
                Direction = Vector3.Normalize(new(0.4f, 0.8f, 0.5f)),
                Color = Vector3.One,
                Intensity = 3,
                AngularRadius = 0.00467f,
            },
            Ambient = new()
            {
                SkyColor = new(0.09f, 0.11f, 0.15f),
                GroundColor = new(0.05f),
                Intensity = 1,
            },
        };
        var up = HemisphereUp.Along(Vector3.UnitY);
        snapshot.Instances.Add(
            RenderInstance.Create(
                mesh: plane,
                material: gray,
                position: new(0, -2, -200),
                rotation: Quaternion.Identity,
                scale: new(250, 1, 250),
                boundingRadius: 0,
                hemisphere: up,
                flags: RenderFlags.Visible | RenderFlags.CastsShadow
            )
        );
        var random = new Random(7);
        for (int i = 0; i < 100; i++)
        {
            float depth = 10 + 190 * random.NextSingle();
            var position = new Vector3((random.NextSingle() * 2 - 1) * depth, -1, -depth);
            snapshot.Instances.Add(
                RenderInstance.Create(
                    mesh: box,
                    material: gray,
                    position: position,
                    rotation: Quaternion.CreateFromYawPitchRoll(random.NextSingle() * 3, 0, 0),
                    scale: new(2),
                    boundingRadius: 0,
                    hemisphere: up,
                    flags: RenderFlags.Visible | RenderFlags.CastsShadow,
                    entityIndex: (uint)i + 1
                )
            );
        }

        float radius = FillTheScreen ? 8f : 0.2f;
        snapshot.PuffLooks.Add(
            new(
                Life: 2f,
                StartRadius: radius,
                EndRadius: FillTheScreen ? 8f : 1f,
                TemperatureRatio: 3f,
                Shading: PuffShading.Glow(new(0.02f, 0.008f, 0.002f))
            )
        );
        snapshot.PuffAgeStep = 1f / 60;
        for (int i = 0; i < Puffs; i++)
        {
            float depth = FillTheScreen ? 3 + random.NextSingle() : 5 + 95 * random.NextSingle();
            Vector3 position = FillTheScreen
                ? new(random.NextSingle() - 0.5f, random.NextSingle() - 0.5f, -depth)
                : new Vector3(
                    (random.NextSingle() * 2 - 1) * depth * 0.9f,
                    (random.NextSingle() * 2 - 1) * depth * 0.5f,
                    -depth
                );
            snapshot.Puffs.Add(
                new()
                {
                    Position = position,
                    PreviousPosition = position,
                    Age = 1.9f * random.NextSingle(),
                    Look = 0,
                }
            );
        }

        // The first frames upload the meshes and the materials and grow the buffers.
        for (int i = 0; i < 4; i++)
            renderer.Render(snapshot, 1f);
    }

    [GlobalCleanup]
    public void Cleanup()
    {
        Console.WriteLine($"// {Puffs} puffs in the snapshot, filling the screen: {FillTheScreen}");
        renderer.Dispose();
    }

    [Benchmark]
    public void Render() => renderer.Render(snapshot, 1f);
}
