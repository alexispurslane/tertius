using AwesomeAssertions;

namespace Tertius.Style.Tests;

/// <summary>
/// TS0010: a local that holds an <c>IDisposable</c> made with <c>new</c>, outside a <c>using</c>, that the code only
/// uses through its members. A local that the code returns, stores, passes as an argument or captures in a lambda hands
/// the object on, so the rule leaves it to its new owner.
/// </summary>
public sealed class UndisposedLocalTests
{
    [Fact]
    public async Task ALocalUsedOnlyThroughItsMembersIsReported() =>
        (await Analysis.Ids("TS0010", """
                                      using System.IO;
                                      public static class C
                                      {
                                          public static long Length()
                                          {
                                              var stream = new MemoryStream();
                                              stream.WriteByte(1);
                                              return stream.Length;
                                          }
                                      }
                                      """)).Should().Equal("TS0010");

    [Fact]
    public async Task ATargetTypedNewIsReported() =>
        (await Analysis.Ids("TS0010", """
                                      using System.IO;
                                      public static class C
                                      {
                                          public static void Write()
                                          {
                                              MemoryStream stream = new();
                                              stream.WriteByte(1);
                                          }
                                      }
                                      """)).Should().Equal("TS0010");

    [Fact]
    public async Task AnExplicitDisposeIsReported() =>
        (await Analysis.Ids("TS0010", """
                                      using System.IO;
                                      public static class C
                                      {
                                          public static void Write()
                                          {
                                              var stream = new MemoryStream();
                                              try
                                              {
                                                  stream.WriteByte(1);
                                              }
                                              finally
                                              {
                                                  stream.Dispose();
                                              }
                                          }
                                      }
                                      """)).Should().Equal(["TS0010"], "a using declaration says the same in one line");

    [Fact]
    public async Task AnUnusedLocalIsReported() =>
        (await Analysis.Ids("TS0010", """
                                      using System.IO;
                                      public static class C
                                      {
                                          public static void Make()
                                          {
                                              var stream = new MemoryStream();
                                          }
                                      }
                                      """)).Should().Equal("TS0010");

    [Fact]
    public async Task AConditionalAccessAndAnIndexAreMemberUses() =>
        (await Analysis.Ids("TS0010", """
                                      using System.IO;
                                      public static class C
                                      {
                                          public static byte First()
                                          {
                                              var stream = new MemoryStream(new byte[] { 7 });
                                              stream?.Flush();
                                              return stream?.GetBuffer()[0] ?? 0;
                                          }
                                          public static int Second()
                                          {
                                              var holder = new Holder();
                                              return holder[0];
                                          }
                                      }
                                      public sealed class Holder : System.IDisposable
                                      {
                                          public int this[int index] => index;
                                          public void Dispose() { }
                                      }
                                      """)).Should().Equal("TS0010", "TS0010");

    [Fact]
    public async Task TheMessageNamesTheLocalAndTheSection() =>
        (await Analysis.Diagnostics("TS0010", """
                                              using System.IO;
                                              public static class C
                                              {
                                                  public static void Make()
                                                  {
                                                      var stream = new MemoryStream();
                                                  }
                                              }
                                              """)).Single().GetMessage().Should()
        .Contain("stream").And.Contain("(STYLE.md, \"Disposal and ownership\")");

    [Fact]
    public async Task UsingDeclarationsAndStatementsAreNotReported() =>
        (await Analysis.Ids("TS0010", """
                                      using System.IO;
                                      using System.Threading.Tasks;
                                      public static class C
                                      {
                                          public static void Write()
                                          {
                                              using var first = new MemoryStream();
                                              using (var second = new MemoryStream())
                                                  second.WriteByte(1);
                                          }
                                          public static async Task WriteAsync()
                                          {
                                              await using var third = new MemoryStream();
                                              await third.FlushAsync();
                                          }
                                      }
                                      """)).Should().BeEmpty();

    [Fact]
    public async Task AReturnedLocalIsNotReported() =>
        (await Analysis.Ids("TS0010", """
                                      using System.IO;
                                      public static class C
                                      {
                                          public static MemoryStream Make()
                                          {
                                              var stream = new MemoryStream();
                                              stream.WriteByte(1);
                                              return stream;
                                          }
                                      }
                                      """)).Should().BeEmpty();

    [Fact]
    public async Task ALocalStoredInAFieldOrAPropertyIsNotReported() =>
        (await Analysis.Ids("TS0010", """
                                      using System.IO;
                                      public sealed class C
                                      {
                                          private MemoryStream? field;
                                          public MemoryStream? Property { get; set; }
                                          public void Keep()
                                          {
                                              var first = new MemoryStream();
                                              field = first;
                                              var second = new MemoryStream();
                                              Property = second;
                                          }
                                      }
                                      """)).Should().BeEmpty();

    [Fact]
    public async Task ALocalPassedAsAnArgumentIsNotReported() =>
        (await Analysis.Ids("TS0010", """
                                      using System.IO;
                                      public static class C
                                      {
                                          public static void Run()
                                          {
                                              var stream = new MemoryStream();
                                              Own(stream);
                                          }
                                          private static void Own(Stream stream) => stream.Dispose();
                                      }
                                      """)).Should().BeEmpty();

    [Fact]
    public async Task ALocalCapturedByALambdaIsNotReported() =>
        (await Analysis.Ids("TS0010", """
                                      using System;
                                      using System.IO;
                                      public static class C
                                      {
                                          public static Action Later()
                                          {
                                              var stream = new MemoryStream();
                                              return () => stream.Dispose();
                                          }
                                      }
                                      """)).Should().BeEmpty();

    [Fact]
    public async Task ALocalThatIsNotDisposableOrNotMadeWithNewIsNotReported() =>
        (await Analysis.Ids("TS0010", """
                                      using System.Collections.Generic;
                                      using System.IO;
                                      public static class C
                                      {
                                          public static long Length(string path)
                                          {
                                              var list = new List<int>();
                                              list.Add(1);
                                              var stream = File.OpenRead(path);
                                              return stream.Length + list.Count;
                                          }
                                      }
                                      """)).Should().BeEmpty();
}
