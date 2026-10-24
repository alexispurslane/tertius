using AwesomeAssertions;

namespace Tertius.Slop.Tests;

/// <summary>
/// Which declarations are functions, and how a function is named and placed (docs/plans/code-quality-tooling.md,
/// decision 16).
/// </summary>
public class FunctionTests
{
    [Fact]
    public void EveryKindOfFunctionWithABodyIsFound() =>
        Source.Functions("""
                         class C
                         {
                             public C() { }
                             ~C() { }
                             void Method() { }
                             int Arrow() => 1;
                             public static C operator +(C a, C b) => a;
                             public static implicit operator int(C c) => 0;
                             int Property { get { return 1; } set { } }
                             int Init { init => field = value; }
                             int Expression => 1;
                             int this[int i] { get => i; }
                             int this[string s] => 0;
                             event Action Changed { add { } remove { } }
                         }
                         """).Select(function => function.Name).Should().Equal(
            "C.C", "C.~C", "C.Method", "C.Arrow", "C.operator +", "C.implicit operator int", "C.Property.get",
            "C.Property.set", "C.Init.init", "C.Expression", "C.this[].get", "C.this[]", "C.Changed.add",
            "C.Changed.remove");

    [Fact]
    public void ADeclarationWithoutABodyIsNotAFunction() =>
        Source.Functions("""
                         interface I { void M(); int P { get; } int this[int i] { get; } }
                         abstract class A
                         {
                             public abstract void M();
                             public int Auto { get; set; }
                             public int Initialized { get; } = 1;
                             public partial void Later();
                             [System.Runtime.InteropServices.DllImport("x")] static extern void Native();
                         }
                         record R(int X);
                         """).Should().BeEmpty();

    [Fact]
    public void AnExternLocalFunctionIsNotAFunction() =>
        Source.Functions("""
                         class C
                         {
                             void F()
                             {
                                 static extern void Native();
                             }
                         }
                         """).Select(function => function.Name).Should().Equal("C.F");

    [Fact]
    public void AFunctionIsNamedAfterTheTypesThatHoldIt() =>
        Source.Functions("""
                         namespace N;
                         class Outer
                         {
                             struct Inner
                             {
                                 void F() { }
                             }
                         }
                         """).Should().ContainSingle().Which.Name.Should().Be("Outer.Inner.F",
            "the namespace adds nothing that the file path does not already say");

    [Fact]
    public void AFunctionInAnExtensionBlockIsNamedAfterTheExtendedType() =>
        Source.Functions("""
                         static class Starts
                         {
                             extension(Query)
                             {
                                 public static Query All() => default;
                             }
                         }
                         """).Should().ContainSingle().Which.Name.Should().Be("Starts.extension(Query).All");

    [Fact]
    public void TheFunctionsOfCSharp15UnionsAndClosedRecordsAreFound() =>
        Source.Functions("""
                         public union Target(A, B)
                         {
                             public override string ToString() => Value?.ToString() ?? "none";
                         }
                         public closed record Shape
                         {
                             public double Size() => 1;
                         }
                         """).Select(function => function.Name).Should().Equal("Target.ToString", "Shape.Size");

    [Fact]
    public void TheLineOfAFunctionIsTheLineOfItsNameAndNotOfItsAttributes() =>
        Source.Functions("""
                         class C
                         {
                             [Obsolete]
                             public void F() { }
                         }
                         """).Should().ContainSingle().Which.Line.Should().Be(4);

    [Fact]
    public void TheLineOfAnAccessorIsTheLineOfItsKeyword() =>
        Source.Functions("""
                         class C
                         {
                             int P
                             {
                                 get => 1;
                             }
                         }
                         """).Should().ContainSingle().Which.Line.Should().Be(5);

    [Fact]
    public void AMeasureCarriesTheFileThatItWasTakenFrom() =>
        Source.Functions("class C { void F() { } }").Should().ContainSingle().Which.File.Should().Be(Source.FileName);
}
