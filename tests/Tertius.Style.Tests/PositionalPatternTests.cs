using AwesomeAssertions;
using Microsoft.CodeAnalysis.CSharp;

namespace Tertius.Style.Tests;

/// <summary>
/// TS0004: a positional pattern or a deconstruction with more than one element, of a type that this solution declares.
/// Such a pattern binds by parameter order, so a new or reordered field breaks it, or swaps two values of one type. A
/// tuple has no names to bind by, so it stays positional, a framework type such as <c>KeyValuePair</c> never reorders
/// its fields, and a positional pattern with one element has no order to break. The code fix turns a positional
/// pattern into a property pattern.
/// </summary>
public sealed class PositionalPatternTests
{
    private const string Point = "public readonly record struct Point(int X, int Y);\n";

    [Fact]
    public async Task APositionalPatternOfARecordWithTwoFieldsIsReported() =>
        (await Analysis.Ids("TS0004", Point + """
                                              public static class C
                                              {
                                                  public static bool Origin(object value) => value is Point(0, 0);
                                              }
                                              """)).Should().Equal("TS0004");

    [Fact]
    public async Task APositionalPatternWithoutATypeIsReportedWhenTheInputIsNotATuple() =>
        (await Analysis.Ids("TS0004", Point + """
                                              public static class C
                                              {
                                                  public static bool Origin(Point point) => point is (0, 0);
                                              }
                                              """)).Should().Equal("TS0004");

    [Fact]
    public async Task APositionalPatternInASwitchArmIsReported() =>
        (await Analysis.Ids("TS0004", Point + """
                                              public static class C
                                              {
                                                  public static int Sum(Point point) => point switch
                                                  {
                                                      Point(var x, var y) => x + y
                                                  };
                                              }
                                              """)).Should().Equal("TS0004");

    [Fact]
    public async Task ADeconstructionDeclarationOfARecordIsReported() =>
        (await Analysis.Ids("TS0004", Point + """
                                              public static class C
                                              {
                                                  public static int Sum(Point point)
                                                  {
                                                      var (x, y) = point;
                                                      return x + y;
                                                  }
                                              }
                                              """)).Should().Equal("TS0004");

    [Fact]
    public async Task ADeconstructionIntoExistingLocalsIsReported() =>
        (await Analysis.Ids("TS0004", Point + """
                                              public static class C
                                              {
                                                  public static int Sum(Point point)
                                                  {
                                                      int x, y;
                                                      (x, y) = point;
                                                      return x + y;
                                                  }
                                              }
                                              """)).Should().Equal("TS0004");

    [Fact]
    public async Task ADeconstructionInsideATupleDeconstructionIsReported() =>
        (await Analysis.Ids("TS0004", Point + """
                                              public static class C
                                              {
                                                  public static int Sum(Point point)
                                                  {
                                                      var (scale, (x, y)) = (2, point);
                                                      return scale * (x + y);
                                                  }
                                              }
                                              """)).Should().Equal("TS0004");

    [Fact]
    public async Task AForEachThatDeconstructsARecordIsReported() =>
        (await Analysis.Ids("TS0004", Point + """
                                              public static class C
                                              {
                                                  public static int Sum(Point[] points)
                                                  {
                                                      int sum = 0;
                                                      foreach (var (x, y) in points)
                                                          sum += x + y;
                                                      return sum;
                                                  }
                                              }
                                              """)).Should().Equal("TS0004");

    [Fact]
    public async Task ARecordPatternInsideATuplePatternIsReportedOnce() =>
        (await Analysis.Ids("TS0004", Point + """
                                              public static class C
                                              {
                                                  public static bool Both(Point point, int count) =>
                                                      (point, count) is (Point(1, 2), 3);
                                              }
                                              """)).Should().Equal(["TS0004"], "the tuple pattern stays positional");

    [Fact]
    public async Task TheMessageNamesTheSectionOfTheGuide() =>
        (await Analysis.Diagnostics("TS0004", Point + """
                                                      public static class C
                                                      {
                                                          public static bool Origin(object value) => value is Point(0, 0);
                                                      }
                                                      """)).Single().GetMessage().Should()
        .Contain("Point").And.Contain("(STYLE.md, \"Use pattern matching and destructuring\")");

    [Fact]
    public async Task APositionalPatternWithOneElementIsNotReported() =>
        (await Analysis.Ids("TS0004", """
                                      public readonly record struct EndPose(int Pose);
                                      public static class C
                                      {
                                          public static int Pose(object value) => value is EndPose(var pose) ? pose : 0;
                                      }
                                      """)).Should().BeEmpty();

    [Fact]
    public async Task ATuplePatternIsNotReported() =>
        (await Analysis.Ids("TS0004", """
                                      public static class C
                                      {
                                          public static int Next(int state, bool go) => (state, go) switch
                                          {
                                              (0, true) => 1,
                                              _ => state
                                          };
                                      }
                                      """)).Should().BeEmpty();

    [Fact]
    public async Task ADeconstructionOfATupleIsNotReported() =>
        (await Analysis.Ids("TS0004", """
                                      public static class C
                                      {
                                          public static (int Count, int Total) Stats() => (1, 2);
                                          public static int Sum()
                                          {
                                              var (count, total) = Stats();
                                              (count, total) = (total, count);
                                              return count + total;
                                          }
                                      }
                                      """)).Should().BeEmpty();

    [Fact]
    public async Task AForEachThatDeconstructsTuplesIsNotReported() =>
        (await Analysis.Ids("TS0004", """
                                      public static class C
                                      {
                                          public static int Sum((int A, int B)[] pairs)
                                          {
                                              int sum = 0;
                                              foreach (var (a, b) in pairs)
                                                  sum += a + b;
                                              return sum;
                                          }
                                      }
                                      """)).Should().BeEmpty();

    [Fact]
    public async Task APropertyPatternIsNotReported() =>
        (await Analysis.Ids("TS0004", Point + """
                                              public static class C
                                              {
                                                  public static bool Origin(object value) => value is Point { X: 0, Y: 0 };
                                              }
                                              """)).Should().BeEmpty();

    [Fact]
    public async Task ARecordOfAnotherAssemblyOfTheSolutionIsReported()
    {
        CSharpCompilation library = TestCompilation.Of(Point, assembly: "Tertius.Library", libraries: []);
        CSharpCompilation probe = TestCompilation.Of("""
                                                     public static class C
                                                     {
                                                         public static bool Origin(Point point) => point is (0, 0);
                                                     }
                                                     """, assembly: "Probe", libraries: [library]);

        (await Analysis.Diagnostics("TS0004", probe)).Select(diagnostic => diagnostic.Id).Should().Equal("TS0004");
    }

    [Fact]
    public async Task ARecordOfAnAssemblyOutsideTheSolutionIsNotReported()
    {
        CSharpCompilation library = TestCompilation.Of(Point, assembly: "Library", libraries: []);
        CSharpCompilation probe = TestCompilation.Of("""
                                                     public static class C
                                                     {
                                                         public static bool Origin(Point point) => point is (0, 0);
                                                     }
                                                     """, assembly: "Probe", libraries: [library]);

        (await Analysis.Diagnostics("TS0004", probe)).Should().BeEmpty();
    }

    [Fact]
    public async Task AForEachThatDeconstructsKeyValuePairsIsNotReported() =>
        (await Analysis.Ids("TS0004", """
                                      using System.Collections.Generic;
                                      public static class C
                                      {
                                          public static int Sum(Dictionary<string, int> counts)
                                          {
                                              int sum = 0;
                                              foreach ((string name, int count) in counts)
                                                  sum += name.Length + count;
                                              return sum;
                                          }
                                      }
                                      """)).Should()
        .BeEmpty("KeyValuePair is a framework type that never reorders its fields");

    [Fact]
    public async Task APositionalPatternOnAnObjectThroughITupleIsNotReported() =>
        (await Analysis.Ids("TS0004", """
                                      public static class C
                                      {
                                          public static bool Pair(object value) => value is (1, 2);
                                      }
                                      """)).Should().BeEmpty();

    // The code fix

    [Fact]
    public async Task TheFixNamesEachElementWithItsProperty() =>
        (await Fixes.PropertyPattern.First(Point + """
                                                   public static class C
                                                   {
                                                       public static int Sum(object value) => value is Point(var x, var y) ? x + y : 0;
                                                   }
                                                   """)).Should().Be(Point + """
                                                                             public static class C
                                                                             {
                                                                                 public static int Sum(object value) => value is Point { X: var x, Y: var y } ? x + y : 0;
                                                                             }
                                                                             """);

    [Fact]
    public async Task TheFixDropsDiscardsAndKeepsConstantsAndTheDesignation() =>
        (await Fixes.PropertyPattern.First("""
                                           public readonly record struct Key(int Code, bool Down, int Time);
                                           public static class C
                                           {
                                               public static int Code(object value) => value is Key(var code, true, _) key ? code + key.Time : 0;
                                           }
                                           """)).Should().Be("""
                                                             public readonly record struct Key(int Code, bool Down, int Time);
                                                             public static class C
                                                             {
                                                                 public static int Code(object value) => value is Key { Code: var code, Down: true } key ? code + key.Time : 0;
                                                             }
                                                             """);

    [Fact]
    public async Task TheFixOfAPatternWithoutATypeWritesOnlyTheBraces() =>
        (await Fixes.PropertyPattern.First(Point + """
                                                   public static class C
                                                   {
                                                       public static bool Origin(Point point) => point is (0, 0);
                                                   }
                                                   """)).Should().Be(Point + """
                                                                             public static class C
                                                                             {
                                                                                 public static bool Origin(Point point) => point is { X: 0, Y: 0 };
                                                                             }
                                                                             """);

    [Fact]
    public async Task TheFixMergesAnExistingPropertyPatternAndReadsNamedElements() =>
        (await Fixes.PropertyPattern.First("""
                                           public readonly record struct Box(int Width, int Height)
                                           {
                                               public int Depth { get; init; }
                                           }
                                           public static class C
                                           {
                                               public static bool Flat(object value) => value is Box(Width: var width, Height: 1) { Depth: 0 } && width > 0;
                                           }
                                           """)).Should().Be("""
                                                             public readonly record struct Box(int Width, int Height)
                                                             {
                                                                 public int Depth { get; init; }
                                                             }
                                                             public static class C
                                                             {
                                                                 public static bool Flat(object value) => value is Box { Width: var width, Height: 1, Depth: 0 } && width > 0;
                                                             }
                                                             """);

    [Fact]
    public async Task TheFixMatchesTheParametersOfAHandWrittenDeconstructToPropertiesIgnoringCase() =>
        (await Fixes.PropertyPattern.First("""
                                           public sealed class Span2
                                           {
                                               public int Start { get; init; }
                                               public int Count { get; init; }
                                               public void Deconstruct(out int start, out int count) => (start, count) = (Start, Count);
                                           }
                                           public static class C
                                           {
                                               public static int End(object value) => value is Span2(var start, var count) ? start + count : 0;
                                           }
                                           """)).Should().Be("""
                                                             public sealed class Span2
                                                             {
                                                                 public int Start { get; init; }
                                                                 public int Count { get; init; }
                                                                 public void Deconstruct(out int start, out int count) => (start, count) = (Start, Count);
                                                             }
                                                             public static class C
                                                             {
                                                                 public static int End(object value) => value is Span2 { Start: var start, Count: var count } ? start + count : 0;
                                                             }
                                                             """);

    [Fact]
    public async Task TheFixWritesAnElementThatSpansTwoLinesOnOneLine() =>
        (await Fixes.PropertyPattern.First(Point + """
                                                   public static class C
                                                   {
                                                       public static int Sum(object value) => value is Point(var x, var
                                                           y) ? x + y : 0;
                                                   }
                                                   """)).Should().Be(Point + """
                                                                             public static class C
                                                                             {
                                                                                 public static int Sum(object value) => value is Point { X: var x, Y: var y } ? x + y : 0;
                                                                             }
                                                                             """);

    [Fact]
    public async Task TheFixOffersNothingWhenAnElementNamesNoMember() =>
        (await Fixes.PropertyPattern.Offered("""
                                             public sealed class Span2
                                             {
                                                 public int First { get; init; }
                                                 public int Length { get; init; }
                                                 public void Deconstruct(out int start, out int count) => (start, count) = (First, Length);
                                             }
                                             public static class C
                                             {
                                                 public static int End(object value) => value is Span2(var start, var count) ? start + count : 0;
                                             }
                                             """)).Should().Be(0, "no member is named start or count");

    [Fact]
    public async Task TheFixAllRewritesTheOuterOfTwoNestedPatternsInOnePass() =>
        (await Fixes.PropertyPattern.AllOnce(Point + """
                                                     public readonly record struct Line(Point From, Point To);
                                                     public static class C
                                                     {
                                                         public static int Size(object value) => value is Line(Point(var x, var y), var to) ? x + y + to.X : 0;
                                                     }
                                                     """)).Should().Be(Point + """
                                                                               public readonly record struct Line(Point From, Point To);
                                                                               public static class C
                                                                               {
                                                                                   public static int Size(object value) => value is Line { From: Point(var x, var y), To: var to } ? x + y + to.X : 0;
                                                                               }
                                                                               """,
            "the nested pattern waits for the next pass");

    [Fact]
    public async Task TheFixAllRewritesEveryArmOfASwitch() =>
        (await Fixes.PropertyPattern.All(Point + """
                                                 public readonly record struct Line(Point From, Point To);
                                                 public static class C
                                                 {
                                                     public static int Size(object value) => value switch
                                                     {
                                                         Point(var x, var y) => x + y,
                                                         Line(_, var to) => to.X,
                                                         _ => 0
                                                     };
                                                 }
                                                 """)).Should().Be(Point + """
                                                                           public readonly record struct Line(Point From, Point To);
                                                                           public static class C
                                                                           {
                                                                               public static int Size(object value) => value switch
                                                                               {
                                                                                   Point { X: var x, Y: var y } => x + y,
                                                                                   Line { To: var to } => to.X,
                                                                                   _ => 0
                                                                               };
                                                                           }
                                                                           """);
}
