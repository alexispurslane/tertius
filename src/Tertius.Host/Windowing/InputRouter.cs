using System.Numerics;
using Silk.NET.Input;

namespace Tertius.Host.Windowing;

/// <summary>Where the window's input can go. The window host implements it, and tests record it.</summary>
public interface IInputTarget
{
    /// <summary>Sends an input event to the game's <c>input</c> topic.</summary>
    void Post(InputEvent e);

    /// <summary>Presses or releases a key of the fly camera.</summary>
    void Fly(FlyKey key, bool down);

    /// <summary>Turns the fly camera by a mouse movement in pixels.</summary>
    void Turn(Vector2 movement);

    /// <summary>The fly camera was switched on or off.</summary>
    void FlyChanged(bool flying);

    /// <summary>Closes the window.</summary>
    void Close();

    /// <summary>Makes the scene's next view the camera.</summary>
    void NextView();
}

/// <summary>
/// Decides where each input event of the window goes: to the host, to the fly camera, or to the game's <c>input</c> topic.
/// On the main thread. The rules:
/// <list type="bullet">
/// <item>Esc closes the window, F1 switches the fly camera on and off, and V makes the scene's next view the camera in
/// free play. The game sees none of these keys.</item>
/// <item>While the fly camera is on, its keys and the mouse movement drive it, and nothing reaches the game.</item>
/// <item>Otherwise, in free play, keys, buttons, mouse movements and scrolls reach the game. During a script, nothing
/// does, so a script runs the same with or without a person at the keyboard.</item>
/// <item>The game sees every down that it got released: switching the fly camera on releases each key and button that the
/// game holds, and a release whose down the game never got is dropped.</item>
/// <item>A switch of the fly camera forgets the mouse position, because the window can hide and move the cursor.</item>
/// <item>The window captures the cursor in free play, and during a script while the fly camera is on.</item>
/// </list>
/// </summary>
public sealed class InputRouter(bool freePlay, IInputTarget target)
{
    private readonly MouseTracker mouse = new();
    // The keys and buttons whose down the game got, and not their release yet.
    private readonly HashSet<Key> heldKeys = new();
    private readonly HashSet<MouseButton> heldButtons = new();
    private bool flying;

    /// <summary>
    /// Whether the window hides the cursor and keeps it inside, so that the mouse turns a view: always in free play, where
    /// the game turns its view with the mouse, and during a script only while the fly camera is on.
    /// </summary>
    public bool CapturesCursor => freePlay || flying;

    public void Key(Key key, bool down)
    {
        if (key == Silk.NET.Input.Key.Escape)
        {
            if (down)
                target.Close();
            return;
        }
        if (key == Silk.NET.Input.Key.F1)
        {
            if (down)
                SwitchFlyCamera();
            return;
        }
        if (key == Silk.NET.Input.Key.V)
        {
            // A script chooses its views with camera steps, so the key changes them only in free play.
            if (down && freePlay)
                target.NextView();
            return;
        }
        if (flying)
        {
            if (FlyKeyOf(key) is { } flyKey)
                target.Fly(flyKey, down);
            return;
        }
        if (!freePlay)
            return;
        if (down)
        {
            heldKeys.Add(key);
            Post(InputKind.KeyDown, (int)key, Vector2.Zero);
        }
        else if (heldKeys.Remove(key))
        {
            Post(InputKind.KeyUp, (int)key, Vector2.Zero);
        }
    }

    public void MouseButton(MouseButton button, bool down)
    {
        if (flying || !freePlay)
            return;
        if (down)
        {
            heldButtons.Add(button);
            Post(InputKind.MouseDown, (int)button, Vector2.Zero);
        }
        else if (heldButtons.Remove(button))
        {
            Post(InputKind.MouseUp, (int)button, Vector2.Zero);
        }
    }

    public void MouseMove(Vector2 position)
    {
        Vector2 movement = mouse.MoveTo(position);
        if (flying)
            target.Turn(movement);
        else if (freePlay)
            Post(InputKind.MouseMove, 0, movement);
    }

    public void Scroll(Vector2 amount)
    {
        if (!flying && freePlay)
            Post(InputKind.Scroll, 0, amount);
    }

    private void SwitchFlyCamera()
    {
        flying = !flying;
        mouse.Forget();
        if (flying)
        {
            foreach (Key key in heldKeys)
                Post(InputKind.KeyUp, (int)key, Vector2.Zero);
            foreach (MouseButton button in heldButtons)
                Post(InputKind.MouseUp, (int)button, Vector2.Zero);
            heldKeys.Clear();
            heldButtons.Clear();
        }
        target.FlyChanged(flying);
    }

    private void Post(InputKind kind, int code, Vector2 delta) =>
        target.Post(new InputEvent { Kind = kind, Code = code, Delta = delta });

    private static FlyKey? FlyKeyOf(Key key) => key switch
    {
        Silk.NET.Input.Key.W => FlyKey.Forward,
        Silk.NET.Input.Key.S => FlyKey.Back,
        Silk.NET.Input.Key.A => FlyKey.Left,
        Silk.NET.Input.Key.D => FlyKey.Right,
        Silk.NET.Input.Key.Q => FlyKey.Down,
        Silk.NET.Input.Key.E => FlyKey.Up,
        Silk.NET.Input.Key.ShiftLeft or Silk.NET.Input.Key.ShiftRight => FlyKey.Fast,
        _ => null,
    };
}
