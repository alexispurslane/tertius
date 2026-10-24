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

    /// <summary>
    /// What the verbosity rules flag in <paramref name="text"/>, read as the whole of <c>src</c>. A test reads every
    /// name of the text, so that the rules about code that nothing outside <c>src</c> uses skip the made-up names of a
    /// snippet.
    /// </summary>
    public static IReadOnlyList<Finding> Findings(string text) => Findings(text: text, outside: text);

    /// <summary>
    /// What the verbosity rules flag in <paramref name="text"/>, read as the whole of <c>src</c>, with
    /// <paramref name="outside"/> as the one file outside <c>src</c>, such as a test or a script.
    /// </summary>
    public static IReadOnlyList<Finding> Findings(string text, string outside)
    {
        SourceFile file = SourceFile.Parse(FileName, text);
        return [.. file.Findings(RuleContext.Of(new Codebase(files: [file], namesOutside: Codebase.NamesIn(outside))))];
    }

    /// <summary>A codebase of <paramref name="files"/>, every name of which a test also reads.</summary>
    public static Codebase UsedByATest(params SourceFile[] files) =>
        new(files: files,
            namesOutside: Codebase.NamesIn(string.Join('\n', files.Select(file => file.Tree.GetText().ToString()))));

    /// <summary>The ids of the rules that flag <paramref name="text"/>, in the order of the findings.</summary>
    public static IReadOnlyList<string> RulesIn(string text) => [.. Findings(text).Select(finding => finding.Rule)];

    /// <summary>
    /// The ids of the rules that flag <paramref name="text"/>, with <paramref name="outside"/> as the one file outside
    /// <c>src</c>, in the order of the findings.
    /// </summary>
    public static IReadOnlyList<string> RulesIn(string text, string outside) =>
        [.. Findings(text: text, outside: outside).Select(finding => finding.Rule)];

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
