namespace Tertius.ReflectionReference;

internal sealed record ComparisonSource(
    float[] Inputs,
    double ExposureStops,
    double PreExposureScale,
    double EmissionRadiance
);

internal static partial class ReferenceComparison
{
    public static void RequireSameReceivers(float[] expected, float[] actual, ReferenceCrop crop)
    {
        ArgumentOutOfRangeException.ThrowIfNotEqual(expected.Length, crop.Width * crop.Height * 16);
        ArgumentOutOfRangeException.ThrowIfNotEqual(actual.Length, expected.Length);
        for (int pixel = 0; pixel < expected.Length / 16; pixel++)
            if (!expected.AsSpan(pixel * 16 + 1, 15).SequenceEqual(actual.AsSpan(pixel * 16 + 1, 15)))
                throw new InvalidOperationException(
                    $"receiver-source-stability at pixel ({crop.Left + pixel % crop.Width},{crop.Top + pixel / crop.Width}): actual={string.Join(",", actual[(pixel * 16 + 1)..(pixel * 16 + 16)].Select(value => Number(value)))}; expected={string.Join(",", expected[(pixel * 16 + 1)..(pixel * 16 + 16)].Select(value => Number(value)))}; tolerance=0; channel units retained in probe metadata."
                );
    }

    public static void RequireCompatibleSources(
        ComparisonSource control,
        ComparisonSource prototype,
        ReferenceCrop crop
    )
    {
        RequireSameReceivers(control.Inputs, prototype.Inputs, crop);
        if (
            !double.IsFinite(control.ExposureStops)
            || !double.IsFinite(control.PreExposureScale)
            || !double.IsFinite(control.EmissionRadiance)
            || control.PreExposureScale <= 0
            || control.EmissionRadiance <= 0
            || control.ExposureStops != prototype.ExposureStops
            || control.PreExposureScale != prototype.PreExposureScale
            || control.EmissionRadiance != prototype.EmissionRadiance
        )
            throw new InvalidOperationException(
                $"off-on-source-exposure: actual stops={Number(prototype.ExposureStops)}, scale={Number(prototype.PreExposureScale)}, emission={Number(prototype.EmissionRadiance)}W/(m^2 sr); expected stops={Number(control.ExposureStops)}, scale={Number(control.PreExposureScale)}, emission={Number(control.EmissionRadiance)}W/(m^2 sr); tolerance=0."
            );
    }
}
