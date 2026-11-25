using System.Collections.Immutable;
using System.Numerics;

namespace Tertius.Rendering.Tests;

/// <summary>
/// Models in the upload queue: a model takes the next model id, waits in the batch with the meshes and materials that the
/// queue made before it, and names only meshes and materials that this queue made. No GPU needed.
/// </summary>
public sealed class UploadQueueModelTests
{
    private static UploadQueue NewQueue() => new(maxTextureSize: 64, deformBytesLimit: 1L << 27);

    private static ModelPart PartOf(MeshId mesh, MaterialId material) =>
        new(Mesh: mesh, Material: material, Offset: Pose.Identity, Scale: Vector3.One, BoundingRadius: 1);

    private static ModelDesc ModelOf(params ModelPart[] parts) => new([.. parts]);

    [Fact]
    public void ModelIdsCountFromZeroAndAreNeverNone()
    {
        UploadQueue queue = NewQueue();
        MeshId mesh = queue.AddMesh(Primitives.Box(Vector3.One));

        ModelId[] models = [.. Enumerable.Range(0, 3).Select(_ => queue.AddModel(ModelOf(PartOf(mesh, default))))];

        models.Select(m => m.Value).Should().Equal(0, 1, 2);
        models.Should().NotContain(ModelId.None);
    }

    [Fact]
    public void ADrainHandsOverEachModelWithItsDescription()
    {
        UploadQueue queue = NewQueue();
        MeshId mesh = queue.AddMesh(Primitives.Box(Vector3.One));
        ModelDesc desc = ModelOf(PartOf(mesh, default));
        ModelId model = queue.AddModel(desc);

        UploadQueue.Pending batch = queue.Drain();

        batch.Models.Should().Equal((model, desc));
    }

    [Fact]
    public void EachDrainHandsOverOnlyTheModelsMadeSinceTheLastOne()
    {
        UploadQueue queue = NewQueue();
        MeshId mesh = queue.AddMesh(Primitives.Box(Vector3.One));
        ModelDesc desc = ModelOf(PartOf(mesh, default));

        // The queue swaps two batches, so the third drain reuses the first batch.
        int[] counts = new int[3];
        for (int drain = 0; drain < counts.Length; drain++)
        {
            queue.AddModel(desc);
            counts[drain] = queue.Drain().Models.Count;
        }

        counts.Should().Equal(1, 1, 1);
    }

    [Fact]
    public void ABatchWithOnlyAModelIsNotEmpty()
    {
        UploadQueue queue = NewQueue();
        MeshId mesh = queue.AddMesh(Primitives.Box(Vector3.One));
        queue.Drain();

        queue.AddModel(ModelOf(PartOf(mesh, default)));

        queue.Drain().IsEmpty.Should().BeFalse();
    }

    [Fact]
    public void APartWithTheDefaultMaterialIsTaken()
    {
        UploadQueue queue = NewQueue();
        MeshId mesh = queue.AddMesh(Primitives.Box(Vector3.One));

        Action add = () => queue.AddModel(ModelOf(PartOf(mesh, MaterialId.None)));

        add.Should().NotThrow();
    }

    [Fact]
    public void APartWithoutABoundingRadiusIsTaken()
    {
        UploadQueue queue = NewQueue();
        MeshId mesh = queue.AddMesh(Primitives.Box(Vector3.One));

        Action add = () => queue.AddModel(ModelOf(PartOf(mesh, default) with { BoundingRadius = 0 }));

        add.Should().NotThrow("a radius of 0 leaves the bound to the mesh");
    }

    [Fact]
    public void AModelHasAsManyPartsAsAPartIndexCounts()
    {
        UploadQueue queue = NewQueue();
        MeshId mesh = queue.AddMesh(Primitives.Box(Vector3.One));
        ModelPart part = PartOf(mesh, default);

        Action most = () => queue.AddModel(new([.. Enumerable.Repeat(part, ushort.MaxValue + 1)]));
        Action tooMany = () => queue.AddModel(new([.. Enumerable.Repeat(part, ushort.MaxValue + 2)]));

        most.Should().NotThrow();
        tooMany.Should().Throw<ArgumentException>();
    }

    public static TheoryData<string> Defects =>
        new(
            "no parts array",
            "a part without a mesh",
            "a mesh that the queue did not make",
            "a material that the queue did not make",
            "an offset that is not finite",
            "an offset rotation that is not a rotation",
            "a scale that is not finite",
            "a scale with a zero axis",
            "a negative bounding radius",
            "a bounding radius that is not finite"
        );

    [Theory]
    [MemberData(nameof(Defects))]
    public void AModelWithADefectIsRefusedWithAnArgumentError(string defect)
    {
        UploadQueue queue = NewQueue();
        MeshId mesh = queue.AddMesh(Primitives.Box(Vector3.One));
        MaterialId material = queue.AddMaterial(MaterialDesc.Color(Vector4.One, 0.5f));
        ModelPart good = PartOf(mesh, material);
        ModelDesc desc = defect switch
        {
            "no parts array" => new(default(ImmutableArray<ModelPart>)),
            "a part without a mesh" => ModelOf(good with { Mesh = MeshId.None }),
            "a mesh that the queue did not make" => ModelOf(good with { Mesh = new(1) }),
            "a material that the queue did not make" => ModelOf(good with { Material = new(1) }),
            "an offset that is not finite" => ModelOf(
                good with
                {
                    Offset = new(new(float.NaN, 0, 0), Quaternion.Identity),
                }
            ),
            "an offset rotation that is not a rotation" => ModelOf(
                good with
                {
                    Offset = new(Vector3.Zero, default),
                }
            ),
            "a scale that is not finite" => ModelOf(good with { Scale = new(1, float.PositiveInfinity, 1) }),
            "a scale with a zero axis" => ModelOf(good with { Scale = new(1, 1, 0) }),
            "a negative bounding radius" => ModelOf(good with { BoundingRadius = -1 }),
            "a bounding radius that is not finite" => ModelOf(good with { BoundingRadius = float.NaN }),
            _ => throw new ArgumentOutOfRangeException(nameof(defect), defect, null),
        };

        Action add = () => queue.AddModel(desc);

        add.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void ARefusedModelTakesNoId()
    {
        UploadQueue queue = NewQueue();
        MeshId mesh = queue.AddMesh(Primitives.Box(Vector3.One));
        Record.Exception(() => queue.AddModel(ModelOf(PartOf(MeshId.None, default))));

        queue.AddModel(ModelOf(PartOf(mesh, default))).Value.Should().Be(0);
    }
}
