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
/// <see cref="JetForce"/>, it pushes with its force, turned from its own axes into those of its frame. The engine carries
/// the push to the person's body. A character reads no torque, so the jet's place off the person's center of mass turns
/// nothing. A tick without a force gives no push, so the jet keeps nothing from an earlier tick. When a tick brings
/// several, the last one counts. A force that is not finite makes the jet throw <see cref="ArgumentOutOfRangeException"/>,
/// which the world logs as an error, and push nothing in that tick.
/// </summary>
/// <remarks>
/// The force comes in the person's own axes, not in those of its frame, so a force that the person sends in the tick before
/// a hand-off still points the same way in the person after it. The jet reads its rotation from the pose at the end of the
/// last finished step, and its push acts in the physics step of the next tick, so the person's thrust acts one tick later
/// than a push of its own body.
/// </remarks>
public sealed class BackJet : IComponent
{
    /// <summary>Where the jet sits on the person, in meters, in the person's axes: on its back, at its chest's height.</summary>
    public static Vector3 Place { get; } = new(0, 0.3f, 0.3f);

    /// <summary>
    /// Spawns a back jet as a part of the running entity, at <see cref="Place"/> with the entity's rotation. It becomes live
    /// at the sync point of the tick, so a force sent to it in the same tick reaches it in the next.
    /// </summary>
    public static Entity Spawn(Self self) => self.Spawn(new SpawnDesc
    {
        AttachTo = self.Entity,
        AttachOffset = new Pose(Place, Quaternion.Identity),
        Components = [new BackJet()],
    });

    public int Priority => 10;

    public EventMask Wants => EventMask.Of<JetForce>();

    public void Handle(Self self, scoped ref EventList events, scoped ref Outbox outbox)
    {
        // The jet wants only JetForce, so the events hold at least one, and the last one counts.
        Vector3 force = Vector3.Zero;
        foreach (ref JetForce each in events.Of<JetForce>())
            force = each.Force;
        if (!(float.IsFinite(force.X) && float.IsFinite(force.Y) && float.IsFinite(force.Z)))
            throw new ArgumentOutOfRangeException(nameof(JetForce), force, "A jet's force is finite.");
        if (force != Vector3.Zero)
            self.Body.AddForce(self.PoseIn(self.Frame).Rotation.Rotate((Double3)force).ToVector3());
    }
}
