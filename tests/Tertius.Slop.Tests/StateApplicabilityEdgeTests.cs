using AwesomeAssertions;

namespace Tertius.Slop.Tests;

/// <summary>Interface contracts and unresolved self access stay distinct from measured storage sharing.</summary>
public sealed class StateApplicabilityEdgeTests
{
    [Theory]
    [InlineData("interface I { int F(); int G(); }")]
    [InlineData("interface I { int F() => 1; int G(); }")]
    public void InterfaceContractsHaveNoOwnInstanceStorageToMeasure(string source)
    {
        ClassMetrics metrics = StateCohesionTests.Measure(source);

        metrics.Types.Single().TccApplicability!.Status.Should().Be("not-applicable");
        metrics.Types.Single().TccApplicability!.Reason.Should().Be("no-instance-storage");
        metrics.Summary.CohesionPopulation.Should().Be(new MetricPopulation(0, 1, 0));
        metrics.Summary.UnresolvedStateAccessCount.Should().Be(0);
    }

    [Theory]
    [InlineData("this[0]")]
    [InlineData("this?.Missing()")]
    [InlineData("this?[0]")]
    public void UnresolvedSelfAccessCannotBecomeCompleteZero(string access)
    {
        ClassMetrics metrics = StateCohesionTests.Measure(
            $"class A {{ int x; public object F() => {access}; public int G() => x; }}"
        );

        metrics.Types.Single().TccApplicability!.Status.Should().Be("incomplete");
        metrics.Types.Single().UnresolvedAccesses.Should().NotBeEmpty();
    }

    [Theory]
    [InlineData("interface I { int Read(); }", "int I.Read() => x;", "((I)this).Read()")]
    [InlineData("interface I { int Value { get; } }", "int I.Value => x;", "((I)this).Value")]
    public void SealedInterfaceDispatchReachesTheActualImplementation(
        string contract,
        string implementation,
        string access
    )
    {
        ClassMetrics metrics = StateCohesionTests.Measure(
            $"{contract} sealed class A : I {{ int x; {implementation} public int F() => {access}; public int G() => x; }}"
        );

        ClassMeasure type = metrics.Types.Single(type => type.Name == "A");
        type.Tcc.Ratio.Should().Be(1);
        type.SharedPairs.Single().Fields.Should().Equal("A.x");
        type.UnresolvedAccesses.Should().BeEmpty();
    }

    [Theory]
    [InlineData("int this[int index] => x;", "this[0]", "A.x")]
    [InlineData("int this[int index] { set => x = value; }", "this[0] = 1", "A.x")]
    public void IndexerAccessContributesItsExecutedStorage(string declaration, string access, string field)
    {
        ClassMetrics metrics = StateCohesionTests.Measure(
            $"class A {{ int x; {declaration} public void F() {{ _ = {access}; }} public int G() => x; }}"
        );

        metrics.Summary.BindingErrorCount.Should().Be(0);
        metrics.Types.Single().Methods.Single(method => method.Member.Name == "A.F()").Fields.Should().Equal(field);
    }

    [Theory]
    [InlineData("int Value { set => x = value; }", "Value")]
    [InlineData("int Value { get => x; }", "Value = 1")]
    public void MissingRequiredAccessorsRetainIncompleteEvidence(string declaration, string access)
    {
        ClassMetrics metrics = StateCohesionTests.Measure(
            $"class A {{ int x; {declaration} public void F() {{ _ = {access}; }} public int G() => x; }}"
        );

        metrics.Types.Single().TccApplicability!.Status.Should().Be("incomplete");
        metrics.Types.Single().UnresolvedAccesses.Should().NotBeEmpty();
    }

    [Fact]
    public void UnresolvedConditionalAccessOnAnotherInstanceDoesNotHideCompleteEvidence()
    {
        ClassMetrics metrics = StateCohesionTests.Measure(
            "class A { int x; public int F(A other) { _ = other?.Missing(); _ = other?[0]; return x; } public int G() => x; }"
        );

        metrics.Summary.Tcc.Should().Be(new ClassRatio(1, 1));
        metrics.Types.Single().UnresolvedAccesses.Should().BeEmpty();
    }
}
