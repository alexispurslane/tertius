using System.Text.Json;
using AwesomeAssertions;

namespace Tertius.Slop.Tests;

/// <summary>Pooled class measures and the source evidence from which their counts are derived.</summary>
public sealed class ClassMetricsTests
{
    [Fact]
    public void TccPoolsPairsInsteadOfAveragingTypes()
    {
        JsonElement metrics = Measure(
            """
            class A { int x; public int F() => x; public int G() => x; }
            class B { int x; int y; public int F() => x; public int G() => y; public int H() => 0; }
            """
        );

        AssertRatio(metrics: metrics, name: "tcc", numerator: 1, denominator: 4, ratio: 0.25);
    }

    [Fact]
    public void NoEligiblePairsAreNotApplicable()
    {
        JsonElement metrics = Measure("class A { public void F() {} }");

        AssertRatio(metrics: metrics, name: "tcc", numerator: 0, denominator: 0, ratio: null);
    }

    [Fact]
    public void PartialsShareFieldIdentityButAParameterDoesNotShareState()
    {
        JsonElement metrics = Measure(
            """
            partial class A { int x; internal int F() => x; }
            partial class A { internal int G(int x) => x; internal int H() => this.x; }
            """
        );

        AssertRatio(metrics: metrics, name: "tcc", numerator: 1, denominator: 3, ratio: 1.0 / 3);
        JsonElement type = metrics.GetProperty("types").EnumerateArray().Single();
        type.GetProperty("locations").GetArrayLength().Should().Be(2);
        type.GetProperty("methods").GetArrayLength().Should().Be(3);
        type.GetProperty("sharedPairs").GetArrayLength().Should().Be(1);
    }

    [Fact]
    public void ExtensionBlocksBelongToTheirContainingType()
    {
        var codebase = Source.UsedByATest(
            SourceFile.Parse(
                "src/A.cs",
                """
                class Target {}
                static class Extensions { extension(Target value) { public int Read() => 1; } }
                """
            )
        );
        ClassMetrics metrics = ClassMetrics.Of(codebase);

        metrics.Types.Select(type => type.Name).Should().BeEquivalentTo("Target", "Extensions");
        ClassMeasure extensions = metrics.Types.Single(type => type.Name == "Extensions");
        extensions.Members.Should().ContainSingle().Which.Name.Should().Contain("Read");
    }

    [Fact]
    public void OtherInstancesFieldsDoNotShareThisInstancesState()
    {
        JsonElement metrics = Measure("class A { int x; public int F() => x; public int G(A other) => other.x; }");

        AssertRatio(metrics: metrics, name: "tcc", numerator: 0, denominator: 1, ratio: 0);
    }

    [Theory]
    [InlineData("class A { int x; public A F() => new A { x = 1 }; public int G() => x; }")]
    [InlineData("record A { int x; public A F() => this with { x = 1 }; public int G() => x; }")]
    [InlineData(
        "class B { public int x; } class A { int state; public B F() => new B { x = 1 }; public B G() => new B { x = 2 }; }"
    )]
    public void InitializersChangeAnotherInstanceWithoutConnectingThisInstancesMethods(string source)
    {
        JsonElement metrics = Measure(source);

        AssertRatio(metrics: metrics, name: "tcc", numerator: 0, denominator: 1, ratio: 0);
    }

    [Fact]
    public void InheritedInstanceStateCanConnectDeclaredMethods()
    {
        JsonElement metrics = Measure(
            """
            class Base { protected int x; }
            class Child : Base { public int F() => x; public int G() => x; }
            """
        );

        AssertRatio(metrics: metrics, name: "tcc", numerator: 1, denominator: 1, ratio: 1);
    }

    [Fact]
    public void StateInAttributesDoesNotConnectMethodBodies()
    {
        JsonElement metrics = Measure(
            """
            class A {
                int x;
                [System.Obsolete(nameof(x))] public int F() => 1;
                public int G() => x;
            }
            """
        );

        AssertRatio(metrics: metrics, name: "tcc", numerator: 0, denominator: 1, ratio: 0);
    }

    [Fact]
    public void GeneratedFilesDoNotContributeClassCounts()
    {
        var codebase = Source.UsedByATest(
            SourceFile.Parse("src/A.cs", "class A {}"),
            SourceFile.Parse("src/Generated.g.cs", "class Generated { public void Work() {} }")
        );

        ClassMetrics metrics = ClassMetrics.Of(codebase);

        metrics.Types.Should().ContainSingle().Which.Name.Should().Be("A");
        metrics.Summary.Tcc.Should().Be(new ClassRatio(0, 0));
    }

    [Fact]
    public void NamedDetailKeepsTheMethodBodyLocationAndSharedFields()
    {
        var codebase = Source.UsedByATest(
            SourceFile.Parse(
                "src/A.cs",
                """
                class A {
                    int x;
                    internal int F() => x;
                    internal int G() => x;
                }
                """
            )
        );

        ClassMeasure type = ClassMetrics.Of(codebase).Types.Single();

        type.Methods[0].Member.Location.Should().Be(new SourcePlace("src/A.cs", 3, 3));
        type.SharedPairs.Should().ContainSingle().Which.Fields.Should().Equal("A.x");
        type.SharedPairs[0].First.Should().Be("A.F()");
        type.SharedPairs[0].Second.Should().Be("A.G()");
    }

    [Fact]
    public void BindingFailuresAreReportedEvenWhenNoNamedTypeIsMissing()
    {
        JsonElement metrics = Measure("class A { public int F() => UnknownMethod(); }");

        metrics.TryGetProperty("bindingDiagnostics", out JsonElement diagnostics).Should().BeTrue();
        diagnostics.EnumerateArray().Should().Contain(item => item.GetProperty("id").GetString() == "CS0103");
        metrics.GetProperty("summary").GetProperty("bindingErrorCount").GetInt32().Should().BeGreaterThan(0);
    }

    [Fact]
    public void PartialMethodsUseTheImplementationBodyOnce()
    {
        JsonElement metrics = Measure(
            """
            partial class A { int x; internal partial int F(); internal int G() => x; }
            partial class A { internal partial int F() => x; }
            """
        );

        AssertRatio(metrics: metrics, name: "tcc", numerator: 1, denominator: 1, ratio: 1);
    }

    [Fact]
    public void ConditionalAccessToAnotherInstanceDoesNotShareState()
    {
        JsonElement metrics = Measure("class A { int x; public int F() => x; public int? G(A other) => other?.x; }");

        AssertRatio(metrics: metrics, name: "tcc", numerator: 0, denominator: 1, ratio: 0);
    }

    [Fact]
    public void IndirectStateAndConstructorsDoNotCreateDirectPairs()
    {
        JsonElement metrics = Measure(
            "class A { int x; int y; public A() { x = y; } public int F() => Helper(); public int G() => y; private int Helper() => y; }"
        );

        AssertRatio(metrics: metrics, name: "tcc", numerator: 0, denominator: 1, ratio: 0);
    }

    [Fact]
    public void TypeFunctionWeightUsesExistingComplexityAndExcludesNestedTypes()
    {
        JsonElement metrics = Measure(
            """
            class Outer {
                internal int Work(int x) {
                    int Local(int v) { if (v > 0) return 1; return 0; }
                    return x switch { 0 => Local(x), _ => 1 };
                }
                class Inner { internal int Work(int x) => x > 0 ? 1 : 0; }
            }
            """
        );

        JsonElement type = metrics
            .GetProperty("types")
            .EnumerateArray()
            .Single(type => type.GetProperty("name").GetString() == "Outer");
        type.TryGetProperty("wmc", out JsonElement wmc).Should().BeTrue();
        wmc.GetInt32().Should().Be(4);
        type.GetProperty("functions").GetArrayLength().Should().Be(2);
    }

    [Fact]
    public void InheritedStateKeepsUnconnectedVisibleMethodsInTheDenominator()
    {
        JsonElement metrics = Measure(
            """
            class Base { protected int x; }
            class Child : Base { public int F() => 1; public int G() => 2; }
            """
        );

        AssertRatio(metrics: metrics, name: "tcc", numerator: 0, denominator: 1, ratio: 0);
    }

    [Fact]
    public void TypeEvidenceIncludesEnumsAndDelegatesWithoutInventingMethods()
    {
        JsonElement metrics = Measure("enum Color { Red, Blue } delegate int Read(int value);");

        JsonElement[] types = metrics.GetProperty("types").EnumerateArray().ToArray();
        types.Select(type => type.GetProperty("name").GetString()).Should().BeEquivalentTo("Color", "Read");
        types
            .Single(type => type.GetProperty("name").GetString() == "Color")
            .GetProperty("members")
            .GetArrayLength()
            .Should()
            .Be(2);
        types.Should().OnlyContain(type => type.GetProperty("methods").GetArrayLength() == 0);
    }

    [Fact]
    public void MemberSignaturesResolveAliasesWithoutConfusingSameNamedTypes()
    {
        var codebase = Source.UsedByATest(
            SourceFile.Parse(
                "src/A.cs",
                """
                using Alias = First.Token;
                namespace First { class Token {} }
                namespace Second { class Token {} }
                class C { internal void F(Alias value) {} internal void G(Second.Token value) {} }
                """
            )
        );

        ClassMeasure type = ClassMetrics.Of(codebase).Types.Single(type => type.Name == "C");

        type.Members.Select(member => member.Name).Should().BeEquivalentTo("C.F(First.Token)", "C.G(Second.Token)");
    }

    [Fact]
    public void NamesOfFieldsDoNotShareRuntimeState()
    {
        JsonElement metrics = Measure("class A { int x; public string F() => nameof(x); public int G() => x; }");

        AssertRatio(metrics: metrics, name: "tcc", numerator: 0, denominator: 1, ratio: 0);
    }

    [Fact]
    public void DepthPoolsWeightAndSurfaceAcrossTheSameTypes()
    {
        JsonElement metrics = Measure(
            """
            class A { public void F() {} private int Hidden(bool b) => b ? 1 : 0; }
            class B { public int x; public int y; public void F() {} }
            class Hidden { private int F(bool b) => b ? 1 : 0; }
            """
        );

        AssertRatio(metrics: metrics, name: "depth", numerator: 6, denominator: 4, ratio: 1.5);
        JsonElement hidden = metrics
            .GetProperty("types")
            .EnumerateArray()
            .Single(type => type.GetProperty("name").GetString() == "Hidden");
        hidden.GetProperty("depth").GetProperty("numerator").GetInt64().Should().Be(2);
        hidden.GetProperty("depth").GetProperty("denominator").GetInt64().Should().Be(0);
        hidden.GetProperty("depth").GetProperty("ratio").ValueKind.Should().Be(JsonValueKind.Null);
    }

    [Fact]
    public void SurfaceCountsFieldsAndPropertiesOnceAlongsideEveryVisibleOverload()
    {
        JsonElement metrics = Measure(
            """
            class A {
                public int X, Y;
                internal int Value { get; set; }
                protected int this[int i] { get => i; set {} }
                public event System.Action E;
                public A() {} internal A(int n) {}
                public void F() {} public void F(int n) {}
                internal static void S() {}
                protected internal void P() {} private protected void Q() {}
                private void Hidden() {}
            }
            """
        );

        JsonElement type = metrics.GetProperty("types").EnumerateArray().Single();
        type.TryGetProperty("apiEntries", out JsonElement entries).Should().BeTrue();
        entries.GetInt32().Should().Be(12);
        JsonElement[] surface = type.GetProperty("members")
            .EnumerateArray()
            .Where(member => member.GetProperty("isApiEntry").GetBoolean())
            .ToArray();
        surface.Length.Should().Be(12);
        surface.Should().Contain(member => member.GetProperty("name").GetString() == "A.Value");
        surface.Should().Contain(member => member.GetProperty("name").GetString() == "A.X");
        surface
            .Should()
            .NotContain(member => member.GetProperty("name").GetString()!.Contains("Hidden", StringComparison.Ordinal));
    }

    [Fact]
    public void InheritedAndSynthesizedMembersDoNotWidenDeclaredSurface()
    {
        JsonElement metrics = Measure(
            "class Base { public void F() {} } class Child : Base { public int X; } record Data(int X);"
        );

        JsonElement child = metrics
            .GetProperty("types")
            .EnumerateArray()
            .Single(type => type.GetProperty("name").GetString() == "Child");
        child.TryGetProperty("apiEntries", out JsonElement entries).Should().BeTrue();
        entries.GetInt32().Should().Be(1);
        child.GetProperty("wmc").GetInt32().Should().Be(0);
        JsonElement data = metrics
            .GetProperty("types")
            .EnumerateArray()
            .Single(type => type.GetProperty("name").GetString() == "Data");
        data.GetProperty("members")
            .GetArrayLength()
            .Should()
            .Be(1, "only the written primary constructor is a declared entry");
        data.GetProperty("apiEntries").GetInt32().Should().Be(1);
    }

    [Fact]
    public void NoApiSurfaceKeepsRawWeightWithoutInventingDepth()
    {
        JsonElement metrics = Measure("class A { private void F() {} }");

        AssertRatio(metrics: metrics, name: "depth", numerator: 1, denominator: 0, ratio: null);
    }

    private static JsonElement Measure(string text)
    {
        var codebase = Source.UsedByATest(SourceFile.Parse("src/Example.cs", text));
        var report = SlopReport.Of(codebase, Clones.FromReport(VerbosityReportTests.Report()));
        using var json = JsonDocument.Parse(
            JsonSerializer.Serialize(report, new JsonSerializerOptions(JsonSerializerDefaults.Web))
        );
        json.RootElement.TryGetProperty("classMetrics", out JsonElement metrics)
            .Should()
            .BeTrue("all reports include class statistics");
        return metrics.Clone();
    }

    private static void AssertRatio(JsonElement metrics, string name, long numerator, long denominator, double? ratio)
    {
        metrics.GetProperty("summary").TryGetProperty(name, out JsonElement measure).Should().BeTrue();
        measure.GetProperty("numerator").GetInt64().Should().Be(numerator);
        measure.GetProperty("denominator").GetInt64().Should().Be(denominator);
        if (ratio is { } value)
            measure.GetProperty("ratio").GetDouble().Should().BeApproximately(value, 1e-12);
        else
            measure.GetProperty("ratio").ValueKind.Should().Be(JsonValueKind.Null);
    }
}
