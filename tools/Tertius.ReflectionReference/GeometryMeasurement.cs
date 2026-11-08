using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace Tertius.ReflectionReference;

internal sealed record GeometrySnapshot(string Name, byte[] Before, byte[] After);

internal sealed record GeometryAuditFrame(float[] Values, GeometrySnapshot[] Snapshots, JsonElement Metadata)
{
    public uint PlateMaterial { get; init; }
    public uint EmitterMaterial { get; init; } = 1;
}

internal static partial class ReferenceComparison
{
    public static ComparisonMeasurement RetainAuditedMeasurement(
        string path,
        ComparisonFixture fixture,
        ComparisonFrame frame,
        GeometryAuditFrame audit,
        bool measured = false
    )
    {
        Directory.CreateDirectory(path);
        File.WriteAllBytes(
            Path.Combine(path, "audit-inputs.f32"),
            MemoryMarshal.AsBytes(audit.Values.AsSpan()).ToArray()
        );
        File.WriteAllText(
            Path.Combine(path, "audit-metadata.json"),
            JsonSerializer.Serialize(
                new
                {
                    audit.Metadata,
                    audit.PlateMaterial,
                    audit.EmitterMaterial,
                    Fields = "8 float32/pixel: interpolated camera-relative position XYZ m, Euclidean distance m, material shading normal XYZ, material ID",
                    DistinctReceivers = "Interpolated mesh position is measured here. Original probe position remains ray times independent distance and is the shading receiver for any later light integration.",
                },
                MeasurementJsonOptions
            )
        );
        var snapshots = audit
            .Snapshots.Select(snapshot => new
            {
                snapshot.Name,
                BeforeBytes = snapshot.Before.Length,
                AfterBytes = snapshot.After.Length,
                BeforeSha256 = Convert.ToHexStringLower(SHA256.HashData(snapshot.Before)),
                AfterSha256 = Convert.ToHexStringLower(SHA256.HashData(snapshot.After)),
                Equal = snapshot.Before.AsSpan().SequenceEqual(snapshot.After),
            })
            .ToArray();
        File.WriteAllText(
            Path.Combine(path, "audit-snapshots.json"),
            JsonSerializer.Serialize(snapshots, MeasurementJsonOptions)
        );
        (ComparisonMeasurement measurement, List<ComparisonFailure> originalFailures) = RetainAndAssess(
            path: path,
            fixture: fixture,
            frame: frame,
            measured: measured,
            auditedGeometry: true
        );
        List<ComparisonFailure> failures =
        [
            .. originalFailures.Where(failure => failure.Predicate != "primary-position"),
        ];
        foreach (var snapshot in snapshots)
            if (!snapshot.Equal)
                failures.Add(
                    new(
                        Predicate: "audit-non-modification",
                        PixelX: -1,
                        PixelY: -1,
                        Actual: $"{snapshot.Name}:{snapshot.AfterSha256}",
                        Expected: snapshot.BeforeSha256,
                        Units: "raw whole-image SHA256",
                        Tolerance: 0
                    )
                );
        string[] expectedSnapshots = ["distance", "history", "composition"];
        if (!audit.Snapshots.Select(snapshot => snapshot.Name).Order().SequenceEqual(expectedSnapshots.Order()))
            failures.Add(
                new(
                    Predicate: "audit-snapshot-coverage",
                    PixelX: -1,
                    PixelY: -1,
                    Actual: string.Join(",", audit.Snapshots.Select(snapshot => snapshot.Name)),
                    Expected: "distance,history,composition",
                    Units: "live images",
                    Tolerance: 0
                )
            );
        int count = fixture.Crop.Width * fixture.Crop.Height;
        if (audit.Values.Length != count * 8)
            failures.Add(
                new(
                    Predicate: "audit-channel-count",
                    PixelX: -1,
                    PixelY: -1,
                    Actual: Number(audit.Values.Length),
                    Expected: Number(count * 8),
                    Units: "float32 scalars",
                    Tolerance: 0
                )
            );
        byte[] auditedSurfaces = [.. Enumerable.Repeat(byte.MaxValue, count)];
        var residuals = new StringBuilder(
            "pixelX,pixelY,surface,auditDistance_m,independentDistance_m,probeDistance_m,interpolatedToShading_m,interpolatedToIdeal_m,shadingRadiusResidual_m\n"
        );
        double maximumInterpolatedToShading = 0;
        double maximumInterpolatedToIdeal = 0;
        double maximumShadingRadius = 0;
        object? worstInterpolatedToShading = null;
        object? worstInterpolatedToIdeal = null;
        int distanceMatches = 0;
        int visibilityMatches = 0;
        for (
            int pixel = 0;
            pixel < count
                && audit.Values.Length == count * 8
                && frame.Inputs.Length == count * 16
                && frame.Distances.Length == count;
            pixel++
        )
        {
            int pixelX = fixture.Crop.Left + pixel % fixture.Crop.Width;
            int pixelY = fixture.Crop.Top + pixel / fixture.Crop.Width;
            float[] values = audit.Values[(pixel * 8)..(pixel * 8 + 8)];
            void Check(bool valid, string predicate, string actual, string expected, string units, double tolerance)
            {
                if (!valid)
                    failures.Add(
                        new(
                            Predicate: predicate,
                            PixelX: pixelX,
                            PixelY: pixelY,
                            Actual: actual,
                            Expected: expected,
                            Units: units,
                            Tolerance: tolerance
                        )
                    );
            }
            Check(
                valid: values.All(float.IsFinite),
                predicate: "audit-finite",
                actual: string.Join(",", values.Select(value => Number(value))),
                expected: "all eight fields finite",
                units: "see audit field metadata",
                tolerance: 0
            );
            if (values.Any(value => !float.IsFinite(value)))
                continue;
            Check(
                valid: values[3] == frame.Distances[pixel]
                    && values[3] == frame.Inputs[pixel * 16 + 9]
                    && values[3] > 0,
                predicate: "audit-distance",
                actual: Number(values[3]),
                expected: $"independent={Number(frame.Distances[pixel])},probe={Number(frame.Inputs[pixel * 16 + 9])}",
                units: "m, exact float32 equality",
                tolerance: 0
            );
            if (values[3] == frame.Distances[pixel] && values[3] == frame.Inputs[pixel * 16 + 9] && values[3] > 0)
                distanceMatches++;
            Double3 position = fixture.Camera.Eye + new Double3(values[0], values[1], values[2]);
            var normal = new Double3(values[4], values[5], values[6]);
            ReferenceSurface? surface = AuditedSurface(
                position: position,
                normal: normal,
                material: values[7],
                fixture: fixture,
                audit: audit
            );
            Check(
                valid: surface is not null,
                predicate: "audit-surface",
                actual: $"position={VectorText(position)},normal={VectorText(normal)},material={Number(values[7])}",
                expected: $"plate material {audit.PlateMaterial} or emitter material {audit.EmitterMaterial} on its recorded surface",
                units: "m/unit normal/material ID",
                tolerance: 1e-4
            );
            if (surface is { } recognized)
                auditedSurfaces[pixel] = (byte)recognized;
            Check(
                valid: auditedSurfaces[pixel] == measurement.Surfaces[pixel],
                predicate: "audit-primary-visibility",
                actual: Number(auditedSurfaces[pixel]),
                expected: Number(measurement.Surfaces[pixel]),
                units: "surface code: plate0/emitter1/unknown255",
                tolerance: 0
            );
            if (auditedSurfaces[pixel] == measurement.Surfaces[pixel])
                visibilityMatches++;
            ComparisonPixel decoded = Decode(
                frame.Inputs[(pixel * 16)..(pixel * 16 + 16)],
                fixture.Camera.Eye,
                fixture.ExposureStops
            );
            Check(
                valid: (normal - decoded.Receiver.Normal).Length <= 1e-6,
                predicate: "audit-normal",
                actual: VectorText(normal),
                expected: VectorText(decoded.Receiver.Normal),
                units: "unit vector",
                tolerance: 1e-6
            );
            double shadingRadiusResidual = Math.Abs(
                (decoded.Receiver.Position - fixture.Camera.Eye).Length - frame.Distances[pixel]
            );
            Check(
                valid: shadingRadiusResidual <= 1e-4,
                predicate: "audit-shading-distance",
                actual: Number(shadingRadiusResidual),
                expected: "<=0.0001",
                units: "m",
                tolerance: 1e-4
            );
            ReferencePrimaryHit? primary = fixture.Camera.PrimaryHit(
                fixture.Camera.Ray(pixelX + 0.5, pixelY + 0.5),
                fixture.Emitter
            );
            double interpolatedToShading = (position - decoded.Receiver.Position).Length;
            double? interpolatedToIdeal = primary is { } hit ? (position - hit.Hit.Position).Length : null;
            var residual = new
            {
                PixelX = pixelX,
                PixelY = pixelY,
                Surface = auditedSurfaces[pixel],
                AuditDistance = values[3],
                IndependentDistance = frame.Distances[pixel],
                ProbeDistance = frame.Inputs[pixel * 16 + 9],
                InterpolatedPosition = new[] { position.X, position.Y, position.Z },
                ShadingPosition = new[]
                {
                    decoded.Receiver.Position.X,
                    decoded.Receiver.Position.Y,
                    decoded.Receiver.Position.Z,
                },
                InterpolatedToShadingMeters = interpolatedToShading,
                InterpolatedToIdealMeters = interpolatedToIdeal,
                ShadingRadiusResidualMeters = shadingRadiusResidual,
            };
            if (interpolatedToShading > maximumInterpolatedToShading)
            {
                maximumInterpolatedToShading = interpolatedToShading;
                worstInterpolatedToShading = residual;
            }
            if (interpolatedToIdeal is { } idealResidual && idealResidual > maximumInterpolatedToIdeal)
            {
                maximumInterpolatedToIdeal = idealResidual;
                worstInterpolatedToIdeal = residual;
            }
            maximumShadingRadius = Math.Max(maximumShadingRadius, shadingRadiusResidual);
            residuals.AppendLine(
                string.Join(
                    ",",
                    new[]
                    {
                        Number(pixelX),
                        Number(pixelY),
                        Number(auditedSurfaces[pixel]),
                        Number(values[3]),
                        Number(frame.Distances[pixel]),
                        Number(frame.Inputs[pixel * 16 + 9]),
                        Number(interpolatedToShading),
                        interpolatedToIdeal is { } ideal ? Number(ideal) : "",
                        Number(shadingRadiusResidual),
                    }
                )
            );
        }
        File.WriteAllText(Path.Combine(path, "audit-residuals.csv"), residuals.ToString());
        File.WriteAllBytes(Path.Combine(path, "audit-surface-classification.u8"), auditedSurfaces);
        File.WriteAllText(
            Path.Combine(path, "audit-validation.json"),
            JsonSerializer.Serialize(
                new
                {
                    Scope = measured
                        ? "Audited measured-frame geometry AND light/history checks. Not an ideal-primary validation pass or reference agreement."
                        : "Audited geometry preflight only. No ideal-primary validation pass or settled-history assumption.",
                    Measured = measured,
                    OriginalAnalyticPositionFailures = originalFailures.Where(failure =>
                        failure.Predicate == "primary-position"
                    ),
                    HistoricalPositionToleranceMeters = 1e-4,
                    SurfacePlaneToleranceMeters = 1e-4,
                    SurfacePlaneLimit = "Existing position tolerance applied only to known mesh planes/bounds, not a widened analytic ray-hit allowance or certified raster bound.",
                    CropPixels = count,
                    ExactDistanceMatches = distanceMatches,
                    AnalyticVisibilityMatches = visibilityMatches,
                    PlatePixels = auditedSurfaces.Count(surface => surface == (byte)ReferenceSurface.Plate),
                    EmitterPixels = auditedSurfaces.Count(surface => surface == (byte)ReferenceSurface.Emitter),
                    UnknownPixels = auditedSurfaces.Count(surface => surface == byte.MaxValue),
                    MaximumInterpolatedToShadingMeters = maximumInterpolatedToShading,
                    WorstInterpolatedToShading = worstInterpolatedToShading,
                    MaximumInterpolatedToIdealMeters = maximumInterpolatedToIdeal,
                    WorstInterpolatedToIdeal = worstInterpolatedToIdeal,
                    MaximumShadingRadiusResidualMeters = maximumShadingRadius,
                    Failures = failures,
                    ResidualFile = "audit-residuals.csv",
                },
                MeasurementJsonOptions
            )
        );
        RejectFailures(path, failures);
        return measurement;
    }

    private static ReferenceSurface? AuditedSurface(
        Double3 position,
        Double3 normal,
        float material,
        ComparisonFixture fixture,
        GeometryAuditFrame audit
    )
    {
        if (material == audit.PlateMaterial && Math.Abs(position.Y) <= 1e-4 && (normal - Double3.UnitY).Length <= 1e-6)
            return ReferenceSurface.Plate;
        if (material != audit.EmitterMaterial)
            return null;
        double[] coordinates = [position.X, position.Y, position.Z];
        double[] minimum = [fixture.Emitter.Minimum.X, fixture.Emitter.Minimum.Y, fixture.Emitter.Minimum.Z];
        double[] maximum = [fixture.Emitter.Maximum.X, fixture.Emitter.Maximum.Y, fixture.Emitter.Maximum.Z];
        double[] normals = [normal.X, normal.Y, normal.Z];
        if (
            Enumerable
                .Range(0, 3)
                .Any(axis => coordinates[axis] < minimum[axis] - 1e-4 || coordinates[axis] > maximum[axis] + 1e-4)
        )
            return null;
        for (int axis = 0; axis < 3; axis++)
        {
            bool upper = normals[axis] > 0;
            double face = upper ? maximum[axis] : minimum[axis];
            if (
                Math.Abs(Math.Abs(normals[axis]) - 1) <= 1e-6
                && Math.Abs(coordinates[axis] - face) <= 1e-4
                && Enumerable.Range(0, 3).Where(other => other != axis).All(other => Math.Abs(normals[other]) <= 1e-6)
            )
                return ReferenceSurface.Emitter;
        }
        return null;
    }
}
