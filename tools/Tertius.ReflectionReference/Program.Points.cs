using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text.Json;
using Tertius.Rendering;

namespace Tertius.ReflectionReference;

internal static partial class Program
{
    private readonly record struct PointSampleLevel(int Samples, double MixedRadiance, double UniformAreaRadiance, int AreaCoreHits);

    private static void WritePointChecks(string path, ReferenceCamera camera, ReferenceEmitter emitter)
    {
        Double3 ray = camera.Ray(570.5, 320.5);
        Double3 point = camera.Eye + (-camera.Eye.Y / ray.Y) * ray;
        int[] levels = [65536, 262144, 1048576];
        uint[] seeds = [311, 811, 1451];
        double[] tolerances = [1e-3, 1e-5, 1e-7];
        const int maxEvaluations = 300000;
        var cases = new List<object>();
        using var csv = new StreamWriter(Path.Combine(path, "points.csv"));
        csv.WriteLine("roughness,method,seed,samples_or_evaluations,specular_radiance_W_per_m2_sr,relative_difference_from_finest_area_quadrature,area_core_hits,converged,estimated_absolute_error");
        foreach (double roughness in new double[] { 0, 0.2f })
        {
            var material = new MaterialSample(BaseColor: new(0.06f, 0.06f, 0.06f), Metallic: 0, Roughness: roughness, Reflectance: 0.5);
            var integral = new ReferenceIntegral(emitter, material);
            ReferenceQuadratureResult[] quadratures = tolerances.Select(tolerance => integral.IntegrateEmitter(
                point: point, toView: -ray, relativeTolerance: tolerance, absoluteTolerance: 1e-9, maxEvaluations: maxEvaluations
            )).ToArray();
            double finest = quadratures[^1].Light.Specular.X;
            var sampled = new List<object>();
            foreach (uint seed in seeds)
            {
                PointSampleLevel[] estimates = PointEstimates(
                    integral: integral, emitter: emitter, material: material, point: point, toView: -ray, levels: levels, seed: seed
                );
                sampled.Add(new { Seed = seed, Levels = estimates });
                foreach (PointSampleLevel estimate in estimates)
                {
                    csv.WriteLine(FormattableString.Invariant($"{material.Lobe.Roughness:R},mixed,{seed},{estimate.Samples},{estimate.MixedRadiance:R},{(estimate.MixedRadiance - finest) / finest:R},,,"));
                    csv.WriteLine(FormattableString.Invariant($"{material.Lobe.Roughness:R},uniform-area,{seed},{estimate.Samples},{estimate.UniformAreaRadiance:R},{(estimate.UniformAreaRadiance - finest) / finest:R},{estimate.AreaCoreHits},,"));
                }
            }
            for (int index = 0; index < quadratures.Length; index++)
            {
                ReferenceQuadratureResult quadrature = quadratures[index];
                csv.WriteLine(FormattableString.Invariant($"{material.Lobe.Roughness:R},area-quadrature,,{quadrature.Evaluations},{quadrature.Light.Specular.X:R},{(quadrature.Light.Specular.X - finest) / finest:R},,{quadrature.Converged},{quadrature.EstimatedAbsoluteError:R}"));
            }
            cases.Add(new
            {
                RequestedRoughness = roughness, FlooredRoughness = material.Lobe.Roughness, material.Lobe.Alpha,
                BaseColor = new[] { material.BaseColor.X, material.BaseColor.Y, material.BaseColor.Z }, material.Metallic, material.Reflectance,
                Sampled = sampled,
                AreaQuadrature = quadratures.Select((result, index) => new
                {
                    RelativeTolerance = tolerances[index], AbsoluteTolerance = 1e-9, MaxEvaluations = maxEvaluations,
                    result.Evaluations, result.Converged, result.EstimatedAbsoluteError,
                    SpecularRadiance = result.Light.Specular.X, DiffuseRadiance = result.Light.Diffuse.X,
                }).ToArray(),
            });
            Console.WriteLine(FormattableString.Invariant($"roughness={material.Lobe.Roughness:R}: finest area radiance={finest:R}, evaluations={quadratures[^1].Evaluations}, converged={quadratures[^1].Converged}"));
        }
        var metadata = new
        {
            Scope = "One fixed surface point from the original fixture. Numerical checks only, no images or pixel averaging.",
            RadianceUnits = "W/(m^2 sr), scene-linear RGB; all channels equal, X reported; no pre-exposure",
            Coordinates = "Meters relative to plate top, normal +Y. Pixel coordinates have top-left origin.",
            PixelCenter = new[] { 570.5, 320.5 }, Point = new[] { point.X, point.Y, point.Z }, ToView = new[] { -ray.X, -ray.Y, -ray.Z },
            Camera = new { Eye = new[] { camera.Eye.X, camera.Eye.Y, camera.Eye.Z }, Target = new[] { camera.Target.X, camera.Target.Y, camera.Target.Z }, camera.VerticalFov, camera.Width, camera.Height },
            Emitter = new { Minimum = new[] { emitter.Minimum.X, emitter.Minimum.Y, emitter.Minimum.Z }, Maximum = new[] { emitter.Maximum.X, emitter.Maximum.Y, emitter.Maximum.Z }, Radiance = new[] { emitter.Radiance.X, emitter.Radiance.Y, emitter.Radiance.Z } },
            Plate = "Infinite Y=0, uniform material per case. No other light, history, limiter, spatial filtering or FSR.",
            Sampling = "Original mixed and uniform-area estimators. Nested 5D shifted Halton samples, bases 2,3,5,7,11, starting at index 1.",
            AreaCoreHits = "Visible emitter samples with an above-horizon light direction and half-vector tilt tan(theta)<=alpha from +Y. Counts use all area samples as denominator, not only visible samples.",
            AreaQuadrature = "Direct integration over outward visible box faces. Tensor Gauss-Legendre 3/5 rules, adaptive subdivision, mirror-footprint seed cuts. Does not use either sampler density or samples.",
            QuadratureError = "Sum of panel max-channel absolute 5-minus-3 differences across diffuse and specular. Estimated, not a certified error bound. Converged means this estimate met absolute + relative * max-channel light within budget; null error means incomplete domain coverage.",
            Limit = "Point agreement is not full-image, pixel-footprint, material-equation, live-renderer, energy-equivalence or quality acceptance. Uniform area repeats can still be unconverged.",
            DfgHalfTableSha256 = Convert.ToHexStringLower(SHA256.HashData(MemoryMarshal.AsBytes(BrdfTables.Shared.Dfg))),
            Runtime = RuntimeInformation.FrameworkDescription, Architecture = RuntimeInformation.ProcessArchitecture.ToString(),
            Levels = levels, Seeds = seeds, Cases = cases,
        };
        File.WriteAllText(Path.Combine(path, "points.json"), JsonSerializer.Serialize(metadata, JsonOptions));
    }

    private static PointSampleLevel[] PointEstimates(
        ReferenceIntegral integral, ReferenceEmitter emitter, MaterialSample material, Double3 point, Double3 toView, int[] levels, uint seed
    )
    {
        ReferenceSample[] samples = ReferenceSample.Sequence(levels[^1]);
        ReferenceSample shift = ReferenceSample.Shift(seed);
        var results = new List<PointSampleLevel>();
        double mixed = 0;
        double area = 0;
        int coreHits = 0;
        int level = 0;
        for (int index = 0; index < samples.Length; index++)
        {
            ReferenceSample sample = samples[index].Rotated(shift);
            mixed += integral.Sample(point: point, toView: toView, sample: sample).Specular.X;
            area += integral.Sample(point: point, toView: toView, sample: sample, emitterOnly: true).Specular.X;
            (Double3 position, Double3 normal) = emitter.Surface(face: sample.Face, across: sample.Across, down: sample.Down);
            Double3 direction = (position - point).Normalized();
            Double3 half = (toView + direction).Normalized();
            if (direction.Y > 0 && Double3.Dot(normal, -direction) > 0 && half.Y > 0
                && half.X * half.X + half.Z * half.Z <= material.Lobe.Alpha * material.Lobe.Alpha * half.Y * half.Y)
                coreHits++;
            if (index + 1 != levels[level])
                continue;
            results.Add(new(Samples: index + 1, MixedRadiance: mixed / (index + 1), UniformAreaRadiance: area / (index + 1), AreaCoreHits: coreHits));
            level++;
        }
        return [.. results];
    }
}
