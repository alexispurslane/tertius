using Tertius.Simulation;

namespace Tertius.Demo;

/// <summary>
/// Records the flight of its own thrown box in a station that turns at a steady rate: the release, and where the box came
/// down to the floor (docs/plans/blue-danube-demo.md, "The coda", "The measure of a landing"). It reads only its own
/// entity, in the station's frame, in every tick. Its entity needs a body.
/// <list type="bullet">
/// <item>The release is the box's place and instant velocity in its first tick, which reads the state of its spawn.</item>
/// <item>A place is free when it lies within <see cref="FreeBound"/> of the place that the straight line in inertial space
/// through the two places before it gives, in the station's turning axes. The two first places are free.</item>
/// <item>The landing is where the straight line through the last two free places above the floor brings the box's center
/// down to its half size above the facet of the floor under it, once a place is not free or lies at or below that height.
/// So a box that touches the floor in a step that starts more than Jolt's speculative contact distance above it, and that
/// sinks a few centimeters into the floor in that step, still lands where its free flight met the floor. A line that
/// does not come down within two steps of its last free place means that something else stopped the box first: then the
/// box has no landing, and the record ends.</item>
/// </list>
/// A dead station's frame stops the record where it is.
/// </summary>
/// <param name="station">The station's frame.</param>
/// <param name="floor">The station's floor, in its axes.</param>
/// <param name="rate">The station's steady spin rate about its +Y, in rad/s.</param>
/// <param name="halfSize">Half the box's size, in meters: its center lies this far above a facet that its face lies flat on.</param>
public sealed class FlightRecorder(FrameHandle station, WheelFloor floor, double rate, double halfSize) : IComponent
{
    /// <summary>How far a free place may lie from the straight line of the two places before it, in meters.</summary>
    public const double FreeBound = 1e-4;

    private readonly List<Double3> places = [];

    // Whether the record is over: the box landed, or something else stopped it.
    private bool over;

    /// <summary>The box's release, from its first tick on.</summary>
    public ThrowRelease? Release { get; private set; }

    /// <summary>Where and when the box came down to the floor, from the tick after its landing on.</summary>
    public ThrowLanding? Landing { get; private set; }

    public int Priority => 0;

    public EventMask Wants => EventMask.Of<Tick>();

    public void Handle(Self self, scoped ref EventList events, scoped ref Outbox outbox)
    {
        if (over || !self.Snapshot.Frames.IsAlive(station))
            return;
        Double3 place = self.PoseIn(station).Position;
        double dt = self.DeltaTime;
        Release ??= new(place, self.VelocityIn(station));
        int n = places.Count;
        bool free = n < 2 ||
                    Double3.Distance(place, Extend(before: places[n - 2], last: places[n - 1], dt: dt, time: dt)) <=
                    FreeBound;
        bool above = floor.Height(place) > halfSize;
        places.Add(place);
        if (free && above)
            return;
        over = true;
        // The places before this one are free and above the floor. The line through the last two, from the last one, for
        // up to two steps.
        if (n < 2)
            return;
        Double3 before = places[n - 2], last = places[n - 1];
        double low = 0, high = 2 * dt;
        if (floor.Height(Extend(before: before, last: last, dt: dt, time: high)) > halfSize)
            return;
        for (int i = 0; i < 60; i++)
        {
            double middle = (low + high) / 2;
            if (floor.Height(Extend(before: before, last: last, dt: dt, time: middle)) > halfSize)
                low = middle;
            else
                high = middle;
        }

        Landing = new ThrowLanding((n - 1) * dt + high, Extend(before: before, last: last, dt: dt, time: high));
    }

    /// <summary>
    /// The place in the station's axes, <paramref name="time"/> seconds after <paramref name="last"/>, of the straight line in
    /// inertial space through <paramref name="before"/> and <paramref name="last"/>, two places <paramref name="dt"/>
    /// seconds apart, each in the station's axes at its own moment. The station turns by ω dt between them, so in the axes
    /// of the last, the place before lies turned back by that angle.
    /// </summary>
    private Double3 Extend(Double3 before, Double3 last, double dt, double time)
    {
        Double3 velocity = (last - DQuaternion.FromAxisAngle(Double3.UnitY, -rate * dt).Rotate(before)) / dt;
        return DQuaternion.FromAxisAngle(Double3.UnitY, -rate * time).Rotate(last + velocity * time);
    }
}
