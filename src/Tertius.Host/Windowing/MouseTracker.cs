using System.Numerics;

namespace Tertius.Host.Windowing;

/// <summary>Turns the mouse positions that the window reports into movements, as an <see cref="InputEvent"/> carries them.</summary>
public sealed class MouseTracker
{
    /// <summary>The movement in pixels since the last position, or zero for the first one.</summary>
    public Vector2 MoveTo(Vector2 position)
    {
        Vector2 movement = last is { } previous ? position - previous : Vector2.Zero;
        last = position;
        return movement;
    }

    private Vector2? last;
}
