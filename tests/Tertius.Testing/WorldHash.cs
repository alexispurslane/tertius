using System.Numerics;
using Tertius.Simulation;

namespace Tertius.Testing;

/// <summary>
/// Hashes of whole worlds, for the tests that compare two runs. A change of one bit in one value, or of the order of two
/// values, changes the result. <see cref="Of"/> hashes what a world holds. The <c>Mix</c> functions fold single values
/// into a hash, for a test that adds its own state to it.
/// </summary>
public static class WorldHash
{
    /// <summary>
    /// A hash of every live entity's handle, pose, velocities, frame and component state, of the render snapshot that the
    /// last tick published, and of the number of errors. Two worlds that ended in the same state give the same hash.
    /// </summary>
    /// <param name="world">The world to hash, after a tick.</param>
    /// <param name="mixComponent">
    /// Folds the state of one component into the hash: it takes the hash so far and the component, and returns the new
    /// hash. It runs for every component of every live entity, after the entity's own values. Null folds nothing.
    /// </param>
    public static ulong Of(World world, Func<ulong, IComponent, ulong>? mixComponent = null)
    {
        ulong hash = 0;
        var live = new List<Entity>();
        world.CopyLiveEntities(live);
        foreach (Entity e in live)
        {
            hash = Mix(hash, e.Raw);
            hash = Mix(hash, world.PoseInOwnFrame(e));
            hash = Mix(hash, world.VelocityInOwnFrame(e));
            hash = Mix(hash, world.AngularVelocityInOwnFrame(e));
            hash = Mix(hash, (ulong)world.GetFrame(e).Index);
            if (mixComponent is null)
                continue;
            foreach (IComponent component in world.GetComponents(e))
                hash = mixComponent(hash, component);
        }
        if (!world.RenderSnapshots.TryAcquireLatest(out RenderSnapshot snapshot))
            throw new InvalidOperationException("The sync point publishes a render snapshot every tick, so the world has ticked at least once.");
        foreach (RenderInstance instance in snapshot.Instances)
            hash = Mix(Mix(Mix(hash, instance.EntityIndex), instance.Position), instance.PreviousPosition);
        return Mix(hash, (ulong)world.Errors.Count);
    }

    /// <summary>
    /// A hash of the puffs of <paramref name="snapshot"/>: each puff's position, previous position, age and look index, in
    /// the order of the snapshot, the age step, and each field of each look of its look table. <see cref="Of"/> leaves the
    /// puffs out, so that a test can compare a world with puffs and one without through it.
    /// </summary>
    public static ulong OfPuffs(RenderSnapshot snapshot)
    {
        ulong hash = 0;
        foreach (RenderPuff puff in snapshot.Puffs)
            hash = Mix(Mix(Mix(Mix(hash, puff.Position), puff.PreviousPosition), Bits(puff.Age)), (ulong)puff.Look);
        hash = Mix(hash, Bits(snapshot.PuffAgeStep));
        // Each field of each look, because the hash code of a union mixes with a seed that changes from process to process.
        foreach (PuffLook look in snapshot.PuffLooks)
        {
            hash = Mix(Mix(Mix(Mix(hash, Bits(look.Life)), Bits(look.StartRadius)), Bits(look.EndRadius)), Bits(look.TemperatureRatio));
            if (look.Shading.TryGetValue(out GlowPuffs glow))
                hash = Mix(Mix(hash, 1UL), glow.Radiance);
            else if (look.Shading.TryGetValue(out LitPuffs lit))
                hash = Mix(Mix(hash, 2UL), lit.Albedo);
            else
                hash = Mix(hash, 0UL);
        }
        return hash;
    }

    /// <summary>The bits of a float, so that a hash tells apart values that differ in their last bit.</summary>
    public static ulong Bits(float value) => BitConverter.SingleToUInt32Bits(value);

    public static ulong Bits(double value) => (ulong)BitConverter.DoubleToInt64Bits(value);

    public static ulong Mix(ulong hash, ulong value)
    {
        hash ^= value + 0x9E3779B97F4A7C15UL + (hash << 6) + (hash >> 2);
        return hash * 0xBF58476D1CE4E5B9UL;
    }

    public static ulong Mix(ulong hash, double value) => Mix(hash, Bits(value));

    public static ulong Mix(ulong hash, in DPose pose)
    {
        hash = Mix(hash, pose.Position.X);
        hash = Mix(hash, pose.Position.Y);
        hash = Mix(hash, pose.Position.Z);
        hash = Mix(hash, pose.Rotation.X);
        hash = Mix(hash, pose.Rotation.Y);
        hash = Mix(hash, pose.Rotation.Z);
        return Mix(hash, pose.Rotation.W);
    }

    public static ulong Mix(ulong hash, Vector3 v) => Mix(Mix(Mix(hash, v.X), v.Y), v.Z);

    public static ulong MixPayload(ulong hash, in Event e)
    {
        ReadOnlySpan<ulong> words = e.Payload;
        foreach (ulong word in words)
            hash = Mix(hash, word);
        return hash;
    }
}
