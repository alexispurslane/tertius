using System.Runtime.InteropServices;

namespace Tertius.Simulation;

internal sealed partial class EntityTable
{
    // The block of each live slot whose entity spawned with a rig pose, and null for every other slot.
    private readonly SlotColumn<RigPoseBlock?> rigPoses;

    // The blocks of the entities that died, by their shape, for the next spawn of that shape. A world whose posed entities
    // only come and go so allocates nothing once its pool holds every shape.
    private readonly Dictionary<(int Joints, int Weights), Stack<RigPoseBlock>> rigPosePool = [];

    /// <summary>
    /// The rig pose of the live entity <paramref name="entity"/>, which spawned with one, or null for an entity without a
    /// rig pose. The snapshot builder reads the front copy and writes the placed copy. Throws
    /// <see cref="StaleEntityException"/> for a stale handle. Worker-safe.
    /// </summary>
    public RigPoseBlock? RigPoseOf(Entity entity) => rigPoses[LiveSlot(entity)];

    /// <summary>
    /// Gives the live entity <paramref name="entity"/>, which has no rig pose, the rig pose of its spawn: a block of its
    /// shape from the pool, or a new one, with <paramref name="pose"/> in all three copies. Part of a spawn, outside the
    /// gameplay part of a tick. Throws <see cref="StaleEntityException"/> for a stale handle.
    /// </summary>
    public void SetSpawnRigPose(Entity entity, RigPose pose)
    {
        uint slot = LiveSlot(entity);
        Contract.Requires(!GameplayRunning && rigPoses[slot] is null);
        (int Joints, int Weights) shape = (pose.Joints.Length, pose.Weights.Length);
        RigPoseBlock? block = null;
        if (rigPosePool.TryGetValue(shape, out Stack<RigPoseBlock>? pooled))
            pooled.TryPop(out block);
        block ??= new(jointCount: shape.Joints, weightCount: shape.Weights);
        block.Reset(pose);
        rigPoses[slot] = block;
    }

    /// <summary>
    /// The running entity's back rig pose, for writing. It marks the render row dirty, so that the sync point copies the
    /// rig pose with it. Throws <see cref="InvalidOperationException"/> for an entity that spawned without a rig pose, and
    /// <see cref="StaleEntityException"/> for a stale handle. Worker-safe for the running entity.
    /// </summary>
    public RigPoseWriter WritableRigPose(Entity entity)
    {
        uint slot = LiveSlot(entity);
        Contract.Requires(GameplayRunning);
        RigPoseBlock block =
            rigPoses[slot]
            ?? throw new InvalidOperationException(
                $"{entity} spawned without a rig pose, so it has none to write. Give its spawn a SpawnDesc.RigPose."
            );
        MarkDirty(slot, SlotFlags.RenderDirty);
        return new(block.BackJoints, block.BackWeights);
    }

    /// <summary>Returns the block of a slot that the table releases to the pool. Part of <see cref="Release"/>.</summary>
    private void ReturnRigPose(uint slot)
    {
        if (rigPoses[slot] is not { } block)
            return;
        (int Joints, int Weights) shape = (block.JointCount, block.WeightCount);
        if (!rigPosePool.TryGetValue(shape, out Stack<RigPoseBlock>? pooled))
            rigPosePool.Add(shape, pooled = new());
        pooled.Push(block);
    }

    /// <summary>
    /// The invariants of the rig poses: a slot that is not live has no block, no block belongs to two slots or to a slot and
    /// the pool, each pooled block is in the stack of its shape, and a slot whose render row is clean has equal front and
    /// back rig poses. Part of <see cref="AssertInvariants"/>.
    /// </summary>
    private void AssertRigPoseInvariants()
    {
        var owned = new HashSet<RigPoseBlock>(ReferenceEqualityComparer.Instance);
        for (int slot = 0; slot < slotCount; slot++)
        {
            if (rigPoses[slot] is not { } block)
                continue;
            bool firstOwner = owned.Add(block);
            Contract.Invariant(state[slot] == SlotState.Live && firstOwner);
            block.AssertShape();
            if ((flags[slot] & SlotFlags.RenderDirty) == 0)
                Contract.Invariant(block.FrontEqualsBack());
        }

        foreach (((int Joints, int Weights) shape, Stack<RigPoseBlock> pooled) in rigPosePool)
        foreach (RigPoseBlock block in pooled)
        {
            Contract.Invariant(!owned.Contains(block) && (block.JointCount, block.WeightCount) == shape);
            block.AssertShape();
        }
    }

    /// <summary>
    /// The rig pose of one posed entity, in three copies of its J joint poses and W weights (docs/plans/skinning-and-morphs.md,
    /// "The rig pose in the simulation"): the back, which the entity writes in its tick, the front, which the sync point
    /// copies from the back, and the placed copy, which the snapshot builder keeps from its last build of the entity.
    /// </summary>
    /// <remarks>
    /// <list type="bullet">
    /// <item>Each kind of value lives in one array of three copies, back, front and placed, each of the block's count.</item>
    /// <item><see cref="PlacedStamp"/> is the build that wrote the placed copy, or 0, which no build has, since the spawn.</item>
    /// </list>
    /// </remarks>
    internal sealed class RigPoseBlock(int jointCount, int weightCount)
    {
        private readonly Pose[] joints = new Pose[3 * jointCount];
        private readonly float[] weights = new float[3 * weightCount];

        public int JointCount { get; } = jointCount;
        public int WeightCount { get; } = weightCount;

        /// <summary>The build that wrote the placed copy, or 0 when no build has since the spawn.</summary>
        public long PlacedStamp { get; private set; }

        public Span<Pose> BackJoints => joints.AsSpan(0, JointCount);
        public Span<float> BackWeights => weights.AsSpan(0, WeightCount);
        public ReadOnlySpan<Pose> FrontJoints => joints.AsSpan(JointCount, JointCount);
        public ReadOnlySpan<float> FrontWeights => weights.AsSpan(WeightCount, WeightCount);
        public ReadOnlySpan<Pose> PlacedJoints => joints.AsSpan(2 * JointCount, JointCount);
        public ReadOnlySpan<float> PlacedWeights => weights.AsSpan(2 * WeightCount, WeightCount);

        /// <summary>Writes <paramref name="pose"/>, of the block's shape, into all three copies, with no build's stamp.</summary>
        public void Reset(RigPose pose)
        {
            Contract.Requires(pose.Joints.Length == JointCount && pose.Weights.Length == WeightCount);
            for (int copy = 0; copy < 3; copy++)
            {
                pose.Joints.AsSpan().CopyTo(joints.AsSpan(copy * JointCount, JointCount));
                pose.Weights.AsSpan().CopyTo(weights.AsSpan(copy * WeightCount, WeightCount));
            }

            PlacedStamp = 0;
        }

        /// <summary>Copies the back copy into the front copy: step 8 of the sync point, with the render row.</summary>
        public void CopyBackToFront()
        {
            joints.AsSpan(0, JointCount).CopyTo(joints.AsSpan(JointCount, JointCount));
            weights.AsSpan(0, WeightCount).CopyTo(weights.AsSpan(WeightCount, WeightCount));
        }

        /// <summary>Copies the front copy into the placed copy, and stamps it with the build <paramref name="stamp"/>.</summary>
        public void Place(long stamp)
        {
            Contract.Requires(stamp > PlacedStamp);
            FrontJoints.CopyTo(joints.AsSpan(2 * JointCount, JointCount));
            FrontWeights.CopyTo(weights.AsSpan(2 * WeightCount, WeightCount));
            PlacedStamp = stamp;
        }

        /// <summary>True when the front copy equals the back copy bit for bit.</summary>
        public bool FrontEqualsBack() => BitEquals(BackJoints, FrontJoints) && BitEquals(BackWeights, FrontWeights);

        /// <summary>Checks that the arrays hold three copies of the block's counts.</summary>
        public void AssertShape() =>
            Contract.Invariant(
                joints.Length == 3 * JointCount && weights.Length == 3 * WeightCount && PlacedStamp >= 0
            );

        private static bool BitEquals<T>(ReadOnlySpan<T> a, ReadOnlySpan<T> b)
            where T : unmanaged => MemoryMarshal.AsBytes(a).SequenceEqual(MemoryMarshal.AsBytes(b));
    }
}
