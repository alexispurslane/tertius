using System.Numerics;
using Tertius.Simulation;

namespace Tertius.Demo;

/// <summary>The grip at contact, in the view of the passenger who sends it.</summary>
public struct GraspPencil : IEventPayload
{
    public Pose Grip;
}

/// <summary>Moves the grip to the seat, or back to the walking passenger when the frame is null.</summary>
public struct SeatPencil : IEventPayload
{
    public FrameHandle Cabin;
}

public struct ReleasePencil : IEventPayload;

/// <summary>The same dynamic pencil follows the glove while held. Releasing it leaves its velocity to physics.</summary>
public sealed class PencilInHand(Entity holder, Pose grip, FrameHandle cabin = default) : IComponent
{
    private FrameHandle seat = cabin;
    private bool seated = !cabin.IsNull;
    private float blend = cabin.IsNull ? 0 : 1;

    private BodyFacts? loose;

    public Entity Holder => holder;
    public int Priority => 0;
    public EventMask Wants => EventMask.Of<Tick>().And<SeatPencil>().And<ReleasePencil>();

    public void Handle(Self self, scoped ref EventList events, scoped ref Outbox outbox)
    {
        if (events.Of<ReleasePencil>().Any() || self.Snapshot.PoseIn(holder, self.Frame) is not { } person)
        {
            if (loose is { } before)
                self.Body.SetCollision(before.CollisionGroups, before.CollisionMask);
            self.RemoveComponent(this);
            return;
        }
        if (loose is null)
        {
            loose = self.Snapshot.BodyOf(self.Entity);
            self.Body.SetCollision(CollisionGroups.None, CollisionMask.Empty);
        }
        if (events.Of<SeatPencil>().First() is { } belt)
        {
            seated = !belt.Cabin.IsNull;
            if (seated)
                seat = belt.Cabin;
        }
        Pose view = self.Snapshot.RenderOf(holder).ViewOffset;
        var target = DPose.Compose(person, Pose.Compose(view, grip).ToDPose());
        blend = Math.Clamp(blend + (seated ? 2 : -2) * (float)self.DeltaTime, 0, 1);
        if (blend > 0 && self.Snapshot.PoseIn(seat.Entity, self.Frame) is { } hull)
        {
            var onLap = DPose.Compose(hull, new(SeatedGrip(grip.Position), DQuaternion.FromQuaternion(grip.Rotation)));
            target = new(
                Double3.Lerp(target.Position, onLap.Position, blend),
                DQuaternion.FromQuaternion(
                    Quaternion.Slerp(target.Rotation.ToQuaternion(), onLap.Rotation.ToQuaternion(), blend)
                )
            );
        }
        self.Body.SteerTo(target);
    }

    internal static Double3 SeatedGrip(Vector3 grip) =>
        new(
            DemoScenes.Shuttle.Seat.Center.X + grip.X,
            DemoScenes.Shuttle.SeatCushionY + 0.77 + grip.Y,
            DemoScenes.Shuttle.Seat.Center.Z + grip.Z
        );
}
