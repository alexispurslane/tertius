using Tertius.Demo;
using StationExit = Tertius.Demo.DemoScenes.StationExit;

namespace Tertius.Host.Tests;

/// <summary>
/// The chute's friction law, <see cref="Chute.Friction"/> (docs/plans/blue-danube-demo.md, "The exit", "The slide"), with
/// no World: the friction opposes the slide along the back plate, by the coefficient times the press of the free
/// acceleration into the plate, only while the person touches the plate between the rails, and never by more than the
/// slide in one step. Every place and velocity is in the station's axes, which spin about +Y.
/// </summary>
public sealed class ChuteFrictionTests
{
    private static Chute Chute => StationExit.Chute;

    /// <summary>The station's rate, in rad/s.</summary>
    private const double Rate = 0.31321;

    private static readonly Double3 Omega = new(0, Rate, 0);

    /// <summary>The person's radius, in meters.</summary>
    private const double Radius = 0.3;

    private const double Dt = 1.0 / 60;

    /// <summary>The middle of the trough across it, where the person's center lies on the plate: 0.3 m off its face.</summary>
    private static double OnThePlate => Chute.BackFace + Radius;

    private static Double3 Friction(Double3 position, Double3 velocity) => Chute.Friction(position, velocity, Omega, Radius, Dt);

    /// <summary>
    /// A person who slides down the spoke at 8 m/s, 50 m from the axis, gets the coefficient of the spoke, 1.5, times the
    /// press, up the spoke. The press is the Coriolis term 2 ω v plus the spin gravity's part into the plate, ω² times the
    /// center's distance from the plane z = 0.
    /// </summary>
    [Fact]
    public void TheFrictionOpposesTheSlideByTheCoefficientTimesTheCoriolisPress()
    {
        Double3 friction = Friction(new Double3(-50, 0, OnThePlate), new Double3(-8, 0, 0));
        double press = 2 * Rate * 8 + Rate * Rate * -OnThePlate;
        friction.X.Should().BeApproximately(Chute.Coefficient * press, 1e-9);
        friction.Y.Should().BeApproximately(0, 1e-12);
        friction.Z.Should().BeApproximately(0, 1e-12, "the friction lies along the plate");
    }

    /// <summary>
    /// The friction opposes the whole slide along the plate, with its part across the axis, and takes no part of the
    /// velocity into the plate.
    /// </summary>
    [Fact]
    public void TheFrictionOpposesTheSlideAlongThePlateAndNotTheMotionIntoIt()
    {
        Double3 velocity = new(-6, 0.8, -0.5);
        Double3 friction = Friction(new Double3(-40, 0.2, OnThePlate), velocity);
        Double3 slide = new(-6, 0.8, 0);
        Double3 free = -Double3.Cross(Omega, Double3.Cross(Omega, new Double3(-40, 0.2, OnThePlate))) - 2 * Double3.Cross(Omega, velocity);
        double press = -free.Z;
        Double3 expected = -Chute.Coefficient * press * slide / slide.Length;
        (friction - expected).Length.Should().BeLessThan(1e-9, $"the friction is {friction}, the law gives {expected}");
    }

    /// <summary>Below the ceiling's face toward the axis, the brake run's coefficient, 6, acts, and above it the spoke's.</summary>
    [Fact]
    public void InTheBrakeRunTheBrakesCoefficientActs()
    {
        double press = 2 * Rate * 3 + Rate * Rate * -OnThePlate;
        Friction(new Double3(Chute.BrakeFrom - 0.01, 0, OnThePlate), new Double3(-3, 0, 0)).X.Should().BeApproximately(Chute.BrakeCoefficient * press, 1e-9);
        Friction(new Double3(Chute.BrakeFrom + 0.01, 0, OnThePlate), new Double3(-3, 0, 0)).X.Should().BeApproximately(Chute.Coefficient * press, 1e-9);
    }

    /// <summary>
    /// The law acts while the center lies within 0.4 m of the plate's face, the person's radius plus
    /// <see cref="Chute.ContactMargin"/>, and not beyond.
    /// </summary>
    [Fact]
    public void APersonOffTheBackPlateGetsNoFriction()
    {
        Friction(new Double3(-50, 0, Chute.BackFace + 0.39), new Double3(-8, 0, 0)).X.Should().BeGreaterThan(0, "the person touches the plate");
        Friction(new Double3(-50, 0, Chute.BackFace + 0.41), new Double3(-8, 0, 0)).Should().Be(Double3.Zero, "the person is off the plate");
    }

    /// <summary>The law acts only between the rails' inner faces.</summary>
    [Fact]
    public void APersonBeyondTheRailsGetsNoFriction()
    {
        Friction(new Double3(-50, Chute.HalfWidth - 0.01, OnThePlate), new Double3(-8, 0, 0)).X.Should().BeGreaterThan(0);
        Friction(new Double3(-50, Chute.HalfWidth + 0.01, OnThePlate), new Double3(-8, 0, 0)).Should().Be(Double3.Zero);
        Friction(new Double3(-50, -Chute.HalfWidth - 0.01, OnThePlate), new Double3(-8, 0, 0)).Should().Be(Double3.Zero);
    }

    /// <summary>The law acts only from the trough's end to the deck.</summary>
    [Fact]
    public void APersonAboveTheDeckOrBelowTheTroughsEndGetsNoFriction()
    {
        Friction(new Double3(Chute.Top - 0.01, 0, OnThePlate), new Double3(-1, 0, 0)).X.Should().BeGreaterThan(0);
        Friction(new Double3(Chute.Top + 0.01, 0, OnThePlate), new Double3(-1, 0, 0)).Should().Be(Double3.Zero);
        Friction(new Double3(Chute.End + 0.01, 0, OnThePlate), new Double3(-3, 0, 0)).X.Should().BeGreaterThan(0);
        Friction(new Double3(Chute.End - 0.01, 0, OnThePlate), new Double3(-3, 0, 0)).Should().Be(Double3.Zero);
    }

    /// <summary>
    /// A person who moves up the spoke has a Coriolis term spinward, away from the plate. A free acceleration away from
    /// the plate presses nothing, so it gives no friction.
    /// </summary>
    [Fact]
    public void AFreeAccelerationAwayFromThePlateGivesNoPress() =>
        Friction(new Double3(-50, 0, OnThePlate), new Double3(3, 0, 0)).Should().Be(Double3.Zero);

    /// <summary>
    /// A slow slide in the brake run gets at most the friction that stops it in one step, |slide| / dt, against it. A
    /// person who does not slide gets none.
    /// </summary>
    [Fact]
    public void TheFrictionNeverTurnsTheSlideAroundInOneStep()
    {
        Double3 slide = new(-0.0006, 0.0008, 0);
        Double3 friction = Friction(new Double3(-97, 0, OnThePlate), slide);
        (friction + slide / Dt).Length.Should().BeLessThan(1e-9, "the friction stops the slide in one step and no more");
        Friction(new Double3(-97, 0, OnThePlate), new Double3(0, 0, -0.2)).Should().Be(Double3.Zero, "a person who does not slide gets no friction");
    }
}

/// <summary>Where the chute holds a person's center, <see cref="Chute.Holds"/>: over its top and in the trough, and nowhere else.</summary>
public sealed class ChuteHoldsTests
{
    private static Chute Chute => StationExit.Chute;

    /// <summary>
    /// The chute holds the center of a person who stands over its top, 0.9 m above the deck, and of one in the trough,
    /// up to its bounds: the rails, its depth from the back plate's face, 1 m above the deck and the trough's end.
    /// </summary>
    [Fact]
    public void TheChuteHoldsACenterOverItsTopAndInTheTrough()
    {
        double middle = Chute.BackFace + Chute.Depth / 2;
        Chute.Holds(new Double3(Chute.Top + 0.9, 0, middle)).Should().BeTrue("a person stands over the chute's top");
        Chute.Holds(new Double3(-50, 0, Chute.BackFace + 0.3)).Should().BeTrue("a person lies in the trough");
        Chute.Holds(new Double3(-50, Chute.HalfWidth - 0.01, middle)).Should().BeTrue();
        Chute.Holds(new Double3(-50, Chute.HalfWidth + 0.01, middle)).Should().BeFalse("beyond a rail");
        Chute.Holds(new Double3(-50, -Chute.HalfWidth - 0.01, middle)).Should().BeFalse("beyond the other rail");
        Chute.Holds(new Double3(-50, 0, Chute.BackFace - 0.01)).Should().BeFalse("behind the back plate's face");
        Chute.Holds(new Double3(-50, 0, Chute.BackFace + Chute.Depth - 0.01)).Should().BeTrue();
        Chute.Holds(new Double3(-50, 0, Chute.BackFace + Chute.Depth + 0.01)).Should().BeFalse("past the chute's depth");
        Chute.Holds(new Double3(Chute.Top + 0.99, 0, middle)).Should().BeTrue();
        Chute.Holds(new Double3(Chute.Top + 1.01, 0, middle)).Should().BeFalse("more than 1 m above the deck");
        Chute.Holds(new Double3(Chute.End + 0.01, 0, middle)).Should().BeTrue();
        Chute.Holds(new Double3(Chute.End - 0.01, 0, middle)).Should().BeFalse("below the trough's end");
    }
}
