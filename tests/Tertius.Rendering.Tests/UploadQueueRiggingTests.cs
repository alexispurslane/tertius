using System.Numerics;

namespace Tertius.Rendering.Tests;

/// <summary>
/// The guards of a mesh's rigging in <see cref="UploadQueue.AddMesh"/>, and the packed deformation that it hands over
/// beside the mesh (docs/plans/skinning-and-morphs.md, "The mesh data"). No GPU needed.
/// </summary>
public sealed class UploadQueueRiggingTests
{
    private const int MaxTextureSize = 64;

    /// <summary>The spec's least maxStorageBufferRange, 2^27 bytes.</summary>
    private const long DeformBytesLimit = 1L << 27;

    private static UploadQueue Queue() => new(maxTextureSize: MaxTextureSize, deformBytesLimit: DeformBytesLimit);

    private static MeshData Cylinder() => Primitives.TwistingBendingCylinder(radius: 0.2f, length: 2);

    private static MeshData WithSkin(MeshData mesh, MeshSkin skin) =>
        mesh with
        {
            Rigging = mesh.Rigging! with { Skin = skin },
        };

    private static MeshData WithSkinVertex(MeshData mesh, int index, VertexSkin vertex)
    {
        MeshSkin skin = mesh.Rigging!.Skin!;
        VertexSkin[] vertices = [.. skin.Vertices];
        vertices[index] = vertex;
        return WithSkin(mesh, skin with { Vertices = vertices });
    }

    private static MeshData WithTargets(MeshData mesh, params MorphTarget[] targets) =>
        mesh with
        {
            Rigging = mesh.Rigging! with { Morphs = [.. targets] },
        };

    private static void ShouldRefuse(MeshData mesh) =>
        FluentActions.Invoking(() => Queue().AddMesh(mesh)).Should().ThrowExactly<ArgumentException>();

    [Fact]
    public void AddMeshRefusesASkinOfAnotherVertexCount()
    {
        MeshData mesh = Cylinder();
        MeshSkin skin = mesh.Rigging!.Skin!;

        ShouldRefuse(WithSkin(mesh, skin with { Vertices = skin.Vertices[..^1] }));
    }

    [Fact]
    public void AddMeshRefusesAJointPastTheRig()
    {
        MeshData mesh = Cylinder();
        VertexSkin vertex = mesh.Rigging!.Skin!.Vertices[3];

        // Even with weight 0: the shader reads every slot's joint.
        ShouldRefuse(WithSkinVertex(mesh, 3, vertex with { Joint3 = 2 }));
    }

    [Theory]
    [InlineData(-0.5f)]
    [InlineData(float.NaN)]
    [InlineData(float.PositiveInfinity)]
    public void AddMeshRefusesANegativeWeight(float weight)
    {
        MeshData mesh = Cylinder();
        VertexSkin vertex = mesh.Rigging!.Skin!.Vertices[3];

        ShouldRefuse(WithSkinVertex(mesh, 3, vertex with { Weights = new(x: 1, y: weight, z: 0, w: 0) }));
    }

    [Fact]
    public void AddMeshRefusesWeightsThatSumToZero()
    {
        MeshData mesh = Cylinder();
        VertexSkin vertex = mesh.Rigging!.Skin!.Vertices[3];

        ShouldRefuse(WithSkinVertex(mesh, 3, vertex with { Weights = Vector4.Zero }));
    }

    [Fact]
    public void AddMeshRefusesInverseBindsOfAnotherJointCount()
    {
        MeshData mesh = Cylinder();
        MeshSkin skin = mesh.Rigging!.Skin!;

        ShouldRefuse(WithSkin(mesh, skin with { InverseBinds = [skin.InverseBinds[0]] }));
    }

    [Theory]
    [InlineData(float.NaN, 1f)]
    [InlineData(float.PositiveInfinity, 1f)]
    [InlineData(0f, float.NaN)]
    public void AddMeshRefusesAnInverseBindThatIsNotFinite(float x, float w)
    {
        MeshData mesh = Cylinder();
        MeshSkin skin = mesh.Rigging!.Skin!;
        var bind = new Pose(new(x, 0, 0), new(x: 0, y: 0, z: 0, w: w));

        ShouldRefuse(WithSkin(mesh, skin with { InverseBinds = skin.InverseBinds.SetItem(1, bind) }));
    }

    [Fact]
    public void AddMeshRefusesAnInverseBindWhoseRotationIsNotARotation()
    {
        MeshData mesh = Cylinder();
        MeshSkin skin = mesh.Rigging!.Skin!;
        var bind = new Pose(Vector3.Zero, new(x: 0, y: 0, z: 0, w: 0));

        ShouldRefuse(WithSkin(mesh, skin with { InverseBinds = skin.InverseBinds.SetItem(1, bind) }));
    }

    [Fact]
    public void AddMeshRefusesMorphTargetsThatAreNotSet()
    {
        MeshData mesh = Cylinder();

        FluentActions
            .Invoking(() => Queue().AddMesh(mesh with { Rigging = mesh.Rigging! with { Morphs = default } }))
            .Should()
            .ThrowExactly<ArgumentException>()
            .WithMessage("The rigging's morph targets are not set.*");
    }

    [Theory]
    [InlineData(1)]
    [InlineData(-1)]
    public void AddMeshRefusesAMorphPastTheRig(int morph)
    {
        MeshData mesh = Cylinder();

        ShouldRefuse(WithTargets(mesh, mesh.Rigging!.Morphs[0] with { Morph = morph }));
    }

    [Fact]
    public void AddMeshRefusesTwoTargetsOfOneMorph()
    {
        MeshData box = Primitives.MorphingBox(Vector3.One);
        MeshRigging rigging = box.Rigging!;

        ShouldRefuse(WithTargets(box, rigging.Morphs[0], rigging.Morphs[1] with { Morph = 0 }));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    public void AddMeshRefusesADeltaArrayOfAnotherLength(int kind)
    {
        MeshData mesh = Cylinder();
        MorphTarget twist = mesh.Rigging!.Morphs[0];
        MorphTarget shorter = kind switch
        {
            0 => twist with { Positions = twist.Positions[..^1] },
            1 => twist with { Normals = twist.Normals[..^1] },
            _ => twist with { Tangents = twist.Tangents[..^1] },
        };

        ShouldRefuse(WithTargets(mesh, shorter));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    public void AddMeshRefusesADeltaThatIsNotFinite(int kind)
    {
        MeshData mesh = Cylinder();
        MorphTarget twist = mesh.Rigging!.Morphs[0];
        Vector3[] positions = [.. twist.Positions];
        Vector3[] normals = [.. twist.Normals];
        Vector3[] tangents = [.. twist.Tangents];
        Vector3[] broken = kind switch
        {
            0 => positions,
            1 => normals,
            _ => tangents,
        };
        broken[5] = new(0, float.NaN, 0);

        ShouldRefuse(
            WithTargets(mesh, new MorphTarget(Morph: 0, Positions: positions, Normals: normals, Tangents: tangents))
        );
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    public void AddMeshRefusesANormalDeltaLongerThanTwo(int kind)
    {
        MeshData mesh = Cylinder();
        MorphTarget twist = mesh.Rigging!.Morphs[0];
        Vector3[] normals = [.. twist.Normals];
        Vector3[] tangents = [.. twist.Tangents];
        (kind == 1 ? normals : tangents)[5] = new(0, 2.001f, 0);

        ShouldRefuse(WithTargets(mesh, twist with { Normals = normals, Tangents = tangents }));
    }

    [Fact]
    public void AddMeshAcceptsANormalDeltaThatTurnsTheNormalAround()
    {
        MeshData mesh = Cylinder();
        MorphTarget twist = mesh.Rigging!.Morphs[0];
        Vector3[] normals = [.. mesh.Vertices.Select(vertex => -2 * vertex.Normal)];

        FluentActions
            .Invoking(() => Queue().AddMesh(WithTargets(mesh, twist with { Normals = normals })))
            .Should()
            .NotThrow("a delta of 2 is the longest difference of two unit vectors");
    }

    [Fact]
    public void AddMeshThrowsWhenTheDeformDataPassesTheDeviceLimit()
    {
        MeshData box = Primitives.MorphingBox(Vector3.One);
        // Room for one box's 24 deform vertices of 80 bytes, and not for two.
        var queue = new UploadQueue(maxTextureSize: MaxTextureSize, deformBytesLimit: 24 * 80 * 3 / 2);
        queue.AddMesh(box);

        FluentActions.Invoking(() => queue.AddMesh(box)).Should().ThrowExactly<InvalidOperationException>();
        FluentActions
            .Invoking(() => queue.AddMesh(Primitives.Box(Vector3.One)))
            .Should()
            .NotThrow("a rigid mesh has no deform data");
    }

    [Fact]
    public void AddMeshThrowsWhenTheMorphDeltasAlonePassTheDeviceLimit()
    {
        MeshData box = Primitives.MorphingBox(Vector3.One);
        // Two targets that move every vertex: 2 deltas of 48 bytes, 96 bytes, against 80 bytes of deform vertex.
        Vector3[] up = [.. box.Vertices.Select(_ => Vector3.UnitY)];
        MeshData everywhere = WithTargets(
            box,
            new MorphTarget(Morph: 0, Positions: up, Normals: [], Tangents: []),
            new MorphTarget(Morph: 1, Positions: up, Normals: [], Tangents: [])
        );
        // Room for the 24 * 80 bytes of deform vertices, and not for the 24 * 96 bytes of deltas.
        var queue = new UploadQueue(maxTextureSize: MaxTextureSize, deformBytesLimit: 24 * 88);

        FluentActions.Invoking(() => queue.AddMesh(everywhere)).Should().ThrowExactly<InvalidOperationException>();
    }

    [Fact]
    public void AMeshThatTheDeformLimitRefusesTakesNoId()
    {
        MeshData box = Primitives.MorphingBox(Vector3.One);
        // Room for one box's 24 deform vertices of 80 bytes, and not for two.
        var queue = new UploadQueue(maxTextureSize: MaxTextureSize, deformBytesLimit: 24 * 80 * 3 / 2);
        MeshId first = queue.AddMesh(box);
        FluentActions.Invoking(() => queue.AddMesh(box)).Should().ThrowExactly<InvalidOperationException>();

        MeshId next = queue.AddMesh(Primitives.Box(Vector3.One));

        next.Value.Should().Be(first.Value + 1, "the refused mesh took no id");
    }

    [Fact]
    public void ADrainHandsOverEachMeshsPackedDeformBesideIt()
    {
        UploadQueue queue = Queue();
        queue.AddMesh(Primitives.Box(Vector3.One));
        queue.AddMesh(Cylinder());

        UploadQueue.Pending pending = queue.Drain();

        pending.Deforms.Should().HaveCount(2);
        pending.Deforms[0].Should().BeNull("a rigid mesh does not deform");
        pending.Deforms[1]!.Deform.VertexCount.Should().Be(pending.Meshes[1].Mesh.Data.Vertices.Length);
    }

    [Fact]
    public void EachDrainHandsOverOnlyTheDeformsOfItsOwnMeshes()
    {
        UploadQueue queue = Queue();
        queue.AddMesh(Cylinder());
        queue.Drain();
        queue.AddMesh(Primitives.MorphingBox(Vector3.One));
        queue.Drain();
        queue.AddMesh(Primitives.Box(Vector3.One));

        UploadQueue.Pending pending = queue.Drain();

        pending.Deforms.Should().Equal([null], "the batch holds one rigid mesh");
    }
}
