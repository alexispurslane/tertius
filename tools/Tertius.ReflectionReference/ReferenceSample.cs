namespace Tertius.ReflectionReference;

internal readonly record struct ReferenceSample(double PixelX, double PixelY, double Face, double Across, double Down)
{
    public static ReferenceSample[] Sequence(int count)
    {
        var samples = new ReferenceSample[count];
        for (int index = 0; index < count; index++)
            samples[index] = new(
                PixelX: RadicalInverse(index + 1, 2),
                PixelY: RadicalInverse(index + 1, 3),
                Face: RadicalInverse(index + 1, 5),
                Across: RadicalInverse(index + 1, 7),
                Down: RadicalInverse(index + 1, 11)
            );
        return samples;
    }

    public static ReferenceSample Shift(uint seed) => new(
        PixelX: Hash(seed + 1), PixelY: Hash(seed + 2), Face: Hash(seed + 3), Across: Hash(seed + 4), Down: Hash(seed + 5)
    );

    public ReferenceSample Rotated(ReferenceSample shift) => new(
        PixelX: Fraction(PixelX + shift.PixelX),
        PixelY: Fraction(PixelY + shift.PixelY),
        Face: Fraction(Face + shift.Face),
        Across: Fraction(Across + shift.Across),
        Down: Fraction(Down + shift.Down)
    );

    private static double RadicalInverse(int index, int radix)
    {
        double value = 0;
        double place = 1.0 / radix;
        while (index > 0)
        {
            value += index % radix * place;
            index /= radix;
            place /= radix;
        }
        return value;
    }

    private static double Hash(uint value)
    {
        value ^= value >> 16;
        value *= 0x7feb352d;
        value ^= value >> 15;
        value *= 0x846ca68b;
        value ^= value >> 16;
        return (value + 0.5) / 4294967296.0;
    }

    private static double Fraction(double value) => value - Math.Floor(value);
}
