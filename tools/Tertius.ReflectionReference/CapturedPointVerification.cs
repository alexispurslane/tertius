using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;

namespace Tertius.ReflectionReference;

internal readonly record struct PointVerification(ReferenceQuadratureResult Coarse, ReferenceQuadratureResult Fine, double SavedMean, double Peak)
{
    public double CoarseFineDrift => Math.Abs(Coarse.Light.Specular.X - Fine.Light.Specular.X);
    public double SignedDifference => SavedMean - Fine.Light.Specular.X;
    public double AbsoluteDifference => Math.Abs(SignedDifference);
    public double? FractionalDifference => Fine.Light.Specular.X > 0 ? SignedDifference / Fine.Light.Specular.X : null;
    public double PeakNormalizedDifference => AbsoluteDifference / Peak;
    public bool DiagnosticCriteriaSatisfied => Coarse.Converged && Fine.Converged
        && CoarseFineDrift / Peak < 1e-4 && PeakNormalizedDifference <= 0.01;
}

internal static partial class ReferenceComparison
{
    public static ComparisonSource ReadSource(string path)
    {
        using JsonDocument metadata = JsonDocument.Parse(File.ReadAllText(Path.Combine(path, "manifest.json")));
        return new(
            Inputs: MemoryMarshal.Cast<byte, float>(File.ReadAllBytes(Path.Combine(path, "decoded-inputs.f32"))).ToArray(),
            ExposureStops: metadata.RootElement.GetProperty("ExposureStops").GetDouble(),
            PreExposureScale: metadata.RootElement.GetProperty("PreExposureScale").GetDouble(),
            EmissionRadiance: metadata.RootElement.GetProperty("EmissionRadiance").GetDouble()
        );
    }

    public static DecodedReceiver PlateReceiver(ComparisonSource source, byte[] surfaces, ReferenceCrop crop, Double3 eye, int pixelX, int pixelY)
    {
        ArgumentOutOfRangeException.ThrowIfNotEqual(source.Inputs.Length, crop.Width * crop.Height * 16);
        ArgumentOutOfRangeException.ThrowIfNotEqual(surfaces.Length, crop.Width * crop.Height);
        if (pixelX < crop.Left || pixelX >= crop.Left + crop.Width || pixelY < crop.Top || pixelY >= crop.Top + crop.Height)
            throw new ArgumentOutOfRangeException(nameof(pixelX), "The fixed point must lie in the original crop.");
        int pixel = (pixelY - crop.Top) * crop.Width + pixelX - crop.Left;
        ComparisonPixel decoded = Decode(source.Inputs[(pixel * 16)..(pixel * 16 + 16)], eye, Math.Log2(source.PreExposureScale));
        if (surfaces[pixel] != (byte)ReferenceSurface.Plate || decoded.Emission != 0)
            throw new InvalidOperationException($"Selected point must be plate at ({pixelX},{pixelY}); classification={surfaces[pixel]}, emission={Number(decoded.Emission)}W/(m^2 sr). No replacement point is allowed.");
        if (decoded.Receiver.Normal != Double3.UnitY)
            throw new InvalidOperationException($"Selected point normal at ({pixelX},{pixelY}) is {VectorText(decoded.Receiver.Normal)}; existing quadrature requires exact +Y. No normal substitution is allowed.");
        return decoded.Receiver;
    }

    public static double ReadPfmPoint(string path, ReferenceCrop crop, int pixelX, int pixelY)
    {
        using var input = new BinaryReader(File.OpenRead(path), Encoding.ASCII);
        string Line()
        {
            var line = new StringBuilder();
            byte value;
            while ((value = input.ReadByte()) != 10)
                line.Append((char)value);
            return line.ToString();
        }
        if (Line() != "PF" || Line() != FormattableString.Invariant($"{crop.Width} {crop.Height}") || Line() != "-1.0")
            throw new InvalidDataException("Expected the retained little-endian RGB PFM with the original crop dimensions.");
        long start = input.BaseStream.Position;
        if (input.BaseStream.Length - start != crop.Width * crop.Height * 12)
            throw new InvalidDataException("Retained PFM byte count does not match its crop.");
        int column = pixelX - crop.Left;
        int row = pixelY - crop.Top;
        if (column < 0 || column >= crop.Width || row < 0 || row >= crop.Height)
            throw new ArgumentOutOfRangeException(nameof(pixelX));
        input.BaseStream.Position = start + ((crop.Height - 1 - row) * crop.Width + column) * 12;
        float red = input.ReadSingle();
        float green = input.ReadSingle();
        float blue = input.ReadSingle();
        if (!float.IsFinite(red) || red < 0 || red != green || red != blue)
            throw new InvalidDataException("Expected finite nonnegative achromatic retained reference radiance.");
        return red;
    }
}
