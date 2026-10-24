using AwesomeAssertions;
using Microsoft.CodeAnalysis;

namespace Tertius.Style.Tests;

/// <summary>
/// TS0001: a tuple type with an element that has no name, where the code writes the type, and where a declaration
/// infers it. A deconstruction, and a tuple literal that a named type takes, are not reported.
/// </summary>
public sealed class TupleElementNameTests
{
    // Written tuple types

    [Fact]
    public async Task AFieldWhoseTupleHasNoNamesIsReported() =>
        (await Analysis.Ids("TS0001", """
                                      public class C
                                      {
                                          public (int, double) Pair;
                                      }
                                      """)).Should().Equal("TS0001");

    [Fact]
    public async Task AReturnTypeWithOneUnnamedElementIsReported() =>
        (await Analysis.Ids("TS0001", """
                                      public static class C
                                      {
                                          public static (int Count, double) Stats() => (1, 2.0);
                                      }
                                      """)).Should().Equal("TS0001");

    [Fact]
    public async Task AParameterWhoseTupleHasNoNamesIsReported() =>
        (await Analysis.Ids("TS0001", """
                                      public static class C
                                      {
                                          public static int First((int, int) pair) => 0;
                                      }
                                      """)).Should().Equal("TS0001");

    [Fact]
    public async Task AnUnnamedTupleAsATypeArgumentIsReported() =>
        (await Analysis.Ids("TS0001", """
                                      using System.Collections.Generic;
                                      public class C
                                      {
                                          public Dictionary<(int, int), string> Cells = new();
                                      }
                                      """)).Should().Equal("TS0001");

    [Fact]
    public async Task ALocalWithAnExplicitUnnamedTupleTypeIsReported() =>
        (await Analysis.Ids("TS0001", """
                                      public static class C
                                      {
                                          public static int Sum()
                                          {
                                              (int, int) pair = (1, 2);
                                              return pair.Item1 + pair.Item2;
                                          }
                                      }
                                      """)).Should().Equal("TS0001");

    [Fact]
    public async Task ATupleNestedInANamedTupleIsReportedOnce() =>
        (await Analysis.Ids("TS0001", """
                                      public class C
                                      {
                                          public (int Id, (double, double) Range) Row;
                                      }
                                      """)).Should().Equal(["TS0001"],
            "the outer tuple names its elements, the inner one does not");

    [Fact]
    public async Task TheMessageNamesTheTupleType() =>
        (await Analysis.Diagnostics("TS0001", """
                                              public class C
                                              {
                                                  public (int Count, double) Pair;
                                              }
                                              """)).Single().GetMessage().Should()
        .StartWith("The tuple type '(int Count, double)'");

    // Inferred tuple types

    [Fact]
    public async Task AVarLocalThatInfersAnUnnamedTupleIsReported() =>
        (await Analysis.Ids("TS0001", """
                                      public static class C
                                      {
                                          public static int Sum()
                                          {
                                              var pair = System.ValueTuple.Create(1, 2);
                                              return pair.Item1;
                                          }
                                      }
                                      """)).Should().Equal("TS0001");

    [Fact]
    public async Task AVarLocalOfATupleLiteralWithAnUnnamedElementIsReported() =>
        (await Analysis.Ids("TS0001", """
                                      public static class C
                                      {
                                          public static int Sum(int count)
                                          {
                                              var pair = (count, 2);
                                              return pair.count;
                                          }
                                      }
                                      """)).Should().Equal(["TS0001"],
            "the literal infers the name count, but not a name for 2");

    [Fact]
    public async Task AForEachVariableThatInfersAnUnnamedTupleIsReported() =>
        (await Analysis.Ids("TS0001", """
                                      public static class C
                                      {
                                          public static int Sum(System.ValueTuple<int, int>[] pairs)
                                          {
                                              int sum = 0;
                                              foreach (var pair in pairs)
                                                  sum += pair.Item1;
                                              return sum;
                                          }
                                      }
                                      """)).Should().Equal("TS0001");

    [Fact]
    public async Task AnOutVarThatInfersAnUnnamedTupleIsReported() =>
        (await Analysis.Ids("TS0001", """
                                      public static class C
                                      {
                                          private static void Get(out System.ValueTuple<int, int> pair) => pair = default;
                                          public static int First()
                                          {
                                              Get(out var pair);
                                              return pair.Item1;
                                          }
                                      }
                                      """)).Should().Equal("TS0001");

    [Fact]
    public async Task AVarPatternThatInfersAnUnnamedTupleIsReported() =>
        (await Analysis.Ids("TS0001", """
                                      public static class C
                                      {
                                          public static int First() =>
                                              System.ValueTuple.Create(1, 2) is var pair ? pair.Item1 : 0;
                                      }
                                      """)).Should().Equal("TS0001");

    [Fact]
    public async Task ADeconstructionIntoAnUnnamedTupleIsReported() =>
        (await Analysis.Ids("TS0001", """
                                      public static class C
                                      {
                                          public static int First()
                                          {
                                              var (pair, count) = (System.ValueTuple.Create(1, 2), 3);
                                              return pair.Item1 + count;
                                          }
                                      }
                                      """)).Should().Equal(["TS0001"], "pair is an unnamed tuple, count is an int");

    [Fact]
    public async Task ANestedDeconstructionIntoAnUnnamedTupleIsReported() =>
        (await Analysis.Ids("TS0001", """
                                      public static class C
                                      {
                                          public static int First()
                                          {
                                              var ((pair, count), more) = ((System.ValueTuple.Create(1, 2), 3), 4);
                                              return pair.Item1 + count + more;
                                          }
                                      }
                                      """)).Should().Equal("TS0001");

    [Fact]
    public async Task APropertyPatternThatInfersAnUnnamedTupleIsReported() =>
        (await Analysis.Ids("TS0001", """
                                      public static class C
                                      {
                                          public static int First() =>
                                              System.ValueTuple.Create(1, 2) is { } pair ? pair.Item1 : 0;
                                      }
                                      """)).Should().Equal("TS0001");

    [Fact]
    public async Task AVarLocalOfASequenceOfUnnamedTuplesIsReported() =>
        (await Analysis.Ids("TS0001", """
                                      using System.Linq;
                                      public static class C
                                      {
                                          public static int Count()
                                          {
                                              var pairs = Enumerable.Repeat(System.ValueTuple.Create(1, 2), 3);
                                              return pairs.Count();
                                          }
                                      }
                                      """)).Should().Equal(["TS0001"], "the tuple is a type argument of the sequence");

    [Fact]
    public async Task AVarLocalOfAnArrayOfUnnamedTuplesIsReported() =>
        (await Analysis.Ids("TS0001", """
                                      public static class C
                                      {
                                          public static int Count()
                                          {
                                              var pairs = new[] { System.ValueTuple.Create(1, 2) };
                                              return pairs.Length;
                                          }
                                      }
                                      """)).Should().Equal("TS0001");

    [Fact]
    public async Task AnUnnamedTupleInsideANamedOneIsReportedForAVarLocal() =>
        (await Analysis.Ids("TS0001", """
                                      public static class C
                                      {
                                          public static double Low()
                                          {
                                              var row = (Id: 1, Range: System.ValueTuple.Create(1.0, 2.0));
                                              return row.Range.Item1;
                                          }
                                      }
                                      """)).Should().Equal("TS0001");

    [Fact]
    public async Task AnInferredTupleIsReportedAtTheNameOfItsVariable()
    {
        const string source = """
                              public static class C
                              {
                                  public static int Sum()
                                  {
                                      var pair = System.ValueTuple.Create(1, 2);
                                      return pair.Item1;
                                  }
                              }
                              """;

        Diagnostic found = (await Analysis.Diagnostics("TS0001", source)).Single();

        source.Substring(found.Location.SourceSpan.Start, found.Location.SourceSpan.Length).Should().Be("pair");
        found.GetMessage().Should().StartWith("The tuple type '(int, int)'");
    }

    [Fact]
    public async Task ALambdaParameterThatInfersAnUnnamedTupleIsReported() =>
        (await Analysis.Ids("TS0001", """
                                      using System.Linq;
                                      public static class C
                                      {
                                          public static int Count(int[] items) =>
                                              items.Select(item => (item, 1)).Count(pair => pair.Item2 > 0);
                                      }
                                      """)).Should().Equal("TS0001");

    // Near misses

    [Fact]
    public async Task ATupleWithEveryElementNamedIsNotReported() =>
        (await Analysis.Ids("TS0001", """
                                      public static class C
                                      {
                                          public static (int Count, double Mean) Stats((int Low, int High) range)
                                          {
                                              (int Low, int High) copy = range;
                                              return (copy.High - copy.Low, 0.5);
                                          }
                                      }
                                      """)).Should().BeEmpty("the literal takes the names of the return type");

    [Fact]
    public async Task ADeconstructionIsNotReported() =>
        (await Analysis.Ids("TS0001", """
                                      public static class C
                                      {
                                          public static int Sum()
                                          {
                                              (var a, var b) = (1, 2);
                                              var (c, d) = (3, 4);
                                              return a + b + c + d;
                                          }
                                      }
                                      """)).Should().BeEmpty("a deconstruction names each value with its own variable");

    [Fact]
    public async Task AVarLocalWhoseTupleNamesAreInferredIsNotReported() =>
        (await Analysis.Ids("TS0001", """
                                      using System.Linq;
                                      public static class C
                                      {
                                          public static int Sum(int count, int total)
                                          {
                                              var pair = (count, total);
                                              return new[] { 1 }.Select(item => (item, Square: item * item))
                                                  .Sum(each => each.Square) + pair.total;
                                          }
                                      }
                                      """)).Should().BeEmpty();

    [Fact]
    public async Task AnExplicitlyTypedLambdaParameterIsReportedAtItsTypeOnly() =>
        (await Analysis.Ids("TS0001", """
                                      public static class C
                                      {
                                          public static System.Func<(int, int), int> First = ((int, int) pair) => pair.Item1;
                                      }
                                      """)).Should().Equal(["TS0001", "TS0001"],
            "each written tuple type is reported once");

    [Fact]
    public async Task GeneratedCodeIsNotAnalyzed() =>
        (await Analysis.Ids("TS0001", """
                                      // <auto-generated/>
                                      public class C
                                      {
                                          public (int, double) Pair;
                                      }
                                      """)).Should().BeEmpty();
}
