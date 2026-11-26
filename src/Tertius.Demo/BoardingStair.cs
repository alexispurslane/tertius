using System.Numerics;

namespace Tertius.Demo;

/// <summary>
/// A boarding stair beside a parked shuttle (https://github.com/alexispurslane/tertius/wiki/blue-danube-demo, "The fit to the art"): a landing a gap away
/// from the hull's face, whose top lies <see cref="LipDrop"/> under the shuttle's porch, so that the porch's lip rests over
/// it, and a way from the landing down to the floor. In a full gravity the way is steps. In the station's weak gravity it
/// is a ramp: a person who walks off a step there falls slowly, about a meter forward for each step, and so flies down a
/// stair in one long fall. Every piece stands on the floor, in the axes of the frame that holds the stair, which run along
/// that frame's axes.
/// </summary>
/// <param name="Top">
/// The middle of the landing's edge at the hull, on its top face, in meters, relative to the place where the stair's body
/// stands, so that the pieces keep a float's precision far from the frame's origin.
/// </param>
/// <param name="Up">The unit axis up from the floor.</param>
/// <param name="Out">The unit axis away from the hull's face, along which the landing reaches.</param>
/// <param name="Down">The unit axis along which the way runs down from the landing: <paramref name="Out"/> or across it.</param>
/// <param name="Height">How high the landing's top lies above the floor, in meters.</param>
/// <param name="Ramp">True for a ramp at <see cref="RampSlope"/>, false for steps of at most <see cref="MostRise"/>.</param>
public sealed record BoardingStair(Vector3 Top, Vector3 Up, Vector3 Out, Vector3 Down, float Height, bool Ramp = false)
{
    /// <summary>
    /// The gap between the hull's face and the landing, in meters. The docking's move out comes down on a slant, so the
    /// hull's face passes the landing's top 0.134 m beyond its parked place.
    /// </summary>
    public const float Gap = 0.3f;

    /// <summary>How far the porch's lip reaches over the landing, in meters, beyond the gap.</summary>
    public const float LipOverlap = 0.3f;

    /// <summary>
    /// How far the landing's top lies under the porch's floor, in meters: the lip's thickness of 3 cm and 5 mm of air. A
    /// person steps 3.5 cm down onto the landing, or up onto the lip. The edge of a landing flush with the porch, across
    /// the gap, threw a person 0.15 m up in the station's gravity, as the climb of any edge does.
    /// </summary>
    public const float LipDrop = 0.035f;

    /// <summary>The most that one step rises, in meters, within the 0.4 m that a character climbs as a stair.</summary>
    public const float MostRise = 0.34f;

    /// <summary>The depth of each step, in meters.</summary>
    public const float Tread = 0.4f;

    /// <summary>
    /// The ramp's slope, in radians: 10 degrees. A person who walks over its top at 1 m/s leaves it for a moment and floats
    /// at most 3 cm above it in the station's 0.48 m/s².
    /// </summary>
    public const float RampSlope = 10 * MathF.PI / 180;

    /// <summary>The width of the stair, and the landing's reach from the hull, in meters.</summary>
    public const float Width = 1.6f;

    /// <summary>How many steps lead from the floor to the landing, the landing included.</summary>
    public int Steps => (int)MathF.Ceiling(Height / MostRise);

    /// <summary>How high each step rises, in meters: the height in equal parts.</summary>
    public float Rise => Height / Steps;

    /// <summary>How far the way runs along <see cref="Down"/> from the landing's far edge to the floor, in meters.</summary>
    public float Run => Ramp ? Height / MathF.Tan(RampSlope) : (Steps - 1) * Tread;

    /// <summary>
    /// Where a person's center stands on the landing, <paramref name="above"/> meters above its top, in its middle.
    /// </summary>
    public Vector3 OnLanding(float above) => LandingMiddle + Up * above;

    /// <summary>
    /// Where a person's center stands on the floor at the stair's foot, <paramref name="above"/> meters above the floor,
    /// one tread beyond the way's end on the stair's middle line.
    /// </summary>
    public Vector3 AtFoot(float above) => LandingMiddle + Down * (Width / 2 + Run + Tread) + Up * (above - Height);

    private Vector3 LandingMiddle => Top + Out * (Width / 2);

    private Vector3 Across =>
        Vector3.Abs(Out - Down) == Vector3.Zero ? Vector3.Abs(Vector3.Cross(Up, Out)) : Vector3.Abs(Out);

    /// <summary>
    /// The blocks: the landing, <see cref="Width"/> square, from the floor to <see cref="Top"/>, and for steps each step one
    /// <see cref="Rise"/> lower than the last, one <see cref="Tread"/> deep, along <see cref="Down"/> from the landing's
    /// far edge. A ramp is no block (see <see cref="RampCorners"/>).
    /// </summary>
    public IReadOnlyList<BoundingBox> Blocks()
    {
        var blocks = new List<BoundingBox>
        {
            Block(from: LandingMiddle - Down * (Width / 2), to: LandingMiddle + Down * (Width / 2), height: Height),
        };
        if (Ramp)
            return blocks;
        for (int step = 1; step < Steps; step++)
        {
            Vector3 near = LandingMiddle + Down * (Width / 2 + (step - 1) * Tread);
            blocks.Add(Block(from: near, to: near + Down * Tread, height: Height - step * Rise));
        }

        return blocks;
    }

    /// <summary>
    /// The ramp's wedge, empty for steps: its six corners, the three of its side on each side of the middle line. Each
    /// side runs from the landing's far edge on the floor up to its top, and down along <see cref="Down"/> to the floor
    /// <see cref="Run"/> beyond.
    /// </summary>
    public Vector3[] RampCorners()
    {
        if (!Ramp)
            return [];
        Vector3 edge = LandingMiddle + Down * (Width / 2),
            floor = -Up * Height,
            side = Across * (Width / 2);
        Vector3[] outline = [edge, edge + floor, edge + Down * Run + floor];
        return [.. outline.Select(p => p - side), .. outline.Select(p => p + side)];
    }

    /// <summary>A block from <paramref name="from"/> to <paramref name="to"/> on the top's line, <see cref="Width"/> wide across, from the floor to <paramref name="height"/>.</summary>
    private BoundingBox Block(Vector3 from, Vector3 to, float height)
    {
        Vector3 floor = -Up * Height,
            top = Up * (height - Height),
            side = Across * (Width / 2);
        Vector3 a = from + floor - side,
            b = to + top + side;
        return new(Vector3.Min(a, b), Vector3.Max(a, b));
    }
}
