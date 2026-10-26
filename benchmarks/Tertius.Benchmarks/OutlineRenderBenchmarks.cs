using System.Numerics;
using BenchmarkDotNet.Attributes;
using Tertius.Rendering;

namespace Tertius.Benchmarks;

/// <summary>
/// The wall time of one headless <see cref="VulkanRenderer.Render"/> at 1280 × 720, with the frame outlines off and on
/// (the overlay pass, ter-p51r). The renderer keeps one frame in flight, so each call first waits for the fence of the
/// frame before it: in a steady run, one call takes the CPU time and the GPU time of one frame. Validation is off, and
/// impostors are off, so that every frame draws the same.
/// </summary>
/// <remarks>
/// The scene is a gray ground and 100 boxes from 10 m to 200 m ahead, lit by the sun. <see cref="Outlines"/> outlines
/// spread over the view from 20 m to 400 m ahead, with radii from 1 m to 20 m, and they cycle through the five cases:
/// a turned semantic ellipsoid, then the falling, resting, nesting and following frames' spheres. 0 is the cost with the
/// outlines off: the snapshot holds none, as it does while <c>World.ShowFrameOutlines</c> is off.
/// </remarks>
[MemoryDiagnoser]
public class OutlineRenderBenchmarks
{
    private VulkanRenderer renderer = null!;
    private RenderSnapshot snapshot = null!;

    [Params(0, 10, 100, 1000)]
    public int Outlines;

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
            MaterialDesc.Color(new(x: 0.55f, y: 0.55f, z: 0.55f, w: 1), roughness: 0.55f)
        );
        snapshot = new()
        {
            Camera = new()
            {
                Rotation = Quaternion.Identity,
                PreviousRotation = Quaternion.Identity,
                VerticalFov = MathF.PI / 3,
            },
            Lights = { BenchmarkLights.Star(direction: new(0.4f, 0.8f, 0.5f), irradiance: 3, chainDepth: 0) },
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
                chainDepth: 0,
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
                    chainDepth: 0,
                    flags: RenderFlags.Visible | RenderFlags.CastsShadow,
                    entityIndex: (uint)i + 1
                )
            );
        }

        for (int i = 0; i < Outlines; i++)
        {
            float depth = 20 + 380 * random.NextSingle();
            var center = new Vector3(
                (random.NextSingle() * 2 - 1) * depth,
                (random.NextSingle() * 2 - 1) * depth * 0.5f,
                -depth
            );
            float radius = 1 + 19 * random.NextSingle();
            snapshot.Outlines.Add(
                (i % 5) switch
                {
                    0 => new SemanticOutline(
                        center,
                        Quaternion.CreateFromYawPitchRoll(i, i * 0.3f, 0),
                        new(radius, radius * 0.7f, radius * 0.9f)
                    ),
                    1 => new FallingOutline(center, radius),
                    2 => new RestingOutline(center, radius),
                    3 => new NestingOutline(center, radius),
                    _ => new FollowingOutline(center, radius),
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
        Console.WriteLine($"// {Outlines} outlines in the snapshot, {renderer.LastFrameStats.Outlines} drawn");
        renderer.Dispose();
    }

    [Benchmark]
    public void Render() => renderer.Render(snapshot, 1f);
}
