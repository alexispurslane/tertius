using AwesomeAssertions;

namespace Tertius.Slop.Tests;

/// <summary>
/// The verbosity rules about comments and directives (docs/plans/code-quality-tooling.md, decision 17): a decorative
/// banner, from scb-check, and the user's rules for a comment that repeats the code under it, a doc comment that repeats
/// a name, commented-out code, a TODO without a ticket and a region.
/// </summary>
public sealed class CommentRuleTests
{
    [Theory]
    [InlineData("// ==========")]
    [InlineData("//------------------------------")]
    public void ABannerCommentIsFlagged(string comment) =>
        Source
            .Findings($"{comment}\nclass C {{ }}")
            .Should()
            .ContainSingle()
            .Which.Should()
            .Be(
                new Finding
                {
                    Rule = "section-banner-comment",
                    File = Source.FileName,
                    Line = 1,
                    EndLine = 1,
                }
            );

    [Theory]
    [InlineData("// a-b")]
    [InlineData("// Gravity ===")]
    [InlineData("// =====")]
    public void ACommentWithWordsOrAShortRuleIsNotABanner(string comment) =>
        Source.RulesIn($"{comment}\nclass C {{ }}").Should().BeEmpty();

    [Fact]
    public void ACommentWhoseWordsAreMostlyTheNamesInTheStatementUnderItIsFlagged() =>
        Source
            .Findings(
                """
                class C
                {
                    void F()
                    {
                        Prepare();
                        // Add the body to the world.
                        world.Add(body);
                    }
                }
                """
            )
            .Should()
            .ContainSingle()
            .Which.Should()
            .Be(
                new Finding
                {
                    Rule = "comment-repeats-code",
                    File = Source.FileName,
                    Line = 6,
                    EndLine = 6,
                }
            );

    [Fact]
    public void EveryLineOfARepeatingCommentIsFlagged() =>
        Source
            .Findings(
                """
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
                """
            )
            .Should()
            .ContainSingle()
            .Which.Should()
            .Be(
                new Finding
                {
                    Rule = "comment-repeats-code",
                    File = Source.FileName,
                    Line = 6,
                    EndLine = 7,
                }
            );

    [Fact]
    public void ACommentThatGivesAReasonIsNotFlagged() =>
        Source
            .RulesIn(
                """
                class C
                {
                    void F()
                    {
                        Prepare();
                        // Jolt keeps one allocator per thread, so the lock serializes the native work.
                        lock (gate) { world.Step(step); }
                    }
                }
                """
            )
            .Should()
            .BeEmpty();

    [Fact]
    public void ACommentWithMoreThanAFifthOfItsWordsOutsideTheCodeIsNotFlagged() =>
        Source
            .RulesIn(
                """
                class C
                {
                    void F()
                    {
                        Prepare();
                        // The section starts at its camera step, the last before its film.
                        int start = steps.Take(film).FindLastIndex(step => step is CameraStep);
                    }
                }
                """
            )
            .Should()
            .BeEmpty();

    [Fact]
    public void TheDocCommentOfALocalFunctionIsNotFlagged() =>
        Source
            .RulesIn(
                """
                class C
                {
                    void F()
                    {
                        Prepare();
                        /// Adds the body to the world.
                        void AddBody(Body body) => world.Add(body);
                        AddBody(a);
                        AddBody(b);
                    }
                }
                """
            )
            .Should()
            .BeEmpty();

    [Fact]
    public void TheCodeInTheBlockOfAStatementDoesNotCount() =>
        Source
            .RulesIn(
                """
                class C
                {
                    void F()
                    {
                        Prepare();
                        // Step the world.
                        lock (gate) { world.Step(); }
                    }
                }
                """
            )
            .Should()
            .BeEmpty();

    [Fact]
    public void ACommentSeparatedFromTheCodeByABlankLineIsNotFlagged() =>
        Source
            .RulesIn(
                """
                class C
                {
                    void F()
                    {
                        Prepare();
                        // Add the body to the world.

                        world.Add(body);
                    }
                }
                """
            )
            .Should()
            .BeEmpty();

    [Fact]
    public void ACommentWithOneWordIsNotFlagged() =>
        Source
            .RulesIn(
                """
                class C
                {
                    void F()
                    {
                        Prepare();
                        // Add.
                        world.Add(body);
                    }
                }
                """
            )
            .Should()
            .BeEmpty();

    [Fact]
    public void ADocSummaryThatOnlyRepeatsTheMemberNameIsFlagged() =>
        Source
            .Findings(
                """
                class C
                {
                    /// <summary>The id.</summary>
                    public string Id { get; }
                }
                """
            )
            .Should()
            .ContainSingle()
            .Which.Should()
            .Be(
                new Finding
                {
                    Rule = "doc-comment-repeats-name",
                    File = Source.FileName,
                    Line = 3,
                    EndLine = 3,
                }
            );

    [Fact]
    public void EveryLineOfADocSummaryThatRepeatsTheNameIsFlagged() =>
        Source
            .Findings(
                """
                class C
                {
                    /// <summary>
                    /// Gets the body mass.
                    /// </summary>
                    /// <remarks>In kilograms.</remarks>
                    float BodyMass;
                }
                """
            )
            .Should()
            .ContainSingle()
            .Which.Should()
            .Be(
                new Finding
                {
                    Rule = "doc-comment-repeats-name",
                    File = Source.FileName,
                    Line = 3,
                    EndLine = 5,
                }
            );

    [Fact]
    public void ADocParamThatOnlyRepeatsTheParameterNameIsFlagged() =>
        Source
            .Findings(
                """
                class C
                {
                    /// <summary>Moves the body to a pose.</summary>
                    /// <param name="pose">The pose.</param>
                    void Move(Pose pose) { Use(pose, 0); }
                }
                """
            )
            .Should()
            .ContainSingle()
            .Which.Should()
            .Be(
                new Finding
                {
                    Rule = "doc-comment-repeats-name",
                    File = Source.FileName,
                    Line = 4,
                    EndLine = 4,
                }
            );

    [Theory]
    [InlineData("/// <summary>The id of the rule.</summary>\nstring Id;")]
    [InlineData("/// <summary>The <see cref=\"Body\"/> of the crate.</summary>\nBody Crate;")]
    [InlineData(
        "/// <param name=\"pose\">The pose in the frame of the parent.</param>\nvoid Move(Pose pose) { Use(pose, 0); }"
    )]
    [InlineData("// The id.\nstring Id;")]
    public void ADocCommentThatSaysMoreThanTheNameOrAPlainCommentIsNotFlagged(string member) =>
        Source.RulesIn($"class C\n{{\n{member}\n}}").Should().BeEmpty();

    [Theory]
    [InlineData("// world.Remove(body);")]
    [InlineData("// var mass = body.Mass * 2;")]
    public void ACommentThatHoldsAStatementIsFlaggedAsCommentedOutCode(string comment) =>
        Source
            .Findings(
                $$"""
                class C
                {
                    void F()
                    {
                        {{comment}}
                        Use(2);
                    }
                }
                """
            )
            .Should()
            .ContainSingle()
            .Which.Should()
            .Be(
                new Finding
                {
                    Rule = "commented-out-code",
                    File = Source.FileName,
                    Line = 5,
                    EndLine = 5,
                }
            );

    [Theory]
    [InlineData("// Wake the body before the push.")]
    [InlineData("// Every step runs once per tick;")]
    [InlineData("// See ARCHITECTURE.md, \"Bubbles\".")]
    public void ACommentInWordsIsNotCommentedOutCode(string comment) =>
        Source
            .RulesIn(
                $$"""
                class C
                {
                    void F()
                    {
                        {{comment}}
                        Use(2);
                    }
                }
                """
            )
            .Should()
            .BeEmpty();

    [Theory]
    [InlineData("// TODO: merge the two tables.")]
    [InlineData("// FIXME the order is wrong.")]
    [InlineData("/* HACK: skip the first frame. */")]
    public void ATodoWithoutATicketIdIsFlagged(string comment) =>
        Source.RulesIn($"{comment}\nclass C {{ }}").Should().Equal("todo-without-ticket");

    [Theory]
    [InlineData("// TODO(ter-a3ya): merge the two tables.")]
    [InlineData("// The todo list of the frame.")]
    public void ATodoWithATicketIdOrTheWordInProseIsNotFlagged(string comment) =>
        Source.RulesIn($"{comment}\nclass C {{ }}").Should().BeEmpty();

    [Fact]
    public void ARegionAndItsEndAreFlagged() =>
        Source
            .Findings(
                """
                class C
                {
                    #region Fields
                    int x;
                    #endregion
                }
                """
            )
            .Select(finding => (finding.Rule, finding.Line))
            .Should()
            .Equal(("region", 3), ("region", 5));
}
