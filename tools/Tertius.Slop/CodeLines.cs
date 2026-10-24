using Microsoft.CodeAnalysis;

namespace Tertius.Slop;

/// <summary>The lines of code that a run of tokens covers.</summary>
internal static class CodeLines
{
    extension(IEnumerable<SyntaxToken> tokens)
    {
        /// <summary>
        /// The number of lines that hold part of a token. A comment or a blank line is trivia and holds no token, so it
        /// does not count. A token over several lines, such as a raw string, counts each of its lines. A token without
        /// text, such as the end of the file, holds no code.
        /// </summary>
        public int CodeLineCount() =>
            tokens.Where(token => !token.Span.IsEmpty)
                .Select(token => token.GetLocation().GetLineSpan())
                .SelectMany(span => Enumerable.Range(span.StartLinePosition.Line,
                    span.EndLinePosition.Line - span.StartLinePosition.Line + 1))
                .Distinct()
                .Count();
    }
}
