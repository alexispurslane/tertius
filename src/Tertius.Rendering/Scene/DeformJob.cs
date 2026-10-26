using System.Diagnostics;
using System.Numerics;

namespace Tertius.Rendering;

/// <summary>
/// The deform region of a frame: the part of the global vertex buffer after the uploaded vertices that holds the frame's
/// deformed copies (docs/plans/skinning-and-morphs.md, "Posing in the batcher").
/// </summary>
/// <param name="Base">The first vertex of the region in the global vertex buffer, a multiple of 16.</param>
/// <param name="Capacity">The number of vertices that the region holds.</param>
internal readonly record struct DeformRegion(int Base, int Capacity)
{
    /// <summary>The empty region: every posed instance draws as uploaded.</summary>
    public static DeformRegion None => new(Base: 0, Capacity: 0);
}

/// <summary>The work of one deformed copy of a frame.</summary>
/// <param name="Mesh">The static mesh of the job, whose <see cref="MeshInfo.Deform"/> it deforms.</param>
/// <param name="Target">The first vertex of the copy, counted from the region's base.</param>
/// <param name="Pose">The first of the job's pose rows in <see cref="PreparedScene.Poses"/>.</param>
internal readonly record struct DeformJob(int Mesh, int Target, int Pose);

/// <summary>
/// The frame meshes of one frame as the passes that cache by mesh see them: which are deformed copies, the static mesh of
/// each, and the pose rows of each copy. A frame mesh is a deformed copy exactly when its index is at least the static
/// mesh count.
/// </summary>
internal readonly ref struct DeformedMeshes
{
    private readonly ReadOnlySpan<MeshInfo> meshes;
    private readonly int staticMeshCount;
    private readonly ReadOnlySpan<DeformJob> jobs;
    private readonly ReadOnlySpan<Vector4> poses;

    /// <param name="meshes">The frame meshes: the static meshes, then one per job.</param>
    /// <param name="staticMeshCount">How many of <paramref name="meshes"/> are static meshes.</param>
    /// <param name="jobs">The frame's jobs, in the order of the copies' frame meshes.</param>
    /// <param name="poses">The frame's pose rows, which the jobs name.</param>
    public DeformedMeshes(
        ReadOnlySpan<MeshInfo> meshes,
        int staticMeshCount,
        ReadOnlySpan<DeformJob> jobs,
        ReadOnlySpan<Vector4> poses
    )
    {
        this.meshes = meshes;
        this.staticMeshCount = staticMeshCount;
        this.jobs = jobs;
        this.poses = poses;
    }

    /// <summary>Whether frame mesh <paramref name="mesh"/> is a deformed copy.</summary>
    public bool IsDeformed(int mesh) => mesh >= staticMeshCount;

    /// <summary>The static mesh of a frame mesh: itself for a static mesh, and its job's mesh for a deformed copy.</summary>
    public int SourceOf(int mesh) => IsDeformed(mesh) ? jobs[mesh - staticMeshCount].Mesh : mesh;

    /// <summary>The pose rows of a deformed copy, and an empty span for a static mesh.</summary>
    public ReadOnlySpan<Vector4> PoseOf(int mesh)
    {
        if (!IsDeformed(mesh))
            return [];
        DeformJob job = jobs[mesh - staticMeshCount];
        int rows = meshes[job.Mesh] switch
        {
            { Deform: { } deform } => deform.PoseRows,
            _ => throw new UnreachableException("Only a deforming mesh has a job."),
        };
        return poses.Slice(job.Pose, rows);
    }
}
