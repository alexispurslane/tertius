using Tertius.Simulation;

namespace Tertius.Demo;

/// <summary>
/// What the shuttle's autopilots read of the hull's own motion: the ascent and the rendezvous steer the hull's center of
/// mass, where every push acts, and not the hull's origin.
/// </summary>
public static class Navigation
{
    /// <summary>
    /// The velocity of the hull's center of mass, in m/s, in <paramref name="frame"/>: the velocity of the hull's origin
    /// plus the turn's sweep of the center of mass about the origin, ω × r, with r the center of mass from the engine's
    /// body facts (<see cref="BodyFacts.CenterOfMass"/>), turned into the frame's axes. A hull with no body facts reads as
    /// its origin. The origin's velocity holds that sweep with the opposite sign of the center's. A guidance that turns the
    /// nose toward its velocity to gain, as the ends of the ascent and the rendezvous burns do, then reads the sweep of a
    /// center of mass ahead of the origin as more velocity to gain, and turns faster: a loop that spun the hull at 0.14 rad/s
    /// once the hull fitted the Lunar Shuttle's box, whose center of mass lies 0.48 m ahead of the origin.
    /// </summary>
    public static Double3 CenterOfMassVelocity(Self self, FrameHandle frame)
    {
        Double3 velocity = self.VelocityIn(frame);
        if (self.Snapshot.BodyOf(self.Entity) is not { } body)
            return velocity;
        Double3 offset = self.PoseIn(frame).Rotation.Rotate(body.CenterOfMass);
        return velocity + Double3.Cross(self.AngularVelocityIn(frame), offset);
    }
}
