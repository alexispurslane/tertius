using Silk.NET.Vulkan;

namespace Tertius.Rendering.Tests;

/// <summary>
/// The reference image that a snapshot test compares with on each driver (ticket ter-8esk). The shared reference
/// &lt;name&gt;.png was recorded on MoltenVK. Another driver can round a few pixels across a decision of the shader, so
/// it can have its own reference, &lt;name&gt;.&lt;driver&gt;.png, approved after a look at its images.
/// </summary>
public sealed class ReferencePathTests : IDisposable
{
    private readonly DirectoryInfo folder = Directory.CreateTempSubdirectory("tertius-reference-path-");

    public void Dispose() => folder.Delete(recursive: true);

    private string Shared => Path.Combine(folder.FullName, "scene.png");
    private string Nvidia => Path.Combine(folder.FullName, "scene.nvidiaproprietary.png");

    private string PathOn(DriverId driver, bool approving = false) =>
        ReferenceImages.ReferencePath(folder: folder.FullName, name: "scene", driver: driver, approving: approving);

    [Fact]
    public void TheRecordingDriverComparesWithTheSharedReferenceEvenBesideAnotherDriversOwn()
    {
        File.WriteAllText(Nvidia, "");

        PathOn(ReferenceImages.RecordedOn).Should().Be(Shared);
        PathOn(ReferenceImages.RecordedOn, approving: true).Should().Be(Shared);
    }

    [Fact]
    public void AnotherDriverWithoutItsOwnReferenceComparesWithTheSharedOne() =>
        PathOn(DriverId.NvidiaProprietary).Should().Be(Shared);

    [Fact]
    public void AnotherDriverWithItsOwnReferenceComparesWithIt()
    {
        File.WriteAllText(Nvidia, "");

        PathOn(DriverId.NvidiaProprietary).Should().Be(Nvidia);
        PathOn(DriverId.MesaRadv).Should().Be(Shared, "only NVIDIA's driver has its own reference");
    }

    [Fact]
    public void ApprovalOnAnotherDriverWritesItsOwnReferenceAndLeavesTheSharedOne() =>
        PathOn(DriverId.NvidiaProprietary, approving: true).Should().Be(Nvidia);
}
