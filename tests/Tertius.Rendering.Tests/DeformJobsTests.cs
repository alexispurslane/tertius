namespace Tertius.Rendering.Tests;

/// <summary>
/// The deform pass's job table, the search by which each workgroup finds its job, and the grid of its one dispatch,
/// without a GPU (docs/plans/skinning-and-morphs.md, "The pass").
/// </summary>
public sealed class DeformJobsTests
{
    /// <summary>A static mesh of <paramref name="vertices"/> vertices that deforms by one root joint and no morph.</summary>
    private static MeshInfo Deforming(int vertices)
    {
        var mesh = new MeshData(new Vertex[vertices], [0, 0, 0])
        {
            Rigging = new(new Rig([new RigJoint("Root", null, Pose.Identity)], []), Skin: null, Morphs: []),
        };
        return new(
            FirstIndex: 0,
            IndexCount: 3,
            VertexOffset: 0,
            Radius: 1,
            Deform: MeshDeform.Pack(mesh, MeshCards.Number(mesh.Vertices, mesh.Indices))!.Deform
        );
    }

    /// <summary>The records of one job of each mesh of <paramref name="vertexCounts"/>, and the frame's workgroups.</summary>
    private static (GpuDeformJob[] Records, uint Groups) RecordsOf(params int[] vertexCounts)
    {
        MeshInfo[] meshes = [.. vertexCounts.Select(Deforming)];
        DeformJob[] jobs = [.. vertexCounts.Select((_, mesh) => new DeformJob(Mesh: mesh, Target: 0, Pose: 0))];
        var records = new GpuDeformJob[jobs.Length];
        uint groups = GpuDeformJob.FromJobs(jobs: jobs, meshes: meshes, records: records);
        return (records, groups);
    }

    [Fact]
    public void RecordNamesTheMeshsSourcesAndTheJobsCopyAndRows()
    {
        MeshData cylinder = Primitives.BendingCylinder(radius: 0.2f, length: 2);
        MeshDeform deform = MeshDeform
            .Pack(cylinder, MeshCards.Number(cylinder.Vertices, cylinder.Indices))!
            .Deform with
        {
            Source = 100,
            Deltas = 7,
        };
        MeshInfo[] meshes =
        [
            new(FirstIndex: 0, IndexCount: 3, VertexOffset: 0, Radius: 1, Deform: null),
            new(FirstIndex: 3, IndexCount: 6, VertexOffset: 3, Radius: 1, Deform: deform),
        ];
        var records = new GpuDeformJob[1];

        GpuDeformJob.FromJobs(jobs: [new(Mesh: 1, Target: 512, Pose: 30)], meshes: meshes, records: records);

        GpuDeformJob record = records[0];
        record.Source.Should().Be(100);
        record.Deltas.Should().Be(7);
        record.Target.Should().Be(512);
        record.Count.Should().Be((uint)deform.VertexCount);
        record.Palette.Should().Be(30);
        record.Weights.Should().Be(30 + 3 * 2, "the weight rows follow three palette rows for each of the two joints");
        record.FirstGroup.Should().Be(0, "the first job starts the dispatch");
    }

    [Fact]
    public void EachJobsWorkgroupsFollowThoseOfTheJobBefore()
    {
        (GpuDeformJob[] records, uint groups) = RecordsOf(1, 64, 65, 425);

        records.Select(record => record.FirstGroup).Should().Equal([0u, 1u, 2u, 4u], "1, 1, 2 and 7 groups");
        groups.Should().Be(11);
    }

    [Fact]
    public void EveryWorkgroupFindsTheJobWhoseVerticesItHolds()
    {
        int[] counts = [425, 24, 65, 1, 128];
        (GpuDeformJob[] records, uint groups) = RecordsOf(counts);
        int[] expected =
        [
            .. counts.SelectMany((count, job) => Enumerable.Repeat(job, (int)DeformPass.Groups((uint)count))),
        ];

        int[] found = [.. Enumerable.Range(0, (int)groups).Select(group => DeformPass.JobOf(records, (uint)group))];

        found.Should().Equal(expected, "each job's groups run from its first group up to the next job's first");
    }

    [Theory]
    [InlineData(0, 0u, 6u)]
    [InlineData(1, 7u, 7u)]
    [InlineData(2, 8u, 9u)]
    public void FirstAndLastWorkgroupOfAJobFindIt(int job, uint first, uint last)
    {
        (GpuDeformJob[] records, _) = RecordsOf(425, 24, 65);

        DeformPass.JobOf(records, first).Should().Be(job, "the job's first workgroup");
        DeformPass.JobOf(records, last).Should().Be(job, "the job's last workgroup");
    }

    [Fact]
    public void JobOfOneVertexHasOneWorkgroupBetweenItsNeighbours()
    {
        (GpuDeformJob[] records, uint groups) = RecordsOf(64, 1, 64);

        groups.Should().Be(3);
        DeformPass.JobOf(records, 0).Should().Be(0);
        DeformPass.JobOf(records, 1).Should().Be(1, "the one vertex's job holds one workgroup");
        DeformPass.JobOf(records, 2).Should().Be(2);
    }

    [Fact]
    public void SingleJobOwnsEveryWorkgroup()
    {
        (GpuDeformJob[] records, uint groups) = RecordsOf(425);

        Enumerable.Range(0, (int)groups).Should().OnlyContain(group => DeformPass.JobOf(records, (uint)group) == 0);
    }
}
