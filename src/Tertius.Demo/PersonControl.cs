using System.Diagnostics;
using System.Numerics;
using Silk.NET.Input;
using Tertius.Simulation;

namespace Tertius.Demo;

/// <summary>
/// Lets the keyboard and the mouse on the <c>input</c> topic steer a person and turn its view, while the person's view
/// is on. While physics stands the person on walkable ground, W, A, S and D walk forward, left, back and right, and
/// Space jumps. At any other time the person is weightless, in any frame and any gravity, and its thrusters push it
/// instead: W, A, S and D along the view, Space up and C down along the person's own up. The thrusters are a
/// <see cref="BackJet"/>, a part of the person that the component spawns in its first tick and sends the force of each
/// tick by message. So a jump is a short use of the
/// thrusters too: in the air W, A, S and D thrust along the view, and Space held after the jump thrusts the person up.
/// The mouse turns the view, and with it the direction of the walk and of the thrust. E uses every entity within
/// <see cref="Reach"/>. Its entity needs a character body and a subscription to <see cref="Topics.Input"/>. The component
/// sets the entity's view offset, so the entity is a first-person camera. While another entity is the world's camera,
/// the person ignores input and lets go of the keys that it held.
/// </summary>
/// <remarks>
/// <para>Only a teleport turns a character, and a teleport skips the sweep that keeps the character out of walls. So the
/// body does not turn: the turn lives in this component and in the view offset. The walk goes along the entity's own
/// axes, turned by the turn of the view about the entity's +Y. Under gravity, physics turns that +Y against the semantic
/// gravity at the person: at once while the person touches anything, and in the air at most at the backend's air rate. So
/// the walk stays along the floor, and looking up or down does not tip it.</para>
/// <para>A character that physics does not stand on walkable ground counts as weightless: in the air, on steep ground,
/// where no gravity acts, or in free fall round a planet. (User) Physics keeps its momentum, gravity and forces change
/// it, and a walk or a jump does not move it. Under gravity its up turns against the gravity, in the air too, and where
/// the gravity is weaker than the zero-gravity threshold it keeps its last up. There the person drifts or falls until a
/// thrust changes its velocity. The person reads
/// <see cref="BodyHandle.Standing"/>, which physics reports for the step of the previous tick. So its thrusters start
/// and stop a tick late, and the back jet pushes one tick after the tick that sends it the force. The walk and the jump do not wait for the report: the person asks for the walk on every tick
/// and for a jump on each press, and physics applies them only in the steps in which it stands the person.</para>
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
    private readonly List<Entity> nearby = [];

    /// <summary>The person's back jet, which the component spawns in its first tick. Null before it.</summary>
    public Entity BackJetPart { get; private set; }

    public int Priority => 0;

    // Tick runs it on every tick, so the walk follows the entity's axes when physics turns them to a new up.
    public EventMask Wants => EventMask.Of<InputEvent>().And<Tick>();

    public void Handle(Self self, scoped ref EventList events, scoped ref Outbox outbox)
    {
        if (BackJetPart.IsNull)
            BackJetPart = BackJet.Spawn(self);
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

        var body = self.PoseIn(self.Frame).Rotation.ToQuaternion();
        var turn = Quaternion.CreateFromAxisAngle(Vector3.UnitY, yaw);
        Quaternion view = turn * Quaternion.CreateFromAxisAngle(Vector3.UnitX, pitch);
        self.Render.ViewOffset = new(eye, view);
        var keys = new Vector3((right ? 1 : 0) - (left ? 1 : 0), (rise ? 1 : 0) - (sink ? 1 : 0),
            (back ? 1 : 0) - (forward ? 1 : 0));
        // The person asks for the walk of the keys held now and for a jump on a press, whether it stands or not.
        // Physics ignores both while it does not stand the person. So a held walk goes on in the first step in which
        // the person stands again, a key let go of in the air leaves no walk behind, and a press just before a landing
        // still jumps.
        Vector3 walk = keys with { Y = 0 };
        if (walk != Vector3.Zero)
            walk = Vector3.Normalize(walk) * WalkSpeed;
        self.Body.MoveCharacter(Vector3.Transform(walk, body * turn));
        if (jump)
            self.Body.Jump(JumpSpeed);
        jump = false;
        if (!self.Body.Standing)
        {
            Vector3 thrust = Vector3.Transform(keys with { Y = 0 }, view) + new Vector3(0, keys.Y, 0);
            // Two keys together thrust no harder than one. Keys that nearly cancel each other thrust only a little.
            if (thrust.LengthSquared() > 1)
                thrust = Vector3.Normalize(thrust);
            // The thrust lies in the person's own axes, which its back jet keeps.
            if (thrust != Vector3.Zero)
                outbox.Send(BackJetPart, new JetForce { Force = thrust * ThrustForce });
        }

        if (use)
        {
            self.Snapshot.QueryRadius(self.Frame, self.PoseIn(self.Frame).Position, Reach, nearby);
            foreach (Entity other in nearby)
                if (other != self.Entity)
                    outbox.Send(other, new Use { User = self.Entity });
            use = false;
        }
    }

    private void Read(in InputEvent input)
    {
        // The person reads the keys and the mouse movement. A mouse button, a scroll and the empty event mean nothing to it.
        switch (input)
        {
            case KeyChanged(var code, var isDown):
                Hold((Key)code, isDown);
                break;
            case MouseMoved(var delta):
                yaw = MathF.IEEERemainder(yaw - delta.X * TurnPerPixel, 2 * MathF.PI);
                pitch = Math.Clamp(pitch - delta.Y * TurnPerPixel, -MaxPitch, MaxPitch);
                break;
            case MouseButtonChanged or Scrolled or null:
                break;
            default:
                throw new UnreachableException($"The input {input} has a case that the person does not read.");
        }
    }

    private void Hold(Key key, bool down)
    {
        // Key is the whole keyboard of Silk.NET, an open set for this switch: most keys mean nothing to a person.
#pragma warning disable IDE0010
        switch (key)
#pragma warning restore IDE0010
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
            default:
                // Every other key does nothing.
                break;
        }
    }
}
