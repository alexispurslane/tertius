using System.Runtime.InteropServices;
using System.Text.Json;

namespace Tertius.ReflectionReference;

internal static partial class Program
{
    private static int WriteCenterReferences(string liveRoot, string path)
    {
        CreateNewOutput(path);
        var cases = new List<object>();
        foreach (string name in new[] { "smooth", "rough" })
        {
            string control = Path.Combine(liveRoot, $"{name}-off");
            string prototype = Path.Combine(liveRoot, $"{name}-on");
            float[] inputs = MemoryMarshal
                .Cast<byte, float>(File.ReadAllBytes(Path.Combine(control, "decoded-inputs.f32")))
                .ToArray();
            float[] onInputs = MemoryMarshal
                .Cast<byte, float>(File.ReadAllBytes(Path.Combine(prototype, "decoded-inputs.f32")))
                .ToArray();
            byte[] surfaces = File.ReadAllBytes(Path.Combine(control, "surface-classification.u8"));
            byte[] onSurfaces = File.ReadAllBytes(Path.Combine(prototype, "surface-classification.u8"));
            int count = ComparisonCrop.Width * ComparisonCrop.Height;
            if (
                inputs.Length != count * 16
                || onInputs.Length != inputs.Length
                || surfaces.Length != count
                || onSurfaces.Length != count
            )
                throw new InvalidOperationException(
                    "Retained OFF/ON receiver and classification counts do not match the original crop."
                );
            using var metadata = JsonDocument.Parse(File.ReadAllText(Path.Combine(control, "manifest.json")));
            using var onMetadata = JsonDocument.Parse(File.ReadAllText(Path.Combine(prototype, "manifest.json")));
            static ComparisonSource Source(float[] values, JsonDocument document) =>
                new(
                    Inputs: values,
                    ExposureStops: document.RootElement.GetProperty("ExposureStops").GetDouble(),
                    PreExposureScale: document.RootElement.GetProperty("PreExposureScale").GetDouble(),
                    EmissionRadiance: document.RootElement.GetProperty("EmissionRadiance").GetDouble()
                );
            ReferenceComparison.RequireCompatibleSources(
                Source(inputs, metadata),
                Source(onInputs, onMetadata),
                ComparisonCrop
            );
            double stops = Math.Log2(metadata.RootElement.GetProperty("PreExposureScale").GetDouble());
            double emission = metadata.RootElement.GetProperty("EmissionRadiance").GetDouble();
            ReferenceEmitter emitter = ComparisonEmitter(emission);
            var receivers = new DecodedReceiver[inputs.Length / 16];
            for (int pixel = 0; pixel < receivers.Length; pixel++)
            {
                ComparisonPixel decoded = ReferenceComparison.Decode(
                    inputs[(pixel * 16)..(pixel * 16 + 16)],
                    ComparisonEye,
                    stops
                );
                int pixelX = ComparisonCrop.Left + pixel % ComparisonCrop.Width;
                int pixelY = ComparisonCrop.Top + pixel / ComparisonCrop.Width;
                if (
                    ComparisonCamera.PrimaryHit(ComparisonCamera.Ray(pixelX + 0.5, pixelY + 0.5), emitter)
                    is not { } primary
                )
                    throw new InvalidOperationException(
                        $"recognized-primary-surface at pixel ({pixelX},{pixelY}): expected plate/emitter; actual unknown."
                    );
                if (surfaces[pixel] != (byte)primary.Surface || onSurfaces[pixel] != surfaces[pixel])
                    throw new InvalidOperationException(
                        $"off-on-primary-visibility at pixel ({pixelX},{pixelY}): OFF={surfaces[pixel]}, ON={onSurfaces[pixel]}; expected={(byte)primary.Surface}; tolerance=0."
                    );
                if (
                    inputs[pixel * 16 + 9] <= 0
                    || (primary.Surface == ReferenceSurface.Plate ? decoded.Emission != 0 : decoded.Emission <= 0)
                )
                    throw new InvalidOperationException(
                        $"primary-receiver at pixel ({pixelX},{pixelY}): distance={inputs[pixel * 16 + 9]}m, emission={decoded.Emission}W/(m^2 sr); expected positive distance and emission consistent with {primary.Surface}."
                    );
                receivers[pixel] = decoded.Receiver;
            }
            var replicates = new List<ReferenceImage[]>();
            foreach (uint seed in Seeds)
            {
                Console.WriteLine(
                    $"center reference {name}, shift {seed}, {Levels[^1]} pairs, {receivers.Length} captured receivers"
                );
                ReferenceImage[] images = ReferenceComparison.CenterLevels(
                    receivers: receivers,
                    emitter: emitter,
                    crop: ComparisonCrop,
                    levels: Levels,
                    seed: seed,
                    camera: ComparisonCamera
                );
                replicates.Add(images);
                for (int level = 0; level < Levels.Length; level++)
                {
                    using FileStream output = File.Create(
                        Path.Combine(path, $"{name}-center-seed{seed}-pairs{Levels[level]}.pfm")
                    );
                    images[level].WritePfm(output);
                }
            }
            var means = new List<ReferenceImage>();
            for (int level = 0; level < Levels.Length; level++)
            {
                Double3[] pixels =
                [
                    .. Enumerable
                        .Range(0, receivers.Length)
                        .Select(pixel => (replicates[0][level].Pixels[pixel] + replicates[1][level].Pixels[pixel]) / 2),
                ];
                var image = new ReferenceImage(ComparisonCrop.Width, ComparisonCrop.Height, pixels);
                means.Add(image);
                using FileStream output = File.Create(
                    Path.Combine(path, $"{name}-center-mean-pairs{Levels[level]}.pfm")
                );
                image.WritePfm(output);
            }
            means[^1].WritePreview(Path.Combine(path, $"{name}-center-preview.png"), ComparisonExposure);
            WriteProfiles(Path.Combine(path, $"{name}-center-profiles.csv"), means[^1], ComparisonCrop);
            DecodedReceiver[] plateReceivers =
            [
                .. receivers.Where((_, pixel) => surfaces[pixel] == (byte)ReferenceSurface.Plate),
            ];
            cases.Add(
                new
                {
                    Name = name,
                    Count = receivers.Length,
                    plateReceivers[0].Material,
                    PlatePixels = plateReceivers.Length,
                    EmitterPixels = surfaces.Count(surface => surface == (byte)ReferenceSurface.Emitter),
                    MinPositionY = plateReceivers.Min(receiver => receiver.Position.Y),
                    MaxPositionY = plateReceivers.Max(receiver => receiver.Position.Y),
                    MaxNormalDifferenceFromIdeal = plateReceivers.Max(receiver =>
                        (receiver.Normal - Double3.UnitY).Length
                    ),
                    MaxViewLengthError = plateReceivers.Max(receiver => Math.Abs(receiver.ToView.Length - 1)),
                    Emission = emission,
                    EmitterMinimum = new[] { emitter.Minimum.X, emitter.Minimum.Y, emitter.Minimum.Z },
                    EmitterMaximum = new[] { emitter.Maximum.X, emitter.Maximum.Y, emitter.Maximum.Z },
                    Changes = new[]
                    {
                        ReferenceDifference.Compare(means[1], means[0]),
                        ReferenceDifference.Compare(means[2], means[1]),
                    },
                    FinalShiftDifference = ReferenceDifference.Compare(replicates[1][^1], replicates[0][^1]),
                    Sum = means[^1].Pixels.Sum(ReferenceImage.Luminance),
                    Peak = means[^1].Pixels.Max(ReferenceImage.Luminance),
                }
            );
        }
        File.WriteAllText(
            Path.Combine(path, "manifest.json"),
            JsonSerializer.Serialize(
                new
                {
                    SamplingFootprint = "Captured native pixel CENTER, not uniform pixel area. Recorded decoded receiver position, normal, view and material; no fitted gain.",
                    Quantity = "Outgoing specular contribution of VISIBLE PLATE. Shared original analytic primary visibility; emitter-occluded plate contribution is zero. Raw emitter values remain separate in live captures.",
                    Crop = ComparisonCrop,
                    Levels,
                    Seeds,
                    Cases = cases,
                    Limits = "One-bounce specular reference. Shared material/DFG, independent mixed box integration. Two shifts and three nested levels are fixed evidence, not rigorous error bounds. GPU float raster geometry and float BRDF arithmetic can differ from CPU double arithmetic. Original ideal area-average outputs remain separate.",
                },
                JsonOptions
            )
        );
        return 0;
    }
}
