using System.Numerics;
using AwesomeAssertions;

namespace Tertius.Core.Tests;

/// <summary>
/// An orbit holds no G·M. Each member that needs one takes the G·M of the parent as <c>parentMu</c>. The frame tree
/// supplies it, from the point gravity of the parent frame, so a Kepler frame and the loose bodies in its parent fall on
/// the same G·M. The tree checks that every Kepler frame has such a parent when it is asked to.
/// </summary>
public class KeplerParentMuTests
{
    private const double Mu = 3.986004418e14;

    /// <summary>The fields of a parent that give a Kepler frame no G·M, by the names that the theories use.</summary>
    public static TheoryData<string, GravityField> FieldsWithoutPointGravity => new()
    {
        { "no gravity", GravityField.None },
        { "a uniform field", GravityField.Uniform(new Vector3(0, -9.81f, 0)) },
        { "a point field with G·M 0", GravityField.Point(0) },
        { "a point field with a negative G·M", GravityField.Point(-Mu) },
        { "a point field with a G·M that is not a number", GravityField.Point(double.NaN) },
        { "a point field with an infinite G·M", GravityField.Point(double.PositiveInfinity) },
        { "a uniform field that carries a G·M number", new GravityField(GravityKind.Uniform, new Vector3(0, -9.81f, 0), null, Mu) },
    };

    private sealed class Planet
    {
        public readonly FrameTree Frames = new();
        public readonly FrameHandle Root;
        public readonly FrameHandle Moon;

        public Planet(GravityField field, MotionDriver moonDriver = MotionDriver.Kepler)
        {
            Root = Frames.Create(new Entity(1, 1), FrameHandle.Null, DPose.Identity, FrameBounds.Sphere(1e9f), MotionDriver.Static, field);
            Moon = Frames.Create(new Entity(2, 1), Root, DPose.Identity, FrameBounds.Sphere(50), moonDriver, kepler: KeplerOrbit.Circular(7e6));
            Frames.Update();
        }
    }

    // ---- The G·M of a field ----------------------------------------------------------------------------------------

    [Fact]
    public void APointFieldWithAPositiveGMCanBeOrbited()
    {
        GravityField.Point(Mu).CanBeOrbited.Should().BeTrue();
    }

    [Theory]
    [MemberData(nameof(FieldsWithoutPointGravity))]
    public void AFieldThatIsNotAPointFieldWithAPositiveGMCannotBeOrbited(string why, GravityField field)
    {
        field.CanBeOrbited.Should().BeFalse(why);
    }

    // ---- The G·M of a Kepler frame ---------------------------------------------------------------------------------

    [Fact]
    public void AKeplerFrameTakesTheGMOfThePointGravityOfItsParent()
    {
        var planet = new Planet(GravityField.Point(Mu));

        planet.Frames.KeplerMu(planet.Moon).Should().Be(Mu);
    }

    [Fact]
    public void TheGMOfAKeplerFrameFollowsTheGravityOfItsParentWhenItChanges()
    {
        var planet = new Planet(GravityField.Point(Mu));

        planet.Frames.SetGravity(planet.Root, GravityField.Point(2 * Mu));

        planet.Frames.KeplerMu(planet.Moon).Should().Be(2 * Mu, "the orbit holds no G·M, so there is no second copy to forget");
    }

    [Theory]
    [MemberData(nameof(FieldsWithoutPointGravity))]
    public void AFrameWhoseParentHasNoPointGravityHasNoGMToOrbit(string why, GravityField field)
    {
        var planet = new Planet(field);

        planet.Frames.KeplerMu(planet.Moon).Should().Be(0, why);
    }

    [Fact]
    public void ARootFrameHasNoGMToOrbit()
    {
        var planet = new Planet(GravityField.Point(Mu));

        planet.Frames.KeplerMu(planet.Root).Should().Be(0);
    }

    [Fact]
    public void TheGMOfAStaleFrameIsRefused()
    {
        var planet = new Planet(GravityField.Point(Mu));
        planet.Frames.Destroy(planet.Moon);

        Action ask = () => planet.Frames.KeplerMu(planet.Moon);

        ask.Should().Throw<InvalidOperationException>();
    }

    // ---- The invariant of the tree ---------------------------------------------------------------------------------

    [Fact]
    public void AKeplerFrameInAParentWithPointGravityKeepsTheInvariants()
    {
        var planet = new Planet(GravityField.Point(Mu));

        Action check = planet.Frames.AssertInvariants;

        check.Should().NotThrow();
    }

    [Theory]
    [MemberData(nameof(FieldsWithoutPointGravity))]
    public void AKeplerFrameWhoseParentHasNoPointGravityBreaksTheInvariants(string why, GravityField field)
    {
        var planet = new Planet(GravityField.Point(Mu));
        planet.Frames.SetGravity(planet.Root, field);

        Action check = planet.Frames.AssertInvariants;

        check.Should().Throw<ContractException>(why).WithMessage("*Invariant*");
    }

    [Fact]
    public void AKeplerRootFrameBreaksTheInvariants()
    {
        var frames = new FrameTree();
        frames.Create(new Entity(1, 1), FrameHandle.Null, DPose.Identity, FrameBounds.Sphere(10), MotionDriver.Kepler, GravityField.Point(Mu), KeplerOrbit.Circular(7e6));

        Action check = frames.AssertInvariants;

        check.Should().Throw<ContractException>();
    }

    [Theory]
    [InlineData(MotionDriver.Static)]
    [InlineData(MotionDriver.Physics)]
    [InlineData(MotionDriver.Spin)]
    public void AFrameWithAnotherDriverNeedsNoGM(MotionDriver driver)
    {
        var planet = new Planet(GravityField.None, driver);

        Action check = planet.Frames.AssertInvariants;

        check.Should().NotThrow();
    }

    // ---- An orbit without a G·M ------------------------------------------------------------------------------------

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
