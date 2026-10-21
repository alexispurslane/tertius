using System.Numerics;
using Tertius.Simulation;

namespace Tertius.Demo;

/// <summary>
/// Turns a person to face the way that it walks: it turns the person's view about the person's up, +Y in its axes,
/// toward the direction of the person's velocity across that up, by at most <see cref="TurnRate"/>, while physics stands the
/// person on walkable ground and that velocity is faster than <see cref="TurnSpeed"/>. At any other time the heading
/// holds: while the person stands still, creeps, floats, slides or sits. The heading is the view's -Z, so a body or a held
/// thing that follows the view faces the same way (see <see cref="Gait"/> and <see cref="HeldInView"/>). Its entity needs
/// a character body and a view offset.
/// </summary>
/// <remarks>
/// <para>Only a teleport turns a character (see <see cref="PersonControl"/>), so the turn lives in the view offset, as a
/// player's turn does. The view is a turn about the person's +Y and nothing else: the component writes a view with no
/// tilt. It keeps the view's eye where it is, so a camera that follows the eye does not move when the person turns.</para>
/// <para>The view is the only store of the heading: the component reads the view of the last tick and writes it turned,
/// and writes nothing while the heading holds. It reads the velocity and the standing of the last finished step, so it
/// turns the view toward the walk of two steps before, as every component that reads physics does.</para>
/// <para>A floating person does not turn by itself: nothing that it can push on turns it. So the heading holds in the air,
/// and the fetch of the pen in orbit, which places the hand in the view, sees the heading that the person had at its
/// seat.</para>
/// </remarks>
public sealed class WalkHeading : IComponent
{
    /// <summary>The fastest turn of the heading, in radians per second: a quarter turn in 0.39 s.</summary>
    public const double TurnRate = 4;

    /// <summary>
    /// The speed of the person across its up, in m/s, at or below which the heading holds: below the station's step into
    /// the chute, 0.4 m/s, and above the drift of a belted person that the cabin shakes.
    /// </summary>
    public const double TurnSpeed = 0.25;

    public int Priority => 0;

    public EventMask Wants => EventMask.Of<Tick>();

    public void Handle(Self self, scoped ref EventList events, scoped ref Outbox outbox)
    {
        if (!self.Body.Standing)
            return;
        // The velocity in the person's own axes, and its part across the person's up.
        Double3 local = self.PoseIn(self.Frame).Rotation.Conjugate().Rotate(self.VelocityIn(self.Frame));
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
