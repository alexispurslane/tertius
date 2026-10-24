using AwesomeAssertions;

namespace Tertius.Style.Tests;

/// <summary>
/// TS0002: a call whose target takes more than 3 parameters, with an argument that binds to a parameter by its position
/// and has no name. The parameter that is <c>params</c> does not count, and neither do its arguments.
/// </summary>
public class ArgumentNameTests
{
    private const string Targets = """
                                   public class Four
                                   {
                                       public Four(int a, int b, int c, int d) { }
                                       public Four() : this(a: 1, b: 2, c: 3, d: 4) { }
                                       public static void Call(int a, int b, int c, int d) { }
                                       public static void Optional(int a, int b, int c = 0, int d = 0) { }
                                       public static void Three(int a, int b, int c) { }
                                       public static void Log(int a, int b, int c, params int[] rest) { }
                                       public static void Sum(int a, int b, int c, int d, params int[] rest) { }
                                   }
                                   """;

    private static Task<IReadOnlyList<string>> Ids(string body) => Analysis.Ids(Targets + "\n" + body);

    // Calls that are reported

    [Fact]
    public async Task AMethodCallWithPositionalArgumentsIsReported() =>
        (await Ids("""
                   public static class Use
                   {
                       public static void Run() => Four.Call(1, 2, 3, 4);
                   }
                   """)).Should().Equal("TS0002");

    [Fact]
    public async Task ACallWithOneUnnamedArgumentAmongNamedOnesIsReported() =>
        (await Ids("""
                   public static class Use
                   {
                       public static void Run() => Four.Call(a: 1, b: 2, 3, d: 4);
                   }
                   """)).Should().Equal("TS0002");

    [Fact]
    public async Task ACallThatLeavesOutOptionalParametersIsReported() =>
        (await Ids("""
                   public static class Use
                   {
                       public static void Run() => Four.Optional(1, 2);
                   }
                   """)).Should().Equal(["TS0002"], "the rule counts the parameters of the target, not the arguments");

    [Fact]
    public async Task AConstructorCallIsReported() =>
        (await Ids("""
                   public static class Use
                   {
                       public static Four Make() => new Four(1, 2, 3, 4);
                   }
                   """)).Should().Equal("TS0002");

    [Fact]
    public async Task ATargetTypedNewIsReported() =>
        (await Ids("""
                   public static class Use
                   {
                       public static Four Make() => new(1, 2, 3, 4);
                   }
                   """)).Should().Equal("TS0002");

    [Fact]
    public async Task ABaseConstructorInitializerIsReported() =>
        (await Ids("""
                   public class Five : Four
                   {
                       public Five() : base(1, 2, 3, 4) { }
                   }
                   """)).Should().Equal("TS0002");

    [Fact]
    public async Task AThisConstructorInitializerIsReported() =>
        (await Ids("""
                   public class Five : Four
                   {
                       public Five(int a, int b, int c, int d) { }
                       public Five() : this(1, 2, 3, 4) { }
                   }
                   """)).Should().Equal("TS0002");

    [Fact]
    public async Task APrimaryConstructorBaseCallIsReported() =>
        (await Ids("""
                   public class Five(int e) : Four(e, 2, 3, 4);
                   """)).Should().Equal("TS0002");

    [Fact]
    public async Task ADelegateInvocationIsReported() =>
        (await Ids("""
                   public static class Use
                   {
                       public static void Run(System.Action<int, int, int, int> act) => act(1, 2, 3, 4);
                   }
                   """)).Should().Equal("TS0002");

    [Fact]
    public async Task AnAttributeWithPositionalArgumentsIsReported() =>
        (await Ids("""
                   public sealed class MarkAttribute(int a, int b, int c, int d) : System.Attribute;
                   [Mark(1, 2, 3, 4)]
                   public static class Use;
                   """)).Should().Equal("TS0002");

    [Fact]
    public async Task ParametersBeforeAParamsListNeedNamesWhenThereAreMoreThanThree() =>
        (await Ids("""
                   public static class Use
                   {
                       public static void Run() => Four.Sum(1, 2, 3, 4, 5, 6);
                   }
                   """)).Should().Equal("TS0002");

    [Fact]
    public async Task EachCallOfANestedPairIsReported() =>
        (await Ids("""
                   public static class Use
                   {
                       public static int Get(int a, int b, int c, int d) => a;
                       public static void Run() => Four.Call(Get(1, 2, 3, 4), 2, 3, 4);
                   }
                   """)).Should().Equal("TS0002", "TS0002");

    [Fact]
    public async Task TheMessageNamesTheTarget() =>
        (await Analysis.Diagnostics(Targets + "\npublic static class Use { public static Four Make() => new(1, 2, 3, 4); }"))
        .Single().GetMessage().Should().StartWith("'Four.Four(int, int, int, int)'");

    // Near misses

    [Fact]
    public async Task ACallToThreeParametersIsNotReported() =>
        (await Ids("""
                   public static class Use
                   {
                       public static void Run() => Four.Three(1, 2, 3);
                   }
                   """)).Should().BeEmpty();

    [Fact]
    public async Task ACallWithEveryArgumentNamedIsNotReported() =>
        (await Ids("""
                   public static class Use
                   {
                       public static Four Run()
                       {
                           Four.Call(a: 1, b: 2, c: 3, d: 4);
                           Four.Call(d: 4, c: 3, b: 2, a: 1);
                           return new(a: 1, b: 2, c: 3, d: 4);
                       }
                   }
                   """)).Should().BeEmpty();

    [Fact]
    public async Task TheParamsParameterDoesNotCountTowardTheLimit() =>
        (await Ids("""
                   public static class Use
                   {
                       public static void Run()
                       {
                           Four.Log(1, 2, 3, 4, 5, 6);
                           Four.Log(1, 2, 3, new[] { 4, 5 });
                       }
                   }
                   """)).Should().BeEmpty("every value of a params list has the same role");

    [Fact]
    public async Task ArgumentsThatBindToTheParamsParameterNeedNoNames() =>
        (await Ids("""
                   public static class Use
                   {
                       public static void Run()
                       {
                           Four.Sum(a: 1, b: 2, c: 3, d: 4, 5, 6);
                           Four.Sum(a: 1, b: 2, c: 3, d: 4, new[] { 5, 6 });
                       }
                   }
                   """)).Should().BeEmpty();

    [Fact]
    public async Task TheReceiverOfAnExtensionMethodIsNotAnArgument() =>
        (await Ids("""
                   public static class Use
                   {
                       public static int Add(this int self, int a, int b, int c) => self + a + b + c;
                       public static int Run() => 1.Add(2, 3, 4);
                   }
                   """)).Should().BeEmpty("the reduced call takes three arguments");

    [Fact]
    public async Task ACallThroughAFunctionPointerIsNotReported() =>
        (await Ids("""
                   public static unsafe class Use
                   {
                       public static void Run(delegate*<int, int, int, int, void> call) => call(1, 2, 3, 4);
                   }
                   """)).Should().BeEmpty("C# does not allow a name on an argument of a function pointer");

    [Fact]
    public async Task ADynamicCallIsNotReported() =>
        (await Ids("""
                   public static class Use
                   {
                       public static void Run(dynamic target) => target.Call(1, 2, 3, 4);
                   }
                   """)).Should().BeEmpty("a dynamic call binds at run time, so the build knows no parameters");

    [Fact]
    public async Task AnAttributeWithNamedConstructorArgumentsAndAPropertyIsNotReported() =>
        (await Ids("""
                   public sealed class MarkAttribute(int a, int b, int c, int d = 0) : System.Attribute
                   {
                       public string Note { get; set; } = "";
                   }
                   [Mark(a: 1, b: 2, c: 3, Note = "x")]
                   public static class Use;
                   """)).Should().BeEmpty("a property of an attribute is not a parameter, and always carries its name");

    [Fact]
    public async Task GeneratedCodeIsNotAnalyzed() =>
        (await Analysis.Ids("// <auto-generated/>\n" + Targets +
                            "\npublic static class Use { public static void Run() => Four.Call(1, 2, 3, 4); }"))
        .Should().BeEmpty();
}
