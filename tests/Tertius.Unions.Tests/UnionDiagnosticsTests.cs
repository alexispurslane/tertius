using AwesomeAssertions;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace Tertius.Unions.Tests;

/// <summary>
/// The declarations that the generator refuses, each with its own diagnostic, and a declaration that it accepts. The
/// tests run the generator in process on a compilation of one source file.
/// </summary>
public class UnionDiagnosticsTests
{
    [Fact]
    public void AValidUnionGetsNoDiagnosticAndCompiles()
    {
        (IReadOnlyList<string> ids, IReadOnlyList<Diagnostic> errors) = Run("""
            public readonly record struct A(int X);
            public readonly record struct B(double Y);
            [Tertius.GeneratedUnion(typeof(A), typeof(B))]
            public readonly partial struct U;
            """);

        ids.Should().BeEmpty();
        errors.Should().BeEmpty("the generated members compile");
    }

    [Fact]
    public void AStructThatIsNotPartialIsRefused()
    {
        Run("""
            public readonly record struct A(int X);
            [Tertius.GeneratedUnion(typeof(A))]
            public readonly struct U { }
            """).Ids.Should().Equal("TU1001");
    }

    [Fact]
    public void AClassCaseIsRefused()
    {
        Run("""
            public sealed record A(int X);
            public readonly record struct B(int Y);
            [Tertius.GeneratedUnion(typeof(A), typeof(B))]
            public readonly partial struct U;
            """).Ids.Should().Equal("TU1002");
    }

    [Fact]
    public void ARefStructCaseIsRefused()
    {
        Run("""
            public ref struct A { public int X; }
            public readonly record struct B(int Y);
            [Tertius.GeneratedUnion(typeof(A), typeof(B))]
            public readonly partial struct U;
            """).Ids.Should().Equal("TU1003");
    }

    [Fact]
    public void ACaseNamedTwiceIsRefused()
    {
        Run("""
            public readonly record struct A(int X);
            [Tertius.GeneratedUnion(typeof(A), typeof(A))]
            public readonly partial struct U;
            """).Ids.Should().Equal("TU1004");
    }

    [Fact]
    public void AZeroCaseThatIsNotACaseIsRefused()
    {
        Run("""
            public readonly record struct A(int X);
            public readonly record struct B(int Y);
            [Tertius.GeneratedUnion(typeof(A), ZeroCase = typeof(B))]
            public readonly partial struct U;
            """).Ids.Should().Equal("TU1005");
    }

    [Fact]
    public void AUnionInATypeThatIsNotPartialIsRefused()
    {
        Run("""
            public readonly record struct A(int X);
            public static class Outer
            {
                [Tertius.GeneratedUnion(typeof(A))]
                public readonly partial struct U;
            }
            """).Ids.Should().Equal("TU1006");
    }

    [Fact]
    public void AUnionWithNoCaseIsRefused()
    {
        Run("""
            [Tertius.GeneratedUnion]
            public readonly partial struct U;
            """).Ids.Should().Equal("TU1007");
    }

    [Fact]
    public void UnionsInPartialRecordsAndStructsCompile()
    {
        (IReadOnlyList<string> ids, IReadOnlyList<Diagnostic> errors) = Run("""
            public readonly record struct A(int X);
            public partial record Box { [Tertius.GeneratedUnion(typeof(A))] public readonly partial struct U; }
            public partial record struct Pair { [Tertius.GeneratedUnion(typeof(A))] public readonly partial struct U; }
            public partial struct Plain { [Tertius.GeneratedUnion(typeof(A))] public readonly partial struct U; }
            public partial interface IHolder { [Tertius.GeneratedUnion(typeof(A))] public readonly partial struct U; }
            """);

        ids.Should().BeEmpty();
        errors.Should().BeEmpty();
    }

    [Fact]
    public void TwoCasesWithOneShortNameGetTheirOwnKinds()
    {
        (IReadOnlyList<string> ids, IReadOnlyList<Diagnostic> errors) = Run("""
            namespace N1 { public readonly record struct A(int X); }
            namespace N2 { public readonly record struct A(double Y); }
            [Tertius.GeneratedUnion(typeof(N1.A), typeof(N2.A))]
            public readonly partial struct U;
            public static class Probe
            {
                public static bool Second() => new U(new N2.A(1)).TryGetValue(out N2.A _) && !new U(new N2.A(1)).TryGetValue(out N1.A _);
            }
            """);

        ids.Should().BeEmpty();
        errors.Should().BeEmpty("the second case gets the kind constant KindA2");
    }

    [Fact]
    public void AnUnchangedUnionIsNotWrittenAgain()
    {
        CSharpCompilation compilation = Compilation("""
            public readonly record struct A(int X);
            [Tertius.GeneratedUnion(typeof(A))]
            public readonly partial struct U;
            """);
        GeneratorDriver driver = CSharpGeneratorDriver.Create([new UnionGenerator().AsSourceGenerator()],
            parseOptions: Parse, driverOptions: new GeneratorDriverOptions(IncrementalGeneratorOutputKind.None, true));
        driver = driver.RunGenerators(compilation, TestContext.Current.CancellationToken);

        driver = driver.RunGenerators(
            compilation.AddSyntaxTrees(CSharpSyntaxTree.ParseText("class Other { }", Parse,
                cancellationToken: TestContext.Current.CancellationToken)),
            TestContext.Current.CancellationToken);

        driver.GetRunResult().Results[0].TrackedOutputSteps.SelectMany(step => step.Value)
            .SelectMany(run => run.Outputs).Should().OnlyContain(output => output.Reason == IncrementalStepRunReason.Cached,
                "the model of the union equals the model of the run before");
    }

    private static readonly CSharpParseOptions Parse = new(LanguageVersion.Preview);

    private static CSharpCompilation Compilation(string source)
    {
        string[] platform = ((string)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES")!).Split(Path.PathSeparator);
        IEnumerable<MetadataReference> references = platform
            .Append(typeof(GeneratedUnionAttribute).Assembly.Location)
            .Select(path => MetadataReference.CreateFromFile(path));
        return CSharpCompilation.Create("Probe", [CSharpSyntaxTree.ParseText(source, Parse)], references,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
    }

    /// <summary>
    /// Runs the generator on <paramref name="source"/>. Gives the ids of the generator's diagnostics, and the errors of
    /// the compilation with the generated files.
    /// </summary>
    private static (IReadOnlyList<string> Ids, IReadOnlyList<Diagnostic> Errors) Run(string source)
    {
        CSharpCompilation compilation = Compilation(source);
        GeneratorDriver driver = CSharpGeneratorDriver.Create(new UnionGenerator()).WithUpdatedParseOptions(Parse);
        driver.RunGeneratorsAndUpdateCompilation(compilation, out Compilation output,
            out System.Collections.Immutable.ImmutableArray<Diagnostic> diagnostics,
            TestContext.Current.CancellationToken);
        return ([.. diagnostics.Select(d => d.Id)],
            [.. output.GetDiagnostics(TestContext.Current.CancellationToken).Where(d => d.Severity == DiagnosticSeverity.Error)]);
    }
}
