using JoltPhysicsSharp;

namespace Tertius.Physics;

/// <summary>Process-wide Jolt state: one-time initialization, and the gate that serializes native work.</summary>
internal static class JoltRuntime
{
    private static readonly Lock InitGate = new();
    private static bool initialized;

    /// <summary>
    /// joltc has one process-wide temp allocator, a stack allocator that is not thread-safe. Every call that can use
    /// it (system updates and character updates) runs under this gate, so two backends in one process never step
    /// at the same time. In the engine there is one backend, so the gate is never contended.
    /// </summary>
    public static readonly Lock NativeGate = new();

    /// <summary>Initializes Jolt in double precision, which loads <c>libjoltc_double</c>. Safe to call many times.</summary>
    public static void EnsureInitialized()
    {
        lock (InitGate)
        {
            if (initialized)
                return;
            if (!Foundation.Init(doublePrecision: true))
                throw new InvalidOperationException("Jolt failed to initialize. Check that libjoltc_double is next to the executable.");
            initialized = true;
        }
    }
}
