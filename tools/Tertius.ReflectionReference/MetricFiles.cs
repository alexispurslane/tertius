using System.Globalization;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace Tertius.ReflectionReference;

internal static class MetricFiles
{
    public static ReferenceImage ReadPfm(string path, ReferenceCrop crop)
    {
        using var reader = new BinaryReader(File.OpenRead(path), Encoding.ASCII);
        string Line()
        {
            var text = new StringBuilder();
            byte value;
            while ((value = reader.ReadByte()) != 10)
                text.Append((char)value);
            return text.ToString();
        }
        if (Line() != "PF" || Line() != FormattableString.Invariant($"{crop.Width} {crop.Height}") || Line() != "-1.0")
            throw new InvalidDataException("Metrics require original-crop little-endian RGB PFM values.");
        if (reader.BaseStream.Length - reader.BaseStream.Position != crop.Width * crop.Height * 12)
            throw new InvalidDataException("Metrics PFM length does not match the retained crop.");
        var pixels = new Double3[crop.Width * crop.Height];
        for (int row = crop.Height - 1; row >= 0; row--)
            for (int column = 0; column < crop.Width; column++)
            {
                float red = reader.ReadSingle();
                float green = reader.ReadSingle();
                float blue = reader.ReadSingle();
                if (!float.IsFinite(red) || red < 0 || red != green || red != blue)
                    throw new InvalidDataException($"Expected nonnegative achromatic radiance at ({crop.Left + column},{crop.Top + row}) in {path}.");
                pixels[row * crop.Width + column] = new(red, green, blue);
            }
        return new(crop.Width, crop.Height, pixels);
    }

    public static double[] Scalars(ReferenceImage image) => [.. image.Pixels.Select(ReferenceImage.Luminance)];

    public static double[][] Frames(string path, ReferenceCrop crop)
    {
        byte[] bytes = File.ReadAllBytes(path);
        int count = crop.Width * crop.Height;
        ArgumentOutOfRangeException.ThrowIfNotEqual(bytes.Length, count * 32 * sizeof(float));
        using var reader = new BinaryReader(new MemoryStream(bytes));
        var frames = new double[32][];
        for (int frame = 0; frame < frames.Length; frame++)
        {
            frames[frame] = new double[count];
            for (int pixel = 0; pixel < count; pixel++)
            {
                float value = reader.ReadSingle();
                frames[frame][pixel] = ReferenceImage.Luminance(new(value, value, value));
            }
            MetricMath.Validate(frames[frame]);
        }
        return frames;
    }

    public static string Hash(string path)
    {
        using FileStream stream = File.OpenRead(path);
        return Convert.ToHexStringLower(SHA256.HashData(stream));
    }
}

internal sealed class MetricTable : IDisposable
{
    private static readonly char[] CsvSpecial = [',', '"', '\n', '\r'];
    private readonly string path;
    private readonly GZipStream compressed;
    private readonly IncrementalHash plainHash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
    private long plainBytes;

    public MetricTable(string path)
    {
        this.path = path;
        compressed = new(File.Create(path), CompressionLevel.SmallestSize);
    }

    public void Write(params object?[] fields)
    {
        string Format(object? value)
        {
            string text = value switch
            {
                null => "",
                double number => number.ToString("R", CultureInfo.InvariantCulture),
                float number => number.ToString("R", CultureInfo.InvariantCulture),
                IFormattable formatted => formatted.ToString(null, CultureInfo.InvariantCulture),
                _ => value.ToString() ?? "",
            };
            if (text.IndexOfAny(CsvSpecial) >= 0)
                return "\"" + text.Replace("\"", "\"\"") + "\"";
            return text;
        }
        byte[] bytes = Encoding.UTF8.GetBytes(string.Join(',', fields.Select(Format)) + "\n");
        plainHash.AppendData(bytes);
        plainBytes += bytes.Length;
        compressed.Write(bytes);
    }

    public void Dispose()
    {
        compressed.Dispose();
        string hash = Convert.ToHexStringLower(plainHash.GetHashAndReset());
        plainHash.Dispose();
        File.WriteAllText(path + ".plain.json", JsonSerializer.Serialize(new
        {
            Format = "UTF-8 CSV, LF newline; lossless gzip of ONLY the new metric table",
            UncompressedBytes = plainBytes, UncompressedSha256 = hash,
        }));
    }
}
