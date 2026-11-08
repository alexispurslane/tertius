using System.Text.Json;

namespace Tertius.ReflectionReference;

internal static partial class Program
{
    private static readonly string[] CapturedPointMaterials = ["smooth", "rough"];
    private static readonly double[] CapturedPointRelativeTolerances = [1e-5, 1e-7];
    private const int CapturedPointMaximumEvaluations = 3_000_000;

    private sealed record CapturedPoint(
        string Material, int PixelX, int PixelY, DecodedReceiver Receiver, ReferenceEmitter Emitter, double Peak, double[] Means, double[] Shifts
    );

    private static int VerifyCapturedPoints(string retainedRoot, string path)
    {
        CreateNewOutput(path);
        (int PixelX, int PixelY)[] coordinates = [
            (550, 260), (570, 260), (590, 260), (550, 320), (570, 320), (590, 320), (550, 380), (570, 380), (590, 380),
            (579, 351), (575, 277), (575, 283), (574, 255), (574, 260), (575, 275), (560, 281), (574, 268),
        ];
        File.WriteAllText(Path.Combine(path, "request.json"), JsonSerializer.Serialize(new
        {
            RetainedRoot = Path.GetFullPath(retainedRoot), Materials = CapturedPointMaterials,
            Points = coordinates.Select(pixel => new { pixel.PixelX, pixel.PixelY }),
            RelativeTolerances = CapturedPointRelativeTolerances, AbsoluteTolerance = 1e-9, MaximumEvaluationsPerQuadrature = CapturedPointMaximumEvaluations,
            MaximumQuadratures = 68, DriftLimitFractionOfPeak = 1e-4, SavedMeanLimitFractionOfPeak = 0.01,
            Scope = "Point-only independent emitter-face quadrature at captured shading receivers. No live draws, sampler reruns, new levels/shifts or point replacements.",
        }, JsonOptions));
        using JsonDocument previous = JsonDocument.Parse(File.ReadAllText(Path.Combine(retainedRoot, "comparison-results.json")));
        using JsonDocument centerMetadata = JsonDocument.Parse(File.ReadAllText(Path.Combine(retainedRoot, "centers", "manifest.json")));
        var points = new List<CapturedPoint>();
        foreach (string name in CapturedPointMaterials)
        {
            string control = Path.Combine(retainedRoot, "live", name + "-off");
            string prototype = Path.Combine(retainedRoot, "live", name + "-on");
            ComparisonSource source = ReferenceComparison.ReadSource(control);
            ReferenceComparison.RequireCompatibleSources(source, ReferenceComparison.ReadSource(prototype), ComparisonCrop);
            byte[] surfaces = File.ReadAllBytes(Path.Combine(control, "surface-classification.u8"));
            if (!surfaces.AsSpan().SequenceEqual(File.ReadAllBytes(Path.Combine(prototype, "surface-classification.u8"))))
                throw new InvalidOperationException("OFF/ON classification bytes differ; no point evaluation is allowed.");
            double peak = previous.RootElement.GetProperty("cases").GetProperty(name).GetProperty("peaks").GetProperty("reference").GetProperty("radiance").GetDouble();
            ReferenceEmitter emitter = ComparisonEmitter(source.EmissionRadiance);
            JsonElement original = centerMetadata.RootElement.GetProperty("Cases").EnumerateArray().Single(element => element.GetProperty("Name").GetString() == name);
            if (!original.GetProperty("EmitterMinimum").EnumerateArray().Select(value => value.GetDouble()).SequenceEqual(new[] { emitter.Minimum.X, emitter.Minimum.Y, emitter.Minimum.Z })
                || !original.GetProperty("EmitterMaximum").EnumerateArray().Select(value => value.GetDouble()).SequenceEqual(new[] { emitter.Maximum.X, emitter.Maximum.Y, emitter.Maximum.Z })
                || original.GetProperty("Emission").GetDouble() != source.EmissionRadiance)
                throw new InvalidOperationException("Recorded source geometry/radiance differs from the frozen center command; no evaluation is allowed.");
            foreach ((int pixelX, int pixelY) in coordinates)
            {
                DecodedReceiver receiver = ReferenceComparison.PlateReceiver(
                    source: source, surfaces: surfaces, crop: ComparisonCrop, eye: ComparisonEye, pixelX: pixelX, pixelY: pixelY
                );
                double Read(string filename) => ReferenceComparison.ReadPfmPoint(
                    path: Path.Combine(retainedRoot, "centers", filename), crop: ComparisonCrop, pixelX: pixelX, pixelY: pixelY
                );
                points.Add(new(
                    Material: name, PixelX: pixelX, PixelY: pixelY, Receiver: receiver, Emitter: emitter, Peak: peak,
                    Means: [.. Levels.Select(level => Read($"{name}-center-mean-pairs{level}.pfm"))],
                    Shifts: [.. Seeds.Select(seed => Read($"{name}-center-seed{seed}-pairs2048.pfm"))]
                ));
            }
        }
        File.WriteAllText(Path.Combine(path, "preselected-inputs.json"), JsonSerializer.Serialize(points, JsonOptions));
        var results = new List<object>();
        using var csv = new StreamWriter(Path.Combine(path, "points.csv"));
        csv.WriteLine("material,pixelX,pixelY,dataset,saved_W_m2_sr,quadrature_fine_W_m2_sr,signed_difference_W_m2_sr,absolute_difference_W_m2_sr,fractional_difference,peak_normalized_difference,coarse_fine_drift_W_m2_sr,drift_over_peak,coarse_converged,fine_converged,coarse_evaluations,fine_evaluations,coarse_estimated_error,fine_estimated_error,diagnostic_criteria_satisfied");
        int completed = 0;
        foreach (CapturedPoint point in points)
        {
            var integral = new ReferenceIntegral(point.Emitter, point.Receiver.Material);
            ReferenceQuadratureResult coarse = integral.IntegrateEmitter(
                point: point.Receiver.Position, toView: point.Receiver.ToView, relativeTolerance: CapturedPointRelativeTolerances[0], absoluteTolerance: 1e-9, maxEvaluations: CapturedPointMaximumEvaluations
            );
            ReferenceQuadratureResult fine = integral.IntegrateEmitter(
                point: point.Receiver.Position, toView: point.Receiver.ToView, relativeTolerance: CapturedPointRelativeTolerances[1], absoluteTolerance: 1e-9, maxEvaluations: CapturedPointMaximumEvaluations
            );
            var verification = new PointVerification(Coarse: coarse, Fine: fine, SavedMean: point.Means[^1], Peak: point.Peak);
            var comparisons = new List<object>();
            for (int dataset = 0; dataset < 5; dataset++)
            {
                string label = dataset < 3 ? $"mean-pairs{Levels[dataset]}" : $"shift{Seeds[dataset - 3]}-pairs2048";
                double saved = dataset < 3 ? point.Means[dataset] : point.Shifts[dataset - 3];
                var difference = verification with { SavedMean = saved };
                comparisons.Add(new { Dataset = label, difference.SavedMean, difference.SignedDifference, difference.AbsoluteDifference, difference.FractionalDifference, difference.PeakNormalizedDifference });
                csv.WriteLine(FormattableString.Invariant($"{point.Material},{point.PixelX},{point.PixelY},{label},{saved:R},{fine.Light.Specular.X:R},{difference.SignedDifference:R},{difference.AbsoluteDifference:R},{difference.FractionalDifference:R},{difference.PeakNormalizedDifference:R},{verification.CoarseFineDrift:R},{verification.CoarseFineDrift / point.Peak:R},{coarse.Converged},{fine.Converged},{coarse.Evaluations},{fine.Evaluations},{coarse.EstimatedAbsoluteError:R},{fine.EstimatedAbsoluteError:R},{verification.DiagnosticCriteriaSatisfied}"));
            }
            completed++;
            results.Add(new { point.Material, point.PixelX, point.PixelY, point.Receiver, point.Emitter, point.Peak, Verification = verification, Comparisons = comparisons });
            csv.Flush();
            File.WriteAllText(Path.Combine(path, "points.json"), JsonSerializer.Serialize(new
            {
                CompletedReceivers = completed, RequestedReceivers = 34, QuadratureCalls = completed * 2,
                RadianceUnits = "W/(m^2 sr), scene-linear outgoing plate specular; no pre-exposure or fitted gain",
                PositionUnits = "World meters, decoded warmed receiver unchanged; exact +Y normal required by existing quadrature",
                EstimatedErrorLimit = "Existing quadrature error estimates are NOT certified bounds. Diagnostic criteria cover only preselected points, not all crop pixels or live source equivalence.",
                Results = results,
            }, JsonOptions));
            Console.WriteLine(FormattableString.Invariant($"{point.Material} ({point.PixelX},{point.PixelY}): quadrature={fine.Light.Specular.X:R}, coarse/fine={coarse.Converged}/{fine.Converged}, evaluations={coarse.Evaluations}/{fine.Evaluations}, drift/peak={verification.CoarseFineDrift / point.Peak:R}, mean error/peak={verification.PeakNormalizedDifference:R}, fractional mean error={verification.FractionalDifference:R}, criteria={verification.DiagnosticCriteriaSatisfied}"));
            if (!verification.DiagnosticCriteriaSatisfied)
                return 1;
        }
        return 0;
    }
}
