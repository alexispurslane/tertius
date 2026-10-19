using System.Numerics;
using Tertius.Simulation;

namespace Tertius.Demo;

/// <summary>
/// Keeps an attached entity at one grip in its parent's view: at the place and turn <paramref name="grip"/> relative to
/// the parent's eye, in the view's axes, so that it moves and turns with the view. The parent's view is the view offset
/// of its render row, which <see cref="PersonControl"/> sets. The entity reads it from the snapshot of the previous tick,
/// so it follows the view one tick late. Its entity must be attached to the parent.
/// </summary>
/// <param name="grip">The place and turn in the view, in meters, in the view's axes: +X right, +Y up and -Z ahead.</param>
public sealed class HeldInView(Pose grip) : IComponent
{
    private readonly Pose grip = grip;

    public int Priority => 0;

    public EventMask Wants => EventMask.Of<Tick>();

    public void Handle(Self self, scoped ref EventList events, scoped ref Outbox outbox) =>
        self.AttachOffset = Place(self.Snapshot.RenderOf(self.AttachedTo).ViewOffset, grip);

    /// <summary>
    /// The offset from the parent of a thing at <paramref name="grip"/> in the view <paramref name="view"/>, the parent's
    /// view offset: the thing's place and its turn, in the parent's axes.
    /// </summary>
    public static Pose Place(Pose view, Pose grip) => Pose.Compose(view, grip);
}
