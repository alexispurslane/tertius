using Tertius.Rendering;
using StbImageWriteSharp;
using System.Text;

namespace Tertius.ReflectionReference;

internal readonly record struct ReferenceCrop(int Left, int Top, int Width, int Height);

internal sealed record ReferenceImage(int Width, int Height, Double3[] Pixels)
{
    public static ReferenceImage[] RenderLevels(
        ReferenceCamera camera, ReferenceCrop crop, ReferenceEmitter emitter, MaterialSample material, int[] levels, uint seed
    )
    {
        if (levels.Length == 0 || levels[0] <= 0 || !levels.SequenceEqual(levels.Distinct().Order()))
            throw new ArgumentException("Sample levels must be positive and strictly increasing.", nameof(levels));
        if (crop.Width <= 0 || crop.Height <= 0 || crop.Left < 0 || crop.Top < 0 || crop.Left + crop.Width > camera.Width || crop.Top + crop.Height > camera.Height)
            throw new ArgumentOutOfRangeException(nameof(crop));
        ReferenceImage[] images = levels.Select(_ => new ReferenceImage(crop.Width, crop.Height, new Double3[crop.Width * crop.Height])).ToArray();
        ReferenceSample[] samples = ReferenceSample.Sequence(levels[^1]);
        var integral = new ReferenceIntegral(emitter, material);
        for (int pixel = 0; pixel < crop.Width * crop.Height; pixel++)
        {
            ReferenceSample shift = ReferenceSample.Shift(seed ^ ((uint)pixel * 0x9e3779b9));
            Double3 sum = default;
            int level = 0;
            for (int index = 0; index < samples.Length; index++)
            {
                ReferenceSample sample = samples[index].Rotated(shift);
                Double3 ray = camera.Ray(crop.Left + pixel % crop.Width + sample.PixelX, crop.Top + pixel / crop.Width + sample.PixelY);
                if (camera.PrimaryHit(ray, emitter) is { Surface: ReferenceSurface.Plate, Hit: var receiver })
                    sum += integral.Sample(point: receiver.Position, toView: -ray, sample: sample).Specular;
                if (index + 1 == levels[level])
                {
                    images[level].Pixels[pixel] = sum / levels[level];
                    level++;
                }
            }
        }
        return images;
    }

    public void WritePfm(Stream destination)
    {
        using var writer = new BinaryWriter(destination, Encoding.ASCII, leaveOpen: true);
        writer.Write(Encoding.ASCII.GetBytes($"PF\n{Width} {Height}\n-1.0\n"));
        for (int row = Height - 1; row >= 0; row--)
            for (int column = 0; column < Width; column++)
            {
                Double3 color = Pixels[row * Width + column];
                writer.Write((float)color.X);
                writer.Write((float)color.Y);
                writer.Write((float)color.Z);
            }
    }

    public void WritePreview(string path, double exposure)
    {
        var bytes = new byte[Pixels.Length * 3];
        for (int pixel = 0; pixel < Pixels.Length; pixel++)
        {
            bytes[pixel * 3] = Display(Pixels[pixel].X, exposure);
            bytes[pixel * 3 + 1] = Display(Pixels[pixel].Y, exposure);
            bytes[pixel * 3 + 2] = Display(Pixels[pixel].Z, exposure);
        }
        using FileStream stream = File.Create(path);
        new ImageWriter().WritePng(data: bytes, width: Width, height: Height, components: ColorComponents.RedGreenBlue, dest: stream);
    }

    public static double Luminance(Double3 color) => 0.2126 * color.X + 0.7152 * color.Y + 0.0722 * color.Z;

    private static byte Display(double radiance, double exposure)
    {
        double exposed = Math.Max(0, radiance) * Math.Pow(2, exposure);
        double mapped = exposed / (1 + exposed);
        double srgb = mapped <= 0.0031308 ? 12.92 * mapped : 1.055 * Math.Pow(mapped, 1 / 2.4) - 0.055;
        return (byte)Math.Clamp(Math.Round(srgb * 255), 0, 255);
    }
}

internal readonly record struct ReferenceDifference(double MissingFraction, double ExcessFraction, double RelativeL1)
{
    public static ReferenceDifference Compare(ReferenceImage reference, ReferenceImage candidate)
    {
        if (reference.Width != candidate.Width || reference.Height != candidate.Height)
            throw new ArgumentException("The two images must cover the same pixels.", nameof(candidate));
        RadianceTotals totals = MetricMath.Measure(
            [.. reference.Pixels.Select(ReferenceImage.Luminance)],
            [.. candidate.Pixels.Select(ReferenceImage.Luminance)],
            Enumerable.Repeat(1.0, reference.Pixels.Length).ToArray()
        );
        if (totals.ReferenceSum <= 0)
            throw new ArgumentException("The reference region must contain positive light.", nameof(reference));
        return new(
            totals.Missing / totals.ReferenceSum,
            totals.Excess / totals.ReferenceSum,
            (totals.Missing + totals.Excess) / totals.ReferenceSum
        );
    }
}
