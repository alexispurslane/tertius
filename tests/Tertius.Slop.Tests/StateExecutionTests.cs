using AwesomeAssertions;

namespace Tertius.Slop.Tests;

/// <summary>Static receiver identity controls dispatch, accessor selection and nested execution.</summary>
public sealed class StateExecutionTests
{
    [Fact]
    public void ConstantComputedPropertiesDoNotInventStorage()
    {
        ClassMetrics metrics = StateCohesionTests.Measure(
            "class A { int x; int Value => 0; public int F() => Value; public int G() => x; }"
        );

        metrics.Summary.Tcc.Should().Be(new ClassRatio(0, 1));
        metrics.Types.Single().Methods.Single(method => method.Member.Name == "A.F()").Fields.Should().BeEmpty();
    }

    [Fact]
    public void AUserDefinedConversionDoesNotTurnAnotherReceiverIntoThisInstance()
    {
        ClassMetrics metrics = StateCohesionTests.Measure(
            "class Other { int y; public int Read() => y; } class A { int x; public static explicit operator Other(A value) => new Other(); public int F() => ((Other)this).Read(); public int G() => x; }"
        );

        metrics
            .Types.Single(type => type.Name == "A")
            .Methods.Single(method => method.Member.Name == "A.F()")
            .Fields.Should()
            .BeEmpty();
    }

    [Fact]
    public void SealedPropertyDispatchUsesTheOverrideStorage()
    {
        ClassMetrics metrics = StateCohesionTests.Measure(
            "class Base { protected int x; protected int Read() => Value; protected virtual int Value => x; } sealed class A : Base { int y; protected override int Value => y; public int F() => Read(); public int G() => y; }"
        );

        metrics.Types.Single(type => type.Name == "A").SharedPairs.Single().Fields.Should().Equal("A.y");
    }

    [Fact]
    public void ASealedOverrideIsDeterminateWithoutASealedContainingType()
    {
        ClassMetrics metrics = StateCohesionTests.Measure(
            "class Base { protected virtual int Read() => 0; } class A : Base { int x; protected sealed override int Read() => x; public int F() => Read(); public int G() => x; }"
        );

        ClassMeasure type = metrics.Types.Single(type => type.Name == "A");
        type.UnresolvedAccesses.Should().BeEmpty();
        type.Tcc.Ratio.Should().Be(1);
    }

    [Fact]
    public void SealedReceiverUsesItsOverrideEvenInsideAnInheritedHelper()
    {
        ClassMetrics metrics = StateCohesionTests.Measure(
            "class Base { protected int x; protected int Read() => Value(); protected virtual int Value() => x; } sealed class A : Base { int y; protected override int Value() => y; public int F() => Read(); public int G() => y; }"
        );

        ClassMeasure type = metrics.Types.Single(type => type.Name == "A");
        type.Methods.Single(method => method.Member.Name == "A.F()").Fields.Should().Equal("A.y");
        type.UnresolvedAccesses.Should().BeEmpty();
    }

    [Fact]
    public void ExplicitBaseDispatchKeepsTheBaseImplementation()
    {
        ClassMetrics metrics = StateCohesionTests.Measure(
            "class Base { protected int x; protected virtual int Value() => x; } sealed class A : Base { int y; protected override int Value() => y; public int F() => base.Value(); public int G() => x; }"
        );

        metrics
            .Types.Single(type => type.Name == "A")
            .Methods.Single(method => method.Member.Name == "A.F()")
            .Fields.Should()
            .Equal("Base.x");
    }

    [Fact]
    public void GenericPartialHelpersAndInheritedStorageHaveOneIdentity()
    {
        ClassMetrics metrics = StateCohesionTests.Measure(
            "class Base<T> { protected int x; protected int Read<U>(U unused) => x; } partial class A<T> : Base<T> { public int F() => Read(1); } partial class A<T> { public int G() => x; }"
        );

        ClassMeasure type = metrics.Types.Single(type => type.Name == "A<T>");
        type.Tcc.Ratio.Should().Be(1);
        type.SharedPairs.Single().Fields.Should().Equal("Base<T>.x");
    }

    [Fact]
    public void WritingThroughARefPropertyExecutesItsGetter()
    {
        ClassMetrics metrics = StateCohesionTests.Measure(
            "class A { int x; ref int Value => ref x; public void F() => Value = 1; public int G() => x; }"
        );

        metrics.Summary.Tcc.Should().Be(new ClassRatio(1, 1));
    }

    [Fact]
    public void DeconstructionTargetsExecuteSettersNotGetters()
    {
        ClassMetrics metrics = StateCohesionTests.Measure(
            "class A { int read, written; int Value { get => read; set => written = value; } public void F() { (Value, _) = (1, 2); } public int G() => written; }"
        );

        metrics
            .Types.Single()
            .Methods.Single(method => method.Member.Name == "A.F()")
            .Fields.Should()
            .Equal("A.written");
        metrics.Summary.Tcc.Should().Be(new ClassRatio(1, 1));
    }

    [Theory]
    [InlineData("Value++")]
    [InlineData("Value += 1")]
    public void CompoundPropertyWritesExecuteBothAccessors(string statement)
    {
        ClassMetrics metrics = StateCohesionTests.Measure(
            $"class A {{ int read, written; int Value {{ get => read; set => written = value; }} public void F() {{ {statement}; }} public int G() => written; }}"
        );

        metrics
            .Types.Single()
            .Methods.Single(method => method.Member.Name == "A.F()")
            .Fields.Should()
            .Equal("A.read", "A.written");
    }

    [Fact]
    public void FieldBackedPropertiesUseTheirActualStorage()
    {
        ClassMetrics metrics = StateCohesionTests.Measure(
            "class A { int Value { get => field; set => field = value; } public int F() => Value; public void G() => Value = 1; }"
        );

        metrics.Summary.BindingErrorCount.Should().Be(0);
        metrics.Summary.Tcc.Should().Be(new ClassRatio(1, 1));
        metrics.Types.Single().SharedPairs.Single().Fields.Should().ContainSingle();
    }

    [Fact]
    public void InvokedLocalRecursionTerminatesAndUnusedLocalAccessStaysExcluded()
    {
        ClassMetrics metrics = StateCohesionTests.Measure(
            "class A { int x, unused; public int F() { int Unused() => unused; int First(bool stop) => stop ? x : Second(); int Second() => First(true); return First(false); } public int G() => x; }"
        );

        metrics.Types.Single().Methods.Single(method => method.Member.Name == "A.F()").Fields.Should().Equal("A.x");
        metrics.Summary.Tcc.Should().Be(new ClassRatio(1, 1));
    }

    [Fact]
    public void DependencyCallsCountOnlyTheDependencyFieldAndEvaluatedArguments()
    {
        ClassMetrics metrics = StateCohesionTests.Measure(
            "class Dependency { public int Read(int value) => value; } class A { Dependency dependency; int x; public int F() => dependency.Read(x); public int G() => x; }"
        );

        metrics
            .Types.Single(type => type.Name == "A")
            .Methods.Single(method => method.Member.Name == "A.F()")
            .Fields.Should()
            .Equal("A.dependency", "A.x");
    }
}
