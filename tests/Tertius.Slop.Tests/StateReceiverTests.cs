using AwesomeAssertions;

namespace Tertius.Slop.Tests;

/// <summary>Patterns and malformed access preserve the identity of the instance being measured.</summary>
public sealed class StateReceiverTests
{
    [Theory]
    [InlineData("return this is { Value: > 0 };")]
    [InlineData("return this is not { Value: > 0 };")]
    [InlineData("return this is { Value: > 0 } or { Value: < 0 };")]
    [InlineData("return this switch { { Value: > 0 } => true, _ => false };")]
    [InlineData("switch (this) { case { Value: > 0 }: return true; default: return false; }")]
    [InlineData("return this is { x: > 0 };")]
    public void PatternsOnThisReadTheMatchedStorage(string body)
    {
        ClassMetrics metrics = StateCohesionTests.Measure(
            $"class A {{ int x; int Value => x; public bool F() {{ {body} }} public int G() => x; }}"
        );

        metrics.Summary.BindingErrorCount.Should().Be(0);
        metrics.Summary.Tcc.Should().Be(new ClassRatio(1, 1));
        metrics.Types.Single().SharedPairs.Single().Fields.Should().Equal("A.x");
    }

    [Theory]
    [InlineData("other is { Value: > 0 }")]
    [InlineData("this is { Other: { Value: > 0 } }")]
    [InlineData("this is { Other.Value: > 0 }")]
    public void PatternsOnAnotherInstanceDoNotReadThisInstancesStorage(string expression)
    {
        ClassMetrics metrics = StateCohesionTests.Measure(
            $"class A {{ int x; A Other; int Value => x; public bool F(A other) => {expression}; public int G() => x; }}"
        );

        metrics.Summary.BindingErrorCount.Should().Be(0);
        metrics.Summary.Tcc.Should().Be(new ClassRatio(0, 1));
        metrics.Types.Single().UnresolvedAccesses.Should().BeEmpty();
    }

    [Theory]
    [InlineData("(this).Missing()")]
    [InlineData("((A)this).Missing()")]
    [InlineData("(this)[0]")]
    [InlineData("((A)this)?.Missing()")]
    public void WrappedSelfReceiversRetainUnresolvedEvidence(string access)
    {
        ClassMetrics metrics = StateCohesionTests.Measure(
            $"class A {{ int x; public object F() => {access}; public int G() => x; }}"
        );

        metrics.Types.Single().TccApplicability!.Status.Should().Be("incomplete");
        metrics.Types.Single().UnresolvedAccesses.Should().NotBeEmpty();
    }

    [Theory]
    [InlineData("(other).Missing()")]
    [InlineData("((A)other).Missing()")]
    [InlineData("new A { Missing = 1 }")]
    [InlineData("new A { Missing = { Value = 1 } }")]
    public void UnresolvedOtherReceiversDoNotContaminateCompleteStorageEvidence(string access)
    {
        ClassMetrics metrics = StateCohesionTests.Measure(
            $"class A {{ int x; public int F(A other) {{ _ = {access}; return x; }} public int G() => x; }}"
        );

        metrics.Summary.BindingErrorCount.Should().BeGreaterThan(0);
        metrics.Summary.Tcc.Should().Be(new ClassRatio(1, 1));
        metrics.Types.Single().UnresolvedAccesses.Should().BeEmpty();
    }

    [Fact]
    public void UnresolvedInitializerValuesStillReferToTheContainingInstance()
    {
        ClassMetrics metrics = StateCohesionTests.Measure(
            "class A { int x; public int F() { _ = new A { x = Missing }; return x; } public int G() => x; }"
        );

        metrics.Types.Single().TccApplicability!.Status.Should().Be("incomplete");
        metrics.Types.Single().UnresolvedAccesses.Should().NotBeEmpty();
    }
}
