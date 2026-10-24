using AwesomeAssertions;

namespace Tertius.Style.Tests;

/// <summary>
/// TS0012 and TS0013: a type parameter constrained to <c>struct</c> (or <c>unmanaged</c>) and an interface is the hot
/// path form of polymorphism. A call on a <c>readonly</c> field or an <c>in</c> parameter of such a type copies the
/// struct first (TS0012), and a conversion of it to an interface type boxes it (TS0013). A field or a parameter that
/// the code only copies, such as an event payload, costs nothing more, so TS0012 reports the call, not the declaration.
/// </summary>
public sealed class GenericStructTests
{
    private const string Filter = """
        public interface IFilter
        {
            bool Matches(int value);
        }

        """;

    // TS0012

    [Fact]
    public async Task AReadonlyFieldOfTheTypeParameterIsReported() => (
            await Analysis.Ids(
                "TS0012",
                Filter
                    + """
                    public struct Cursor<T> where T : struct, IFilter
                    {
                        private readonly T filter;
                        public bool One() => filter.Matches(1);
                    }
                    """
            )
        ).Should().Equal("TS0012");

    [Fact]
    public async Task AnInParameterOfTheTypeParameterIsReported() => (
            await Analysis.Ids(
                "TS0012",
                Filter
                    + """
                    public static class Scan
                    {
                        public static bool One<T>(in T filter) where T : struct, IFilter => filter.Matches(1);
                    }
                    """
            )
        ).Should().Equal("TS0012");

    [Fact]
    public async Task ARefReadonlyParameterOfAnUnmanagedTypeParameterIsReported() => (
            await Analysis.Ids(
                "TS0012",
                Filter
                    + """
                    public static class Scan
                    {
                        public static bool One<T>(ref readonly T filter) where T : unmanaged, IFilter =>
                            filter.Matches(1);
                    }
                    """
            )
        ).Should().Equal("TS0012");

    [Fact]
    public async Task TheMessageOfACopyNamesTheTypeParameterAndTheSection() => (
            await Analysis.Diagnostics(
                "TS0012",
                Filter
                    + """
                    public static class Scan
                    {
                        public static bool One<T>(in T filter) where T : struct, IFilter => filter.Matches(1);
                    }
                    """
            )
        ).Single().GetMessage().Should().Contain("filter").And.Contain("(STYLE.md, \"Polymorphism is an interface\")");

    [Fact]
    public async Task APropertyReadOnAReadonlyFieldIsReported() => (
            await Analysis.Ids(
                "TS0012",
                """
                public interface ISized
                {
                    int Size { get; }
                }
                public sealed class Holder<T> where T : struct, ISized
                {
                    private readonly T value;
                    public int Size => value.Size;
                }
                """
            )
        ).Should().Equal("TS0012");

    [Fact]
    public async Task EachCallIsReported() => (
            await Analysis.Ids(
                "TS0012",
                Filter
                    + """
                    public static class Scan
                    {
                        public static bool Two<T>(in T filter) where T : struct, IFilter =>
                            filter.Matches(1) && filter.Matches(2);
                    }
                    """
            )
        ).Should().Equal("TS0012", "TS0012");

    [Fact]
    public async Task AnInParameterThatTheCodeOnlyCopiesIsNotReported() => (
            await Analysis.Ids(
                "TS0012",
                """
                public interface IPayload;
                public struct Slot<T> where T : unmanaged, IPayload
                {
                    private readonly T kept;
                    public T Value;
                    public Slot(in T payload) { kept = payload; Value = payload; }
                    public readonly T Kept() => kept;
                }
                """
            )
        ).Should().BeEmpty("a copy into storage calls nothing on the struct");

    [Fact]
    public async Task AFieldThatIsNotReadonlyAndARefParameterAreNotReported() => (
            await Analysis.Ids(
                "TS0012",
                Filter
                    + """
                    public struct Cursor<T> where T : struct, IFilter
                    {
                        private T filter;
                        public bool One() => filter.Matches(1);
                        public static bool Two(ref T other) => other.Matches(2);
                    }
                    """
            )
        ).Should().BeEmpty();

    [Fact]
    public async Task ATypeParameterWithoutAStructConstraintOrAnInterfaceIsNotReported() => (
            await Analysis.Ids(
                "TS0012",
                Filter
                    + """
                    public readonly struct Holder<TValue, TFilter, TAny>
                        where TValue : struct where TFilter : class, IFilter
                    {
                        private readonly TValue value;
                        private readonly TFilter filter;
                        private readonly TAny any;
                        public int Hash() => value.GetHashCode() + (filter.Matches(1) ? 1 : 0) + any!.GetHashCode();
                        public static int Size(in TValue value) => value.GetHashCode();
                    }
                    """
            )
        ).Should().BeEmpty();

    [Fact]
    public async Task AnInParameterOfAConcreteStructIsNotReported() => (
            await Analysis.Ids(
                "TS0012",
                Filter
                    + """
                    public readonly struct Even : IFilter
                    {
                        public bool Matches(int value) => value % 2 == 0;
                    }
                    public static class Scan
                    {
                        public static bool One(in Even filter) => filter.Matches(1);
                    }
                    """
            )
        ).Should().BeEmpty();

    // TS0013

    [Fact]
    public async Task AnImplicitConversionToTheInterfaceIsReported() => (
            await Analysis.Ids(
                "TS0013",
                Filter
                    + """
                    public static class Scan
                    {
                        public static IFilter Boxed<T>(T filter) where T : struct, IFilter => filter;
                    }
                    """
            )
        ).Should().Equal("TS0013");

    [Fact]
    public async Task PassingTheStructToAnInterfaceParameterIsReported() => (
            await Analysis.Ids(
                "TS0013",
                Filter
                    + """
                    public static class Scan
                    {
                        private static bool Take(IFilter filter) => filter.Matches(1);
                        public static bool Run<T>(T filter) where T : unmanaged, IFilter => Take(filter);
                    }
                    """
            )
        ).Should().Equal("TS0013");

    [Fact]
    public async Task ACastToAnInterfaceIsReported() => (
            await Analysis.Ids(
                "TS0013",
                Filter
                    + """
                    public static class Scan
                    {
                        public static object Run<T>(T filter) where T : struct, IFilter =>
                            (System.IComparable)(object)filter;
                        public static IFilter Cast<T>(T filter) where T : struct, IFilter => (IFilter)filter;
                    }
                    """
            )
        ).Should().Equal(["TS0013"], "the conversion to object is not to an interface, and the cast from object unboxes nothing new");

    [Fact]
    public async Task TheMessageOfABoxNamesTheTypesAndTheSection() => (
            await Analysis.Diagnostics(
                "TS0013",
                Filter
                    + """
                    public static class Scan
                    {
                        public static IFilter Boxed<T>(T filter) where T : struct, IFilter => filter;
                    }
                    """
            )
        ).Single().GetMessage().Should().Contain("'T'").And.Contain("'IFilter'").And.Contain("(STYLE.md, \"Polymorphism is an interface\")");

    [Fact]
    public async Task PassingTheStructOnAsATypeParameterIsNotReported() => (
            await Analysis.Ids(
                "TS0013",
                Filter
                    + """
                    public static class Scan
                    {
                        private static bool Take<U>(U filter) where U : struct, IFilter => filter.Matches(1);
                        public static bool Run<T>(T filter) where T : struct, IFilter => Take(filter);
                    }
                    """
            )
        ).Should().BeEmpty();

    [Fact]
    public async Task AConversionOfAClassTypeParameterOrAConcreteStructIsNotReported() => (
            await Analysis.Ids(
                "TS0013",
                Filter
                    + """
                    public readonly struct Even : IFilter
                    {
                        public bool Matches(int value) => value % 2 == 0;
                    }
                    public static class Scan
                    {
                        public static IFilter Plain<T>(T filter) where T : class, IFilter => filter;
                        public static IFilter Concrete(Even filter) => filter;
                    }
                    """
            )
        ).Should().BeEmpty();
}
