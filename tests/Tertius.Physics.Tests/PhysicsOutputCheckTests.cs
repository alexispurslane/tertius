using System.Numerics;
using Tertius.Frames.Rails;
using Tertius.Simulation;
using static Tertius.Testing.WorldTesting;

namespace Tertius.Physics.Tests;

/// <summary>
/// The sync point checks each physics output against what a correct backend keeps (<see cref="PhysicsOutputCheck"/>), before
/// any of it lands. Jolt never reports an output that breaks a rule, so these tests make one on purpose with
/// <see cref="FaultInjectingBackend.EditNextOutput"/>, in the World on the real backend. A Debug build breaks a contract in
/// the tick. A Release build skips the whole update of every entity that the output names, and records one error. The tests
/// also pin down how the sync point orders the parts of one step's output, and what it keeps of a skipped update.
/// </summary>
public sealed class PhysicsOutputCheckTests
{
    private static readonly BodyDesc PersonBody = new(
        Shape: new CapsuleShapeDesc(0.6f, 0.3f),
        Motion: BodyMotion.Character,
        Mass: 70
    );

    /// <summary>A dynamic box with sides of 2 · <paramref name="half"/> m that nothing slows down on its own and that does not bounce.</summary>
    private static BodyDesc BoxBody(float half, float mass) =>
        new(Shape: new BoxShapeDesc(new(half)), Mass: mass, Restitution: 0, LinearDamping: 0, AngularDamping: 0);

    /// <summary>A dynamic ball that nothing slows down on its own and that does not bounce.</summary>
    private static BodyDesc BallBody(float radius, float mass) =>
        new(Shape: new SphereShapeDesc(radius), Mass: mass, Restitution: 0, LinearDamping: 0, AngularDamping: 0);

    /// <summary>
    /// A world that holds: a ship frame that physics drives, 10 m along +X in space, with a cockpit frame inside it, a ball in
    /// space, a frame whose owner has no body, a second root frame whose owner has a static body, a third root frame whose
    /// owner has a dynamic body, a dock frame with a static hull and a moon frame on a Kepler orbit with a kinematic hull,
    /// both in space, a bubble frame that is gone from the tree, a wreck whose body is
    /// gone, and a person, whose body is a character, in space. Space has a point gravity with a tiny G·M, because the
    /// Kepler moon needs a G·M to orbit. It pulls a body at 100 m with 0.0001 m/s², which no test notices.
    /// </summary>
    private sealed class Scene : IDisposable
    {
        public Scene()
        {
            World = NewWorldWithFaults(faults: out FaultInjectingBackend faults);
            Faults = faults;
            Space = World.CreateRootFrame(new(FrameBounds.Sphere(1e6f), GravityField.Point(1)));
            Entity shipOwner = World.Spawn(
                new()
                {
                    Frame = Space,
                    Pose = At(10),
                    Body = BoxBody(3, 100),
                    AsFrame = new(FrameBounds.Sphere(30)),
                }
            );
            Ship = World.GetOwnedFrame(shipOwner);
            Cockpit = FrameOf(Ship, At(1), 2);
            Ball = World.Spawn(
                new()
                {
                    Frame = Space,
                    Pose = At(-100),
                    Body = BallBody(0.5f, 1),
                }
            );
            Wreck = World.Spawn(
                new()
                {
                    Frame = Space,
                    Pose = At(-200),
                    Body = BallBody(0.5f, 1),
                }
            );
            Person = World.Spawn(
                new()
                {
                    Frame = Space,
                    Pose = At(0, 0, 100),
                    Body = PersonBody,
                }
            );
            Drifter = World.GetOwnedFrame(
                World.Spawn(
                    new()
                    {
                        Frame = Space,
                        Pose = At(300),
                        AsFrame = new(FrameBounds.Sphere(5)),
                    }
                )
            );
            Island = World.GetOwnedFrame(
                World.Spawn(
                    new()
                    {
                        Body = new(Shape: new SphereShapeDesc(5), Motion: BodyMotion.Static),
                        AsFrame = new(FrameBounds.Sphere(10)),
                    }
                )
            );
            Raft = World.GetOwnedFrame(
                World.Spawn(new() { Body = BoxBody(2, 100), AsFrame = new(FrameBounds.Sphere(10)) })
            );
            Dock = World.GetOwnedFrame(
                World.Spawn(
                    new()
                    {
                        Frame = Space,
                        Pose = At(0, -200),
                        Body = new(Shape: new BoxShapeDesc(new(1)), Motion: BodyMotion.Static),
                        AsFrame = new(FrameBounds.Sphere(5)),
                    }
                )
            );
            Moon = World.GetOwnedFrame(
                World.Spawn(
                    new SpawnDesc
                    {
                        Frame = Space,
                        Body = BoxBody(1, 10) with { Motion = BodyMotion.Kinematic },
                        AsFrame = new(FrameBounds.Sphere(5)),
                    }.OnKeplerOrbit(World, KeplerOrbit.Circular(200, phase: 1.0))
                )
            );
            Entity goneOwner = World.Spawn(
                new()
                {
                    Frame = Space,
                    Pose = At(-500),
                    AsFrame = new(FrameBounds.Sphere(5)),
                }
            );
            Gone = World.GetOwnedFrame(goneOwner);
            World.Tick();
            World.Destroy(goneOwner);
            World.Destroy(Wreck);
            World.Errors.Should().BeEmpty("the scene itself breaks no rule");
        }

        public World World { get; }
        public FaultInjectingBackend Faults { get; }
        public FrameHandle Space { get; }
        public FrameHandle Ship { get; }
        public FrameHandle Cockpit { get; }
        public Entity Ball { get; }
        public Entity Wreck { get; }
        public Entity Person { get; }
        public FrameHandle Drifter { get; }
        public FrameHandle Island { get; }
        public FrameHandle Raft { get; }
        public FrameHandle Dock { get; }
        public FrameHandle Moon { get; }
        public FrameHandle Gone { get; }

        private FrameHandle FrameOf(FrameHandle parent, DPose pose, float radius) =>
            World.GetOwnedFrame(
                World.Spawn(
                    new()
                    {
                        Frame = parent,
                        Pose = pose,
                        AsFrame = new(FrameBounds.Sphere(radius)),
                    }
                )
            );

        /// <summary>
        /// Adds <paramref name="broken"/> to the output of the next step, and expects the sync point to refuse the output. A
        /// Debug build breaks a contract out of the tick, with the message of the fault. A Release build skips the update,
        /// goes on, and records the fault, with the same message, as the only error of the world.
        /// <paramref name="messagePattern"/>
        /// is a wildcard pattern for the message.
        /// </summary>
        public void ShouldRefuse(Action<PhysicsStepOutput> broken, string messagePattern)
        {
            Faults.EditNextOutput = broken;
            Action tick = World.Tick;

#if TERTIUS_CONTRACTS
            tick.Should().Throw<ContractException>().WithMessage(messagePattern);
#else
            tick.Should().NotThrow();
            World.Errors.Should().ContainSingle().Which.Should().Match(messagePattern);
#endif
        }

        public void Dispose() => World.Dispose();
    }

    private static HandOff HandOffOf(Entity entity, FrameHandle from, FrameHandle to) =>
        new(
            Entity: entity,
            From: from,
            To: to,
            Pose: DPose.Identity,
            LinearVelocity: Vector3.Zero,
            AngularVelocity: Vector3.Zero
        );

    private static ContactStarted ContactOf(Entity a, Entity b, FrameHandle frame) =>
        new(A: a, B: b, Frame: frame, Point: Double3.Zero, Normal: Vector3.UnitX, Impulse: 0);

    [Fact]
    public void AHandOffThatTheFrameTreeRefusesThrowsOutOfTheStep()
    {
        using var scene = new Scene();

        scene.ShouldRefuse(
            output => output.HandOffs.Add(HandOffOf(scene.Ship.Entity, scene.Space, scene.Cockpit)),
            $"*refused the hand-off of {scene.Ship.Entity}*{scene.Ship}*"
        );

        scene.World.Frames.Parent(scene.Ship).Should().Be(scene.Space);
    }

    [Fact]
    public void ABodyStateInAFrameThatIsGoneThrowsOutOfTheStep()
    {
        using var scene = new Scene();

        scene.ShouldRefuse(
            output =>
                output.Bodies.Add(
                    new(
                        Entity: scene.Ball,
                        Frame: scene.Gone,
                        Pose: DPose.Identity,
                        LinearVelocity: Vector3.Zero,
                        AngularVelocity: Vector3.Zero
                    )
                ),
            $"*body state of {scene.Ball}*{scene.Gone}*"
        );
    }

    [Fact]
    public void ABodyStateOfAnEntityWhoseBodyIsGoneThrowsOutOfTheStepAndDoesNotBringTheBodyBack()
    {
        using var scene = new Scene();

        scene.ShouldRefuse(
            output =>
                output.Bodies.Add(
                    new(
                        Entity: scene.Wreck,
                        Frame: scene.Space,
                        Pose: DPose.Identity,
                        LinearVelocity: Vector3.Zero,
                        AngularVelocity: Vector3.Zero
                    )
                ),
            $"*body state of {scene.Wreck}*no longer exists*"
        );

        scene.World.IsAlive(scene.Wreck).Should().BeFalse();
        scene.Faults.Jolt.TryGetBodyFrame(scene.Wreck, out _).Should().BeFalse();
    }

    [Fact]
    public void ABodyStateOfAnEntityWithoutABodyThrowsOutOfTheStep()
    {
        using var scene = new Scene();

        scene.ShouldRefuse(
            output =>
                output.Bodies.Add(
                    new(
                        Entity: scene.Space.Entity,
                        Frame: scene.Space,
                        Pose: DPose.Identity,
                        LinearVelocity: Vector3.Zero,
                        AngularVelocity: Vector3.Zero
                    )
                ),
            $"*body state of {scene.Space.Entity}*has no body*"
        );
    }

    [Fact]
    public void APlainBodysHandOffToAFrameThatIsGoneThrowsOutOfTheStep()
    {
        using var scene = new Scene();

        scene.ShouldRefuse(
            output => output.HandOffs.Add(HandOffOf(scene.Ball, scene.Space, scene.Gone)),
            $"*hand-off of {scene.Ball}*{scene.Gone}*"
        );
    }

    [Fact]
    public void AHandOffOfAnEntityWhoseBodyIsGoneThrowsOutOfTheStepAndDoesNotBringTheBodyBack()
    {
        using var scene = new Scene();

        scene.ShouldRefuse(
            output => output.HandOffs.Add(HandOffOf(scene.Wreck, scene.Space, scene.Ship)),
            $"*hand-off of {scene.Wreck}*no longer exists*"
        );

        scene.World.IsAlive(scene.Wreck).Should().BeFalse();
        scene.Faults.Jolt.TryGetBodyFrame(scene.Wreck, out _).Should().BeFalse();
    }

    [Fact]
    public void AHandOffFromAFrameThatTheEntityIsNotInThrowsOutOfTheStep()
    {
        using var scene = new Scene();

        scene.ShouldRefuse(
            output => output.HandOffs.Add(HandOffOf(scene.Ball, scene.Ship, scene.Cockpit)),
            $"*hand-off of {scene.Ball}*is in {scene.Space}, not in {scene.Ship}*"
        );
    }

    [Fact]
    public void AHandOffOfTheOwnerOfARootFrameThrowsOutOfTheStep()
    {
        using var scene = new Scene();

        scene.ShouldRefuse(
            output => output.HandOffs.Add(HandOffOf(scene.Island.Entity, scene.Space, scene.Ship)),
            $"*hand-off of {scene.Island.Entity}*is in no frame*"
        );

        scene.World.Frames.Parent(scene.Island).Should().Be(FrameHandle.Null);
    }

    [Fact]
    public void AHandOffIntoTheFrameThatTheEntityIsInThrowsOutOfTheStep()
    {
        using var scene = new Scene();

        scene.ShouldRefuse(
            output => output.HandOffs.Add(HandOffOf(scene.Ball, scene.Space, scene.Space)),
            $"*hand-off of {scene.Ball}*which it is in already*"
        );
    }

    [Fact]
    public void ASecondHandOffOfOneEntityInOneStepThrowsOutOfTheStep()
    {
        using var scene = new Scene();

        scene.ShouldRefuse(
            output =>
            {
                output.HandOffs.Add(HandOffOf(scene.Ball, scene.Space, scene.Ship));
                output.HandOffs.Add(HandOffOf(scene.Ball, scene.Space, scene.Ship));
            },
            $"*hand-off of {scene.Ball}*another hand-off in this step*"
        );
    }

    [Fact]
    public void AStepRecordOfAFrameWhoseOwnerHasNoBodyThrowsOutOfTheStep()
    {
        using var scene = new Scene();

        scene.ShouldRefuse(
            output =>
                output.Frames.Add(
                    StepRecords.Of(
                        frame: scene.Drifter,
                        pose: DPose.Identity,
                        velocity: Double3.Zero,
                        angularVelocity: Vector3.Zero,
                        measured: Vector3.Zero
                    )
                ),
            $"*step record of {scene.Drifter}*has no body*"
        );
    }

    [Fact]
    public void AStepRecordOfARootFrameThrowsOutOfTheStep()
    {
        using var scene = new Scene();

        scene.ShouldRefuse(
            output =>
                output.Frames.Add(
                    StepRecords.Of(
                        frame: scene.Raft,
                        pose: DPose.Identity,
                        velocity: Double3.Zero,
                        angularVelocity: Vector3.Zero,
                        measured: Vector3.Zero
                    )
                ),
            $"*step record of {scene.Raft}*root frame*"
        );
    }

    [Theory]
    [InlineData(BodyMotion.Static)]
    [InlineData(BodyMotion.Kinematic)]
    public void AHandOffOfTheOwnerOfAFrameWithoutADynamicHullThrowsOutOfTheStep(BodyMotion motion)
    {
        using var scene = new Scene();
        FrameHandle frame = motion == BodyMotion.Kinematic ? scene.Moon : scene.Dock;

        scene.ShouldRefuse(
            output => output.HandOffs.Add(HandOffOf(frame.Entity, scene.Space, scene.Ship)),
            $"*hand-off of {frame.Entity}*hull of {frame}*only a dynamic hull hands off*"
        );

        scene.World.Frames.Parent(frame).Should().Be(scene.Space);
    }

    [Fact]
    public void AStepRecordOfAFrameThatIsGoneThrowsOutOfTheStep()
    {
        using var scene = new Scene();

        scene.ShouldRefuse(
            output =>
                output.Frames.Add(
                    StepRecords.Of(
                        frame: scene.Gone,
                        pose: DPose.Identity,
                        velocity: Double3.Zero,
                        angularVelocity: Vector3.Zero,
                        measured: Vector3.Zero
                    )
                ),
            $"*step record of the stale frame {scene.Gone}*"
        );
    }

    [Fact]
    public void AContactThatNamesAnEntityWhoseBodyIsGoneThrowsOutOfTheStep()
    {
        using var scene = new Scene();

        scene.ShouldRefuse(
            output => output.Contacts.Add(ContactOf(scene.Ball, scene.Wreck, scene.Space)),
            $"*contact of {scene.Ball} and {scene.Wreck}*no longer exists*"
        );
    }

    [Fact]
    public void AContactThatNamesNoFrameThrowsOutOfTheStep()
    {
        using var scene = new Scene();

        scene.ShouldRefuse(
            output => output.Contacts.Add(ContactOf(scene.Ball, scene.Ship.Entity, FrameHandle.Null)),
            $"*contact of {scene.Ball} and {scene.Ship.Entity}*names no frame*"
        );
    }

    [Fact]
    public void AContactOfABodyWithItselfThrowsOutOfTheStep()
    {
        using var scene = new Scene();

        scene.ShouldRefuse(
            output => output.Contacts.Add(ContactOf(scene.Ball, scene.Ball, scene.Space)),
            $"*contact of {scene.Ball} and {scene.Ball}*cannot touch itself*"
        );
    }

    [Fact]
    public void ACharacterStateOfAnEntityWhoseBodyIsGoneThrowsOutOfTheStep()
    {
        using var scene = new Scene();

        scene.ShouldRefuse(
            output => output.Characters.Add(new(scene.Wreck, Standing: true)),
            $"*character state of {scene.Wreck}*no longer exists*"
        );
    }

    [Fact]
    public void ACharacterStateOfABodyThatIsNotACharacterThrowsOutOfTheStep()
    {
        using var scene = new Scene();

        scene.ShouldRefuse(
            output => output.Characters.Add(new(scene.Ball, Standing: true)),
            $"*character state of {scene.Ball}*is not a character*"
        );
    }

    [Fact]
    public void ASecondCharacterStateOfOneCharacterInOneStepThrowsOutOfTheStep()
    {
        using var scene = new Scene();
        scene
            .Faults.Outputs[^1]
            .Characters.Select(state => state.Entity)
            .Should()
            .Contain(scene.Person, "the backend reports every character once in each step");

        scene.ShouldRefuse(
            output => output.Characters.Add(new(scene.Person, Standing: true)),
            $"*character state of {scene.Person}*another character state in this step*"
        );
    }

    /// <summary>
    /// A carrier bubble 1 km from the origin of space, and a fighter that flies into it at 45 m/s. A correct backend's hand-off
    /// of the fighter is legal, so a test that wants a broken one breaks it first. The fighter is slower than the 50 m/s at
    /// which the engine puts a body into a falling numeric frame, and nothing here is about that frame. With
    /// <paramref name="fighterOwnsFrame"/>, the fighter is a ship with a hull and a frame, as a frame owner hands off with the
    /// frame; otherwise it is a plain body.
    /// </summary>
    private static (FrameHandle Space, FrameHandle Carrier, Entity Fighter) FighterApproachingCarrier(
        World world,
        bool fighterOwnsFrame
    )
    {
        FrameHandle space = world.CreateRootFrame(new(FrameBounds.Sphere(1e7f)));
        FrameHandle carrier = world.GetOwnedFrame(
            world.Spawn(
                new()
                {
                    Frame = space,
                    Pose = At(1000),
                    AsFrame = new(FrameBounds.Sphere(200)),
                }
            )
        );
        Entity fighter = world.Spawn(
            new()
            {
                Frame = space,
                Pose = At(1260),
                LinearVelocity = new(-45, 0, 0),
                Body = BoxBody(2, 1000),
                AsFrame = fighterOwnsFrame ? new FrameSpec(FrameBounds.Sphere(10)) : null,
            }
        );
        return (space, carrier, fighter);
    }

    /// <summary>
    /// The step that hands the fighter into the carrier reports it twice: in the hand-off, and in a body state in the
    /// carrier. The sync point lands body states first and hand-offs after them, so the entity keeps the state of its
    /// hand-off. The test moves the body state by 1 m, so that the two states differ by more than rounding. The fighter is a
    /// plain body, because the owner of a frame gets its state from the frame state after both.
    /// </summary>
    [Fact]
    public void AnEntityWithABodyStateAndAHandOffInOneStepKeepsTheStateOfItsHandOff()
    {
        using World world = NewWorldWithFaults(faults: out FaultInjectingBackend faults);
        (_, FrameHandle carrier, Entity fighter) = FighterApproachingCarrier(world, fighterOwnsFrame: false);
        bool reportedBoth = false;
        faults.EditOutput = output =>
        {
            if (output.HandOffs.Count == 0)
                return;
            for (int i = 0; i < output.Bodies.Count; i++)
            {
                BodyState state = output.Bodies[i];
                if (state.Entity != fighter)
                    continue;
                reportedBoth = true;
                output.Bodies[i] = state with
                {
                    Pose = new(state.Pose.Position + new Double3(1, 0, 0), state.Pose.Rotation),
                };
            }
        };

        TickUntil(world, () => faults.Outputs.Any(output => output.HandOffs.Count > 0), 120);

        HandOff entry = faults.Outputs.SelectMany(output => output.HandOffs).Should().ContainSingle().Which;
        reportedBoth.Should().BeTrue("the backend reports the body in its new frame as well");
        entry.Entity.Should().Be(fighter);
        entry.To.Should().Be(carrier);
        world.GetFrame(fighter).Should().Be(carrier);
        world.PoseIn(fighter, carrier).Should().Be(entry.Pose);
        world.VelocityIn(fighter, carrier).ToVector3().Should().Be(entry.LinearVelocity);
        world.AngularVelocityIn(fighter, carrier).Should().Be(entry.AngularVelocity);
        world.Errors.Should().BeEmpty();
    }

    /// <summary>
    /// A test breaks the backend's own hand-off of a ship's frame, which claims the wrong frame to leave. A Debug build
    /// breaks a contract. A Release build skips the ship's whole update in that step, its body state and the state of the
    /// frame that it owns included, so the ship and its frame keep the state they had before the step.
    /// </summary>
    [Fact]
    public void AnEntityWhoseBrokenHandOffATestRefusesKeepsTheStateItHadBeforeTheStep()
    {
        using World world = NewWorldWithFaults(faults: out FaultInjectingBackend faults);
        (FrameHandle space, FrameHandle carrier, Entity fighter) = FighterApproachingCarrier(
            world,
            fighterOwnsFrame: true
        );
        FrameHandle fighterFrame = world.GetOwnedFrame(fighter);
        faults.EditOutput = output =>
        {
            for (int i = 0; i < output.HandOffs.Count; i++)
                if (output.HandOffs[i].Entity == fighter)
                    output.HandOffs[i] = output.HandOffs[i] with { From = carrier };
        };
        DPose poseBefore = default;
        DPose ownerBefore = default;
        Double3 velocityBefore = default;
        Action run = () =>
            TickUntil(
                world,
                () =>
                {
                    if (world.Errors.Count > 0)
                        return true;
                    poseBefore = world.Frames.LocalPose(fighterFrame);
                    ownerBefore = world.PoseIn(fighter, space);
                    velocityBefore = world.Frames.LinearVelocity(fighterFrame);
                    return false;
                },
                120
            );

#if TERTIUS_CONTRACTS
        run.Should().Throw<ContractException>().WithMessage($"*hand-off of {fighter}*is in {space}, not in {carrier}*");
#else
        run.Should().NotThrow();
        world
            .Errors.Should()
            .ContainSingle()
            .Which.Should()
            .Match($"*hand-off of {fighter}*is in {space}, not in {carrier}*");
        faults.Outputs.SelectMany(output => output.HandOffs).Should().ContainSingle().Which.To.Should().Be(carrier);
        world
            .PoseIn(fighter, space)
            .Should()
            .Be(ownerBefore, "the World skips the body state that came with the refused hand-off");
        world.Frames.Parent(fighterFrame).Should().Be(space);
        world
            .Frames.LocalPose(fighterFrame)
            .Should()
            .Be(poseBefore, "the World skips the frame state that came with the refused hand-off");
        world.Frames.LinearVelocity(fighterFrame).Should().Be(velocityBefore);
#endif
    }

    /// <summary>
    /// A person stands on the floor of a building under gravity and walks out of the building's bubble. The floor reaches
    /// past the bubble, so the person stands until the step that hands it to the ground, which reports it not standing.
    /// The test breaks that hand-off, so a Release build skips the person's whole update in that step, its character state
    /// included.
    /// </summary>
    [Fact]
    public void ACharacterWhoseBrokenHandOffATestRefusesKeepsTheStandingStateOfItsLastStep()
    {
        using World world = NewWorldWithFaults(faults: out FaultInjectingBackend faults);
        var gravity = GravityField.Uniform(new(0, -9.81f, 0));
        FrameHandle ground = world.CreateRootFrame(new(FrameBounds.Sphere(1e6f), gravity));
        world.Spawn(
            new()
            {
                Frame = ground,
                Pose = At(0, -0.5),
                Body = new(Shape: new BoxShapeDesc(new(100, 0.5f, 100)), Motion: BodyMotion.Static),
            }
        );
        FrameHandle building = world.GetOwnedFrame(
            world.Spawn(new() { Frame = ground, AsFrame = new(FrameBounds.Sphere(10), gravity) })
        );
        world.Spawn(
            new()
            {
                Frame = building,
                Pose = At(0, -0.5),
                Body = new(Shape: new BoxShapeDesc(new(15, 0.5f, 15)), Motion: BodyMotion.Static),
            }
        );
        var pilot = new Pilot { EveryTick = body => body.MoveCharacter(new(4, 0, 0)) };
        Entity walker = world.Spawn(
            new()
            {
                Frame = building,
                Pose = At(0, 0.95),
                Body = PersonBody,
                Components = [pilot],
            }
        );
        bool reportedNotStanding = false;
        faults.EditOutput = output =>
        {
            if (output.HandOffs.Count == 0)
                return;
            for (int i = 0; i < output.HandOffs.Count; i++)
                if (output.HandOffs[i].Entity == walker)
                    output.HandOffs[i] = output.HandOffs[i] with { From = ground };
            reportedNotStanding =
                output.Characters.Single(state => state.Entity == walker)
                == new CharacterState(walker, Standing: false);
        };
        Action run = () => TickUntil(world, () => world.Errors.Count > 0, 300);

#if TERTIUS_CONTRACTS
        run.Should()
            .Throw<ContractException>()
            .WithMessage($"*hand-off of {walker}*is in {building}, not in {ground}*");
        reportedNotStanding.Should().BeTrue("the backend reports a character that it hands off as not standing");
#else
        run.Should().NotThrow();
        world
            .Errors.Should()
            .ContainSingle()
            .Which.Should()
            .Match($"*hand-off of {walker}*is in {building}, not in {ground}*");
        faults.Outputs.SelectMany(output => output.HandOffs).Should().ContainSingle().Which.To.Should().Be(ground);
        reportedNotStanding.Should().BeTrue("the backend reports a character that it hands off as not standing");
        world
            .Snapshot.IsStanding(walker)
            .Should()
            .BeTrue("the World skips the character state that came with the refused hand-off");
#endif
    }
}
