using AwesomeAssertions;

namespace Tertius.Slop.Tests;

/// <summary>The measures of source text that a test writes, parsed as the file <c>src/T.cs</c>.</summary>
internal static class Source
{
    public const string FileName = "src/T.cs";

    /// <summary>The measures of every function in <paramref name="text"/>, in the order of the source.</summary>
    public static IReadOnlyList<FunctionMeasure> Functions(string text) =>
        [.. SourceFile.Parse(FileName, text).Functions.Select(function => function.Measure(FileName))];

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
