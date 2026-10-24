using System.Numerics;
using BenchmarkDotNet.Attributes;
using Tertius.Rendering;

namespace Tertius.Benchmarks;

/// <summary>
/// The wall time of one headless <see cref="VulkanRenderer.Render"/> at 1280 × 720 with lit puffs and eight lights
/// (ter-4zt6, task 7), against the same frame with glow puffs, as <see cref="PuffRenderBenchmarks"/> measures it. The
/// renderer keeps one frame in flight, so in a steady run one call takes the CPU time and the GPU time of one frame.
/// Validation and impostors are off.
/// </summary>
/// <remarks>
/// The scene is the one of <see cref="PuffRenderBenchmarks"/>, with its small puffs: a gray ground and 100 boxes lit by the
/// sun, and <see cref="Puffs"/> puffs of radii from 0.2 m to 1 m from 5 m to 100 m ahead. Eight point and spot lights of
/// range 30 m stand among the near puffs, so most puffs are within the range of some of them. A lit puff computes its light
/// once in each of its six vertices: the ambient mean, the sun with the far shadows and the cascades, and the eight lights.
/// A glow puff computes none of it, so the difference of the two is the cost of the lighting.
/// </remarks>
[MemoryDiagnoser]
public class LitPuffRenderBenchmarks
{
    private VulkanRenderer renderer = null!;
    private RenderSnapshot snapshot = null!;

    [Params(0, 100, 1000, 4096)] public int Puffs;

    [Params(false, true)] public bool Lit;

    [GlobalSetup]
    public void Setup()
    {
        renderer = VulkanRenderer.CreateHeadless(new()
        {
            Validation = false,
            Width = 1280,
            Height = 720,
            FramesInFlight = 1,
            Impostors = false
        });
        MeshId box = renderer.CreateMesh(Primitives.Box(new(0.5f)));
        MeshId plane = renderer.CreateMesh(Primitives.Plane(1f));
        MaterialId gray =
            renderer.CreateMaterial(MaterialDesc.Color(new(x: 0.55f, y: 0.55f, z: 0.55f, w: 1), gloss: 0.3f));
        snapshot = new()
        {
            Camera = new()
                { Rotation = Quaternion.Identity, PreviousRotation = Quaternion.Identity, VerticalFov = MathF.PI / 3 },
            Sun = new()
            {
                Direction = Vector3.Normalize(new(0.4f, 0.8f, 0.5f)), Color = Vector3.One, Intensity = 3,
                AngularRadius = 0.00467f
            },
            Ambient = new() { SkyColor = new(0.09f, 0.11f, 0.15f), GroundColor = new(0.05f), Intensity = 1 }
        };
        HemisphereUp up = HemisphereUp.Along(Vector3.UnitY);
        snapshot.Instances.Add(RenderInstance.Create(mesh: plane, material: gray, position: new(0, -2, -200),
            rotation: Quaternion.Identity,
            scale: new(250, 1, 250), boundingRadius: 0, hemisphere: up,
            flags: RenderFlags.Visible | RenderFlags.CastsShadow));
        var random = new Random(7);
        for (int i = 0; i < 100; i++)
        {
            float depth = 10 + 190 * random.NextSingle();
            var position = new Vector3((random.NextSingle() * 2 - 1) * depth, -1, -depth);
            snapshot.Instances.Add(RenderInstance.Create(mesh: box, material: gray, position: position,
                rotation: Quaternion.CreateFromYawPitchRoll(random.NextSingle() * 3, 0, 0),
                scale: new(2), boundingRadius: 0, hemisphere: up, flags: RenderFlags.Visible | RenderFlags.CastsShadow,
                entityIndex: (uint)i + 1));
        }

        for (int i = 0; i < 8; i++)
        {
            var place = new Vector3(-14 + 4 * i, 1, -10 - 5 * i);
            var color = new Vector3(1f, 0.8f, 0.6f);
            snapshot.Lights.Add(i % 2 == 0
                ? new PointRenderLight(Position: place, Color: color, Intensity: 40, Range: 30)
                : new SpotRenderLight(Position: place, Direction: new(0, -1, -1), Color: color, Intensity: 60,
                    Range: 30, InnerConeAngle: 0.5f,
                    OuterConeAngle: 0.8f));
        }

        snapshot.PuffLooks.Add(new(Life: 2f, StartRadius: 0.2f, EndRadius: 1f, TemperatureRatio: Lit ? 0.5f : 3f,
            Shading: Lit ? PuffShading.Lit(new(0.8f)) : PuffShading.Glow(new(0.02f, 0.008f, 0.002f))));
        snapshot.PuffAgeStep = 1f / 60;
        for (int i = 0; i < Puffs; i++)
        {
            float depth = 5 + 95 * random.NextSingle();
            var position = new Vector3((random.NextSingle() * 2 - 1) * depth * 0.9f,
                (random.NextSingle() * 2 - 1) * depth * 0.5f, -depth);
            snapshot.Puffs.Add(new()
                { Position = position, PreviousPosition = position, Age = 1.9f * random.NextSingle(), Look = 0 });
        }

        // The first frames upload the meshes and the materials and grow the buffers.
        for (int i = 0; i < 4; i++)
            renderer.Render(snapshot, 1f);
    }

    [GlobalCleanup]
    public void Cleanup()
    {
        Console.WriteLine($"// {Puffs} puffs in the snapshot, lit: {Lit}");
        renderer.Dispose();
    }

    [Benchmark]
    public void Render() => renderer.Render(snapshot, 1f);
}
