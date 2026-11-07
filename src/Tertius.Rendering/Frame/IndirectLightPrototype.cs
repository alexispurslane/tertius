namespace Tertius.Rendering;

internal readonly record struct IndirectLightPrototype(bool SingleDiffuseHistory, bool LimitIncoming)
{
    public static IndirectLightPrototype Select(bool enabled, bool disableNeighborCap, SignalLight light)
    {
        return new(enabled && light == SignalLight.Diffuse, enabled && !disableNeighborCap);
    }
}
