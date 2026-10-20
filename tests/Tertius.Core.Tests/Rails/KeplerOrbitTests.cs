using System.Numerics;
using AwesomeAssertions;
using Tertius.Frames.Rails;

namespace Tertius.Core.Tests;

/// <summary>
/// The solution of Kepler's equation behind <see cref="KeplerOrbit.StateAt"/> and the attitudes: the frame moves without a
/// jump at every eccentricity that the orbit allows, and its clock starts at its epoch. Also the sample of an orbit over a
/// step (<see cref="KeplerOrbit.Sample"/>) and the angular acceleration of a frame that faces its parent. And the G·M rule:
/// an orbit holds no G·M, and each member that needs one takes the G·M of the parent as <c>mu</c>, so a Kepler frame and
/// the loose bodies in its parent fall on the same G·M.
/// </summary>
public class KeplerOrbitTests
{
    private const double Mu = 3.986004418e14;
    private const double Tick = 1.0 / 60;

    private static KeplerOrbit Oval(double eccentricity, KeplerAttitude attitude = KeplerAttitude.Spin) =>
        new(1e7, eccentricity, 0, 0, 0, 0, 0, DQuaternion.Identity, 0, 0, attitude);

    [Theory]
    [InlineData(0.8)]
    [InlineData(0.9)]
    [InlineData(0.99)]
    public void OnAnOrbitOfHighEccentricityTheFrameMovesNoFurtherInATickThanItsSpeedCarriesIt(double eccentricity)
    {
        KeplerOrbit orbit = Oval(eccentricity);
        (Double3 position, Double3 velocity) = orbit.StateAt(0, Mu);

        for (long tick = 1; tick * Tick <= orbit.Period(Mu); tick++)
        {
            (Double3 next, Double3 nextVelocity) = orbit.StateAt(tick * Tick, Mu);
            // Along one tick, the speed changes by under 1% even at the periapsis of e = 0.99.
            double reach = Math.Max(velocity.Length, nextVelocity.Length) * Tick * 1.05;
            Double3.Distance(next, position).Should().BeLessThan(reach, $"the frame jumps at tick {tick}");
            (position, velocity) = (next, nextVelocity);
        }
    }

    [Theory]
    [InlineData(0.8)]
    [InlineData(0.9)]
    [InlineData(0.99)]
    public void OnAnOrbitOfHighEccentricityAFacingFrameTurnsNoFurtherInATickThanItsAngularVelocityCarriesIt(double eccentricity)
    {
        KeplerOrbit orbit = Oval(eccentricity, KeplerAttitude.FacesParent);
        DQuaternion rotation = orbit.RotationAt(0, Mu);
        double rate = orbit.AngularVelocityAt(0, Mu).Length;

        for (long tick = 1; tick * Tick <= orbit.Period(Mu); tick++)
        {
            DQuaternion next = orbit.RotationAt(tick * Tick, Mu);
            double nextRate = orbit.AngularVelocityAt(tick * Tick, Mu).Length;
            double dot = Math.Abs(rotation.X * next.X + rotation.Y * next.Y + rotation.Z * next.Z + rotation.W * next.W);
            double angle = 2 * Math.Acos(Math.Min(dot, 1));
            // The small absolute margin covers the rounding of Acos near 1.
            angle.Should().BeLessThan(Math.Max(rate, nextRate) * Tick * 1.05 + 1e-6, $"the frame flips at tick {tick}");
            (rotation, rate) = (next, nextRate);
        }
    }

    [Fact]
    public void ASpinningOrbitStandsAtItsSpinPhaseAtItsEpochAndTurnsAtItsRate()
    {
        KeplerOrbit orbit = new(1e7, 0, 0, 0, 0, 0, 40, DQuaternion.Identity, 3, Math.PI / 2);

        // At the epoch, a quarter turn about +Y takes +X to -Z; π/6 s later, 3 rad/s has turned it another half turn.
        Double3.Distance(orbit.RotationAt(40, Mu).Rotate(Double3.UnitX), new Double3(0, 0, -1)).Should().BeLessThan(1e-12);
        Double3.Distance(orbit.RotationAt(40 + Math.PI / 6, Mu).Rotate(Double3.UnitX), new Double3(-1, 0, 0)).Should().BeLessThan(1e-12);
    }

    [Fact]
    public void AnOrbitWhoseMeanAnomalyIsZeroAtItsEpochIsAtPeriapsisThen()
    {
        // Periapsis lies along +X, at a (1 - e) = 7,000 km, and the epoch is 100 s.
        KeplerOrbit orbit = new(1e7, 0.3, 0, 0, 0, 0, 100, DQuaternion.Identity, 0, 0, KeplerAttitude.FacesParent);

        Double3.Distance(orbit.StateAt(100, Mu).Position, new Double3(7e6, 0, 0)).Should().BeLessThan(1e-3);
        Double3.Distance(orbit.RotationAt(100, Mu).Rotate(new Double3(0, -1, 0)), new Double3(-1, 0, 0)).Should().BeLessThan(1e-9);
    }

    // ---- The sample of an orbit over a step --------------------------------------------------------------------

    /// <summary>
    /// An eccentric, inclined orbit from 7,000 km to 13,000 km round earth, whose period is about 9,950 s, with a tilted
    /// spin for the attitude <see cref="KeplerAttitude.Spin"/>. A frame that faces earth on it turns at a changing rate about
    /// a tilted normal.
    /// </summary>
    private static readonly KeplerOrbit InclinedOval =
        new(1e7, 0.3, 0.5, 0.7, 1.1, 0.2, 100, DQuaternion.FromAxisAngle(Double3.UnitZ, -0.4), 7.3e-5, 0.6, KeplerAttitude.FacesParent);

    /// <summary>Ends of steps, in seconds: at the epoch, after a fraction of a turn, and after more than three turns.</summary>
    private static readonly double[] StepEnds = [100, 1334.56, 33_000.25];

    [Theory]
    [InlineData(KeplerAttitude.Spin)]
    [InlineData(KeplerAttitude.FacesParent)]
    public void A_sample_holds_the_place_rotation_velocity_and_angular_velocity_of_the_orbit_at_the_end_of_its_step(KeplerAttitude attitude)
    {
        KeplerOrbit orbit = InclinedOval with { Attitude = attitude };
        foreach (double stepEnd in StepEnds)
        {
            MotionSample sample = orbit.Sample(Mu, stepEnd, Tick);

            (Double3 position, Double3 velocity) = orbit.StateAt(stepEnd, Mu);
            sample.Pose.Should().Be(new DPose(position, orbit.RotationAt(stepEnd, Mu)), $"the sample ends its step at {stepEnd} s");
            sample.Velocity.Should().Be(velocity);
            sample.AngularVelocity.Should().Be(orbit.AngularVelocityAt(stepEnd, Mu));
        }
    }

    [Theory]
    [InlineData(6.8e6, 0)]
    [InlineData(1e7, 0.3)]
    public void The_chord_of_a_sample_is_the_change_of_place_over_its_step_divided_by_the_step(double semiMajorAxis, double eccentricity)
    {
        KeplerOrbit orbit = InclinedOval with { SemiMajorAxis = semiMajorAxis, Eccentricity = eccentricity };
        foreach (double stepEnd in StepEnds)
        {
            MotionSample sample = orbit.Sample(Mu, stepEnd, Tick);

            Double3 change = orbit.StateAt(stepEnd, Mu).Position - orbit.StateAt(stepEnd - Tick, Mu).Position;
            // The chord leaves out terms of the fourth order, under 1e-13 m over the step. The change of place is less
            // exact: each place carries the rounding of its time, up to 3.6e-12 s at 33,000 s, times a speed of up to
            // 8.6 km/s, and the rounding of its mean anomaly, up to 1.8e-15 rad at 21 rad, times up to 13,000 km. That is
            // about 5e-8 m a place, so the bound is 2e-7 m. A chord off by half a step of the pull is 1e-3 m off.
            (sample.ChordVelocity * Tick - change).Length.Should().BeLessThan(2e-7, $"the step ends at {stepEnd} s");
        }
    }

    [Theory]
    [InlineData(6.8e6, 0)]
    [InlineData(1e7, 0.3)]
    public void The_acceleration_of_a_sample_is_the_pull_at_the_place_where_its_step_starts(double semiMajorAxis, double eccentricity)
    {
        KeplerOrbit orbit = InclinedOval with { SemiMajorAxis = semiMajorAxis, Eccentricity = eccentricity };
        foreach (double stepEnd in StepEnds)
        {
            MotionSample sample = orbit.Sample(Mu, stepEnd, Tick);

            Double3 pull = KeplerOrbit.Acceleration(orbit.StateAt(stepEnd - Tick, Mu).Position, Mu);
            // The sample finds the start as the end less the chord times the step. That place and the orbit's place at
            // the start differ by the rounding of the places, under 2e-7 m (see the chord test above), which changes the
            // pull by its gradient, at most 2 GM / r³ = 2.5e-6 /s², times that: 5e-13 m/s². The pull at the end of the
            // step is 1.6e-4 m/s² off.
            (sample.Acceleration - pull).Length.Should().BeLessThan(1e-12, $"the step ends at {stepEnd} s");
        }
    }

    [Theory]
    [InlineData(250)]
    [InlineData(2_000)]
    [InlineData(7_000)]
    [InlineData(33_000.25)]
    public void The_angular_acceleration_of_a_frame_that_faces_its_parent_is_the_change_of_its_angular_velocity(double time)
    {
        const double h = 0.25;
        Double3 change = (InclinedOval.AngularVelocityAt(time + h, Mu) - InclinedOval.AngularVelocityAt(time - h, Mu)) / (2 * h);
        change.Length.Should().BeGreaterThan(5e-8, "away from the apsides, the frame turns faster or slower");

        // The central difference leaves out the third derivative of the angular velocity times h² / 6: under 1.4e-14
        // rad/s² near periapsis, and it shrinks with h². The rounding of the angular velocities, about 1e-18 rad/s, adds
        // under 1e-17 rad/s². An angular acceleration off by a factor or a sign is off by 5e-8 rad/s² or more.
        (InclinedOval.AngularAccelerationAt(time, Mu) - change).Length.Should().BeLessThan(1e-13);
    }

    [Fact]
    public void A_spinning_orbit_has_no_angular_acceleration()
    {
        KeplerOrbit orbit = InclinedOval with { Attitude = KeplerAttitude.Spin };

        foreach (double time in StepEnds)
            orbit.AngularAccelerationAt(time, Mu).Should().Be(Double3.Zero);
    }

    [Theory]
    [InlineData(KeplerAttitude.Spin)]
    [InlineData(KeplerAttitude.FacesParent)]
    public void The_angular_acceleration_of_a_sample_is_the_mean_over_its_step(KeplerAttitude attitude)
    {
        // Every step record means the same by its angular acceleration: the change of the angular velocity over the step,
        // divided by the step (docs/plans/spin-gravity.md, "V3"). The mean is the value at the middle of the step, to the
        // third derivative of the angular velocity times the step squared over 24, far under 1e-15 rad/s².
        KeplerOrbit orbit = InclinedOval with { Attitude = attitude };

        foreach (double stepEnd in StepEnds)
        {
            Double3 mean = orbit.Sample(Mu, stepEnd, Tick).AngularAcceleration;
            mean.Should().Be((orbit.AngularVelocityAt(stepEnd, Mu) - orbit.AngularVelocityAt(stepEnd - Tick, Mu)) / Tick);
            (mean - orbit.AngularAccelerationAt(stepEnd - Tick / 2, Mu)).Length.Should().BeLessThan(1e-13);
        }
    }

    // ---- An orbit around no G·M ---------------------------------------------------------------------------------

    [Fact]
    public void A_frame_that_faces_a_parent_without_GM_rests_at_its_origin_and_does_not_turn()
    {
        InclinedOval.StateAt(1334.56, 0).Should().Be((Double3.Zero, Double3.Zero));
        InclinedOval.AngularVelocityAt(1334.56, 0).Should().Be(Double3.Zero);
        InclinedOval.AngularAccelerationAt(1334.56, 0).Should().Be(Double3.Zero);
    }

    [Theory]
    [InlineData(KeplerAttitude.Spin)]
    [InlineData(KeplerAttitude.FacesParent)]
    public void A_sample_around_no_GM_is_the_orbit_at_the_end_of_its_step(KeplerAttitude attitude)
    {
        KeplerOrbit orbit = InclinedOval with { Attitude = attitude };

        MotionSample sample = orbit.Sample(0, 1334.56, Tick);

        (Double3 position, Double3 velocity) = orbit.StateAt(1334.56, 0);
        sample.Pose.Should().Be(new DPose(position, orbit.RotationAt(1334.56, 0)));
        sample.Velocity.Should().Be(velocity);
        sample.AngularVelocity.Should().Be(orbit.AngularVelocityAt(1334.56, 0));
    }

    [Theory]
    [InlineData(KeplerAttitude.Spin)]
    [InlineData(KeplerAttitude.FacesParent)]
    public void A_sample_around_no_GM_has_no_chord_and_no_acceleration(KeplerAttitude attitude)
    {
        MotionSample sample = (InclinedOval with { Attitude = attitude }).Sample(0, 1334.56, Tick);

        sample.ChordVelocity.Should().Be(Double3.Zero);
        sample.Acceleration.Should().Be(Double3.Zero);
        sample.AngularAcceleration.Should().Be(Double3.Zero);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void A_parent_without_GM_gives_an_infinite_period(double mu)
    {
        // A G·M below 0 has no square root, so there the mean motion is NaN, and 2π over it would be NaN as well. A G·M
        // of 0 gives a mean motion of 0 and 2π / 0 is infinite anyway, so only the second case pins the guard.
        InclinedOval.Period(mu).Should().Be(double.PositiveInfinity);
    }

    // ---- An orbit with no size ----------------------------------------------------------------------------------

    [Theory]
    [InlineData(0)]
    [InlineData(-1e7)]
    public void An_orbit_with_a_semi_major_axis_of_0_or_below_does_not_turn_and_never_comes_round(double semiMajorAxis)
    {
        KeplerOrbit orbit = InclinedOval with { SemiMajorAxis = semiMajorAxis };

        orbit.MeanMotion(Mu).Should().Be(0);
        orbit.Period(Mu).Should().Be(double.PositiveInfinity);
    }

    [Theory]
    [InlineData(0, KeplerAttitude.Spin)]
    [InlineData(0, KeplerAttitude.FacesParent)]
    [InlineData(-1e7, KeplerAttitude.Spin)]
    [InlineData(-1e7, KeplerAttitude.FacesParent)]
    public void An_orbit_with_a_semi_major_axis_of_0_or_below_sits_at_the_origin_of_its_parent_with_no_velocity(
        double semiMajorAxis, KeplerAttitude attitude)
    {
        KeplerOrbit orbit = InclinedOval with { SemiMajorAxis = semiMajorAxis, Attitude = attitude };

        foreach (double time in StepEnds)
            orbit.StateAt(time, Mu).Should().Be((Double3.Zero, Double3.Zero), $"at {time} s");
    }

    [Theory]
    [InlineData(0, KeplerAttitude.Spin)]
    [InlineData(0, KeplerAttitude.FacesParent)]
    [InlineData(-1e7, KeplerAttitude.Spin)]
    [InlineData(-1e7, KeplerAttitude.FacesParent)]
    public void A_sample_of_an_orbit_with_a_semi_major_axis_of_0_or_below_rests_at_the_origin_without_chord_or_acceleration(
        double semiMajorAxis, KeplerAttitude attitude)
    {
        KeplerOrbit orbit = InclinedOval with { SemiMajorAxis = semiMajorAxis, Attitude = attitude };

        // The pull and the chord of a frame at the parent's origin would divide zero by zero, so a mutant that lets such an
        // orbit through gives NaN here.
        MotionSample sample = orbit.Sample(Mu, 1334.56, Tick);

        sample.Pose.Position.Should().Be(Double3.Zero);
        sample.Velocity.Should().Be(Double3.Zero);
        sample.ChordVelocity.Should().Be(Double3.Zero);
        sample.Acceleration.Should().Be(Double3.Zero);
        sample.AngularAcceleration.Should().Be(Double3.Zero);
    }

    // ---- The G·M of the parent ------------------------------------------------------------------------------------

    [Fact]
    public void AnOrbitIsValidWithoutAGM()
    {
        KeplerOrbit.Circular(7e6).IsValid.Should().BeTrue();
        default(KeplerOrbit).IsValid.Should().BeFalse("it would leave the frame at its parent's origin");
    }

    [Fact]
    public void TheSameOrbitTurnsHalfAsFastAroundAParentWithAQuarterOfTheGM()
    {
        KeplerOrbit orbit = KeplerOrbit.Circular(7e6);

        orbit.MeanMotion(Mu / 4).Should().BeApproximately(orbit.MeanMotion(Mu) / 2, 1e-15);
        orbit.Period(Mu / 4).Should().BeApproximately(orbit.Period(Mu) * 2, 1e-6);
    }

    [Fact]
    public void TheMeanMotionOfACircleIsTheSquareRootOfTheGMOverTheCubeOfItsRadius()
    {
        KeplerOrbit.Circular(7e6).MeanMotion(Mu).Should().BeApproximately(Math.Sqrt(Mu / (7e6 * 7e6 * 7e6)), 1e-18);
    }

    [Fact]
    public void TheSameOrbitMovesAtTheSpeedOfTheGMThatItIsGiven()
    {
        KeplerOrbit orbit = KeplerOrbit.Circular(7e6);

        orbit.StateAt(0, Mu).Velocity.Length.Should().BeApproximately(Math.Sqrt(Mu / 7e6), 1e-6);
        orbit.StateAt(0, 4 * Mu).Velocity.Length.Should().BeApproximately(2 * Math.Sqrt(Mu / 7e6), 1e-6);
    }

    [Theory]
    [InlineData(0.0)]
    [InlineData(-1.0)]
    public void AnOrbitAroundAParentWithoutAGMStaysAtTheOriginOfTheParent(double parentMu)
    {
        KeplerOrbit orbit = KeplerOrbit.Circular(7e6);

        orbit.StateAt(123, parentMu).Should().Be((Double3.Zero, Double3.Zero));
    }

    [Fact]
    public void ASpinAttitudeIgnoresTheGMOfTheParent()
    {
        KeplerOrbit orbit = KeplerOrbit.Circular(7e6, spinRate: 0.1);

        orbit.RotationAt(50, Mu).Should().Be(orbit.RotationAt(50, 4 * Mu), "only the attitude that faces the parent follows the place on the orbit");
        orbit.AngularVelocityAt(50, Mu).Should().Be(orbit.AngularVelocityAt(50, 4 * Mu));
    }

    [Theory]
    [InlineData(0.25)]
    [InlineData(1.0)]
    [InlineData(4.0)]
    public void ABellyThatFacesTheParentPointsAtItWhateverTheGMThatMovesTheFrame(double share)
    {
        KeplerOrbit orbit = KeplerOrbit.Circular(7e6, phase: 0.3) with { Attitude = KeplerAttitude.FacesParent };
        double parentMu = share * Mu;

        Double3 position = orbit.StateAt(100, parentMu).Position;
        Double3 belly = orbit.RotationAt(100, parentMu).Rotate(new Double3(0, -1, 0));

        Double3.Distance(belly, -position.Normalized()).Should().BeLessThan(1e-9);
    }

    [Fact]
    public void ARotationThatFacesTheParentFollowsTheGMThatMovesTheFrame()
    {
        KeplerOrbit orbit = KeplerOrbit.Circular(7e6) with { Attitude = KeplerAttitude.FacesParent };

        double rate = orbit.AngularVelocityAt(50, Mu).Length;

        rate.Should().BeApproximately(orbit.MeanMotion(Mu), 1e-12);
        orbit.AngularVelocityAt(50, 4 * Mu).Length.Should().BeApproximately(2 * rate, 1e-12);
    }
}
