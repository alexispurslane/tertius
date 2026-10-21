using System.Numerics;
using AwesomeAssertions;

namespace Tertius.Core.Tests;

/// <summary>
/// A frame's medium is one of two cases or neither: <see cref="UniformMedium"/> (one density, in a region or everywhere),
/// <see cref="PlanetMedium"/> (the air of a planet at the frame's origin), or the empty medium, which is vacuum. These tests
/// pin what each case says about the gas at a place: whether it fills the place, its density there and its drag.
/// </summary>
public class MediumFieldTests
{
    private const double EarthRadius = 6_371_000;
    private const float SeaLevelAir = 1.225f;
    private const float ScaleHeight = 8_500;
    private static readonly BoundingBox Cabin = new(new Vector3(-2, -1, -3), new Vector3(2, 1, 3));
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
        MediumField everywhere = MediumField.Uniform(SeaLevelAir, 2);
        MediumField cabin = MediumField.Uniform(SeaLevelAir, 2, Cabin);

        everywhere.DensityAt(InCabin).Should().Be(SeaLevelAir);
        everywhere.DensityAt(new Double3(1e7, -3e6, 4)).Should().Be(SeaLevelAir);
        cabin.DensityAt(InCabin).Should().Be(SeaLevelAir);
        cabin.DensityAt(OutsideCabin).Should().Be(0, "outside its region the frame holds vacuum");
        everywhere.Drag.Should().Be(2);
        cabin.Drag.Should().Be(2, "the drag is the medium's, wherever the place");
    }

    [Fact]
    public void AMediumFillsItsRegionOrItsWholeFrameOnlyWhenItsDensityIsAboveZero()
    {
        MediumField.Uniform(SeaLevelAir, 2).Fills(OutsideCabin).Should().BeTrue("a uniform medium without a region fills the whole frame");
        MediumField.Uniform(SeaLevelAir, 2, Cabin).Fills(InCabin).Should().BeTrue();
        MediumField.Uniform(SeaLevelAir, 2, Cabin).Fills(OutsideCabin).Should().BeFalse();
        MediumField.Uniform(0, 2).Fills(InCabin).Should().BeFalse("a medium of no density holds no gas");
        MediumField.Uniform(0, 2, Cabin).Fills(InCabin).Should().BeFalse();
        MediumField.Planet(EarthRadius, SeaLevelAir, ScaleHeight, 2).Fills(new Double3(0, EarthRadius * 3, 0))
            .Should().BeTrue("a planet medium fills its whole frame, however thin");
        MediumField.Planet(EarthRadius, 0, ScaleHeight, 2).Fills(new Double3(0, EarthRadius, 0)).Should().BeFalse();
    }

    [Fact]
    public void APlanetMediumThinsByTheFactorEPerScaleHeightAboveItsRadius()
    {
        MediumField air = MediumField.Planet(EarthRadius, SeaLevelAir, ScaleHeight, 2);
        Double3 up = new Double3(1, 2, -2) / 3;

        air.DensityAt(up * EarthRadius).Should().BeApproximately(SeaLevelAir, 1e-9);
        air.DensityAt(up * (EarthRadius + ScaleHeight)).Should().BeApproximately(SeaLevelAir / Math.E, 1e-9);
        air.DensityAt(up * (EarthRadius + 3 * ScaleHeight)).Should().BeApproximately(SeaLevelAir * Math.Exp(-3), 1e-9);
        air.Drag.Should().Be(2);
    }

    [Fact]
    public void APlanetMediumKeepsItsSurfaceDensityBelowItsRadius()
    {
        MediumField air = MediumField.Planet(EarthRadius, SeaLevelAir, ScaleHeight, 2);

        air.DensityAt(new Double3(0, EarthRadius - 1000, 0)).Should().Be(SeaLevelAir);
        air.DensityAt(Double3.Zero).Should().Be(SeaLevelAir);
    }

    [Fact]
    public void AMediumWithANumberThatIsNotFiniteOrIsNegativeIsNotValid()
    {
        MediumField.Uniform(SeaLevelAir, 2, Cabin).IsValid.Should().BeTrue();
        MediumField.Uniform(0, 0).IsValid.Should().BeTrue("no density and no drag are allowed");
        MediumField.Planet(EarthRadius, SeaLevelAir, ScaleHeight, 2).IsValid.Should().BeTrue();
        MediumField.Planet(0, 0, ScaleHeight, 0).IsValid.Should().BeTrue("a radius of 0 is allowed");

        MediumField[] invalid =
        [
            MediumField.Uniform(-0.1f, 2),
            MediumField.Uniform(float.NaN, 2),
            MediumField.Uniform(float.PositiveInfinity, 2),
            MediumField.Uniform(SeaLevelAir, -1),
            MediumField.Uniform(SeaLevelAir, float.NaN),
            MediumField.Uniform(SeaLevelAir, float.PositiveInfinity),
            MediumField.Uniform(SeaLevelAir, 2, new BoundingBox(new Vector3(float.NaN, 0, 0), Vector3.One)),
            MediumField.Uniform(SeaLevelAir, 2, new BoundingBox(Vector3.Zero, new Vector3(0, float.PositiveInfinity, 0))),
            MediumField.Planet(-1, SeaLevelAir, ScaleHeight, 2),
            MediumField.Planet(double.NaN, SeaLevelAir, ScaleHeight, 2),
            MediumField.Planet(double.PositiveInfinity, SeaLevelAir, ScaleHeight, 2),
            MediumField.Planet(EarthRadius, -1, ScaleHeight, 2),
            MediumField.Planet(EarthRadius, float.NaN, ScaleHeight, 2),
            MediumField.Planet(EarthRadius, float.PositiveInfinity, ScaleHeight, 2),
            MediumField.Planet(EarthRadius, SeaLevelAir, 0, 2),
            MediumField.Planet(EarthRadius, SeaLevelAir, -1, 2),
            MediumField.Planet(EarthRadius, SeaLevelAir, float.NaN, 2),
            MediumField.Planet(EarthRadius, SeaLevelAir, float.PositiveInfinity, 2),
            MediumField.Planet(EarthRadius, SeaLevelAir, ScaleHeight, -1),
            MediumField.Planet(EarthRadius, SeaLevelAir, ScaleHeight, float.NaN),
            MediumField.Planet(EarthRadius, SeaLevelAir, ScaleHeight, float.PositiveInfinity),
        ];
        for (int i = 0; i < invalid.Length; i++)
            invalid[i].IsValid.Should().BeFalse($"medium {i}, {invalid[i]}, holds a number out of its range");
    }

    [Fact]
    public void TheMediumFieldUnionHoldsOneCaseAndMatchesWithoutBoxing()
    {
        var uniformCase = new UniformMedium(SeaLevelAir, 2, Cabin);
        var planetCase = new PlanetMedium(EarthRadius, SeaLevelAir, ScaleHeight, 3);
        MediumField uniform = MediumField.Uniform(SeaLevelAir, 2, Cabin);
        MediumField planet = MediumField.Planet(EarthRadius, SeaLevelAir, ScaleHeight, 3);

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
        (new MediumField(uniformCase) == uniform).Should().BeTrue();
        ((MediumField)planetCase == planet).Should().BeTrue();
        (uniform == planet).Should().BeFalse();
        (uniform == MediumField.None).Should().BeFalse();
        (MediumField.Uniform(0, 0) == MediumField.None).Should().BeFalse("a medium with a case never equals vacuum, even with zero fields");
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
        long before = GC.GetAllocatedBytesForCurrentThread();
        for (int round = 0; round < 100; round++)
            foreach (MediumField medium in media)
                total += ReadDrag(medium);
        long allocated = GC.GetAllocatedBytesForCurrentThread() - before;

        allocated.Should().Be(0);
        total.Should().Be((2 + 3) * 101, "a uniform medium of drag 2 and a planet medium of drag 3, over 101 rounds");
    }

    private static float ReadDrag(in MediumField medium) => medium switch
    {
        UniformMedium { Drag: var drag } => drag,
        PlanetMedium { Drag: var drag } => drag,
        _ => 0,
    };
}
