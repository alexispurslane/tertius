using System.Numerics;
using Tertius.Simulation;

namespace Tertius.Demo;

/// <summary>
/// Turns a person to face the way that it walks: it turns the person's view about the person's up, +Y in its axes,
/// toward the direction of the walk that the person asks for in this tick, across that up, by at most
/// <see cref="TurnRate"/>, while physics stands the person on walkable ground and that walk is faster than
/// <see cref="TurnSpeed"/>. At any other time the heading holds: while the person asks for no walk or a slow one, and
/// while it floats, slides or sits, also when a push or a jolt moves it. The heading is the view's -Z, so a body or a
/// held thing that follows the view faces the same way (see <see cref="Gait"/> and <see cref="HeldInView"/>). Its entity
/// needs a character body and a view offset.
/// </summary>
/// <remarks>
/// <para>Only a teleport turns a character (see <see cref="PersonControl"/>), so the turn lives in the view offset, as a
/// player's turn does. The view is a turn about the person's +Y and nothing else: the component writes a view with no
/// tilt. It keeps the view's eye where it is, so a camera that follows the eye does not move when the person turns.</para>
/// <para>The view is the only store of the heading: the component reads the view of the last tick and writes it turned,
/// and writes nothing while the heading holds.</para>
/// <para>The heading follows the walk that the person asks for, the character walk of its pending intent, and not its
/// velocity. A belted passenger whom the cabin's jolt pushes across the floor at 0.4 m/s asks for no walk, so it does not
/// turn. The person's walkers, <see cref="Walker"/> and <see cref="Disembark"/>, ask for the walk in every tick of a walk,
/// and this component runs after them, at <see cref="Priority"/> 10. The standing comes from the last finished step.</para>
/// <para>A floating person does not turn by itself: nothing that it can push on turns it. So the heading holds in the air,
/// and the fetch of the pen in orbit, which places the hand in the view, sees the heading that the person had at its
/// seat.</para>
/// </remarks>
public sealed class WalkHeading : IComponent
{
    /// <summary>The fastest turn of the heading, in radians per second: a quarter turn in 0.39 s.</summary>
    public const double TurnRate = 4;

    /// <summary>
    /// The speed of the asked walk across the person's up, in m/s, at or below which the heading holds: below the station's
    /// step into the chute, 0.4 m/s, so the person turns into the chute's top. A walker's slowdown asks for less within
    /// 8 cm of its last waypoint, where the direction to it swings as the person settles, so the heading holds there.
    /// </summary>
    public const double TurnSpeed = 0.25;

    /// <summary>After the components that ask for the person's walk, which run at 0.</summary>
    public int Priority => 10;

    public EventMask Wants => EventMask.Of<Tick>();

    public void Handle(Self self, scoped ref EventList events, scoped ref Outbox outbox)
    {
        if (!self.Body.Standing)
            return;
        // The walk that the person asks for in this tick, in the axes of its frame, turned into the person's own axes, and
        // its part across the person's up. It is zero when nothing asks for a walk in this tick.
        Double3 local = self.PoseIn(self.Frame).Rotation.Conjugate().Rotate(self.Body.Pending.CharacterVelocity);
        var across = new Double3(local.X, 0, local.Z);
        if (across.Length <= TurnSpeed)
            return;
        Pose view = self.Snapshot.RenderOf(self.Entity).ViewOffset;
        double yaw = Yaw(view.Rotation);
        double error = Math.IEEERemainder(Math.Atan2(-across.X, -across.Z) - yaw, 2 * Math.PI);
        double step = TurnRate * self.DeltaTime;
        self.Render.ViewOffset = new Pose(view.Position, Quaternion.CreateFromAxisAngle(Vector3.UnitY, (float)(yaw + Math.Clamp(error, -step, step))));
    }

    /// <summary>
    /// The turn of <paramref name="rotation"/> about +Y, in radians, counterclockwise seen from above: the angle from -Z to
    /// the part of its -Z across +Y.
    /// </summary>
    public static double Yaw(Quaternion rotation)
    {
        Vector3 facing = Vector3.Transform(-Vector3.UnitZ, rotation);
        return Math.Atan2(-facing.X, -facing.Z);
    }
}
