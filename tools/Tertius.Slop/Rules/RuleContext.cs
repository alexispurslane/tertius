using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Tertius.Slop.Rules;

/// <summary>
/// What a rule may know about the whole codebase beyond the node in front of it: where each simple name is used. The
/// low-use-short-function rule reads it to count the call sites of a method. A name is matched by its text only, so
/// two methods with one name share their uses, which makes the rule flag less, never more.
/// </summary>
public sealed class RuleContext(ILookup<string, SimpleNameSyntax> uses)
{
    /// <summary>The context of <paramref name="files"/>, read in parallel.</summary>
    public static RuleContext Of(IEnumerable<SourceFile> files) =>
        new(files.AsParallel()
            .SelectMany(file => file.Tree.GetRoot().DescendantNodes().OfType<SimpleNameSyntax>())
            .ToLookup(name => name.Identifier.ValueText, StringComparer.Ordinal));

    /// <summary>Every simple name in the codebase that reads <paramref name="name"/>, such as a call of a method.</summary>
    public IEnumerable<SimpleNameSyntax> UsesOf(string name) => uses[name];
}
