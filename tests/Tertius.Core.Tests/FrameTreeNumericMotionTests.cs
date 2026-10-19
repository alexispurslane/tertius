using System.Numerics;
using AwesomeAssertions;

namespace Tertius.Core.Tests;

/// <summary>
/// How numeric frames move and what gravity their members feel: <see cref="FrameTree.BeginStep"/>,
/// <see cref="FrameTree.AdvanceNumericFrames"/>, <see cref="FrameTree.HomeOffsetAfter"/>,
/// <see cref="FrameTree.EffectiveGravity"/> and <see cref="FrameTree.SemanticGravity"/>, around Earth, with frames that a
/// numeric update made.
/// </summary>
public class FrameTreeNumericMotionTests
{
    private const double EarthMu = 3.986004418e14;
    private const double OrbitRadius = 6.771e6;
    private const double OrbitSpeed = 7_672.6;

    /// <summary>
    /// A distance from Earth's center of 10,000 km, in meters: past 2^23 m (about 8,389 km), the distance from which a slow
    /// body gets a resting frame.
    /// </summary>
    private const double RestRadius = 1e7;
    private const double Dt = 1.0 / 60;
    private static readonly GravityField Earth = GravityField.Point(EarthMu);

    private readonly FrameTree frames = new();
    private readonly FrameHandle earth;

    public FrameTreeNumericMotionTests() =>
        earth = frames.Create(new Entity(1, 1), FrameHandle.Null, DPose.Identity, FrameBounds.Sphere(1e9f), Earth);

    private static Double3 X(double x) => new(x, 0, 0);

    /// <summary>The numeric frame that a numeric update gives one body of Earth at <paramref name="position"/>.</summary>
    private FrameHandle FrameFor(Double3 position, Double3 velocity) => FrameFor(earth, position, velocity);

    /// <summary>The numeric frame that a numeric update gives one body of <paramref name="semantic"/> at <paramref name="position"/>.</summary>
    private FrameHandle FrameFor(FrameHandle semantic, Double3 position, Double3 velocity)
    {
        FrameHandle home = FrameHandle.Null;
        var body = new NumericBody
        {
            Entity = new Entity(10, 1), Semantic = semantic, Home = semantic, Position = position, HomePosition = position,
            Velocity = velocity, Radius = 1, Mass = 1,
        };
        frames.UpdateNumericFrames([body], 0.1, 0.1, new PhysicsChanges(), moves => home = moves.Single().To);
        frames.Update();
        frames.BeginStep([], 1.0 / 60, lastStepFailed: false);
        return home;
    }

    [Fact]
    public void A_falling_frame_takes_the_field_at_its_origin_as_its_acceleration_for_the_step()
    {
        FrameHandle pod = FrameFor(X(OrbitRadius), new Double3(0, 0, OrbitSpeed));
        frames.HomeAcceleration(pod, earth).Should().Be(Earth.AtPrecise(X(OrbitRadius)));
        frames.EffectiveGravity(pod, Double3.Zero).Should().Be(Double3.Zero);
    }

    [Fact]
    public void A_falling_frame_accelerates_relative_to_its_semantic_frame_by_its_step_acceleration()
    {
        FrameHandle pod = FrameFor(X(OrbitRadius), new Double3(0, 0, OrbitSpeed));
        frames.AccelerationRelative(pod, earth).Should().Be(Earth.AtPrecise(X(OrbitRadius)));
    }

    [Fact]
    public void The_semantic_frame_accelerates_relative_to_a_falling_frame_against_the_frames_step_acceleration()
    {
        FrameHandle pod = FrameFor(X(OrbitRadius), new Double3(0, 0, OrbitSpeed));
        frames.AccelerationRelative(earth, pod).Should().Be(-Earth.AtPrecise(X(OrbitRadius)));
    }

    [Fact]
    public void A_falling_frame_in_a_turned_frame_accelerates_relative_to_the_turned_frames_parent_by_its_step_acceleration_in_the_parents_axes()
    {
        // A frame at Earth's origin, turned by 0.9 rad about Z, with Earth's field in its own axes. A fast body in it gets a
        // falling frame, which keeps the field at its origin, in the turned frame's axes, as its step acceleration.
        DQuaternion turn = DQuaternion.FromAxisAngle(Double3.UnitZ, 0.9);
        FrameHandle turned = frames.Create(new Entity(2, 1), earth, new DPose(Double3.Zero, turn), FrameBounds.Sphere(1e9f), Earth);
        FrameHandle pod = FrameFor(turned, X(OrbitRadius), new Double3(0, 0, OrbitSpeed));
        frames.TreeParent(pod).Should().Be(turned);

        Double3 expected = turn.Rotate(Earth.AtPrecise(X(OrbitRadius)));
        (frames.AccelerationRelative(pod, earth) - expected).Length.Should().BeLessThan(1e-12);
    }

    // Step 2 of every sync point converts the record of each frame in a numeric home into its semantic parent, on the serial
    // sync point, with the acceleration and the angular velocity of the home relative to that parent. The walk only goes up.

    [Fact]
    public void The_acceleration_of_a_falling_frame_relative_to_its_semantic_frame_allocates_nothing()
    {
        FrameHandle pod = FrameFor(X(OrbitRadius), new Double3(0, 0, OrbitSpeed));
        frames.AccelerationRelative(pod, earth);

        long before = GC.GetAllocatedBytesForCurrentThread();
        frames.AccelerationRelative(pod, earth);
        (GC.GetAllocatedBytesForCurrentThread() - before).Should().Be(0);
    }

    [Fact]
    public void The_angular_velocity_of_a_falling_frame_relative_to_its_semantic_frame_allocates_nothing()
    {
        FrameHandle pod = FrameFor(X(OrbitRadius), new Double3(0, 0, OrbitSpeed));
        frames.AngularVelocityRelative(pod, earth);

        long before = GC.GetAllocatedBytesForCurrentThread();
        frames.AngularVelocityRelative(pod, earth);
        (GC.GetAllocatedBytesForCurrentThread() - before).Should().Be(0);
    }

    [Fact]
    public void A_body_away_from_the_origin_of_a_falling_frame_feels_the_tidal_gravity_from_doubles()
    {
        FrameHandle pod = FrameFor(X(OrbitRadius), new Double3(0, 0, OrbitSpeed));
        Double3 tidal = frames.EffectiveGravity(pod, X(100));
        tidal.Should().Be(Earth.AtPrecise(X(OrbitRadius + 100)) - Earth.AtPrecise(X(OrbitRadius)));
        // About 2·μ/r³ per meter outward: 2.6e-4 m/s² at 100 m. The read stays in doubles, so the reader that needs a float
        // narrows only the difference, and a tidal gravity of a fraction of a mm/s² keeps its digits.
        tidal.X.Should().BeApproximately(-2 * EarthMu / Math.Pow(OrbitRadius, 3) * -100, 1e-7);
    }

    [Fact]
    public void The_semantic_gravity_in_a_numeric_frame_is_the_field_at_the_bodys_true_position()
    {
        FrameHandle pod = FrameFor(X(OrbitRadius), new Double3(0, 0, OrbitSpeed));
        frames.SemanticGravity(pod, X(100)).Should().Be(Earth.AtPrecise(X(OrbitRadius + 100)));
        frames.SemanticGravity(earth, X(OrbitRadius)).Should().Be(frames.EffectiveGravity(earth, X(OrbitRadius)));
    }

    [Fact]
    public void A_falling_frame_moves_with_the_scheme_that_jolt_uses_for_bodies()
    {
        FrameHandle pod = FrameFor(X(OrbitRadius), new Double3(0, 0, OrbitSpeed));
        Double3 velocity = new Double3(0, 0, OrbitSpeed) + Earth.AtPrecise(X(OrbitRadius)) * Dt;
        Double3 position = X(OrbitRadius) + velocity * Dt;

        frames.AdvanceNumericFrames(Dt);

        frames.TreeLinearVelocity(pod).Should().Be(velocity);
        frames.TreeLocalPose(pod).Position.Should().Be(position);
    }

    [Fact]
    public void The_offset_after_a_step_is_where_the_step_puts_the_frame()
    {
        FrameHandle pod = FrameFor(X(OrbitRadius), new Double3(0, 0, OrbitSpeed));
        Double3 predicted = frames.HomeOffsetAfter(pod, earth, Dt);
        Double3 velocity = frames.HomeVelocityAfter(pod, earth, Dt);
        frames.AdvanceNumericFrames(Dt);
        frames.HomeOffset(pod, earth).Should().Be(predicted);
        frames.HomeVelocity(pod, earth).Should().Be(velocity);
        frames.OffsetAfter(earth, pod, 0).Should().Be(-frames.HomeOffset(pod, earth));
    }

    [Fact]
    public void A_resting_frame_does_not_move_and_its_members_feel_the_whole_field()
    {
        FrameHandle raft = FrameFor(X(RestRadius), Double3.Zero);
        frames.Numeric(raft).Should().Be(NumericMode.Resting);
        frames.AdvanceNumericFrames(Dt);
        frames.TreeLocalPose(raft).Position.Should().Be(X(RestRadius));
        frames.EffectiveGravity(raft, X(100)).Should().Be(Earth.AtPrecise(X(RestRadius + 100)));
    }
}
