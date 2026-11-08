using System.Runtime.InteropServices;
using System.Globalization;
using System.Text.Json;
using Tertius.Rendering;

namespace Tertius.ReflectionReference;

internal enum ReferenceSurface : byte { Plate, Emitter }

internal sealed record ComparisonFixture(ReferenceCamera Camera, ReferenceCrop Crop, ReferenceEmitter Emitter, double Roughness, double ExposureStops);

internal sealed record ComparisonFrame(int Draw, float[] Inputs, float[] Distances, float[] History)
{
    public double? ActualExposureStops { get; init; }
    public double? PreviousExposureStops { get; init; }
    public JsonElement? RasterMetadata { get; init; }
}

internal sealed record ComparisonMeasurement(
    double[] RawSpecular, double[] PlateSpecular, double[] EmitterSpecular, byte[] Surfaces, double MaximumFactorRelativeError
);

internal sealed record ComparisonFailure(string Predicate, int PixelX, int PixelY, string Actual, string Expected, string Units, double Tolerance);

internal static partial class ReferenceComparison
{
    private static readonly JsonSerializerOptions MeasurementJsonOptions = new() { WriteIndented = true };

    public static ComparisonMeasurement RetainAndMeasure(string path, ComparisonFixture fixture, ComparisonFrame frame, bool measured)
    {
        (ComparisonMeasurement measurement, List<ComparisonFailure> failures) = RetainAndAssess(path: path, fixture: fixture, frame: frame, measured: measured);
        RejectFailures(path, failures);
        return measurement;
    }

    private static (ComparisonMeasurement Measurement, List<ComparisonFailure> Failures) RetainAndAssess(string path, ComparisonFixture fixture, ComparisonFrame frame, bool measured, bool auditedGeometry = false)
    {
        Directory.CreateDirectory(path);
        File.WriteAllBytes(Path.Combine(path, "probe-inputs.f32"), MemoryMarshal.AsBytes(frame.Inputs.AsSpan()).ToArray());
        File.WriteAllBytes(Path.Combine(path, "distance.f32"), MemoryMarshal.AsBytes(frame.Distances.AsSpan()).ToArray());
        File.WriteAllBytes(Path.Combine(path, "source-history.f32"), MemoryMarshal.AsBytes(frame.History.AsSpan()).ToArray());
        File.WriteAllText(Path.Combine(path, "metadata.json"), JsonSerializer.Serialize(new
        {
            Fixture = fixture, frame.Draw, Tick = frame.Draw + 1, SnapshotTimeSeconds = (frame.Draw + 1) / 60.0,
            Measured = measured, frame.ActualExposureStops, frame.PreviousExposureStops, frame.RasterMetadata,
            ProbeFields = "16 float32/pixel: preexposed specular R, preexposed emission R, roughness, reflectance, baseColor R, metallic, normal XYZ, distance m, camera-relative position XYZ m, composite view XYZ",
            IndependentFields = "distance.f32: one distance m/pixel; source-history.f32: four preexposed history channels/pixel",
            Phase = measured ? "Measured-frame checks after advancing warm-up" : "First-draw geometry preflight; no settled-history assumption",
        }, MeasurementJsonOptions));
        int count = fixture.Crop.Width * fixture.Crop.Height;
        var raw = new double[count];
        var plate = new double[count];
        var emitter = new double[count];
        byte[] surfaces = [.. Enumerable.Repeat(byte.MaxValue, count)];
        var failures = new List<ComparisonFailure>();
        int pixelX = -1;
        int pixelY = -1;
        void Check(bool valid, string predicate, string actual, string expected, string units, double tolerance)
        {
            if (!valid)
                failures.Add(new(
                    Predicate: predicate, PixelX: pixelX, PixelY: pixelY, Actual: actual, Expected: expected, Units: units, Tolerance: tolerance
                ));
        }
        Check(valid: frame.Inputs.Length == count * 16 && frame.Distances.Length == count && frame.History.Length == count * 4,
            predicate: "crop-channel-counts", actual: $"{frame.Inputs.Length},{frame.Distances.Length},{frame.History.Length}",
            expected: $"{count * 16},{count},{count * 4}", units: "scalars", tolerance: 0);
        Check(valid: frame.ActualExposureStops is null || frame.ActualExposureStops == fixture.ExposureStops,
            predicate: "exposure-stops", actual: Number(frame.ActualExposureStops ?? fixture.ExposureStops),
            expected: Number(fixture.ExposureStops), units: "stops", tolerance: 0);
        if (measured)
            Check(valid: frame.PreviousExposureStops is null || frame.PreviousExposureStops == fixture.ExposureStops,
                predicate: "previous-exposure-stops", actual: Number(frame.PreviousExposureStops ?? fixture.ExposureStops),
                expected: Number(fixture.ExposureStops), units: "stops", tolerance: 0);
        double maximumFactorError = 0;
        for (int pixel = 0; pixel < count && frame.Inputs.Length == count * 16 && frame.Distances.Length == count && frame.History.Length == count * 4; pixel++)
        {
            pixelX = fixture.Crop.Left + pixel % fixture.Crop.Width;
            pixelY = fixture.Crop.Top + pixel / fixture.Crop.Width;
            int priorFailures = failures.Count;
            float[] packed = frame.Inputs[(pixel * 16)..(pixel * 16 + 16)];
            Check(valid: packed.All(float.IsFinite), predicate: "probe-fields-finite", actual: string.Join(",", packed.Select(value => Number(value))),
                expected: "all 16 values finite", units: "see probe field metadata", tolerance: 0);
            if (packed.Any(value => !float.IsFinite(value)))
                continue;
            ComparisonPixel decoded = Decode(packed, fixture.Camera.Eye, fixture.ExposureStops);
            DecodedReceiver receiver = decoded.Receiver;
            raw[pixel] = decoded.Specular;
            Double3 ray = fixture.Camera.Ray(pixelX + 0.5, pixelY + 0.5);
            if (fixture.Camera.PrimaryHit(ray, fixture.Emitter) is not { } primary)
            {
                Check(valid: false, predicate: "recognized-primary-surface", actual: "no modeled receiver", expected: "plate or emitter", units: "surface", tolerance: 0);
                continue;
            }
            bool isPlate = primary.Surface == ReferenceSurface.Plate;
            surfaces[pixel] = (byte)primary.Surface;
            Check(valid: packed[9] == frame.Distances[pixel] && packed[9] > 0,
                predicate: "distance-mapping", actual: Number(packed[9]), expected: $"positive; independent distance {Number(frame.Distances[pixel])}", units: "m", tolerance: 0);
            Check(valid: (receiver.Position - primary.Hit.Position).Length <= 1e-4,
                predicate: "primary-position", actual: VectorText(receiver.Position), expected: VectorText(primary.Hit.Position), units: "m", tolerance: 1e-4);
            Check(valid: (receiver.Normal - primary.Hit.Normal).Length <= 1e-6,
                predicate: "primary-normal", actual: VectorText(receiver.Normal), expected: VectorText(primary.Hit.Normal), units: "unit vector", tolerance: 1e-6);
            Check(valid: (receiver.ToView + ray).Length <= 2e-6,
                predicate: "camera-view", actual: VectorText(receiver.ToView), expected: VectorText(-ray), units: "unit vector", tolerance: 2e-6);
            Check(valid: decoded.Specular >= 0, predicate: "outgoing-nonnegative", actual: Number(decoded.Specular), expected: ">=0", units: "W/(m^2 sr)", tolerance: 0);
            Check(valid: isPlate ? decoded.Emission == 0 : decoded.Emission > 0,
                predicate: "primary-emission", actual: Number(decoded.Emission), expected: isPlate ? "0" : ">0", units: "W/(m^2 sr)", tolerance: 0);
            Check(valid: receiver.Material.Metallic == 0, predicate: "primary-metallic", actual: Number(receiver.Material.Metallic), expected: "0", units: "fraction", tolerance: 0);
            Check(valid: Math.Abs(receiver.Material.Roughness - (isPlate ? fixture.Roughness : 1)) <= 1.0 / 255,
                predicate: isPlate ? "plate-roughness" : "emitter-roughness", actual: Number(receiver.Material.Roughness),
                expected: Number(isPlate ? fixture.Roughness : 1), units: "material roughness", tolerance: 1.0 / 255);
            Check(valid: Math.Abs(receiver.Material.Reflectance - (isPlate ? 0.5 : 0)) <= 1.0 / 255,
                predicate: "primary-reflectance", actual: Number(receiver.Material.Reflectance), expected: isPlate ? "0.5" : "0", units: "material reflectance", tolerance: 1.0 / 255);
            Check(valid: Math.Abs(receiver.Material.BaseColor.X - (isPlate ? 0.06 : 0)) <= 0.002,
                predicate: "primary-base-color", actual: Number(receiver.Material.BaseColor.X), expected: isPlate ? "0.06" : "0", units: "linear color fraction", tolerance: 0.002);
            if (isPlate)
                plate[pixel] = decoded.Specular;
            else
                emitter[pixel] = decoded.Specular;
            if (measured)
            {
                float[] history = frame.History[(pixel * 4)..(pixel * 4 + 4)];
                Check(valid: history.All(float.IsFinite) && history[0] == history[1] && history[0] == history[2],
                    predicate: "achromatic-source-history", actual: string.Join(",", history.Select(value => Number(value))),
                    expected: "finite RGB with R=G=B", units: "preexposed source-history channels", tolerance: 0);
                if (failures.Skip(priorFailures).All(failure => auditedGeometry && failure.Predicate == "primary-position"))
                {
                    double factor = receiver.Material.CompensatedSpecularAlbedo(receiver.Normal, receiver.ToView, BrdfTables.Shared).X;
                    double expected = factor * history[0];
                    double error = Math.Abs(packed[0] - expected) / Math.Max(1e-7, expected);
                    maximumFactorError = Math.Max(maximumFactorError, error);
                    Check(valid: error <= 0.01, predicate: "material-factor-once", actual: Number(packed[0]),
                        expected: $"{Number(expected)}; relative difference {Number(error)}", units: "preexposed W/(m^2 sr)", tolerance: 0.01);
                }
            }
        }
        File.WriteAllBytes(Path.Combine(path, "surface-classification.u8"), surfaces);
        File.WriteAllText(Path.Combine(path, "validation.json"), JsonSerializer.Serialize(new
        {
            PlatePixels = surfaces.Count(surface => surface == (byte)ReferenceSurface.Plate),
            EmitterPixels = surfaces.Count(surface => surface == (byte)ReferenceSurface.Emitter),
            UnrecognizedPixels = surfaces.Count(surface => surface == byte.MaxValue),
            Rule = "Original analytic camera-ray emitter-before-Y=0-plate visibility, evaluated at native pixel center; not brightness masking.",
            MaximumFactorRelativeError = maximumFactorError, Failures = failures,
        }, MeasurementJsonOptions));
        return (new(
            RawSpecular: raw, PlateSpecular: plate, EmitterSpecular: emitter, Surfaces: surfaces,
            MaximumFactorRelativeError: maximumFactorError
        ), failures);
    }

    private static void RejectFailures(string path, List<ComparisonFailure> failures)
    {
        if (failures.Count == 0)
            return;
        ComparisonFailure first = failures[0];
        throw new InvalidOperationException($"{first.Predicate} at pixel ({first.PixelX},{first.PixelY}): actual={first.Actual} {first.Units}; expected={first.Expected}; tolerance={Number(first.Tolerance)}. All failures and raw inputs retained in {path}.");
    }

    private static string Number(double value) => value.ToString("R", CultureInfo.InvariantCulture);

    private static string VectorText(Double3 value) => $"({Number(value.X)},{Number(value.Y)},{Number(value.Z)})";
}
