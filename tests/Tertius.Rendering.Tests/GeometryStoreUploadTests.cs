using System.Numerics;

namespace Tertius.Rendering.Tests;

/// <summary>
/// The mesh records that an upload of <see cref="GeometryStore"/> adds. A validating headless device; the recorded
/// commands are never submitted.
/// </summary>
public sealed class GeometryStoreUploadTests
{
    [Fact]
    public void AnUploadedMeshsRecordSaysWhetherTheMeshIsClosed()
    {
        using var plumbing = new RecordingPlumbing();
        using var store = new GeometryStore(plumbing.Device, plumbing.Allocator);
        using var deforms = new DeformStore(plumbing.Device, plumbing.Allocator, maxBytes: 1 << 20);
        using var staging = new StagingArena(plumbing.Device, plumbing.Allocator, "upload");

        store.Upload(
            commandBuffer: plumbing.Commands,
            pending:
            [
                (new MeshId(0), StagedMesh.Of(Primitives.Box(Vector3.One))),
                (new MeshId(1), StagedMesh.Of(Primitives.Plane(1))),
            ],
            deforms: [null, null],
            deformStore: deforms,
            staging: staging,
            barriers: plumbing.Barriers,
            releases: plumbing.Releases,
            frame: 0
        );

        store.Meshes.ToArray().Select(mesh => mesh.Closed).Should().Equal(true, false);
    }
}
