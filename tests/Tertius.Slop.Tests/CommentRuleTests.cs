using AwesomeAssertions;

namespace Tertius.Slop.Tests;

/// <summary>
/// The verbosity rules about comments (docs/plans/code-quality-tooling.md, decision 17): a decorative banner, from
/// scb-check, and the user's rule for a comment that repeats the code under it.
/// </summary>
public class CommentRuleTests
{
    [Theory]
    [InlineData("// ==========")]
    [InlineData("//------------------------------")]
    public void ABannerCommentIsFlagged(string comment) =>
        Source.Findings($"{comment}\nclass C {{ }}").Should().ContainSingle().Which.Should().Be(new Finding
        {
            Rule = "section-banner-comment", File = Source.FileName, Line = 1, EndLine = 1
        });

    [Theory]
    [InlineData("// a-b")]
    [InlineData("// Gravity ===")]
    [InlineData("// =====")]
    public void ACommentWithWordsOrAShortRuleIsNotABanner(string comment) =>
        Source.RulesIn($"{comment}\nclass C {{ }}").Should().BeEmpty();

    [Fact]
    public void ACommentWhoseWordsAreMostlyTheNamesInTheStatementUnderItIsFlagged() =>
        Source.Findings("""
                        class C
                        {
                            void F()
                            {
                                Prepare();
                                // Add the body to the world.
                                world.Add(body);
                            }
                        }
                        """).Should().ContainSingle().Which.Should().Be(new Finding
        {
            Rule = "comment-repeats-code", File = Source.FileName, Line = 6, EndLine = 6
        });

    [Fact]
    public void EveryLineOfARepeatingCommentIsFlagged() =>
        Source.Findings("""
                        class C
                        {
                            void F()
                            {
                                Prepare();
                                // Step the world
                                // with the step.
                                world.Step(step);
                            }
                        }
                        """).Should().ContainSingle().Which.Should().Be(new Finding
        {
            Rule = "comment-repeats-code", File = Source.FileName, Line = 6, EndLine = 7
        });

    [Fact]
    public void ACommentThatGivesAReasonIsNotFlagged() =>
        Source.RulesIn("""
                       class C
                       {
                           void F()
                           {
                               Prepare();
                               // Jolt keeps one allocator per thread, so the lock serializes the native work.
                               lock (gate) { world.Step(step); }
                           }
                       }
                       """).Should().BeEmpty();

    [Fact]
    public void ACommentWithMoreThanAFifthOfItsWordsOutsideTheCodeIsNotFlagged() =>
        Source.RulesIn("""
                       class C
                       {
                           void F()
                           {
                               Prepare();
                               // The section starts at its camera step, the last before its film.
                               int start = steps.Take(film).FindLastIndex(step => step is CameraStep);
                           }
                       }
                       """).Should().BeEmpty();

    [Fact]
    public void ACommentSeparatedFromTheCodeByABlankLineIsNotFlagged() =>
        Source.RulesIn("""
                       class C
                       {
                           void F()
                           {
                               Prepare();
                               // Add the body to the world.

                               world.Add(body);
                           }
                       }
                       """).Should().BeEmpty();

    [Fact]
    public void ACommentWithOneWordIsNotFlagged() =>
        Source.RulesIn("""
                       class C
                       {
                           void F()
                           {
                               Prepare();
                               // Add.
                               world.Add(body);
                           }
                       }
                       """).Should().BeEmpty();
}
