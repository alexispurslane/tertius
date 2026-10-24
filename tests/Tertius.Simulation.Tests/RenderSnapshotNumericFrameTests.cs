using AwesomeAssertions;
using Tertius.Physics;

namespace Tertius.Simulation.Tests;

/// <summary>
/// The render snapshot when numeric frames come and go under the camera (docs/plans/numeric-frames.md, "What gameplay
/// never sees"): the camera's motion between two builds stays smooth.
/// </summary>
public sealed class RenderSnapshotNumericFrameTests
{
    private const float HeavyMass = 1_000;

    private const float CameraMass = 1;

    /// <summary>The ticks of thrust after which a body thrusts: 0.5 s at 60 Hz (<see cref="NumericFrameOptions.ThrustOnset"/>
    /// ).</summary>
    private const int OnsetTicks = 30;

    private static readonly BodyDesc Crate = new(
        Shape: new BoxShapeDesc(new(0.5f)),
        Mass: HeavyMass,
        LinearDamping: 0,
        AngularDamping: 0
    );

    /// <summary>
    /// Two crates drift side by side at 60 m/s, the camera's light one 30 m from the heavy one and closing in at 5 m/s. Each
    /// pushes itself with a gentle thrust (<see cref="Thruster"/>), so that from its thirtieth tick each falls in a numeric
    /// frame of its own, above the 50 m/s from which a group that thrusts falls. Both thrusts give the same acceleration, so
    /// they leave the camera's velocity relative to the heavy crate as it is.
    /// </summary>
    [Fact]
    public void TheCameraKeepsItsMotionInTheBuildWhereAMergeTakesAwayItsNumericFrame()
    {
        using var test = new TestWorld(
            physics: new JoltPhysicsBackend(JoltBackendOptions.Unpaced),
            configure: options => options.DisposePhysics = true
        );
        var heavyVelocity = new Double3(0, 0, 60);
        Entity heavy = test.World.Spawn(
            new()
            {
                Frame = test.Root,
                Pose = DPose.Identity,
                LinearVelocity = heavyVelocity,
                Body = Crate,
                Components = [new Thruster(HeavyMass)],
            }
        );
        var velocity = new Double3(-5, 0, 60);
        Entity camera = test.World.Spawn(
            new()
            {
                Frame = test.Root,
                Pose = new(new(30, 0, 0), DQuaternion.Identity),
                LinearVelocity = velocity,
                Body = Crate with { Mass = CameraMass },
                Components = [new Thruster(CameraMass)],
            }
        );
        test.World.Camera = camera;
        test.Run(OnsetTicks);
        FrameHandle first = test.World.PhysicsHomeOf(camera);
        test.World.Frames.Numeric(first)
            .Should()
            .Be(NumericMode.Falling, "the camera's body thrusts at 60 m/s, so it falls in a numeric frame");
        test.World.PhysicsHomeOf(heavy)
            .Should()
            .NotBe(first, "27.5 m apart, the two crates fall in frames of their own");

        // The camera closes in at 5 m/s. About 12 m out, the frame of the heavier crate takes it in, and its own frame goes.
        for (int ticks = 0; ticks < 600 && test.World.PhysicsHomeOf(camera) == first; ticks++)
            test.Run(1);
        test.World.Frames.IsAlive(first).Should().BeFalse();
        FrameHandle merged = test.World.PhysicsHomeOf(camera);
        merged.Should().Be(test.World.PhysicsHomeOf(heavy), "the frame of the heavier crate took the camera in");
        test.World.Frames.Numeric(merged).Should().Be(NumericMode.Falling);

        // The snapshot places everything relative to the camera's home, which moves with the heavier crate, so the camera
        // moved by its velocity relative to that crate over the tick.
        RenderSnapshot snapshot = test.World.Latest();
        var moved = ((velocity - heavyVelocity) * test.World.DeltaTime).ToVector3();
        (snapshot.Camera.PreviousPosition + moved)
            .Length()
            .Should()
            .BeLessThan(1e-4f, $"the camera came from {snapshot.Camera.PreviousPosition}, and it moved by {moved}");
    }
}
