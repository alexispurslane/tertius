using System.Reflection;
using System.Text.Json;

namespace Tertius.ReflectionReference;

internal static partial class Program
{
    private static readonly string[] MetricMaterials = ["smooth", "rough"];
    private static readonly string[] MetricModes = ["off", "on"];
    private static readonly string[] MetricLiveFiles =
    [
        "manifest.json",
        "mean.pfm",
        "measured-plate-specular-r.f32",
        "decoded-inputs.f32",
        "surface-classification.u8",
    ];
    private static readonly string[] MetricCenterFiles =
    [
        "center-mean-pairs128.pfm",
        "center-mean-pairs512.pfm",
        "center-mean-pairs2048.pfm",
        "center-seed41-pairs2048.pfm",
        "center-seed97-pairs2048.pfm",
    ];
    private static readonly string[] MetricSharedMetadata =
    [
        "Case",
        "AuthoredRoughness",
        "InternalWidth",
        "InternalHeight",
        "OutputWidth",
        "OutputHeight",
        "Crop",
        "WarmUpDraws",
        "MeasuredDraws",
        "FirstMeasuredDraw",
        "LastMeasuredDraw",
        "JitterPixels",
        "Upscaling",
        "ExposureStops",
        "PreExposureScale",
        "EmissionRadiance",
        "Eye",
        "PlateBounds",
        "EmitterAuthoredBounds",
        "Stage",
        "Quantity",
        "SourceLimit",
        "PrimaryVisibilityRule",
        "MeanQuantity",
        "AuditedGeometry",
    ];

    private static int WriteMetricReport(string retainedRoot, string path)
    {
        CreateNewOutput(path);
        string[] inputs = [.. MetricInputs(retainedRoot).Append(Assembly.GetExecutingAssembly().Location)];
        Dictionary<string, string> inputHashes = inputs.ToDictionary(Path.GetFullPath, MetricFiles.Hash);
        File.WriteAllLines(
            Path.Combine(path, "inputs.sha256"),
            inputHashes.Select(pair => pair.Value + "  " + pair.Key)
        );
        File.WriteAllText(
            Path.Combine(path, "request.json"),
            JsonSerializer.Serialize(
                new
                {
                    RetainedRoot = Path.GetFullPath(retainedRoot),
                    Tool = Assembly.GetExecutingAssembly().Location,
                    Crop = ComparisonCrop,
                    Materials = MetricMaterials,
                    Modes = MetricModes,
                    Scope = "Exactly four retained cases; 32 recorded correlated frames per case, draws64..95; no new renderer or integration work.",
                    Quantity = "Outgoing visible-plate specular luminance in W/(m^2 sr), achromatic RGB/red fixture, exposure already undone by capture. No new exposure conversion.",
                    Totals = "Image-space sums of radiance times fractional unit-pixel-cell overlap, NOT watts or physical energy. Every crop pixel is in full totals.",
                    Anchor = "Saved2048-pair center-reference mean; same geometry visibility/source. Original area-average images stay separate and unchanged.",
                    Profiles = "Every horizontal scanline is primary. Column-mean x and row-mean y are explicitly aggregate diagnostics. Original pixel centers at integer+0.5.",
                    Contrast = "Physical background0. Each anchor profile's maximum supplies fixed10/50/90 percent levels for ALL candidates, including reference uncertainty candidates. No endpoint/minimum fit or brightness renormalization.",
                    Crossings = "All directed linear-interpolation roots and exact-level intervals retained. Isolated exact samples count once. Only unique unobscured roots yield edges. Missing/ambiguous/occluded/open-crop/no-contrast states stay undefined.",
                    Regions = "Fixed anchor left10..left50 and right50..right10 outer bands; tails outside anchor10; complementary core between anchor50. Fractional pixel-cell overlaps. Invalid bands do not discard full totals or other valid bands.",
                    Coverage = "Each band/edge summary reports valid/invalid rows and reasons plus radiance coverage. CoveredReferenceRadiance is band light; EligibleRowReferenceFraction is all anchor light in eligible rows / all anchor crop light.",
                    Normalization = "OFF/reference and ON/reference use anchor light; ON/OFF uses OFF light in the SAME anchor bands. Zero denominators yield null with reasons, not epsilon/NaN/zero success.",
                    Width = "Central5-95 percent cumulative-light width; piecewise-constant unit pixel cells. Each candidate's own cumulative distribution describes shape ONLY. Always CROP-LIMITED; no extrapolation of missing tails.",
                    Temporal = "Sample SD over32 correlated advancing draws; first8/last8 drift and persistent mean error separate. Edge SD uses only valid frames with explicit counts. No confidence interval or temporal convergence claim.",
                    ReferenceSpread = "Saved128/512 means and final2048 shifts41/97 through frozen2048 amplitude, regions and visibility. Descriptive metric sensitivity/status changes, NOT error bounds.",
                    RootEncoding = "all_directed_roots: direction:original_pixel_coordinate:plate_or_occluded separated by semicolons. all_exact_level_intervals: start:end separated by semicolons. Exact touches/boundaries are not chosen edges.",
                    Limits = "One-bounce reference versus live fast-history source feedback: WHOLE-PIPELINE deviation, not isolated limiter/denoiser error. No quality score, acceptance thresholds, energy equivalence, production or performance claim.",
                },
                JsonOptions
            )
        );
        var cases = new Dictionary<string, object>();
        var charts = new List<MetricChartCase>();
        using var tables = new MetricReportTables(path);
        foreach (string material in MetricMaterials)
            cases.Add(
                material,
                MeasureMetricCase(retainedRoot: retainedRoot, material: material, tables: tables, charts: charts)
            );
        File.WriteAllText(
            Path.Combine(path, "metrics.json"),
            JsonSerializer.Serialize(
                new
                {
                    CompletedCases = 4,
                    RecordedFrames = 128,
                    NewRendererDraws = 0,
                    NewLightIntegrations = 0,
                    Cases = cases,
                    Uncertainty = "Reference level/shift ranges are descriptive.32 correlated live frames provide spread/drift, not independent-sample uncertainty or convergence.",
                },
                JsonOptions
            )
        );
        File.WriteAllText(Path.Combine(path, "charts.html"), MetricCharts.Render(charts));
        foreach ((string input, string hash) in inputHashes)
            if (MetricFiles.Hash(input) != hash)
                throw new InvalidOperationException(
                    $"Report input changed while reading: {input}. Saved input identities and partial report are retained."
                );
        File.WriteAllText(
            Path.Combine(path, "input-verification.txt"),
            $"All{inputHashes.Count} saved inputs/tool bytes unchanged before/after;4cases/128recorded frames; no new renderer draws or integrations.\n"
        );
        Console.WriteLine(
            $"Saved descriptive metrics for four cases and128recorded frames to {Path.GetFullPath(path)}"
        );
        return 0;
    }

    private static object MeasureMetricCase(
        string retainedRoot,
        string material,
        MetricReportTables tables,
        List<MetricChartCase> charts
    )
    {
        string controlPath = Path.Combine(retainedRoot, "live", material + "-off");
        string prototypePath = Path.Combine(retainedRoot, "live", material + "-on");
        ComparisonSource controlSource = ReferenceComparison.ReadSource(controlPath);
        ReferenceComparison.RequireCompatibleSources(
            controlSource,
            ReferenceComparison.ReadSource(prototypePath),
            ComparisonCrop
        );
        byte[] surfaces = File.ReadAllBytes(Path.Combine(controlPath, "surface-classification.u8"));
        if (
            surfaces.Length != ComparisonCrop.Width * ComparisonCrop.Height
            || !surfaces.SequenceEqual(File.ReadAllBytes(Path.Combine(prototypePath, "surface-classification.u8")))
        )
            throw new InvalidDataException("Exact OFF/ON source visibility bytes must match the original crop.");
        if (
            surfaces.Any(surface =>
                surface is not ((byte)ReferenceSurface.Plate) and not ((byte)ReferenceSurface.Emitter)
            )
        )
            throw new InvalidDataException("Unknown source surface; no pixel exclusions are allowed.");
        RequireMetricMetadata(retainedRoot, material, controlSource);
        var referenceImages = new Dictionary<string, ReferenceImage>();
        foreach (string file in MetricCenterFiles)
            referenceImages.Add(
                file,
                MetricFiles.ReadPfm(Path.Combine(retainedRoot, "centers", material + "-" + file), ComparisonCrop)
            );
        ReferenceImage anchorImage = referenceImages["center-mean-pairs2048.pfm"];
        double[] anchor = MetricFiles.Scalars(anchorImage);
        var basis = new MetricReference(
            ComparisonCrop,
            anchor,
            [.. surfaces.Select(surface => surface == (byte)ReferenceSurface.Plate ? 1.0 : 0.0)]
        );
        var liveImages = new Dictionary<string, ReferenceImage>();
        var live = new Dictionary<string, double[]>();
        var temporal = new Dictionary<string, object>();
        var comparisons = new Dictionary<string, MetricComparison>();
        var legacy = new Dictionary<string, ReferenceDifference>();
        tables.Bands(material, basis);
        tables.Candidate(material: material, name: "anchor2048", basis: basis, values: anchor);
        foreach (string mode in MetricModes)
        {
            string folder = Path.Combine(retainedRoot, "live", material + "-" + mode);
            ReferenceImage image = MetricFiles.ReadPfm(Path.Combine(folder, "mean.pfm"), ComparisonCrop);
            double[] values = MetricFiles.Scalars(image);
            double[][] frames = MetricFiles.Frames(
                Path.Combine(folder, "measured-plate-specular-r.f32"),
                ComparisonCrop
            );
            RequireZeroEmitterContribution(surfaces, values);
            foreach (double[] frame in frames)
                RequireZeroEmitterContribution(surfaces, frame);
            liveImages.Add(mode, image);
            live.Add(mode, values);
            MetricComparison comparison = basis.Compare(anchor, values);
            comparisons.Add(mode + "_reference", comparison);
            var difference = ReferenceDifference.Compare(anchorImage, image);
            RadianceTotals totals = comparison.Regions.Single(region => region.Name == "full").Totals;
            if (
                difference.MissingFraction != totals.MissingFraction
                || difference.ExcessFraction != totals.ExcessFraction
                || difference.RelativeL1 != totals.RelativeL1
            )
                throw new InvalidOperationException(
                    "Legacy full-crop ReferenceDifference identity failed; saved evidence retained."
                );
            legacy.Add(mode + "_reference", difference);
            tables.Candidate(material: material, name: mode + "_mean", basis: basis, values: values);
            MetricTemporalResult spread = basis.Temporal(frames);
            tables.Temporal(material: material, mode: mode, basis: basis, frames: frames, temporal: spread);
            double[] frameMean = [.. spread.Pixels.Select(pixel => pixel.Temporal.Mean)];
            temporal.Add(
                mode,
                new
                {
                    Frames = 32,
                    FirstDraw = 64,
                    LastDraw = 95,
                    Correlated = true,
                    spread.Regions,
                    SavedMeanVsRecordedFrameMean = new
                    {
                        MaximumAbsolutePixelRadianceDifference = values
                            .Select((value, pixel) => Math.Abs(frameMean[pixel] - value))
                            .Max(),
                        FullCrop = basis
                            .Compare(values, frameMean)
                            .Regions.Single(region => region.Name == "full")
                            .Totals,
                        Reason = "Saved PFM and frame float32 storage can round separately; neither is silently substituted. Pixel temporal mean/bias uses retained frame values; mean image metrics use saved PFM.",
                    },
                }
            );
        }
        comparisons.Add("on_off", basis.Compare(live["off"], live["on"]));
        legacy.Add("on_off", ReferenceDifference.Compare(liveImages["off"], liveImages["on"]));
        var referenceSpread = new Dictionary<string, MetricComparison>();
        foreach ((string file, ReferenceImage image) in referenceImages)
        {
            double[] values = MetricFiles.Scalars(image);
            RequireZeroEmitterContribution(surfaces, values);
            string name = file.Replace(".pfm", "");
            referenceSpread.Add(name, basis.Compare(anchor, values));
            if (file != "center-mean-pairs2048.pfm")
                tables.Candidate(material: material, name: name, basis: basis, values: values);
        }
        charts.Add(new(Material: material, Basis: basis, Off: live["off"], On: live["on"]));
        return new
        {
            Anchor = new
            {
                File = material + "-center-mean-pairs2048.pfm",
                PeakRadiance = anchor.Max(),
                Sum = basis.Compare(anchor, anchor).Regions.Single(region => region.Name == "full").Totals.ReferenceSum,
            },
            PlatePixels = surfaces.Count(surface => surface == (byte)ReferenceSurface.Plate),
            EmitterPixels = surfaces.Count(surface => surface == (byte)ReferenceSurface.Emitter),
            Comparisons = comparisons,
            LegacyFullCropDifference = legacy,
            Temporal = temporal,
            ReferenceCandidatesOnFrozenBasis = referenceSpread,
            ReferenceMetricRanges = MetricRanges([.. referenceSpread.Values]),
            ReferenceRowMetricRanges = MetricRowRanges([.. referenceSpread.Values]),
            OriginalAnalyticResiduals = "The original26ideal-position failures remain failed diagnostics. Inputs use separately measured/audited geometry alignment and reconstructed shading receivers; no global tolerance change.",
        };
    }

    private static object[] MetricRanges(MetricComparison[] candidates) =>
        [
            .. candidates[0]
                .Regions.Select(region => new
                {
                    Region = region.Name,
                    CandidateSum = MetricReference.Distribution([
                        .. candidates.Select(candidate =>
                            candidate.Regions.Single(value => value.Name == region.Name).Totals.CandidateSum
                        ),
                    ]),
                    SignedFraction = MetricReference.Distribution([
                        .. candidates
                            .Select(candidate =>
                                candidate.Regions.Single(value => value.Name == region.Name).Totals.SignedFraction
                            )
                            .OfType<double>(),
                    ]),
                    MissingFraction = MetricReference.Distribution([
                        .. candidates
                            .Select(candidate =>
                                candidate.Regions.Single(value => value.Name == region.Name).Totals.MissingFraction
                            )
                            .OfType<double>(),
                    ]),
                    ExcessFraction = MetricReference.Distribution([
                        .. candidates
                            .Select(candidate =>
                                candidate.Regions.Single(value => value.Name == region.Name).Totals.ExcessFraction
                            )
                            .OfType<double>(),
                    ]),
                }),
        ];

    private static object[] MetricRowRanges(MetricComparison[] candidates) =>
        [
            .. MetricReference.ScalarNames.Select(name =>
            {
                MetricRowObservable[] values =
                [
                    .. candidates.Select(candidate => candidate.RowObservables.Single(value => value.Name == name)),
                ];
                return new
                {
                    Observable = name,
                    MeanOfValidRowValues = MetricReference.Distribution([
                        .. values.Select(value => value.Actual.Values.Mean).OfType<double>(),
                    ]),
                    MeanOfValidRowChanges = MetricReference.Distribution([
                        .. values.Select(value => value.Change.Values.Mean).OfType<double>(),
                    ]),
                    MaximumAbsoluteValidRowChange = MetricReference.Distribution([
                        .. values
                            .Where(value => value.Change.Values.Count > 0)
                            .Select(value =>
                                Math.Max(
                                    Math.Abs(value.Change.Values.Minimum.GetValueOrDefault()),
                                    Math.Abs(value.Change.Values.Maximum.GetValueOrDefault())
                                )
                            ),
                    ]),
                    ValidRowCounts = values.Select(value => value.Actual.Values.Count),
                    Reason = "Different validity/status sets are NOT silently pooled; per-candidate row counts, coverage and status changes accompany these descriptive ranges.",
                };
            }),
        ];

    private static IEnumerable<string> MetricInputs(string root)
    {
        yield return Path.Combine(root, "comparison-results.json");
        yield return Path.Combine(root, "centers", "manifest.json");
        foreach (string material in MetricMaterials)
        {
            foreach (string mode in MetricModes)
            foreach (string file in MetricLiveFiles)
                yield return Path.Combine(path1: root, path2: "live", path3: material + "-" + mode, path4: file);
            foreach (string file in MetricCenterFiles)
                yield return Path.Combine(root, "centers", material + "-" + file);
        }
    }

    private static void RequireZeroEmitterContribution(byte[] surfaces, double[] values)
    {
        for (int pixel = 0; pixel < surfaces.Length; pixel++)
            if (surfaces[pixel] == (byte)ReferenceSurface.Emitter && values[pixel] != 0)
                throw new InvalidDataException(
                    $"Nonzero PLATE contribution on retained emitter pixel{pixel}; raw emitter data must not be relabeled or excluded."
                );
    }

    private static void RequireMetricMetadata(string root, string material, ComparisonSource source)
    {
        using var control = JsonDocument.Parse(
            File.ReadAllText(Path.Combine(path1: root, path2: "live", path3: material + "-off", path4: "manifest.json"))
        );
        using var prototype = JsonDocument.Parse(
            File.ReadAllText(Path.Combine(path1: root, path2: "live", path3: material + "-on", path4: "manifest.json"))
        );
        foreach (string field in MetricSharedMetadata)
            if (
                control.RootElement.GetProperty(field).GetRawText()
                != prototype.RootElement.GetProperty(field).GetRawText()
            )
                throw new InvalidDataException(
                    $"OFF/ON metadata mismatch in{field}; no reference gain or pixel exclusion is allowed."
                );
        JsonElement metadata = control.RootElement;
        if (
            metadata.GetProperty("MeasuredDraws").GetInt32() != 32
            || metadata.GetProperty("FirstMeasuredDraw").GetInt32() != 64
            || metadata.GetProperty("LastMeasuredDraw").GetInt32() != 95
            || metadata.GetProperty("WarmUpDraws").GetInt32() != 64
            || metadata.GetProperty("InternalWidth").GetInt32() != 1280
            || metadata.GetProperty("InternalHeight").GetInt32() != 720
            || metadata.GetProperty("JitterPixels").GetProperty("X").GetDouble() != 0
            || metadata.GetProperty("JitterPixels").GetProperty("Y").GetDouble() != 0
            || metadata.GetProperty("Upscaling").GetString() != "NoUpscaling"
            || !metadata.GetProperty("AuditedGeometry").GetBoolean()
            || JsonSerializer.Deserialize<ReferenceCrop>(metadata.GetProperty("Crop"), JsonOptions) != ComparisonCrop
        )
            throw new InvalidDataException(
                "Metrics accept only the original four audited native zero-jitter cases and fixed64+32draw recording."
            );
        using var centers = JsonDocument.Parse(File.ReadAllText(Path.Combine(root, "centers", "manifest.json")));
        JsonElement center = centers
            .RootElement.GetProperty("Cases")
            .EnumerateArray()
            .Single(value => value.GetProperty("Name").GetString() == material);
        ReferenceEmitter emitter = ComparisonEmitter(source.EmissionRadiance);
        if (
            center.GetProperty("Emission").GetDouble() != source.EmissionRadiance
            || !center
                .GetProperty("EmitterMinimum")
                .EnumerateArray()
                .Select(value => value.GetDouble())
                .SequenceEqual([emitter.Minimum.X, emitter.Minimum.Y, emitter.Minimum.Z])
            || !center
                .GetProperty("EmitterMaximum")
                .EnumerateArray()
                .Select(value => value.GetDouble())
                .SequenceEqual([emitter.Maximum.X, emitter.Maximum.Y, emitter.Maximum.Z])
            || JsonSerializer.Deserialize<ReferenceCrop>(centers.RootElement.GetProperty("Crop"), JsonOptions)
                != ComparisonCrop
        )
            throw new InvalidDataException(
                "Frozen center-reference source/crop does not match retained decoded live source; no fitted conversion is allowed."
            );
    }
}
