namespace Tertius.Testing;

/// <summary>Turns the nullable result of a read into its value, failing the test when the read gave null.</summary>
public static class NullableReads
{
    /// <summary>
    /// The value of <paramref name="read"/>. Throws <see cref="InvalidOperationException"/>, which fails the test, with
    /// <paramref name="because"/> if the read is null.
    /// </summary>
    public static T Found<T>(this T? read, string because = "")
        where T : struct =>
        read ?? throw new InvalidOperationException($"The read gave null. {because}".TrimEnd());
}
