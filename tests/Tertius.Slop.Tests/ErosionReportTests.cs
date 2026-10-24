using AwesomeAssertions;

namespace Tertius.Slop.Tests;

/// <summary>
/// The mass of a function and the erosion of a codebase (docs/plans/code-quality-tooling.md, decision 16):
/// mass = CC * sqrt(SLOC), and the erosion is the share of the mass in functions with CC above 10.
/// </summary>
public sealed class ErosionReportTests
{
    [Fact]
    public void TheMassOfAFunctionIsItsComplexityTimesTheSquareRootOfItsSloc() =>
        Measure(complexity: 3, sloc: 16).Mass.Should().Be(12);

    [Fact]
    public void TheErosionIsTheShareOfTheMassInFunctionsAboveTheComplexityLimit()
    {
        // Masses: 12 * 3 = 36, 2 * 4 = 8 and 4 * 2 = 8, so 36 of 52 is in the one function above the limit.
        ErosionReport report = ErosionReport.Of(
            [Measure(complexity: 12, sloc: 9), Measure(complexity: 2, sloc: 16), Measure(complexity: 4, sloc: 4)],
            sloc: 100, fileCount: 2);

        (report.Erosion, report.TotalMass, report.HighCcMass, report.FunctionCount, report.HighCcFunctionCount)
            .Should().Be((36.0 / 52.0, 52.0, 36.0, 3, 1));
    }

    [Fact]
    public void TheReportCarriesTheSlocAndTheFileCountOfTheWholeCodebase()
    {
        ErosionReport report = ErosionReport.Of([Measure(complexity: 1, sloc: 1)], sloc: 120, fileCount: 7);

        (report.Sloc, report.FileCount).Should().Be((120, 7));
    }

    [Fact]
    public void AFunctionAtExactlyTheComplexityLimitDoesNotErode() =>
        ErosionReport.Of([Measure(complexity: 10, sloc: 4), Measure(complexity: 1, sloc: 4)], sloc: 8, fileCount: 1)
            .Erosion.Should().Be(0, "the limit is strictly greater than 10");

    [Fact]
    public void AFunctionOneAboveTheComplexityLimitErodes() =>
        ErosionReport.Of([Measure(complexity: 11, sloc: 4), Measure(complexity: 1, sloc: 4)], sloc: 8, fileCount: 1)
            .Erosion.Should().Be(22.0 / 24.0);

    [Fact]
    public void ACodebaseWithNoFunctionsHasNoErosion() =>
        ErosionReport.Of([], sloc: 3, fileCount: 1).Erosion.Should().Be(0);

    [Fact]
    public void TheTopFunctionsAreTheTenHeaviestFromHeaviestDown()
    {
        FunctionMeasure[] functions = [.. Enumerable.Range(1, 12).Select(complexity => Measure(complexity, sloc: 4))];

        ErosionReport.Of(functions, sloc: 48, fileCount: 1).TopFunctions.Select(function => function.Complexity)
            .Should().Equal(12, 11, 10, 9, 8, 7, 6, 5, 4, 3);
    }

    [Fact]
    public void FunctionsOfEqualMassAreOrderedByFileThenLine()
    {
        FunctionMeasure[] functions =
        [
            Measure(complexity: 2, sloc: 4) with { File = "src/B.cs", Line = 1 },
            Measure(complexity: 2, sloc: 4) with { File = "src/A.cs", Line = 9 },
            Measure(complexity: 2, sloc: 4) with { File = "src/A.cs", Line = 3 }
        ];

        ErosionReport.Of(functions, sloc: 12, fileCount: 2).TopFunctions
            .Select(function => (function.File, function.Line))
            .Should().Equal(("src/A.cs", 3), ("src/A.cs", 9), ("src/B.cs", 1));
    }

    private static FunctionMeasure Measure(int complexity, int sloc) =>
        new() { File = "src/T.cs", Line = 1, Name = "C.F", Complexity = complexity, Sloc = sloc };
}
