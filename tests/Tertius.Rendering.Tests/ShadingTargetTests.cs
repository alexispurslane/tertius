using System.Numerics;
using Tertius.Testing;

namespace Tertius.Rendering.Tests;

/// <summary>
/// The shading target of the G-buffer on the GPU (docs/plans/skin-and-hair.md, "The shading target"): each lit pixel
/// names its shading model by <see cref="ShadingCode"/> in A, and a hair pixel its strand by <see cref="StrandCode"/> in R
/// and G and its card by <see cref="CardCode"/> in the spare bits of B and A. The cards are the plane mesh, 3 m ahead.
/// </summary>
[Collection(GpuCollection.Name)]
public sealed class ShadingTargetTests
{
    private const int Width = 160,
        Height = 90;

    /// <summary>A card that faces the camera: the plane mesh's front, +Y, turned to +Z, with u along +X.</summary>
    private static readonly Quaternion FacingTheCamera = Quaternion.CreateFromAxisAngle(Vector3.UnitX, MathF.PI / 2);

    /// <summary>The four channels of the shading target at the pixel that holds <paramref name="point"/>, a camera-relative point.</summary>
    private static byte[] At(byte[] shading, Vector3 point, int width = Width, int height = Height)
    {
        Vector2 pixel = Gpu.Project(point: point, width: width, height: height);
        int i = ((int)pixel.Y * width + (int)pixel.X) * 4;
        return shading[i..(i + 4)];
    }

    private static MaterialId Card(VulkanRenderer renderer, ShadingModel model) =>
        renderer.CreateMaterial(
            new(BaseColor: new(x: 0.5f, y: 0.4f, z: 0.3f, w: 1), Roughness: 0.5f, Reflectance: 0.5f, Model: model)
        );

    [Fact]
    public void TheGBufferWritesEachModelsCode()
    {
        VulkanRenderer renderer = Gpu.Headless(width: Width, height: Height);
        try
        {
            MeshId plane = renderer.CreateMesh(Primitives.Plane(0.4f));
            RenderSnapshot snapshot = Gpu.Snapshot(Vector3.UnitZ);
            var centers = new Vector3[] { new(-1, 0, -3), new(0, 0, -3), new(1, 0, -3) };
            ShadingModel[] models =
            [
                new StandardShading(0.5f),
                new SkinShading(),
                new HairShading(StrandDirection.PlusV),
            ];
            for (int i = 0; i < models.Length; i++)
                snapshot.Instances.Add(
                    Gpu.Instance(
                        mesh: plane,
                        material: Card(renderer, models[i]),
                        position: centers[i],
                        scale: Vector3.One,
                        rotation: FacingTheCamera,
                        radius: 0.6f,
                        chainDepth: TestChains.CamerasFrame
                    )
                );

            renderer.Render(snapshot, 1f);
            byte[] shading = renderer.ReadShading();

            At(shading: shading, point: centers[0])
                .Should()
                .Equal([0, 0, 0, (byte)ShadingCode.Standard], "a standard pixel holds no strand");
            At(shading: shading, point: centers[1])
                .Should()
                .Equal([0, 0, 0, (byte)ShadingCode.Skin], "a skin pixel holds no strand");
            CardCode
                .ModelOf(At(shading: shading, point: centers[2])[3])
                .Should()
                .Be(ShadingCode.Hair, "A holds the model's code below a hair pixel's card code");
        }
        finally
        {
            Gpu.DisposeAndAssertClean(renderer);
        }
    }

    [Theory]
    [InlineData(StrandDirection.PlusU)]
    [InlineData(StrandDirection.MinusU)]
    [InlineData(StrandDirection.PlusV)]
    [InlineData(StrandDirection.MinusV)]
    public void TheGBufferWritesTheStrandThatStrandCodeGives(StrandDirection direction)
    {
        // Turned off the axes, so that no code lies on a tie of its rounding.
        var rotation = Quaternion.Concatenate(
            FacingTheCamera,
            Quaternion.CreateFromYawPitchRoll(yaw: 0.35f, pitch: 0.2f, roll: 0.1f)
        );
        Double3 strand = HairFiber.Strand(
            normal: Vector3.Transform(Vector3.UnitY, rotation),
            tangent: Vector3.Transform(Vector3.UnitX, rotation),
            bitangentSign: 1,
            direction: direction
        );
        (byte x, byte y) = StrandCode.Encode(strand.ToVector3());
        VulkanRenderer renderer = Gpu.Headless(width: Width, height: Height);
        try
        {
            MeshId plane = renderer.CreateMesh(Primitives.Plane(1));
            RenderSnapshot snapshot = Gpu.Snapshot(Vector3.UnitZ);
            snapshot.Instances.Add(
                Gpu.Instance(
                    mesh: plane,
                    material: Card(renderer, new HairShading(direction)),
                    position: new(0, 0, -3),
                    scale: Vector3.One,
                    rotation: rotation,
                    radius: 1.5f,
                    chainDepth: TestChains.CamerasFrame
                )
            );

            renderer.Render(snapshot, 1f);
            byte[] stored = At(shading: renderer.ReadShading(), point: new(0, 0, -3));

            ((int)stored[0])
                .Should()
                .BeCloseTo(x, 1, "the shader's strand differs from the C# one by its rounding only");
            ((int)stored[1]).Should().BeCloseTo(y, 1);
            stored[2..]
                .Should()
                .Equal(
                    [0, (byte)ShadingCode.Hair],
                    "B holds no light mark, and A the hair's code, with the code of card 0 of the first instance, 0"
                );
        }
        finally
        {
            Gpu.DisposeAndAssertClean(renderer);
        }
    }

    [Fact]
    public void TheGBufferWritesAHairPixelsCardCodeBesideItsLightMarkAndItsModelsCode()
    {
        // One hair mesh of two cards, the plane mesh and a copy 1 m along its +X, drawn by two instances 1 m apart
        // along Y. The plane mesh's +X turns to the world's +X.
        MeshData plane = Primitives.Plane(0.4f);
        Vertex[] second =
        [
            .. plane.Vertices.Select(vertex => vertex with { Position = vertex.Position + Vector3.UnitX }),
        ];
        var twoCards = new MeshData(
            [.. plane.Vertices, .. second],
            [.. plane.Indices, .. plane.Indices.Select(index => index + (uint)plane.Vertices.Length)]
        );
        Vector3[] instanceCenters = [new(-0.5f, 0.5f, -3), new(-0.5f, -0.5f, -3)];
        VulkanRenderer renderer = Gpu.Headless(width: Width, height: Height);
        try
        {
            MeshId mesh = renderer.CreateMesh(twoCards);
            MaterialId hair = Card(renderer, new HairShading(StrandDirection.PlusV));
            RenderSnapshot snapshot = Gpu.Snapshot(Vector3.UnitZ);
            foreach (Vector3 center in instanceCenters)
                snapshot.Instances.Add(
                    Gpu.Instance(
                        mesh: mesh,
                        material: hair,
                        position: center,
                        scale: Vector3.One,
                        rotation: FacingTheCamera,
                        radius: 1.6f,
                        chainDepth: TestChains.CamerasFrame
                    )
                );

            renderer.Render(snapshot, 1f);
            byte[] shading = renderer.ReadShading();

            for (uint instance = 0; instance < instanceCenters.Length; instance++)
            for (uint card = 0; card < 2; card++)
            {
                byte[] stored = At(shading: shading, point: instanceCenters[instance] + card * Vector3.UnitX);
                (byte b, byte a) = CardCode.Channels(
                    code: CardCode.Of(card: card, instance: instance),
                    holdsLight: false,
                    model: ShadingCode.Hair
                );
                stored[2..]
                    .Should()
                    .Equal(
                        [b, a],
                        "card {0} of instance {1}, the instance's place in the frame, holds its card code",
                        card,
                        instance
                    );
            }
        }
        finally
        {
            Gpu.DisposeAndAssertClean(renderer);
        }
    }

    [Fact]
    public void AnImpostorBillboardAndTheSkyHoldTheStandardCode()
    {
        const int width = 320,
            height = 180;
        VulkanRenderer renderer = Gpu.Headless(width: width, height: height, impostors: true);
        try
        {
            MeshId plane = renderer.CreateMesh(Primitives.Plane(1));
            RenderSnapshot snapshot = Gpu.Snapshot(Vector3.UnitZ);
            var center = new Vector3(0, 0, -6);
            snapshot.Instances.Add(
                Gpu.Instance(
                    mesh: plane,
                    material: Card(renderer, new HairShading(StrandDirection.PlusV)),
                    position: center,
                    scale: Vector3.One,
                    rotation: FacingTheCamera,
                    radius: 1.5f,
                    chainDepth: TestChains.CamerasFrame
                )
            );

            renderer.Render(snapshot, 1f);
            byte[] shading = renderer.ReadShading();
            float[] distance = renderer.ReadDistance();

            renderer.LastFrameStats.ImpostorCaptures.Should().Be(1, "a 2 m card 6 m away covers fewer than 200 pixels");
            Vector2 middle = Gpu.Project(point: center, width: width, height: height);
            distance[(int)middle.Y * width + (int)middle.X].Should().BePositive("the billboard covers the middle");
            At(shading: shading, point: center, width: width, height: height)
                .Should()
                .Equal([0, 0, 0, 0], "a billboard's pixel is unlit, not hair");
            distance[0].Should().Be(0, "the corner is sky");
            shading[..4].Should().Equal([0, 0, 0, 0], "the target is cleared to 0");
        }
        finally
        {
            Gpu.DisposeAndAssertClean(renderer);
        }
    }
}
