using AwesomeAssertions;

namespace Tertius.Style.Tests;

/// <summary>
/// TS0014: a static class named as a bag of helpers, <c>*Helper</c>, <c>*Helpers</c>, <c>*Util</c>, <c>*Utils</c> or
/// <c>*Extensions</c>, that declares no extension member. Its functions belong in the types that they work on.
/// </summary>
public sealed class HelperClassTests
{
    [Theory]
    [InlineData("MathHelper")]
    [InlineData("MathHelpers")]
    [InlineData("MathUtil")]
    [InlineData("MathUtils")]
    [InlineData("MathExtensions")]
    public async Task AStaticHelperClassWithoutExtensionMembersIsReported(string name) => (
            await Analysis.Ids(
                "TS0014",
                $$"""
                public static class {{name}}
                {
                    public static int Twice(int value) => 2 * value;
                }
                """
            )
        ).Should().Equal("TS0014");

    [Fact]
    public async Task TheMessageNamesTheClassAndTheSection() => (
            await Analysis.Diagnostics(
                "TS0014",
                """
                public static class KeplerHelper
                {
                    public static int Twice(int value) => 2 * value;
                }
                """
            )
        ).Single().GetMessage().Should().Contain("KeplerHelper").And.Contain("(STYLE.md, \"Put a function into the type that it belongs to\")");

    [Fact]
    public async Task AClassWithAnExtensionMethodIsNotReported() => (
            await Analysis.Ids(
                "TS0014",
                """
                public static class IntExtensions
                {
                    public static int Twice(this int value) => 2 * value;
                }
                """
            )
        ).Should().BeEmpty();

    [Fact]
    public async Task AClassWithAnExtensionBlockIsNotReported() => (
            await Analysis.Ids(
                "TS0014",
                """
                public static class IntExtensions
                {
                    extension(int value)
                    {
                        public int Twice => 2 * value;
                    }
                }
                """
            )
        ).Should().BeEmpty();

    [Fact]
    public async Task OtherNamesAndClassesThatAreNotStaticAreNotReported() => (
            await Analysis.Ids(
                "TS0014",
                """
                public static class Utility
                {
                    public static int Twice(int value) => 2 * value;
                }
                public sealed class RequestHelper
                {
                    public int Twice(int value) => 2 * value;
                }
                """
            )
        ).Should().BeEmpty();
}
