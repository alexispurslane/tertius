using AwesomeAssertions;

namespace Tertius.Core.Tests;

/// <summary>
/// The spin of a frame with the Spin driver: a lean by its tilt, then a steady turn about its own +Y. The tilt here
/// leans the pole 23.44 degrees from the parent's +Y toward its +X, as Earth's axis leans from the pole of its orbit.
/// </summary>
public class FrameSpinTests
{
    private static readonly DQuaternion Obliquity = DQuaternion.FromAxisAngle(new Double3(0, 0, 1), -23.44 * Math.PI / 180);
    private static readonly Double3 Pole = new(Math.Sin(23.44 * Math.PI / 180), Math.Cos(23.44 * Math.PI / 180), 0);

    private static void ExpectNear(Double3 actual, Double3 expected, double tolerance = 1e-12) =>
        Double3.Distance(actual, expected).Should().BeLessThan(tolerance, $"{actual} is not {expected}");

    [Theory]
    [InlineData(0)]
    [InlineData(7.5)]
    [InlineData(86164.09)]
    public void TheFramesPoleIsItsTiltedPlusYAndStaysPut(double time)
    {
        var spin = new FrameSpin(Obliquity, 7.2921159e-5);

        ExpectNear(spin.RotationAt(time).Rotate(new Double3(0, 1, 0)), Pole);
    }

    [Fact]
    public void AtItsEpochTheFrameStandsAtItsPhase()
    {
        var spin = new FrameSpin(DQuaternion.Identity, 3, PhaseAtEpoch: Math.PI / 2, Epoch: 40);

        // A quarter turn about +Y, counterclockwise seen from above, takes +X to -Z.
        ExpectNear(spin.RotationAt(40).Rotate(new Double3(1, 0, 0)), new Double3(0, 0, -1));
    }

    [Fact]
    public void TheFrameTurnsAboutItsPoleByItsRateTimesTheTimeThatPassed()
    {
        var spin = new FrameSpin(Obliquity, 0.25, PhaseAtEpoch: 0.7, Epoch: 3);

        // From t = 5 to t = 7 the frame turns 0.5 rad about its pole: the change of rotation is that turn.
        DQuaternion change = spin.RotationAt(7) * spin.RotationAt(5).Conjugate();
        Double3 axisTimesSine = new Double3(change.X, change.Y, change.Z) * Math.Sign(change.W);
        ExpectNear(axisTimesSine, Pole * Math.Sin(0.25));
    }

    [Fact]
    public void TheAngularVelocityIsTheRateAboutThePole()
    {
        ExpectNear(new FrameSpin(Obliquity, 0.25).AngularVelocity, Pole * 0.25);
    }

    [Fact]
    public void ATiltThatIsNotUnitLengthLeansTheFrameByItsDirectionAlone()
    {
        var spin = new FrameSpin(new DQuaternion(Obliquity.X * 1.5, Obliquity.Y * 1.5, Obliquity.Z * 1.5, Obliquity.W * 1.5), 0.25);

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
            "rate NaN" => new FrameSpin(DQuaternion.Identity, double.NaN),
            "rate infinite" => new FrameSpin(DQuaternion.Identity, double.PositiveInfinity),
            "phase NaN" => new FrameSpin(DQuaternion.Identity, 1, PhaseAtEpoch: double.NaN),
            "epoch infinite" => new FrameSpin(DQuaternion.Identity, 1, Epoch: double.NegativeInfinity),
            "tilt NaN" => new FrameSpin(new DQuaternion(double.NaN, 0, 0, 1), 1),
            "tilt Y NaN" => new FrameSpin(new DQuaternion(0, double.NaN, 0, 1), 1),
            "tilt Z infinite" => new FrameSpin(new DQuaternion(0, 0, double.PositiveInfinity, 1), 1),
            _ => new FrameSpin(new DQuaternion(0, 0, 0, double.PositiveInfinity), 1),
        };

        spin.IsValid.Should().BeFalse();
    }
}
