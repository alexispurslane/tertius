using System.Text.Json;
using AwesomeAssertions;

namespace Tertius.Slop.Tests;

/// <summary>Unknown evidence cannot enter the complete population or masquerade as absence of storage.</summary>
public sealed class MetricApplicabilityTests
{
    [Fact]
    public void DefaultInterfaceImplementationContributesWeightAndItsDeclaredSurface()
    {
        ClassMetrics metrics = StateCohesionTests.Measure(
            "interface I { int Read() => 1; int Other(); } class Payload { public int X; }"
        );

        metrics.Summary.Depth.Should().Be(new ClassRatio(1, 2));
        metrics.Types.Single(type => type.Name == "I").DepthApplicability!.Status.Should().Be("complete");
    }

    [Fact]
    public void PrivateOnlyImplementationRetainsWeightWithoutAddingPooledSurface()
    {
        ClassMetrics metrics = StateCohesionTests.Measure(
            "class Hidden { int F(bool value) => value ? 1 : 0; } class Visible { public void F() {} } class Payload { public int X; }"
        );

        metrics.Summary.Depth.Should().Be(new ClassRatio(3, 1));
        metrics.Types.Single(type => type.Name == "Hidden").DepthApplicability!.Reason.Should().Be("no-api-entries");
        metrics.Summary.DepthPopulation.Should().Be(new MetricPopulation(1, 2, 0));
    }

    [Fact]
    public void AHandwrittenConstructorIsImplementationButNotACohesionMethod()
    {
        ClassMetrics metrics = StateCohesionTests.Measure("class A { public A() {} }");

        metrics.Summary.Depth.Should().Be(new ClassRatio(1, 1));
        metrics.Types.Single().TccApplicability!.Reason.Should().Be("fewer-than-two-methods");
    }

    [Fact]
    public void DeclarationOnlyTypesKeepRawSurfaceWithoutAnImplementationDepthRatio()
    {
        ClassMetrics metrics = StateCohesionTests.Measure(
            "interface I { void F(); } enum Color { Red, Blue } delegate int Read(); class Payload { public int X; }"
        );

        metrics.Summary.Depth.Should().Be(new ClassRatio(0, 0));
        metrics
            .Types.Should()
            .OnlyContain(type =>
                type.DepthApplicability!.Reason == "no-handwritten-functions" && type.Depth.Ratio == null
            );
        metrics.Types.Single(type => type.Name == "I").ApiEntries.Should().Be(1);
        metrics.Summary.DepthPopulation.Should().Be(new MetricPopulation(0, 4, 0));
    }

    [Fact]
    public void StatelessTypesRetainRawPossiblePairsButHaveNoCohesionRatio()
    {
        ClassMetrics metrics = StateCohesionTests.Measure("class A { public int F() => 1; public int G() => 2; }");

        ClassMeasure type = metrics.Types.Single();
        type.Tcc.Denominator.Should().Be(1);
        type.Tcc.Ratio.Should().BeNull();
        metrics.Summary.Tcc.Should().Be(new ClassRatio(0, 0));
        type.TccApplicability!.Reason.Should().Be("no-instance-storage");
    }

    [Theory]
    [InlineData("class A { int x; public int F() => Missing(); public int G() => x; }")]
    [InlineData("class A { int x; protected virtual int Read() => x; public int F() => Read(); public int G() => x; }")]
    [InlineData("class A : Missing { public int F() => 0; public int G() => 0; }")]
    public void UnresolvedStateIsIncompleteRatherThanCompleteZero(string source)
    {
        ClassMetrics metrics = StateCohesionTests.Measure(source);
        using var json = JsonDocument.Parse(
            JsonSerializer.Serialize(metrics, new JsonSerializerOptions(JsonSerializerDefaults.Web))
        );
        JsonElement type = json.RootElement.GetProperty("types").EnumerateArray().Single();

        type.TryGetProperty("tccApplicability", out JsonElement applicability).Should().BeTrue();
        applicability.GetProperty("status").GetString().Should().Be("incomplete");
        type.GetProperty("unresolvedAccesses").GetArrayLength().Should().BeGreaterThan(0);
        metrics.Summary.Tcc.Ratio.Should().BeNull();
        metrics.Summary.DescribeAgainst(null).Should().Contain("incomplete evidence");
    }

    [Theory]
    [InlineData("class A { int x; public int F() => x; }", "fewer-than-two-methods")]
    [InlineData("class A { public int F() => 0; public int G() => 1; }", "no-instance-storage")]
    public void NotApplicableCohesionNamesTheStructuralReason(string source, string reason)
    {
        ClassMetrics metrics = StateCohesionTests.Measure(source);
        using var json = JsonDocument.Parse(
            JsonSerializer.Serialize(metrics, new JsonSerializerOptions(JsonSerializerDefaults.Web))
        );
        JsonElement type = json.RootElement.GetProperty("types").EnumerateArray().Single();

        type.TryGetProperty("tccApplicability", out JsonElement applicability).Should().BeTrue();
        applicability.GetProperty("status").GetString().Should().Be("not-applicable");
        applicability.GetProperty("reason").GetString().Should().Be(reason);
    }

    [Fact]
    public void DeclarationOnlySurfaceDoesNotDiluteImplementationDepth()
    {
        ClassMetrics metrics = StateCohesionTests.Measure(
            "class Payload { public int X; public int Value { get; set; } } interface I { void F(); } class Worker { public void F() {} }"
        );

        metrics.Summary.Depth.Should().Be(new ClassRatio(1, 1));
        ClassMeasure payload = metrics.Types.Single(type => type.Name == "Payload");
        payload.ApiEntries.Should().Be(2);
        payload.Depth.Ratio.Should().BeNull();
    }

    [Fact]
    public void CompleteZeroRemainsMeasurableBesideIncompleteAndStatelessTypes()
    {
        ClassMetrics metrics = StateCohesionTests.Measure(
            "class Complete { int x, y; public int F() => x; public int G() => y; } class Unknown { int x; public int F() => Missing(); public int G() => x; } class Empty { public int F() => 0; public int G() => 0; }"
        );

        metrics.Summary.Tcc.Should().Be(new ClassRatio(0, 1));
        using var json = JsonDocument.Parse(
            JsonSerializer.Serialize(metrics, new JsonSerializerOptions(JsonSerializerDefaults.Web))
        );
        JsonElement summary = json.RootElement.GetProperty("summary");
        summary.TryGetProperty("cohesionPopulation", out JsonElement population).Should().BeTrue();
        population.GetProperty("included").GetInt32().Should().Be(1);
        population.GetProperty("notApplicable").GetInt32().Should().Be(1);
        population.GetProperty("incomplete").GetInt32().Should().Be(1);
    }
}
