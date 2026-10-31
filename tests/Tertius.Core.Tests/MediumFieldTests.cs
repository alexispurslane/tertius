using System.Numerics;
using AwesomeAssertions;
using Tertius.Testing;

namespace Tertius.Core.Tests;

/// <summary>
/// A frame's medium is one of two cases or neither: <see cref="UniformMedium"/> (one density, in a region or everywhere),
/// <see cref="PlanetMedium"/> (the air of a planet at the frame's origin), or the empty medium, which is vacuum. These tests
/// pin what each case says about the gas at a place: whether it fills the place, its density there and its drag.
/// </summary>
public sealed class MediumFieldTests
{
    private const double EarthRadius = 6_371_000;
    private const float SeaLevelAir = 1.225f;
    private const float ScaleHeight = 8_500;
    private static readonly BoundingBox Cabin = new(new(-2, -1, -3), new(2, 1, 3));
    private static readonly Double3 InCabin = new(1, 0.5, -2);
    private static readonly Double3 OutsideCabin = new(0, 5, 0);

    [Fact]
    public void NoMediumHasNoDensityAndNoDrag()
    {
        MediumField vacuum = MediumField.None;

        vacuum.HasValue.Should().BeFalse();
        vacuum.Fills(InCabin).Should().BeFalse();
        vacuum.DensityAt(InCabin).Should().Be(0);
        vacuum.Drag.Should().Be(0);
        (vacuum == default).Should().BeTrue("no medium is the all-zero default");
        vacuum.IsValid.Should().BeTrue("vacuum is a medium that the World accepts");
    }

    [Fact]
    public void AUniformMediumHasItsDensityEverywhereOrOnlyInsideItsRegion()
    {
        var everywhere = MediumField.Uniform(SeaLevelAir, 2);
        var cabin = MediumField.Uniform(SeaLevelAir, 2, Cabin);

        everywhere.DensityAt(InCabin).Should().Be(SeaLevelAir);
        everywhere.DensityAt(new(1e7, -3e6, 4)).Should().Be(SeaLevelAir);
        cabin.DensityAt(InCabin).Should().Be(SeaLevelAir);
        cabin.DensityAt(OutsideCabin).Should().Be(0, "outside its region the frame holds vacuum");
        everywhere.Drag.Should().Be(2);
        cabin.Drag.Should().Be(2, "the drag is the medium's, wherever the place");
    }

    [Fact]
    public void AMediumFillsItsRegionOrItsWholeFrameOnlyWhenItsDensityIsAboveZero()
    {
        MediumField
            .Uniform(SeaLevelAir, 2)
            .Fills(OutsideCabin)
            .Should()
            .BeTrue("a uniform medium without a region fills the whole frame");
        MediumField.Uniform(SeaLevelAir, 2, Cabin).Fills(InCabin).Should().BeTrue();
        MediumField.Uniform(SeaLevelAir, 2, Cabin).Fills(OutsideCabin).Should().BeFalse();
        MediumField.Uniform(0, 2).Fills(InCabin).Should().BeFalse("a medium of no density holds no gas");
        MediumField.Uniform(0, 2, Cabin).Fills(InCabin).Should().BeFalse();
        MediumField
            .Planet(radius: EarthRadius, surfaceDensity: SeaLevelAir, scaleHeight: ScaleHeight, drag: 2)
            .Fills(new(0, EarthRadius * 3, 0))
            .Should()
            .BeTrue("a planet medium fills its whole frame, however thin");
        MediumField
            .Planet(radius: EarthRadius, surfaceDensity: 0, scaleHeight: ScaleHeight, drag: 2)
            .Fills(new(0, EarthRadius, 0))
            .Should()
            .BeFalse();
    }

    [Fact]
    public void APlanetMediumThinsByTheFactorEPerScaleHeightAboveItsRadius()
    {
        var air = MediumField.Planet(
            radius: EarthRadius,
            surfaceDensity: SeaLevelAir,
            scaleHeight: ScaleHeight,
            drag: 2
        );
        Double3 up = new Double3(1, 2, -2) / 3;

        air.DensityAt(up * EarthRadius).Should().BeApproximately(SeaLevelAir, 1e-9);
        air.DensityAt(up * (EarthRadius + ScaleHeight)).Should().BeApproximately(SeaLevelAir / Math.E, 1e-9);
        air.DensityAt(up * (EarthRadius + 3 * ScaleHeight)).Should().BeApproximately(SeaLevelAir * Math.Exp(-3), 1e-9);
        air.Drag.Should().Be(2);
    }

    [Fact]
    public void APlanetMediumKeepsItsSurfaceDensityBelowItsRadius()
    {
        var air = MediumField.Planet(
            radius: EarthRadius,
            surfaceDensity: SeaLevelAir,
            scaleHeight: ScaleHeight,
            drag: 2
        );

        air.DensityAt(new(0, EarthRadius - 1000, 0)).Should().Be(SeaLevelAir);
        air.DensityAt(Double3.Zero).Should().Be(SeaLevelAir);
    }

    [Fact]
    public void APlanetMediumsHeightIsTheDistanceAboveItsRadiusAndZeroBelowIt()
    {
        var air = new PlanetMedium(Radius: EarthRadius, SurfaceDensity: SeaLevelAir, ScaleHeight: ScaleHeight, Drag: 2);

        air.HeightAt(EarthRadius + 1000).Should().Be(1000);
        air.HeightAt(EarthRadius).Should().Be(0);
        air.HeightAt(EarthRadius - 1000).Should().Be(0, "below the surface the air is no denser than at it");
        air.HeightAt(0).Should().Be(0);
    }

    [Fact]
    public void AMediumWithANumberThatIsNotFiniteOrIsNegativeIsNotValid()
    {
        MediumField.Uniform(SeaLevelAir, 2, Cabin).IsValid.Should().BeTrue();
        MediumField.Uniform(0, 0).IsValid.Should().BeTrue("no density and no drag are allowed");
        MediumField
            .Planet(radius: EarthRadius, surfaceDensity: SeaLevelAir, scaleHeight: ScaleHeight, drag: 2)
            .IsValid.Should()
            .BeTrue();
        MediumField
            .Planet(radius: 0, surfaceDensity: 0, scaleHeight: ScaleHeight, drag: 0)
            .IsValid.Should()
            .BeTrue("a radius of 0 is allowed");

        MediumField[] invalid =
        [
            MediumField.Uniform(-0.1f, 2),
            MediumField.Uniform(float.NaN, 2),
            MediumField.Uniform(float.PositiveInfinity, 2),
            MediumField.Uniform(SeaLevelAir, -1),
            MediumField.Uniform(SeaLevelAir, float.NaN),
            MediumField.Uniform(SeaLevelAir, float.PositiveInfinity),
            MediumField.Uniform(SeaLevelAir, 2, new BoundingBox(new(float.NaN, 0, 0), Vector3.One)),
            MediumField.Uniform(SeaLevelAir, 2, new BoundingBox(Vector3.Zero, new(0, float.PositiveInfinity, 0))),
            MediumField.Planet(radius: -1, surfaceDensity: SeaLevelAir, scaleHeight: ScaleHeight, drag: 2),
            MediumField.Planet(radius: double.NaN, surfaceDensity: SeaLevelAir, scaleHeight: ScaleHeight, drag: 2),
            MediumField.Planet(
                radius: double.PositiveInfinity,
                surfaceDensity: SeaLevelAir,
                scaleHeight: ScaleHeight,
                drag: 2
            ),
            MediumField.Planet(radius: EarthRadius, surfaceDensity: -1, scaleHeight: ScaleHeight, drag: 2),
            MediumField.Planet(radius: EarthRadius, surfaceDensity: float.NaN, scaleHeight: ScaleHeight, drag: 2),
            MediumField.Planet(
                radius: EarthRadius,
                surfaceDensity: float.PositiveInfinity,
                scaleHeight: ScaleHeight,
                drag: 2
            ),
            MediumField.Planet(radius: EarthRadius, surfaceDensity: SeaLevelAir, scaleHeight: 0, drag: 2),
            MediumField.Planet(radius: EarthRadius, surfaceDensity: SeaLevelAir, scaleHeight: -1, drag: 2),
            MediumField.Planet(radius: EarthRadius, surfaceDensity: SeaLevelAir, scaleHeight: float.NaN, drag: 2),
            MediumField.Planet(
                radius: EarthRadius,
                surfaceDensity: SeaLevelAir,
                scaleHeight: float.PositiveInfinity,
                drag: 2
            ),
            MediumField.Planet(radius: EarthRadius, surfaceDensity: SeaLevelAir, scaleHeight: ScaleHeight, drag: -1),
            MediumField.Planet(
                radius: EarthRadius,
                surfaceDensity: SeaLevelAir,
                scaleHeight: ScaleHeight,
                drag: float.NaN
            ),
            MediumField.Planet(
                radius: EarthRadius,
                surfaceDensity: SeaLevelAir,
                scaleHeight: ScaleHeight,
                drag: float.PositiveInfinity
            ),
        ];
        for (int i = 0; i < invalid.Length; i++)
            invalid[i].IsValid.Should().BeFalse($"medium {i}, {invalid[i]}, holds a number out of its range");
    }

    [Fact]
    public void APlanetsPaintedLookWithinItsRangesIsValid() =>
        MediumField
            .Planet(
                radius: EarthRadius,
                surfaceDensity: SeaLevelAir,
                scaleHeight: ScaleHeight,
                drag: 2,
                skyRadiance: new(0, 1e30f, 0.5f),
                haze: new(new(0, 2, 0), Distance: 1e-3f),
                sunTint: new(0, 1, 0.5f)
            )
            .IsValid.Should()
            .BeTrue("a black or very bright sky, a haze of any finite light and a tint from 0 to 1 are allowed");

    [Fact]
    public void APlanetsLookWithANumberOutOfItsRangeIsNotValid()
    {
        static MediumField Painted(Vector3 sky = default, PlanetHaze? haze = null, Vector3? tint = null) =>
            MediumField.Planet(
                radius: EarthRadius,
                surfaceDensity: SeaLevelAir,
                scaleHeight: ScaleHeight,
                drag: 2,
                skyRadiance: sky,
                haze: haze,
                sunTint: tint
            );

        MediumField[] invalid =
        [
            Painted(sky: new(-0.1f, 0, 0)),
            Painted(sky: new(0, float.NaN, 0)),
            Painted(sky: new(0, 0, float.PositiveInfinity)),
            Painted(haze: new(new(-1, 0, 0), Distance: 100)),
            Painted(haze: new(new(0, float.NaN, 0), Distance: 100)),
            Painted(haze: new(new(0, 0, float.PositiveInfinity), Distance: 100)),
            Painted(haze: new(Vector3.One, Distance: 0)),
            Painted(haze: new(Vector3.One, Distance: -5)),
            Painted(haze: new(Vector3.One, Distance: float.NaN)),
            Painted(haze: new(Vector3.One, Distance: float.PositiveInfinity)),
            Painted(tint: new(-0.1f, 1, 1)),
            Painted(tint: new(1, 1.1f, 1)),
            Painted(tint: new(1, 1, float.NaN)),
        ];
        for (int i = 0; i < invalid.Length; i++)
            invalid[i]
                .IsValid.Should()
                .BeFalse($"medium {i}, {invalid[i]}, holds a number of its look out of its range");
    }

    [Fact]
    public void TheMediumFieldUnionHoldsOneCaseAndMatchesWithoutBoxing()
    {
        var uniformCase = new UniformMedium(SeaLevelAir, 2, Cabin);
        var planetCase = new PlanetMedium(
            Radius: EarthRadius,
            SurfaceDensity: SeaLevelAir,
            ScaleHeight: ScaleHeight,
            Drag: 3
        );
        var uniform = MediumField.Uniform(SeaLevelAir, 2, Cabin);
        var planet = MediumField.Planet(
            radius: EarthRadius,
            surfaceDensity: SeaLevelAir,
            scaleHeight: ScaleHeight,
            drag: 3
        );

        // One case in, the same case out, and no other case answers.
        uniform.TryGetValue(out UniformMedium heldUniform).Should().BeTrue();
        heldUniform.Should().Be(uniformCase);
        uniform.TryGetValue(out PlanetMedium noPlanet).Should().BeFalse();
        noPlanet.Should().Be(default(PlanetMedium), "a view of a case that the medium does not hold is empty");
        planet.TryGetValue(out PlanetMedium heldPlanet).Should().BeTrue();
        heldPlanet.Should().Be(planetCase);
        planet.TryGetValue(out UniformMedium noUniform).Should().BeFalse();
        noUniform.Should().Be(default(UniformMedium));
        MediumField.None.TryGetValue(out UniformMedium _).Should().BeFalse();
        MediumField.None.TryGetValue(out PlanetMedium _).Should().BeFalse();
        uniform.Value.Should().Be(uniformCase);
        planet.Value.Should().Be(planetCase);
        MediumField.None.Value.Should().BeNull();

        // The constructors, the implicit conversions and the factories build the same medium.
        (new MediumField(uniformCase) == uniform)
            .Should()
            .BeTrue();
        (planetCase == planet).Should().BeTrue();
        (uniform == planet).Should().BeFalse();
        (uniform == MediumField.None).Should().BeFalse();
        (MediumField.Uniform(0, 0) == MediumField.None)
            .Should()
            .BeFalse("a medium with a case never equals vacuum, even with zero fields");
        (uniform != MediumField.Uniform(SeaLevelAir, 2.5f, Cabin)).Should().BeTrue();
        uniform.GetHashCode().Should().Be(MediumField.Uniform(SeaLevelAir, 2, Cabin).GetHashCode());
        uniform.Equals((object)planet).Should().BeFalse();
        uniform.ToString().Should().Contain(nameof(UniformMedium));
        planet.ToString().Should().Contain(nameof(PlanetMedium));
        MediumField.None.ToString().Should().Contain("none");

        // A pattern match reads the fields of the case in place. Boxing the case, as Value does, would allocate.
        MediumField[] media = [uniform, planet, default];
        double total = 0;
        foreach (MediumField medium in media)
            total += ReadDrag(medium);
        long allocated = Allocations.Fewest(() =>
        {
            total = 0;
            for (int round = 0; round < 100; round++)
                foreach (MediumField medium in media)
                    total += ReadDrag(medium);
        });

        allocated.Should().Be(0);
        total.Should().Be((2 + 3) * 100, "a uniform medium of drag 2 and a planet medium of drag 3, over 100 rounds");
    }

    private static float ReadDrag(in MediumField medium) =>
        medium switch
        {
            UniformMedium { Drag: var drag } => drag,
            PlanetMedium { Drag: var drag } => drag,
            null => 0,
        };
}
