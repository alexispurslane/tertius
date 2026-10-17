using System.Numerics;
using Tertius.Simulation;

namespace Tertius.Physics.Tests.Precision;

/// <summary>
/// How far the relative motion of two free bodies drifts from the exact one at each common speed (ticket ter-kugq). It
/// writes relative-drift.csv; see <see cref="MotionDrift"/> for the method and the columns.
/// </summary>
public class RelativeDriftMeasurement
{
    /// <summary>
    /// Two dynamic boxes of 1 m by 1 m by 1 m and 10 kg, with no damping, in zero gravity. Box A starts 5 m along −x from the
    /// reference point and box B 5 m along +x, so they are 10 m apart. A moves at the common velocity minus 0.05 m/s along
    /// x and B at the common velocity plus 0.05 m/s along x, so they move apart at 0.1 m/s and neither rests, also with no
    /// common velocity. The measured quantities are B's position and velocity minus A's. Their exact values are
    /// (10 m + 0.1 m/s · t) along +x and 0.1 m/s along +x. The easy direction of the common velocity is +y, across the
    /// relative motion.
    /// </summary>
    private sealed class RelativeScene : DriftScene
    {
        private const double HalfSeparation = 5;
        private const double HalfRelativeSpeed = 0.05;
        private static readonly BodyDesc Box = new(new BoxShapeDesc(new Vector3(0.5f)), Mass: 10, Restitution: 0, LinearDamping: 0, AngularDamping: 0);

        public override string Measurement => "relative-drift";
        public override Double3 AxisDirection => Double3.UnitY;
        public override Double3 Motion => Double3.UnitX;
        public override FrameSpec Space => new(FrameBounds.Sphere(MotionDrift.SpaceRadius), MotionDriver.Static);

        public override Entity[] Spawn(World world, FrameHandle space, Double3 start, Double3 common) =>
        [
            SpawnBox(world, space, start - Double3.UnitX * HalfSeparation, common - Double3.UnitX * HalfRelativeSpeed),
            SpawnBox(world, space, start + Double3.UnitX * HalfSeparation, common + Double3.UnitX * HalfRelativeSpeed),
        ];

        private static Entity SpawnBox(World world, FrameHandle space, Double3 position, Double3 velocity) =>
            world.Spawn(new SpawnDesc { Frame = space, Pose = new DPose(position, DQuaternion.Identity), LinearVelocity = velocity, Body = Box });

        public override (Double3 Position, Double3 Velocity) Measure(World world, FrameHandle space, Entity[] bodies) =>
        (
            world.PoseIn(bodies[1], space).Position - world.PoseIn(bodies[0], space).Position,
            world.VelocityIn(bodies[1], space) - world.VelocityIn(bodies[0], space)
        );

        public override (Double3 Position, Double3 Velocity) Expected(int ticks, double dt, Double3 start, Double3 common)
        {
            Double3 relativeVelocity = Double3.UnitX * (2 * HalfRelativeSpeed);
            return (Double3.UnitX * (2 * HalfSeparation) + relativeVelocity * (ticks * dt), relativeVelocity);
        }

        /// <summary>The rounding of the relative velocity: B's narrowed velocity minus A's, against the exact difference.</summary>
        public override double StartVelocityRounding(Double3 common)
        {
            Double3 a = common - Double3.UnitX * HalfRelativeSpeed;
            Double3 b = common + Double3.UnitX * HalfRelativeSpeed;
            return (Narrowed(b) - Narrowed(a) - (b - a)).Length;
        }
    }

    [Fact(Explicit = true)]
    public void RecordsHowFarTheRelativeMotionOfTwoFreeBodiesDriftsAtEachCommonSpeed() => MotionDrift.Record(new RelativeScene());
}
