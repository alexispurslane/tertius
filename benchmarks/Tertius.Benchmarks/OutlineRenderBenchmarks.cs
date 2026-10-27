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
        renderer = GroundAndBoxesScene.Renderer();
        var random = new Random(7);
        snapshot = GroundAndBoxesScene.Snapshot(renderer, random);
        AddOutlines(random);
        GroundAndBoxesScene.WarmUp(renderer, snapshot);
    }

    /// <summary>Adds the outlines from 20 m to 400 m ahead, which cycle through the five cases.</summary>
    private void AddOutlines(Random random)
    {
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
