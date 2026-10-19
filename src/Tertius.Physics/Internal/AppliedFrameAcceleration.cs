using System.Numerics;

namespace Tertius.Physics;

/// <summary>
/// The acceleration that a body's frame gave it in the last step in which physics moved it, in the axes of its home
/// (docs/plans/spin-gravity.md, "V4, the acceleration that the frame gave a body"). The accelerometer of a dynamic hull's
/// frame reads every change of the hull's velocity over the step that is not in <see cref="Total"/> as proper
/// acceleration (see JoltPhysicsBackend.ComputeProperAccelerations).
/// </summary>
/// <param name="Field">The linear gravity at the body's place (see <see cref="FrameTree.LinearGravity"/>), in m/s².</param>
/// <param name="Turning">
/// The change that a turning frame's update made to the body's chord beyond the field, divided by the step, in m/s²: the
/// rotation part of the two-turn update, the move of an impulse's change behind, the velocity shadow's take-back of Jolt's
/// rounding of the last step, and the contact turn. Zero in a frame that does not turn. A sleeping hull holds the
/// centrifugal and the Euler term at its center of mass here, so that <see cref="Total"/> is the effective gravity that it
/// rests against. The contact turn is here, against the design's first rule, for the design's own reason: on a floor the
/// rotation part of the update leans across the floor by ω³ r dt, about 0.051 m/s² at 100 m in the spike's station at
/// 60 Hz, and the contact turn takes that lean back out of the force, so without it the accelerometer of a ship on the
/// floor read 0.0507 m/s² across the floor, and with it 4e-5 m/s².
/// </param>
internal readonly record struct AppliedFrameAcceleration(Vector3 Field, Vector3 Turning)
{
    /// <summary>The whole acceleration that the frame gave the body, in m/s².</summary>
    public Vector3 Total => Field + Turning;
}
