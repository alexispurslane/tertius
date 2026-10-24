using AwesomeAssertions;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace Tertius.Style.Tests;

/// <summary>
/// TS0015: a switch statement over a closed set names every case. The compiler checks a switch expression for a
/// missing case, but not a switch statement, and IDE0010 covers only enums. A closed set here is a
/// <c>[GeneratedUnion]</c> struct, a built-in <c>union</c> or a <c>closed</c> record hierarchy. A case is named by a
/// label without a guard whose pattern is the case's type, alone or in an <c>or</c> pattern. A union without a zero case
/// also names <c>null</c>, its empty value. A default section only throws <c>UnreachableException</c>.
/// </summary>
public sealed class SwitchStatementTests
{
    // The sets of the tests. Move is a [GeneratedUnion] as the generator writes it: the generated part carries [Union]
    // and a constructor per case. Step is a built-in union, and Shape a closed record hierarchy.
    private const string Sets = """
        using System;
        using System.Diagnostics;
        using System.Runtime.CompilerServices;
        namespace Tertius
        {
            [AttributeUsage(AttributeTargets.Struct)]
            public sealed class GeneratedUnionAttribute(params Type[] cases) : Attribute
            {
                public Type? ZeroCase { get; set; }
            }
        }
        public readonly record struct Push(int Force);
        public readonly record struct Pull(int Force);
        public readonly record struct Hold(int Ticks);
        [Tertius.GeneratedUnion(typeof(Push), typeof(Pull), typeof(Hold))]
        [Union]
        public readonly struct Move
        {
            public Move(Push value) => Value = value;
            public Move(Pull value) => Value = value;
            public Move(Hold value) => Value = value;
            public object? Value { get; }
        }
        [Tertius.GeneratedUnion(typeof(Push), typeof(Hold), ZeroCase = typeof(Hold))]
        [Union]
        public readonly struct Grip
        {
            public Grip(Push value) => Value = value;
            public Grip(Hold value) => Value = value;
            public object Value { get; }
        }
        public sealed record Run(int Ticks);
        public sealed record Post(string Topic, string? Note);
        public union Step(Run, Post);
        public closed record Shape;
        public sealed record Box(double Side) : Shape;
        public sealed record Ball(double Radius) : Shape;

        """;

    [Fact]
    public async Task ASwitchOverAGeneratedUnionThatNamesEveryCaseAndNullIsNotReported() => (
            await Analysis.Ids(
                "TS0015",
                Sets
                    + """
                    public static class C
                    {
                        public static int Force(Move move)
                        {
                            switch (move)
                            {
                                case Push push:
                                    return push.Force;
                                case Pull pull:
                                    return -pull.Force;
                                case Hold:
                                    return 0;
                                case null:
                                    return 0;
                                default:
                                    throw new UnreachableException();
                            }
                        }
                    }
                    """
            )
        ).Should().BeEmpty();

    [Fact]
    public async Task ASwitchOverAGeneratedUnionThatMissesACaseIsReported() => (
            await Analysis.Ids(
                "TS0015",
                Sets
                    + """
                    public static class C
                    {
                        public static int Force(Move move)
                        {
                            switch (move)
                            {
                                case Push push:
                                    return push.Force;
                                case Pull pull:
                                    return -pull.Force;
                                case null:
                                    return 0;
                                default:
                                    throw new UnreachableException();
                            }
                        }
                    }
                    """
            )
        ).Should().Equal("TS0015");

    [Fact]
    public async Task ASwitchOverAGeneratedUnionWithoutANullLabelIsReported() => (
            await Analysis.Ids(
                "TS0015",
                Sets
                    + """
                    public static class C
                    {
                        public static int Force(Move move)
                        {
                            switch (move)
                            {
                                case Push push:
                                    return push.Force;
                                case Pull pull:
                                    return -pull.Force;
                                case Hold:
                                    return 0;
                                default:
                                    throw new UnreachableException();
                            }
                        }
                    }
                    """
            )
        ).Should().Equal(["TS0015"], "the empty value of the union is null");

    [Fact]
    public async Task AGeneratedUnionWithAZeroCaseNeedsNoNullLabel() => (
            await Analysis.Ids(
                "TS0015",
                Sets
                    + """
                    public static class C
                    {
                        public static int Force(Grip grip)
                        {
                            switch (grip)
                            {
                                case Push push:
                                    return push.Force;
                                case Hold:
                                    return 0;
                                default:
                                    throw new UnreachableException();
                            }
                        }
                    }
                    """
            )
        ).Should().BeEmpty("the all-zero value holds the zero case");

    [Fact]
    public async Task AConstructorOfAUnionThatIsNotPublicMakesNoCase() => (
            await Analysis.Ids(
                "TS0015",
                Sets
                    + """
                    [Union]
                    public readonly struct Signal
                    {
                        public Signal(Push value) => Value = value;
                        private Signal(int raw) => Value = raw;
                        public object? Value { get; }
                    }
                    public static class C
                    {
                        public static int Force(Signal signal)
                        {
                            switch (signal)
                            {
                                case Push push:
                                    return push.Force;
                                case null:
                                    return 0;
                                default:
                                    throw new UnreachableException();
                            }
                        }
                    }
                    """
            )
        ).Should().BeEmpty("the cases of a union are its public constructors");

    [Fact]
    public async Task ASwitchOverABuiltInUnionThatNamesEveryCaseAndNullIsNotReported() => (
            await Analysis.Ids(
                "TS0015",
                Sets
                    + """
                    public static class C
                    {
                        public static int Ticks(Step step)
                        {
                            switch (step)
                            {
                                case Run run:
                                    return run.Ticks;
                                case Post:
                                    return 1;
                                case null:
                                    return 0;
                                default:
                                    throw new UnreachableException();
                            }
                        }
                    }
                    """
            )
        ).Should().BeEmpty();

    [Fact]
    public async Task ASwitchOverABuiltInUnionThatMissesACaseIsReported() => (
            await Analysis.Ids(
                "TS0015",
                Sets
                    + """
                    public static class C
                    {
                        public static int Ticks(Step step)
                        {
                            switch (step)
                            {
                                case Run run:
                                    return run.Ticks;
                                case null:
                                    return 0;
                                default:
                                    throw new UnreachableException();
                            }
                        }
                    }
                    """
            )
        ).Should().Equal("TS0015");

    [Fact]
    public async Task ASwitchOverABuiltInUnionWithoutANullLabelIsReported() => (
            await Analysis.Ids(
                "TS0015",
                Sets
                    + """
                    public static class C
                    {
                        public static int Ticks(Step step)
                        {
                            switch (step)
                            {
                                case Run run:
                                    return run.Ticks;
                                case Post:
                                    return 1;
                                default:
                                    throw new UnreachableException();
                            }
                        }
                    }
                    """
            )
        ).Should().Equal(["TS0015"], "a built-in union has no zero case");

    [Fact]
    public async Task ASwitchOverAClosedRecordThatNamesEveryCaseNeedsNoNullLabel() => (
            await Analysis.Ids(
                "TS0015",
                Sets
                    + """
                    public static class C
                    {
                        public static double Size(Shape shape)
                        {
                            switch (shape)
                            {
                                case Box box:
                                    return box.Side;
                                case Ball ball:
                                    return 2 * ball.Radius;
                                default:
                                    throw new UnreachableException();
                            }
                        }
                    }
                    """
            )
        ).Should().BeEmpty("a closed hierarchy is no union, so it has no empty value");

    [Fact]
    public async Task ASwitchOverAClosedRecordThatMissesACaseIsReported() => (
            await Analysis.Ids(
                "TS0015",
                Sets
                    + """
                    public static class C
                    {
                        public static double Size(Shape shape)
                        {
                            switch (shape)
                            {
                                case Box box:
                                    return box.Side;
                                default:
                                    throw new UnreachableException();
                            }
                        }
                    }
                    """
            )
        ).Should().Equal("TS0015");

    [Fact]
    public async Task AClosedHierarchyInANamespaceAndInAClassIsChecked() => (
            await Analysis.Diagnostics(
                "TS0015",
                """
                using System.Diagnostics;
                namespace Cargo
                {
                    public static class Holds
                    {
                        public closed record Load;
                        public sealed record Crate(int Mass) : Load;
                        public sealed record Empty : Load;
                    }
                }
                public static class C
                {
                    public static int Mass(Cargo.Holds.Load load)
                    {
                        switch (load)
                        {
                            case Cargo.Holds.Crate crate:
                                return crate.Mass;
                            default:
                                throw new UnreachableException();
                        }
                    }
                }
                """
            )
        ).Single().GetMessage().Should().Contain("does not name Empty.");

    [Fact]
    public async Task ALabelOfTheClosedBaseDoesNotNameItsCases() => (
            await Analysis.Ids(
                "TS0015",
                Sets
                    + """
                    public static class C
                    {
                        public static double Size(Shape shape)
                        {
                            switch (shape)
                            {
                                case Shape:
                                    return 1;
                                default:
                                    throw new UnreachableException();
                            }
                        }
                    }
                    """
            )
        ).Should().Equal(["TS0015"], "a label of the base is a discard");

    [Fact]
    public async Task AGuardedLabelDoesNotNameItsCase() => (
            await Analysis.Ids(
                "TS0015",
                Sets
                    + """
                    public static class C
                    {
                        public static int Ticks(Step step)
                        {
                            switch (step)
                            {
                                case Run run when run.Ticks > 0:
                                    return run.Ticks;
                                case Post:
                                    return 1;
                                case null:
                                    return 0;
                                default:
                                    throw new UnreachableException();
                            }
                        }
                    }
                    """
            )
        ).Should().Equal(["TS0015"], "a run of no ticks falls to the default");

    [Fact]
    public async Task AGuardedLabelBesideAnUnguardedOneOfTheSameCaseIsNotReported() => (
            await Analysis.Ids(
                "TS0015",
                Sets
                    + """
                    public static class C
                    {
                        public static int Ticks(Step step)
                        {
                            switch (step)
                            {
                                case Run run when run.Ticks > 0:
                                    return run.Ticks;
                                case Run or Post or null:
                                    // A run of no ticks, a post and the empty step take one tick.
                                    return 1;
                                default:
                                    throw new UnreachableException();
                            }
                        }
                    }
                    """
            )
        ).Should().BeEmpty();

    [Fact]
    public async Task ACaseInAnOrPatternInParenthesesIsNamed() => (
            await Analysis.Ids(
                "TS0015",
                Sets
                    + """
                    public static class C
                    {
                        public static int Force(Move move)
                        {
                            switch (move)
                            {
                                case Push push:
                                    return push.Force;
                                case (Pull or Hold) or null:
                                    return 0;
                                default:
                                    throw new UnreachableException();
                            }
                        }
                    }
                    """
            )
        ).Should().BeEmpty();

    [Fact]
    public async Task AnEmptyPropertyPatternNamesItsCase() => (
            await Analysis.Ids(
                "TS0015",
                Sets
                    + """
                    public static class C
                    {
                        public static double Size(Shape shape)
                        {
                            switch (shape)
                            {
                                case Box { } box:
                                    return box.Side;
                                case Ball:
                                    return 1;
                                default:
                                    throw new UnreachableException();
                            }
                        }
                    }
                    """
            )
        ).Should().BeEmpty();

    [Fact]
    public async Task APropertyPatternThatOnlyBindsAndDiscardsNamesItsCase() => (
            await Analysis.Ids(
                "TS0015",
                Sets
                    + """
                    public static class C
                    {
                        public static double Size(Shape shape)
                        {
                            switch (shape)
                            {
                                case Box { Side: var side }:
                                    return side;
                                case Ball { Radius: _ }:
                                    return 1;
                                default:
                                    throw new UnreachableException();
                            }
                        }
                    }
                    """
            )
        ).Should().BeEmpty("a var or a discard matches every value");

    [Fact]
    public async Task APositionalPatternThatOnlyBindsNamesItsCase() => (
            await Analysis.Ids(
                "TS0015",
                Sets
                    + """
                    public static class C
                    {
                        public static double Size(Shape shape)
                        {
                            switch (shape)
                            {
                                case Box(var side):
                                    return side;
                                case Ball(_):
                                    return 1;
                                default:
                                    throw new UnreachableException();
                            }
                        }
                    }
                    """
            )
        ).Should().BeEmpty("a var or a discard matches every value");

    [Fact]
    public async Task APropertyPatternThatDeclaresAStructOfItsOwnTypeNamesItsCase() => (
            await Analysis.Ids(
                "TS0015",
                Sets
                    + """
                    public static class C
                    {
                        public static double Size(Shape shape)
                        {
                            switch (shape)
                            {
                                case Box { Side: double side }:
                                    return side;
                                case Ball { Radius: double }:
                                    return 1;
                                default:
                                    throw new UnreachableException();
                            }
                        }
                    }
                    """
            )
        ).Should().BeEmpty("every double is a double");

    [Fact]
    public async Task APropertyPatternThatDeclaresAStructNamesItsCaseWhereNullableIsOff() => (
            await Analysis.Ids(
                "TS0015",
                """
                using System.Diagnostics;
                #nullable disable
                public closed record Load;
                public sealed record Crate(double Mass) : Load;
                public sealed record Empty : Load;
                #nullable restore
                public static class C
                {
                    public static double Mass(Load load)
                    {
                        switch (load)
                        {
                            case Crate { Mass: double mass }:
                                return mass;
                            case Empty:
                                return 0;
                            default:
                                throw new UnreachableException();
                        }
                    }
                }
                """
            )
        ).Should().BeEmpty("a struct is never null, with or without annotations");

    [Fact]
    public async Task APropertyPatternThatDeclaresAReferenceThatCannotBeNullNamesItsCase() => (
            await Analysis.Ids(
                "TS0015",
                Sets
                    + """
                    public static class C
                    {
                        public static int Ticks(Step step)
                        {
                            switch (step)
                            {
                                case Run run:
                                    return run.Ticks;
                                case Post { Topic: string topic }:
                                    return topic.Length;
                                case null:
                                    return 0;
                                default:
                                    throw new UnreachableException();
                            }
                        }
                    }
                    """
            )
        ).Should().BeEmpty("the topic is not nullable, as the compiler reads it");

    [Fact]
    public async Task APositionalPatternReadsTheNullableAnnotationOfTheDeconstructParameter() => (
            await Analysis.Ids(
                "TS0015",
                Sets
                    + """
                    public static class C
                    {
                        public static int Ticks(Step step)
                        {
                            switch (step)
                            {
                                case Run run:
                                    return run.Ticks;
                                case Post(string topic, _):
                                    return topic.Length;
                                case null:
                                    return 0;
                                default:
                                    throw new UnreachableException();
                            }
                            switch (step)
                            {
                                case Run run:
                                    return run.Ticks;
                                case Post(_, string note):
                                    return note.Length;
                                case null:
                                    return 0;
                                default:
                                    throw new UnreachableException();
                            }
                        }
                    }
                    """
            )
        ).Should().Equal(["TS0015"], "only the note is nullable");

    [Fact]
    public async Task AVarSubpatternOfANullableReferenceNamesItsCase() => (
            await Analysis.Ids(
                "TS0015",
                Sets
                    + """
                    public static class C
                    {
                        public static int Ticks(Step step)
                        {
                            switch (step)
                            {
                                case Run run:
                                    return run.Ticks;
                                case Post { Note: var note }:
                                    return note?.Length ?? 0;
                                case null:
                                    return 0;
                                default:
                                    throw new UnreachableException();
                            }
                        }
                    }
                    """
            )
        ).Should().BeEmpty("a var matches null too");

    [Fact]
    public async Task ASubpatternOfANarrowerTypeDoesNotNameItsCase() => (
            await Analysis.Ids(
                "TS0015",
                Sets
                    + """
                    public closed record Load;
                    public sealed record Crate(Shape Item) : Load;
                    public sealed record Empty : Load;
                    public static class C
                    {
                        public static double Size(Load load)
                        {
                            switch (load)
                            {
                                case Crate { Item: Box box }:
                                    return box.Side;
                                case Empty:
                                    return 0;
                                default:
                                    throw new UnreachableException();
                            }
                        }
                    }
                    """
            )
        ).Should().Equal(["TS0015"], "a crate of a ball falls to the default");

    [Fact]
    public async Task APropertyPatternThatDeclaresANullableReferenceDoesNotNameItsCase() => (
            await Analysis.Ids(
                "TS0015",
                Sets
                    + """
                    public static class C
                    {
                        public static int Ticks(Step step)
                        {
                            switch (step)
                            {
                                case Run run:
                                    return run.Ticks;
                                case Post { Note: string note }:
                                    return note.Length;
                                case null:
                                    return 0;
                                default:
                                    throw new UnreachableException();
                            }
                        }
                    }
                    """
            )
        ).Should().Equal(["TS0015"], "a post with no note falls to the default");

    [Fact]
    public async Task APropertyPatternWithASubpatternDoesNotNameItsCase() => (
            await Analysis.Ids(
                "TS0015",
                Sets
                    + """
                    public static class C
                    {
                        public static double Size(Shape shape)
                        {
                            switch (shape)
                            {
                                case Box { Side: > 0 } box:
                                    return box.Side;
                                case Ball:
                                    return 1;
                                default:
                                    throw new UnreachableException();
                            }
                        }
                    }
                    """
            )
        ).Should().Equal(["TS0015"], "a box of no side falls to the default");

    [Fact]
    public async Task ADefaultSectionThatBreaksIsReportedAtItsLabel()
    {
        Diagnostic found = (
            await Analysis.Diagnostics(
                "TS0015",
                Sets
                    + """
                    public static class C
                    {
                        public static double Size(Shape shape)
                        {
                            switch (shape)
                            {
                                case Box box:
                                    return box.Side;
                                case Ball:
                                    return 1;
                                default:
                                    break;
                            }
                            return 0;
                        }
                    }
                    """
            )
        ).Single();

        found
            .Location.SourceTree!.GetText(TestContext.Current.CancellationToken)
            .ToString(found.Location.SourceSpan)
            .Should()
            .Be("default:");
        found.GetMessage().Should().Contain("default section");
    }

    [Fact]
    public async Task ADefaultSectionThatThrowsAnotherExceptionIsReported() => (
            await Analysis.Ids(
                "TS0015",
                Sets
                    + """
                    public static class C
                    {
                        public static double Size(Shape shape)
                        {
                            switch (shape)
                            {
                                case Box box:
                                    return box.Side;
                                case Ball:
                                    return 1;
                                default:
                                    throw new InvalidOperationException();
                            }
                        }
                    }
                    """
            )
        ).Should().Equal("TS0015");

    [Fact]
    public async Task ADefaultSectionThatRunsAStatementBeforeTheThrowIsReported() => (
            await Analysis.Ids(
                "TS0015",
                Sets
                    + """
                    public static class C
                    {
                        public static double Size(Shape shape)
                        {
                            switch (shape)
                            {
                                case Box box:
                                    return box.Side;
                                case Ball:
                                    return 1;
                                default:
                                    Console.WriteLine(shape);
                                    throw new UnreachableException();
                            }
                        }
                    }
                    """
            )
        ).Should().Equal("TS0015");

    [Fact]
    public async Task ADefaultSectionWithAStatementAfterTheThrowIsReported() => (
            await Analysis.Ids(
                "TS0015",
                Sets
                    + """
                    public static class C
                    {
                        public static double Size(Shape shape)
                        {
                            switch (shape)
                            {
                                case Box box:
                                    return box.Side;
                                case Ball:
                                    return 1;
                                default:
                                    throw new UnreachableException();
                                    return 0;
                            }
                        }
                    }
                    """
            )
        ).Should().Equal("TS0015");

    [Fact]
    public async Task ADefaultSectionThatThrowsUnreachableExceptionWithAMessageIsNotReported() => (
            await Analysis.Ids(
                "TS0015",
                Sets
                    + """
                    public static class C
                    {
                        public static double Size(Shape shape)
                        {
                            switch (shape)
                            {
                                case Box box:
                                    return box.Side;
                                case Ball:
                                    return 1;
                                default:
                                    throw new UnreachableException($"{shape} is no shape.");
                            }
                        }
                    }
                    """
            )
        ).Should().BeEmpty();

    [Fact]
    public async Task ASwitchWithoutADefaultSectionIsLeftToIde0010() => (
            await Analysis.Ids(
                "TS0015",
                Sets
                    + """
                    public static class C
                    {
                        public static double Size(Shape shape)
                        {
                            switch (shape)
                            {
                                case Box box:
                                    return box.Side;
                                case Ball:
                                    return 1;
                            }
                            return 0;
                        }
                    }
                    """
            )
        ).Should().BeEmpty("IDE0010 reports a switch statement without a default");

    [Fact]
    public async Task ASwitchOverAnOpenTypeIsNotReported() => (
            await Analysis.Ids(
                "TS0015",
                Sets
                    + """
                    public enum Side { Left, Right }
                    public static class C
                    {
                        public static int Pick(object value, Side side)
                        {
                            switch (value)
                            {
                                case Box:
                                    return 1;
                                default:
                                    break;
                            }
                            switch (side)
                            {
                                case Side.Left:
                                    return 2;
                                default:
                                    return 3;
                            }
                        }
                    }
                    """
            )
        ).Should().BeEmpty("IDE0010 covers an enum, and an object is an open set");

    [Fact]
    public async Task TheMessageNamesTheMissingCasesAndTheSectionOfTheGuide()
    {
        Diagnostic found = (
            await Analysis.Diagnostics(
                "TS0015",
                Sets
                    + """
                    public static class C
                    {
                        public static int Force(Move move)
                        {
                            switch (move)
                            {
                                case Push push:
                                    return push.Force;
                                default:
                                    throw new UnreachableException();
                            }
                        }
                    }
                    """
            )
        ).Single();

        found
            .GetMessage()
            .Should()
            .Contain("'Move'")
            .And.Contain("Pull, Hold and null")
            .And.Contain("(STYLE.md, \"Switches\")");
        found
            .Location.SourceTree!.GetText(TestContext.Current.CancellationToken)
            .ToString(found.Location.SourceSpan)
            .Should()
            .Be("switch");
    }

    [Fact]
    public async Task ABuiltInUnionOfAnotherAssemblyIsChecked()
    {
        CSharpCompilation library = TestCompilation.Of(
            """
            public sealed record Run(int Ticks);
            public sealed record Post(string Topic);
            public union Step(Run, Post);
            """,
            assembly: "Tertius.Library",
            libraries: []
        );
        CSharpCompilation probe = TestCompilation.Of(
            """
            using System.Diagnostics;
            public static class C
            {
                public static int Ticks(Step step)
                {
                    switch (step)
                    {
                        case Run run:
                            return run.Ticks;
                        case Post:
                            return 1;
                        default:
                            throw new UnreachableException();
                    }
                }
            }
            """,
            assembly: "Probe",
            libraries: [library]
        );

        (await Analysis.Diagnostics("TS0015", probe)).Single().GetMessage().Should().Contain("null");
    }

    [Fact]
    public async Task AGeneratedUnionOfAnotherAssemblyKeepsItsZeroCase()
    {
        CSharpCompilation library = TestCompilation.Of(Sets, assembly: "Tertius.Library", libraries: []);
        CSharpCompilation probe = TestCompilation.Of(
            """
            using System.Diagnostics;
            public static class C
            {
                public static int Force(Grip grip)
                {
                    switch (grip)
                    {
                        case Push push:
                            return push.Force;
                        default:
                            throw new UnreachableException();
                    }
                }
            }
            """,
            assembly: "Probe",
            libraries: [library]
        );

        (await Analysis.Diagnostics("TS0015", probe))
            .Single()
            .GetMessage()
            .Should()
            .Contain("Hold")
            .And.NotContain("null");
    }

    [Fact]
    public async Task GeneratedCodeIsNotAnalyzed() => (
            await Analysis.Ids(
                "TS0015",
                "// <auto-generated/>\n"
                    + Sets
                    + """
                    public static class C
                    {
                        public static double Size(Shape shape)
                        {
                            switch (shape)
                            {
                                case Box box:
                                    return box.Side;
                                default:
                                    break;
                            }
                            return 0;
                        }
                    }
                    """
            )
        ).Should().BeEmpty();
}
