using System.Numerics;
using Silk.NET.Input;
using Tertius.Simulation;

namespace Tertius.Demo;

/// <summary>
/// Lets the keyboard and the mouse on the <c>input</c> topic walk a person and turn its view: W, A, S and D walk forward,
/// left, back and right, Space jumps, and the mouse turns the view, and with it the direction of the walk. Its entity
/// needs a character body and a subscription to <see cref="Topics.Input"/>. The component sets the entity's view offset,
/// so the entity is a first-person camera.
/// </summary>
/// <remarks>
/// No physics intent turns a character, so the body does not turn: the turn lives in this component and in the view
/// offset. The walk goes along the entity's own axes, turned by the turn of the view about the entity's +Y. Physics keeps
/// that +Y along the frame's up, so the walk stays on the floor in any gravity, and looking up or down does not tip it.
/// </remarks>
/// <param name="eye">The eye's position relative to the entity, in meters, in the entity's axes.</param>
public sealed class PersonControl(Vector3 eye) : IComponent
{
    /// <summary>The walking speed, in meters per second.</summary>
    public const float WalkSpeed = 3;

    /// <summary>The upward speed of a jump, in meters per second.</summary>
    public const float JumpSpeed = 4;

    /// <summary>Radians of turn per pixel of mouse movement.</summary>
    public const float TurnPerPixel = 0.0025f;

    // The view never reaches straight up or down, where the axis of the turn and the view would line up.
    private const float MaxPitch = MathF.PI / 2 - 0.01f;

    private bool forward, back, left, right, jump;
    // The turn of the view about the entity's +Y, and its tilt about the turned +X, in radians. Mouse right turns right,
    // and mouse down looks down.
    private float yaw, pitch;

    public int Priority => 0;

    // Tick runs it on every tick, so the walk follows the entity's axes when physics turns them to a new up.
    public EventMask Wants => EventMask.Of<InputEvent>().And<Tick>();

    public void Handle(Self self, scoped ref EventList events, scoped ref Outbox outbox)
    {
        for (int i = 0; i < events.Count; i++)
            if (events[i].Is<InputEvent>())
                Read(events[i].As<InputEvent>());

        Quaternion turn = Quaternion.CreateFromAxisAngle(Vector3.UnitY, yaw);
        self.Render.ViewOffset = new Pose(eye, turn * Quaternion.CreateFromAxisAngle(Vector3.UnitX, pitch));
        var walk = new Vector3((right ? 1 : 0) - (left ? 1 : 0), 0, (back ? 1 : 0) - (forward ? 1 : 0));
        if (walk != Vector3.Zero)
            walk = Vector3.Normalize(walk) * WalkSpeed;
        self.Body.MoveCharacter(Vector3.Transform(walk, self.Pose.Rotation.ToQuaternion() * turn));
        if (jump)
        {
            // Physics drops a jump that starts in the air.
            self.Body.Jump(JumpSpeed);
            jump = false;
        }
    }

    private void Read(in InputEvent input)
    {
        switch (input.Kind)
        {
            case InputKind.KeyDown or InputKind.KeyUp:
                Hold((Key)input.Code, input.Kind == InputKind.KeyDown);
                break;
            case InputKind.MouseMove:
                yaw = MathF.IEEERemainder(yaw - input.Delta.X * TurnPerPixel, 2 * MathF.PI);
                pitch = Math.Clamp(pitch - input.Delta.Y * TurnPerPixel, -MaxPitch, MaxPitch);
                break;
        }
    }

    private void Hold(Key key, bool down)
    {
        switch (key)
        {
            case Key.W:
                forward = down;
                break;
            case Key.S:
                back = down;
                break;
            case Key.A:
                left = down;
                break;
            case Key.D:
                right = down;
                break;
            case Key.Space:
                jump |= down;
                break;
        }
    }
}
