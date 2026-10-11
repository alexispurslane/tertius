using System.Numerics;

namespace Tertius.Physics.Tests;

/// <summary>
/// The harness stands in for the sync point, so the physics tests are only as strict as the harness. It runs the same
/// <see cref="PhysicsOutputCheck"/> as the sync point. These tests pin down that it throws out of the step for each output
/// that only a bug in the backend produces, so that a physics test with such a bug fails, and how it treats a broken
/// hand-off that a test refuses on purpose.
/// </summary>
public class PhysicsHarnessTests
{
    /// <summary>
    /// A ship frame that physics drives, 10 m along +X in space, with a cockpit frame inside it, a ball in space, a frame
    /// with the physics driver whose owner has no body, a second root frame whose owner has a body, a third root frame with
    /// the physics driver whose owner has a body, a dock frame with a static hull and a moon frame with the Kepler driver
    /// and a kinematic hull, both in space, a bubble frame that is gone from the tree, and a wreck whose body is gone.
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
            Wreck = H.AddBody(Space, Shapes.Ball(0.5f), Shapes.At(-200, 0, 0));
            Drifter = H.AddFrame(Space, Shapes.At(300, 0, 0), FrameBounds.Sphere(5), MotionDriver.Physics);
            Island = H.AddFrameWithBody(FrameHandle.Null, DPose.Identity, FrameBounds.Sphere(10), MotionDriver.Static,
                new BodyDesc(new SphereShapeDesc(5), BodyMotion.Static));
            Raft = H.AddFrameWithBody(FrameHandle.Null, DPose.Identity, FrameBounds.Sphere(10), MotionDriver.Physics, Shapes.Box(2, mass: 100));
            Dock = H.AddFrameWithBody(Space, Shapes.At(0, -200, 0), FrameBounds.Sphere(5), MotionDriver.Static,
                new BodyDesc(new BoxShapeDesc(new Vector3(1)), BodyMotion.Static));
            Moon = H.AddFrameWithBody(Space, Shapes.At(0, 200, 0), FrameBounds.Sphere(5), MotionDriver.Kepler, Shapes.Box(1, mass: 10));
            Gone = H.AddFrame(Space, Shapes.At(-500, 0, 0), FrameBounds.Sphere(5), MotionDriver.Static);
            H.Step();
            H.Frames.Destroy(Gone);
            H.Changes.FramesDestroyed.Add(Gone);
            H.Destroy(Wreck);
        }

        public PhysicsHarness H { get; }
        public FrameHandle Space { get; }
        public FrameHandle Ship { get; }
        public FrameHandle Cockpit { get; }
        public Entity Ball { get; }
        public Entity Wreck { get; }
        public FrameHandle Drifter { get; }
        public FrameHandle Island { get; }
        public FrameHandle Raft { get; }
        public FrameHandle Dock { get; }
        public FrameHandle Moon { get; }
        public FrameHandle Gone { get; }

        /// <summary>Adds <paramref name="broken"/> to the output of the next step, which then throws out of the step.</summary>
        public Action StepWith(Action<PhysicsStepOutput> broken)
        {
            H.BeforeApply = broken;
            return () => H.Step();
        }

        public void Dispose() => H.Dispose();
    }

    private static HandOff HandOffOf(Entity entity, FrameHandle from, FrameHandle to) => new(entity, from, to, DPose.Identity, Vector3.Zero, Vector3.Zero);

    private static ContactStarted ContactOf(Entity a, Entity b, FrameHandle frame) => new(a, b, frame, Double3.Zero, Vector3.UnitX, 0);

    [Fact]
    public void AHandOffThatTheFrameTreeRefusesThrowsOutOfTheStep()
    {
        using var scene = new Scene();

        Action step = scene.StepWith(output => output.HandOffs.Add(HandOffOf(scene.Ship.Entity, scene.Space, scene.Cockpit)));

        step.Should().Throw<InvalidOperationException>().WithMessage($"*refused the hand-off of {scene.Ship.Entity}*{scene.Ship}*");
        scene.H.Frames.Parent(scene.Ship).Should().Be(scene.Space);
    }

    [Fact]
    public void ABodyStateInAFrameThatIsGoneThrowsOutOfTheStep()
    {
        using var scene = new Scene();

        Action step = scene.StepWith(output => output.Bodies.Add(new BodyState(scene.Ball, scene.Gone, DPose.Identity, Vector3.Zero, Vector3.Zero)));

        step.Should().Throw<InvalidOperationException>().WithMessage($"*body state of {scene.Ball}*{scene.Gone}*");
    }

    [Fact]
    public void ABodyStateOfAnEntityWhoseBodyIsGoneThrowsOutOfTheStepAndDoesNotBringTheBodyBack()
    {
        using var scene = new Scene();

        Action step = scene.StepWith(output => output.Bodies.Add(new BodyState(scene.Wreck, scene.Space, DPose.Identity, Vector3.Zero, Vector3.Zero)));

        step.Should().Throw<InvalidOperationException>().WithMessage($"*body state of {scene.Wreck}*no longer exists*");
        scene.H.States.Should().NotContainKey(scene.Wreck);
    }

    [Fact]
    public void ABodyStateOfAnEntityWithoutABodyThrowsOutOfTheStep()
    {
        using var scene = new Scene();

        Action step = scene.StepWith(output => output.Bodies.Add(new BodyState(scene.Space.Entity, scene.Space, DPose.Identity, Vector3.Zero, Vector3.Zero)));

        step.Should().Throw<InvalidOperationException>().WithMessage($"*body state of {scene.Space.Entity}*has no body*");
    }

    [Fact]
    public void APlainBodysHandOffToAFrameThatIsGoneThrowsOutOfTheStep()
    {
        using var scene = new Scene();

        Action step = scene.StepWith(output => output.HandOffs.Add(HandOffOf(scene.Ball, scene.Space, scene.Gone)));

        step.Should().Throw<InvalidOperationException>().WithMessage($"*hand-off of {scene.Ball}*{scene.Gone}*");
    }

    [Fact]
    public void AHandOffOfAnEntityWhoseBodyIsGoneThrowsOutOfTheStepAndDoesNotBringTheBodyBack()
    {
        using var scene = new Scene();

        Action step = scene.StepWith(output => output.HandOffs.Add(HandOffOf(scene.Wreck, scene.Space, scene.Ship)));

        step.Should().Throw<InvalidOperationException>().WithMessage($"*hand-off of {scene.Wreck}*no longer exists*");
        scene.H.States.Should().NotContainKey(scene.Wreck);
    }

    [Fact]
    public void AHandOffFromAFrameThatTheEntityIsNotInThrowsOutOfTheStep()
    {
        using var scene = new Scene();

        Action step = scene.StepWith(output => output.HandOffs.Add(HandOffOf(scene.Ball, scene.Ship, scene.Cockpit)));

        step.Should().Throw<InvalidOperationException>().WithMessage($"*hand-off of {scene.Ball}*is in {scene.Space}, not in {scene.Ship}*");
    }

    [Fact]
    public void AHandOffOfTheOwnerOfARootFrameThrowsOutOfTheStep()
    {
        using var scene = new Scene();

        Action step = scene.StepWith(output => output.HandOffs.Add(HandOffOf(scene.Island.Entity, scene.Space, scene.Ship)));

        step.Should().Throw<InvalidOperationException>().WithMessage($"*hand-off of {scene.Island.Entity}*is in no frame*");
        scene.H.Frames.Parent(scene.Island).Should().Be(FrameHandle.Null);
    }

    [Fact]
    public void AHandOffIntoTheFrameThatTheEntityIsInThrowsOutOfTheStep()
    {
        using var scene = new Scene();

        Action step = scene.StepWith(output => output.HandOffs.Add(HandOffOf(scene.Ball, scene.Space, scene.Space)));

        step.Should().Throw<InvalidOperationException>().WithMessage($"*hand-off of {scene.Ball}*which it is in already*");
    }

    [Fact]
    public void ASecondHandOffOfOneEntityInOneStepThrowsOutOfTheStep()
    {
        using var scene = new Scene();

        Action step = scene.StepWith(output =>
        {
            output.HandOffs.Add(HandOffOf(scene.Ball, scene.Space, scene.Ship));
            output.HandOffs.Add(HandOffOf(scene.Ball, scene.Space, scene.Ship));
        });

        step.Should().Throw<InvalidOperationException>().WithMessage($"*hand-off of {scene.Ball}*another hand-off in this step*");
    }

    [Fact]
    public void AFrameStateOfAFrameThatPhysicsDoesNotDriveThrowsOutOfTheStep()
    {
        using var scene = new Scene();

        Action step = scene.StepWith(output => output.Frames.Add(new FrameState(scene.Cockpit, DPose.Identity, Double3.Zero, Vector3.Zero, Vector3.Zero)));

        step.Should().Throw<InvalidOperationException>().WithMessage($"*state of {scene.Cockpit}*physics does not drive it*");
    }

    [Fact]
    public void AFrameStateOfAFrameWhoseOwnerHasNoBodyThrowsOutOfTheStep()
    {
        using var scene = new Scene();

        Action step = scene.StepWith(output => output.Frames.Add(new FrameState(scene.Drifter, DPose.Identity, Double3.Zero, Vector3.Zero, Vector3.Zero)));

        step.Should().Throw<InvalidOperationException>().WithMessage($"*state of {scene.Drifter}*has no body*");
    }

    [Fact]
    public void AFrameStateOfARootFrameThrowsOutOfTheStep()
    {
        using var scene = new Scene();

        Action step = scene.StepWith(output => output.Frames.Add(new FrameState(scene.Raft, DPose.Identity, Double3.Zero, Vector3.Zero, Vector3.Zero)));

        step.Should().Throw<InvalidOperationException>().WithMessage($"*state of {scene.Raft}*root frame*");
    }

    [Theory]
    [InlineData(MotionDriver.Static)]
    [InlineData(MotionDriver.Kepler)]
    public void AHandOffOfTheOwnerOfAFrameThatPhysicsDoesNotDriveThrowsOutOfTheStep(MotionDriver driver)
    {
        using var scene = new Scene();
        FrameHandle frame = driver == MotionDriver.Kepler ? scene.Moon : scene.Dock;

        Action step = scene.StepWith(output => output.HandOffs.Add(HandOffOf(frame.Entity, scene.Space, scene.Ship)));

        step.Should().Throw<InvalidOperationException>().WithMessage($"*hand-off of {frame.Entity}*physics does not drive {frame}*");
        scene.H.Frames.Parent(frame).Should().Be(scene.Space);
    }

    [Fact]
    public void AFrameStateOfAFrameThatIsGoneThrowsOutOfTheStep()
    {
        using var scene = new Scene();

        Action step = scene.StepWith(output => output.Frames.Add(new FrameState(scene.Gone, DPose.Identity, Double3.Zero, Vector3.Zero, Vector3.Zero)));

        step.Should().Throw<InvalidOperationException>().WithMessage($"*state of the stale frame {scene.Gone}*");
    }

    [Fact]
    public void AContactThatNamesAnEntityWhoseBodyIsGoneThrowsOutOfTheStep()
    {
        using var scene = new Scene();

        Action step = scene.StepWith(output => output.Contacts.Add(ContactOf(scene.Ball, scene.Wreck, scene.Space)));

        step.Should().Throw<InvalidOperationException>().WithMessage($"*contact of {scene.Ball} and {scene.Wreck}*no longer exists*");
    }

    [Fact]
    public void AContactThatNamesNoFrameThrowsOutOfTheStep()
    {
        using var scene = new Scene();

        Action step = scene.StepWith(output => output.Contacts.Add(ContactOf(scene.Ball, scene.Ship.Entity, FrameHandle.Null)));

        step.Should().Throw<InvalidOperationException>().WithMessage($"*contact of {scene.Ball} and {scene.Ship.Entity}*names no frame*");
    }

    [Fact]
    public void AContactOfABodyWithItselfThrowsOutOfTheStep()
    {
        using var scene = new Scene();

        Action step = scene.StepWith(output => output.Contacts.Add(ContactOf(scene.Ball, scene.Ball, scene.Space)));

        step.Should().Throw<InvalidOperationException>().WithMessage($"*contact of {scene.Ball} and {scene.Ball}*cannot touch itself*");
    }

    /// <summary>
    /// A carrier bubble 1 km from the origin of space, and a fighter that flies into it. A correct backend's hand-off of
    /// the fighter is legal, so a test that wants a refused one breaks it first.
    /// </summary>
    private static (FrameHandle Space, FrameHandle Carrier, FrameHandle Fighter) FighterApproachingCarrier(PhysicsHarness h)
    {
        FrameHandle space = h.AddFrame(FrameHandle.Null, DPose.Identity, FrameBounds.Sphere(1e7f), MotionDriver.Static);
        FrameHandle carrier = h.AddFrame(space, Shapes.At(1000, 0, 0), FrameBounds.Sphere(200), MotionDriver.Static);
        FrameHandle fighter = h.AddFrameWithBody(space, Shapes.At(1260, 0, 0), FrameBounds.Sphere(10), MotionDriver.Physics,
            Shapes.Box(2, mass: 1000), velocity: new Vector3(-60, 0, 0));
        return (space, carrier, fighter);
    }

    /// <summary>
    /// The step that hands the fighter into the carrier reports it twice: in the hand-off, and in a body state in the
    /// carrier. The sync point lands body states first and hand-offs after them, so the entity keeps the state of its
    /// hand-off. The harness keeps the same one. The test moves the body state by 1 m, so that the two states differ by
    /// more than rounding.
    /// </summary>
    [Fact]
    public void AnEntityWithABodyStateAndAHandOffInOneStepKeepsTheStateOfItsHandOff()
    {
        using var h = new PhysicsHarness();
        var (_, carrier, fighter) = FighterApproachingCarrier(h);
        HandOff? handOff = null;
        bool reportedBoth = false;
        h.BeforeApply = output =>
        {
            if (output.HandOffs.Count == 0)
                return;
            handOff = output.HandOffs.Single();
            for (int i = 0; i < output.Bodies.Count; i++)
            {
                BodyState state = output.Bodies[i];
                if (state.Entity != fighter.Entity)
                    continue;
                reportedBoth = true;
                output.Bodies[i] = state with { Pose = new DPose(state.Pose.Position + new Double3(1, 0, 0), state.Pose.Rotation) };
            }
        };

        h.StepUntil(() => handOff != null, 120);

        handOff.Should().NotBeNull();
        reportedBoth.Should().BeTrue("the backend reports the body in its new frame as well");
        HandOff entry = handOff!.Value;
        entry.To.Should().Be(carrier);
        h[fighter.Entity].Should().Be(new BodyState(fighter.Entity, carrier, entry.Pose, entry.LinearVelocity, entry.AngularVelocity));
    }

    [Fact]
    public void ATestThatRefusesAHandOffThatTheTreeAcceptsFailsAtThatStep()
    {
        using var h = new PhysicsHarness();
        var (_, carrier, fighter) = FighterApproachingCarrier(h);
        h.RefusesHandOff = handOff => handOff.Entity == fighter.Entity;

        Action run = () => h.StepUntil(() => h.HandOffs.Count > 0, 120);

        run.Should().Throw<InvalidOperationException>().WithMessage($"*refuses the hand-off of {fighter.Entity}*{carrier}*the frame tree accepts it*");
    }

    [Fact]
    public void AnEntityWhoseBrokenHandOffATestRefusesKeepsTheStateItHadBeforeTheStep()
    {
        using var h = new PhysicsHarness();
        var (space, carrier, fighter) = FighterApproachingCarrier(h);
        h.BeforeApply = output => PhysicsHarness.ReplaceHandOffs(output, fighter.Entity, handOff => handOff with { From = carrier });
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
