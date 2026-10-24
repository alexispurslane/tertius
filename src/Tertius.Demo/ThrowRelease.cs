namespace Tertius.Demo;

/// <summary>Where a thrown box lands: <paramref name="Time"/> seconds after its release, at <paramref name="Place"/> in the station's axes.</summary>
public readonly record struct ThrowLanding(double Time, Double3 Place);

/// <summary>
/// The release of a box in a station whose frame falls freely and turns about its +Y at a steady rate, and the two
/// predictions of its landing that the coda sets side by side (docs/plans/blue-danube-demo.md, "The coda"). A free box
/// moves on a straight line in inertial space: through its place, at its velocity relative to the station plus the
/// velocity ω × r of the station's point there. In the station's turning axes, that line bends. The flat field is the
/// textbook's other answer: a parabola over a flat floor in a field of one strength that does not turn.
/// </summary>
/// <param name="Place">The box's center at the release, in the station's axes, in meters.</param>
/// <param name="Velocity">The box's instant velocity relative to the station at the release, in its axes, in m/s.</param>
public readonly record struct ThrowRelease(Double3 Place, Double3 Velocity)
{
    /// <summary>The step of the search for a landing, in seconds.</summary>
    private const double SearchStep = 1e-3;

    /// <summary>The longest flight that the search for a landing follows, in seconds.</summary>
    private const double LongestFlight = 60;

    /// <summary>
    /// The place in the station's axes, <paramref name="time"/> seconds after the release, of a point on the straight line
    /// of the release, in a station that turns about its +Y at <paramref name="rate"/> rad/s: the line's place in the axes
    /// of the release, turned back by the station's turn since the release.
    /// </summary>
    public Double3 PlaceAt(double time, double rate)
    {
        Double3 inertial = Velocity + Double3.Cross(new(0, rate, 0), Place);
        return DQuaternion.FromAxisAngle(Double3.UnitY, -rate * time).Rotate(Place + inertial * time);
    }

    /// <summary>
    /// Where the straight line of the release brings the center of a box <paramref name="halfSize"/> meters from its
    /// faces down to <paramref name="halfSize"/> above the facet of <paramref name="floor"/> under it, in a station that
    /// turns at <paramref name="rate"/> rad/s: the first such moment, to 1e-12 s. A box whose faces lie flat on that facet
    /// touches it then. Throws <see cref="InvalidOperationException"/> for a line that does not come down within 60 s.
    /// </summary>
    public ThrowLanding Landing(double rate, WheelFloor floor, double halfSize)
    {
        ThrowRelease release = this;
        double Above(double time) => floor.Height(release.PlaceAt(time, rate)) - halfSize;
        double after = 0;
        while (Above(after) > 0)
        {
            after += SearchStep;
            if (after > LongestFlight)
                throw new InvalidOperationException(
                    $"The line of the release at {Place} does not come down to the floor within {LongestFlight} s."
                );
        }

        double before = Math.Max(0, after - SearchStep);
        while (after - before > 1e-12)
        {
            double middle = (before + after) / 2;
            if (Above(middle) > 0)
                before = middle;
            else
                after = middle;
        }

        return new(after, PlaceAt(after, rate));
    }

    /// <summary>
    /// Where the same release lands over a flat floor in a field of <paramref name="gravity"/> m/s² that does not turn: the
    /// floor is the plane of the facet of <paramref name="floor"/> under the release, and the box lands when its center comes
    /// down to <paramref name="halfSize"/> above it. Returns the time of the landing, in seconds, and how far spinward of the
    /// release it lies along the floor, in meters.
    /// </summary>
    public (double Time, double Spinward) FlatLanding(double gravity, WheelFloor floor, double halfSize)
    {
        Double3 up = -floor.Normal(Place),
            spinward = WheelFloor.SpinwardAt(Place);
        double rise = Double3.Dot(Velocity, up),
            height = floor.Height(Place) - halfSize;
        double time = (rise + Math.Sqrt(rise * rise + 2 * gravity * height)) / gravity;
        return (time, Double3.Dot(Velocity, spinward) * time);
    }
}
