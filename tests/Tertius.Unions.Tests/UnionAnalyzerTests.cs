using System.Collections.Immutable;
using AwesomeAssertions;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Diagnostics;

namespace Tertius.Unions.Tests;

/// <summary>
/// The analyzer's rules that steer a closed set to its form (docs/plans/union-tooling.md, "The analyzer"): for each rule,
/// a declaration that it reports and a near miss that it does not. Each test runs the generator first, as a build does,
/// and then the analyzer on the compilation with the generated files.
/// </summary>
public sealed class UnionAnalyzerTests
{
    [Fact]
    public void TheAnalyzerHasFourRulesThatAreWarningsOnByDefault()
    {
        ImmutableArray<DiagnosticDescriptor> rules = new UnionAnalyzer().SupportedDiagnostics;

        rules.Select(rule => rule.Id).Should().Equal(["TU0001", "TU0002", "TU0004", "TU0005"],
            "the generator's TU1002 is the one report of a class case, so TU0003 is not a rule");
        rules.Should().OnlyContain(
            rule => rule.DefaultSeverity == DiagnosticSeverity.Warning && rule.IsEnabledByDefault,
            "a rule is a warning, as a compiler warning is, and TreatWarningsAsErrors makes it an error");
    }

    // TU0001

    [Fact]
    public async Task AHandWrittenUnionStructIsReported() =>
        (await Analyze("""
                       public readonly record struct A(int X);
                       [System.Runtime.CompilerServices.Union]
                       public readonly struct U
                       {
                           public U(A a) => Value = a;
                           public object? Value { get; }
                       }
                       """)).Should().Equal("TU0001");

    [Fact]
    public async Task TheUnionAttributeThatTheGeneratorWritesIsNotReported() =>
        (await Analyze("""
                       public readonly record struct A(int X);
                       [Tertius.GeneratedUnion(typeof(A))]
                       public readonly partial struct U;
                       """)).Should()
        .BeEmpty("the generated partial carries [Union], and the struct is marked [GeneratedUnion]");

    // TU0002

    [Fact]
    public async Task ABuiltInUnionWithAStructCaseIsReported() =>
        (await Analyze("""
                       public sealed record A(int X);
                       public readonly record struct B(int Y);
                       public union U(A, B);
                       """)).Should().Equal("TU0002");

    [Fact]
    public async Task ABuiltInUnionOfClassesIsNotReported() =>
        (await Analyze("""
                       public sealed record A(int X);
                       public sealed record B(int Y);
                       public union U(A, B);
                       """)).Should().BeEmpty("a built-in union holds a class case as one reference, without a box");

    // TU0003: the generator's TU1002 is the one report of a class case.

    [Fact]
    public async Task AClassCaseOfAGeneratedUnionIsReportedOnceByTheGenerator() =>
        (await Analyze("""
                       public sealed record A(int X);
                       public readonly record struct B(int Y);
                       [Tertius.GeneratedUnion(typeof(A), typeof(B))]
                       public readonly partial struct U;
                       """)).Should().Equal("TU1002");

    // TU0004

    [Fact]
    public async Task AnAbstractRecordWhoseSubtypesAreAllSealedIsReported() =>
        (await Analyze("""
                       public abstract record Shape;
                       public sealed record Ball(double Radius) : Shape;
                       public sealed record Box(double Side) : Shape;
                       """)).Should().Equal("TU0004");

    [Fact]
    public async Task AnAbstractClassWhoseSubtypesAreAllSealedIsReported() =>
        (await Analyze("""
                       public abstract class Shape;
                       public sealed class Ball : Shape;
                       """)).Should().Equal("TU0004");

    [Fact]
    public async Task AClosedRecordIsNotReported() =>
        (await Analyze("""
                       public closed record Shape;
                       public sealed record Ball(double Radius) : Shape;
                       """)).Should().BeEmpty("a closed record already makes a switch over it fail at a missing case");

    [Fact]
    public async Task AnAbstractRecordWithAnOpenSubtypeIsNotReported() =>
        (await Analyze("""
                       public abstract record Shape;
                       public sealed record Ball(double Radius) : Shape;
                       public record Box(double Side) : Shape;
                       """)).Should().BeEmpty("the subtype Box is open to more subtypes");

    [Fact]
    public async Task ABaseWithAnAbstractMemberIsNotReported() =>
        (await Analyze("""
                       public abstract class Pass { public abstract void Run(); }
                       public sealed class Draw : Pass { public override void Run() { } }
                       """)).Should().BeEmpty("its subtypes supply behavior through virtual dispatch, an open set");

    [Fact]
    public async Task ABaseWithAVirtualMemberIsNotReported() =>
        (await Analyze("""
                       public abstract record Shape { public virtual double Area => 0; }
                       public sealed record Ball(double Radius) : Shape;
                       """)).Should().BeEmpty("its subtypes supply behavior through virtual dispatch, an open set");

    [Fact]
    public async Task ABaseThatOnlyOverridesAnInheritedMemberIsReported() =>
        (await Analyze("""
                       public abstract record Shape { public override string ToString() => "shape"; }
                       public sealed record Ball(double Radius) : Shape;
                       """)).Should().Equal(["TU0004"], "an override supplies no behavior that the subtypes choose");

    [Fact]
    public async Task AnAbstractRecordWithNoSubtypeIsNotReported() =>
        (await Analyze("""
                       public abstract record Shape;
                       """)).Should().BeEmpty("a base with no subtype names no set");

    [Fact]
    public async Task ASealedSubtypeOfABaseInAnotherAssemblyIsNotReported() =>
        (await Analyze("""
                       public sealed class Reader : System.IO.Stream
                       {
                           public override bool CanRead => true;
                           public override bool CanSeek => false;
                           public override bool CanWrite => false;
                           public override long Length => 0;
                           public override long Position { get => 0; set { } }
                           public override void Flush() { }
                           public override int Read(byte[] buffer, int offset, int count) => 0;
                           public override long Seek(long offset, System.IO.SeekOrigin origin) => 0;
                           public override void SetLength(long value) { }
                           public override void Write(byte[] buffer, int offset, int count) { }
                       }
                       """)).Should().BeEmpty("the base Stream is not declared in this assembly");

    // TU0005

    [Fact]
    public async Task AStructWithAKindEnumBesideOtherFieldsIsReported() =>
        (await Analyze("""
                       public enum ShapeKind { Ball, Box }
                       public struct Shape
                       {
                           public ShapeKind Kind;
                           public double Radius;
                       }
                       """)).Should().Equal("TU0005");

    [Fact]
    public async Task ARecordWithAKindPropertyBesideOtherStateIsReported() =>
        (await Analyze("""
                       public enum ShapeKind { Ball, Box }
                       public sealed record Shape(ShapeKind Kind, double Size);
                       """)).Should().Equal("TU0005");

    [Fact]
    public async Task AKindEnumWithNoOtherFieldIsNotReported() =>
        (await Analyze("""
                       public enum ShapeKind { Ball, Box }
                       public readonly record struct Shape(ShapeKind Kind)
                       {
                           public static readonly Shape Ball = new(ShapeKind.Ball);
                           public bool IsBall => Kind == ShapeKind.Ball;
                       }
                       """)).Should().BeEmpty("nothing beside the kind means something only for some kinds");

    [Fact]
    public async Task AKindThatIsNotAnEnumIsNotReported() =>
        (await Analyze("""
                       public struct Shape
                       {
                           public int Kind;
                           public double Radius;
                       }
                       """)).Should().BeEmpty();

    [Fact]
    public async Task AStaticKindIsNotReported() =>
        (await Analyze("""
                       public enum ShapeKind { Ball, Box }
                       public struct Shape
                       {
                           public static ShapeKind Kind = ShapeKind.Ball;
                           public double Radius;
                       }
                       """)).Should().BeEmpty("a static kind tags no value");

    [Fact]
    public async Task ASuppressedKindIsNotReported() =>
        (await Analyze("""
                       public enum ShapeKind { Ball, Box }
                       [System.Diagnostics.CodeAnalysis.SuppressMessage("Tertius.Unions", "TU0005",
                           Justification = "Every field means something for every kind.")]
                       public struct Shape
                       {
                           public ShapeKind Kind;
                           public double Radius;
                       }
                       """)).Should().BeEmpty();

    /// <summary>
    /// Runs the generator and then the analyzer on <paramref name="source"/>. Gives the ids of the diagnostics of both,
    /// the generator's first.
    /// </summary>
    private static async Task<IReadOnlyList<string>> Analyze(string source)
    {
        CancellationToken cancel = TestContext.Current.CancellationToken;
        GeneratorDriver driver = CSharpGeneratorDriver.Create(new UnionGenerator())
            .WithUpdatedParseOptions(TestCompilation.Parse);
        driver.RunGeneratorsAndUpdateCompilation(compilation: TestCompilation.Of(source),
            outputCompilation: out Compilation output,
            diagnostics: out ImmutableArray<Diagnostic> generated, cancellationToken: cancel);
        ImmutableArray<Diagnostic> analyzed = await output.WithAnalyzers([new UnionAnalyzer()])
            .GetAnalyzerDiagnosticsAsync(cancel);
        return [.. generated.Concat(analyzed).Select(diagnostic => diagnostic.Id)];
    }
}
