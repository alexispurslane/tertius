using Tertius.Frames.Rails;
using Tertius.Simulation;
using static Tertius.Testing.WorldTesting;

namespace Tertius.Physics.Tests;

/// <summary>
/// <see cref="Self.PoseAtNextStepStartIn"/> gives the pose where the entity starts the next step, which is where the step
/// that runs beside the tick leaves it (docs/plans/rails-components.md, "The motion intent, PhysicsIntent.Move"). A kinematic
/// body without a motion intent in that step holds still in its home, hull or not, so it ends the step where its home
/// carries it. A numeric home moves over the step, so the read moves the pose now with it, and a rails component that
/// starts from the read starts where the body stands. The world runs on the real backend.
/// </summary>
public class PoseAtNextStepStartTests
{
    /// <summary>What a reader saw in one tick: the read of the next step's start, and the pose now, both in the entity's frame.</summary>
    private readonly record struct Seen(DPose NextStepStart, DPose Now);

    /// <summary>Writes nothing. In every tick it keeps the two reads of its entity, in the frame that the entity is in.</summary>
    private sealed class Reader : IComponent
    {
        public List<Seen> Seen { get; } = new();

        public int Priority => 0;

        public EventMask Wants => EventMask.Of<Tick>();

        public void Handle(Self self, scoped ref EventList events, scoped ref Outbox outbox) =>
            Seen.Add(new Seen(self.PoseAtNextStepStartIn(self.Frame), self.PoseIn(self.Frame)));
    }

    /// <summary>
    /// Removes <paramref name="motion"/> from <paramref name="entity"/>, lets one tick write no motion intent, and then reads
    /// in two ticks: the first reads where the entity starts the next step, in a tick whose running step has no motion intent,
    /// and the second reads where the entity stands once that step ended.
    /// </summary>
    private static (Seen Gap, Seen After) ReadAcrossAStepWithoutAMotionIntent(World world, Entity entity, IComponent motion)
    {
        world.RemoveComponent(entity, motion).Should().BeTrue();
        world.Tick();
        var reader = new Reader();
        world.AddComponent(entity, reader);
        Tick(world, 2);
        world.Errors.Should().BeEmpty();
        return (reader.Seen[0], reader.Seen[1]);
    }

    /// <summary>
    /// A fast kinematic hull on its Kepler orbit rests in an orbit frame of its own (see <see cref="Scenes.FastKeplerHull"/>).
    /// Its component goes, so a step runs without a motion intent: the hull holds still in its orbit frame, which falls on
    /// along the orbit, 33 m in a step. A read of the next step's start in that step's tick gives the place where the hull
    /// stands when the step ends.
    /// </summary>
    [Fact]
    public void AKinematicHullWithoutAMotionIntentStartsTheNextStepWhereItsOrbitFrameCarriesIt()
    {
        using World world = NewWorld(out _);
        FrameHandle space = Scenes.AsteroidSpace(world);
        Entity hull = Scenes.FastKeplerHull(world, space, Ball() with { Motion = BodyMotion.Kinematic });
        Tick(world, 10);
        world.Frames.Numeric(world.Frames.TreeParent(world.GetOwnedFrame(hull))).Should().Be(NumericMode.Orbiting);

        (Seen gap, Seen after) = ReadAcrossAStepWithoutAMotionIntent(world, hull, world.GetComponent<KeplerMotion>(hull)!);

        (after.Now.Position - gap.Now.Position).Length.Should().BeGreaterThan(30, "the orbit frame carries the hull on over the step");
        (gap.NextStepStart.Position - after.Now.Position).Length.Should().BeLessThan(1e-6);
        gap.NextStepStart.Rotation.Should().Be(after.Now.Rotation, "a numeric frame never turns, so the hull keeps its rotation");
    }

    /// <summary>
    /// A plain kinematic ball that end poses move at 1,200 m/s through space without gravity runs in a falling numeric frame
    /// that moves with it. Its end poses stop, so a step runs without a motion intent: the ball holds still in its home, which
    /// moves on 20 m in the step. A read of the next step's start in that step's tick gives the place where the ball stands
    /// when the step ends.
    /// </summary>
    [Fact]
    public void APlainKinematicBodyWithoutAMotionIntentStartsTheNextStepWhereItsNumericHomeCarriesIt()
    {
        using World world = NewWorld(out _);
        FrameHandle space = Ground(world, 0);
        Entity ball = world.Spawn(KinematicDrift.Drifting(new SpawnDesc { Frame = space, Body = Ball() with { Motion = BodyMotion.Kinematic } },
            world, new Double3(1_200, 0, 0)));
        Tick(world, 10);
        world.Frames.Numeric(world.PhysicsHomeOf(ball)).Should().Be(NumericMode.Falling);

        (Seen gap, Seen after) = ReadAcrossAStepWithoutAMotionIntent(world, ball, world.GetComponent<KinematicDrift>(ball)!);

        (after.Now.Position - gap.Now.Position).Length.Should().BeGreaterThan(19, "the numeric home carries the ball on over the step");
        (gap.NextStepStart.Position - after.Now.Position).Length.Should().BeLessThan(1e-6);
        gap.NextStepStart.Rotation.Should().Be(after.Now.Rotation);
    }

    /// <summary>
    /// A dynamic ball that flies at 1,200 m/s runs in a falling numeric frame too, but physics moves it, so the read does not
    /// predict its step: it gives the pose where the running step started, the pose now (the decision of
    /// <see cref="Self.PoseAtNextStepStartIn"/>).
    /// </summary>
    [Fact]
    public void ADynamicBodyStartsTheNextStepAtItsPoseNowInTheRead()
    {
        using World world = NewWorld(out _);
        FrameHandle space = Ground(world, 0);
        var reader = new Reader();
        Entity ball = world.Spawn(new SpawnDesc { Frame = space, Body = Ball(), LinearVelocity = new Double3(1_200, 0, 0), Components = [reader] });
        Tick(world, 10);
        world.Frames.Numeric(world.PhysicsHomeOf(ball)).Should().Be(NumericMode.Falling);

        reader.Seen[^1].NextStepStart.Should().Be(reader.Seen[^1].Now);
    }
}
