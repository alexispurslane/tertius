using System.Collections.Frozen;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Text;

namespace Tertius.Slop.Rules;

// The verbosity rules of the user's about types, and about code that nothing uses (docs/plans/code-quality-tooling.md,
// decision 17). The rules that need the uses of a name read them from the syntax of src and from the text of the files
// outside src (RuleContext).

/// <summary>An interface with one implementation in <c>src</c> and no test double.</summary>
public sealed class SingleImplementationInterface : IRule
{
    public string Id => "single-implementation-interface";

    public string Message =>
        "An interface with one implementation and no test double: use the class, and add the interface with the second implementation.";

    public string Source => "The user";

    public IEnumerable<TextSpan> Find(SyntaxNode node, RuleContext context)
    {
        if (node is not InterfaceDeclarationSyntax type || type.Modifiers.Any(SyntaxKind.PartialKeyword))
            return [];
        // A test double names the interface in a test, so any name of it outside src keeps it.
        string name = type.Identifier.ValueText;
        int implementations = context.UsesOf(name).Count(use => use.Parent is SimpleBaseTypeSyntax);
        return implementations == 1 && !context.UsedOutside(name) ? [type.Span] : [];
    }
}

/// <summary>A class that holds one part and whose every method forwards a call to it.</summary>
public sealed class ForwardingClass : IRule
{
    public string Id => "forwarding-class";

    public string Message =>
        "A class whose every method forwards to its one field: use the field's type, or give the class behavior.";

    public string Source =>
        "The user; STYLE.md, \"Composition\" (never hold a part only to pass calls through to it)";

    public IEnumerable<TextSpan> Find(SyntaxNode node, RuleContext context)
    {
        if (node is not ClassDeclarationSyntax type || HeldPart(type) is not { } part)
            return [];
        MethodDeclarationSyntax[] methods = [.. type.Members.OfType<MethodDeclarationSyntax>()];
        bool forwards = methods.Length > 0 && methods.All(method => ForwardedTo(method) == part);
        return forwards ? [type.Span] : [];
    }

    // The name of the value that a method's whole body calls a method of, as part.F(...) or this.part.F(...), or null.
    private static string? ForwardedTo(MethodDeclarationSyntax method) => method.SingleExpression switch
    {
        InvocationExpressionSyntax { Expression: MemberAccessExpressionSyntax { Expression: var receiver } } =>
            receiver switch
            {
                IdentifierNameSyntax name => name.Identifier.ValueText,
                MemberAccessExpressionSyntax { Expression: ThisExpressionSyntax, Name: var name } =>
                    name.Identifier.ValueText,
                _ => null
            },
        _ => null
    };

    // The name of the one part that a class holds: its one instance field, or the one parameter of its primary
    // constructor when it has no instance field. Null for another class.
    private static string? HeldPart(ClassDeclarationSyntax type)
    {
        string[] fields =
        [
            .. type.Members.OfType<FieldDeclarationSyntax>()
                .Where(field => !field.Modifiers.Any(modifier =>
                    modifier.Kind() is SyntaxKind.StaticKeyword or SyntaxKind.ConstKeyword))
                .SelectMany(field => field.Declaration.Variables)
                .Select(variable => variable.Identifier.ValueText)
        ];
        string[] parameters =
            [.. type.ParameterList?.Parameters.Select(parameter => parameter.Identifier.ValueText) ?? []];
        return (fields, parameters) switch
        {
            ([var field], []) => field,
            ([], [var parameter]) => parameter,
            _ => null
        };
    }
}

/// <summary>A type whose name ends in a word that says nothing about what it is.</summary>
public sealed class VagueTypeName : IRule
{
    private static readonly string[] VagueSuffixes = ["Manager", "Handler", "Processor", "Service", "Data", "Info"];

    public string Id => "vague-type-name";

    public string Message =>
        "A type named *Manager, *Handler, *Processor, *Service, *Data or *Info: name what it holds or does.";

    public string Source => "The user";

    public IEnumerable<TextSpan> Find(SyntaxNode node, RuleContext context)
    {
        SyntaxToken? name = node switch
        {
            BaseTypeDeclarationSyntax type => type.Identifier,
            DelegateDeclarationSyntax type => type.Identifier,
            _ => null
        };
        return name is { } identifier && VagueSuffixes.Any(suffix =>
            identifier.ValueText.EndsWith(suffix, StringComparison.Ordinal))
            ? [identifier.Span]
            : [];
    }
}

/// <summary>
/// An internal or public type, method, property, field or enum member whose name nothing else uses: no code of
/// <c>src</c> outside it, no string of <c>src</c>, and no test, benchmark or script.
/// </summary>
/// <remarks>
/// A name counts as used wherever it appears, so two members with one name keep each other. The scripts load events
/// and steps by name through reflection, so their names count, without case. A private member and a parameter are the
/// style analyzer's job (IDE0051, IDE0052, IDE0060). An override, an explicit interface member and a member that the
/// runtime or the compiler calls by its name, such as <c>Main</c> or <c>Dispose</c>, have callers that the syntax does
/// not show.
/// </remarks>
public sealed class UnusedCode : IRule
{
    private const string AttributeSuffix = "Attribute";

    private static readonly FrozenSet<string> CalledByConvention = FrozenSet.ToFrozenSet(
    [
        "Main", "Dispose", "DisposeAsync", "GetEnumerator", "MoveNext", "Current", "GetAwaiter", "GetResult",
        "IsCompleted", "Deconstruct", "GetPinnableReference"
    ], StringComparer.Ordinal);

    public string Id => "unused-code";
    public string Message => "Code that nothing uses: no code in src, no test, no benchmark and no script. Delete it.";
    public string Source => "The user";

    public IEnumerable<TextSpan> Find(SyntaxNode node, RuleContext context)
    {
        if (node is FieldDeclarationSyntax field && IsVisible(field))
        {
            VariableDeclaratorSyntax[] unused =
                [.. field.Declaration.Variables.Where(variable => IsUnused(variable.Identifier.ValueText, field, context))];
            return unused.Length == field.Declaration.Variables.Count
                ? [field.Span]
                : unused.Select(variable => variable.Span);
        }

        // A class of extension members is used through its members, never by its own name.
        SyntaxToken? name = node switch
        {
            ClassDeclarationSyntax type when HoldsExtensions(type) => null,
            BaseTypeDeclarationSyntax type => type.Identifier,
            DelegateDeclarationSyntax type => type.Identifier,
            MethodDeclarationSyntax { ExplicitInterfaceSpecifier: null } method => method.Identifier,
            PropertyDeclarationSyntax { ExplicitInterfaceSpecifier: null } property => property.Identifier,
            EnumMemberDeclarationSyntax value => value.Identifier,
            _ => null
        };
        return name is { ValueText: var text } && node is MemberDeclarationSyntax member && IsVisible(member) &&
               !CalledByConvention.Contains(text) && IsUnused(text, node, context)
            ? [node.Span]
            : [];
    }

    // Whether no code of src outside the declaration uses the name, no string of src holds it, and no file outside
    // src holds it. An attribute class is used by the name without its suffix, as [Glow] uses GlowAttribute.
    private static bool IsUnused(string name, SyntaxNode declaration, RuleContext context)
    {
        string[] names = name.EndsWith(AttributeSuffix, StringComparison.Ordinal) && name != AttributeSuffix
            ? [name, name[..^AttributeSuffix.Length]]
            : [name];
        return !names.Any(each => context.UsesOf(each).Any(use => !use.Ancestors().Contains(declaration)) ||
                                  context.NamedInAString(each) || context.UsedOutside(each));
    }

    // Whether a class holds an extension block or an extension method, whose first parameter is this.
    private static bool HoldsExtensions(ClassDeclarationSyntax type) =>
        type.Members.Any(member => member switch
        {
            ExtensionBlockDeclarationSyntax => true,
            MethodDeclarationSyntax { ParameterList.Parameters: [{ Modifiers: var modifiers }, ..] } =>
                modifiers.Any(SyntaxKind.ThisKeyword),
            _ => false
        });

    // Whether a member is internal or public: by its modifiers, or by the default of its place. A type outside every
    // type is internal, and a member of an interface or an enum is public. An override and an extern member are not
    // the code's own.
    private static bool IsVisible(MemberDeclarationSyntax member)
    {
        SyntaxTokenList modifiers = member.Modifiers;
        if (modifiers.Any(SyntaxKind.OverrideKeyword) || modifiers.Any(SyntaxKind.ExternKeyword))
            return false;
        bool widened = modifiers.Any(SyntaxKind.PublicKeyword) || modifiers.Any(SyntaxKind.InternalKeyword);
        bool narrowed = modifiers.Any(SyntaxKind.PrivateKeyword) || modifiers.Any(SyntaxKind.ProtectedKeyword);
        bool publicByPlace = member.Parent is BaseNamespaceDeclarationSyntax or CompilationUnitSyntax
            or InterfaceDeclarationSyntax or EnumDeclarationSyntax;
        return widened || (!narrowed && publicByPlace);
    }
}
