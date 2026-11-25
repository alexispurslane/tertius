using System.Text.Json.Nodes;
using StbImageWriteSharp;

namespace Tertius.Assets.Tests;

/// <summary>
/// Builds a small glTF binary in memory: a JSON chunk and one binary buffer, as the asset converter writes them. A test
/// adds meshes, materials, textures and nodes, and may edit <see cref="Json"/> before <see cref="Build"/> to break it.
/// </summary>
internal sealed class GlbBuilder
{
    public static readonly float[] TrianglePositions = [0, 0, 0, 1, 0, 0, 0, 1, 0];
    public static readonly float[] TriangleNormals = [0, 0, 1, 0, 0, 1, 0, 0, 1];
    public static readonly float[] TriangleTangents = [1, 0, 0, 1, 1, 0, 0, 1, 1, 0, 0, -1];
    public static readonly float[] TriangleUvs = [0, 0, 1, 0, 0.25f, 0.75f];

    private readonly List<byte> bin = [];

    public JsonObject Json { get; } =
        new()
        {
            ["asset"] = new JsonObject { ["version"] = "2.0" },
            ["scene"] = 0,
            ["scenes"] = new JsonArray(new JsonObject { ["nodes"] = new JsonArray() }),
            ["nodes"] = new JsonArray(),
            ["meshes"] = new JsonArray(),
            ["accessors"] = new JsonArray(),
            ["bufferViews"] = new JsonArray(),
            ["buffers"] = new JsonArray(),
            ["materials"] = new JsonArray(),
            ["textures"] = new JsonArray(),
            ["images"] = new JsonArray(),
        };

    /// <summary>The PNG file of RGBA8 pixels, top row first.</summary>
    public static byte[] Png(byte[] rgba, int width, int height)
    {
        using var stream = new MemoryStream();
        new ImageWriter().WritePng(
            data: rgba,
            width: width,
            height: height,
            components: ColorComponents.RedGreenBlueAlpha,
            dest: stream
        );
        return stream.ToArray();
    }

    /// <summary>A triangle in the XY plane that faces +Z, with tangents along +X and UVs, of material <paramref name="material"/>.</summary>
    public JsonObject Triangle(int? material = null) =>
        Primitive(
            positions: TrianglePositions,
            normals: TriangleNormals,
            tangents: TriangleTangents,
            uvs: TriangleUvs,
            indices: [0, 1, 2],
            material: material
        );

    public JsonObject Primitive(
        float[] positions,
        float[]? normals,
        float[]? tangents,
        float[]? uvs,
        uint[] indices,
        int? material = null
    )
    {
        var attributes = new JsonObject { ["POSITION"] = Floats(positions, "VEC3") };
        if (normals is not null)
            attributes["NORMAL"] = Floats(normals, "VEC3");
        if (tangents is not null)
            attributes["TANGENT"] = Floats(tangents, "VEC4");
        if (uvs is not null)
            attributes["TEXCOORD_0"] = Floats(uvs, "VEC2");
        var primitive = new JsonObject { ["attributes"] = attributes, ["indices"] = Indices(indices) };
        if (material is int index)
            primitive["material"] = index;
        return primitive;
    }

    public int Mesh(params JsonObject[] primitives) =>
        Add("meshes", new JsonObject { ["primitives"] = new JsonArray(primitives) });

    public int Material(JsonObject material) => Add("materials", material);

    public int Texture(byte[] png)
    {
        int view = View(png);
        int image = Add("images", new JsonObject { ["bufferView"] = view, ["mimeType"] = "image/png" });
        return Add("textures", new JsonObject { ["source"] = image });
    }

    /// <summary>Adds a node that is no scene root, such as a child, and returns its index.</summary>
    public int Node(JsonObject node, int? mesh = null)
    {
        if (mesh is int index)
            node["mesh"] = index;
        return Add("nodes", node);
    }

    /// <summary>Adds a node and lists it as a root of the scene.</summary>
    public int Root(JsonObject node, int? mesh = null)
    {
        int index = Node(node, mesh);
        Json["scenes"]![0]!["nodes"]!.AsArray().Add(index);
        return index;
    }

    public byte[] Build()
    {
        while (bin.Count % 4 != 0)
            bin.Add(0);
        Json["buffers"] = new JsonArray(new JsonObject { ["byteLength"] = bin.Count });
        byte[] json = System.Text.Encoding.UTF8.GetBytes(Json.ToJsonString());
        int jsonLength = (json.Length + 3) / 4 * 4;
        using var stream = new MemoryStream();
        using var writer = new BinaryWriter(stream);
        writer.Write(0x46546C67u);
        writer.Write(2u);
        writer.Write((uint)(12 + 8 + jsonLength + 8 + bin.Count));
        writer.Write((uint)jsonLength);
        writer.Write(0x4E4F534Au);
        writer.Write(json);
        for (int i = json.Length; i < jsonLength; i++)
            writer.Write((byte)' ');
        writer.Write((uint)bin.Count);
        writer.Write(0x004E4942u);
        writer.Write(bin.ToArray());
        writer.Flush();
        return stream.ToArray();
    }

    public int Bytes(byte[] values, string type, int count) =>
        Add(
            "accessors",
            new JsonObject
            {
                ["bufferView"] = View(values),
                ["componentType"] = 5121,
                ["count"] = count,
                ["type"] = type,
            }
        );

    public int Floats(float[] values, string type)
    {
        int view = View([.. values.SelectMany(BitConverter.GetBytes)]);
        int components = type switch
        {
            "VEC2" => 2,
            "VEC3" => 3,
            "MAT4" => 16,
            _ => 4,
        };
        return Add(
            "accessors",
            new JsonObject
            {
                ["bufferView"] = view,
                ["componentType"] = 5126,
                ["count"] = values.Length / components,
                ["type"] = type,
            }
        );
    }

    private int Indices(uint[] values)
    {
        int view = View([.. values.SelectMany(BitConverter.GetBytes)]);
        return Add(
            "accessors",
            new JsonObject
            {
                ["bufferView"] = view,
                ["componentType"] = 5125,
                ["count"] = values.Length,
                ["type"] = "SCALAR",
            }
        );
    }

    private int View(byte[] bytes)
    {
        while (bin.Count % 4 != 0)
            bin.Add(0);
        int offset = bin.Count;
        bin.AddRange(bytes);
        return Add(
            "bufferViews",
            new JsonObject
            {
                ["buffer"] = 0,
                ["byteOffset"] = offset,
                ["byteLength"] = bytes.Length,
            }
        );
    }

    private int Add(string list, JsonObject item)
    {
        JsonArray array = Json[list]!.AsArray();
        array.Add(item);
        return array.Count - 1;
    }
}
