using AwesomeAssertions;

namespace Tertius.Slop.Tests;

/// <summary>Unresolved execution retains known storage without guessing a runtime body.</summary>
public sealed class IncompleteStateTests
{
    [Fact]
    public void DistinctUnknownCallsOnOneLineKeepDistinctLocations()
    {
        ClassMetrics metrics = StateCohesionTests.Measure(
            "class A { int x; public int F() => Missing() + Another(); public int G() => x; }"
        );

        metrics.Types.Single().UnresolvedAccesses.Should().HaveCount(2);
    }

    [Theory]
    [InlineData("Missing()", "unresolved-binding")]
    [InlineData("Missing<int>()", "unresolved-binding")]
    [InlineData("this.Missing", "unresolved-binding")]
    [InlineData("((dynamic)this).Read()", "dynamic-dispatch")]
    [InlineData("((dynamic)this).Value", "dynamic-dispatch")]
    [InlineData("((dynamic)this)[0]", "dynamic-dispatch")]
    [InlineData("Read()", "runtime-dispatch")]
    public void UnknownSameInstanceAccessRetainsKnownSharedPairs(string expression, string reason)
    {
        ClassMetrics metrics = StateCohesionTests.Measure(
            $"class A {{ int x; protected virtual int Read() => 0; public int F() {{ _ = {expression}; return x; }} public int G() => x; }}"
        );

        ClassMeasure type = metrics.Types.Single();
        type.TccApplicability!.Status.Should().Be("incomplete");
        type.Tcc.Ratio.Should().BeNull();
        type.Tcc.Numerator.Should().Be(1);
        type.Tcc.Denominator.Should().Be(3);
        type.SharedPairs.Single().Fields.Should().Equal("A.x");
        type.UnresolvedAccesses.Should().Contain(issue => issue.Reason == reason && issue.Location.Line == 1);
    }

    [Fact]
    public void AnUnavailableInheritedHelperBodyIsIncomplete()
    {
        ClassMetrics metrics = StateCohesionTests.Measure(
            "class A : System.IO.MemoryStream { int x; public int F() => base.ReadByte(); public int G() => x; }"
        );

        metrics.Summary.BindingErrorCount.Should().Be(0);
        metrics.Types.Single().UnresolvedAccesses.Should().Contain(issue => issue.Reason == "source-body-unavailable");
        metrics.Summary.Tcc.Ratio.Should().BeNull();
    }

    [Fact]
    public void EachUnavailableHelperCallRetainsItsOwnLocation()
    {
        ClassMetrics metrics = StateCohesionTests.Measure(
            "class A : System.IO.MemoryStream { int x; public int F() => base.ReadByte() + base.ReadByte(); public int G() => x; }"
        );

        metrics.Types.Single().UnresolvedAccesses.Should().HaveCount(2);
    }

    [Fact]
    public void PrimaryConstructorCapturesCannotMasqueradeAsNoInstanceStorage()
    {
        ClassMetrics metrics = StateCohesionTests.Measure(
            "class A(int value) { public int F() => value; public int G() => value; }"
        );

        metrics.Types.Single().TccApplicability!.Status.Should().Be("incomplete");
        metrics.Types.Single().UnresolvedAccesses.Should().HaveCount(2);
        metrics.Summary.Tcc.Ratio.Should().BeNull();
    }

    [Fact]
    public void SealedDispatchWithoutAnInterfaceImplementationIsIncomplete()
    {
        ClassMetrics metrics = StateCohesionTests.Measure(
            "interface I { int Read(); } sealed class A : I { int x; public int F() => ((I)this).Read(); public int G() => x; }"
        );

        ClassMeasure type = metrics.Types.Single(type => type.Name == "A");
        type.TccApplicability!.Status.Should().Be("incomplete");
        type.UnresolvedAccesses.Should().NotBeEmpty();
    }

    [Fact]
    public void UnknownCallsOnAnotherInstanceDoNotMakeThisInstanceIncomplete()
    {
        ClassMetrics metrics = StateCohesionTests.Measure(
            "class A { int x, y; public int F(A other, dynamic dependency) { _ = other.Missing(); _ = dependency.Value; return x; } public int G() => y; }"
        );

        metrics.Types.Single().TccApplicability!.Status.Should().Be("complete");
        metrics.Summary.Tcc.Should().Be(new ClassRatio(0, 1));
    }

    [Fact]
    public void ObjectInitializerTargetsDoNotExecuteOnThisInstance()
    {
        ClassMetrics metrics = StateCohesionTests.Measure(
            "class A { int x; int Value { get => x; set => x = value; } public A F() => new A { Value = 1 }; public int G() => x; }"
        );

        metrics.Types.Single().Methods.Single(method => method.Member.Name == "A.F()").Fields.Should().BeEmpty();
        metrics.Summary.Tcc.Should().Be(new ClassRatio(0, 1));
    }
}
