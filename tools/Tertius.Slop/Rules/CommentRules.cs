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
        trivia.IsKind(SyntaxKind.SingleLineCommentTrivia) && !trivia.ToString().StartsWith("///", StringComparison.Ordinal);

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
