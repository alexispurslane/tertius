using System.IO.Compression;
using System.Numerics;
using StbImageSharp;
using Tertius.Assets;

namespace Tertius.Demo;

/// <summary>The satellite mosaic and topographic normals that the Blue Danube setup cooks for the rotating globe.</summary>
internal static class EarthMaterial
{
    internal static string PathIn(ContentFolders content) =>
        Path.Combine(content.Root, "tertius.demo/bin/assets/blue-danube/earth/material.zip");

    internal static MeshData Globe(float radius, bool mapped)
    {
        MeshData globe = Primitives.Sphere(radius, 256, 128);
        if (mapped)
        {
            // earth.py reflects 1/64 of the image height beyond each pole for the repeating sampler.
            foreach (ref Vertex vertex in globe.Vertices.AsSpan())
                vertex.TexCoord.Y = (1 + 64 * vertex.TexCoord.Y) / 66;
        }

        return globe;
    }

    internal static MaterialId? Load(ContentFolders? content, IRenderResources resources)
    {
        if (content is null || !File.Exists(PathIn(content)))
            return null;

        using ZipArchive archive = ZipFile.OpenRead(PathIn(content));
        TextureId Map(string name, bool srgb)
        {
            ZipArchiveEntry entry = archive.GetEntry(name) ?? throw new InvalidDataException(name);
            byte[] png = new byte[entry.Length];
            using Stream stream = entry.Open();
            stream.ReadExactly(png);
            ImageResult image = ImageResult.FromMemory(png, ColorComponents.RedGreenBlueAlpha);
            return resources.CreateTexture(new(Width: image.Width, Height: image.Height, Rgba: image.Data, Srgb: srgb));
        }

        // The ocean covers most of the globe. Its refractive index of 1.33 gives F0 ≈ 0.020.
        return resources.CreateMaterial(
            new(
                BaseColor: Vector4.One,
                Roughness: 1,
                Reflectance: 0.355f,
                BaseColorMap: Map("color.png", srgb: true),
                NormalMap: Map("normal.png", srgb: false),
                MetallicRoughnessMap: Map("roughness.png", srgb: false)
            )
        );
    }
}
