using System.Numerics;

namespace Tertius.Physics.Tests;

/// <summary>
/// The harness stands in for the sync point, so the physics tests are only as strict as the harness. These tests pin
/// down how it treats a hand-off that a test refuses on purpose, and that it throws out of the step for an output that
/// only a bug in the backend produces, so that a physics test with such a bug fails.
/// </summary>
public class PhysicsHarnessTests
{
    /// <summary>
    /// A ship frame that physics drives, 10 m along +X in space, with a cockpit frame inside it, a ball in space, and a
    /// bubble frame that is already gone from the tree.
    /// </summary>
    private sealed class Scene : IDisposable
    {
        public Scene()
        {
            H = new PhysicsHarness();
            Space = H.AddFrame(FrameHandle.Null, DPose.Identity, FrameBounds.Sphere(1e6f), MotionDriver.Static);
            Ship = H.AddFrameWithBody(Space, Shapes.At(10, 0, 0), FrameBounds.Sphere(30), MotionDriver.Physics, Shapes.Box(3, mass: 100));
            Cockpit = H.AddFrame(Ship, Shapes.At(1, 0, 0), FrameBounds.Sphere(2), MotionDriver.Static);
            Ball = H.AddBody(Space, Shapes.Ball(0.5f), Shapes.At(-100, 0, 0));
            Gone = H.AddFrame(Space, Shapes.At(-500, 0, 0), FrameBounds.Sphere(5), MotionDriver.Static);
            H.Step();
            H.Frames.Destroy(Gone);
            H.Changes.FramesDestroyed.Add(Gone);
        }

        public PhysicsHarness H { get; }
        public FrameHandle Space { get; }
        public FrameHandle Ship { get; }
        public FrameHandle Cockpit { get; }
        public Entity Ball { get; }
        public FrameHandle Gone { get; }

        public void Dispose() => H.Dispose();
    }

    [Fact]
    public void AHandOffThatTheFrameTreeRefusesThrowsOutOfTheStep()
    {
        using var scene = new Scene();
        scene.H.BeforeApply = output =>
            output.HandOffs.Add(new HandOff(scene.Ship.Entity, scene.Space, scene.Cockpit, DPose.Identity, Vector3.Zero, Vector3.Zero));

        Action step = () => scene.H.Step();

        step.Should().Throw<InvalidOperationException>().WithMessage($"*{scene.Ship}*");
        scene.H.Frames.Parent(scene.Ship).Should().Be(scene.Space);
    }

    [Fact]
    public void ABodyStateInAFrameThatIsGoneThrowsOutOfTheStep()
    {
        using var scene = new Scene();
        scene.H.BeforeApply = output =>
            output.Bodies.Add(new BodyState(scene.Ball, scene.Gone, DPose.Identity, Vector3.Zero, Vector3.Zero));

        Action step = () => scene.H.Step();

        step.Should().Throw<InvalidOperationException>().WithMessage($"*body state of {scene.Ball}*{scene.Gone}*");
    }

    [Fact]
    public void APlainBodysHandOffToAFrameThatIsGoneThrowsOutOfTheStep()
    {
        using var scene = new Scene();
        scene.H.BeforeApply = output =>
            output.HandOffs.Add(new HandOff(scene.Ball, scene.Space, scene.Gone, DPose.Identity, Vector3.Zero, Vector3.Zero));

        Action step = () => scene.H.Step();

        step.Should().Throw<InvalidOperationException>().WithMessage($"*hand-off of {scene.Ball}*{scene.Gone}*");
    }

    [Fact]
    public void AnEntityWhoseHandOffATestRefusesKeepsTheStateItHadBeforeTheStep()
    {
        using var h = new PhysicsHarness();
        FrameHandle space = h.AddFrame(FrameHandle.Null, DPose.Identity, FrameBounds.Sphere(1e7f), MotionDriver.Static);
        // A carrier bubble 1 km from the origin of space, and a fighter that flies into it.
        FrameHandle carrier = h.AddFrame(space, Shapes.At(1000, 0, 0), FrameBounds.Sphere(200), MotionDriver.Static);
        FrameHandle fighter = h.AddFrameWithBody(space, Shapes.At(1260, 0, 0), FrameBounds.Sphere(10), MotionDriver.Physics,
            Shapes.Box(2, mass: 1000), velocity: new Vector3(-60, 0, 0));
        h.RefusesHandOff = handOff => handOff.Entity == fighter.Entity;

        BodyState stateBefore = default;
        DPose poseBefore = default;
        Double3 velocityBefore = default;
        h.StepUntil(() =>
        {
            if (h.RefusedHandOffs.Count > 0)
                return true;
            stateBefore = h[fighter.Entity];
            poseBefore = h.Frames.LocalPose(fighter);
            velocityBefore = h.Frames.LinearVelocity(fighter);
            return false;
        }, 120);

        h.RefusedHandOffs.Should().ContainSingle().Which.To.Should().Be(carrier);
        h[fighter.Entity].Should().Be(stateBefore, "the harness skips the body state that came with the refused hand-off");
        h.Frames.Parent(fighter).Should().Be(space);
        h.Frames.LocalPose(fighter).Should().Be(poseBefore, "the harness skips the frame state that came with the refused hand-off");
        h.Frames.LinearVelocity(fighter).Should().Be(velocityBefore);
    }
}
