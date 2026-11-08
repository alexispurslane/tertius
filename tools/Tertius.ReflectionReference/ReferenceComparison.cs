using Tertius.Rendering;

namespace Tertius.ReflectionReference;

internal readonly record struct DecodedReceiver(
    Double3 Position,
    Double3 Normal,
    Double3 ToView,
    MaterialSample Material
);

internal readonly record struct ComparisonPixel(double Specular, double Emission, DecodedReceiver Receiver);

internal static partial class ReferenceComparison
{
    public static ComparisonPixel Decode(float[] values, Double3 eye, double exposureStops)
    {
        ArgumentOutOfRangeException.ThrowIfNotEqual(values.Length, 16);
        double scale = Math.Pow(2, exposureStops);
        return new(
            values[0] / scale,
            values[1] / scale,
            new(
                Position: eye + new Double3(values[10], values[11], values[12]),
                Normal: new(values[6], values[7], values[8]),
                ToView: new(values[13], values[14], values[15]),
                Material: new(
                    BaseColor: new(values[4], values[4], values[4]),
                    Metallic: values[5],
                    Roughness: values[2],
                    Reflectance: values[3]
                )
            )
        );
    }

    public static ReferenceImage[] CenterLevels(
        DecodedReceiver[] receivers,
        ReferenceEmitter emitter,
        ReferenceCrop crop,
        int[] levels,
        uint seed,
        ReferenceCamera? camera = null
    )
    {
        if (levels.Length == 0 || levels[0] <= 0 || !levels.SequenceEqual(levels.Distinct().Order()))
            throw new ArgumentException("Sample levels must be positive and strictly increasing.", nameof(levels));
        ArgumentOutOfRangeException.ThrowIfNotEqual(receivers.Length, crop.Width * crop.Height);
        ReferenceImage[] images =
        [
            .. levels.Select(_ => new ReferenceImage(crop.Width, crop.Height, new Double3[receivers.Length])),
        ];
        ReferenceSample[] samples = ReferenceSample.Sequence(levels[^1]);
        for (int pixel = 0; pixel < receivers.Length; pixel++)
        {
            if (
                camera is { } primaryCamera
                && primaryCamera.PrimaryHit(
                    primaryCamera.Ray(crop.Left + pixel % crop.Width + 0.5, crop.Top + pixel / crop.Width + 0.5),
                    emitter
                )
                    is not { Surface: ReferenceSurface.Plate }
            )
                continue;
            DecodedReceiver receiver = receivers[pixel];
            var integral = new ReferenceIntegral(emitter, receiver.Material);
            var shift = ReferenceSample.Shift(seed ^ ((uint)pixel * 0x9e3779b9));
            Double3 sum = default;
            int level = 0;
            for (int index = 0; index < samples.Length; index++)
            {
                sum += integral
                    .Sample(
                        point: receiver.Position,
                        toView: receiver.ToView,
                        sample: samples[index].Rotated(shift),
                        normal: receiver.Normal
                    )
                    .Specular;
                if (index + 1 == levels[level])
                    images[level++].Pixels[pixel] = sum / (index + 1);
            }
        }
        return images;
    }
}
