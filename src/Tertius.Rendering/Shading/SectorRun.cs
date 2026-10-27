using System.Numerics;

namespace Tertius.Rendering;

/// <summary>
/// A run of a slice's sectors, <see cref="First"/> to <see cref="End"/> − 1, whose centers lie between the angles
/// <see cref="Low"/> and <see cref="High"/>, both within the hemisphere (docs/plans/screen-space-illumination.md, decision
/// 41). An empty run has an end at or before its first sector. SectorRun in sectors.glsl is the GPU copy.
/// </summary>
internal readonly record struct SectorRun(int First, int End, double Low, double High)
{
    public SectorMask Sectors => SectorMask.Run(start: First, end: End);

    /// <summary>
    /// The middle and the width of the angles of <paramref name="part"/>, a set of this run's sectors that is not empty,
    /// from its first sector to its last: each sector stands at the angle that its place in the run takes in proportion
    /// between <see cref="Low"/> and <see cref="High"/>.
    /// </summary>
    public (double Middle, double Width) PartAngles(SectorMask part)
    {
        double perSector = (High - Low) / (End - First);
        double start = Low + (BitOperations.TrailingZeroCount(part.Bits) - First) * perSector;
        double end = Low + (SliceSectors.Count - BitOperations.LeadingZeroCount(part.Bits) - First) * perSector;
        return (Middle: (start + end) / 2, Width: end - start);
    }
}
