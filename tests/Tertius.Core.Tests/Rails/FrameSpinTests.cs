using AwesomeAssertions;
using Tertius.Frames.Rails;

namespace Tertius.Core.Tests;

/// <summary>
/// The spin of a frame that turns in place: a lean by its tilt, then a steady turn about its own +Y. The tilt here
/// leans the pole 23.44 degrees from the parent's +Y toward its +X, as Earth's axis leans from the pole of its orbit.
/// </summary>
public class FrameSpinTests
{
    private const double Tick = 1.0 / 60;
    private static readonly DQuaternion Obliquity = DQuaternion.FromAxisAngle(new(0, 0, 1), -23.44 * Math.PI / 180);
    private static readonly Double3 Pole = new(Math.Sin(23.44 * Math.PI / 180), Math.Cos(23.44 * Math.PI / 180), 0);

    // ---- The sample of a spin over a step ----------------------------------------------------------------------

    private static readonly FrameSpin Turning = new(Tilt: Obliquity, Rate: 0.25, PhaseAtEpoch: 0.7, Epoch: 3);
    private static readonly Double3 Place = new(5, -6, 7e6);

    private static void ExpectNear(Double3 actual, Double3 expected, double tolerance = 1e-12) =>
        Double3.Distance(actual, expected).Should().BeLessThan(tolerance, $"{actual} is not {expected}");

    [Theory]
    [InlineData(0)]
    [InlineData(7.5)]
    [InlineData(86164.09)]
    public void TheFramesPoleIsItsTiltedPlusYAndStaysPut(double time)
    {
        var spin = new FrameSpin(Tilt: Obliquity, Rate: 7.2921159e-5);

        ExpectNear(spin.RotationAt(time).Rotate(new Double3(0, 1, 0)), Pole);
    }

    [Fact]
    public void AtItsEpochTheFrameStandsAtItsPhase()
    {
        var spin = new FrameSpin(Tilt: DQuaternion.Identity, Rate: 3, PhaseAtEpoch: Math.PI / 2, Epoch: 40);

        // A quarter turn about +Y, counterclockwise seen from above, takes +X to -Z.
        ExpectNear(spin.RotationAt(40).Rotate(new Double3(1, 0, 0)), new(0, 0, -1));
    }

    [Fact]
    public void TheFrameTurnsAboutItsPoleByItsRateTimesTheTimeThatPassed()
    {
        var spin = new FrameSpin(Tilt: Obliquity, Rate: 0.25, PhaseAtEpoch: 0.7, Epoch: 3);

        // From t = 5 to t = 7 the frame turns 0.5 rad about its pole: the change of rotation is that turn.
        DQuaternion change = spin.RotationAt(7) * spin.RotationAt(5).Conjugate();
        Double3 axisTimesSine = new Double3(change.X, change.Y, change.Z) * Math.Sign(change.W);
        ExpectNear(axisTimesSine, Pole * Math.Sin(0.25));
    }

    [Fact]
    public void TheAngularVelocityIsTheRateAboutThePole()
    {
        ExpectNear(new FrameSpin(Tilt: Obliquity, Rate: 0.25).AngularVelocity, Pole * 0.25);
    }

    [Fact]
    public void ATiltThatIsNotUnitLengthLeansTheFrameByItsDirectionAlone()
    {
        var spin = new FrameSpin(Tilt: new(X: Obliquity.X * 1.5, Y: Obliquity.Y * 1.5, Z: Obliquity.Z * 1.5, W: Obliquity.W * 1.5), Rate: 0.25);

        ExpectNear(spin.RotationAt(3).Rotate(new Double3(0, 1, 0)), Pole);
        ExpectNear(spin.AngularVelocity, Pole * 0.25);
    }

    [Fact]
    public void TheDefaultSpinHoldsTheFrameStillWithTheAxesOfItsParent()
    {
        FrameSpin spin = default;

        spin.IsValid.Should().BeTrue();
        spin.RotationAt(1234).Should().Be(DQuaternion.Identity);
        spin.AngularVelocity.Should().Be(Double3.Zero);
    }

    [Theory]
    [InlineData("rate NaN")]
    [InlineData("rate infinite")]
    [InlineData("phase NaN")]
    [InlineData("epoch infinite")]
    [InlineData("tilt NaN")]
    [InlineData("tilt Y NaN")]
    [InlineData("tilt Z infinite")]
    [InlineData("tilt infinite")]
    public void ASpinWithAValueThatIsNotFiniteIsNotValid(string which)
    {
        FrameSpin spin = which switch
        {
            "rate NaN" => new(Tilt: DQuaternion.Identity, Rate: double.NaN),
            "rate infinite" => new(Tilt: DQuaternion.Identity, Rate: double.PositiveInfinity),
            "phase NaN" => new(Tilt: DQuaternion.Identity, Rate: 1, PhaseAtEpoch: double.NaN),
            "epoch infinite" => new(Tilt: DQuaternion.Identity, Rate: 1, Epoch: double.NegativeInfinity),
            "tilt NaN" => new(Tilt: new(X: double.NaN, Y: 0, Z: 0, W: 1), Rate: 1),
            "tilt Y NaN" => new(Tilt: new(X: 0, Y: double.NaN, Z: 0, W: 1), Rate: 1),
            "tilt Z infinite" => new(Tilt: new(X: 0, Y: 0, Z: double.PositiveInfinity, W: 1), Rate: 1),
            _ => new(Tilt: new(X: 0, Y: 0, Z: 0, W: double.PositiveInfinity), Rate: 1)
        };

        spin.IsValid.Should().BeFalse();
    }

    [Fact]
    public void A_spin_sample_stands_at_its_place_with_the_rotation_of_the_spin_at_the_end_of_its_step()
    {
        Turning.Sample(Place, 12.5, Tick).Pose.Should().Be(new DPose(Place, Turning.RotationAt(12.5)));
    }

    [Fact]
    public void A_spin_sample_does_not_move()
    {
        MotionSample sample = Turning.Sample(Place, 12.5, Tick);

        sample.ChordVelocity.Should().Be(Double3.Zero);
        sample.Velocity.Should().Be(Double3.Zero);
        sample.Acceleration.Should().Be(Double3.Zero);
    }

    [Fact]
    public void A_spin_sample_turns_steadily_at_the_angular_velocity_of_the_spin()
    {
        MotionSample sample = Turning.Sample(Place, 12.5, Tick);

        sample.AngularVelocity.Should().Be(Turning.AngularVelocity);
        sample.AngularAcceleration.Should().Be(Double3.Zero);
    }
}
