using AwesomeAssertions;

namespace Tertius.Core.Tests;

/// <summary>
/// <see cref="Result{T, TProblem}"/>, the result of a method whose caller needs the reason for a failure (STYLE.md,
/// "Expected failures"): which case it holds, its nullable reads for coalescing, and that a struct result never boxes.
/// </summary>
public sealed class ResultTests
{
    private sealed record Problem(string Message);

    private sealed record Script(string Name);

    private static readonly Problem NotANumber = new("not a number");

    private static Result<int, Problem> ParseNumber(string text) =>
        int.TryParse(text, out int number) ? number : NotANumber;

    [Fact]
    public void AResultThatHoldsAValueGivesItThroughOkAndHasNoProblem()
    {
        Result<int, Problem> parsed = ParseNumber("42");

        parsed.Ok.Should().Be(42);
        parsed.Problem.Should().BeNull();
    }

    [Fact]
    public void AResultThatHoldsAProblemGivesItThroughProblemAndHasNoValue()
    {
        Result<int, Problem> parsed = ParseNumber("x");

        parsed.Ok.Should().BeNull();
        parsed.Problem.Should().BeSameAs(NotANumber);
    }

    [Fact]
    public void AStructValueCoalescesThroughOk()
    {
        int number = ParseNumber("x").Ok ?? -1;

        number.Should().Be(-1);
    }

    [Fact]
    public void AClassValueCoalescesThroughOk()
    {
        Result<Script, Problem> found = new Script("demo");
        Result<Script, Problem> missing = new Problem("no such script");

        (found.Ok?.Name ?? "fallback").Should().Be("demo");
        (missing.Ok?.Name ?? "fallback").Should().Be("fallback");
    }

    [Fact]
    public void ASwitchReadsTheCaseThatTheResultHolds()
    {
        string Describe(Result<int, Problem> parsed) =>
            parsed switch
            {
                int number => $"number {number}",
                Problem problem => $"problem {problem.Message}",
                null => "empty",
            };

        Describe(ParseNumber("7")).Should().Be("number 7");
        Describe(ParseNumber("x")).Should().Be("problem not a number");
        Describe(default).Should().Be("empty");
    }

    [Fact]
    public void TheDefaultResultHoldsNeitherCase()
    {
        Result<int, Problem> empty = default;

        empty.HasValue.Should().BeFalse();
        empty.Value.Should().BeNull();
        empty.Ok.Should().BeNull();
        empty.Problem.Should().BeNull();
    }

    [Fact]
    public void ValueGivesTheCaseThatTheResultHolds()
    {
        ParseNumber("5").Value.Should().Be(5);
        ParseNumber("x").Value.Should().BeSameAs(NotANumber);
    }

    [Fact]
    public void ReadingAStructValueAllocatesNothing()
    {
        Result<int, Problem> parsed = 5;
        long sum = 0;
        for (int i = 0; i < 100; i++)
            sum += Read(parsed);
        long before = GC.GetAllocatedBytesForCurrentThread();

        for (int i = 0; i < 10_000; i++)
            sum += Read(parsed);

        (GC.GetAllocatedBytesForCurrentThread() - before)
            .Should()
            .Be(0, "a switch and Ok call TryGetValue, which reads the struct without a box");
        sum.Should().Be(5 * 2 * 10_100);
    }

    private static int Read(Result<int, Problem> parsed) =>
        parsed switch
        {
            int number => number,
            Problem => 0,
            null => 0,
        }
        + (parsed.Ok ?? 0);
}
