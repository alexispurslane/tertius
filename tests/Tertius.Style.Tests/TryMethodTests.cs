using AwesomeAssertions;

namespace Tertius.Style.Tests;

/// <summary>
/// TS0009: a method named <c>Try</c> and an upper-case letter that does not return <c>bool</c> with at least one
/// <c>out</c> parameter. An override and an implementation of an interface member take the shape of the member that
/// they implement, so only that member is reported.
/// </summary>
public sealed class TryMethodTests
{
    [Fact]
    public async Task ATryMethodThatDoesNotReturnBoolIsReported() => (
            await Analysis.Ids(
                "TS0009",
                """
                public static class C
                {
                    public static int? TryParse(string text, out int value) { value = 0; return null; }
                }
                """
            )
        ).Should().Equal("TS0009");

    [Fact]
    public async Task ATryMethodWithoutAnOutParameterIsReported() => (
            await Analysis.Ids(
                "TS0009",
                """
                public static class C
                {
                    public static bool TryFind(string key) => key.Length > 0;
                }
                """
            )
        ).Should().Equal("TS0009");

    [Fact]
    public async Task ARefParameterIsNotAnOutParameter() => (
            await Analysis.Ids(
                "TS0009",
                """
                public static class C
                {
                    public static bool TryBump(ref int value) => ++value > 0;
                }
                """
            )
        ).Should().Equal("TS0009");

    [Fact]
    public async Task AnAsyncTryMethodIsReported() => (
            await Analysis.Ids(
                "TS0009",
                """
                using System.Threading.Tasks;
                public static class C
                {
                    public static Task<bool> TryLoadAsync(string path) => Task.FromResult(path.Length > 0);
                }
                """
            )
        ).Should().Equal("TS0009");

    [Fact]
    public async Task ALocalFunctionIsReported() => (
            await Analysis.Ids(
                "TS0009",
                """
                public static class C
                {
                    public static int Run()
                    {
                        return TryOne();
                        static int TryOne() => 1;
                    }
                }
                """
            )
        ).Should().Equal("TS0009");

    [Fact]
    public async Task TheMessageNamesTheMethodAndTheSection() => (
            await Analysis.Diagnostics(
                "TS0009",
                """
                public static class C
                {
                    public static bool TryFind(string key) => key.Length > 0;
                }
                """
            )
        ).Single().GetMessage().Should().Contain("TryFind").And.Contain("(STYLE.md, \"Expected failures\")");

    [Fact]
    public async Task ABoolTryMethodWithAnOutParameterIsNotReported() => (
            await Analysis.Ids(
                "TS0009",
                """
                using System.Diagnostics.CodeAnalysis;
                public static class C
                {
                    public static bool TryGet(int key, [NotNullWhen(true)] out string? value, out string reason)
                    {
                        value = null;
                        reason = "none";
                        return false;
                    }
                }
                """
            )
        ).Should().BeEmpty();

    [Fact]
    public async Task ANameWithoutAnUpperCaseLetterAfterTryIsNotReported() => (
            await Analysis.Ids(
                "TS0009",
                """
                public static class C
                {
                    public static int Try() => 1;
                    public static int Tryout() => 1;
                    public static int Trying() => 1;
                }
                """
            )
        ).Should().BeEmpty();

    [Fact]
    public async Task AnOverrideIsNotReportedBesideItsBase() => (
            await Analysis.Ids(
                "TS0009",
                """
                public abstract class Base
                {
                    public abstract int TryCount();
                }
                public sealed class Derived : Base
                {
                    public override int TryCount() => 1;
                }
                """
            )
        ).Should().Equal(["TS0009"], "only the base declares the shape");

    [Fact]
    public async Task AnImplementationIsNotReportedBesideItsInterface() => (
            await Analysis.Ids(
                "TS0009",
                """
                public interface ICounter
                {
                    int TryCount();
                }
                public sealed class Counter : ICounter
                {
                    public int TryCount() => 1;
                }
                public sealed class Explicit : ICounter
                {
                    int ICounter.TryCount() => 1;
                }
                """
            )
        ).Should().Equal(["TS0009"], "only the interface declares the shape");
}
