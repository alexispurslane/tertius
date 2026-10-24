using System.Numerics;
using Tertius.Simulation;

namespace Tertius.Demo;

/// <summary>
/// The force that a person's <see cref="BackJet"/> gives for one tick, which the person's <see cref="PersonControl"/> or
/// <see cref="Passenger"/> sends to the jet as a message.
/// </summary>
public struct JetForce : IEventPayload
{
    /// <summary>The force, in newtons, in the person's own axes, which the jet's part keeps.</summary>
    public Vector3 Force;
}

/// <summary>
/// A person's back jet, on its part of the person (docs/plans/parts.md, "The demo's use"): in each tick that brings a
/// <see cref="JetForce"/>, it pushes with its force, turned from the person's axes into those of its frame, and blows its
/// cold gas, <see cref="Plume"/>, at the throttle that the force's size is of <see cref="PersonControl.ThrustForce"/>. It
/// turns its own offset so that its puffs leave against the force. The engine carries the push to the person's body. A
/// character reads no torque, so the jet's place off the person's center of mass turns nothing. A tick without a force, or
/// with a force of zero, gives no push and no puffs, so the jet keeps nothing from an earlier tick. When a tick brings
/// several, the last one counts. A force that is not finite makes the jet throw <see cref="ArgumentOutOfRangeException"/>,
/// which the world logs as an error, and push nothing and blow nothing in that tick.
/// </summary>
/// <remarks>
/// The force comes in the person's own axes, not in those of its frame, so a force that the person sends in the tick before
/// a hand-off still points the same way in the person after it. The jet reads the person's rotation at the end of the last
/// finished step (see <see cref="Plume.PushedBodyRotation"/>), and not its own, which still holds the offset of an earlier
/// force. Its push acts in the physics step of the next tick, so the person's thrust acts one tick later than a push of its
/// own body. The offset and the emitter that it writes in the tick take effect at the tick's sync point.
/// </remarks>
public sealed class BackJet : IComponent
{
    /// <summary>
    /// The back jet's exhaust: cold gas, at three quarters of the air's temperature, lit pale gray, in small puffs that live
    /// 1.5 s. At full thrust 30 puffs leave each second at 3 m/s, so that in the cabin's air they stop within about 2 m of
    /// the person (docs/plans/blue-danube-demo.md, "The plumes").
    /// </summary>
    public static Plume Plume { get; } = new(
        Look: new(Life: 1.5f, StartRadius: 0.04f, EndRadius: 0.3f, TemperatureRatio: 0.75f,
            Shading: PuffShading.Lit(new(0.85f, 0.87f, 0.9f))),
        FullRate: 30, Speed: 3, Spread: 0.2f);

    /// <summary>Where the jet sits on the person, in meters, in the person's axes: on its back, at its chest's height.</summary>
    public static Vector3 Place { get; } = new(0, 0.3f, 0.3f);

    public int Priority => 10;

    // Tick brings the jet into every tick, so that a tick without a force clears its emitter.
    public EventMask Wants => EventMask.Of<JetForce>().And<Tick>();

    /// <summary>
    /// Spawns a back jet as a part of the running entity, at <see cref="Place"/> with the entity's rotation. It becomes live
    /// at the sync point of the tick, so a force sent to it in the same tick reaches it in the next.
    /// </summary>
    public static Entity Spawn(Self self) => self.Spawn(new()
    {
        AttachTo = self.Entity,
        AttachOffset = new(Place, Quaternion.Identity),
        Components = [new BackJet()]
    });

    public void Handle(Self self, scoped ref EventList events, scoped ref Outbox outbox)
    {
        Vector3 force = Vector3.Zero;
        foreach (ref JetForce each in events.Of<JetForce>())
            force = each.Force;
        if (!(float.IsFinite(force.X) && float.IsFinite(force.Y) && float.IsFinite(force.Z)))
        {
            self.Render.Emitter = default;
            throw new ArgumentOutOfRangeException(nameof(JetForce), force, "A jet's force is finite.");
        }

        self.Render.Emitter = Plume.At(MathF.Min(1, force.Length() / PersonControl.ThrustForce));
        if (force == Vector3.Zero)
            return;
        self.AttachOffset = Plume.AimedOffset(Place, -(Double3)force);
        self.Body.AddForce(Plume.PushedBodyRotation(self).Rotate((Double3)force).ToVector3());
    }
}
