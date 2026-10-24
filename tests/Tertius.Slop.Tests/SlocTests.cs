using AwesomeAssertions;

namespace Tertius.Slop.Tests;

/// <summary>
/// The source lines of code of a function and of a file (docs/plans/code-quality-tooling.md, decision 16): the lines
/// that hold code, and not the lines that are blank or hold only comments.
/// </summary>
public sealed class SlocTests
{
    [Fact]
    public void BlankLinesAndLinesOfOnlyCommentsAreNotCode() =>
        Source.Functions("""
                         class C
                         {
                             void F()
                             {
                                 int x = 1;

                                 // A comment.
                                 /* A block
                                    comment. */
                                 x++;
                             }
                         }
                         """).Should().ContainSingle().Which.Sloc.Should().Be(5,
            "the signature, the two braces and the two statements hold code");

    [Fact]
    public void ALineWithCodeAndATrailingCommentIsCode() =>
        Source.Functions("""
                         class C
                         {
                             int F() => 1; // A comment.
                         }
                         """).Should().ContainSingle().Which.Sloc.Should().Be(1);

    [Fact]
    public void EveryLineOfAStatementOverSeveralLinesIsCode() =>
        Source.Functions("""
                         class C
                         {
                             int F() =>
                                 Add(1,
                                     2);
                         }
                         """).Should().ContainSingle().Which.Sloc.Should().Be(3);

    [Fact]
    public void EveryLineOfAStringOverSeveralLinesIsCode() =>
        Source.Functions("""
                         class C
                         {
                             string F() => @"one

                         three";
                         }
                         """).Should().ContainSingle().Which.Sloc.Should().Be(3,
            "the blank line inside the string is part of a token");

    [Fact]
    public void TheAttributesOfAFunctionAreCodeAndItsDocCommentIsNot() =>
        Source.Functions("""
                         class C
                         {
                             /// <summary>A doc comment.</summary>
                             [Obsolete]
                             int F() => 1;
                         }
                         """).Should().ContainSingle().Which.Sloc.Should().Be(2,
            "an attribute is code, and a doc comment is a comment");

    [Fact]
    public void TheLinesOfALocalFunctionBelongToItAndNotToTheFunctionThatHoldsIt() =>
        Source.Functions("""
                         class C
                         {
                             void F()
                             {
                                 Twice(1);
                                 int Twice(int x)
                                 {
                                     return 2 * x;
                                 }
                             }
                         }
                         """).Select(function => (function.Name, function.Sloc)).Should()
            .Equal(("C.F", 4), ("C.F.Twice", 4));

    [Fact]
    public void TheSlocOfAFileCountsTheCodeOutsideFunctionsToo() =>
        SourceFile.Parse(Source.FileName, """
                                          using System;

                                          // A comment.
                                          namespace N;

                                          class C
                                          {
                                              int field = 1;
                                              void F() { }
                                          }
                                          """).Sloc.Should().Be(7);

    [Fact]
    public void TheEmptyLineAfterTheLastNewlineOfAFileIsNotCode() =>
        SourceFile.Parse(Source.FileName, "class C { }\n").Sloc.Should().Be(1,
            "the end-of-file token sits on that line, but it holds no text");
}
