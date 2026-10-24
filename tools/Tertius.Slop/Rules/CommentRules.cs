using System.Collections.Frozen;
using System.Text.RegularExpressions;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Text;

namespace Tertius.Slop.Rules;

// The verbosity rules about comments (docs/plans/code-quality-tooling.md, decision 17). A finding covers the comment
// lines, which hold no code, so these are the one kind of finding whose lines are outside the lines of code.

/// <summary>A decorative banner comment.</summary>
public sealed partial class SectionBannerComment : IRule
{
    public string Id => "section-banner-comment";
    public string Message => "A decorative banner comment: delete it, and let the structure of the code speak.";
    public string Source => "scb-check misc.yaml: section-banner-comment";

    public IEnumerable<TextSpan> Find(SyntaxNode node, RuleContext context) => node switch
    {
        CompilationUnitSyntax file => file.DescendantTrivia()
            .Where(trivia => trivia.IsKind(SyntaxKind.SingleLineCommentTrivia) && Banner().IsMatch(trivia.ToString()))
            .Select(trivia => trivia.Span),
        _ => []
    };

    // A comment that is only a rule of at least 10 dashes or equals signs, as scb-check reads one.
    [GeneratedRegex(@"^//\s*[-=─]{10,}\s*$")]
    private static partial Regex Banner();
}

/// <summary>A comment whose words are mostly the names in the statement under it.</summary>
/// <remarks>
/// A heuristic of the user's rule "a comment that repeats the code under it" (STYLE.md, "Comments": a comment gives the
/// reason for the code, never a description of it). The comment is the run of line comments right above a statement,
/// with no blank line between. Its words are the words of three letters or more, without common words such as "the"
/// or "with", and without a plural s. The words of the statement are those of its names and keywords, with a name in
/// camel case split into its words, and without the code in its blocks. The comment repeats the statement when it has
/// at least two words and at least four fifths of them are words of the statement. A share of two thirds flagged six
/// comments in src, and each of them gave a reason or a fact that the statement does not hold.
/// </remarks>
public sealed partial class CommentRepeatsCode : IRule
{
    private const int LeastWords = 2;
    private const double RepeatedShare = 4.0 / 5.0;

    private static readonly FrozenSet<string> CommonWords = FrozenSet.ToFrozenSet(
    [
        "the", "and", "for", "with", "from", "into", "onto", "are", "its", "this", "that", "these", "those", "then",
        "else", "not", "our", "here", "there", "now", "all", "each", "one", "but", "has", "have", "was", "were", "will",
        "can", "may", "also", "only", "just", "when", "which", "who", "what", "they", "them", "their", "than", "too",
        "very", "does", "did", "get", "set", "new"
    ], StringComparer.Ordinal);

    public string Id => "comment-repeats-code";
    public string Message => "A comment that repeats the code under it: delete it, or say why the code does this.";
    public string Source => "The user; STYLE.md, \"Comments\"";

    public IEnumerable<TextSpan> Find(SyntaxNode node, RuleContext context)
    {
        if (node is not StatementSyntax statement || statement is BlockSyntax ||
            CommentsAbove(statement.GetFirstToken().LeadingTrivia) is not [var first, ..] comments)
            return [];
        HashSet<string> said = [.. comments.SelectMany(comment => WordsOf(comment.ToString()))];
        HashSet<string> coded =
        [
            .. statement.DescendantTokens(descendIntoChildren: child => child == statement || child is not BlockSyntax)
                .Where(token => token.IsKeyword() || token.IsKind(SyntaxKind.IdentifierToken))
                .SelectMany(token => WordsOf(token.ValueText))
        ];
        bool repeats = said.Count >= LeastWords && said.Count(coded.Contains) >= said.Count * RepeatedShare;
        return repeats ? [TextSpan.FromBounds(first.SpanStart, comments[^1].Span.End)] : [];
    }

    // The line comments right above a statement, top first, from the trivia before its first token. A blank line or
    // anything else ends the run.
    private static List<SyntaxTrivia> CommentsAbove(SyntaxTriviaList trivia)
    {
        var comments = new List<SyntaxTrivia>();
        int index = SkipSpace(trivia, trivia.Count - 1);
        while (index >= 0 && trivia[index].IsKind(SyntaxKind.EndOfLineTrivia))
        {
            int comment = SkipSpace(trivia, index - 1);
            if (comment < 0 || !IsLineComment(trivia[comment]))
                break;
            comments.Insert(0, trivia[comment]);
            index = SkipSpace(trivia, comment - 1);
        }

        return comments;
    }

    private static int SkipSpace(SyntaxTriviaList trivia, int index)
    {
        while (index >= 0 && trivia[index].IsKind(SyntaxKind.WhitespaceTrivia))
            index--;
        return index;
    }

    // The parser reads doc comments as plain comments (SourceFile), so the three slashes tell them apart.
    private static bool IsLineComment(SyntaxTrivia trivia) =>
        trivia.IsKind(SyntaxKind.SingleLineCommentTrivia) &&
        !trivia.ToString().StartsWith("///", StringComparison.Ordinal);

    private static IEnumerable<string> WordsOf(string text) =>
        Word().Matches(text)
            .Select(match => match.Value.ToLowerInvariant())
            .Where(word => word.Length >= 3 && !CommonWords.Contains(word))
            .Select(word => word.Length > 3 && word.EndsWith('s') && !word.EndsWith("ss", StringComparison.Ordinal)
                ? word[..^1]
                : word);

    // A word of text or of a name in camel case: a run of capitals before another capital or the end, or an optional
    // capital and lower-case letters.
    [GeneratedRegex("[A-Z]+(?![a-z])|[A-Z]?[a-z]+")]
    private static partial Regex Word();
}

/// <summary>A doc comment whose summary or parameter text only repeats the name that it documents.</summary>
/// <remarks>
/// The text repeats the name when it has a word and each of its words is a word of the name, after leaving out
/// articles and words such as "gets" and "returns". A plural s does not count, and a name in camel case splits into its
/// words. A <c>see</c> or <c>paramref</c> tag counts as the name that it points to. The parser reads doc comments as
/// plain comments (<see cref="SourceFile"/>), so the rule reads their text.
/// </remarks>
public sealed partial class DocCommentRepeatsName : IRule
{
    private static readonly FrozenSet<string> FillerWords = FrozenSet.ToFrozenSet(
        ["a", "an", "the", "of", "to", "this", "that", "its", "is", "and", "or", "gets", "sets", "returns", "given"],
        StringComparer.Ordinal);

    public string Id => "doc-comment-repeats-name";

    public string Message =>
        "A doc comment that only repeats the name: say what the member is or does, or what the parameter means.";

    public string Source => "The user; STYLE.md, \"Comments\"";

    public IEnumerable<TextSpan> Find(SyntaxNode node, RuleContext context)
    {
        if (node is not MemberDeclarationSyntax member || DeclaredName(member) is not { } name)
            return [];
        SyntaxTrivia[] lines =
        [
            .. member.GetLeadingTrivia().Where(trivia => trivia.IsKind(SyntaxKind.SingleLineCommentTrivia) &&
                                                         trivia.ToString().StartsWith("///", StringComparison.Ordinal))
        ];
        string doc = string.Join('\n', lines.Select(line => line.ToString()[3..]));
        (Match Element, string Name)[] elements =
        [
            .. Summary().Matches(doc).Select(match => (match, name)),
            .. Param().Matches(doc).Select(match => (match, match.Groups["name"].Value))
        ];
        return elements.Where(element => Repeats(element.Element.Groups["text"].Value, element.Name))
            .Select(element => TextSpan.FromBounds(lines[LineAt(doc, element.Element.Index)].SpanStart,
                lines[LineAt(doc, element.Element.Index + element.Element.Length - 1)].Span.End));
    }

    private static string? DeclaredName(MemberDeclarationSyntax member) => member switch
    {
        BaseTypeDeclarationSyntax type => type.Identifier.ValueText,
        DelegateDeclarationSyntax type => type.Identifier.ValueText,
        MethodDeclarationSyntax method => method.Identifier.ValueText,
        ConstructorDeclarationSyntax constructor => constructor.Identifier.ValueText,
        PropertyDeclarationSyntax property => property.Identifier.ValueText,
        EventDeclarationSyntax @event => @event.Identifier.ValueText,
        BaseFieldDeclarationSyntax { Declaration.Variables: [var first, ..] } => first.Identifier.ValueText,
        EnumMemberDeclarationSyntax value => value.Identifier.ValueText,
        _ => null
    };

    // Whether every word of the text is a word of the name, and the text has a word.
    private static bool Repeats(string text, string name)
    {
        string plain = Tag().Replace(Reference().Replace(text, reference => reference.Groups["name"].Value), " ");
        HashSet<string> said = [.. WordsOf(plain).Where(word => !FillerWords.Contains(word)).Select(Singular)];
        HashSet<string> named = [.. WordsOf(name).Select(Singular)];
        return said.Count > 0 && said.IsSubsetOf(named);
    }

    private static IEnumerable<string> WordsOf(string text) =>
        Word().Matches(text).Select(match => match.Value.ToLowerInvariant());

    private static string Singular(string word) =>
        word.Length > 3 && word.EndsWith('s') && !word.EndsWith("ss", StringComparison.Ordinal) ? word[..^1] : word;

    private static int LineAt(string doc, int position) => doc.AsSpan(0, position).Count('\n');

    [GeneratedRegex("<summary>(?<text>.*?)</summary>", RegexOptions.Singleline)]
    private static partial Regex Summary();

    [GeneratedRegex("""<param name="(?<name>[^"]+)">(?<text>.*?)</param>""", RegexOptions.Singleline)]
    private static partial Regex Param();

    // A tag that points to a name, such as <see cref="World.Step"/>: the last part of the name, without its type
    // arguments or parameters.
    [GeneratedRegex("""<\w+\s+(?:cref|name|langword)="(?:[^"]*\.)?(?<name>\w+)[^"]*"\s*/>""")]
    private static partial Regex Reference();

    [GeneratedRegex("<[^>]+>")]
    private static partial Regex Tag();

    [GeneratedRegex("[A-Z]+(?![a-z])|[A-Z]?[a-z]+|[0-9]+")]
    private static partial Regex Word();
}

/// <summary>A line comment that holds a statement of C#.</summary>
public sealed class CommentedOutCode : IRule
{
    public string Id => "commented-out-code";
    public string Message => "Commented-out code: delete it, the history keeps the old code.";
    public string Source => "The user";

    public IEnumerable<TextSpan> Find(SyntaxNode node, RuleContext context) => node switch
    {
        CompilationUnitSyntax file => file.DescendantTrivia()
            .Where(trivia => trivia.IsKind(SyntaxKind.SingleLineCommentTrivia) && IsCode(trivia.ToString()))
            .Select(trivia => trivia.Span),
        _ => []
    };

    // A comment is code when it ends in a semicolon and parses as a statement without errors, other than a lone name
    // such as "Done;", which a sentence can be too. A sentence ends in a full stop, and the prose of this project holds
    // no semicolons, so few sentences parse.
    private static bool IsCode(string comment)
    {
        if (comment.StartsWith("///", StringComparison.Ordinal))
            return false;
        string text = comment[2..].Trim();
        if (!text.EndsWith(';'))
            return false;
        StatementSyntax statement = SyntaxFactory.ParseStatement(text: text);
        bool parses = !statement.ContainsDiagnostics && statement.FullSpan.Length == text.Length;
        return parses && statement is not ExpressionStatementSyntax { Expression: IdentifierNameSyntax };
    }
}

/// <summary>A TODO, FIXME, HACK or XXX comment without a ticket id.</summary>
public sealed partial class TodoWithoutTicket : IRule
{
    public string Id => "todo-without-ticket";
    public string Message => "A TODO without a ticket id: make a ticket with tk create and name it, or do the work.";
    public string Source => "The user";

    public IEnumerable<TextSpan> Find(SyntaxNode node, RuleContext context) => node switch
    {
        CompilationUnitSyntax file => file.DescendantTrivia()
            .Where(trivia => trivia.IsKind(SyntaxKind.SingleLineCommentTrivia) ||
                             trivia.IsKind(SyntaxKind.MultiLineCommentTrivia))
            .Where(trivia => Todo().IsMatch(trivia.ToString()) && !Ticket().IsMatch(trivia.ToString()))
            .Select(trivia => trivia.Span),
        _ => []
    };

    [GeneratedRegex(@"\b(TODO|FIXME|HACK|XXX)\b")]
    private static partial Regex Todo();

    [GeneratedRegex(@"\bter-[0-9a-z]{4}\b")]
    private static partial Regex Ticket();
}

/// <summary>A region directive.</summary>
public sealed class Region : IRule
{
    public string Id => "region";
    public string Message => "A #region: delete it, and split a type that needs one into smaller types.";
    public string Source => "The user";

    public IEnumerable<TextSpan> Find(SyntaxNode node, RuleContext context) => node switch
    {
        CompilationUnitSyntax file => file.DescendantTrivia()
            .Where(trivia => trivia.IsKind(SyntaxKind.RegionDirectiveTrivia) ||
                             trivia.IsKind(SyntaxKind.EndRegionDirectiveTrivia))
            .Select(trivia => trivia.Span),
        _ => []
    };
}
