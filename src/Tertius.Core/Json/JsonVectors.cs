using System.Globalization;
using System.Numerics;
using System.Text.Json;

namespace Tertius;

/// <summary>
/// The JSON form of numbers and vectors in scripts, recordings and state dumps. A vector is an array of its numbers on one
/// line, such as <c>[3, -4]</c>. Every converter that reads or writes one calls these methods, the converters of the host's
/// script options and the converter of <see cref="InputEvent"/> alike, so that a vector has the same form wherever it is.
/// </summary>
/// <remarks>
/// Reading is strict: an array with the wrong count of numbers, a value that is not a number, and a number that rounds to
/// infinity, such as 1e39 for a float, are errors. Writing is lenient, because a dump of a broken state must still write:
/// a number that JSON cannot hold, such as NaN, becomes a string.
/// </remarks>
internal static class JsonVectors
{
    /// <summary>
    /// Reads a <see cref="Vector2"/> from an array of exactly 2 numbers. The reader is at the start of the array, and it ends
    /// at the end of the array. <paramref name="form"/> names what the array is, for the message of an error.
    /// </summary>
    public static Vector2 ReadVector2(ref Utf8JsonReader reader, string form)
    {
        StartArray(ref reader, form);
        // C# evaluates arguments left to right, so this reads x, then y.
        var value = new Vector2(NextSingle(ref reader, form), NextSingle(ref reader, form));
        EndArray(ref reader, form);
        return value;
    }

    /// <summary>Writes <paramref name="value"/> as an array of its 2 numbers on one line.</summary>
    public static void WriteVector2(Utf8JsonWriter writer, Vector2 value) => WriteArray(writer, Number(value.X), Number(value.Y));

    // A vector reads from an array with its exact count of numbers: StartArray, then Next once per number, then EndArray.

    /// <summary>Checks that the reader is at the start of an array.</summary>
    public static void StartArray(ref Utf8JsonReader reader, string form)
    {
        if (reader.TokenType != JsonTokenType.StartArray)
            throw new JsonException($"Expected {form}.");
    }

    /// <summary>Reads the next value of the array, which must be a finite float.</summary>
    public static float NextSingle(ref Utf8JsonReader reader, string form)
    {
        if (!reader.Read() || reader.TokenType != JsonTokenType.Number)
            throw new JsonException($"Expected {form}.");
        return Finite(reader.GetSingle());
    }

    /// <summary>Reads the next value of the array, which must be a finite double.</summary>
    public static double NextDouble(ref Utf8JsonReader reader, string form)
    {
        if (!reader.Read() || reader.TokenType != JsonTokenType.Number)
            throw new JsonException($"Expected {form}.");
        return Finite(reader.GetDouble());
    }

    /// <summary>Reads the end of the array, which must come after its last number.</summary>
    public static void EndArray(ref Utf8JsonReader reader, string form)
    {
        if (!reader.Read() || reader.TokenType != JsonTokenType.EndArray)
            throw new JsonException($"Expected {form}.");
    }

    /// <summary>The value, or a <see cref="JsonException"/> for a value that rounded to infinity.</summary>
    public static float Finite(float value) =>
        float.IsFinite(value) ? value : throw new JsonException("The number is out of the range of a float.");

    /// <summary>The value, or a <see cref="JsonException"/> for a value that rounded to infinity.</summary>
    public static double Finite(double value) =>
        double.IsFinite(value) ? value : throw new JsonException("The number is out of the range of a double.");

    /// <summary>Writes the numbers, each from <see cref="Number(float)"/> or <see cref="Number(double)"/>, as one array on one line.</summary>
    public static void WriteArray(Utf8JsonWriter writer, params ReadOnlySpan<string> numbers) =>
        writer.WriteRawValue($"[{string.Join(", ", numbers.ToArray())}]", skipInputValidation: true);

    /// <summary>
    /// The shortest text that reads back to the same value. Adding +0 turns -0 into 0, so that two equal values write the
    /// same text. A value that JSON cannot hold, such as NaN, becomes a string.
    /// </summary>
    public static string Number(float value) =>
        float.IsFinite(value) ? (value + 0f).ToString("R", CultureInfo.InvariantCulture) : $"\"{value.ToString(CultureInfo.InvariantCulture)}\"";

    /// <inheritdoc cref="Number(float)"/>
    public static string Number(double value) =>
        double.IsFinite(value) ? (value + 0.0).ToString("R", CultureInfo.InvariantCulture) : $"\"{value.ToString(CultureInfo.InvariantCulture)}\"";
}
