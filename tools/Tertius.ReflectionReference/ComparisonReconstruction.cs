using System.Runtime.InteropServices;

namespace Tertius.ReflectionReference;

internal static partial class ReferenceComparison
{
    public static ComparisonMeasurement RetainReconstruction(string path, ComparisonMeasurement unfiltered, float[] output, double exposureScale)
    {
        Directory.CreateDirectory(path);
        File.WriteAllBytes(Path.Combine(path, "reconstructed-output.f32"), MemoryMarshal.AsBytes(output.AsSpan()).ToArray());
        int count = unfiltered.RawSpecular.Length;
        if (output.Length != count * 2 || !double.IsFinite(exposureScale) || exposureScale <= 0)
            throw new InvalidDataException("Reconstruction requires two float32 channels per pixel and a finite positive recorded exposure scale.");
        var raw = new double[count];
        var plate = new double[count];
        var emitter = new double[count];
        for (int pixel = 0; pixel < count; pixel++)
        {
            double value = output[2 * pixel] / exposureScale;
            double original = output[2 * pixel + 1] / exposureScale;
            if (!double.IsFinite(value) || value < 0 || original != unfiltered.RawSpecular[pixel])
                throw new InvalidDataException($"reconstruction-output at crop index {pixel}: outgoing={value:G17} W/(m^2 sr), unfiltered={original:G17}, expected unfiltered={unfiltered.RawSpecular[pixel]:G17}; finite nonnegative output and exact raw identity required. Actual bytes retained in {path}.");
            raw[pixel] = value;
            if (unfiltered.Surfaces[pixel] == (byte)ReferenceSurface.Plate)
                plate[pixel] = value;
            else if (unfiltered.Surfaces[pixel] == (byte)ReferenceSurface.Emitter)
                emitter[pixel] = value;
            else
                throw new InvalidDataException($"Unrecognized reconstruction surface at crop index {pixel}.");
        }
        return unfiltered with { RawSpecular = raw, PlateSpecular = plate, EmitterSpecular = emitter };
    }
}
