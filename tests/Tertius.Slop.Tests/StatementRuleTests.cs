using AwesomeAssertions;

namespace Tertius.Slop.Tests;

/// <summary>
/// The verbosity rules about single statements and expressions, ported from the scb-check slop rules
/// (docs/plans/code-quality-tooling.md, decision 17): for each rule, code that it flags and a near miss.
/// </summary>
public sealed class StatementRuleTests
{
    [Theory]
    [InlineData("ys = xs.Select(x => Parse(x));")]
    [InlineData("ys = xs.Select((x) => Parse(x));")]
    [InlineData("ys = xs.Aggregate((a, b) => Add(a, b));")]
    [InlineData("ys = xs.Select(static x => Text.Parse(x));")]
    public void ALambdaThatOnlyPassesItsParametersToAMethodIsFlagged(string body) =>
        Source.RulesInMethod(body).Should().Equal("unnecessary-lambda");

    [Theory]
    [InlineData("ys = xs.Select(x => Parse(x, 1));")]
    [InlineData("ys = xs.Select(x => x.Parse());")]
    [InlineData("ys = xs.Select(x => Parse(y));")]
    [InlineData("ys = xs.Aggregate((a, b) => Add(b, a));")]
    [InlineData("ys = xs.Select(x => x.Of(x));")]
    public void ALambdaThatDoesMoreThanPassItsParametersOnIsNotFlagged(string body) =>
        Source.RulesInMethod(body).Should().BeEmpty();

    [Theory]
    [InlineData("foreach (var x in xs) { Use(x); continue; }")]
    [InlineData("while (a) { Use(a); continue; }")]
    public void AContinueAtTheEndOfALoopBodyIsFlagged(string body) =>
        Source.RulesInMethod(body).Should().Equal("redundant-continue");

    [Fact]
    public void AContinueThatSkipsTheRestOfALoopBodyIsNotFlagged() =>
        Source.RulesInMethod("foreach (var x in xs) { if (x) continue; Use(x); }").Should().BeEmpty();

    [Theory]
    [InlineData("void F() { Use(1); Use(2); return; }")]
    [InlineData("void F() { void Local() { Use(1); Use(2); return; } Local(); Local(); }")]
    public void AReturnAtTheEndOfAFunctionThatReturnsNothingIsFlagged(string function) =>
        Source.RulesIn($"class C {{ {function} }}").Should().Equal("redundant-return");

    [Fact]
    public void AReturnAtTheEndOfAnIfIsNotFlagged() =>
        Source.RulesIn("class C { void F() { if (a) { Use(1); return; } Use(2); } }").Should().BeEmpty();

    [Fact]
    public void TheOnlyConstructorOfAClassWhenItIsPublicEmptyAndWithoutParametersIsFlagged() =>
        Source.RulesIn("class C { public C() { } }").Should().Equal("empty-constructor");

    [Theory]
    [InlineData("class C { public C() { } public C(int x) { Use(x); Use(x); } }")]
    [InlineData("class C { private C() { } }")]
    [InlineData("class C { static C() { } }")]
    [InlineData("class C : B { public C() : base(1) { } }")]
    [InlineData("struct S { public S() { } }")]
    [InlineData("class C { [SetsRequiredMembers] public C() { } }")]
    public void AnEmptyConstructorThatChangesWhatTheTypeCanDoIsNotFlagged(string type) =>
        Source.RulesIn(type).Should().BeEmpty();

    [Theory]
    [InlineData("foreach (var x in xs) s += $\"{x},\";")]
    [InlineData("while (a) { s += \"x\"; }")]
    [InlineData("for (int i = 0; i < n; i++) s += i.ToString();")]
    [InlineData("foreach (var p in parts) line += \" \" + p;")]
    public void AddingAStringToAVariableInALoopIsFlagged(string body) =>
        Source.RulesInMethod(body).Should().Equal("manual-str-join");

    [Theory]
    [InlineData("foreach (var x in xs) n += 1;")]
    [InlineData("s += \"x\";")]
    public void AddingANumberInALoopOrAStringOutsideALoopIsNotFlagged(string body) =>
        Source.RulesInMethod(body).Should().BeEmpty();

    [Theory]
    [InlineData("var (a, b, c, d, e) = t;")]
    [InlineData("(a, b, c, d, e) = t;")]
    public void DeconstructingFiveOrMoreValuesIsFlagged(string body) =>
        Source.RulesInMethod(body).Should().Equal("long-tuple-unpacking");

    [Fact]
    public void DeconstructingFourValuesIsNotFlagged() =>
        Source.RulesInMethod("var (a, b, c, d) = t;").Should().BeEmpty();

    [Theory]
    [InlineData("foreach (var x in xs.ToList()) Use(x);")]
    [InlineData("foreach (var x in xs.Where(Live).ToArray()) Use(x);")]
    public void AListOrArrayMadeOnlyToLoopOverIsFlagged(string body) =>
        Source.RulesInMethod(body).Should().Equal("materialize-to-loop");

    [Theory]
    [InlineData("foreach (var x in xs.Where(Live)) Use(x);")]
    [InlineData("foreach (var x in xs.ToList(capacity)) Use(x);")]
    [InlineData("Use(xs.ToList());")]
    public void ALoopOverAQueryOrAListThatIsKeptIsNotFlagged(string body) =>
        Source.RulesInMethod(body).Should().BeEmpty();

    [Fact]
    public void ARunOfMoreThanTenPlainStatementsWithNoBlankLineIsFlagged() =>
        Source.Findings(Method(Lines(11))).Should().ContainSingle().Which.Should().Be(new Finding
        {
            Rule = "statement-wall", File = Source.FileName, Line = 5, EndLine = 15
        });

    [Fact]
    public void ARunOfTenStatementsIsNotFlagged() =>
        Source.RulesIn(Method(Lines(10))).Should().BeEmpty();

    [Fact]
    public void ABlankLineBreaksARunOfStatements() =>
        Source.RulesIn(Method($"{Lines(6)}\n\n{Lines(6)}")).Should().BeEmpty();

    [Fact]
    public void ABranchBreaksARunOfStatements() =>
        Source.RulesIn(Method($"{Lines(6)}\n        if (a) Use(a);\n{Lines(6)}")).Should().BeEmpty();

    [Theory]
    [InlineData("Console.WriteLine(x);")]
    [InlineData("Console.Error.Write(x);")]
    [InlineData("System.Console.Out.WriteLine(x);")]
    public void ConsoleOutputOutsideTheMainMethodIsFlagged(string body) =>
        Source.RulesInMethod(body).Should().Equal("console-output");

    [Fact]
    public void TheMainMethodThatHandsTheConsoleToTheProgramIsNotFlagged() =>
        Source.RulesIn("""
                       static class Program
                       {
                           public static int Main(string[] args) => Run(args, Console.Out, Console.Error);
                       }
                       """).Should().BeEmpty();

    // A class whose one method has the body text.
    private static string Method(string body) => $$"""
                                                   class C
                                                   {
                                                       void F()
                                                       {
                                                   {{body}}
                                                       }
                                                   }
                                                   """;

    // Lines of plain statements, one per line, with no blank line between.
    private static string Lines(int count) =>
        string.Join('\n', Enumerable.Range(0, count).Select(i => $"        Use({i});"));
}
