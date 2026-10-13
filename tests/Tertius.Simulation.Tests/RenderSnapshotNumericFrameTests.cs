using System.Numerics;
using AwesomeAssertions;
using Tertius.Physics;

namespace Tertius.Simulation.Tests;

/// <summary>
/// The render snapshot when numeric frames come and go under the camera (docs/plans/numeric-frames.md, "What gameplay
/// never sees"): the camera's motion between two builds stays smooth.
/// </summary>
public class RenderSnapshotNumericFrameTests
{
    private static readonly BodyDesc Crate = new(new BoxShapeDesc(new Vector3(0.5f)), Mass: 1_000, LinearDamping: 0, AngularDamping: 0);

    [Fact]
    public void TheCameraKeepsItsMotionInTheBuildWhereAMergeTakesAwayItsNumericFrame()
    {
        using var test = new TestWorld(new JoltPhysicsBackend(), configure: options => options.DisposePhysics = true);
        var heavyVelocity = new Double3(0, 0, 60);
        test.World.Spawn(new SpawnDesc { Frame = test.Root, Pose = DPose.Identity, LinearVelocity = heavyVelocity, Body = Crate });
        var velocity = new Double3(-5, 0, 60);
        Entity camera = test.World.Spawn(new SpawnDesc
        {
            Frame = test.Root,
            Pose = new DPose(new Double3(30, 0, 0), DQuaternion.Identity),
            LinearVelocity = velocity,
            Body = Crate with { Mass = 1 },
        });
        test.World.Camera = camera;
        test.Run(1);
        FrameHandle first = test.World.PhysicsHomeOf(camera);
        test.World.Frames.IsNumeric(first).Should().BeTrue("the camera's body is fast, so it falls in a numeric frame");

        // The camera closes in at 5 m/s. About 12 m out, the frame of the heavier crate takes it in, and its own frame goes.
        for (int ticks = 0; ticks < 600 && test.World.PhysicsHomeOf(camera) == first; ticks++)
            test.Run(1);
        test.World.Frames.IsAlive(first).Should().BeFalse();

        // The snapshot places everything relative to the camera's home, which moves with the heavier crate, so the camera
        // moved by its velocity relative to that crate over the tick.
        RenderSnapshot snapshot = test.World.Latest();
        Vector3 moved = ((velocity - heavyVelocity) * test.World.DeltaTime).ToVector3();
        (snapshot.Camera.PreviousPosition + moved).Length().Should().BeLessThan(1e-4f,
            $"the camera came from {snapshot.Camera.PreviousPosition}, and it moved by {moved}");
    }
}
