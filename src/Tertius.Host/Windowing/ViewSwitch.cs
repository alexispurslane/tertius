using Tertius.Simulation;

namespace Tertius.Host.Windowing;

/// <summary>
/// Presses of the view key in free play. The main thread counts them, and the simulation thread applies them between
/// ticks, because only the ticking thread may set the world's camera.
/// </summary>
public sealed class ViewSwitch(SceneInstance scene)
{
    private int presses;

    /// <summary>Counts one press of the view key. Safe on any thread.</summary>
    public void Press() => Interlocked.Increment(ref presses);

    /// <summary>
    /// Moves <paramref name="world"/>'s camera one view on (see <see cref="SceneInstance.NextView"/>) for every press since
    /// the last call. Call it on the ticking thread, between ticks.
    /// </summary>
    public void Apply(World world)
    {
        for (int changes = Interlocked.Exchange(ref presses, 0); changes > 0; changes--)
            world.Camera = scene.NextView(world.Camera);
    }
}
