using System.Numerics;
using Silk.NET.Input;
using Tertius.Simulation;

namespace Tertius.Demo;

/// <summary>
/// Lets the keyboard and the mouse on the <c>input</c> topic steer a person and turn its view, while the person's view
/// is on. Under gravity, W, A, S and D walk forward, left, back and right, and Space jumps. Where the person is
/// weightless, thrusters push it instead: W, A, S and D along the view, Space up and C down along the person's own up.
/// The mouse turns the view, and with it the direction of the walk and of the thrust. E uses every entity within
/// <see cref="Reach"/>. Its entity needs a character body and a subscription to <see cref="Topics.Input"/>. The component
/// sets the entity's view offset, so the entity is a first-person camera. While another entity is the world's camera,
/// the person ignores input and lets go of the keys that it held.
/// </summary>
/// <remarks>
/// <para>Only a teleport turns a character, and a teleport skips the sweep that keeps the character out of walls. So the
/// body does not turn: the turn lives in this component and in the view offset. The walk goes along the entity's own
/// axes, turned by the turn of the view about the entity's +Y. Physics keeps that +Y against the effective gravity at the
/// person, so the walk stays along the floor, and looking up or down does not tip it.</para>
/// <para>Below <see cref="FrameTree.ZeroGravityThreshold"/>, physics keeps a character's momentum and ignores its walk
/// and its jump, and the character keeps its last up. There the person drifts until a thrust changes its velocity. The
/// person reads the same threshold at its position at the end of the last tick, so it switches to thrust within a tick
/// of physics.</para>
/// </remarks>
/// <param name="eye">The eye's position relative to the entity, in meters, in the entity's axes.</param>
public sealed class PersonControl(Vector3 eye) : IComponent
{
    /// <summary>The walking speed, in meters per second.</summary>
    public const float WalkSpeed = 3;

    /// <summary>The upward speed of a jump, in meters per second.</summary>
    public const float JumpSpeed = 4;

    /// <summary>The force of the thrusters, in newtons: 1.5 m/s² on a person of 70 kg.</summary>
    public const float ThrustForce = 105;

    /// <summary>How far from the person's position an entity can be for E to use it, in meters.</summary>
    public const double Reach = 3;

    /// <summary>Radians of turn per pixel of mouse movement.</summary>
    public const float TurnPerPixel = 0.0025f;

    // The view never reaches straight up or down, where the axis of the turn and the view would line up.
    private const float MaxPitch = MathF.PI / 2 - 0.01f;

    // The keys held down. Space is both rise, while held, and jump, once per press.
    private bool forward, back, left, right, rise, sink;
    // A press of Space or of E that the component has not acted on yet.
    private bool jump, use;
    // The turn of the view about the entity's +Y, and its tilt about the turned +X, in radians. Mouse right turns right,
    // and mouse down looks down.
    private float yaw, pitch;
    private readonly List<Entity> nearby = new();

    public int Priority => 0;

    // Tick runs it on every tick, so the walk follows the entity's axes when physics turns them to a new up.
    public EventMask Wants => EventMask.Of<InputEvent>().And<Tick>();

    public void Handle(Self self, scoped ref EventList events, scoped ref Outbox outbox)
    {
        if (self.Snapshot.Camera == self.Entity)
        {
            for (int i = 0; i < events.Count; i++)
                if (events[i].Is<InputEvent>())
                    Read(events[i].As<InputEvent>());
        }
        else
        {
            forward = back = left = right = rise = sink = jump = use = false;
        }

        Quaternion body = self.Pose.Rotation.ToQuaternion();
        Quaternion turn = Quaternion.CreateFromAxisAngle(Vector3.UnitY, yaw);
        Quaternion view = turn * Quaternion.CreateFromAxisAngle(Vector3.UnitX, pitch);
        self.Render.ViewOffset = new Pose(eye, view);
        var keys = new Vector3((right ? 1 : 0) - (left ? 1 : 0), (rise ? 1 : 0) - (sink ? 1 : 0), (back ? 1 : 0) - (forward ? 1 : 0));
        if (Weightless(self))
        {
            // No walk is left over for the moment that the person lands.
            self.Body.MoveCharacter(Vector3.Zero);
            Vector3 thrust = Vector3.Transform(keys with { Y = 0 }, view) + new Vector3(0, keys.Y, 0);
            // Two keys together thrust no harder than one. Keys that nearly cancel each other thrust only a little.
            if (thrust.LengthSquared() > 1)
                thrust = Vector3.Normalize(thrust);
            if (thrust != Vector3.Zero)
                self.Body.AddForce(Vector3.Transform(thrust, body) * ThrustForce);
        }
        else
        {
            Vector3 walk = keys with { Y = 0 };
            if (walk != Vector3.Zero)
                walk = Vector3.Normalize(walk) * WalkSpeed;
            self.Body.MoveCharacter(Vector3.Transform(walk, body * turn));
            // Physics drops a jump that starts in the air.
            if (jump)
                self.Body.Jump(JumpSpeed);
        }
        jump = false;

        if (use)
        {
            self.Snapshot.QueryRadius(self.Frame, self.Pose.Position, Reach, nearby);
            foreach (Entity other in nearby)
                if (other != self.Entity)
                    outbox.Send(other, new Use { User = self.Entity });
            use = false;
        }
    }

    /// <summary>True where physics moves the person as weightless, at its position at the end of the last tick.</summary>
    private static bool Weightless(Self self) =>
        self.Snapshot.Frames.EffectiveGravity(self.Frame, self.Pose.Position).Length() < FrameTree.ZeroGravityThreshold;

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
                rise = down;
                jump |= down;
                break;
            case Key.C:
                sink = down;
                break;
            case Key.E:
                use |= down;
                break;
        }
    }
}
