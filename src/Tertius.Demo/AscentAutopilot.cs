using Tertius.Frames.Rails;
using Tertius.Simulation;

namespace Tertius.Demo;

/// <summary>Tells a parked shuttle's <see cref="AscentAutopilot"/> to launch.</summary>
public struct Ignition : IEventPayload;

/// <summary>What an <see cref="AscentAutopilot"/> does, in the order of a flight.</summary>
public enum AscentPhase
{
    /// <summary>The hull stands parked, static, and waits for an <see cref="Ignition"/>.</summary>
    Parked,

    /// <summary>The engine pushes at full throttle, and the nose follows the velocity, as the ramp leads it.</summary>
    Ramp,

    /// <summary>The <see cref="AscentGuidance"/> steers the thrust to the circle of the orbit.</summary>
    Climb,

    /// <summary>The engine is off, and the nose follows the velocity along the orbit, with the roof away from the planet.</summary>
    Orbit,
}

/// <summary>
/// Flies a parked shuttle up its ramp and to a circular orbit of radius <paramref name="orbitRadius"/>, round the point
/// gravity of the frame that the shuttle is in, by setting the throttle of its <paramref name="engine"/> and the target of
/// its <paramref name="attitude"/> autopilot on every tick. Both must be components of the same entity, which runs them
/// after this one (see <see cref="Priority"/>). Its entity must own a frame and have a hull with mass. The flight goes
/// through the <see cref="AscentPhase"/>s:
/// <list type="bullet">
/// <item><see cref="AscentPhase.Parked"/>: an <see cref="Ignition"/> unparks the hull with <c>Self.Body.SetMotion</c>, to
/// dynamic, and the autopilot remembers how far from the planet's center the hull launched. In the same tick it starts
/// the ramp.</item>
/// <item><see cref="AscentPhase.Ramp"/>: full throttle, and the nose toward the velocity, so the torque helps the ramp
/// rather than fight it. At first, slower than 1 m/s, no torque. When the hull is <paramref name="climbHeight"/> meters
/// higher than where it launched, clear of the ramp, the climb starts.</item>
/// <item><see cref="AscentPhase.Climb"/>: the <see cref="AscentGuidance"/> gives the acceleration, at most that of full
/// throttle. The nose turns toward it, and the throttle is its length divided by full throttle's acceleration. When the
/// guidance finds the circle reached, the engine stops and the orbit starts.</item>
/// <item><see cref="AscentPhase.Orbit"/>: the engine stays off, and the nose follows the velocity.</item>
/// </list>
/// In every phase that steers, the roof turns away from the planet across the nose.
/// </summary>
/// <remarks>
/// The autopilot reads the hull's state at the end of the last finished step, and the engine's push of a tick acts in
/// the step of the next tick. The guidance corrects each error from the state alone, so the lag costs nothing that lasts.
/// The autopilot takes the planet's G·M from the point gravity of its frame in the snapshot (see
/// <see cref="KeplerOrbit.MuFrom"/>), so it flies by the gravity that pulls the hull, and the full throttle's acceleration
/// from the engine's thrust and the hull's mass in the body facts.
/// </remarks>
/// <param name="engine">The hull's main engine.</param>
/// <param name="attitude">The hull's attitude autopilot.</param>
/// <param name="orbitRadius">The radius of the circle, in meters from the planet's center.</param>
/// <param name="climbHeight">How much higher than where it launched the hull must be before the climb steers, in meters.</param>
public sealed class AscentAutopilot(MainEngine engine, AttitudeAutopilot attitude, double orbitRadius, double climbHeight) : IComponent
{
    /// <summary>Below this speed, in m/s, the ramp phase gives no torque, because the velocity has no direction yet.</summary>
    public const double SteerSpeed = 1;

    // The steering law of the climb, and how far from the planet's center the hull launched, in meters. Both are set at
    // the ignition.
    private AscentGuidance guidance;
    private double launchRadius;

    /// <summary>What the autopilot does now.</summary>
    public AscentPhase Phase { get; private set; } = AscentPhase.Parked;

    public int Priority => 0;

    public EventMask Wants => EventMask.Of<Ignition>().And<Tick>();

    public void Handle(Self self, scoped ref EventList events, scoped ref Outbox outbox)
    {
        DPose pose = self.PoseIn(self.Frame);
        Double3 position = pose.Position, velocity = self.VelocityIn(self.Frame);
        switch (Phase)
        {
            case AscentPhase.Parked:
                if (!events.Of<Ignition>().Any())
                    return;
                double mu = KeplerOrbit.MuFrom(self.Snapshot.Frames.Gravity(self.Frame));
                if (mu <= 0 || !self.Snapshot.TryGetBody(self.Entity, out BodyFacts body) || body.Mass <= 0)
                {
                    self.Log("AscentAutopilot stays parked: its frame has no point gravity to climb in, or its hull has no mass.");
                    return;
                }
                guidance = new AscentGuidance(mu, orbitRadius, engine.MaxThrust / body.Mass);
                launchRadius = position.Length;
                self.Body.SetMotion(BodyMotion.Dynamic);
                Phase = AscentPhase.Ramp;
                goto case AscentPhase.Ramp;
            case AscentPhase.Ramp:
                engine.Throttle = 1;
                attitude.Target = velocity.Length > SteerSpeed ? Facing(velocity, pose) : null;
                if (position.Length - launchRadius > climbHeight)
                    Phase = AscentPhase.Climb;
                return;
            case AscentPhase.Climb:
                if (guidance.Reached(position, velocity))
                {
                    Phase = AscentPhase.Orbit;
                    goto case AscentPhase.Orbit;
                }
                Double3 acceleration = guidance.Acceleration(position, velocity);
                engine.Throttle = (float)Math.Min(1, acceleration.Length / guidance.MaxAcceleration);
                attitude.Target = Facing(acceleration, pose);
                return;
            case AscentPhase.Orbit:
                engine.Throttle = 0;
                attitude.Target = Facing(velocity, pose);
                return;
        }
    }

    /// <summary>
    /// The rotation whose nose points along <paramref name="nose"/>, with the roof away from the planet across the nose.
    /// When the nose points straight away from the planet or at it, the roof keeps its direction in <paramref name="pose"/>.
    /// </summary>
    private static DQuaternion Facing(Double3 nose, in DPose pose)
    {
        Double3 up = pose.Position.Normalized(), along = nose.Normalized();
        Double3 roof = up - along * Double3.Dot(up, along);
        return AttitudeAutopilot.Facing(nose, roof.Length > 1e-6 ? roof : pose.Rotation.Rotate(Double3.UnitY));
    }
}
