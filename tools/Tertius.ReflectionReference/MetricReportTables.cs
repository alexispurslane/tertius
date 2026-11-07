using System.Globalization;

namespace Tertius.ReflectionReference;

internal sealed class MetricReportTables : IDisposable
{
    private static readonly int[] ProfileRows = [260, 320, 380];
    private readonly MetricTable roots;
    private readonly MetricTable rows;
    private readonly MetricTable profiles;
    private readonly MetricTable pixels;
    private readonly MetricTable temporalEdges;
    private readonly MetricTable frameRegions;
    private readonly MetricTable regionRows;

    public MetricReportTables(string path)
    {
        roots = new(Path.Combine(path, "crossings.csv.gz"));
        roots.Write("material", "candidate", "axis", "scanline", "reference_fraction", "level_W_per_m2_sr", "lower_position_px", "upper_position_px", "lower_reasons", "upper_reasons", "all_directed_roots", "all_exact_level_intervals");
        rows = new(Path.Combine(path, "row-observables.csv.gz"));
        rows.Write("material", "candidate", "y", "observable", "reference_value_px", "candidate_value_px", "change_px", "reference_reasons", "candidate_reasons");
        profiles = new(Path.Combine(path, "profiles.csv.gz"));
        profiles.Write("material", "candidate", "profile", "coordinate_px", "reference_W_per_m2_sr", "candidate_W_per_m2_sr", "plate_coverage");
        pixels = new(Path.Combine(path, "pixel-temporal.csv.gz"));
        pixels.Write("material", "mode", "x", "y", "plate_coverage", "mean_W_per_m2_sr", "sample_sd_W_per_m2_sr", "last8_minus_first8_W_per_m2_sr", "mean_signed_error_W_per_m2_sr");
        temporalEdges = new(Path.Combine(path, "edge-temporal.csv.gz"));
        temporalEdges.Write("material", "mode", "y", "observable", "valid_frames", "invalid_frames", "mean_px", "minimum_px", "maximum_px", "sample_sd_px", "last8_minus_first8_px", "drift_reason", "invalid_frame_reasons");
        frameRegions = new(Path.Combine(path, "frame-regions.csv.gz"));
        frameRegions.Write("material", "mode", "draw", "region", "reference_sum", "candidate_sum", "missing", "excess", "signed_fraction", "missing_fraction", "excess_fraction", "normalization_reason", "valid_rows", "invalid_rows", "covered_reference_fraction", "eligible_row_reference_fraction");
        regionRows = new(Path.Combine(path, "region-rows.csv.gz"));
        regionRows.Write("material", "region", "y", "valid", "reasons", "reference_radiance_in_band", "reference_radiance_in_row", "fractional_pixel_cell_weights");
    }

    public void Candidate(string material, string name, MetricReference basis, double[] values)
    {
        WriteRows(material: material, name: name, basis: basis, measured: basis.Rows(values));
        double[] anchorColumns = basis.ColumnMean(basis.Reference);
        double[] actualColumns = basis.ColumnMean(values);
        double[] coverageColumns = basis.ColumnMean(basis.Coverage);
        double[] anchorRows = basis.RowMean(basis.Reference);
        double[] actualRows = basis.RowMean(values);
        double[] coverageRows = basis.RowMean(basis.Coverage);
        for (int column = 0; column < basis.Crop.Width; column++)
            profiles.Write(material, name, "column_mean", basis.Crop.Left + column + 0.5, anchorColumns[column], actualColumns[column], coverageColumns[column]);
        for (int row = 0; row < basis.Crop.Height; row++)
            profiles.Write(material, name, "row_mean", basis.Crop.Top + row + 0.5, anchorRows[row], actualRows[row], coverageRows[row]);
        foreach (int pixelY in ProfileRows)
        {
            int row = pixelY - basis.Crop.Top;
            if (row < 0 || row >= basis.Crop.Height)
                continue;
            for (int column = 0; column < basis.Crop.Width; column++)
            {
                int pixel = row * basis.Crop.Width + column;
                profiles.Write(material, name, "horizontal_y" + pixelY, basis.Crop.Left + column + 0.5, basis.Reference[pixel], values[pixel], basis.Coverage[pixel]);
            }
        }
        MetricComparison comparison = basis.Compare(basis.Reference, values);
        WriteLevels(material: material, name: name, axis: "column_mean_x", scanline: "all_rows", profile: comparison.ColumnMean);
        WriteLevels(material: material, name: name, axis: "row_mean_y", scanline: "all_columns", profile: comparison.RowMean);
    }

    public void Bands(string material, MetricReference basis)
    {
        foreach (MetricBandRow row in basis.BandRows())
            regionRows.Write(material, row.Name, row.PixelY, row.Reasons.Length == 0, string.Join(';', row.Reasons), row.ReferenceRadiance, row.RowReferenceRadiance, string.Join(';', row.Weights.Select(Number)));
    }

    public void Temporal(string material, string mode, MetricReference basis, double[][] frames, MetricTemporalResult temporal)
    {
        for (int pixel = 0; pixel < temporal.Pixels.Length; pixel++)
        {
            MetricPixelTemporal measured = temporal.Pixels[pixel];
            pixels.Write(material, mode, basis.Crop.Left + pixel % basis.Crop.Width, basis.Crop.Top + pixel / basis.Crop.Width, basis.Coverage[pixel], measured.Temporal.Mean, measured.Temporal.SampleStandardDeviation, measured.Temporal.First8Last8Difference, measured.MeanSignedError);
        }
        MetricProfile[][] frameRows = [.. frames.Select(basis.Rows)];
        for (int frame = 0; frame < frames.Length; frame++)
        {
            WriteRows(material: material, name: mode + "_draw" + (64 + frame), basis: basis, measured: frameRows[frame]);
            foreach (MetricRegionResult region in basis.MeasureRegions(basis.Reference, frames[frame]))
            {
                RadianceTotals totals = region.Totals;
                frameRegions.Write(material, mode, 64 + frame, region.Name, totals.ReferenceSum, totals.CandidateSum, totals.Missing, totals.Excess, totals.SignedFraction, totals.MissingFraction, totals.ExcessFraction, totals.NormalizationReason, region.ValidRows, region.InvalidRows, region.ReferenceCoverageFraction, region.EligibleRowReferenceFraction);
            }
        }
        for (int row = 0; row < basis.Crop.Height; row++)
            foreach (string name in MetricReference.ScalarNames)
            {
                MetricScalar[] values = [.. frameRows.Select(frame => MetricReference.Scalars(frame[row])[name])];
                MetricDistribution distribution = MetricReference.Distribution([.. values.Where(value => value.Value.HasValue).Select(value => value.Value.GetValueOrDefault())]);
                double? drift = null;
                if (values[..8].All(value => value.Value.HasValue) && values[^8..].All(value => value.Value.HasValue))
                    drift = values[^8..].Average(value => value.Value.GetValueOrDefault()) - values[..8].Average(value => value.Value.GetValueOrDefault());
                string[] reasons = [.. values.SelectMany(value => value.Reasons).GroupBy(reason => reason).Select(group => group.Key + ":" + group.Count())];
                temporalEdges.Write(material, mode, basis.Crop.Top + row, name, distribution.Count, 32 - distribution.Count, distribution.Mean, distribution.Minimum, distribution.Maximum, distribution.SampleStandardDeviation, drift, drift.HasValue ? "" : "MISSING_EDGE_IN_FIRST_OR_LAST8", string.Join(';', reasons));
            }
    }

    public void Dispose()
    {
        roots.Dispose();
        rows.Dispose();
        profiles.Dispose();
        pixels.Dispose();
        temporalEdges.Dispose();
        frameRegions.Dispose();
        regionRows.Dispose();
    }

    private void WriteRows(string material, string name, MetricReference basis, MetricProfile[] measured)
    {
        MetricProfile[] expected = basis.Rows(basis.Reference);
        for (int row = 0; row < basis.Crop.Height; row++)
        {
            WriteLevels(material: material, name: name, axis: "horizontal_x", scanline: (basis.Crop.Top + row).ToString(CultureInfo.InvariantCulture), profile: measured[row]);
            Dictionary<string, MetricScalar> before = MetricReference.Scalars(expected[row]);
            Dictionary<string, MetricScalar> after = MetricReference.Scalars(measured[row]);
            foreach (string scalar in MetricReference.ScalarNames)
                rows.Write(material, name, basis.Crop.Top + row, scalar, before[scalar].Value, after[scalar].Value, after[scalar].Value - before[scalar].Value, string.Join(';', before[scalar].Reasons), string.Join(';', after[scalar].Reasons));
        }
    }

    private void WriteLevels(string material, string name, string axis, string scanline, MetricProfile profile)
    {
        for (int level = 0; level < profile.Levels.Length; level++)
        {
            MetricLevel measured = profile.Levels[level];
            string points = string.Join(';', measured.Roots.Points.Select(root => root.Direction + ":" + Number(root.Position) + ":" + (root.Occluded ? "occluded" : "plate")));
            string plateaus = string.Join(';', measured.Roots.Plateaus.Select(interval => Number(interval.Start) + ":" + Number(interval.End)));
            roots.Write(material, name, axis, scanline, level switch { 0 => 0.1, 1 => 0.5, _ => 0.9 }, measured.Height, measured.Left.Position, measured.Right.Position, string.Join(';', measured.Left.Reasons), string.Join(';', measured.Right.Reasons), points, plateaus);
        }
    }

    private static string Number(double value) => value.ToString("R", CultureInfo.InvariantCulture);
}
