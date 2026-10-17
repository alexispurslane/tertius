using System.Numerics;
using AwesomeAssertions;

namespace Tertius.Core.Tests;

/// <summary>
/// How numeric frames move and what gravity their members feel: <see cref="FrameTree.BeginNumericStep"/>,
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
        earth = frames.Create(new Entity(1, 1), FrameHandle.Null, DPose.Identity, FrameBounds.Sphere(1e9f), MotionDriver.Static, Earth);

    private static Double3 X(double x) => new(x, 0, 0);

    /// <summary>The numeric frame that a numeric update gives one body of Earth at <paramref name="position"/>.</summary>
    private FrameHandle FrameFor(Double3 position, Double3 velocity)
    {
        FrameHandle home = FrameHandle.Null;
        var body = new NumericBody
        {
            Entity = new Entity(10, 1), Semantic = earth, Home = earth, Position = position, HomePosition = position,
            Velocity = velocity, Radius = 1, Mass = 1,
        };
        frames.UpdateNumericFrames([body], 0.1, new PhysicsChanges(), moves => home = moves.Single().To);
        frames.Update();
        frames.BeginNumericStep();
        return home;
    }

    [Fact]
    public void A_falling_frame_takes_the_field_at_its_origin_as_its_acceleration_for_the_step()
    {
        FrameHandle pod = FrameFor(X(OrbitRadius), new Double3(0, 0, OrbitSpeed));
        frames.HomeAcceleration(pod, earth).Should().Be(Earth.AtPrecise(X(OrbitRadius)));
        frames.EffectiveGravity(pod, Double3.Zero).Should().Be(Vector3.Zero);
    }

    [Fact]
    public void A_body_away_from_the_origin_of_a_falling_frame_feels_the_tidal_gravity_from_doubles()
    {
        FrameHandle pod = FrameFor(X(OrbitRadius), new Double3(0, 0, OrbitSpeed));
        Vector3 tidal = frames.EffectiveGravity(pod, X(100));
        tidal.Should().Be((Earth.AtPrecise(X(OrbitRadius + 100)) - Earth.AtPrecise(X(OrbitRadius))).ToVector3());
        // About 2·μ/r³ per meter outward: 2.6e-4 m/s² at 100 m. A difference of two floats near 8.7 m/s² steps by 9.5e-7.
        tidal.X.Should().BeApproximately((float)(-2 * EarthMu / Math.Pow(OrbitRadius, 3) * -100), 1e-7f);
    }

    [Fact]
    public void The_semantic_gravity_in_a_numeric_frame_is_the_field_at_the_bodys_true_position()
    {
        FrameHandle pod = FrameFor(X(OrbitRadius), new Double3(0, 0, OrbitSpeed));
        frames.SemanticGravity(pod, X(100)).Should().Be(Earth.AtPrecise(X(OrbitRadius + 100)).ToVector3());
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
        frames.EffectiveGravity(raft, X(100)).Should().Be(Earth.AtPrecise(X(RestRadius + 100)).ToVector3());
    }
}
