using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text.Json;
using Tertius.Rendering;

namespace Tertius.ReflectionReference;

internal static partial class Program
{
    private static readonly int[] Levels = [128, 512, 2048];
    private static readonly uint[] Seeds = [41, 97];
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    internal static int Main(string[] args)
    {
        if (args is ["--geometry-audit", "--out", _])
            return CaptureLive(roughnessName: "smooth", mode: "off", path: args[2], diagnosticOnly: true, geometryAudit: true);
        if (args is ["--raster-metadata", "--out", _])
            return CaptureLive(roughnessName: "smooth", mode: "off", path: args[2], diagnosticOnly: true);
        if (args is ["--live", _, _, "--out", _])
            return CaptureLive(roughnessName: args[1], mode: args[2], path: args[4], geometryAudit: true);
        if (args is ["--centers", _, "--out", _])
            return WriteCenterReferences(args[1], args[3]);
        if (args is ["--captured-points", _, "--out", _])
            return VerifyCapturedPoints(args[1], args[3]);
        if (args is ["--metrics", _, "--out", _])
            return WriteMetricReport(args[1], args[3]);
        if (args is ["--cap-metrics", _, _, "--out", _])
            return WriteCapMetricReport(retainedRoot: args[1], uncappedRoot: args[2], path: args[4]);
        if (args is ["--cap-shaders", _, "--out", _])
            return VerifyCapShaderIdentity(args[1], args[3]);
        if (args is ["--sharing-shaders", _, "--out", _])
            return VerifyCapShaderIdentity(args[1], args[3], allowReconstruction: true);
        if (args is ["--sharing-metrics", _, _, _, "--out", _])
            return WriteCapMetricReport(retainedRoot: args[1], uncappedRoot: args[2], path: args[5], sharingRoot: args[3]);
        bool pointsOnly = args is ["--points", "--out", _];
        if (!pointsOnly && args is not ["--out", _])
        {
            Console.Error.WriteLine("Usage: Tertius.ReflectionReference [--points | --raster-metadata] --out <new-output-directory>");
            return 2;
        }
        string path = args[^1];
        if (Directory.Exists(path) || File.Exists(path))
        {
            Console.Error.WriteLine("The output path already exists. Choose a new directory to preserve prior evidence.");
            return 2;
        }
        Directory.CreateDirectory(path);
        var camera = new ReferenceCamera(
            Eye: new(0, 3, 6), Target: new(0, 0, 6 - 3 * Math.Sqrt(3)),
            VerticalFov: MathF.PI / 3f, Width: 1280, Height: 720
        );
        var crop = new ReferenceCrop(Left: 530, Top: 240, Width: 80, Height: 160);
        var emitter = new ReferenceEmitter(
            Minimum: new(-1.1, 0, -2.1), Maximum: new(-0.9, 2, -1.9), Radiance: new(6800, 6800, 6800)
        );
        if (pointsOnly)
        {
            WritePointChecks(path, camera, emitter);
            return 0;
        }
        var cases = new List<object>();
        foreach (double roughness in new double[] { 0, 0.2f })
        {
            var material = new MaterialSample(BaseColor: new(0.06f, 0.06f, 0.06f), Metallic: 0, Roughness: roughness, Reflectance: 0.5);
            string name = roughness == 0 ? "smooth" : "rough-020";
            cases.Add(RenderCase(path: path, name: name, camera: camera, crop: crop, emitter: emitter, material: material));
        }
        var metadata = new
        {
            Scope = "One-bounce specular radiance of a controlled emitter above an infinite flat plate. No other lighting.",
            RadianceUnits = "W/(m^2 sr), scene-linear RGB; no pre-exposure or material demodulation",
            Coordinates = "Meters relative to the plate top. +Y is the plate normal. Pixels have top-left origin.",
            Camera = new { Eye = new[] { camera.Eye.X, camera.Eye.Y, camera.Eye.Z }, Target = new[] { camera.Target.X, camera.Target.Y, camera.Target.Z }, camera.VerticalFov, camera.Width, camera.Height },
            Crop = crop,
            PixelFootprint = "Uniform box average over each original 1280x720 pixel. Crop is not resized. No jittered history.",
            Emitter = new { Minimum = new[] { -1.1, 0, -2.1 }, Maximum = new[] { -0.9, 2, -1.9 }, Radiance = new[] { 6800, 6800, 6800 }, Faces = "Opaque box, uniform outward radiance; exact slab intersection" },
            Plate = "Infinite Y=0 plane. Material is uniform within each case; this is not the full six-strip scene.",
            Integration = "Equal-count balance-weighted emitter-area and GGX-normal samples. Two exact-geometry light directions per pair. No ray weight cap.",
            PixelAndLightSequence = "5D Halton bases 2,3,5,7,11, starting at index 1, independent deterministic per-pixel unit-torus shifts",
            Seeds, PairsPerPixel = Levels, Replicates = Seeds.Length,
            Convergence = "Nested sample levels and two shifted replicates. Differences are measured estimates, not certified error bounds or confidence intervals.",
            Display = "Preview only: radiance * 2^-6.8, Reinhard x/(1+x), then standard sRGB encoding. No bloom, FSR, sharpen, histories or limiting.",
            DfgHalfTableSha256 = Convert.ToHexStringLower(SHA256.HashData(MemoryMarshal.AsBytes(BrdfTables.Shared.Dfg))),
            Runtime = RuntimeInformation.FrameworkDescription,
            Architecture = RuntimeInformation.ProcessArchitecture.ToString(),
            Cases = cases,
        };
        File.WriteAllText(Path.Combine(path, "manifest.json"), JsonSerializer.Serialize(metadata, JsonOptions));
        Console.WriteLine($"Saved bounded reference outputs to {Path.GetFullPath(path)}");
        return 0;
    }

    private static object RenderCase(string path, string name, ReferenceCamera camera, ReferenceCrop crop, ReferenceEmitter emitter, MaterialSample material)
    {
        var replicates = new List<ReferenceImage[]>();
        foreach (uint seed in Seeds)
        {
            Console.WriteLine($"Rendering {name}, shift {seed}, {Levels[^1]} sample pairs per pixel, {crop.Width}x{crop.Height} pixels");
            ReferenceImage[] images = ReferenceImage.RenderLevels(camera: camera, crop: crop, emitter: emitter, material: material, levels: Levels, seed: seed);
            replicates.Add(images);
            for (int level = 0; level < Levels.Length; level++)
            {
                using FileStream stream = File.Create(Path.Combine(path, $"{name}-seed{seed}-pairs{Levels[level]}.pfm"));
                images[level].WritePfm(stream);
            }
        }
        var means = new List<ReferenceImage>();
        for (int level = 0; level < Levels.Length; level++)
        {
            var pixels = new Double3[crop.Width * crop.Height];
            for (int pixel = 0; pixel < pixels.Length; pixel++)
                pixels[pixel] = (replicates[0][level].Pixels[pixel] + replicates[1][level].Pixels[pixel]) / 2;
            var mean = new ReferenceImage(crop.Width, crop.Height, pixels);
            means.Add(mean);
            using FileStream stream = File.Create(Path.Combine(path, $"{name}-mean-pairs{Levels[level]}.pfm"));
            mean.WritePfm(stream);
        }
        ReferenceImage final = means[^1];
        final.WritePreview(Path.Combine(path, $"{name}-preview.png"), -6.8);
        WriteProfiles(path: Path.Combine(path, $"{name}-profiles.csv"), image: final, crop: crop);
        object[] changes = [
            new { From = Levels[0], To = Levels[1], Difference = ReferenceDifference.Compare(means[1], means[0]) },
            new { From = Levels[1], To = Levels[2], Difference = ReferenceDifference.Compare(means[2], means[1]) },
        ];
        ReferenceDifference repeatDifference = ReferenceDifference.Compare(replicates[1][^1], replicates[0][^1]);
        Console.WriteLine($"{name}: final replicate relative L1 = {repeatDifference.RelativeL1:G6}");
        return new
        {
            Name = name,
            material.Roughness,
            FlooredRoughness = material.Lobe.Roughness,
            material.Lobe.Alpha,
            BaseColor = new[] { material.BaseColor.X, material.BaseColor.Y, material.BaseColor.Z },
            material.Metallic, material.Reflectance,
            Changes = changes,
            FinalReplicateDifference = repeatDifference,
            RegionPixelRadianceSum = final.Pixels.Sum(ReferenceImage.Luminance),
            RegionPeakRadiance = final.Pixels.Max(ReferenceImage.Luminance),
            CrossCheck = CrossCheck(camera: camera, emitter: emitter, material: material),
        };
    }

    private static object CrossCheck(ReferenceCamera camera, ReferenceEmitter emitter, MaterialSample material)
    {
        Double3 ray = camera.Ray(570.5, 320.5);
        Double3 point = camera.Eye + (-camera.Eye.Y / ray.Y) * ray;
        var integral = new ReferenceIntegral(emitter, material);
        double mixed = integral.Estimate(point: point, toView: -ray, pairs: 65536, seed: 311).Specular.X;
        double area = integral.Estimate(point: point, toView: -ray, pairs: 65536, seed: 811, emitterOnly: true).Specular.X;
        return new
        {
            Point = new[] { point.X, point.Y, point.Z }, PixelCenter = new[] { 570.5, 320.5 },
            MixedRadiance = mixed, EmitterOnlyRadiance = area,
            RelativeDifference = (area - mixed) / mixed,
            Limit = "One fixed surface point, no pixel averaging. Area-only sampling can miss a narrow smooth lobe; this is a cross-check, not a second ground truth.",
        };
    }

    private static void WriteProfiles(string path, ReferenceImage image, ReferenceCrop crop)
    {
        using var writer = new StreamWriter(path);
        writer.WriteLine("axis,frame_pixel_center,mean_radiance_W_per_m2_sr");
        for (int column = 0; column < image.Width; column++)
        {
            double sum = 0;
            for (int row = 0; row < image.Height; row++)
                sum += ReferenceImage.Luminance(image.Pixels[row * image.Width + column]);
            writer.WriteLine(FormattableString.Invariant($"x,{crop.Left + column + 0.5},{sum / image.Height:R}"));
        }
        for (int row = 0; row < image.Height; row++)
        {
            double sum = 0;
            for (int column = 0; column < image.Width; column++)
                sum += ReferenceImage.Luminance(image.Pixels[row * image.Width + column]);
            writer.WriteLine(FormattableString.Invariant($"y,{crop.Top + row + 0.5},{sum / image.Width:R}"));
        }
    }
}
