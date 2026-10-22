using System.Runtime.CompilerServices;
using Tertius.Frames.Rails;
using Tertius.Simulation;

namespace Tertius.Demo;

/// <summary>
/// The plane of an <see cref="AscentAutopilot"/>'s orbit: the frame in which the autopilot reads, and the plane's
/// prograde normal, a unit vector in that frame's axes (see <see cref="AscentGuidance.PlaneNormal"/>). A normal has no
/// meaning without the axes that it is given in, so the two go together.
/// </summary>
/// <param name="Frame">The frame in which the autopilot reads the hull's state: one that does not turn, with its origin at the planet's center and its point gravity.</param>
/// <param name="Normal">The plane's prograde normal, in the axes of <paramref name="Frame"/>.</param>
public readonly record struct LaunchPlane(FrameHandle Frame, Double3 Normal);

/// <summary>Tells a parked shuttle's <see cref="AscentAutopilot"/> to launch.</summary>
public struct Ignition : IEventPayload;

/// <summary>The hull stands parked, static, and its <see cref="AscentAutopilot"/> waits for an <see cref="Ignition"/>.</summary>
public readonly record struct Parked;

/// <summary>
/// The engine pushes at full throttle, and the nose follows the velocity, as the ramp leads it.
/// </summary>
/// <param name="Guidance">The steering law of the climb that follows, set at the ignition.</param>
/// <param name="LaunchRadius">How far from the planet's center the hull launched, in meters.</param>
public readonly record struct Ramp(AscentGuidance Guidance, double LaunchRadius);

/// <summary>The <see cref="AscentGuidance"/> steers the thrust to the circle of the orbit.</summary>
/// <param name="Guidance">The steering law, set at the ignition.</param>
public readonly record struct Climb(AscentGuidance Guidance);

/// <summary>The engine is off, and the nose follows the velocity along the orbit, with the roof away from the planet.</summary>
public readonly record struct Orbit;

/// <summary>
/// What an <see cref="AscentAutopilot"/> does, in the order of a flight: <see cref="Parked"/>, <see cref="Ramp"/>,
/// <see cref="Climb"/> and <see cref="Orbit"/>. Each case holds the data that it needs, so no state reads a value that
/// only another state sets. The default value is <see cref="Parked"/>.
/// This is a custom C# 15 union, like <see cref="MotionIntent"/>: <see cref="HasValue"/> and the <c>TryGetValue</c>
/// methods let the compiler pattern-match it without boxing. <see cref="Value"/> boxes, so use it only for debugging.
/// </summary>
[Union]
public readonly struct AscentState : IEquatable<AscentState>
{
    private const byte KindParked = 0, KindRamp = 1, KindClimb = 2, KindOrbit = 3;

    // The ramp and the climb fill the guidance, and only the ramp fills the launch radius. Every other field is zero, so
    // equality compares every field.
    private readonly AscentGuidance guidance;
    private readonly double launchRadius;
    private readonly byte kind;

    public AscentState(Parked parked)
    {
        kind = KindParked;
    }

    public AscentState(Ramp ramp)
    {
        kind = KindRamp;
        guidance = ramp.Guidance;
        launchRadius = ramp.LaunchRadius;
    }

    public AscentState(Climb climb)
    {
        kind = KindClimb;
        guidance = climb.Guidance;
    }

    public AscentState(Orbit orbit)
    {
        kind = KindOrbit;
    }

    /// <summary>Always true: every value is a state, and the default value is <see cref="Parked"/>.</summary>
    public bool HasValue => true;

    public object Value => kind switch
    {
        KindRamp => new Ramp(guidance, launchRadius),
        KindClimb => new Climb(guidance),
        KindOrbit => new Orbit(),
        _ => new Parked()
    };

    public bool TryGetValue(out Parked value)
    {
        value = default;
        return kind == KindParked;
    }

    public bool TryGetValue(out Ramp value)
    {
        value = new(guidance, launchRadius);
        return kind == KindRamp;
    }

    public bool TryGetValue(out Climb value)
    {
        value = new(guidance);
        return kind == KindClimb;
    }

    public bool TryGetValue(out Orbit value)
    {
        value = default;
        return kind == KindOrbit;
    }

    public bool Equals(AscentState other) =>
        kind == other.kind && guidance == other.guidance && launchRadius.Equals(other.launchRadius);

    public override bool Equals(object? obj) => obj is AscentState other && Equals(other);
    public override int GetHashCode() => HashCode.Combine(kind, guidance, launchRadius);
    public static bool operator ==(AscentState a, AscentState b) => a.Equals(b);
    public static bool operator !=(AscentState a, AscentState b) => !a.Equals(b);

    public override string ToString() => $"AscentState({Value})";
}

/// <summary>
/// Flies a parked shuttle up its ramp and to a circular orbit of radius <paramref name="orbitRadius"/>, round the point
/// gravity of the frame of <paramref name="plane"/>, in that plane. It reads every state in the plane's frame, which does
/// not turn, so its two-body math holds there whatever frame the shuttle is in, such as a turning ground. In every tick it adds a
/// <see cref="Throttle"/> for its entity's
/// <see cref="MainEngine"/> and an <see cref="AttitudeTarget"/> for its entity's <see cref="AttitudeAutopilot"/> to the
/// entity's events, which those components read after it (see <see cref="Priority"/>). It is the first layer of the
/// shuttle's flight program, so a layer that runs after it overrides what it adds. It plans with
/// <paramref name="maxThrust"/>, the engine's thrust at full throttle. Its entity must own a frame and have a hull with
/// mass. The flight goes through the cases of <see cref="AscentState"/>:
/// <list type="bullet">
/// <item><see cref="Parked"/>: an <see cref="Ignition"/> unparks the hull with <c>Self.Body.SetMotion</c>, to dynamic. The
/// autopilot makes the <see cref="AscentGuidance"/> of the climb, and it remembers how far from the planet's center the
/// hull launched. In the same tick it starts the ramp.</item>
/// <item><see cref="Ramp"/>: full throttle, and the nose toward the velocity, so the torque helps the ramp rather than
/// fight it. At first, slower than 1 m/s, no target, so no torque. When the hull is <paramref name="climbHeight"/> meters
/// higher than where it launched, clear of the ramp, the climb starts.</item>
/// <item><see cref="Climb"/>: the guidance gives the acceleration, at most that of full throttle. The nose turns toward
/// it, and the throttle is its length divided by full throttle's acceleration. When the guidance finds the circle reached,
/// the engine stops and the orbit starts.</item>
/// <item><see cref="Orbit"/>: no throttle, so the engine stays off, and the nose follows the velocity, while the hull is in
/// a frame with point gravity. In a frame without it, such as the turning frame of a station that the shuttle docks in,
/// the autopilot adds nothing.</item>
/// </list>
/// In every case that steers, the roof turns away from the planet across the nose. The autopilot finds each target in the
/// axes of the plane's frame and writes it in the axes of the shuttle's frame, turned by the hull's two rotations, in its
/// frame and in the plane's frame.
/// </summary>
/// <remarks>
/// The autopilot reads the hull's state at the end of the last finished step, and the engine's push of a tick acts in
/// the step of the next tick. The guidance corrects each error from the state alone, so the lag costs nothing that lasts.
/// The autopilot takes the planet's G·M from the point gravity of the plane's frame in the snapshot (see
/// <see cref="KeplerOrbit.MuFrom"/>), and the full throttle's acceleration from <paramref name="maxThrust"/> and the hull's
/// mass in the body facts. The ground's axes turn about 2.4e-6 rad between the read and the step in which the torque acts,
/// which the attitude autopilot does not notice.
/// </remarks>
/// <param name="maxThrust">The thrust of the hull's main engine at full throttle, in newtons.</param>
/// <param name="orbitRadius">The radius of the circle, in meters from the planet's center.</param>
/// <param name="climbHeight">How much higher than where it launched the hull must be before the climb steers, in meters.</param>
/// <param name="plane">
/// The orbit's plane: the frame in which the autopilot reads, and the plane's prograde normal, a unit vector in that frame's
/// axes (see <see cref="AscentGuidance.PlaneNormal"/>). The ramp should lead the shuttle into that plane.
/// </param>
public sealed class AscentAutopilot(double maxThrust, double orbitRadius, double climbHeight, LaunchPlane plane)
    : IComponent
{
    /// <summary>Below this speed, in m/s, the ramp gives no attitude target, because the velocity has no direction yet.</summary>
    public const double SteerSpeed = 1;

    private readonly Double3 planeNormal = Math.Abs(plane.Normal.Length - 1) < 1e-9
        ? plane.Normal
        : throw new ArgumentException($"The normal of the orbit's plane must be a unit vector, not {plane.Normal}.",
            nameof(plane));

    /// <summary>What the autopilot does now. It starts <see cref="Parked"/>.</summary>
    public AscentState State { get; private set; } = new Parked();

    // The first layer of the flight program, before the layers that come later in the flight and before the actuators,
    // the main engine and the attitude autopilot, which read what the layers add.
    public int Priority => 0;

    public EventMask Wants => EventMask.Of<Ignition>().And<Tick>();

    public void Handle(Self self, scoped ref EventList events, scoped ref Outbox outbox)
    {
        DPose pose = self.PoseIn(plane.Frame);
        Double3 position = pose.Position, velocity = self.VelocityIn(plane.Frame);
        // The turn from the plane frame's axes to those of the hull's frame, by the hull's own two rotations, which come
        // from one record. The targets are found in the plane frame's axes and written in those of the hull's frame.
        DQuaternion intoFrame = self.PoseIn(self.Frame).Rotation * pose.Rotation.Conjugate();
        if (State is Parked)
        {
            if (!events.Of<Ignition>().Any())
                return;
            double mu = KeplerOrbit.MuFrom(self.Snapshot.Frames.Gravity(plane.Frame));
            if (mu <= 0 || !self.Snapshot.TryGetBody(self.Entity, out BodyFacts body) || body.Mass <= 0)
            {
                self.Log(
                    "AscentAutopilot stays parked: the frame of its plane has no point gravity to climb in, or its hull has no mass.");
                return;
            }

            self.Body.SetMotion(BodyMotion.Dynamic);
            State = new Ramp(new(mu, orbitRadius, maxThrust / body.Mass, planeNormal), position.Length);
        }

        if (State is Ramp ramp && position.Length - ramp.LaunchRadius > climbHeight)
            State = new Climb(ramp.Guidance);
        if (State is Climb climb && climb.Guidance.Reached(position, velocity))
            State = new Orbit();
        switch (State)
        {
            case Ramp:
                events.Add(new Throttle { Value = 1 });
                if (velocity.Length > SteerSpeed)
                    events.Add(new AttitudeTarget
                    {
                        Rotation = intoFrame * AttitudeAutopilot.Facing(velocity, pose),
                        MaxTurnRate = AttitudeAutopilot.MaxTurnRate
                    });
                break;
            case Climb { Guidance: var guidance }:
                Double3 acceleration = guidance.Acceleration(position, velocity);
                events.Add(new Throttle { Value = (float)Math.Min(1, acceleration.Length / guidance.MaxAcceleration) });
                events.Add(new AttitudeTarget
                {
                    Rotation = intoFrame * AttitudeAutopilot.Facing(acceleration, pose),
                    MaxTurnRate = AttitudeAutopilot.MaxTurnRate
                });
                break;
            // The law of the nose along the velocity and the roof away from the planet means something only while the hull is
            // in a frame with point gravity, such as earth, where it flew its orbit. In the docking station's frame, which
            // has no field, the docking autopilot steers, and this layer adds nothing.
            case Orbit when KeplerOrbit.MuFrom(self.Snapshot.Frames.Gravity(self.Frame)) > 0:
                events.Add(new AttitudeTarget
                {
                    Rotation = intoFrame * AttitudeAutopilot.Facing(velocity, pose),
                    MaxTurnRate = AttitudeAutopilot.MaxTurnRate
                });
                break;
        }
    }
}
