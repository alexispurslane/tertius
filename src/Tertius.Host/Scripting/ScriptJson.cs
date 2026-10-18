using System.Numerics;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.Json.Serialization.Metadata;
using Tertius.Simulation;
using static Tertius.JsonVectors;

namespace Tertius.Host.Scripting;

/// <summary>
/// The System.Text.Json options of scripts and state dumps. They follow the conventions of the content formats design:
/// camelCase keys and enum values, public fields, no comments, and vectors and quaternions as arrays.
/// </summary>
/// <remarks>
/// Reading is strict: an unknown key, a duplicate key, a number in a string, an enum value as a number, a number out of
/// the range of its field, and a key for a payload member that a script cannot set are all errors. Writing a dump is
/// lenient, because a dump of a broken state must still write: a number that JSON cannot hold, such as NaN, becomes a
/// string, and an enum value with no name becomes its number. Every options object is new, never
/// <see cref="JsonSerializerOptions.Default"/>, whose caches live as long as the process (content formats design,
/// decision 18).
/// </remarks>
internal static class ScriptJson
{
    /// <summary>The options that read a script. They know no scene, so they cannot read an entity.</summary>
    public static readonly JsonSerializerOptions Scripts = Reading(world: null, scene: null);

    /// <summary>The options that read the payloads of a run's posts, with the scene's ids.</summary>
    public static JsonSerializerOptions Payloads(World world, SceneInstance scene) => Reading(world, scene);

    /// <summary>The options that write a run's dumps, with the scene's ids.</summary>
    public static JsonSerializerOptions Dumps(SceneInstance scene)
    {
        JsonSerializerOptions options = Common(JsonNumberHandling.AllowNamedFloatingPointLiterals, new DefaultJsonTypeInfoResolver());
        options.Converters.Add(new JsonStringEnumConverter(JsonNamingPolicy.CamelCase, allowIntegerValues: true));
        options.Converters.Add(new EntityConverter(scene));
        options.Converters.Add(new FrameHandleConverter(world: null, scene));
        return options;
    }

    /// <summary>
    /// The options that write a recorded script: the same conventions as a script that a person writes, with each step
    /// on one line. A recording can hold tens of thousands of steps, and a line for each step keeps it easy to search,
    /// to cut and to compare. Writing is as lenient as for a dump, so a number that JSON cannot hold, such as NaN, becomes
    /// a string, and the script then fails to parse at that step instead of the recording failing to write.
    /// </summary>
    public static readonly JsonSerializerOptions Recordings = Recording(indented: true);

    private static JsonSerializerOptions Recording(bool indented)
    {
        JsonSerializerOptions options = Common(JsonNumberHandling.AllowNamedFloatingPointLiterals, new DefaultJsonTypeInfoResolver());
        options.Converters.Add(new JsonStringEnumConverter(JsonNamingPolicy.CamelCase, allowIntegerValues: false));
        options.WriteIndented = indented;
        if (indented)
            options.Converters.Add(new OneLinePerStepConverter(Recording(indented: false)));
        return options;
    }

    /// <summary>
    /// Writes the steps of a script as an array with each step on one line of compact JSON. The converter is for the list
    /// and not for the step, because System.Text.Json cannot write the discriminator of a polymorphic type through a
    /// custom converter of that type. Writing only.
    /// </summary>
    private sealed class OneLinePerStepConverter(JsonSerializerOptions lineOptions) : JsonConverter<IReadOnlyList<ScriptStep>>
    {
        public override IReadOnlyList<ScriptStep> Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options) =>
            throw new NotSupportedException("The recording options only write.");

        public override void Write(Utf8JsonWriter writer, IReadOnlyList<ScriptStep> value, JsonSerializerOptions options)
        {
            writer.WriteStartArray();
            // The writer adds no new line or indentation before a raw value, so each line starts with its own. The writer
            // adds the comma before it, and the new line and the indentation of the closing bracket.
            string indentation = "\n" + new string(options.IndentCharacter, options.IndentSize * writer.CurrentDepth);
            foreach (ScriptStep step in value)
                // The declared type ScriptStep makes the line options write the step's "do" key.
                writer.WriteRawValue(indentation + JsonSerializer.Serialize(step, typeof(ScriptStep), lineOptions), skipInputValidation: true);
            writer.WriteEndArray();
        }
    }

    private static JsonSerializerOptions Reading(World? world, SceneInstance? scene)
    {
        JsonSerializerOptions options = Common(JsonNumberHandling.Strict, new DefaultJsonTypeInfoResolver { Modifiers = { LeaveOutReadOnlyPayloadMembers } });
        options.Converters.Add(new JsonStringEnumConverter(JsonNamingPolicy.CamelCase, allowIntegerValues: false));
        options.Converters.Add(new FiniteSingleConverter());
        options.Converters.Add(new FiniteDoubleConverter());
        if (world != null && scene != null)
        {
            options.Converters.Add(new EntityConverter(scene, world));
            options.Converters.Add(new FrameHandleConverter(world, scene));
        }
        return options;
    }

    private static JsonSerializerOptions Common(JsonNumberHandling numbers, DefaultJsonTypeInfoResolver resolver)
    {
        var options = new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
            AllowDuplicateProperties = false,
            NumberHandling = numbers,
            IncludeFields = true,
            AllowOutOfOrderMetadataProperties = true,
            RespectNullableAnnotations = true,
            RespectRequiredConstructorParameters = true,
            WriteIndented = true,
            NewLine = "\n",
            Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
            TypeInfoResolver = resolver,
        };
        options.Converters.Add(new Vector2Converter());
        options.Converters.Add(new Vector3Converter());
        options.Converters.Add(new Double3Converter());
        options.Converters.Add(new QuaternionConverter());
        options.Converters.Add(new DQuaternionConverter());
        options.Converters.Add(new TopicConverter());
        return options;
    }

    /// <summary>
    /// Leaves the read-only members of a payload type out of its contract, so that a key that names one is an unknown
    /// key. Without it, System.Text.Json reads the key and drops the value without an error.
    /// </summary>
    private static void LeaveOutReadOnlyPayloadMembers(JsonTypeInfo info)
    {
        if (info.Kind != JsonTypeInfoKind.Object || !typeof(IEventPayload).IsAssignableFrom(info.Type))
            return;
        for (int i = info.Properties.Count - 1; i >= 0; i--)
            if (info.Properties[i].Set is null)
                info.Properties.RemoveAt(i);
    }

    // A vector reads and writes with the methods of JsonVectors, which the converter of InputEvent calls too, so that a
    // vector has one form in every script and dump. It reads from an array with its exact count of numbers: StartArray,
    // then Next once per number, then EndArray. C# evaluates arguments left to right, so
    // new Vector3(Next(..), Next(..), Next(..)) reads x, y, z in order.

    /// <summary>Reads a float, and refuses a number that rounds to infinity, such as 1e39. Reading only.</summary>
    private sealed class FiniteSingleConverter : JsonConverter<float>
    {
        public override float Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options) =>
            reader.TokenType == JsonTokenType.Number ? Finite(reader.GetSingle()) : throw new JsonException("Expected a number.");

        public override void Write(Utf8JsonWriter writer, float value, JsonSerializerOptions options) => writer.WriteNumberValue(value);
    }

    /// <summary>Reads a double, and refuses a number that rounds to infinity, such as 1e309. Reading only.</summary>
    private sealed class FiniteDoubleConverter : JsonConverter<double>
    {
        public override double Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options) =>
            reader.TokenType == JsonTokenType.Number ? Finite(reader.GetDouble()) : throw new JsonException("Expected a number.");

        public override void Write(Utf8JsonWriter writer, double value, JsonSerializerOptions options) => writer.WriteNumberValue(value);
    }

    private sealed class Vector2Converter : JsonConverter<Vector2>
    {
        private const string Form = "a Vector2, an array of 2 numbers";

        public override Vector2 Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options) => ReadVector2(ref reader, Form);

        public override void Write(Utf8JsonWriter writer, Vector2 value, JsonSerializerOptions options) => WriteVector2(writer, value);
    }

    private sealed class Vector3Converter : JsonConverter<Vector3>
    {
        private const string Form = "a Vector3, an array of 3 numbers";

        public override Vector3 Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        {
            StartArray(ref reader, Form);
            var value = new Vector3(NextSingle(ref reader, Form), NextSingle(ref reader, Form), NextSingle(ref reader, Form));
            EndArray(ref reader, Form);
            return value;
        }

        public override void Write(Utf8JsonWriter writer, Vector3 value, JsonSerializerOptions options) =>
            WriteArray(writer, Number(value.X), Number(value.Y), Number(value.Z));
    }

    private sealed class Double3Converter : JsonConverter<Double3>
    {
        private const string Form = "a Double3, an array of 3 numbers";

        public override Double3 Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        {
            StartArray(ref reader, Form);
            var value = new Double3(NextDouble(ref reader, Form), NextDouble(ref reader, Form), NextDouble(ref reader, Form));
            EndArray(ref reader, Form);
            return value;
        }

        public override void Write(Utf8JsonWriter writer, Double3 value, JsonSerializerOptions options) =>
            WriteArray(writer, Number(value.X), Number(value.Y), Number(value.Z));
    }

    private sealed class QuaternionConverter : JsonConverter<Quaternion>
    {
        private const string Form = "a quaternion, an array of 4 numbers x, y, z, w";

        public override Quaternion Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        {
            StartArray(ref reader, Form);
            var value = new Quaternion(NextSingle(ref reader, Form), NextSingle(ref reader, Form), NextSingle(ref reader, Form), NextSingle(ref reader, Form));
            EndArray(ref reader, Form);
            return value;
        }

        public override void Write(Utf8JsonWriter writer, Quaternion value, JsonSerializerOptions options) =>
            WriteArray(writer, Number(value.X), Number(value.Y), Number(value.Z), Number(value.W));
    }

    private sealed class DQuaternionConverter : JsonConverter<DQuaternion>
    {
        private const string Form = "a quaternion, an array of 4 numbers x, y, z, w";

        public override DQuaternion Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        {
            StartArray(ref reader, Form);
            var value = new DQuaternion(NextDouble(ref reader, Form), NextDouble(ref reader, Form), NextDouble(ref reader, Form), NextDouble(ref reader, Form));
            EndArray(ref reader, Form);
            return value;
        }

        public override void Write(Utf8JsonWriter writer, DQuaternion value, JsonSerializerOptions options) =>
            WriteArray(writer, Number(value.X), Number(value.Y), Number(value.Z), Number(value.W));
    }

    /// <summary>A topic is its name, such as <c>"input"</c>.</summary>
    private sealed class TopicConverter : JsonConverter<Topic>
    {
        public override Topic Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options) =>
            Topics.Get(reader.GetString() ?? throw new JsonException("A topic is a topic name."));

        public override void Write(Utf8JsonWriter writer, Topic value, JsonSerializerOptions options) =>
            writer.WriteStringValue(Topics.NameOf(value));
    }

    /// <summary>
    /// An entity is its scene id, or <c>#index.generation</c> for an entity without one. Reading needs the world, and
    /// takes only the id of a live entity.
    /// </summary>
    private sealed class EntityConverter(SceneInstance scene, World? world = null) : JsonConverter<Entity>
    {
        public override Entity Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options) =>
            LiveEntity(reader.GetString() ?? throw new JsonException("An entity is a scene id."), scene, world ?? throw WriteOnly());

        public override void Write(Utf8JsonWriter writer, Entity value, JsonSerializerOptions options)
        {
            if (value.IsNull)
                writer.WriteNullValue();
            else
                writer.WriteStringValue(scene.IdOf(value));
        }
    }

    /// <summary>A frame is the scene id of the entity that owns it. Reading needs the world, and takes only a live owner.</summary>
    private sealed class FrameHandleConverter(World? world, SceneInstance scene) : JsonConverter<FrameHandle>
    {
        public override FrameHandle Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        {
            string id = reader.GetString() ?? throw new JsonException("A frame is the scene id of the entity that owns it.");
            World live = world ?? throw WriteOnly();
            FrameHandle frame = live.GetOwnedFrame(LiveEntity(id, scene, live));
            return frame.IsNull ? throw new JsonException($"The entity '{id}' owns no frame.") : frame;
        }

        public override void Write(Utf8JsonWriter writer, FrameHandle value, JsonSerializerOptions options)
        {
            if (value.IsNull)
                writer.WriteNullValue();
            else
                writer.WriteStringValue(scene.IdOf(value.Entity));
        }
    }

    private static InvalidOperationException WriteOnly() => new("The dump options only write. Reading an entity needs the world.");

    /// <summary>The live entity with the scene id. Throws <see cref="JsonException"/> for an unknown id or an entity that is gone.</summary>
    private static Entity LiveEntity(string id, SceneInstance scene, World world)
    {
        if (!scene.TryGetEntity(id, out Entity entity))
            throw new JsonException($"No entity has the scene id '{id}'.");
        return world.IsAlive(entity) ? entity : throw new JsonException($"The entity '{id}' is not alive.");
    }
}
