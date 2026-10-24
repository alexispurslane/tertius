using AwesomeAssertions;

namespace Tertius.Style.Tests;

/// <summary>
/// TS0005: a default method of an interface that a type parameter constrained to <c>struct</c> or <c>unmanaged</c>
/// names, without <c>[MethodImpl(MethodImplOptions.AggressiveInlining)]</c>. A call of such a method on the struct
/// boxes it unless the JIT inlines the method.
/// </summary>
public sealed class InlinedDefaultMethodTests
{
    private const string Filter = """
                                  public interface IFilter
                                  {
                                      bool Matches(int value);
                                      bool MatchesAny(int[] values) => System.Array.Exists(values, Matches);
                                  }

                                  """;

    [Fact]
    public async Task ADefaultMethodOfAnInterfaceInAStructConstraintIsReported() =>
        (await Analysis.Ids("TS0005", Filter + """
                                               public static class Scan
                                               {
                                                   public static bool Any<T>(T filter, int[] values) where T : struct, IFilter =>
                                                       filter.MatchesAny(values);
                                               }
                                               """)).Should().Equal("TS0005");

    [Fact]
    public async Task AnUnmanagedConstraintCountsAsAStructConstraint() =>
        (await Analysis.Ids("TS0005", Filter + """
                                               public static class Scan
                                               {
                                                   public static bool Any<T>(T filter, int[] values) where T : unmanaged, IFilter =>
                                                       filter.MatchesAny(values);
                                               }
                                               """)).Should().Equal("TS0005");

    [Fact]
    public async Task AConstraintOnATypeParameterOfATypeCounts() =>
        (await Analysis.Ids("TS0005", Filter + """
                                               public struct Cursor<T> where T : struct, IFilter
                                               {
                                                   private T filter;
                                                   public bool Any(int[] values) => filter.MatchesAny(values);
                                               }
                                               """)).Should().Equal("TS0005");

    [Fact]
    public async Task ADefaultMethodOfABaseInterfaceIsReported() =>
        (await Analysis.Ids("TS0005", """
                                      public interface IBase
                                      {
                                          int Count() => 1;
                                      }
                                      public interface IFilter : IBase;
                                      public static class Scan
                                      {
                                          public static int Count<T>(T filter) where T : struct, IFilter => filter.Count();
                                      }
                                      """)).Should().Equal(["TS0005"],
            "a struct that implements IFilter gets the default method of IBase too");

    [Fact]
    public async Task TheMessageNamesTheMethodAndTheSection() =>
        (await Analysis.Diagnostics("TS0005", Filter + """
                                                       public static class Scan
                                                       {
                                                           public static bool Any<T>(T filter, int[] values) where T : struct, IFilter =>
                                                               filter.MatchesAny(values);
                                                       }
                                                       """)).Single().GetMessage().Should()
        .Contain("IFilter.MatchesAny").And.Contain("(STYLE.md, \"Composition\")");

    [Fact]
    public async Task ADefaultMethodMarkedForInliningIsNotReported() =>
        (await Analysis.Ids("TS0005", """
                                      using System.Runtime.CompilerServices;
                                      public interface IFilter
                                      {
                                          bool Matches(int value);
                                          [MethodImpl(MethodImplOptions.AggressiveInlining | MethodImplOptions.AggressiveOptimization)]
                                          bool MatchesAny(int[] values) => System.Array.Exists(values, Matches);
                                      }
                                      public static class Scan
                                      {
                                          public static bool Any<T>(T filter, int[] values) where T : struct, IFilter =>
                                              filter.MatchesAny(values);
                                      }
                                      """)).Should().BeEmpty();

    [Fact]
    public async Task ADefaultMethodMarkedWithAnotherOptionIsReported() =>
        (await Analysis.Ids("TS0005", """
                                      using System.Runtime.CompilerServices;
                                      public interface IFilter
                                      {
                                          bool Matches(int value);
                                          [MethodImpl(MethodImplOptions.AggressiveOptimization)]
                                          bool MatchesAny(int[] values) => System.Array.Exists(values, Matches);
                                      }
                                      public static class Scan
                                      {
                                          public static bool Any<T>(T filter, int[] values) where T : struct, IFilter =>
                                              filter.MatchesAny(values);
                                      }
                                      """)).Should().Equal("TS0005");

    [Fact]
    public async Task AnInterfaceWithoutAStructConstraintIsNotReported() =>
        (await Analysis.Ids("TS0005", Filter + """
                                               public static class Scan
                                               {
                                                   public static bool Any<T>(T filter, int[] values) where T : IFilter =>
                                                       filter.MatchesAny(values);
                                                   public static bool All<T>(T filter, int[] values) where T : class, IFilter =>
                                                       filter.MatchesAny(values);
                                                   public static bool Plain(IFilter filter, int[] values) => filter.MatchesAny(values);
                                               }
                                               """)).Should().BeEmpty();

    [Fact]
    public async Task AnAbstractOrStaticInterfaceMethodIsNotReported() =>
        (await Analysis.Ids("TS0005", """
                                      public interface IFilter
                                      {
                                          bool Matches(int value);
                                          static int Zero() => 0;
                                      }
                                      public static class Scan
                                      {
                                          public static bool One<T>(T filter) where T : struct, IFilter => filter.Matches(1);
                                      }
                                      """)).Should().BeEmpty();

    [Fact]
    public async Task APrivateInterfaceMethodIsNotReported() =>
        (await Analysis.Ids("TS0005", """
                                      using System.Runtime.CompilerServices;
                                      public interface IFilter
                                      {
                                          bool Matches(int value);
                                          [MethodImpl(MethodImplOptions.AggressiveInlining)]
                                          bool MatchesTwo(int value) => Matches(value) && Matches(Twice(value));
                                          private int Twice(int value) => 2 * value;
                                      }
                                      public static class Scan
                                      {
                                          public static bool Two<T>(T filter) where T : struct, IFilter => filter.MatchesTwo(1);
                                      }
                                      """)).Should().BeEmpty("no caller outside the interface can call a private method");

    // The code fix

    [Fact]
    public async Task TheFixPutsTheUsingAmongTheOthersInOrder() =>
        (await Fixes.Inline.First("""
                                  // A header.
                                  using System;
                                  using System.Text;
                                  public interface IFilter
                                  {
                                      bool Matches(int value);
                                      bool MatchesAny(int[] values) => Array.Exists(values, Matches);
                                  }
                                  public static class Scan
                                  {
                                      public static bool One<T>(T filter) where T : struct, IFilter => filter.MatchesAny([1]);
                                      public static StringBuilder Text() => new();
                                  }
                                  """)).Should().Be("""
                                                    // A header.
                                                    using System;
                                                    using System.Runtime.CompilerServices;
                                                    using System.Text;
                                                    public interface IFilter
                                                    {
                                                        bool Matches(int value);
                                                        [MethodImpl(MethodImplOptions.AggressiveInlining)]
                                                        bool MatchesAny(int[] values) => Array.Exists(values, Matches);
                                                    }
                                                    public static class Scan
                                                    {
                                                        public static bool One<T>(T filter) where T : struct, IFilter => filter.MatchesAny([1]);
                                                        public static StringBuilder Text() => new();
                                                    }
                                                    """);

    [Fact]
    public async Task TheFixPutsAFirstUsingAfterTheFileHeader() =>
        (await Fixes.Inline.First("""
                                  // A header.
                                  using System.Text;
                                  public interface IFilter
                                  {
                                      bool Matches(int value);
                                      bool MatchesAny(int[] values) => System.Array.Exists(values, Matches);
                                  }
                                  public static class Scan
                                  {
                                      public static bool One<T>(T filter) where T : struct, IFilter => filter.MatchesAny([1]);
                                      public static StringBuilder Text() => new();
                                  }
                                  """)).Should().Be("""
                                                    // A header.
                                                    using System.Runtime.CompilerServices;
                                                    using System.Text;
                                                    public interface IFilter
                                                    {
                                                        bool Matches(int value);
                                                        [MethodImpl(MethodImplOptions.AggressiveInlining)]
                                                        bool MatchesAny(int[] values) => System.Array.Exists(values, Matches);
                                                    }
                                                    public static class Scan
                                                    {
                                                        public static bool One<T>(T filter) where T : struct, IFilter => filter.MatchesAny([1]);
                                                        public static StringBuilder Text() => new();
                                                    }
                                                    """);

    [Fact]
    public async Task TheFixMarksTheMethodAndAddsTheUsing() =>
        (await Fixes.Inline.First("""
                                  public interface IFilter
                                  {
                                      bool Matches(int value);

                                      /// <summary>True when a value matches.</summary>
                                      bool MatchesAny(int[] values) => System.Array.Exists(values, Matches);
                                  }
                                  public static class Scan
                                  {
                                      public static bool Any<T>(T filter, int[] values) where T : struct, IFilter =>
                                          filter.MatchesAny(values);
                                  }
                                  """)).Should().Be("""
                                                    using System.Runtime.CompilerServices;

                                                    public interface IFilter
                                                    {
                                                        bool Matches(int value);

                                                        /// <summary>True when a value matches.</summary>
                                                        [MethodImpl(MethodImplOptions.AggressiveInlining)]
                                                        bool MatchesAny(int[] values) => System.Array.Exists(values, Matches);
                                                    }
                                                    public static class Scan
                                                    {
                                                        public static bool Any<T>(T filter, int[] values) where T : struct, IFilter =>
                                                            filter.MatchesAny(values);
                                                    }
                                                    """);

    [Fact]
    public async Task TheFixKeepsAnExistingUsingAndOtherAttributes() =>
        (await Fixes.Inline.First("""
                                  using System.Runtime.CompilerServices;
                                  namespace N;
                                  public interface IFilter
                                  {
                                      bool Matches(int value);
                                      [System.Obsolete]
                                      bool MatchesAny(int[] values) => System.Array.Exists(values, Matches);
                                  }
                                  public static class Scan
                                  {
                                      public static bool Any<T>(T filter, int[] values) where T : struct, IFilter =>
                                          filter.MatchesAny(values);
                                  }
                                  """)).Should().Be("""
                                                    using System.Runtime.CompilerServices;
                                                    namespace N;
                                                    public interface IFilter
                                                    {
                                                        bool Matches(int value);
                                                        [System.Obsolete]
                                                        [MethodImpl(MethodImplOptions.AggressiveInlining)]
                                                        bool MatchesAny(int[] values) => System.Array.Exists(values, Matches);
                                                    }
                                                    public static class Scan
                                                    {
                                                        public static bool Any<T>(T filter, int[] values) where T : struct, IFilter =>
                                                            filter.MatchesAny(values);
                                                    }
                                                    """);

    [Fact]
    public async Task TheFixKeepsTheLineEndsOfTheFile()
    {
        const string source = """
                              public interface IFilter
                              {
                                  bool Matches(int value);
                                  bool MatchesAny(int[] values) => System.Array.Exists(values, Matches);
                              }
                              public static class Scan
                              {
                                  public static bool One<T>(T filter) where T : struct, IFilter => filter.MatchesAny([1]);
                              }
                              """;

        string fixedSource = await Fixes.Inline.First(source.ReplaceLineEndings("\r\n"));

        fixedSource.Replace("\r\n", "").Should().NotContain("\n", "every line of the file ends in CR LF");
    }

    [Fact]
    public async Task TheFixAllMarksEveryMethod() =>
        (await Fixes.Inline.All("""
                                using System.Runtime.CompilerServices;
                                public interface IFilter
                                {
                                    bool Matches(int value);
                                    bool MatchesAny(int[] values) => System.Array.Exists(values, Matches);
                                    bool MatchesAll(int[] values) => System.Array.TrueForAll(values, Matches);
                                }
                                public static class Scan
                                {
                                    public static bool One<T>(T filter) where T : struct, IFilter => filter.Matches(1);
                                }
                                """)).Should().Be("""
                                                  using System.Runtime.CompilerServices;
                                                  public interface IFilter
                                                  {
                                                      bool Matches(int value);
                                                      [MethodImpl(MethodImplOptions.AggressiveInlining)]
                                                      bool MatchesAny(int[] values) => System.Array.Exists(values, Matches);
                                                      [MethodImpl(MethodImplOptions.AggressiveInlining)]
                                                      bool MatchesAll(int[] values) => System.Array.TrueForAll(values, Matches);
                                                  }
                                                  public static class Scan
                                                  {
                                                      public static bool One<T>(T filter) where T : struct, IFilter => filter.Matches(1);
                                                  }
                                                  """);
}
