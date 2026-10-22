using JoltPhysicsSharp;

namespace Tertius.Physics;

/// <summary>
/// Object layers and broad-phase layers. Each object layer is Jolt's pair of 16-bit sets in
/// <see cref="ObjectLayerPairFilterMask"/> (joltc builds with 32-bit object layers): the body's groups in the low half and
/// its mask in the high half. Two bodies collide when the groups of each one meet the mask of the other, which is Jolt's
/// own pair test:
/// <code>
/// (group1 &amp; mask2) != 0 &amp;&amp; (group2 &amp; mask1) != 0
/// </code>
/// Bits 0 to 11 of each half are content's collision groups (see <see cref="CollisionGroup"/>), and bits 12 to 15 are the
/// engine's roles: static, moving, frame hull and ghost. A body's groups are its role and its collision groups. Its mask is
/// the any-of set of its collision mask, and a static body's mask also names the frame hull role. So between two bodies
/// that are not hulls, only their collision groups and masks decide. A hull is in the frame hull role alone, and its mask
/// names the static and frame hull roles, so it meets every static body and every other hull, and nothing else.
/// <para>
/// With the default groups this gives the table in ARCHITECTURE.md for every pair in which a body can be dynamic. The
/// pairs of two bodies that never are (static with static, static with ghost, ghost with ghost) pass the filter when
/// their groups allow it, but Jolt makes no contact between two bodies that are not dynamic.
/// </para>
/// </summary>
internal static class Layers
{
    private const int RoleShift = CollisionGroup.Capacity;
    private const uint StaticRole = 1u << RoleShift;
    private const uint MovingRole = 1u << (RoleShift + 1);
    private const uint HullRole = 1u << (RoleShift + 2);
    private const uint GhostRole = 1u << (RoleShift + 3);
    private const uint AllGroups = (1u << CollisionGroup.Capacity) - 1;
    private const uint AllRoles = StaticRole | MovingRole | HullRole | GhostRole;

    /// <summary>
    /// The mask half of an any-of set that is empty, which is no condition: every collision group, and the role of every
    /// body that is not a hull, so that a body in no collision group matches too.
    /// </summary>
    private const uint MatchesAnything = AllGroups | StaticRole | MovingRole | GhostRole;

    /// <summary>The layer of every frame hull, whatever the groups and the mask of its owner.</summary>
    public static ObjectLayer FrameHull { get; } = Encode(HullRole, HullRole | StaticRole);

    /// <summary>
    /// The object layer of a body in <paramref name="role"/> with <paramref name="groups"/> and <paramref name="mask"/>.
    /// The static role is for static bodies and the interiors of frames. The moving role is for dynamic and kinematic
    /// bodies and for characters, whose movement also uses the layer. The ghost role is for ghosts, which carry the groups
    /// and the mask of their body. A frame hull ignores both (see <see cref="FrameHull"/>). The mask holds only an any-of
    /// set (see <see cref="BodyDesc.Problem"/>).
    /// </summary>
    public static ObjectLayer For(BodyLayer role, CollisionGroups groups, CollisionMask mask)
    {
        Contract.Requires(mask.AllOf.IsEmpty && mask.NoneOf.IsEmpty);
        uint anyOf = mask.AnyOf.IsEmpty ? MatchesAnything : mask.AnyOf.Bits;
        return role switch
        {
            BodyLayer.Static => Encode(StaticRole | groups.Bits, anyOf | HullRole),
            BodyLayer.Moving => Encode(MovingRole | groups.Bits, anyOf),
            BodyLayer.Ghost => Encode(GhostRole | groups.Bits, anyOf),
            BodyLayer.FrameHull => FrameHull
        };
    }

    /// <summary>Jolt's <c>ObjectLayerPairFilterMask::sGetObjectLayer</c> for 32-bit object layers.</summary>
    private static ObjectLayer Encode(uint groups, uint mask) => groups | (mask << 16);

    /// <summary>
    /// True when bodies of the two layers collide: Jolt's pair test of <see cref="ObjectLayerPairFilterMask"/>, which the
    /// summary of this class shows. Jolt also needs their motions to allow a contact.
    /// </summary>
    public static bool Meet(ObjectLayer a, ObjectLayer b)
    {
        uint x = a, y = b;
        return (x & 0xFFFF & (y >> 16)) != 0 && (y & 0xFFFF & (x >> 16)) != 0;
    }

    /// <summary>
    /// The broad-phase layers, one for each role. A body goes into the layer of its role. A body looks in a layer only
    /// when its mask meets the groups that the layer admits, except the last one, the ghosts' layer, in which Jolt lets
    /// every body look.
    /// </summary>
    private enum BroadPhaseLayers : byte
    {
        Static,
        Moving,
        FrameHull,
        Ghost
    }

    /// <summary>
    /// The Jolt filters of one physics system. joltc's <c>JPH_PhysicsSystem_Destroy</c> deletes the three filters that
    /// the system was created with. So each system gets its own set, the system frees it, and the managed objects here
    /// never free it.
    /// </summary>
    public sealed class Tables
    {
        public Tables()
        {
            Pairs = NativeOwnership.Disowned(new ObjectLayerPairFilterMask());

            BroadPhase = NativeOwnership.Disowned(new BroadPhaseLayerInterfaceMask(4));
            // A layer admits the bodies of its role, and it lists every collision group too, because a body looks in it
            // only when its mask meets one of those groups. The hull layer lists no collision group, since only the mask
            // of a hull or of a static body names the hull role.
            Admit(BroadPhaseLayers.Static, StaticRole | AllGroups);
            Admit(BroadPhaseLayers.Moving, MovingRole | AllGroups);
            Admit(BroadPhaseLayers.FrameHull, HullRole);
            Admit(BroadPhaseLayers.Ghost, GhostRole | AllGroups);

            ObjectVsBroadPhase = NativeOwnership.Disowned(new ObjectVsBroadPhaseLayerFilterMask(BroadPhase));
        }

        public ObjectLayerPairFilterMask Pairs { get; }
        public BroadPhaseLayerInterfaceMask BroadPhase { get; }
        public ObjectVsBroadPhaseLayerFilterMask ObjectVsBroadPhase { get; }

        /// <summary>Makes <paramref name="layer"/> hold the bodies whose groups meet <paramref name="admits"/> and have no other role.</summary>
        private void Admit(BroadPhaseLayers layer, uint admits) =>
            BroadPhase.ConfigureLayer((byte)layer, admits, AllRoles & ~admits);
    }
}
