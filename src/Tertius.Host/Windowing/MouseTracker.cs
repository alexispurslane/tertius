using System.Numerics;

namespace Tertius.Host.Windowing;

/// <summary>Turns the mouse positions that the window reports into movements, as an <see cref="InputEvent"/> carries them.</summary>
public sealed class MouseTracker
{
    private Vector2? last;

    /// <summary>The movement in pixels since the last position, or zero for the first one.</summary>
    public Vector2 MoveTo(Vector2 position)
    {
        Vector2 movement = last is { } previous ? position - previous : Vector2.Zero;
        last = position;
        return movement;
    }

    /// <summary>Forgets the last position, so that the next one moves nothing. Call it when the cursor mode changes.</summary>
    public void Forget() => last = null;
}
