using AwesomeAssertions;
using Tertius.Slop.Rules;

namespace Tertius.Slop.Tests;

/// <summary>The measures of source text that a test writes, parsed as the file <c>src/T.cs</c>.</summary>
internal static class Source
{
    public const string FileName = "src/T.cs";

    /// <summary>The measures of every function in <paramref name="text"/>, in the order of the source.</summary>
    public static IReadOnlyList<FunctionMeasure> Functions(string text) =>
        [.. SourceFile.Parse(FileName, text).Functions.Select(function => function.Measure(FileName))];

    /// <summary>What the verbosity rules flag in <paramref name="text"/>, read as the whole codebase.</summary>
    public static IReadOnlyList<Finding> Findings(string text)
    {
        SourceFile file = SourceFile.Parse(FileName, text);
        return [.. file.Findings(RuleContext.Of([file]))];
    }

    /// <summary>The ids of the rules that flag <paramref name="text"/>, in the order of the findings.</summary>
    public static IReadOnlyList<string> RulesIn(string text) => [.. Findings(text).Select(finding => finding.Rule)];

    /// <summary>
    /// The ids of the rules that flag a class whose one method <c>F</c>, private and with no parameters, has the body
    /// <paramref name="body"/>.
    /// </summary>
    public static IReadOnlyList<string> RulesInMethod(string body) =>
        RulesIn($$"""
                  class C
                  {
                      int F()
                      {
                          {{body}}
                      }
                  }
                  """);

    /// <summary>The one function of a class whose one method <c>F</c> has the body <paramref name="body"/>.</summary>
    public static FunctionMeasure Method(string body) =>
        Functions($$"""
                    class C
                    {
                        void F()
                        {
                            {{body}}
                        }
                    }
                    """).Should().ContainSingle().Subject;
}
