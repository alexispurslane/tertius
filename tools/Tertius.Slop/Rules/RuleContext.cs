using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Tertius.Slop.Rules;

/// <summary>
/// What a rule may know about the whole codebase beyond the node in front of it: where each simple name is used in
/// <c>src</c>, which names a string in <c>src</c> holds, and which names the files outside <c>src</c> hold. A name is
/// matched by its text only, so two members with one name share their uses. That makes a rule about a use flag less,
/// never more.
/// </summary>
public sealed class RuleContext
{
    private readonly ILookup<string, SimpleNameSyntax> uses;
    private readonly IReadOnlySet<string> namesInStrings;
    private readonly IReadOnlySet<string> namesOutside;

    private RuleContext(ILookup<string, SimpleNameSyntax> uses, IReadOnlySet<string> namesInStrings,
        IReadOnlySet<string> namesOutside)
    {
        this.uses = uses;
        this.namesInStrings = namesInStrings;
        this.namesOutside = namesOutside;
    }

    /// <summary>The context of <paramref name="codebase"/>, read in parallel.</summary>
    public static RuleContext Of(Codebase codebase) =>
        new(uses: codebase.Files.AsParallel()
                .SelectMany(file => file.Tree.GetRoot().DescendantNodes().OfType<SimpleNameSyntax>())
                .ToLookup(name => name.Identifier.ValueText, StringComparer.Ordinal),
            namesInStrings: Codebase.NamesIn(string.Join('\n', codebase.Files.AsParallel().SelectMany(StringsIn))),
            namesOutside: codebase.NamesOutside);

    /// <summary>Every simple name in <c>src</c> that reads <paramref name="name"/>, such as a call of a method.</summary>
    public IEnumerable<SimpleNameSyntax> UsesOf(string name) => uses[name];

    /// <summary>
    /// Whether a string in <c>src</c> holds <paramref name="name"/>, whatever its case, so that reflection can find the
    /// member by it, as a blueprint finds a field.
    /// </summary>
    public bool NamedInAString(string name) => namesInStrings.Contains(name);

    /// <summary>
    /// Whether a file outside <c>src</c> holds <paramref name="name"/>, whatever its case: a test, a benchmark or a
    /// script (<see cref="Codebase.NamesOutside"/>).
    /// </summary>
    public bool UsedOutside(string name) => namesOutside.Contains(name);

    // The text of the string literals of a file, and of the text parts of its interpolated strings.
    private static IEnumerable<string> StringsIn(SourceFile file) =>
        file.Tree.GetRoot().DescendantTokens()
            .Where(token => token.IsKind(SyntaxKind.StringLiteralToken) ||
                            token.IsKind(SyntaxKind.InterpolatedStringTextToken) ||
                            token.IsKind(SyntaxKind.SingleLineRawStringLiteralToken) ||
                            token.IsKind(SyntaxKind.MultiLineRawStringLiteralToken))
            .Select(token => token.ValueText);
}
