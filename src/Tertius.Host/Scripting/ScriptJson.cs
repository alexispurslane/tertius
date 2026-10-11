using System.Globalization;
using System.Numerics;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.Json.Serialization.Metadata;
using Tertius.Simulation;

namespace Tertius.Host.Scripting;

/// <summary>
/// The System.Text.Json options of scripts and state dumps. They follow the conventions of the content formats design:
/// camelCase keys and enum values, public fields, no comments, and an unknown key, a duplicate key or a number in a
/// string is an error. Vectors are arrays, and a dump writes each one on one line.
/// </summary>
/// <remarks>
/// Every options object is new, never <see cref="JsonSerializerOptions.Default"/>, whose caches live as long as the
/// process (content formats design, decision 18).
/// </remarks>
internal static class ScriptJson
{
    /// <summary>The options that read a script. They know no scene, so they cannot read an entity.</summary>
    public static readonly JsonSerializerOptions Scripts = Create(world: null, scene: null);

    /// <summary>The options of one run: they read the payloads of its posts and write its dumps, with the scene's ids.</summary>
    public static JsonSerializerOptions ForRun(World world, SceneInstance scene) => Create(world, scene);

    private static JsonSerializerOptions Create(World? world, SceneInstance? scene)
    {
        var options = new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
            AllowDuplicateProperties = false,
            NumberHandling = JsonNumberHandling.Strict,
            IncludeFields = true,
            AllowOutOfOrderMetadataProperties = true,
            RespectNullableAnnotations = true,
            RespectRequiredConstructorParameters = true,
            WriteIndented = true,
            NewLine = "\n",
            Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
            TypeInfoResolver = new DefaultJsonTypeInfoResolver(),
        };
        options.Converters.Add(new JsonStringEnumConverter(JsonNamingPolicy.CamelCase, allowIntegerValues: false));
        options.Converters.Add(new Vector2Converter());
        options.Converters.Add(new Vector3Converter());
        options.Converters.Add(new Double3Converter());
        options.Converters.Add(new QuaternionConverter());
        options.Converters.Add(new DQuaternionConverter());
        options.Converters.Add(new TopicConverter());
        if (world != null && scene != null)
        {
            options.Converters.Add(new EntityConverter(scene));
            options.Converters.Add(new FrameHandleConverter(world, scene));
        }
        return options;
    }

    // A vector reads from an array with its exact count of numbers: StartArray, then Next once per number, then EndArray.
    // C# evaluates arguments left to right, so new Vector3(Next(..), Next(..), Next(..)) reads x, y, z in order.

    private static void StartArray(ref Utf8JsonReader reader, string form)
    {
        if (reader.TokenType != JsonTokenType.StartArray)
            throw new JsonException($"Expected {form}.");
    }

    private static float NextSingle(ref Utf8JsonReader reader, string form)
    {
        if (!reader.Read() || reader.TokenType != JsonTokenType.Number)
            throw new JsonException($"Expected {form}.");
        return reader.GetSingle();
    }

    private static double NextDouble(ref Utf8JsonReader reader, string form)
    {
        if (!reader.Read() || reader.TokenType != JsonTokenType.Number)
            throw new JsonException($"Expected {form}.");
        return reader.GetDouble();
    }

    private static void EndArray(ref Utf8JsonReader reader, string form)
    {
        if (!reader.Read() || reader.TokenType != JsonTokenType.EndArray)
            throw new JsonException($"Expected {form}.");
    }

    /// <summary>
    /// Writes the numbers as one array on one line. Each number is the shortest text that reads back to the same value.
    /// A value that JSON cannot hold, such as NaN, becomes a string, so that a dump of a broken state still writes.
    /// </summary>
    private static void WriteArray(Utf8JsonWriter writer, params ReadOnlySpan<string> numbers) =>
        writer.WriteRawValue($"[{string.Join(", ", numbers.ToArray())}]", skipInputValidation: true);

    private static string Number(float value) =>
        float.IsFinite(value) ? value.ToString("R", CultureInfo.InvariantCulture) : $"\"{value.ToString(CultureInfo.InvariantCulture)}\"";

    private static string Number(double value) =>
        double.IsFinite(value) ? value.ToString("R", CultureInfo.InvariantCulture) : $"\"{value.ToString(CultureInfo.InvariantCulture)}\"";

    private sealed class Vector2Converter : JsonConverter<Vector2>
    {
        private const string Form = "a Vector2, an array of 2 numbers";

        public override Vector2 Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        {
            StartArray(ref reader, Form);
            var value = new Vector2(NextSingle(ref reader, Form), NextSingle(ref reader, Form));
            EndArray(ref reader, Form);
            return value;
        }

        public override void Write(Utf8JsonWriter writer, Vector2 value, JsonSerializerOptions options) =>
            WriteArray(writer, Number(value.X), Number(value.Y));
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

    /// <summary>An entity is its scene id, or <c>#index.generation</c> for an entity without one.</summary>
    private sealed class EntityConverter(SceneInstance scene) : JsonConverter<Entity>
    {
        public override Entity Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        {
            string id = reader.GetString() ?? throw new JsonException("An entity is a scene id.");
            return scene.TryGetEntity(id, out Entity entity) ? entity : throw new JsonException($"No entity has the scene id '{id}'.");
        }

        public override void Write(Utf8JsonWriter writer, Entity value, JsonSerializerOptions options)
        {
            if (value.IsNull)
                writer.WriteNullValue();
            else
                writer.WriteStringValue(scene.IdOf(value));
        }
    }

    /// <summary>A frame is the scene id of the entity that owns it.</summary>
    private sealed class FrameHandleConverter(World world, SceneInstance scene) : JsonConverter<FrameHandle>
    {
        public override FrameHandle Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        {
            string id = reader.GetString() ?? throw new JsonException("A frame is the scene id of the entity that owns it.");
            if (!scene.TryGetEntity(id, out Entity entity))
                throw new JsonException($"No entity has the scene id '{id}'.");
            FrameHandle frame = world.GetOwnedFrame(entity);
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
}
