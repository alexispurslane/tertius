using JoltPhysicsSharp;

namespace Tertius.Physics;

/// <summary>Process-wide Jolt state: one-time initialization.</summary>
/// <remarks>
/// The native library holds no other mutable state for the whole process that a step uses. joltc's temp allocator, a
/// stack allocator that is not thread-safe, is one per calling thread in the patched build (native/joltc.nix), so two
/// backends can step at the same time on different threads. Each backend serializes its own native work with its
/// <see cref="JoltPhysicsBackend.Gate"/>.
/// </remarks>
internal static class JoltRuntime
{
    private static readonly Lock InitGate = new();
    private static bool initialized;

    /// <summary>
    /// Initializes Jolt in double precision, which loads <c>libjoltc_double</c>, and makes joltc call the backend's own
    /// contact callbacks (see <see cref="ContactListener"/>). Safe to call many times.
    /// </summary>
    public static void EnsureInitialized()
    {
        lock (InitGate)
        {
            if (initialized)
                return;
            if (!Foundation.Init(doublePrecision: true))
                throw new InvalidOperationException(
                    "Jolt failed to initialize. Check that libjoltc_double is next to the executable."
                );
            ContactListener.InstallCallbacks();
            initialized = true;
        }
    }
}
