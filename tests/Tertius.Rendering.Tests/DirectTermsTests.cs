namespace Tertius.Rendering.Tests;

/// <summary>
/// The standard response of one light in two parts, the diffuse part and the specular part, as the scatter of skin needs
/// them apart (docs/plans/skin-and-hair.md, "The light of each model").
/// </summary>
public sealed class DirectTermsTests
{
    private static readonly MaterialSample Plastic = new(
        BaseColor: new(0.8, 0.45, 0.3),
        Metallic: 0,
        Roughness: 0.4,
        Reflectance: 0.5
    );

    private static readonly Double3 ToView = new Double3(0.3, -0.2, 0.9).Normalized();
    private static readonly Double3 ToLight = new Double3(-0.5, 0.4, 0.7).Normalized();

    private static void ShouldBeNear(Double3 actual, Double3 expected, double tolerance, string because)
    {
        actual.X.Should().BeApproximately(expected.X, tolerance, because);
        actual.Y.Should().BeApproximately(expected.Y, tolerance, because);
        actual.Z.Should().BeApproximately(expected.Z, tolerance, because);
    }

    [Fact]
    public void TheTwoPartsOfTheStandardResponseSumToTheBrdf()
    {
        DirectTerms terms = Plastic.ReflectedTerms(
            normal: Double3.UnitZ,
            toView: ToView,
            toLight: ToLight,
            tables: BrdfTables.Shared
        );

        ShouldBeNear(
            actual: terms.Diffuse + terms.Specular,
            expected: Plastic.Reflected(
                normal: Double3.UnitZ,
                toView: ToView,
                toLight: ToLight,
                tables: BrdfTables.Shared
            ),
            tolerance: 1e-15,
            because: "the split moves no light between the parts and loses none"
        );
    }

    [Fact]
    public void TheDiffusePartIsTheLambertTermAlone()
    {
        DirectTerms terms = Plastic.ReflectedTerms(
            normal: Double3.UnitZ,
            toView: ToView,
            toLight: ToLight,
            tables: BrdfTables.Shared
        );

        ShouldBeNear(
            actual: terms.Diffuse,
            expected: Plastic.DiffuseColor / Math.PI * ToLight.Z,
            tolerance: 1e-15,
            because: "the diffuse part is c_diff / π · (n·l), and the scatter of skin blurs it alone"
        );
    }

    [Fact]
    public void ALightBehindTheSurfaceGivesNeitherPart()
    {
        DirectTerms terms = Plastic.ReflectedTerms(
            normal: Double3.UnitZ,
            toView: ToView,
            toLight: -ToLight,
            tables: BrdfTables.Shared
        );

        terms.Should().Be(default(DirectTerms));
    }
}
