using System.Numerics;
using Tertius.Testing;

namespace Tertius.Rendering.Tests;

/// <summary>
/// The G-buffer's specular target: metallic, roughness and reflectance land on the same 8-bit code on every driver
/// (ticket ter-8esk). A UNORM store may take either of the two codes nearest to a value, and NVIDIA's store gave a
/// roughness of 0.1 (25.5 / 255) the code 25 where Apple's gave 26: a code apart moves a sharp highlight's tail by
/// (25 / 26)⁴, 14 %. So the pass writes the codes itself, the nearest to the float product value × 255, the upper one
/// halfway, which the C# float arithmetic here computes the same.
/// </summary>
[Collection(GpuCollection.Name)]
public sealed class SpecularTargetTests
{
    private const int Width = 160,
        Height = 90;

    /// <summary>The 8-bit code of a value: the nearest to its float product with 255, the upper one halfway.</summary>
    private static byte Code(float value) => (byte)MathF.Floor(value * 255f + 0.5f);

    [Theory]
    [InlineData(0.1f)]
    [InlineData(0.3f)]
    [InlineData(0.5f)]
    [InlineData(0.7f)]
    [InlineData(0.9f)]
    public void AMaterialValueHalfwayBetweenTwoCodesLandsOnTheUpperCodeOnEveryDriver(float value)
    {
        VulkanRenderer renderer = Gpu.Headless(width: Width, height: Height);
        try
        {
            MaterialId material = renderer.CreateMaterial(
                new(
                    BaseColor: new(x: 0.5f, y: 0.4f, z: 0.3f, w: 1),
                    Roughness: value,
                    Reflectance: value,
                    Model: new StandardShading(Metallic: value)
                )
            );
            RenderSnapshot snapshot = Gpu.Snapshot(Vector3.UnitZ);
            snapshot.Instances.Add(
                Gpu.Instance(
                    mesh: renderer.CreateMesh(Primitives.Plane(2f)),
                    material: material,
                    position: new(0, 0, -3),
                    scale: Vector3.One,
                    rotation: Quaternion.CreateFromAxisAngle(Vector3.UnitX, MathF.PI / 2),
                    radius: 3,
                    chainDepth: TestChains.CamerasFrame
                )
            );

            renderer.Render(snapshot, 1f);

            byte[] specular = renderer.ReadSpecularTarget();
            int center = 4 * (Height / 2 * Width + Width / 2);
            specular[center..(center + 3)]
                .Should()
                .Equal([Code(value), Code(value), Code(value)], "metallic, roughness and reflectance of {0}", value);
        }
        finally
        {
            Gpu.DisposeAndAssertClean(renderer);
        }
    }
}
