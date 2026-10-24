using AwesomeAssertions;

namespace Tertius.Style.Tests;

/// <summary>TS0008: a finalizer. A resource that escapes disposal is a bug for a test to find.</summary>
public sealed class FinalizerTests
{
    [Fact]
    public async Task AFinalizerIsReported() =>
        (await Analysis.Ids("TS0008", """
                                      public sealed class Handle
                                      {
                                          ~Handle() { }
                                      }
                                      """)).Should().Equal("TS0008");

    [Fact]
    public async Task TheMessageNamesTheSection() =>
        (await Analysis.Diagnostics("TS0008", """
                                              public sealed class Handle
                                              {
                                                  ~Handle() { }
                                              }
                                              """)).Single().GetMessage().Should()
        .Contain("Handle").And.Contain("(STYLE.md, \"Disposal and ownership\")");

    [Fact]
    public async Task ADisposeMethodIsNotReported() =>
        (await Analysis.Ids("TS0008", """
                                      public sealed class Handle : System.IDisposable
                                      {
                                          public void Dispose() { }
                                      }
                                      """)).Should().BeEmpty();
}
