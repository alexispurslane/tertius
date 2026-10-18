using System.Numerics;
using Tertius.Simulation;

namespace Tertius.Physics.Tests.Precision;

/// <summary>
/// How far a body that falls in a uniform field drifts from its exact parabola at each common speed (ticket ter-kugq).
/// It writes ballistic-drift.csv; see <see cref="MotionDrift"/> for the method and the columns.
/// </summary>
public class BallisticDriftMeasurement
{
    /// <summary>
    /// A dynamic box of 1 m by 1 m by 1 m and 10 kg, with no damping, in a uniform field of 9.81 m/s² along −y. The engine
    /// gives Jolt the chord of each step's velocity, so the exact answer is the parabola itself:
    /// x(t) = x0 + v0·t + g·t²/2 and v(t) = v0 + g·t, with t = n·dt after n ticks and g = (0, −9.81f, 0), the field as a
    /// float gives it. The easy direction of the common velocity is +x, across the field.
    /// </summary>
    private sealed class BallisticScene : DriftScene
    {
        private const float Gravity = 9.81f;
        private static readonly BodyDesc Box = new(new BoxShapeDesc(new Vector3(0.5f)), Mass: 10, Restitution: 0, LinearDamping: 0, AngularDamping: 0);

        public override string Measurement => "ballistic-drift";
        public override Double3 AxisDirection => Double3.UnitX;
        public override Double3 Motion => -Double3.UnitY;

        public override FrameSpec Space =>
            new(FrameBounds.Sphere(MotionDrift.SpaceRadius), GravityField.Uniform(new Vector3(0, -Gravity, 0)));

        public override Entity[] Spawn(World world, FrameHandle space, Double3 start, Double3 common) =>
        [
            world.Spawn(new SpawnDesc { Frame = space, Pose = new DPose(start, DQuaternion.Identity), LinearVelocity = common, Body = Box }),
        ];

        public override (Double3 Position, Double3 Velocity) Measure(World world, FrameHandle space, Entity[] bodies) =>
            (world.PoseIn(bodies[0], space).Position, world.VelocityIn(bodies[0], space));

        public override (Double3 Position, Double3 Velocity) Expected(int ticks, double dt, Double3 start, Double3 common)
        {
            var g = new Double3(0, -(double)Gravity, 0);
            double t = ticks * dt;
            return (start + common * t + g * (t * t / 2), common + g * t);
        }

        public override double StartVelocityRounding(Double3 common) => (Narrowed(common) - common).Length;
    }

    [Fact(Explicit = true)]
    public void RecordsHowFarAFallingBodyDriftsFromItsExactParabolaAtEachCommonSpeed() => MotionDrift.Record(new BallisticScene());
}
