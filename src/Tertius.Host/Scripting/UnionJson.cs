using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.Json.Serialization.Metadata;

namespace Tertius.Host.Scripting;

/// <summary>
/// An error of a converter of a union. System.Text.Json sets the path and the position of such an error as properties, but
/// writes them into the message of its own errors only, so <see cref="Script.Parse"/> adds them to the message of this one.
/// </summary>
internal sealed class UnionJsonException(string message) : JsonException(message);

/// <summary>One case of a union in JSON: the name that its key has in a script, the record type of the case, and how to put a case into the union.</summary>
/// <typeparam name="TUnion">The union.</typeparam>
internal sealed class JsonCase<TUnion>(string name, Type type, Func<object, TUnion> wrap)
{
    public string Name => name;

    public Type Type => type;

    /// <summary>The name as UTF-8, so that reading compares it with the key's bytes and makes no string.</summary>
    public byte[] NameUtf8 { get; } = Encoding.UTF8.GetBytes(name);

    public TUnion Wrap(object held) => wrap(held);
}

/// <summary>Makes the <see cref="JsonCase{TUnion}"/> of a union.</summary>
internal static class JsonCase
{
    /// <summary>The case <typeparamref name="TCase"/> of <typeparamref name="TUnion"/>, which scripts call <paramref name="name"/>.</summary>
    public static JsonCase<TUnion> Of<TCase, TUnion>(string name, Func<TCase, TUnion> wrap) where TCase : class =>
        new(name, typeof(TCase), held => wrap((TCase)held));
}

/// <summary>
/// The JSON form of a custom union whose cases are records: one JSON object with a key, such as <c>do</c> or <c>kind</c>,
/// whose string value names the case, and with the members of the case beside it, in any order. A converter of the union
/// holds one of these.
/// </summary>
/// <remarks>
/// <para>
/// Reading finds the key in a copy of the reader, then hands the whole object to System.Text.Json as the record of the
/// case, with the same options, so every rule of the strict reader holds for the members: an unknown member, a duplicate
/// member, a missing member and a wrong value are errors, and they carry its words. The record's contract has the key as one more
/// member (<see cref="AddKeyTo"/>), which reading ignores because the name was read already, and which writing puts first.
/// </para>
/// <para>
/// That is why the options must be those of <see cref="ScriptJson"/>: other options leave the key out of the contract, and
/// the converter refuses them with an <see cref="InvalidOperationException"/> instead of writing a step without its key.
/// A path in a message of a member is relative to the record of the case, such as <c>$.ticks</c>, because the record is read
/// by a call of its own. The line and the position in the line stay those of the whole text.
/// </para>
/// </remarks>
/// <typeparam name="TUnion">The union.</typeparam>
/// <param name="key">The key that names the case, such as <c>do</c>.</param>
/// <param name="what">What the union is, in a message, such as <c>step</c>.</param>
/// <param name="cases">The cases, in the order that a message lists them.</param>
internal sealed class JsonCases<TUnion>(string key, string what, params JsonCase<TUnion>[] cases)
{
    private readonly byte[] keyUtf8 = Encoding.UTF8.GetBytes(key);

    /// <summary>
    /// Reads the object at the reader as one case of the union. Throws <see cref="JsonException"/> for a value that is not an
    /// object, an object without the key or with it twice, a value of the key that is not a string or names no case, and
    /// whatever the record of the case refuses.
    /// </summary>
    public TUnion Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        if (reader.TokenType != JsonTokenType.StartObject)
            throw new UnionJsonException($"The JSON value could not be converted to {typeToConvert}.");
        JsonCase<TUnion> found = FindCase(reader);
        object held = JsonSerializer.Deserialize(ref reader, InfoOf(found, options)) ?? throw new UnionJsonException($"The {what} is null.");
        return found.Wrap(held);
    }

    /// <summary>Writes the held case of a union as an object that starts with the key.</summary>
    public void Write(Utf8JsonWriter writer, object held, JsonSerializerOptions options)
    {
        JsonCase<TUnion> found = cases.Single(c => c.Type == held.GetType());
        JsonSerializer.Serialize(writer, held, InfoOf(found, options));
    }

    /// <summary>A modifier of a type info resolver: gives the record of each case the key as a member that is first and has the name of the case.</summary>
    public void AddKeyTo(JsonTypeInfo info)
    {
        foreach (JsonCase<TUnion> found in cases)
        {
            if (found.Type != info.Type)
                continue;
            JsonPropertyInfo property = info.CreateJsonPropertyInfo(typeof(string), key);
            property.Get = _ => found.Name;
            // The name was read before the record, so there is nothing left to set.
            property.Set = static (_, _) => { };
            property.Order = int.MinValue;
            info.Properties.Add(property);
        }
    }

    // The reader is a copy, so the caller's reader stays at the start of the object.
    private JsonCase<TUnion> FindCase(Utf8JsonReader reader)
    {
        JsonCase<TUnion>? found = null;
        bool seen = false;
        while (reader.Read() && reader.TokenType == JsonTokenType.PropertyName)
        {
            bool isKey = reader.ValueTextEquals(keyUtf8);
            reader.Read();
            if (isKey)
            {
                if (seen)
                    throw new UnionJsonException($"The \"{key}\" key is given twice.");
                seen = true;
                if (reader.TokenType != JsonTokenType.String)
                    throw new UnionJsonException($"The \"{key}\" key must be a string, such as \"{cases[0].Name}\".");
                found = CaseNamed(ref reader);
            }
            reader.Skip();
        }
        return found ?? throw new UnionJsonException($"{(IsVowel(what[0]) ? "An" : "A")} {what} needs a \"{key}\" key. {KindsMessage}");
    }

    private JsonCase<TUnion> CaseNamed(ref Utf8JsonReader reader)
    {
        foreach (JsonCase<TUnion> found in cases)
            if (reader.ValueTextEquals(found.NameUtf8))
                return found;
        throw new UnionJsonException($"'{reader.GetString()}' is not a kind of {what}. {KindsMessage}");
    }

    private JsonTypeInfo InfoOf(JsonCase<TUnion> found, JsonSerializerOptions options)
    {
        JsonTypeInfo info = options.GetTypeInfo(found.Type);
        foreach (JsonPropertyInfo property in info.Properties)
            if (property.Name == key)
                return info;
        throw new InvalidOperationException($"The options give {found.Type.Name} no \"{key}\" member, so a {what} would lose its kind. Use the options of ScriptJson.");
    }

    private string KindsMessage => $"The kinds of {what} are {string.Join(", ", cases.SkipLast(1).Select(c => c.Name))} and {cases[^1].Name}.";

    private static bool IsVowel(char letter) => "aeiou".Contains(letter);
}

/// <summary>The JSON form of a <see cref="ScriptStep"/>: an object whose <c>do</c> key names the case. See <see cref="JsonCases{TUnion}"/>.</summary>
internal sealed class ScriptStepJsonConverter : JsonConverter<ScriptStep>
{
    internal static readonly JsonCases<ScriptStep> Cases = new("do", "step",
        JsonCase.Of("run", (RunStep step) => new ScriptStep(step)),
        JsonCase.Of("post", (PostStep step) => new ScriptStep(step)),
        JsonCase.Of("dump", (DumpStep step) => new ScriptStep(step)),
        JsonCase.Of("capture", (CaptureStep step) => new ScriptStep(step)),
        JsonCase.Of("camera", (CameraStep step) => new ScriptStep(step)));

    // System.Text.Json gives a null to the converter of a struct. It reads as the default step, which holds no case, and
    // Script.CheckSteps names its step number.
    public override ScriptStep Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options) =>
        reader.TokenType == JsonTokenType.Null ? default : Cases.Read(ref reader, typeToConvert, options);

    public override void Write(Utf8JsonWriter writer, ScriptStep value, JsonSerializerOptions options)
    {
        if (value.Value is { } held)
            Cases.Write(writer, held, options);
        else
            writer.WriteNullValue();
    }
}

/// <summary>The JSON form of a <see cref="MessageTarget"/>: an object whose <c>kind</c> key names the case. See <see cref="JsonCases{TUnion}"/>.</summary>
internal sealed class MessageTargetJsonConverter : JsonConverter<MessageTarget>
{
    internal static readonly JsonCases<MessageTarget> Cases = new("kind", "event target",
        JsonCase.Of("entity", (ToEntity target) => new MessageTarget(target)),
        JsonCase.Of("topic", (ToTopic target) => new MessageTarget(target)),
        JsonCase.Of("frame", (ToFrame target) => new MessageTarget(target)),
        JsonCase.Of("broadcast", (ToEveryone target) => new MessageTarget(target)));

    // System.Text.Json gives a null to the converter of a struct. A post needs a target, so a null is an error here.
    public override MessageTarget Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options) =>
        reader.TokenType == JsonTokenType.Null ? throw new UnionJsonException("An event target cannot be null.") : Cases.Read(ref reader, typeToConvert, options);

    public override void Write(Utf8JsonWriter writer, MessageTarget value, JsonSerializerOptions options)
    {
        if (value.Value is { } held)
            Cases.Write(writer, held, options);
        else
            writer.WriteNullValue();
    }
}
