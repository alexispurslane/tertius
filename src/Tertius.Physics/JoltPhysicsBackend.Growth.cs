using System.Numerics;
using JoltPhysicsSharp;

namespace Tertius.Physics;

public sealed partial class JoltPhysicsBackend
{
    // Scratch for a growth: the IDs of the bodies of the full system, its characters, and the inner bodies that changed ID.
    private BodyID[] growthIds = new BodyID[256];
    private readonly List<BodyRecord> growthCharacters = new();
    private readonly Dictionary<uint, uint> renamedBodies = new();

    private static readonly Comparer<BodyID> ByIndex = Comparer<BodyID>.Create(static (a, b) => a.Index.CompareTo(b.Index));

    /// <summary>
    /// Makes room for one more body in the system of <paramref name="world"/>: grows the system when it is full. Every
    /// path that adds a rigid body or a character to a system calls it first, so no path fails for lack of room.
    /// </summary>
    private void EnsureRoom(FrameWorld world)
    {
        if (world.IsFull)
            Grow(world);
    }

    /// <summary>
    /// Replaces the full system of <paramref name="world"/> with a new system whose limits are twice as large, and moves
    /// every body into it. The step after the growth starts from the same state, with these exceptions:
    /// <list type="bullet">
    /// <item>Every rigid body keeps its ID, its spec (see <see cref="RigidSpec"/>), its pose, both velocities, and whether
    /// it is awake. Jolt keeps a body's center of mass, so a body whose center of mass is off its origin can move by a
    /// rounding error of its double position.</item>
    /// <item>Every character keeps its pose, velocity and up direction, and the backend's input state for it. Jolt gives
    /// its new inner body a new ID, and the character looks up its contacts and its ground again.</item>
    /// <item>Jolt's contact cache stays behind, so the first update in the new system solves every contact without the
    /// impulses of the last update (no warm start), and each body's sleep timer starts again.</item>
    /// <item>Every pair that touches stays touching: the backend does not report its contact again.</item>
    /// </list>
    /// The move is deterministic: the rigid bodies move in the order of their IDs, then the characters in the order of
    /// the IDs of their old inner bodies. The world, its member list, every record and every rigid body ID stay the same,
    /// so a growth in the middle of a step, while a loop walks the members or the ghosts of any system, changes nothing
    /// that the loop holds. Only <see cref="FrameWorld.System"/>, <see cref="FrameWorld.Bodies"/> and the characters of
    /// this world change. Costs time in proportion to the number of bodies: see <see cref="JoltBackendOptions.MaxBodiesPerFrame"/>.
    /// </summary>
    private unsafe void Grow(FrameWorld world)
    {
        SystemLimits limits = world.Limits.Doubled();
        if (limits.MaxBodies <= world.Limits.MaxBodies)
            throw new InvalidOperationException($"The system of {world.Frame} holds {world.Limits.MaxBodies} bodies, the most that Jolt allows.");

        PhysicsSystem full = world.System;
        BodyInterface from = world.Bodies;
        PhysicsSystem larger = world.CreateSystem(limits);
        BodyInterface to = larger.BodyInterface;

        int count = (int)full.BodiesCount;
        if (count > growthIds.Length)
            Array.Resize(ref growthIds, Math.Max(count, growthIds.Length * 2));
        fixed (BodyID* first = growthIds)
            full.GetBodies(first, (uint)count);
        Array.Sort(growthIds, 0, count, ByIndex);

        for (int i = 0; i < count; i++)
        {
            BodyID id = growthIds[i];
            ulong userData = from.GetUserData(id);
            BodyKind kind = BodyUserData.Kind(userData);
            // A character's inner body moves with its character, below.
            if (kind == BodyKind.CharacterInner)
                continue;
            BodyRecord? record = RecordFor(userData);
            // Every body in a system belongs to a live record. A body without one comes only from a bug.
            if (record is null)
            {
                ReportBug($"Physics: {id} in {world.Frame} belongs to no entity. Only a bug leaves such a body. The growth of the system leaves it behind.");
                continue;
            }
            RigidSpec spec = kind switch
            {
                BodyKind.Primary => PrimarySpec(record),
                BodyKind.Interior => InteriorSpec(record),
                _ => GhostSpec(record, from.GetMotionType(id)),
            };
            from.GetRPosition(id, out RVector3 position);
            from.GetLinearAndAngularVelocity(id, out Vector3 linear, out Vector3 angular);
            using BodyCreationSettings settings = Settings(spec, position, from.GetRotation(id), linear, angular);
            if (to.CreateBodyWithID(id, settings) is null)
                throw new InvalidOperationException($"Jolt refused to move {id} into the grown system of {world.Frame}.");
            to.AddBody(id, from.IsActive(id) ? Activation.Activate : Activation.DontActivate);
        }

        growthCharacters.Clear();
        foreach (BodyRecord member in world.Members)
            if (member.Character is not null)
                growthCharacters.Add(member);
        growthCharacters.Sort(static (a, b) => a.Character!.InnerBodyID.Index.CompareTo(b.Character!.InnerBodyID.Index));
        renamedBodies.Clear();
        foreach (BodyRecord member in growthCharacters)
        {
            // A character removes its inner body from its system, so it goes before the full system does.
            CharacterVirtual old = member.Character!;
            RVector3 position = old.RPosition;
            Quaternion rotation = old.Rotation;
            Vector3 velocity = old.LinearVelocity;
            BodyID oldInner = old.InnerBodyID;
            old.Dispose();
            member.Character = NewCharacter(member, larger, position, rotation, velocity);
            renamedBodies[oldInner.ID] = member.Character.InnerBodyID.ID;
        }

        // Jolt builds a better broad-phase tree at once than one body at a time.
        larger.OptimizeBroadPhase();
        world.ReplaceSystem(larger, limits, renamedBodies);
        queryBodyFilter?.ForgetBodies();
        foreach (BodyRecord member in growthCharacters)
            member.Character!.RefreshContacts(PrimaryLayer(member), larger);
        growthCharacters.Clear();

        log($"Physics: the system of {world.Frame} was full with {count} bodies, so it grew to hold {limits.MaxBodies}. " +
            "Raise JoltBackendOptions.MaxBodiesPerFrame to avoid the pause.");
        Contract.Ensures(world.System == larger && !world.IsFull);
    }
}
