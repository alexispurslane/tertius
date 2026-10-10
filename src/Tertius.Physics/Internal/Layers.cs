using JoltPhysicsSharp;

namespace Tertius.Physics;

/// <summary>
/// Object layers and broad-phase layers. Each <see cref="BodyLayer"/> is one Jolt object layer and one broad-phase
/// layer with the same number. The collision table is the one in ARCHITECTURE.md.
/// </summary>
internal static class Layers
{
    public const uint Count = 4;

    public static readonly ObjectLayer Static = (uint)BodyLayer.Static;
    public static readonly ObjectLayer Moving = (uint)BodyLayer.Moving;
    public static readonly ObjectLayer FrameHull = (uint)BodyLayer.FrameHull;
    public static readonly ObjectLayer Ghost = (uint)BodyLayer.Ghost;

    /// <summary>The layer pairs that collide. Every other pair does not.</summary>
    private static readonly (BodyLayer A, BodyLayer B)[] Pairs =
    [
        (BodyLayer.Static, BodyLayer.Moving),
        (BodyLayer.Static, BodyLayer.FrameHull),
        (BodyLayer.Moving, BodyLayer.Moving),
        (BodyLayer.Moving, BodyLayer.Ghost),
        (BodyLayer.FrameHull, BodyLayer.FrameHull),
    ];

    public static bool Collide(BodyLayer a, BodyLayer b)
    {
        foreach ((BodyLayer x, BodyLayer y) in Pairs)
            if ((x == a && y == b) || (x == b && y == a))
                return true;
        return false;
    }

    /// <summary>The Jolt filter tables. Every physics system of a backend shares one set.</summary>
    public sealed class Tables : IDisposable
    {
        public Tables()
        {
            Pairs = new ObjectLayerPairFilterTable(Count);
            foreach ((BodyLayer a, BodyLayer b) in Layers.Pairs)
                Pairs.EnableCollision((uint)a, (uint)b);

            BroadPhase = new BroadPhaseLayerInterfaceTable(Count, Count);
            for (uint layer = 0; layer < Count; layer++)
                BroadPhase.MapObjectToBroadPhaseLayer(layer, (byte)layer);

            ObjectVsBroadPhase = new ObjectVsBroadPhaseLayerFilterTable(BroadPhase, Count, Pairs, Count);
        }

        public ObjectLayerPairFilterTable Pairs { get; }
        public BroadPhaseLayerInterfaceTable BroadPhase { get; }
        public ObjectVsBroadPhaseLayerFilterTable ObjectVsBroadPhase { get; }

        public void Dispose()
        {
            ObjectVsBroadPhase.Dispose();
            BroadPhase.Dispose();
            Pairs.Dispose();
        }
    }
}
