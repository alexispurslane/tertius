using System.Numerics;
using Tertius.Simulation;

namespace Tertius.Demo;

/// <summary>
/// A clip that holds its entity, a small dynamic body such as the demo's pen, at <paramref name="place"/> in the
/// entity's frame while the frame has weight, and lets it go once the frame has been weightless for
/// <see cref="ReleaseSeconds"/>.
/// <list type="bullet">
/// <item>While it holds, the clip steers the body to its place in every tick (<see cref="BodyHandle.SteerTo"/>): physics
/// gives the body, at the start of each step, the velocities that carry it from where it is to the place over the step.
/// The weight of the step still acts on it, so it ends each step about the gravity times the step squared off its place,
/// 8 mm at three gravities, and the next step brings it back.</item>
/// <item>The frame is weightless while the effective gravity at the place is below
/// <see cref="FrameTree.ZeroGravityThreshold"/>. On the tick that ends <see cref="ReleaseSeconds"/> of weightlessness in a
/// row, the clip lets go: it gives the body the velocity <paramref name="push"/> and the angular velocity
/// <paramref name="spin"/>, relative to its frame, in the frame's axes, and never holds it again.</item>
/// </list>
/// </summary>
/// <remarks>
/// The clip changes only its own entity, as every component does. A clip in the cabin of a ship holds what it holds
/// through the ship's climb, and lets it float free once the ship falls freely in orbit.
/// </remarks>
/// <param name="place">Where the clip holds the body, in the body's frame: the pose of its center and its turn.</param>
/// <param name="push">The velocity that the clip gives the body when it lets go, in m/s, in the body's frame.</param>
/// <param name="spin">The angular velocity that the clip gives the body when it lets go, in rad/s, in the frame's axes.</param>
public sealed class Clip(DPose place, Double3 push, Vector3 spin) : IComponent
{
    /// <summary>How long the frame must be weightless before the clip lets go, in seconds.</summary>
    public const double ReleaseSeconds = 1;

    private readonly DPose place = place;
    private readonly Double3 push = push;
    private readonly Vector3 spin = spin;

    // How many ticks in a row the frame has been weightless at the place.
    private int weightlessTicks;

    /// <summary>True while the clip holds the body, false from the tick on which it lets go.</summary>
    public bool Holds { get; private set; } = true;

    public int Priority => 0;

    public EventMask Wants => EventMask.Of<Tick>();

    public void Handle(Self self, scoped ref EventList events, scoped ref Outbox outbox)
    {
        if (!Holds)
            return;
        bool weightless = self.Snapshot.Frames.EffectiveGravity(self.Frame, place.Position).Length() < FrameTree.ZeroGravityThreshold;
        weightlessTicks = weightless ? weightlessTicks + 1 : 0;
        if (weightlessTicks * (double)self.DeltaTime >= ReleaseSeconds)
        {
            Holds = false;
            self.Body.SetVelocity(push);
            self.Body.SetAngularVelocity(spin);
            return;
        }
        self.Body.SteerTo(place);
    }
}
