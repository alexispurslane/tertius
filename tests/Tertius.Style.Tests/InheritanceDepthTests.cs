using AwesomeAssertions;
using Microsoft.CodeAnalysis.CSharp;

namespace Tertius.Style.Tests;

/// <summary>
/// TS0007: a class more than 2 levels below the root of its hierarchy, counting only the classes of this solution: the
/// classes of the compilation and of the assemblies whose names start with <c>Tertius</c>. A base from outside the
/// solution, such as <c>JsonConverter&lt;T&gt;</c>, is a root, as <c>object</c> is.
/// </summary>
public sealed class InheritanceDepthTests
{
    [Fact]
    public async Task AClassThreeLevelsBelowObjectIsReported() =>
        (await Analysis.Ids("TS0007", """
                                      public abstract class A;
                                      public abstract class B : A;
                                      public sealed class C : B;
                                      """)).Should().Equal("TS0007");

    [Fact]
    public async Task EveryLevelPastTheSecondIsReported() =>
        (await Analysis.Ids("TS0007", """
                                      public abstract class A;
                                      public abstract class B : A;
                                      public abstract class C : B;
                                      public sealed class D : C;
                                      """)).Should().Equal("TS0007", "TS0007");

    [Fact]
    public async Task TheLevelsOfAnotherAssemblyOfTheSolutionCount()
    {
        CSharpCompilation library = TestCompilation.Of("""
                                                       public abstract class A;
                                                       public abstract class B : A;
                                                       """, assembly: "Tertius.Library", libraries: []);
        CSharpCompilation probe = TestCompilation.Of("public sealed class C : B;", assembly: "Probe",
            libraries: [library]);

        (await Analysis.Diagnostics("TS0007", probe)).Select(diagnostic => diagnostic.Id).Should().Equal("TS0007");
    }

    [Fact]
    public async Task TheMessageNamesTheChainAndTheSection() =>
        (await Analysis.Diagnostics("TS0007", """
                                              public abstract class A;
                                              public abstract class B : A;
                                              public sealed class C : B;
                                              """)).Single().GetMessage().Should()
        .Contain("C : B : A").And.Contain("(STYLE.md, \"Composition\")");

    [Fact]
    public async Task AClassTwoLevelsBelowObjectIsNotReported() =>
        (await Analysis.Ids("TS0007", """
                                      public abstract class A;
                                      public sealed class B : A;
                                      """)).Should().BeEmpty();

    [Fact]
    public async Task AFrameworkBaseIsTheRootOfTheHierarchy() =>
        (await Analysis.Ids("TS0007", """
                                      using System.Text.Json;
                                      using System.Text.Json.Serialization;
                                      public abstract class Converter<T> : JsonConverter<T>;
                                      public sealed class IntConverter : Converter<int>
                                      {
                                          public override int Read(ref Utf8JsonReader reader, System.Type type, JsonSerializerOptions options) => 0;
                                          public override void Write(Utf8JsonWriter writer, int value, JsonSerializerOptions options) { }
                                      }
                                      """)).Should()
        .BeEmpty("JsonConverter<T> derives from JsonConverter, which the code cannot change");

    [Fact]
    public async Task TheLevelsOfAnAssemblyOutsideTheSolutionDoNotCount()
    {
        CSharpCompilation library = TestCompilation.Of("""
                                                       public abstract class A;
                                                       public abstract class B : A;
                                                       """, assembly: "Library", libraries: []);
        CSharpCompilation probe = TestCompilation.Of("""
                                                     public abstract class C : B;
                                                     public sealed class D : C;
                                                     """, assembly: "Probe", libraries: [library]);

        (await Analysis.Diagnostics("TS0007", probe)).Should().BeEmpty();
    }

    [Fact]
    public async Task ARecordChainCountsAsAClassChain() =>
        (await Analysis.Ids("TS0007", """
                                      public abstract record A;
                                      public abstract record B : A;
                                      public sealed record C : B;
                                      """)).Should().Equal("TS0007");
}
