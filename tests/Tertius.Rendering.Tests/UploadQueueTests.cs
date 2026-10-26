using System.Numerics;

namespace Tertius.Rendering.Tests;

/// <summary>The upload queue on its own: id assignment, argument checks and draining. No GPU needed.</summary>
public sealed class UploadQueueTests
{
    private const int MaxTextureSize = 64;

    private static TextureData Pixels(int width, int height) =>
        new(Width: width, Height: height, Rgba: new byte[width * height * 4]);

    [Fact]
    public void EachKindOfIdCountsFromZeroAndIsNeverNone()
    {
        var queue = new UploadQueue(maxTextureSize: MaxTextureSize, deformBytesLimit: 1L << 27);
        MeshId[] meshes = [.. Enumerable.Range(0, 3).Select(_ => queue.AddMesh(Primitives.Box(Vector3.One)))];
        TextureId[] textures = [.. Enumerable.Range(0, 3).Select(_ => queue.AddTexture(Pixels(2, 2)))];
        MaterialId[] materials =
        [
            .. Enumerable
                .Range(0, 3)
                .Select(i =>
                    queue.AddMaterial(new(BaseColor: Vector4.One, Roughness: 0.38f, BaseColorMap: textures[i]))
                ),
        ];

        meshes.Select(m => m.Value).Should().Equal(0, 1, 2);
        textures.Select(t => t.Value).Should().Equal(0, 1, 2);
        materials.Select(m => m.Value).Should().Equal(0, 1, 2);
        meshes.Should().NotContain(MeshId.None);
        textures.Should().NotContain(TextureId.None);
        materials.Should().NotContain(MaterialId.None);
    }

    [Fact]
    public void ADrainHandsOverEachResourceOnceInIdOrder()
    {
        var queue = new UploadQueue(maxTextureSize: MaxTextureSize, deformBytesLimit: 1L << 27);
        TextureId first = queue.AddTexture(Pixels(1, 1));
        MeshId mesh = queue.AddMesh(Primitives.Box(Vector3.One));
        MaterialId material = queue.AddMaterial(new(BaseColor: Vector4.One, Roughness: 0.38f, BaseColorMap: first));
        UploadQueue.Pending batch = queue.Drain();
        batch.Textures.Select(t => t.Id).Should().Equal(first);
        batch.Meshes.Select(m => m.Id).Should().Equal(mesh);
        batch.Materials.Select(m => m.Id).Should().Equal(material);

        // Resources created after a drain come in the next batch, without those of the batch before: the stores
        // append each batch to what they hold, so a repeated resource would land in the wrong slot.
        TextureId second = queue.AddTexture(Pixels(1, 1));
        batch = queue.Drain();
        batch.Textures.Select(t => t.Id).Should().Equal(second);
        batch.Meshes.Should().BeEmpty();
        batch.Materials.Should().BeEmpty();
        batch.IsEmpty.Should().BeFalse();

        queue.Drain().IsEmpty.Should().BeTrue("nothing was created since the last drain");
    }

    [Fact]
    public void TexturesUpToTheDeviceLimitAreAcceptedAndLargerOrEmptyOnesAreNot()
    {
        var queue = new UploadQueue(maxTextureSize: MaxTextureSize, deformBytesLimit: 1L << 27);
        queue.AddTexture(Pixels(MaxTextureSize, 1)).Value.Should().Be(0);
        queue.AddTexture(Pixels(1, MaxTextureSize)).Value.Should().Be(1);
        FluentActions
            .Invoking(() => queue.AddTexture(Pixels(MaxTextureSize + 1, 1)))
            .Should()
            .ThrowExactly<ArgumentOutOfRangeException>();
        FluentActions
            .Invoking(() => queue.AddTexture(Pixels(1, MaxTextureSize + 1)))
            .Should()
            .ThrowExactly<ArgumentOutOfRangeException>();
        FluentActions
            .Invoking(() => queue.AddTexture(Pixels(0, 1)))
            .Should()
            .ThrowExactly<ArgumentOutOfRangeException>();
        FluentActions
            .Invoking(() => queue.AddTexture(Pixels(1, 0)))
            .Should()
            .ThrowExactly<ArgumentOutOfRangeException>();
        FluentActions
            .Invoking(() => queue.AddTexture(new(Width: 2, Height: 3, Rgba: new byte[2 * 3 * 4 - 1])))
            .Should()
            .ThrowExactly<ArgumentException>("2 × 3 RGBA8 is 24 bytes");
        FluentActions
            .Invoking(() => queue.AddTexture(new(Width: 2, Height: 3, Rgba: new byte[2 * 3 * 4 + 4])))
            .Should()
            .ThrowExactly<ArgumentException>();
        // A rejected texture takes no id.
        queue.AddTexture(Pixels(2, 3)).Value.Should().Be(2);
    }

    [Fact]
    public void MissingDataIsRejectedBeforeAnIdIsTaken()
    {
        var queue = new UploadQueue(maxTextureSize: MaxTextureSize, deformBytesLimit: 1L << 27);
        FluentActions.Invoking(() => queue.AddMesh(null!)).Should().ThrowExactly<ArgumentNullException>();
        FluentActions
            .Invoking(() => queue.AddMesh(new(null!, [0, 0, 0])))
            .Should()
            .ThrowExactly<ArgumentNullException>();
        FluentActions.Invoking(() => queue.AddMesh(new([new()], null!))).Should().ThrowExactly<ArgumentNullException>();
        FluentActions
            .Invoking(() => queue.AddMesh(new([new()], [])))
            .Should()
            .ThrowExactly<ArgumentException>("a mesh needs indices");
        FluentActions
            .Invoking(() => queue.AddMesh(new([], [0, 0, 0])))
            .Should()
            .ThrowExactly<ArgumentException>("a mesh needs vertices");
        FluentActions.Invoking(() => queue.AddTexture(null!)).Should().ThrowExactly<ArgumentNullException>();
        FluentActions
            .Invoking(() => queue.AddTexture(new(Width: 1, Height: 1, Rgba: null!)))
            .Should()
            .ThrowExactly<ArgumentNullException>();
        FluentActions.Invoking(() => queue.AddMaterial(null!)).Should().ThrowExactly<ArgumentNullException>();

        queue.AddMesh(Primitives.Box(Vector3.One)).Value.Should().Be(0);
        queue.AddTexture(Pixels(1, 1)).Value.Should().Be(0);
        queue.AddMaterial(new(BaseColor: Vector4.One, Roughness: 0.38f)).Value.Should().Be(0);
        queue.Drain().Meshes.Should().ContainSingle("rejected meshes are not queued");
    }

    [Fact]
    public void AMaterialNamesOnlyNoneOrTexturesThisQueueCreated()
    {
        var queue = new UploadQueue(maxTextureSize: MaxTextureSize, deformBytesLimit: 1L << 27);
        FluentActions
            .Invoking(() => queue.AddMaterial(new(BaseColor: Vector4.One, Roughness: 0.38f, BaseColorMap: new(0))))
            .Should()
            .ThrowExactly<ArgumentException>("texture 0 does not exist yet");
        TextureId texture = queue.AddTexture(Pixels(1, 1));
        queue
            .AddMaterial(
                new(
                    BaseColor: Vector4.One,
                    Roughness: 0.38f,
                    BaseColorMap: texture,
                    NormalMap: TextureId.None,
                    MetallicRoughnessMap: texture
                )
            )
            .Value.Should()
            .Be(0);
        FluentActions
            .Invoking(() => queue.AddMaterial(new(BaseColor: Vector4.One, Roughness: 0.38f, NormalMap: new(1))))
            .Should()
            .ThrowExactly<ArgumentException>();
        FluentActions
            .Invoking(() =>
                queue.AddMaterial(new(BaseColor: Vector4.One, Roughness: 0.38f, MetallicRoughnessMap: new(-2)))
            )
            .Should()
            .ThrowExactly<ArgumentException>("no texture has a negative id");
        // A rejected material takes no id.
        queue.AddMaterial(new(BaseColor: Vector4.One, Roughness: 0.38f)).Value.Should().Be(1);
    }

    [Fact]
    public void TheBindlessArrayFillsUpBesideTheBuiltInDefaults()
    {
        var queue = new UploadQueue(maxTextureSize: MaxTextureSize, deformBytesLimit: 1L << 27);
        TextureId last = TextureId.None;
        for (int i = 0; i < TextureStore.UserCapacity; i++)
            last = queue.AddTexture(Pixels(1, 1));
        TextureStore.Slot(last).Should().Be(TextureStore.Capacity - 1, "the last texture takes the array's last slot");
        FluentActions
            .Invoking(() => queue.AddTexture(Pixels(1, 1)))
            .Should()
            .ThrowExactly<InvalidOperationException>("the array is full");
    }
}
