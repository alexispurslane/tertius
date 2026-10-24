using AwesomeAssertions;

namespace Tertius.Slop.Tests;

/// <summary>
/// The clone lines of src, from jscpd with the settings of the commit report (docs/plans/code-quality-tooling.md,
/// decision 17). The tests that run jscpd need it on the search path, as the dev shell has it.
/// </summary>
public sealed class ClonesTests : IDisposable
{
    // A method of more than 70 tokens, the least that jscpd reports with the settings of the commit report.
    private const string Method = """
            public int Sum(int first, int second, int third)
            {
                int total = first * 3 + second * 5 + third * 7;
                total = total + first * second * third + 11;
                total = total - first - second - third + 13;
                total = total * 2 + first + second + third + 17;
                return total + first + second + third + 19;
            }
        """;

    private readonly string root = Directory.CreateTempSubdirectory("tertius-slop-").FullName;

    public void Dispose() => Directory.Delete(root, recursive: true);

    [Fact]
    public void EachFragmentOfAReportGivesItsLinesUnderSrc()
    {
        var clones = Clones.FromReport(VerbosityReportTests.Report(("Tertius.Core/A.cs", 3, 5), ("B.cs", 7, 7)));

        clones.LinesIn("src/Tertius.Core/A.cs").Should().BeEquivalentTo([3, 4, 5]);
        clones.LinesIn("src/B.cs").Should().BeEquivalentTo([7]);
    }

    [Fact]
    public void AFileWithoutClonesHasNoCloneLines() =>
        Clones.FromReport(VerbosityReportTests.Report()).LinesIn("src/A.cs").Should().BeEmpty();

    [Fact]
    public void JscpdFindsTheSameMethodInTwoFiles()
    {
        Write("src/A.cs", $"class A\n{{\n{Method}\n}}\n");
        Write("src/B.cs", $"class B\n{{\n{Method}\n}}\n");

        var clones = Clones.Find(root, "jscpd");

        clones.LinesIn("src/A.cs").Should().NotBeEmpty();
        clones.LinesIn("src/B.cs").Should().NotBeEmpty();
    }

    [Fact]
    public void AJscpdThatFailsFailsTheClones()
    {
        Write("src/A.cs", "class A { }");

        // false is a program that exits with code 1, whatever its arguments.
        Action find = () => Clones.Find(root, "false");

        find.Should().Throw<InvalidOperationException>().WithMessage("*exit code 1*");
    }

    [Fact]
    public void AMissingJscpdFailsWithAMessageThatNamesTheDevShell()
    {
        Write("src/A.cs", "class A { }");

        Action find = () => Clones.Find(root, "jscpd-that-is-not-installed");

        find.Should().Throw<JscpdMissingException>().WithMessage("*jscpd*nix develop*");
    }

    private void Write(string path, string text)
    {
        string full = Path.Combine(root, path);
        Directory.CreateDirectory(Path.GetDirectoryName(full) ?? root);
        File.WriteAllText(full, text);
    }
}
