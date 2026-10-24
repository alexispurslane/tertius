using AwesomeAssertions;

namespace Tertius.Style.Tests;

/// <summary>
/// The code fix of TS0002 names every argument that binds to a parameter by its position, with the name of that
/// parameter, and keeps the order of the arguments, their modifiers and their layout.
/// </summary>
public class NameArgumentsFixTests
{
    [Fact]
    public async Task EveryPositionalArgumentGetsTheNameOfItsParameter() =>
        (await Fixes.First("""
                           public static class C
                           {
                               public static void Call(int a, int b, int c, int d) { }
                               public static void Run() => Call(1, 2, 3, 4);
                           }
                           """)).Should().Be("""
                                             public static class C
                                             {
                                                 public static void Call(int a, int b, int c, int d) { }
                                                 public static void Run() => Call(a: 1, b: 2, c: 3, d: 4);
                                             }
                                             """);

    [Fact]
    public async Task ANamedArgumentKeepsItsNameAndItsPlace() =>
        (await Fixes.First("""
                           public static class C
                           {
                               public static void Call(int a, int b, int c, int d) { }
                               public static void Run() => Call(1, b: 2, 3, 4);
                           }
                           """)).Should().Be("""
                                             public static class C
                                             {
                                                 public static void Call(int a, int b, int c, int d) { }
                                                 public static void Run() => Call(a: 1, b: 2, c: 3, d: 4);
                                             }
                                             """);

    [Fact]
    public async Task OutRefAndInArgumentsKeepTheirModifiersAfterTheName() =>
        (await Fixes.First("""
                           public static class C
                           {
                               public static void Call(out int a, ref int b, in int c, int d) => a = b + c + d;
                               public static void Run(int y, int z) => Call(out var x, ref y, in z, 4);
                           }
                           """)).Should().Be("""
                                             public static class C
                                             {
                                                 public static void Call(out int a, ref int b, in int c, int d) => a = b + c + d;
                                                 public static void Run(int y, int z) => Call(a: out var x, b: ref y, c: in z, d: 4);
                                             }
                                             """);

    [Fact]
    public async Task AParameterNamedWithAKeywordIsEscaped() =>
        (await Fixes.First("""
                           public static class C
                           {
                               public static void Call(int @object, int @in, int value, int d) { }
                               public static void Run() => Call(1, 2, 3, 4);
                           }
                           """)).Should().Be("""
                                             public static class C
                                             {
                                                 public static void Call(int @object, int @in, int value, int d) { }
                                                 public static void Run() => Call(@object: 1, @in: 2, value: 3, d: 4);
                                             }
                                             """);

    [Fact]
    public async Task ArgumentsOfTheParamsListStayPositional() =>
        (await Fixes.First("""
                           public static class C
                           {
                               public static void Sum(int a, int b, int c, int d, params int[] rest) { }
                               public static void Run() => Sum(1, 2, 3, 4, 5, 6);
                           }
                           """)).Should().Be("""
                                             public static class C
                                             {
                                                 public static void Sum(int a, int b, int c, int d, params int[] rest) { }
                                                 public static void Run() => Sum(a: 1, b: 2, c: 3, d: 4, 5, 6);
                                             }
                                             """);

    [Fact]
    public async Task ArgumentsOnTheirOwnLinesKeepTheirIndentation() =>
        (await Fixes.First("""
                           public static class C
                           {
                               public static void Call(int a, int b, int c, int d) { }
                               public static void Run() =>
                                   Call(
                                       1,
                                       // The second.
                                       2,
                                       3, 4);
                           }
                           """)).Should().Be("""
                                             public static class C
                                             {
                                                 public static void Call(int a, int b, int c, int d) { }
                                                 public static void Run() =>
                                                     Call(
                                                         a: 1,
                                                         // The second.
                                                         b: 2,
                                                         c: 3, d: 4);
                                             }
                                             """);

    [Fact]
    public async Task AConstructorInitializerIsFixed() =>
        (await Fixes.First("""
                           public class C
                           {
                               public C(int a, int b, int c, int d) { }
                               public C() : this(1, 2, 3, 4) { }
                           }
                           """)).Should().Be("""
                                             public class C
                                             {
                                                 public C(int a, int b, int c, int d) { }
                                                 public C() : this(a: 1, b: 2, c: 3, d: 4) { }
                                             }
                                             """);

    [Fact]
    public async Task APrimaryConstructorBaseCallIsFixed() =>
        (await Fixes.First("""
                           public class B(int a, int b, int c, int d);
                           public class C() : B(1, 2, 3, 4);
                           """)).Should().Be("""
                                             public class B(int a, int b, int c, int d);
                                             public class C() : B(a: 1, b: 2, c: 3, d: 4);
                                             """);

    [Fact]
    public async Task AnAttributeIsFixedAndKeepsItsProperties() =>
        (await Fixes.First("""
                           public sealed class MarkAttribute(int a, int b, int c, int d) : System.Attribute
                           {
                               public string Note { get; set; } = "";
                           }
                           [Mark(1, 2, 3, 4, Note = "x")]
                           public static class C;
                           """)).Should().Be("""
                                             public sealed class MarkAttribute(int a, int b, int c, int d) : System.Attribute
                                             {
                                                 public string Note { get; set; } = "";
                                             }
                                             [Mark(a: 1, b: 2, c: 3, d: 4, Note = "x")]
                                             public static class C;
                                             """);

    [Fact]
    public async Task FixingEveryCallAtOnceNamesTheArgumentsOfNestedCalls() =>
        (await Fixes.All("""
                         public static class C
                         {
                             public static int Get(int a, int b, int c, int d) => a;
                             public static void Run() => Get(Get(1, 2, 3, 4), 2, Get(5, 6, 7, 8), 4);
                         }
                         """)).Should().Be("""
                                           public static class C
                                           {
                                               public static int Get(int a, int b, int c, int d) => a;
                                               public static void Run() => Get(a: Get(a: 1, b: 2, c: 3, d: 4), b: 2, c: Get(a: 5, b: 6, c: 7, d: 8), d: 4);
                                           }
                                           """);
}
