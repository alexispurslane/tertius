using System.Numerics;
using System.Reflection;
using AwesomeAssertions;

namespace Tertius.Core.Tests;

/// <summary>
/// A gravity field is one of two cases or neither: <see cref="UniformGravity"/> (an acceleration, in a region or
/// everywhere), <see cref="PointGravity"/> (a G·M), or the empty field, which is no gravity. The field holds only the
/// values of its own case, so no field can be a uniform field that also carries a G·M.
/// </summary>
public class GravityFieldTests
{
    private const double EarthMu = 3.986004418e14;
    private static readonly Vector3 Down = new(0, -9.81f, 0);
    private static readonly BoundingBox Cabin = new(new Vector3(-2, -1, -3), new Vector3(2, 1, 3));

    // ---- Holding a case and reading it back -------------------------------------------------------------------------

    [Fact]
    public void AUniformFieldReadsBackAsItsUniformCaseWithItsRegion()
    {
        GravityField field = GravityField.Uniform(Down, Cabin);

        field.HasValue.Should().BeTrue();
        field.TryGetValue(out UniformGravity uniform).Should().BeTrue();
        uniform.Acceleration.Should().Be(Down);
        uniform.Region.Should().Be(Cabin);
        (field is UniformGravity { Acceleration: var acceleration } ? acceleration : default).Should().Be(Down, "a pattern match reads the same case");
    }

    [Fact]
    public void AUniformFieldWithoutARegionReadsBackWithNoRegion()
    {
        GravityField field = GravityField.Uniform(Down);

        field.TryGetValue(out UniformGravity uniform).Should().BeTrue();
        uniform.Region.Should().BeNull("the field holds everywhere");
    }

    [Fact]
    public void APointFieldReadsBackAsItsPointCase()
    {
        GravityField field = GravityField.Point(EarthMu);

        field.HasValue.Should().BeTrue();
        field.TryGetValue(out PointGravity point).Should().BeTrue();
        point.Mu.Should().Be(EarthMu);
        (field is PointGravity { Mu: var mu } ? mu : -1).Should().Be(EarthMu, "a pattern match reads the same case");
    }

    [Fact]
    public void AFieldBuiltFromACaseIsTheFieldThatTheFactoryBuilds()
    {
        GravityField viaConstructor = new(new UniformGravity(Down, Cabin));
        GravityField viaConversion = new PointGravity(EarthMu);

        (viaConstructor == GravityField.Uniform(Down, Cabin)).Should().BeTrue();
        (viaConversion == GravityField.Point(EarthMu)).Should().BeTrue();
    }

    [Fact]
    public void AUniformFieldHoldsNothingOfThePointCase()
    {
        GravityField field = GravityField.Uniform(Down, Cabin);

        field.TryGetValue(out PointGravity point).Should().BeFalse();
        point.Should().Be(default(PointGravity), "the view of the other case is empty, not a leftover");
        (field is PointGravity).Should().BeFalse();
    }

    [Fact]
    public void APointFieldHoldsNothingOfTheUniformCase()
    {
        GravityField field = GravityField.Point(EarthMu);

        field.TryGetValue(out UniformGravity uniform).Should().BeFalse();
        uniform.Should().Be(default(UniformGravity), "the view of the other case is empty, not a leftover");
        (field is UniformGravity).Should().BeFalse();
    }

    [Fact]
    public void TheEmptyFieldHoldsNoCase()
    {
        GravityField none = GravityField.None;

        none.HasValue.Should().BeFalse();
        none.TryGetValue(out UniformGravity uniform).Should().BeFalse();
        none.TryGetValue(out PointGravity point).Should().BeFalse();
        uniform.Should().Be(default(UniformGravity));
        point.Should().Be(default(PointGravity));
        none.Value.Should().BeNull();
        (none == default).Should().BeTrue("the empty field is the all-zero default, so a frame made without a field has none");
    }

    [Fact]
    public void AFieldOfZeroStrengthIsStillAFieldAndNotTheEmptyField()
    {
        GravityField.Uniform(Vector3.Zero).HasValue.Should().BeTrue();
        GravityField.Point(0).HasValue.Should().BeTrue();
        (GravityField.Uniform(Vector3.Zero) == GravityField.None).Should().BeFalse();
        (GravityField.Point(0) == GravityField.None).Should().BeFalse();
    }

    [Fact]
    public void ValueBoxesTheCaseForDebugging()
    {
        GravityField.Uniform(Down, Cabin).Value.Should().Be(new UniformGravity(Down, Cabin));
        GravityField.Point(EarthMu).Value.Should().Be(new PointGravity(EarthMu));
    }

    [Fact]
    public void ASwitchOverTheCasesTellsThemApart()
    {
        static string Describe(GravityField field) => field switch
        {
            UniformGravity { Region: null } => "uniform everywhere",
            UniformGravity => "uniform in a region",
            PointGravity => "point",
            _ => "none",
        };

        Describe(GravityField.Uniform(Down)).Should().Be("uniform everywhere");
        Describe(GravityField.Uniform(Down, Cabin)).Should().Be("uniform in a region");
        Describe(GravityField.Point(EarthMu)).Should().Be("point");
        Describe(GravityField.None).Should().Be("none");
    }

    // ---- Equality ---------------------------------------------------------------------------------------------------

    public static TheoryData<string, GravityField, GravityField> EqualFields => new()
    {
        { "two empty fields", GravityField.None, default },
        { "two uniform fields of the same acceleration", GravityField.Uniform(Down), GravityField.Uniform(Down) },
        { "two uniform fields of the same acceleration and region", GravityField.Uniform(Down, Cabin), GravityField.Uniform(Down, Cabin) },
        { "two point fields of the same G·M", GravityField.Point(EarthMu), GravityField.Point(EarthMu) },
    };

    public static TheoryData<string, GravityField, GravityField> UnequalFields => new()
    {
        { "uniform fields of different accelerations", GravityField.Uniform(Down), GravityField.Uniform(2 * Down) },
        { "a uniform field with a region and one without", GravityField.Uniform(Down, Cabin), GravityField.Uniform(Down) },
        { "uniform fields of different regions", GravityField.Uniform(Down, Cabin), GravityField.Uniform(Down, new BoundingBox(Vector3.Zero, Vector3.One)) },
        { "point fields of different G·M", GravityField.Point(EarthMu), GravityField.Point(2 * EarthMu) },
        { "a uniform field of zero and the empty field", GravityField.Uniform(Vector3.Zero), GravityField.None },
        { "a point field of zero and the empty field", GravityField.Point(0), GravityField.None },
        { "a uniform field of zero and a point field of zero", GravityField.Uniform(Vector3.Zero), GravityField.Point(0) },
    };

    [Theory]
    [MemberData(nameof(EqualFields))]
    public void FieldsOfTheSameCaseAndValuesAreEqualAndHashTheSame(string why, GravityField a, GravityField b)
    {
        (a == b).Should().BeTrue(why);
        (a != b).Should().BeFalse(why);
        a.Equals(b).Should().BeTrue(why);
        a.Equals((object)b).Should().BeTrue(why);
        a.GetHashCode().Should().Be(b.GetHashCode(), why);
    }

    [Theory]
    [MemberData(nameof(UnequalFields))]
    public void FieldsThatDifferInCaseOrValueAreNotEqual(string why, GravityField a, GravityField b)
    {
        (a == b).Should().BeFalse(why);
        (a != b).Should().BeTrue(why);
        a.Equals(b).Should().BeFalse(why);
        b.Equals(a).Should().BeFalse(why);
    }

    // ---- What no field can hold -------------------------------------------------------------------------------------

    [Fact]
    public void NoPublicMemberOfTheUnionExposesTheValuesOfOneCase()
    {
        // The values of the cases are reachable only through TryGetValue and a pattern match, which name the case. A
        // public property or field of the union could be read whichever case the field holds.
        const BindingFlags publicInstance = BindingFlags.Public | BindingFlags.Instance;
        Type union = typeof(GravityField);

        union.GetFields(publicInstance).Should().BeEmpty();
        union.GetProperties(publicInstance).Select(property => property.PropertyType).Should().OnlyContain(type => type == typeof(bool) || type == typeof(object));
        union.GetConstructors().Select(constructor => constructor.GetParameters().Single().ParameterType)
            .Should().BeEquivalentTo([typeof(UniformGravity), typeof(PointGravity)], "a field is made from one case and nothing else");
    }

    // ---- Validity and orbits ----------------------------------------------------------------------------------------

    public static TheoryData<string, GravityField> ValidFields => new()
    {
        { "no gravity", GravityField.None },
        { "a uniform field", GravityField.Uniform(Down) },
        { "a uniform field of zero", GravityField.Uniform(Vector3.Zero) },
        { "a uniform field in a region", GravityField.Uniform(Down, Cabin) },
        { "a point field", GravityField.Point(EarthMu) },
        { "a point field of G·M 0", GravityField.Point(0) },
    };

    public static TheoryData<string, GravityField> InvalidFields => new()
    {
        { "a uniform field with a NaN component", GravityField.Uniform(new Vector3(0, float.NaN, 0)) },
        { "a uniform field with an infinite component", GravityField.Uniform(new Vector3(float.PositiveInfinity, 0, 0)) },
        { "a uniform field with a NaN corner of the region", GravityField.Uniform(Down, new BoundingBox(new Vector3(float.NaN), Vector3.One)) },
        { "a uniform field with an infinite corner of the region", GravityField.Uniform(Down, new BoundingBox(Vector3.Zero, new Vector3(0, float.PositiveInfinity, 0))) },
        { "a point field with a NaN G·M", GravityField.Point(double.NaN) },
        { "a point field with an infinite G·M", GravityField.Point(double.PositiveInfinity) },
        { "a point field with a negative G·M", GravityField.Point(-EarthMu) },
    };

    [Theory]
    [MemberData(nameof(ValidFields))]
    public void AFieldThatPhysicsCanApplyIsValid(string why, GravityField field)
    {
        field.IsValid.Should().BeTrue(why);
    }

    [Theory]
    [MemberData(nameof(InvalidFields))]
    public void AFieldThatWouldGiveABodyAForceThatIsNotFiniteIsNotValid(string why, GravityField field)
    {
        field.IsValid.Should().BeFalse(why);
    }

    [Theory]
    [InlineData(1.0, true)]
    [InlineData(EarthMu, true)]
    [InlineData(double.Epsilon, true)]
    [InlineData(0.0, false)]
    [InlineData(-1.0, false)]
    [InlineData(double.NaN, false)]
    [InlineData(double.PositiveInfinity, false)]
    public void OnlyAPointFieldOfAFinitePositiveGMCanBeOrbited(double mu, bool expected)
    {
        GravityField.Point(mu).CanBeOrbited.Should().Be(expected);
    }

    [Fact]
    public void NeitherAUniformFieldNorTheEmptyFieldCanBeOrbited()
    {
        GravityField.Uniform(Down).CanBeOrbited.Should().BeFalse();
        GravityField.Uniform(Down, Cabin).CanBeOrbited.Should().BeFalse();
        GravityField.None.CanBeOrbited.Should().BeFalse();
    }

    // ---- The acceleration at a point --------------------------------------------------------------------------------

    [Fact]
    public void TheEmptyFieldPullsNowhere()
    {
        GravityField.None.At(new Double3(3, 4, 5)).Should().Be(Vector3.Zero);
    }

    [Fact]
    public void AUniformFieldAcceleratesEverywhereInItsFrameWhenItHasNoRegion()
    {
        GravityField.Uniform(Down).At(new Double3(1e9, -1e9, 5)).Should().Be(Down);
    }

    [Fact]
    public void AUniformFieldHoldsOnTheEdgeOfItsRegionAndNotBeyondIt()
    {
        GravityField field = GravityField.Uniform(Down, Cabin);

        field.At(new Double3(2, 1, 3)).Should().Be(Down, "the region includes its edge");
        field.At(new Double3(2.01, 0, 0)).Should().Be(Vector3.Zero);
        field.At(new Double3(0, -1.01, 0)).Should().Be(Vector3.Zero);
    }

    [Fact]
    public void APointFieldPullsTowardTheOriginWithAnInverseSquare()
    {
        GravityField field = GravityField.Point(100);

        field.At(new Double3(10, 0, 0)).Should().Be(new Vector3(-1, 0, 0));
        field.At(new Double3(0, 0, -5)).Should().Be(new Vector3(0, 0, 4));
    }

    [Fact]
    public void APointFieldPullsNothingAtItsOwnCenterInsteadOfDividingByZero()
    {
        GravityField.Point(100).At(Double3.Zero).Should().Be(Vector3.Zero);
        GravityField.Point(100).At(new Double3(0.0005, 0, 0)).Should().Be(Vector3.Zero, "closer than a millimeter, the pull is zero");
    }
}
