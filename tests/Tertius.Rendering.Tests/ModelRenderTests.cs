using System.Numerics;
using Tertius.Testing;

namespace Tertius.Rendering.Tests;

/// <summary>
/// A model on the GPU (ARCHITECTURE.md, "Models"): an entity that draws a model of two parts shows the same image as two
/// plain entities that draw the parts' meshes and materials at the parts' poses, and the image matches its reference.
/// </summary>
[Collection(GpuCollection.Name)]
public sealed class ModelRenderTests
{
    private const int Width = 640,
        Height = 360;

    /// <summary>The entity of the model: turned 30° about Y, 1.5 times its size, below and ahead of the camera.</summary>
    private static readonly Vector3 EntityPosition = new(0.4f, -0.6f, -6);

    private static readonly Quaternion EntityRotation = Quaternion.CreateFromAxisAngle(Vector3.UnitY, MathF.PI / 6);

    private const float EntityScale = 1.5f;

    /// <summary>
    /// The two parts: a flat red box at the entity's origin, and a blue ball, stretched along its Y, that stands 0.7 m up
    /// and 0.5 m to the entity's +X, tilted 45° about X.
    /// </summary>
    private static ModelPart[] Parts(MeshId box, MeshId ball, MaterialId red, MaterialId blue) =>
        [
            new(Mesh: box, Material: red, Offset: Pose.Identity, Scale: new(1, 0.3f, 0.7f), BoundingRadius: 1.3f),
            new(
                Mesh: ball,
                Material: blue,
                Offset: new(new(0.5f, 0.7f, 0), Quaternion.CreateFromAxisAngle(Vector3.UnitX, MathF.PI / 4)),
                Scale: new(0.3f, 0.5f, 0.3f),
                BoundingRadius: 0.5f
            ),
        ];

    /// <summary>
    /// A sunlit gray floor under the camera's view, and the model's parts drawn by <paramref name="drawParts"/>, which
    /// takes the snapshot and the parts.
    /// </summary>
    private static Capture RenderScene(
        VulkanRenderer renderer,
        string name,
        Action<RenderSnapshot, ModelPart[]> drawParts
    )
    {
        MeshId floor = renderer.CreateMesh(Primitives.Plane(8f));
        MeshId box = renderer.CreateMesh(Primitives.Box(new(0.5f)));
        MeshId ball = renderer.CreateMesh(Primitives.Sphere(1f, 48, 24));
        MaterialId gray = renderer.CreateMaterial(MaterialDesc.Color(new(x: 0.5f, y: 0.5f, z: 0.5f, w: 1), 0.8f));
        MaterialId red = renderer.CreateMaterial(MaterialDesc.Color(new(x: 0.8f, y: 0.1f, z: 0.08f, w: 1), 0.4f));
        MaterialId blue = renderer.CreateMaterial(MaterialDesc.Color(new(x: 0.1f, y: 0.25f, z: 0.85f, w: 1), 0.3f));
        RenderSnapshot snapshot = Gpu.Snapshot(Vector3.Normalize(new(-0.4f, 1, 0.6f)));
        snapshot.Instances.Add(
            Gpu.Instance(
                mesh: floor,
                material: gray,
                position: new(0, -1.2f, -6),
                scale: Vector3.One,
                chainDepth: TestChains.CamerasFrame,
                entity: 1
            )
        );
        drawParts(snapshot, Parts(box: box, ball: ball, red: red, blue: blue));
        return Gpu.RenderAndCapture(renderer: renderer, snapshot: snapshot, name: name);
    }

    /// <summary>One entity that draws a model of the parts, made by <paramref name="renderer"/>.</summary>
    private static Action<RenderSnapshot, ModelPart[]> AsModel(VulkanRenderer renderer) =>
        (snapshot, parts) =>
        {
            RenderInstance ship = Gpu.Instance(
                mesh: MeshId.None,
                material: MaterialId.None,
                position: EntityPosition,
                scale: new(EntityScale),
                chainDepth: TestChains.CamerasFrame,
                rotation: EntityRotation,
                flags: RenderFlags.CastsShadow,
                entity: 2
            );
            ship.Model = renderer.CreateModel(new([.. parts]));
            snapshot.Instances.Add(ship);
        };

    /// <summary>
    /// One plain entity for each of <paramref name="parts"/>, at the part's offset in the turned and scaled axes of the
    /// model's entity, at the part's scale times the entity's.
    /// </summary>
    private static void AsPlainEntities(RenderSnapshot snapshot, ModelPart[] parts)
    {
        for (int index = 0; index < parts.Length; index++)
        {
            ModelPart part = parts[index];
            Vector3 offset = EntityScale * part.Offset.Position;
            snapshot.Instances.Add(
                Gpu.Instance(
                    mesh: part.Mesh,
                    material: part.Material,
                    position: EntityPosition + Vector3.Transform(offset, EntityRotation),
                    scale: EntityScale * part.Scale,
                    chainDepth: TestChains.CamerasFrame,
                    rotation: EntityRotation * part.Offset.Rotation,
                    flags: RenderFlags.CastsShadow,
                    radius: EntityScale * part.BoundingRadius,
                    entity: (uint)(2 + index)
                )
            );
        }
    }

    [Fact]
    public void AModelOfTwoPartsShowsTheImageOfItsPartsDrawnAsPlainEntities()
    {
        VulkanRenderer renderer = Gpu.Headless(width: Width, height: Height);
        try
        {
            Capture model = RenderScene(renderer: renderer, name: "model-parts", drawParts: AsModel(renderer));
            Capture plain = RenderScene(renderer: renderer, name: "model-parts-plain", drawParts: AsPlainEntities);

            Flip.Compare(plain, model)
                .CountAbove(ReferenceImages.DefaultTolerance)
                .Should()
                .Be(0, "each part of the model draws as an ordinary instance at the same pose");
            ReferenceImages.Verify(model, ReferenceImages.ReferencePath("model-parts", renderer.Driver));
        }
        finally
        {
            Gpu.DisposeAndAssertClean(renderer);
        }
    }
}
