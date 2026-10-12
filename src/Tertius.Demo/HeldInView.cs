using System.Numerics;
using Tertius.Simulation;

namespace Tertius.Demo;

/// <summary>
/// Keeps an attached entity at one place in its parent's view: at <paramref name="hold"/> from the parent's eye, in the
/// view's axes, and turned with the view. The parent's view is the view offset of its render row, which
/// <see cref="PersonControl"/> sets. The entity reads it from the snapshot of the previous tick, so it follows the view
/// one tick late. Its entity must be attached to the parent.
/// </summary>
/// <param name="hold">The place in the view, in meters, in the view's axes: +X right, +Y up and -Z ahead.</param>
public sealed class HeldInView(Vector3 hold) : IComponent
{
    private readonly Vector3 hold = hold;

    public int Priority => 0;

    public EventMask Wants => EventMask.Of<Tick>();

    public void Handle(Self self, scoped ref EventList events, scoped ref Outbox outbox) =>
        self.AttachOffset = Place(self.Snapshot.RenderOf(self.AttachedTo).ViewOffset, hold);

    /// <summary>
    /// The offset from the parent of a thing at <paramref name="hold"/> in the view <paramref name="view"/>, the parent's
    /// view offset: the thing's place and its turn, in the parent's axes.
    /// </summary>
    public static Pose Place(Pose view, Vector3 hold) => new(view.TransformPoint(hold), view.Rotation);
}
