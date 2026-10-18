using System.Globalization;
using System.Numerics;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Tertius;

/// <summary>
/// The JSON form of an <see cref="InputEvent"/>, which scripts and recordings use. A key or a mouse button is
/// <c>{"kind":"keyDown","code":87}</c>, and a move or a scroll is <c>{"kind":"mouseMove","delta":[3,-4]}</c>.
/// </summary>
/// <remarks>
/// <para>
/// The kind is <c>keyDown</c>, <c>keyUp</c>, <c>mouseDown</c>, <c>mouseUp</c>, <c>mouseMove</c> or <c>scroll</c>. Writing
/// uses these names. Reading ignores the case of a kind name, as it does for an enum value anywhere else in a script. The
/// event of a key or a button has the member <c>code</c>, an integer, and the event of a move or a scroll has the member
/// <c>delta</c>, an array of two finite numbers. A kind has exactly its own members. Reading refuses, with a
/// <see cref="JsonException"/>, a kind that is missing or unknown, a member that the kind needs and lacks, a member that
/// the kind does not have, a member that is given twice, and a value of the wrong form.
/// </para>
/// <para>
/// The member names do not follow the naming policy of the options, so the form is the same in every serializer. Writing
/// is lenient, as a state dump has to be. The default event, which holds no case, is <c>null</c>, which reading refuses.
/// A delta is written on one line, and a number that JSON cannot hold, such as NaN, becomes a string.
/// </para>
/// </remarks>
internal sealed class InputEventJsonConverter : JsonConverter<InputEvent>
{
    private const string KeyDown = "keyDown", KeyUp = "keyUp", MouseDown = "mouseDown", MouseUp = "mouseUp", MouseMove = "mouseMove", Scroll = "scroll";
    private const string Form = "an input event, an object with a kind and its code or its delta";
    private const string DeltaForm = "delta must be an array of 2 finite numbers";

    public override InputEvent Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        if (reader.TokenType != JsonTokenType.StartObject)
            throw new JsonException($"Expected {Form}.");

        string? kind = null;
        int? code = null;
        Vector2? delta = null;
        while (reader.Read() && reader.TokenType == JsonTokenType.PropertyName)
        {
            if (reader.ValueTextEquals("kind"u8))
            {
                if (kind != null)
                    throw new JsonException("The kind is given twice.");
                reader.Read();
                kind = reader.TokenType == JsonTokenType.String ? reader.GetString() : throw new JsonException("The kind must be a string, such as \"keyDown\".");
            }
            else if (reader.ValueTextEquals("code"u8))
            {
                if (code != null)
                    throw new JsonException("The code is given twice.");
                reader.Read();
                code = reader.TokenType == JsonTokenType.Number && reader.TryGetInt32(out int value) ? value : throw new JsonException("The code must be an integer.");
            }
            else if (reader.ValueTextEquals("delta"u8))
            {
                if (delta != null)
                    throw new JsonException("The delta is given twice.");
                delta = ReadDelta(ref reader);
            }
            else
            {
                throw new JsonException($"The member '{reader.GetString()}' is not a member of an input event. The members are kind, code and delta.");
            }
        }

        if (kind == null)
            throw new JsonException($"An input event needs a kind: {KeyDown}, {KeyUp}, {MouseDown}, {MouseUp}, {MouseMove} or {Scroll}.");
        if (Is(kind, KeyDown))
            return InputEvent.KeyDown(CodeOf(kind, code, delta));
        if (Is(kind, KeyUp))
            return InputEvent.KeyUp(CodeOf(kind, code, delta));
        if (Is(kind, MouseDown))
            return InputEvent.MouseDown(CodeOf(kind, code, delta));
        if (Is(kind, MouseUp))
            return InputEvent.MouseUp(CodeOf(kind, code, delta));
        if (Is(kind, MouseMove))
            return InputEvent.MouseMove(DeltaOf(kind, code, delta));
        if (Is(kind, Scroll))
            return InputEvent.Scroll(DeltaOf(kind, code, delta));
        throw new JsonException($"'{kind}' is not an input event kind. The kinds are {KeyDown}, {KeyUp}, {MouseDown}, {MouseUp}, {MouseMove} and {Scroll}.");
    }

    public override void Write(Utf8JsonWriter writer, InputEvent value, JsonSerializerOptions options)
    {
        switch (value)
        {
            case KeyChanged(var code, var isDown):
                WriteCode(writer, isDown ? KeyDown : KeyUp, code);
                break;
            case MouseButtonChanged(var code, var isDown):
                WriteCode(writer, isDown ? MouseDown : MouseUp, code);
                break;
            case MouseMoved(var delta):
                WriteDelta(writer, MouseMove, delta);
                break;
            case Scrolled(var delta):
                WriteDelta(writer, Scroll, delta);
                break;
            case null:
                writer.WriteNullValue();
                break;
        }
    }

    // The members of a kind of key or button event: its code and nothing else.
    private static int CodeOf(string kind, int? code, Vector2? delta)
    {
        if (delta != null)
            throw new JsonException($"A {kind} event has a code, not a delta.");
        return code ?? throw new JsonException($"A {kind} event needs a code.");
    }

    // The members of a kind of move or scroll event: its delta and nothing else.
    private static Vector2 DeltaOf(string kind, int? code, Vector2? delta)
    {
        if (code != null)
            throw new JsonException($"A {kind} event has a delta, not a code.");
        return delta ?? throw new JsonException($"A {kind} event needs a delta.");
    }

    private static bool Is(string kind, string name) => string.Equals(kind, name, StringComparison.OrdinalIgnoreCase);

    // The reader is at the name of the member. A delta reads from an array with its exact count of numbers: StartArray, then
    // a number twice, then EndArray.
    private static Vector2 ReadDelta(ref Utf8JsonReader reader)
    {
        if (!reader.Read() || reader.TokenType != JsonTokenType.StartArray)
            throw new JsonException($"The {DeltaForm}.");
        var delta = new Vector2(NextFinite(ref reader), NextFinite(ref reader));
        if (!reader.Read() || reader.TokenType != JsonTokenType.EndArray)
            throw new JsonException($"The {DeltaForm}.");
        return delta;
    }

    private static float NextFinite(ref Utf8JsonReader reader) =>
        reader.Read() && reader.TokenType == JsonTokenType.Number && reader.TryGetSingle(out float value) && float.IsFinite(value)
            ? value
            : throw new JsonException($"The {DeltaForm}.");

    private static void WriteCode(Utf8JsonWriter writer, string kind, int code)
    {
        writer.WriteStartObject();
        writer.WriteString("kind", kind);
        writer.WriteNumber("code", code);
        writer.WriteEndObject();
    }

    private static void WriteDelta(Utf8JsonWriter writer, string kind, Vector2 delta)
    {
        writer.WriteStartObject();
        writer.WriteString("kind", kind);
        writer.WritePropertyName("delta");
        writer.WriteRawValue($"[{Number(delta.X)}, {Number(delta.Y)}]", skipInputValidation: true);
        writer.WriteEndObject();
    }

    // The shortest text that reads back to the same value. Adding +0 turns -0 into 0, so that two equal events write equal text.
    // A value that JSON cannot hold, such as NaN, becomes a string, so that a dump of a broken state still writes.
    private static string Number(float value) =>
        float.IsFinite(value) ? (value + 0f).ToString("R", CultureInfo.InvariantCulture) : $"\"{value.ToString(CultureInfo.InvariantCulture)}\"";
}
