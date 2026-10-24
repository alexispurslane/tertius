using AwesomeAssertions;
using Tertius.Slop.Rules;

namespace Tertius.Slop.Tests;

/// <summary>
/// The verbosity rules of the user's about types and about code that nothing uses (docs/plans/code-quality-tooling.md,
/// decision 17): for each rule, code that it flags and a near miss. A test names the file outside <c>src</c> that the
/// rules read for the uses of a name.
/// </summary>
public sealed class TypeRuleTests
{
    private const string Shape = """
                                 interface IShape { double Area(); }
                                 sealed class Circle : IShape { public double Area() => 1; }
                                 """;

    [Fact]
    public void AnInterfaceWithOneImplementationAndNoTestDoubleIsFlagged() =>
        Source.Findings(Shape, outside: "var area = new Circle().Area();").Should().ContainSingle().Which.Should()
            .Be(new Finding { Rule = "single-implementation-interface", File = Source.FileName, Line = 1, EndLine = 1 });

    [Fact]
    public void AnInterfaceWithTwoImplementationsIsNotFlagged() =>
        Source.RulesIn($"{Shape}\nsealed class Square : IShape {{ public double Area() => 2; }}",
            outside: "var area = new Circle().Area() + new Square().Area();").Should().BeEmpty();

    [Fact]
    public void AnInterfaceWithATestDoubleIsNotFlagged() =>
        Source.RulesIn(Shape, outside: "sealed class FakeShape : IShape { public double Area() => new Circle().Area(); }")
            .Should().BeEmpty();

    [Theory]
    [InlineData("""
                sealed class Printer
                {
                    private readonly TextWriter writer;
                    public void Line(string text) => writer.WriteLine("> " + text);
                    public void Flush() { writer.Flush(true); }
                }
                """)]
    [InlineData("""
                sealed class Printer(TextWriter writer)
                {
                    public void Line(string text) => writer.WriteLine("> " + text);
                    public void Flush() { writer.Flush(true); }
                }
                """)]
    public void AClassWithOneFieldWhoseEveryMethodForwardsToItIsFlagged(string type) =>
        Source.RulesIn(type).Should().Equal("forwarding-class");

    [Theory]
    [InlineData("""
                sealed class Printer
                {
                    private readonly TextWriter writer;
                    private int lines;
                    public void Line(string text) => writer.WriteLine("> " + text);
                    public void Flush() { writer.Flush(true); }
                }
                """)]
    [InlineData("""
                sealed class Printer(TextWriter writer)
                {
                    public void Line(string text) => writer.WriteLine("> " + text);
                    public int Count() => Lines(writer) + 1;
                }
                """)]
    public void AClassWithMoreThanOneFieldOrAMethodThatDoesMoreIsNotFlagged(string type) =>
        Source.RulesIn(type).Should().BeEmpty();

    [Theory]
    [InlineData("sealed class ImpostorManager { }")]
    [InlineData("interface IInputHandler { }")]
    [InlineData("readonly record struct MeshInfo(int First, int Count);")]
    [InlineData("sealed class PacketProcessor { }")]
    [InlineData("sealed class SaveService { }")]
    [InlineData("struct RenderData { }")]
    public void ATypeWithAVagueSuffixIsFlagged(string type) =>
        Source.RulesIn(type).Should().Equal("vague-type-name");

    [Theory]
    [InlineData("sealed class Metadata { }")]
    [InlineData("sealed class DataTable { }")]
    [InlineData("sealed class Information { }")]
    public void ATypeWhoseNameOnlyContainsAVagueWordIsNotFlagged(string type) =>
        Source.RulesIn(type).Should().BeEmpty();

    [Fact]
    public void AnInternalMethodThatNothingUsesIsFlagged() =>
        Source.Findings("""
                        static class Ship
                        {
                            public static void Launch() { Fly(); Fly(); }
                            internal static void Fly() { }
                            internal static void Drift() { }
                        }
                        """, outside: "Ship.Launch();").Should().ContainSingle().Which.Should().Be(new Finding
        {
            Rule = "unused-code", File = Source.FileName, Line = 5, EndLine = 5
        });

    [Fact]
    public void AnInternalTypeThatNothingUsesIsFlaggedOverAllItsLines() =>
        Source.Findings("""
                        sealed class Ghost
                        {
                        }
                        """, outside: "").Should().ContainSingle().Which.Should().Be(new Finding
        {
            Rule = "unused-code", File = Source.FileName, Line = 1, EndLine = 3
        });

    [Theory]
    [InlineData("Ship.Drift();")]
    [InlineData("""{ "do": "post", "event": "drift" }""")]
    public void AMemberThatOnlyATestOrAScriptUsesIsNotFlagged(string outside) =>
        Source.RulesIn("""
                       static class Ship
                       {
                           internal static void Drift() { }
                       }
                       """, outside: $"Ship {outside}").Should().BeEmpty();

    [Fact]
    public void AMemberThatAStringInSrcNamesIsNotFlagged() =>
        Source.RulesIn("""
                       sealed class Engine
                       {
                           public float Thrust;
                       }
                       static class Scenes
                       {
                           public static Spec Rocket = new(typeof(Engine), new Dictionary<string, object> { ["Thrust"] = 3f });
                       }
                       """, outside: "Scenes.Rocket").Should().BeEmpty();

    [Theory]
    [InlineData("private static void Drift() { }")]
    [InlineData("static void Drift() { }")]
    [InlineData("public override string ToString() => \"ship\";")]
    [InlineData("void IDisposable.Dispose() { }")]
    [InlineData("public static int Main(string[] args) => 0;")]
    public void APrivateMemberAnOverrideAnInterfaceMemberOrTheEntryPointIsNotFlagged(string member) =>
        Source.RulesIn($"sealed class Ship {{ {member} }}", outside: "Ship").Should().BeEmpty();

    [Fact]
    public void AMethodThatOnlyCallsItselfIsFlagged() =>
        Source.RulesIn("""
                       static class Ship
                       {
                           internal static int Count(int n) => n == 0 ? 0 : Count(n - 1);
                       }
                       """, outside: "Ship").Should().Equal("unused-code");

    [Fact]
    public void ATypeThatAnotherFileOfSrcUsesIsNotFlagged()
    {
        // The use in the other file sits at an offset that falls inside the span of the declaration.
        SourceFile ship = SourceFile.Parse("src/Ship.cs", "sealed class Ship\n{\n    int mass;\n    int crew;\n}\n");
        SourceFile dock = SourceFile.Parse("src/Dock.cs", "class Dock { Ship moored; }");
        RuleContext context = RuleContext.Of(new Codebase(files: [ship, dock], namesOutside: Codebase.NamesIn("")));

        ship.Findings(context).Should().BeEmpty();
    }

    [Theory]
    [InlineData("public static void Check(this int result) { }")]
    [InlineData("extension(int result) { public void Check() { } }")]
    public void AClassOfExtensionMembersIsNotFlaggedForItsOwnName(string member) =>
        Source.RulesIn($"static class ResultChecks {{ {member} }}", outside: "3.Check()").Should().BeEmpty();

    [Fact]
    public void AnAttributeClassThatAnAttributeUsesIsNotFlagged() =>
        Source.RulesIn("""
                       sealed class GlowAttribute : Attribute { }
                       [Glow] sealed class Lamp { }
                       """, outside: "Lamp").Should().BeEmpty();
}
