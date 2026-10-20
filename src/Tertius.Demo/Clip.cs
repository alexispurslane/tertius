using System.Numerics;
using System.Runtime.CompilerServices;
using Tertius.Simulation;

namespace Tertius.Demo;

/// <summary>A <see cref="Clip"/> holds its body at its place.</summary>
/// <param name="WeightlessTicks">How many ticks in a row the frame has been weightless at the place, up to the last one.</param>
public readonly record struct Clipped(int WeightlessTicks);

/// <summary>A <see cref="Clip"/> has let its body go, and never holds it again.</summary>
public readonly record struct Released;

/// <summary>
/// What a <see cref="Clip"/> does: <see cref="Clipped"/>, from the start, or <see cref="Released"/>. The default value is
/// <see cref="Clipped"/> with no weightless tick. This is a custom C# 15 union, like <see cref="AscentState"/>.
/// </summary>
[Union]
public readonly struct ClipState : IEquatable<ClipState>
{
    // True for the released case. A clipped state fills the weightless ticks, and a released one leaves them zero.
    private readonly bool released;
    private readonly int weightlessTicks;

    public ClipState(Clipped clipped) { weightlessTicks = clipped.WeightlessTicks; }
    public ClipState(Released released) { this.released = true; }

    /// <summary>Always true: every value is a state.</summary>
    public bool HasValue => true;

    public bool TryGetValue(out Clipped value)
    {
        value = new Clipped(weightlessTicks);
        return !released;
    }

    public bool TryGetValue(out Released value)
    {
        value = default;
        return released;
    }

    public object Value => released ? new Released() : new Clipped(weightlessTicks);

    public bool Equals(ClipState other) => released == other.released && weightlessTicks == other.weightlessTicks;
    public override bool Equals(object? obj) => obj is ClipState other && Equals(other);
    public override int GetHashCode() => HashCode.Combine(released, weightlessTicks);
    public static bool operator ==(ClipState a, ClipState b) => a.Equals(b);
    public static bool operator !=(ClipState a, ClipState b) => !a.Equals(b);

    public override string ToString() => $"ClipState({Value})";
}

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

    /// <summary>What the clip does now: <see cref="Clipped"/> from the start, <see cref="Released"/> from the tick on which it lets go.</summary>
    public ClipState State { get; private set; } = new Clipped(0);

    public int Priority => 0;

    public EventMask Wants => EventMask.Of<Tick>();

    public void Handle(Self self, scoped ref EventList events, scoped ref Outbox outbox)
    {
        if (State is not Clipped clipped)
            return;
        // The gravity of the last finished step at the clip's place: the frame's field less its proper acceleration. The frame
        // tree's effective gravity, with the rotation terms of a turning frame, is a read of the step in flight that only
        // physics makes (docs/plans/spin-gravity.md, "V5, the effective gravity").
        FrameTree frames = self.Snapshot.Frames;
        Double3 gravity = frames.Gravity(self.Frame).AtPrecise(place.Position) - (Double3)frames.ProperAcceleration(self.Frame);
        bool weightless = gravity.Length < FrameTree.ZeroGravityThreshold;
        int weightlessTicks = weightless ? clipped.WeightlessTicks + 1 : 0;
        if (weightlessTicks * (double)self.DeltaTime >= ReleaseSeconds)
        {
            State = new Released();
            self.Body.SetVelocity(push);
            self.Body.SetAngularVelocity(spin);
            return;
        }
        State = new Clipped(weightlessTicks);
        self.Body.SteerTo(place);
    }
}
