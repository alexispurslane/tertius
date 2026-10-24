using Tertius.Simulation;

namespace Tertius.Physics.Tests.Precision;

/// <summary>
/// How far a thrusting character drifts from its exact path at each common speed (ticket ter-kugq). It writes
/// thrust-drift.csv; see <see cref="MotionDrift"/> for the method and the columns.
/// </summary>
public sealed class ThrustDriftMeasurement
{
    /// <summary>
    /// A character of 70 kg (a capsule with a half height of 0.6 m and a radius of 0.3 m) in zero gravity. A pilot pushes
    /// it with 105 N along +x in every tick, the thrust of a person in the demo, so 1.5 m/s². The force of tick i acts in
    /// the step of tick i + 1, so after n ticks n − 1 steps had the force. The character's step adds each step's change of
    /// velocity before it moves, so the exact answer, with a = 1.5 m/s² and the tick length dt, is
    /// x(n) = x0 + v0·n·dt + a·dt²·(n − 1)·n/2 along +x, and v(n) = v0 + a·dt·(n − 1) along +x. The easy direction of the
    /// common velocity is +y, across the thrust.
    /// </summary>
    private sealed class ThrustScene : DriftScene
    {
        private const float ThrustForce = 105;
        private const float Mass = 70;

        private static readonly BodyDesc Person = new(Shape: new CapsuleShapeDesc(0.6f, 0.3f),
            Motion: BodyMotion.Character,
            Mass: Mass);

        public override string Measurement => "thrust-drift";
        public override Double3 AxisDirection => Double3.UnitY;
        public override Double3 Motion => Double3.UnitX;
        public override FrameSpec Space => new(FrameBounds.Sphere(MotionDrift.SpaceRadius));

        public override Entity[] Spawn(World world, FrameHandle space, Double3 start, Double3 common)
        {
            var pilot = new Pilot { EveryTick = body => body.AddForce(new(ThrustForce, 0, 0)) };
            Entity person = world.Spawn(new()
            {
                Frame = space,
                Pose = new(start, DQuaternion.Identity),
                LinearVelocity = common,
                Body = Person,
                Components = [pilot]
            });
            return [person];
        }

        public override (Double3 Position, Double3 Velocity) Measure(World world, FrameHandle space, Entity[] bodies) =>
            (world.PoseIn(bodies[0], space).Position, world.VelocityIn(bodies[0], space));

        public override (Double3 Position, Double3 Velocity) Expected(int ticks, double dt, Double3 start,
            Double3 common)
        {
            const double acceleration = (double)ThrustForce / Mass;
            int pushed = ticks - 1;
            Double3 position = start + common * (ticks * dt) +
                               Double3.UnitX * (acceleration * dt * dt * pushed * (pushed + 1) / 2);
            Double3 velocity = common + Double3.UnitX * (acceleration * dt * pushed);
            return (position, velocity);
        }

        public override double StartVelocityRounding(Double3 common) => (Narrowed(common) - common).Length;
    }

    [Fact(Explicit = true)]
    public void RecordsHowFarAThrustingCharacterDriftsFromItsExactPathAtEachCommonSpeed() =>
        MotionDrift.Record(new ThrustScene());
}
