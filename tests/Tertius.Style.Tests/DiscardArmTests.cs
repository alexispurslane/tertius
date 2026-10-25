using AwesomeAssertions;
using Microsoft.CodeAnalysis;

namespace Tertius.Style.Tests;

/// <summary>
/// TS0016: a switch expression over a closed set has no discard arm. A closed set is what TS0015 counts, a
/// <c>[GeneratedUnion]</c> struct, a built-in <c>union</c> or a <c>closed</c> hierarchy, and an enum that the
/// compilation declares. A discard arm is an arm without a guard whose pattern matches every value: <c>_</c>,
/// <c>var _</c> or <c>var x</c>. The fix deletes a discard arm that only throws <c>UnreachableException</c>, and offers
/// nothing for any other discard arm.
/// </summary>
public sealed class DiscardArmTests
{
    private const string Sets = SwitchStatementTests.Sets;

    private const string Side = """
        using System.Diagnostics;
        public enum Side { Left, Right }

        """;

    [Fact]
    public async Task ADiscardArmOverAGeneratedUnionIsReported() => (
            await Analysis.Ids(
                "TS0016",
                Sets
                    + """
                    public static class C
                    {
                        public static int Force(Move move) =>
                            move switch
                            {
                                Push push => push.Force,
                                Pull pull => -pull.Force,
                                _ => 0,
                            };
                    }
                    """
            )
        ).Should().Equal("TS0016");

    [Fact]
    public async Task ADiscardArmOverABuiltInUnionIsReported() => (
            await Analysis.Ids(
                "TS0016",
                Sets
                    + """
                    public static class C
                    {
                        public static int Ticks(Step step) =>
                            step switch
                            {
                                Run run => run.Ticks,
                                _ => 0,
                            };
                    }
                    """
            )
        ).Should().Equal("TS0016");

    [Fact]
    public async Task ADiscardArmOverAClosedHierarchyIsReported() => (
            await Analysis.Ids(
                "TS0016",
                Sets
                    + """
                    public static class C
                    {
                        public static double Size(Shape shape) =>
                            shape switch
                            {
                                Box box => box.Side,
                                _ => 0,
                            };
                    }
                    """
            )
        ).Should().Equal("TS0016");

    [Fact]
    public async Task ADiscardArmOverAnEnumIsReported() => (
            await Analysis.Ids(
                "TS0016",
                Side
                    + """
                    public static class C
                    {
                        public static int Pick(Side side) =>
                            side switch
                            {
                                Side.Left => 1,
                                _ => 2,
                            };
                    }
                    """
            )
        ).Should().Equal("TS0016");

    [Fact]
    public async Task AVarDiscardAndAVarDesignationAreDiscardArms() => (
            await Analysis.Ids(
                "TS0016",
                Side
                    + """
                    public static class C
                    {
                        public static int First(Side side) =>
                            side switch
                            {
                                Side.Left => 1,
                                var _ => 2,
                            };

                        public static int Second(Side side) =>
                            side switch
                            {
                                Side.Left => 1,
                                var other => (int)other,
                            };
                    }
                    """
            )
        ).Should().Equal("TS0016", "TS0016");

    [Fact]
    public async Task AGuardedDiscardArmIsNotADiscardArm() => (
            await Analysis.Ids(
                "TS0016",
                Side
                    + """
                    public static class C
                    {
                        public static int Pick(Side side, bool strict) =>
                            side switch
                            {
                                Side.Left => 1,
                                Side.Right => 2,
                                _ when strict => 3,
                            };
                    }
                    """
            )
        ).Should().BeEmpty("a guard makes the arm match fewer values than every value");

    [Fact]
    public async Task ASwitchExpressionThatNamesEveryCaseIsNotReported() => (
            await Analysis.Ids(
                "TS0016",
                Sets
                    + """
                    public static class C
                    {
                        public static int Force(Move move) =>
                            move switch
                            {
                                Push push => push.Force,
                                Pull pull => -pull.Force,
                                Hold => 0,
                                null => 0,
                            };
                    }
                    """
            )
        ).Should().BeEmpty();

    [Fact]
    public async Task ADiscardArmOverAnOpenTypeIsNotReported() => (
            await Analysis.Ids(
                "TS0016",
                Side
                    + """
                    public interface IShape;
                    public static class C
                    {
                        public static int Pick(object value, IShape shape, int number, string text) =>
                            value switch
                            {
                                string => 1,
                                _ => shape switch
                                {
                                    null => 2,
                                    _ => number switch
                                    {
                                        1 => 3,
                                        _ => text switch
                                        {
                                            "a" => 4,
                                            _ => 5,
                                        },
                                    },
                                },
                            };
                    }
                    """
            )
        ).Should().BeEmpty("an object, an interface, a number and a string are open sets");

    [Fact]
    public async Task ADiscardArmOverAnEnumOfALibraryIsNotReported() => (
            await Analysis.Ids(
                "TS0016",
                """
                using System;
                public static class C
                {
                    public static int Pick(DayOfWeek day) =>
                        day switch
                        {
                            DayOfWeek.Monday => 1,
                            _ => 2,
                        };
                }
                """
            )
        ).Should().BeEmpty("an enum of another assembly, such as the keys of Silk.NET, is an open set");

    [Fact]
    public async Task ADiscardArmInGeneratedCodeIsNotReported() => (
            await Analysis.Ids(
                "TS0016",
                """
                // <auto-generated/>
                public enum Side { Left, Right }
                public static class C
                {
                    public static int Pick(Side side) =>
                        side switch
                        {
                            Side.Left => 1,
                            _ => 2,
                        };
                }
                """
            )
        ).Should().BeEmpty();

    [Fact]
    public async Task TheMessageNamesTheTypeAndTheSectionOfTheGuideAndTheHiddenCase()
    {
        Diagnostic found = (
            await Analysis.Diagnostics(
                "TS0016",
                Side
                    + """
                    public static class C
                    {
                        public static int Pick(Side side) =>
                            side switch
                            {
                                Side.Left => 1,
                                _ => 2,
                            };
                    }
                    """
            )
        ).Single();

        found
            .GetMessage()
            .Should()
            .Contain("'Side'")
            .And.Contain("hides a new case from the compiler")
            .And.Contain("(STYLE.md, \"Switches\")");
        found.Severity.Should().Be(DiagnosticSeverity.Warning);
        found
            .Location.SourceTree!.GetText(TestContext.Current.CancellationToken)
            .ToString(found.Location.SourceSpan)
            .Should()
            .Be("_", "the diagnostic sits at the pattern of the discard arm");
    }

    // The code fix

    [Fact]
    public async Task TheFixDeletesTheLastArmAndKeepsTheTrailingComma() =>
        (
            await Fixes.DiscardArm.First(
                """
                using System.Diagnostics;
                public enum Side { Left, Right }
                public static class C
                {
                    public static int Pick(Side side) =>
                        side switch
                        {
                            Side.Left => 1,
                            Side.Right => 2,
                            _ => throw new UnreachableException(),
                        };
                }
                """
            )
        )
            .Should()
            .Be(
                """
                using System.Diagnostics;
                public enum Side { Left, Right }
                public static class C
                {
                    public static int Pick(Side side) =>
                        side switch
                        {
                            Side.Left => 1,
                            Side.Right => 2,
                        };
                }
                """
            );

    [Fact]
    public async Task TheFixDeletesTheLastArmAndKeepsAMissingTrailingComma() =>
        (
            await Fixes.DiscardArm.First(
                """
                using System.Diagnostics;
                public enum Side { Left, Right }
                public static class C
                {
                    public static int Pick(Side side) =>
                        side switch
                        {
                            Side.Left => 1,
                            Side.Right => 2,
                            _ => throw new UnreachableException()
                        };
                }
                """
            )
        )
            .Should()
            .Be(
                """
                using System.Diagnostics;
                public enum Side { Left, Right }
                public static class C
                {
                    public static int Pick(Side side) =>
                        side switch
                        {
                            Side.Left => 1,
                            Side.Right => 2
                        };
                }
                """
            );

    [Fact]
    public async Task TheFixDeletesAnArmThatThrowsWithAMessage() =>
        (
            await Fixes.DiscardArm.First(
                """
                using System.Diagnostics;
                public enum Side { Left, Right }
                public static class C
                {
                    public static int Pick(Side side) =>
                        side switch
                        {
                            Side.Left => 1,
                            Side.Right => 2,
                            _ => throw new UnreachableException($"No such side: {side}"),
                        };
                }
                """
            )
        )
            .Should()
            .Be(
                """
                using System.Diagnostics;
                public enum Side { Left, Right }
                public static class C
                {
                    public static int Pick(Side side) =>
                        side switch
                        {
                            Side.Left => 1,
                            Side.Right => 2,
                        };
                }
                """
            );

    [Fact]
    public async Task TheFixDeletesAQualifiedThrowAndAVarDiscardArm() =>
        (
            await Fixes.DiscardArm.First(
                """
                public enum Side { Left, Right }
                public static class C
                {
                    public static int Pick(Side side) =>
                        side switch
                        {
                            Side.Left => 1,
                            Side.Right => 2,
                            var _ => throw new System.Diagnostics.UnreachableException(),
                        };
                }
                """
            )
        )
            .Should()
            .Be(
                """
                public enum Side { Left, Right }
                public static class C
                {
                    public static int Pick(Side side) =>
                        side switch
                        {
                            Side.Left => 1,
                            Side.Right => 2,
                        };
                }
                """
            );

    [Fact]
    public async Task TheFixKeepsTheFormattingOfTheOtherArms() =>
        (
            await Fixes.DiscardArm.First(
                """
                using System.Diagnostics;
                public enum Side { Left, Right }
                public static class C
                {
                    public static int Pick(Side side) =>
                        side switch
                        {
                            Side.Left =>
                                1, // the left
                            Side.Right   =>   2,
                            _ => throw new UnreachableException(),
                        };
                }
                """
            )
        )
            .Should()
            .Be(
                """
                using System.Diagnostics;
                public enum Side { Left, Right }
                public static class C
                {
                    public static int Pick(Side side) =>
                        side switch
                        {
                            Side.Left =>
                                1, // the left
                            Side.Right   =>   2,
                        };
                }
                """
            );

    [Fact]
    public async Task TheFixDeletesAnArmInTheMiddleAndItsComma() =>
        (
            await Fixes.DiscardArm.First(
                """
                using System.Diagnostics;
                public enum Side { Left, Right }
                public static class C
                {
                    public static int Pick(Side side) =>
                        side switch
                        {
                            Side.Left => 1,
                            _ => throw new UnreachableException(),
                            Side.Right => 2,
                        };
                }
                """
            )
        )
            .Should()
            .Be(
                """
                using System.Diagnostics;
                public enum Side { Left, Right }
                public static class C
                {
                    public static int Pick(Side side) =>
                        side switch
                        {
                            Side.Left => 1,
                            Side.Right => 2,
                        };
                }
                """
            );

    [Fact]
    public async Task TheFixOffersNothingForADiscardArmThatReturnsAValue() => (
            await Fixes.DiscardArm.Offered(
                """
                public enum Side { Left, Right }
                public static class C
                {
                    public static int Pick(Side side) =>
                        side switch
                        {
                            Side.Left => 1,
                            _ => 2,
                        };
                }
                """
            )
        ).Should().Be(0);

    [Fact]
    public async Task TheFixOffersNothingForADiscardArmThatThrowsAnotherException() => (
            await Fixes.DiscardArm.Offered(
                """
                using System;
                public enum Side { Left, Right }
                public static class C
                {
                    public static int Pick(Side side) =>
                        side switch
                        {
                            Side.Left => 1,
                            _ => throw new InvalidOperationException(),
                        };
                }
                """
            )
        ).Should().Be(0);

    [Fact]
    public async Task TheFixOffersNothingForTheOnlyArm() => (
            await Fixes.DiscardArm.Offered(
                """
                using System.Diagnostics;
                public enum Side { Left, Right }
                public static class C
                {
                    public static int Pick(Side side) =>
                        side switch
                        {
                            _ => throw new UnreachableException(),
                        };
                }
                """
            )
        ).Should().Be(0, "a switch expression with no arm does not compile");

    [Fact]
    public async Task TheFixAllDeletesEveryArmOfADocumentIncludingANestedOne() =>
        (
            await Fixes.DiscardArm.All(
                """
                using System.Diagnostics;
                public enum Side { Left, Right }
                public static class C
                {
                    public static int Pick(Side side, Side other) =>
                        side switch
                        {
                            Side.Left => 1,
                            Side.Right => other switch
                            {
                                Side.Left => 2,
                                Side.Right => 3,
                                _ => throw new UnreachableException(),
                            },
                            _ => throw new UnreachableException(),
                        };

                    public static int Again(Side side) =>
                        side switch
                        {
                            Side.Left => 1,
                            Side.Right => 2,
                            _ => throw new UnreachableException()
                        };
                }
                """
            )
        )
            .Should()
            .Be(
                """
                using System.Diagnostics;
                public enum Side { Left, Right }
                public static class C
                {
                    public static int Pick(Side side, Side other) =>
                        side switch
                        {
                            Side.Left => 1,
                            Side.Right => other switch
                            {
                                Side.Left => 2,
                                Side.Right => 3,
                            },
                        };

                    public static int Again(Side side) =>
                        side switch
                        {
                            Side.Left => 1,
                            Side.Right => 2
                        };
                }
                """
            );

    [Fact]
    public async Task TheFixAllLeavesADiscardArmThatReturnsAValue() =>
        (
            await Fixes.DiscardArm.AllOnce(
                """
                using System.Diagnostics;
                public enum Side { Left, Right }
                public static class C
                {
                    public static int Again(Side side) =>
                        side switch
                        {
                            Side.Left => 1,
                            Side.Right => 2,
                            _ => throw new UnreachableException(),
                        };

                    public static int Pick(Side side) =>
                        side switch
                        {
                            Side.Left => 1,
                            _ => 2,
                        };
                }
                """
            )
        )
            .Should()
            .Be(
                """
                using System.Diagnostics;
                public enum Side { Left, Right }
                public static class C
                {
                    public static int Again(Side side) =>
                        side switch
                        {
                            Side.Left => 1,
                            Side.Right => 2,
                        };

                    public static int Pick(Side side) =>
                        side switch
                        {
                            Side.Left => 1,
                            _ => 2,
                        };
                }
                """
            );
}
