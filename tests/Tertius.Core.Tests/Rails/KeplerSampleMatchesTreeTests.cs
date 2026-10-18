using AwesomeAssertions;
using Tertius.Frames.Rails;

namespace Tertius.Core.Tests;

/// <summary>
/// What the frame tree computes for a Kepler frame today, against the rails library. <see cref="FrameTree.KeplerMotion"/>,
/// which physics reads, calls <see cref="KeplerOrbit.Sample"/> itself. So these tests compare it with the separate
/// functions <see cref="KeplerOrbit.StateAt"/>, <see cref="KeplerOrbit.RotationAt"/> and
/// <see cref="KeplerOrbit.AngularVelocityAt"/>, which are what it computed before the sample existed: a fault in the
/// sample's math cannot pass both. <see cref="FrameTree.PlaceOnOrbit"/> and <see cref="FrameTree.BeginNumericStep"/>, which
/// move an orbit frame, compute their own values, and these tests compare those with the sample. They pin the library to
/// the tree until rails components replace the tree's versions (docs/plans/rails-components.md). The tree is built in the
/// test, with a step of 1/60 s, round an earth whose point gravity the orbits take their G·M from.
/// </summary>
public class KeplerSampleMatchesTreeTests
{
    private const double EarthMu = 3.986004418e14;
    private const double Step = 1.0 / 60;

    /// <summary>The ends of steps at which the tests compare, in seconds.</summary>
    private static readonly double[] Times = [100, 1334.56, 33_000.25];

    /// <summary>An eccentric, inclined orbit from 7,000 km to 13,000 km, with a tilted spin for the attitude <see cref="KeplerAttitude.Spin"/>.</summary>
    private static readonly KeplerOrbit Oval =
        new(1e7, 0.3, 0.5, 0.7, 1.1, 0.2, 100, DQuaternion.FromAxisAngle(Double3.UnitZ, -0.4), 7.3e-5, 0.6, KeplerAttitude.FacesParent);

    /// <summary>A station in low orbit that faces earth. It moves fast enough for an orbit frame of its own.</summary>
    private static readonly KeplerOrbit Low = KeplerOrbit.Circular(6.8e6, phase: 0.4) with { Inclination = 0.5, Attitude = KeplerAttitude.FacesParent };

    private readonly FrameTree frames = new() { StepLength = Step };
    private readonly FrameHandle earth;

    public KeplerSampleMatchesTreeTests() =>
        earth = frames.Create(new Entity(1, 1), FrameHandle.Null, DPose.Identity, FrameBounds.Sphere(1e9f), MotionDriver.Static,
            GravityField.Point(EarthMu));

    /// <summary>A Kepler frame in earth on <paramref name="orbit"/>, at its place at <paramref name="time"/>.</summary>
    private FrameHandle Station(KeplerOrbit orbit, double time)
    {
        FrameHandle frame = frames.Create(new Entity(10, 1), earth, DPose.Identity, FrameBounds.Sphere(40), MotionDriver.Kepler, kepler: orbit);
        frames.PlaceOnOrbit(frame, time);
        return frame;
    }

    /// <summary>
    /// Gives <paramref name="station"/> an orbit frame: one numeric update with its hull, a fixed body of 5 m that orbits,
    /// where the tree holds it now. Returns the orbit frame.
    /// </summary>
    private FrameHandle OrbitFrameOf(FrameHandle station)
    {
        var hull = new NumericBody
        {
            Entity = station.Entity,
            Semantic = earth,
            Home = earth,
            Position = frames.LocalPose(station).Position,
            HomePosition = frames.TreeLocalPose(station).Position,
            Velocity = frames.LinearVelocity(station),
            Radius = 5,
            Mass = 1e5,
            Fixed = true,
            Orbits = true,
            OwnedFrame = station,
        };
        frames.UpdateNumericFrames([hull], Step, new PhysicsChanges(), _ => { });
        frames.InOrbitFrame(station).Should().BeTrue("a hull at orbital speed gets an orbit frame");
        return frames.TreeParent(station);
    }

    [Theory]
    [InlineData(KeplerAttitude.Spin)]
    [InlineData(KeplerAttitude.FacesParent)]
    public void The_tree_moves_a_Kepler_frame_in_its_semantic_parent_along_the_state_rotation_and_angular_velocity_of_its_orbit(KeplerAttitude attitude)
    {
        KeplerOrbit orbit = Oval with { Attitude = attitude };
        FrameHandle station = Station(orbit, Times[0]);

        foreach (double time in Times)
        {
            (Double3 position, Double3 velocity) = orbit.StateAt(time, EarthMu);

            frames.KeplerMotion(station, time).Should().Be(
                (new DPose(position, orbit.RotationAt(time, EarthMu)), velocity, orbit.AngularVelocityAt(time, EarthMu)), $"at {time} s");
        }
    }

    [Fact]
    public void The_tree_rests_a_Kepler_frame_at_the_origin_of_its_orbit_frame_with_the_rotation_and_angular_velocity_of_its_orbit()
    {
        FrameHandle station = Station(Low, Times[0]);
        OrbitFrameOf(station);

        foreach (double time in Times)
            frames.KeplerMotion(station, time).Should().Be(
                (new DPose(Double3.Zero, Low.RotationAt(time, EarthMu)), Double3.Zero, Low.AngularVelocityAt(time, EarthMu)), $"at {time} s");
    }

    [Fact]
    public void An_orbit_frame_takes_the_place_and_the_chord_of_the_sample_of_the_step_that_ends()
    {
        FrameHandle station = Station(Low, Times[0]);
        FrameHandle orbitFrame = OrbitFrameOf(station);

        foreach (double time in Times)
        {
            frames.PlaceOnOrbit(station, time);

            MotionSample sample = Low.Sample(EarthMu, time, Step);
            frames.TreeLocalPose(orbitFrame).Position.Should().Be(sample.Pose.Position, $"at {time} s");
            frames.LinearVelocity(orbitFrame).Should().Be(sample.ChordVelocity, $"at {time} s");
        }
    }

    [Fact]
    public void During_a_step_an_orbit_frame_falls_with_the_acceleration_of_the_sample_of_that_step()
    {
        FrameHandle station = Station(Low, Times[0]);
        FrameHandle orbitFrame = OrbitFrameOf(station);

        foreach (double time in Times)
        {
            frames.PlaceOnOrbit(station, time);
            frames.Update();
            frames.BeginNumericStep();

            // The tree takes the pull at the orbit's place now, where the step starts. The sample of the step that starts
            // now finds that place as its end less its chord times the step. The two places differ by the chord's terms of
            // the fourth order, under 1e-13 m, and by rounding: the end time rounds by up to 3.6e-12 s at 33,000 s, which
            // moves the place by up to 3e-8 m at 7.7 km/s, and each place rounds its mean anomaly by up to 1.8e-15 rad,
            // 1.2e-8 m at 6,800 km. The pull changes by its gradient, 2 GM / r³ = 2.5e-6 /s², times that, under 2e-13 m/s².
            // The pull of 8.6 m/s² itself rounds to about 2e-15 m/s². The pull at the end of the step is 1.6e-4 m/s² off.
            MotionSample sample = Low.Sample(EarthMu, time + Step, Step);
            (frames.HomeAcceleration(orbitFrame, earth) - sample.Acceleration).Length.Should().BeLessThan(1e-12, $"at {time} s");
        }
    }
}
