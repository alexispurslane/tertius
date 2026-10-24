using System.Numerics;
using Tertius.Simulation;

namespace Tertius.Demo;

/// <summary>
/// Draws a person's body as it walks: an attached entity that faces the way of the person's view, turned about the
/// person's up, and rises and falls a little with each step while physics stands the person on walkable ground. Its
/// entity is attached to the person, and the component writes only its own offset from the person, in every tick:
/// <list type="bullet">
/// <item>The turn: the turn of the person's view about the person's up, +Y in the person's axes, without the view's tilt.
/// So the body stays upright, and it faces the view's -Z. <see cref="WalkHeading"/> turns that view toward the walk.</item>
/// <item>The place: <paramref name="center"/>, turned with the body, plus the lift along the person's up. The lift is
/// <see cref="StepRise"/> times the square of the sine of π times the distance walked over <see cref="StepLength"/>,
/// times the bob's weight. So it is zero where a foot comes down, once for each step length, and its phase follows the
/// distance walked, not the time.</item>
/// <item>The distance walked: the person's speed across its up times the tick, added up while physics stands the person.
/// The speed is the person's velocity in the body's frame, which is the person's own frame.</item>
/// <item>The bob's weight: from 0 to 1. While physics stands the person, it eases toward the person's speed over
/// <see cref="FullRiseSpeed"/>, at most 1, and otherwise toward 0, with the time constant <see cref="RiseTime"/>. So a slow
/// walk bobs less, and the bob dies away when the person stops, leaves the floor, floats or slides, wherever in its step
/// that happens.</item>
/// </list>
/// </summary>
/// <remarks>
/// <para>A character does not turn, so a drawn body that faces its way cannot be the person's own render row. The person's
/// row draws nothing, and this attached entity carries the look. It has no body, so the bob moves nothing in physics, and
/// the cameras, which follow the person's eye, do not bob.</para>
/// <para>The component reads the person's view, pose, velocity and standing from the snapshot of the previous tick, as
/// <see cref="HeldInView"/> reads the view. So the body turns one tick after the view, together with a thing held in the
/// view, and the attachment places it at the person's pose at the end of the step in flight.</para>
/// </remarks>
/// <param name="center">Where the body's center rests relative to the person's center, in meters, in the body's axes.</param>
public sealed class Gait(Vector3 center) : IComponent
{
    /// <summary>The distance walked in one step, in meters: one rise and fall of the body.</summary>
    public const double StepLength = 0.75;

    /// <summary>How high the body rises in a step at a full bob, in meters.</summary>
    public const double StepRise = 0.035;

    /// <summary>The speed across the person's up, in m/s, from which the body bobs fully: the station's walk.</summary>
    public const double FullRiseSpeed = 1;

    /// <summary>The time constant with which the bob's weight eases toward the person's walk, in seconds.</summary>
    public const double RiseTime = 0.1;

    private readonly Vector3 center = center;

    // The distance walked since the last step length, in meters, and the bob's weight, from 0 to 1.
    private double walked,
        weight;

    public int Priority => 0;

    public EventMask Wants => EventMask.Of<Tick>();

    public void Handle(Self self, scoped ref EventList events, scoped ref Outbox outbox)
    {
        Entity person = self.AttachedTo;
        Snapshot snapshot = self.Snapshot;
        if (person.IsNull || snapshot.PoseIn(person, self.Frame) is not { } pose)
            return;
        double dt = self.DeltaTime,
            target = 0;
        if (snapshot.IsStanding(person) && snapshot.VelocityIn(person, self.Frame) is { } velocity)
        {
            Double3 local = pose.Rotation.Conjugate().Rotate(velocity);
            double speed = Math.Sqrt(local.X * local.X + local.Z * local.Z);
            walked = (walked + speed * dt) % StepLength;
            target = Math.Min(1, speed / FullRiseSpeed);
        }

        weight += (target - weight) * (1 - Math.Exp(-dt / RiseTime));
        double sine = Math.Sin(Math.PI * walked / StepLength);
        var turn = Quaternion.CreateFromAxisAngle(
            Vector3.UnitY,
            (float)WalkHeading.Yaw(snapshot.RenderOf(person).ViewOffset.Rotation)
        );
        self.AttachOffset = new(
            Vector3.Transform(center, turn) + new Vector3(0, (float)(StepRise * weight * sine * sine), 0),
            turn
        );
    }
}
