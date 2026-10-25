using AwesomeAssertions;
using Microsoft.CodeAnalysis.CSharp;

namespace Tertius.Style.Tests;

/// <summary>
/// TS0009: a method or a local function that returns <c>bool</c> and has at least one <c>out</c> parameter, whatever
/// its name. Three kinds of method are not reported: <c>TryGetValue</c> on a union, which the compiler calls by name; an
/// override, and an implementation of an interface member, which take their shape from the member that they implement,
/// so only that member is reported; and a method whose leading comment names a benchmark.
/// </summary>
public sealed class TryMethodTests
{
    [Fact]
    public async Task ABoolMethodWithAnOutParameterIsReportedWhateverItsName() => (
            await Analysis.Ids(
                "TS0009",
                """
                public static class C
                {
                    public static bool Find(string key, out int value) { value = key.Length; return value > 0; }
                    public static bool TryFind(string key, out int value) { value = key.Length; return value > 0; }
                }
                """
            )
        ).Should().Equal("TS0009", "TS0009");

    [Fact]
    public async Task AnyOutParameterMakesTheShapeNotOnlyTheFirst() => (
            await Analysis.Ids(
                "TS0009",
                """
                public static class C
                {
                    public static bool Split(string text, int at, out string head, out string tail)
                    {
                        head = text[..at];
                        tail = text[at..];
                        return at > 0;
                    }
                    public static bool Match(string text, out string rest, int at) { rest = text[at..]; return at > 0; }
                }
                """
            )
        ).Should().Equal("TS0009", "TS0009");

    [Fact]
    public async Task AGenericMethodWithTheShapeIsReported() => (
            await Analysis.Ids(
                "TS0009",
                """
                public sealed class C
                {
                    public bool Look<T>(T key, out T value) { value = key; return true; }
                }
                """
            )
        ).Should().Equal("TS0009");

    [Fact]
    public async Task ALocalFunctionWithTheShapeIsReported() => (
            await Analysis.Ids(
                "TS0009",
                """
                public static class C
                {
                    public static int Run()
                    {
                        return One(out int value) ? value : 0;
                        static bool One(out int value) { value = 1; return true; }
                    }
                }
                """
            )
        ).Should().Equal("TS0009");

    [Fact]
    public async Task ATryNameWithoutTheShapeIsNotReported() => (
            await Analysis.Ids(
                "TS0009",
                """
                using System.Threading.Tasks;
                public static class C
                {
                    public static int? TryParse(string text, out int value) { value = 0; return null; }
                    public static bool TryFind(string key) => key.Length > 0;
                    public static bool TryBump(ref int value) => ++value > 0;
                    public static bool TryRead(in int value) => value > 0;
                    public static void TryCopy(out int value) { value = 1; }
                    public static Task<bool> TryLoadAsync(string path) => Task.FromResult(path.Length > 0);
                    public static int Try() => 1;
                    public static int Tryout() => 1;
                }
                """
            )
        ).Should().BeEmpty();

    [Fact]
    public async Task ABoolMethodWithoutAnOutParameterIsNotReported() => (
            await Analysis.Ids(
                "TS0009",
                """
                public static class C
                {
                    public static bool Has(string key) => key.Length > 0;
                    public static bool Bump(ref int value) => ++value > 0;
                    public static bool Read(in int value) => value > 0;
                }
                """
            )
        ).Should().BeEmpty();

    [Fact]
    public async Task ANonBoolMethodWithAnOutParameterIsNotReported() => (
            await Analysis.Ids(
                "TS0009",
                """
                public static class C
                {
                    public static void Split(string text, out string head) { head = text; }
                    public static int Count(string text, out string head) { head = text; return text.Length; }
                }
                """
            )
        ).Should().BeEmpty();

    [Fact]
    public async Task TheMessageNamesTheMethodTheSectionAndTheTwoReturnTypes()
    {
        string message = (
            await Analysis.Diagnostics(
                "TS0009",
                """
                public static class C
                {
                    public static bool Find(string key, out int value) { value = key.Length; return value > 0; }
                }
                """
            )
        ).Single().GetMessage();

        message
            .Should()
            .Contain("Find")
            .And.Contain("(STYLE.md, \"Expected failures\")")
            .And.Contain("T?")
            .And.Contain("Result<T, TProblem>");
    }

    [Fact]
    public async Task TryGetValueOnAGeneratedUnionIsNotReported() => (
            await Analysis.Ids(
                "TS0009",
                """
                using System;
                using System.Runtime.CompilerServices;
                namespace Tertius
                {
                    [AttributeUsage(AttributeTargets.Struct)]
                    public sealed class GeneratedUnionAttribute(params Type[] cases) : Attribute;
                }
                [Tertius.GeneratedUnion(typeof(int), typeof(string))]
                public readonly struct Move
                {
                    public Move(int value) => Value = value;
                    public Move(string value) => Value = value;
                    public object? Value { get; }
                    public bool TryGetValue(out int value) { value = 0; return Value is int; }
                }
                """
            )
        ).Should().BeEmpty();

    [Fact]
    public async Task TryGetValueOnAUnionIsNotReported() => (
            await Analysis.Ids(
                "TS0009",
                """
                using System.Runtime.CompilerServices;
                [Union]
                public readonly struct Move
                {
                    public Move(int value) => Value = value;
                    public Move(string value) => Value = value;
                    public object? Value { get; }
                    public bool TryGetValue(out string value) { value = ""; return Value is string; }
                }
                """
            )
        ).Should().BeEmpty();

    [Fact]
    public async Task TryGetValueOnATypeThatIsNotAUnionIsReported() => (
            await Analysis.Ids(
                "TS0009",
                """
                public readonly struct Box
                {
                    public bool TryGetValue(out int value) { value = 0; return true; }
                }
                """
            )
        ).Should().Equal("TS0009");

    [Fact]
    public async Task AnotherMethodOnAUnionIsReported() => (
            await Analysis.Ids(
                "TS0009",
                """
                using System.Runtime.CompilerServices;
                [Union]
                public readonly struct Move
                {
                    public Move(int value) => Value = value;
                    public Move(string value) => Value = value;
                    public object? Value { get; }
                    public bool Find(out int value) { value = 0; return Value is int; }
                    public bool TryGet(out int value) { value = 0; return Value is int; }
                }
                """
            )
        ).Should().Equal("TS0009", "TS0009");

    [Fact]
    public async Task AnOverrideIsNotReportedBesideItsBase() => (
            await Analysis.Ids(
                "TS0009",
                """
                public abstract class Base
                {
                    public abstract bool Count(out int count);
                }
                public sealed class Derived : Base
                {
                    public override bool Count(out int count) { count = 1; return true; }
                }
                """
            )
        ).Should().Equal(["TS0009"], "only the base declares the shape");

    [Fact]
    public async Task AnOverrideOfABaseOutsideTheSolutionIsNotReported() => (
            await Analysis.Ids(
                "TS0009",
                """
                using System.IO;
                using System.Net;
                using System.Net.Http;
                using System.Threading.Tasks;
                public sealed class Body : HttpContent
                {
                    protected override bool TryComputeLength(out long length) { length = 0; return true; }
                    protected override Task SerializeToStreamAsync(Stream stream, TransportContext? context) =>
                        Task.CompletedTask;
                }
                """
            )
        ).Should().BeEmpty();

    [Fact]
    public async Task AnImplementationIsNotReportedBesideItsInterface() => (
            await Analysis.Ids(
                "TS0009",
                """
                public interface ICounter
                {
                    bool Count(out int count);
                }
                public sealed class Counter : ICounter
                {
                    public bool Count(out int count) { count = 1; return true; }
                }
                public sealed class Explicit : ICounter
                {
                    bool ICounter.Count(out int count) { count = 1; return true; }
                }
                """
            )
        ).Should().Equal(["TS0009"], "only the interface declares the shape");

    [Fact]
    public async Task AnOverloadBesideAnImplementationIsReportedAsItDoesNotImplementTheMember() => (
            await Analysis.Ids(
                "TS0009",
                """
                public interface ICounter
                {
                    bool Count(out int count);
                }
                public sealed class Counter : ICounter
                {
                    public bool Count(out int count) { count = 1; return true; }
                    public bool Count(out int count, out int total) { count = 1; total = 2; return true; }
                }
                """
            )
        ).Should().Equal(["TS0009", "TS0009"], "the interface, and the overload that implements nothing");

    [Fact]
    public async Task AnImplementationOfAnInterfaceOutsideTheSolutionIsNotReported() => (
            await Analysis.Ids(
                "TS0009",
                """
                using System;
                public sealed class Number : ISpanParsable<Number>
                {
                    public static Number Parse(string s, IFormatProvider? provider) => new();
                    public static bool TryParse(string? s, IFormatProvider? provider, out Number result)
                    {
                        result = new();
                        return true;
                    }
                    public static Number Parse(ReadOnlySpan<char> s, IFormatProvider? provider) => new();
                    public static bool TryParse(ReadOnlySpan<char> s, IFormatProvider? provider, out Number result)
                    {
                        result = new();
                        return true;
                    }
                }
                public sealed class Hidden : IParsable<Hidden>
                {
                    static Hidden IParsable<Hidden>.Parse(string s, IFormatProvider? provider) => new();
                    static bool IParsable<Hidden>.TryParse(string? s, IFormatProvider? provider, out Hidden result)
                    {
                        result = new();
                        return true;
                    }
                }
                """
            )
        ).Should().BeEmpty();

    [Fact]
    public async Task AnImplementationOfAnInterfaceOfAnotherAssemblyOfTheSolutionIsNotReported()
    {
        CSharpCompilation library = TestCompilation.Of(
            "public interface ICounter { bool Count(out int count); }",
            assembly: "Tertius.Library",
            libraries: []
        );
        CSharpCompilation probe = TestCompilation.Of(
            "public sealed class Counter : ICounter { public bool Count(out int count) { count = 1; return true; } }",
            assembly: "Probe",
            libraries: [library]
        );

        (await Analysis.Diagnostics("TS0009", probe)).Should().BeEmpty();
        (await Analysis.Diagnostics("TS0009", library))
            .Select(diagnostic => diagnostic.Id)
            .Should()
            .Equal(["TS0009"], "the interface declares the shape");
    }

    [Fact]
    public async Task ALineCommentAboveTheMethodThatNamesABenchmarkExcusesIt() => (
            await Analysis.Ids(
                "TS0009",
                """
                public static class C
                {
                    // Kept as a Try method: BENCHMARK Find shows that the nullable form costs 40 ns per call.
                    public static bool Find(string key, out int value) { value = key.Length; return value > 0; }
                }
                """
            )
        ).Should().BeEmpty();

    [Fact]
    public async Task ADocCommentAboveTheAttributesOfTheMethodThatNamesABenchmarkExcusesIt() => (
            await Analysis.Ids(
                "TS0009",
                """
                public static class C
                {
                    /// <summary>
                    /// True, with the length in <paramref name="value"/>. It stays a Try method because the
                    /// benchmark FindBenchmarks shows that a nullable result costs real time.
                    /// </summary>
                    [System.Diagnostics.CodeAnalysis.SuppressMessage("a", "b")]
                    public static bool Find(string key, out int value) { value = key.Length; return value > 0; }
                }
                """
            )
        ).Should().BeEmpty();

    [Fact]
    public async Task ABlockCommentAboveALocalFunctionThatNamesABenchmarkExcusesIt() => (
            await Analysis.Ids(
                "TS0009",
                """
                public static class C
                {
                    public static int Run()
                    {
                        return One(out int value) ? value : 0;
                        /* Measured by the Benchmark of Run. */
                        static bool One(out int value) { value = 1; return true; }
                    }
                }
                """
            )
        ).Should().BeEmpty();

    [Fact]
    public async Task ARegionNamedBenchmarkIsNoCommentAndDoesNotExcuseTheMethod() => (
            await Analysis.Ids(
                "TS0009",
                """
                public static class C
                {
                #region Benchmark
                    public static bool Find(string key, out int value) { value = key.Length; return value > 0; }
                #endregion
                }
                """
            )
        ).Should().Equal("TS0009");

    [Fact]
    public async Task ACommentThatDoesNotNameABenchmarkDoesNotExcuseTheMethod() => (
            await Analysis.Ids(
                "TS0009",
                """
                public static class C
                {
                    // Fast enough, and the Try shape is what the callers know.
                    public static bool Find(string key, out int value) { value = key.Length; return value > 0; }
                }
                """
            )
        ).Should().Equal("TS0009");

    [Fact]
    public async Task ABenchmarkCommentInsideTheBodyOrAboveAnotherMethodDoesNotExcuseTheMethod() => (
            await Analysis.Ids(
                "TS0009",
                """
                public static class C
                {
                    // The benchmark of Other shows nothing about Find.
                    public static int Other() => 1;

                    public static bool Find(string key, out int value)
                    {
                        // A benchmark would tell.
                        value = key.Length;
                        return value > 0;
                    }
                }
                """
            )
        ).Should().Equal("TS0009");
}
