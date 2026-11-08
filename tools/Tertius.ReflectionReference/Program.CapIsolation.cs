using System.Reflection;
using System.Security.Cryptography;
using System.Text.Json;
using Tertius.Rendering;

namespace Tertius.ReflectionReference;

internal static partial class Program
{
    private static readonly string[] CapModes = ["off", "on", "uncapped"];

    private static int VerifyCapShaderIdentity(string originalAssembly, string path, bool allowReconstruction = false)
    {
        CreateNewOutput(path);
        var original = Assembly.LoadFile(Path.GetFullPath(originalAssembly));
        Assembly current = ShaderLibrary.EngineShaders;
        string[] names =
        [
            .. current
                .GetManifestResourceNames()
                .Where(name => name.EndsWith(".spv", StringComparison.Ordinal))
                .Order(),
        ];
        string[] oldNames =
        [
            .. original
                .GetManifestResourceNames()
                .Where(name => name.EndsWith(".spv", StringComparison.Ordinal))
                .Order(),
        ];
        var rows = names
            .Select(name =>
            {
                using Stream actual =
                    current.GetManifestResourceStream(name)
                    ?? throw new InvalidDataException($"Missing current shader {name}.");
                using Stream expected =
                    original.GetManifestResourceStream(name)
                    ?? throw new InvalidDataException($"Missing original shader {name}.");
                string actualHash = Convert.ToHexStringLower(SHA256.HashData(actual));
                string expectedHash = Convert.ToHexStringLower(SHA256.HashData(expected));
                return new
                {
                    Name = name,
                    ActualSha256 = actualHash,
                    ExpectedSha256 = expectedHash,
                    Equal = actualHash == expectedHash,
                };
            })
            .ToArray();
        File.WriteAllText(
            Path.Combine(path, "shader-identity.json"),
            JsonSerializer.Serialize(
                new
                {
                    OriginalAssembly = Path.GetFullPath(originalAssembly),
                    OriginalSha256 = MetricFiles.Hash(originalAssembly),
                    CurrentAssembly = current.Location,
                    CurrentSha256 = MetricFiles.Hash(current.Location),
                    ExactResourceNames = names.SequenceEqual(oldNames),
                    Shaders = rows,
                    ReflectionReconstructionWithDistanceHistory = allowReconstruction,
                },
                JsonOptions
            )
        );
        bool unexpectedChanges = rows.Any(row =>
            !row.Equal
            && !(
                allowReconstruction
                && (
                    row.Name.EndsWith(".composite.frag.spv", StringComparison.Ordinal)
                    || row.Name.EndsWith(".composite_scatter.frag.spv", StringComparison.Ordinal)
                    || row.Name.EndsWith(".accumulate_temporal.comp.spv", StringComparison.Ordinal)
                    || row.Name.EndsWith(".accumulate_spatial.comp.spv", StringComparison.Ordinal)
                )
            )
        );
        if (
            !names.SequenceEqual(oldNames)
            || unexpectedChanges
            || allowReconstruction && rows.Count(row => !row.Equal) != 4
        )
            throw new InvalidOperationException(
                "Unexpected embedded shader change; all-identical cap control or exactly two composite and two distance-history accumulation modules required."
            );
        Console.WriteLine(
            $"Embedded engine shaders: {rows.Count(row => row.Equal)}/{rows.Length} byte-identical; exact composite/distance-history changes accepted={allowReconstruction}."
        );
        return 0;
    }

    private static int WriteCapMetricReport(
        string retainedRoot,
        string uncappedRoot,
        string path,
        string? sharingRoot = null
    )
    {
        CreateNewOutput(path);
        string[] modes = sharingRoot is null ? CapModes : [.. CapModes, "sharing"];
        string[] sharingInputs = sharingRoot is null
            ? []
            :
            [
                .. MetricMaterials.SelectMany(material =>
                    MetricLiveFiles.Select(file =>
                        Path.Combine(path1: sharingRoot, path2: "live", path3: material + "-sharing", path4: file)
                    )
                ),
            ];
        string[] inputs =
        [
            .. MetricInputs(retainedRoot),
            .. MetricMaterials.SelectMany(material =>
                MetricLiveFiles.Select(file =>
                    Path.Combine(path1: uncappedRoot, path2: "live", path3: material + "-uncapped", path4: file)
                )
            ),
            .. sharingInputs,
            Assembly.GetExecutingAssembly().Location,
        ];
        Dictionary<string, string> hashes = inputs.ToDictionary(Path.GetFullPath, MetricFiles.Hash);
        File.WriteAllLines(Path.Combine(path, "inputs.sha256"), hashes.Select(pair => pair.Value + "  " + pair.Key));
        File.WriteAllText(
            Path.Combine(path, "request.json"),
            JsonSerializer.Serialize(
                new
                {
                    RetainedRoot = retainedRoot,
                    UncappedRoot = uncappedRoot,
                    Modes = modes,
                    Crop = ComparisonCrop,
                    SharingRoot = sharingRoot,
                    Scope = "Two new prototype cases; reuse original OFF/ON and any retained uncapped cases and existing center references. No reference integration.",
                    Controls = "off=whole prototype OFF; on=neighbor cap ON plus selected single diffuse history; uncapped=same selected history, only neighbor cap OFF; sharing=uncapped plus output-only radiance sharing, no source/history writes.",
                    Basis = "Unchanged2048 center anchor, physical background0, fixed10/50/90 thresholds and fractional bands. Every pixel in full totals. Undefined rows keep reasons and coverage.",
                    Quantities = "Outgoing plate specular radiance W/(m^2 sr); image-space radiance sums are not watts. Pre-exposure already undone, material factor once. No gain fit.",
                    Frames = "Each case:64warm-up draws0..63 and32measured draws64..95, one initial reset, native1280x720 zero jitter. Actual single-frame preview is draw95, chosen BEFORE inspection.",
                    Display = "Every reference/mean/single-frame preview uses exposure-6.8, Reinhard then sRGB, without per-image normalization. Cropped specular contribution, NOT full composed game output.",
                    Temporal = "Sample SD across32correlated frames, persistent mean error and first8/last8 drift remain separate. No confidence interval, independent-sample or convergence claim.",
                    ReferenceUncertainty = "Existing128/512means and2048shifts use the SAME frozen anchor. Point checks do not certify every edge/tail. Live source includes history feedback; reference is one bounce.",
                },
                JsonOptions
            )
        );
        var cases = new Dictionary<string, object>();
        using (var tables = new MetricReportTables(path))
            foreach (string material in MetricMaterials)
                cases.Add(
                    material,
                    MeasureCapCase(
                        retainedRoot: retainedRoot,
                        uncappedRoot: uncappedRoot,
                        material: material,
                        path: path,
                        tables: tables,
                        sharingRoot: sharingRoot
                    )
                );
        File.WriteAllText(
            Path.Combine(path, "metrics.json"),
            JsonSerializer.Serialize(
                new
                {
                    CompletedNewCases = 2,
                    NewRecordedFrames = 64,
                    Cases = cases,
                },
                JsonOptions
            )
        );
        Dictionary<string, string> after = inputs.ToDictionary(Path.GetFullPath, MetricFiles.Hash);
        File.WriteAllLines(
            Path.Combine(path, "inputs-after.sha256"),
            after.Select(pair => pair.Value + "  " + pair.Key)
        );
        if (hashes.Any(pair => after[pair.Key] != pair.Value))
            throw new InvalidOperationException("Cap-isolation input bytes changed during reporting.");
        Console.WriteLine(
            "Compared two cap-disabled cases against unchanged OFF/ON cases and center references; single draw95 previews and temporal noise retained."
        );
        return 0;
    }

    private static object MeasureCapCase(
        string retainedRoot,
        string uncappedRoot,
        string material,
        string path,
        MetricReportTables tables,
        string? sharingRoot
    )
    {
        string original = Path.Combine(retainedRoot, "live", material + "-off");
        ComparisonSource source = ReferenceComparison.ReadSource(original);
        RequireMetricMetadata(retainedRoot, material, source);
        byte[] surfaces = File.ReadAllBytes(Path.Combine(original, "surface-classification.u8"));
        ReferenceImage anchorImage = MetricFiles.ReadPfm(
            Path.Combine(retainedRoot, "centers", material + "-center-mean-pairs2048.pfm"),
            ComparisonCrop
        );
        double[] anchor = MetricFiles.Scalars(anchorImage);
        var basis = new MetricReference(
            ComparisonCrop,
            anchor,
            [.. surfaces.Select(surface => surface == (byte)ReferenceSurface.Plate ? 1.0 : 0.0)]
        );
        var images = new Dictionary<string, double[]>();
        var comparisons = new Dictionary<string, MetricComparison>();
        var temporal = new Dictionary<string, object>();
        tables.Bands(material, basis);
        tables.Candidate(material: material, name: "anchor2048", basis: basis, values: anchor);
        anchorImage.WritePreview(Path.Combine(path, material + "-reference.png"), ComparisonExposure);
        string[] modes = sharingRoot is null ? CapModes : [.. CapModes, "sharing"];
        foreach (string mode in modes)
        {
            string root = mode == "uncapped" ? uncappedRoot : retainedRoot;
            if (mode == "sharing")
                root =
                    sharingRoot
                    ?? throw new InvalidOperationException("A sharing report requires its retained inputs.");
            string folder = Path.Combine(root, "live", material + "-" + mode);
            ReferenceComparison.RequireCompatibleSources(
                source,
                ReferenceComparison.ReadSource(folder),
                ComparisonCrop
            );
            if (!surfaces.SequenceEqual(File.ReadAllBytes(Path.Combine(folder, "surface-classification.u8"))))
                throw new InvalidOperationException(
                    "Cap isolation requires exact source visibility bytes; no masks or pixel substitutions."
                );
            using var expected = JsonDocument.Parse(File.ReadAllText(Path.Combine(original, "manifest.json")));
            using var actual = JsonDocument.Parse(File.ReadAllText(Path.Combine(folder, "manifest.json")));
            foreach (string field in MetricSharedMetadata)
                if (
                    expected.RootElement.GetProperty(field).GetRawText()
                    != actual.RootElement.GetProperty(field).GetRawText()
                )
                    throw new InvalidOperationException(
                        $"Cap-isolation metadata differs in {field} for {material}/{mode}."
                    );
            ReferenceImage image = MetricFiles.ReadPfm(Path.Combine(folder, "mean.pfm"), ComparisonCrop);
            double[] values = MetricFiles.Scalars(image);
            double[][] frames = MetricFiles.Frames(
                Path.Combine(folder, "measured-plate-specular-r.f32"),
                ComparisonCrop
            );
            RequireZeroEmitterContribution(surfaces, values);
            foreach (double[] frame in frames)
                RequireZeroEmitterContribution(surfaces, frame);
            images.Add(mode, values);
            MetricComparison comparison = basis.Compare(anchor, values);
            comparisons.Add(mode + "_reference", comparison);
            var legacy = ReferenceDifference.Compare(anchorImage, image);
            RadianceTotals totals = comparison.Regions.Single(region => region.Name == "full").Totals;
            if (
                legacy.MissingFraction != totals.MissingFraction
                || legacy.ExcessFraction != totals.ExcessFraction
                || legacy.RelativeL1 != totals.RelativeL1
            )
                throw new InvalidOperationException("Legacy full-crop metric identity failed.");
            tables.Candidate(material: material, name: mode, basis: basis, values: values);
            MetricTemporalResult spread = basis.Temporal(frames);
            temporal.Add(
                mode,
                new
                {
                    Frames = 32,
                    FirstDraw = 64,
                    LastDraw = 95,
                    Correlated = true,
                    spread.Regions,
                }
            );
            if (mode is "uncapped" or "sharing")
                tables.Temporal(material: material, mode: mode, basis: basis, frames: frames, temporal: spread);
            image.WritePreview(Path.Combine(path, material + "-" + mode + "-mean.png"), ComparisonExposure);
            new ReferenceImage(
                ComparisonCrop.Width,
                ComparisonCrop.Height,
                [.. frames[^1].Select(value => new Double3(value, value, value))]
            ).WritePreview(Path.Combine(path, material + "-" + mode + "-draw95.png"), ComparisonExposure);
        }
        comparisons.Add("uncapped_on", basis.Compare(images["on"], images["uncapped"]));
        comparisons.Add("uncapped_off", basis.Compare(images["off"], images["uncapped"]));
        if (sharingRoot is not null)
        {
            comparisons.Add("sharing_uncapped", basis.Compare(images["uncapped"], images["sharing"]));
            comparisons.Add("sharing_on", basis.Compare(images["on"], images["sharing"]));
        }
        var referenceSpread = new Dictionary<string, MetricComparison>();
        foreach (string file in MetricCenterFiles)
        {
            double[] values = MetricFiles.Scalars(
                MetricFiles.ReadPfm(Path.Combine(retainedRoot, "centers", material + "-" + file), ComparisonCrop)
            );
            referenceSpread.Add(file, basis.Compare(anchor, values));
        }
        return new
        {
            Comparisons = comparisons,
            Temporal = temporal,
            ReferenceCandidatesOnFrozenBasis = referenceSpread,
        };
    }
}
