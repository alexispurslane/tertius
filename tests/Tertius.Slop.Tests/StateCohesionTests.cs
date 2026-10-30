using AwesomeAssertions;

namespace Tertius.Slop.Tests;

/// <summary>Methods share actual instance storage through executed property and helper paths.</summary>
public sealed class StateCohesionTests
{
    [Theory]
    [InlineData("int Value => x; public int F() => Value;")]
    [InlineData("int Read() => x; public int F() => Read();")]
    [InlineData("int Read() => Next(); int Next() => x; public int F() => Read();")]
    [InlineData("int Read(bool stop) => stop ? x : Next(); int Next() => Read(true); public int F() => Read(false);")]
    [InlineData("public int F() { int Read() => x; return Read(); }")]
    public void PropertyAndHelperExtractionPreserveSharedStorage(string access)
    {
        ClassMetrics metrics = Measure($"class A {{ int x; {access} public int G() => x; }}");

        metrics.Summary.Tcc.Should().Be(new ClassRatio(1, 1));
        metrics.Types.Single().SharedPairs.Single().Fields.Should().Equal("A.x");
    }

    [Fact]
    public void AutomaticPropertiesSupplyOneSharedStorageLocation()
    {
        ClassMetrics metrics = Measure(
            "class A { int Value { get; set; } public int F() => Value; public void G() => Value = 1; }"
        );

        metrics.Summary.Tcc.Should().Be(new ClassRatio(1, 1));
        metrics.Types.Single().SharedPairs.Single().Fields.Should().ContainSingle();
    }

    [Fact]
    public void PositionalRecordPropertiesResolveToTheirSynthesizedStorage()
    {
        ClassMetrics metrics = Measure("record A(int Value) { public int F() => Value; public int G() => Value; }");

        metrics.Summary.Tcc.Should().Be(new ClassRatio(1, 1));
        metrics.Types.Single().SharedPairs.Single().Fields.Should().ContainSingle();
    }

    [Theory]
    [InlineData("Value", "A.read")]
    [InlineData("Value = 1", "A.written")]
    public void PropertyAccessUsesOnlyTheExecutedAccessor(string access, string field)
    {
        ClassMetrics metrics = Measure(
            $"class A {{ int read, written; int Value {{ get => read; set => written = value; }} public void F() {{ _ = {access}; }} public int G() => read; }}"
        );

        metrics.Types.Single().Methods.Single(method => method.Member.Name == "A.F()").Fields.Should().Equal(field);
    }

    [Fact]
    public void ConditionalThisCallsFollowTheSameInstance()
    {
        ClassMetrics metrics = Measure(
            "class A { int x; int Read() => x; public int? F() => this?.Read(); public int G() => x; }"
        );

        metrics.Summary.Tcc.Should().Be(new ClassRatio(1, 1));
    }

    [Fact]
    public void AnotherInstanceAndUnusedNestedBodiesDoNotSupplyState()
    {
        ClassMetrics metrics = Measure(
            "class A { int x; int Read() => x; public int F(A other) { int Unused() => x; System.Func<int> unused = () => x; _ = nameof(x); _ = new A { x = 2 }; return other.Read() + (other?.x ?? 0); } public int G() => x; }"
        );

        metrics.Summary.Tcc.Should().Be(new ClassRatio(0, 1));
        metrics.Types.Single().Methods.Single(method => method.Member.Name == "A.F(A)").Fields.Should().BeEmpty();
    }

    internal static ClassMetrics Measure(string text) =>
        ClassMetrics.Of(Source.UsedByATest(SourceFile.Parse("src/State.cs", text)));
}
