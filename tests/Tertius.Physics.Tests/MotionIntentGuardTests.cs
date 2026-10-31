using System.Diagnostics;
using System.Numerics;
using Tertius.Frames.Rails;
using Tertius.Simulation;
using static Tertius.Testing.WorldTesting;

namespace Tertius.Physics.Tests;

/// <summary>
/// One guard judges every motion intent that an entity writes, whatever its kind (docs/plans/rails-components.md, "The flow
/// per kind of entity"): an end pose and a full sample are the result itself, for a kinematic body or an entity without a
/// body, and a steer target is an input that physics integrates for a dynamic body. A static body, a character and an
/// attached entity refuse every case. A refused write throws an <see cref="InvalidOperationException"/> with the reason that
/// <see cref="World.MotionIntentProblem"/> gives, and leaves the next step no motion intent. A kinematic body moves only by
/// its motion intents and teleports, so it refuses a velocity. The world runs on the real backend.
/// </summary>
public sealed class MotionIntentGuardTests
{
    private static readonly FrameSpec SmallBubble = new(FrameBounds.Sphere(0.01f));

    /// <summary>A place near the origin, turned a little about Z.</summary>
    private static readonly DPose Turned = new(new(1, 0.5, -0.25), DQuaternion.FromAxisAngle(Double3.UnitZ, 0.1));

    private static FrameHandle Space(World world) => world.CreateRootFrame(new(FrameBounds.Sphere(1e6f)));

    private static BodyDesc Box(BodyMotion motion) =>
        new(Shape: new BoxShapeDesc(new(2, 0.5f, 2)), Motion: motion, Mass: 1000, LinearDamping: 0, AngularDamping: 0);

    /// <summary>What a test does with the running entity in a tick. A delegate of its own, because <see cref="Self"/> is a ref struct.</summary>
    private delegate void SelfAction(Self self);

    /// <summary>Runs <paramref name="act"/> in the next tick of its entity only, and keeps what it threw.</summary>
    private sealed class Once(SelfAction act) : IComponent
    {
        private bool done;

        public Exception? Thrown { get; private set; }

        public int Priority => 0;

        public EventMask Wants => EventMask.Of<Tick>();

        public void Handle(Self self, scoped ref EventList events, scoped ref Outbox outbox)
        {
            if (done)
                return;
            done = true;
            try
            {
                act(self);
            }
            catch (Exception ex)
            {
                Thrown = ex;
            }
        }
    }

    /// <summary>The motion intent of the write <paramref name="write"/> at <see cref="Turned"/>.</summary>
    private static MotionIntent Move(string write) =>
        write switch
        {
            "end pose" => new(new EndPose(Turned)),
            "full sample" => new(
                new MotionSample(
                    Pose: Turned,
                    ChordVelocity: Double3.Zero,
                    Velocity: Double3.Zero,
                    Acceleration: Double3.Zero,
                    AngularVelocity: Double3.Zero,
                    AngularAcceleration: Double3.Zero
                )
            ),
            _ => new(new SteerTarget(Turned)),
        };

    /// <summary>Writes <paramref name="move"/> through the writer of its case.</summary>
    private static void Write(Self self, in MotionIntent move)
    {
        switch (move)
        {
            case EndPose end:
                self.MoveKinematic(end.Pose);
                break;
            case MotionSample sample:
                self.MoveOnRails(sample);
                break;
            case SteerTarget steer:
                self.Body.SteerTo(steer.Pose);
                break;
            case null:
                // Move gives a case for every write.
                throw new UnreachableException("The motion intent is empty.");
            default:
                throw new UnreachableException();
        }
    }

    /// <summary>Runs one tick in which a component of <paramref name="entity"/> runs <paramref name="act"/>. Returns what it threw, or null.</summary>
    private static Exception? InATick(World world, Entity entity, SelfAction act)
    {
        var once = new Once(act);
        world.AddComponent(entity, once);
        world.Tick();
        return once.Thrown;
    }

    /// <summary>
    /// The entity of the kind <paramref name="kind"/> in <paramref name="space"/>: a body of a motion, as a plain body or as a
    /// frame's hull, a character, an attached entity, an entity without a body, or the owner of a root frame.
    /// </summary>
    private static Entity Spawn(World world, FrameHandle space, string kind) =>
        kind switch
        {
            "dynamic plain body" => world.Spawn(new() { Frame = space, Body = Ball() }),
            "dynamic hull" => world.Spawn(
                new()
                {
                    Frame = space,
                    Body = Box(BodyMotion.Dynamic),
                    AsFrame = SmallBubble,
                }
            ),
            "static plain body" => world.Spawn(
                new() { Frame = space, Body = Ball() with { Motion = BodyMotion.Static } }
            ),
            "static hull" => world.Spawn(
                new()
                {
                    Frame = space,
                    Body = Box(BodyMotion.Static),
                    AsFrame = SmallBubble,
                }
            ),
            "kinematic plain body" => world.Spawn(
                new() { Frame = space, Body = Ball() with { Motion = BodyMotion.Kinematic } }
            ),
            "kinematic hull" => world.Spawn(
                new()
                {
                    Frame = space,
                    Body = Box(BodyMotion.Kinematic),
                    AsFrame = SmallBubble,
                }
            ),
            "character" => world.Spawn(
                new()
                {
                    Frame = space,
                    Body = new(Shape: new CapsuleShapeDesc(0.6f, 0.3f), Motion: BodyMotion.Character, Mass: 70),
                }
            ),
            "attached entity" => world.Spawn(new() { AttachTo = world.Spawn(new() { Frame = space }) }),
            "entity without a body" => world.Spawn(new() { Frame = space }),
            "owner of a root frame" => world.Spawn(new() { Body = Box(BodyMotion.Static), AsFrame = SmallBubble }),
            "owner of a root frame without a body" => world.Spawn(new() { AsFrame = SmallBubble }),
            _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, "no such kind"),
        };

    /// <summary>
    /// A component of <paramref name="kind"/> writes <paramref name="write"/>. The writer throws an
    /// <see cref="InvalidOperationException"/> that names the entity and the guard's reason, which matches
    /// <paramref name="reason"/>, and the next step applies no motion intent to the entity.
    /// </summary>
    private static void AssertRefused(string kind, string write, string reason)
    {
        using World world = NewWorld(backend: out _);
        FrameHandle space = Space(world);
        Entity entity = Spawn(world, space, kind);
        world.Tick();
        MotionIntent move = Move(write);

        Exception? refusal = InATick(world, entity, self => Write(self, move));

        string? problem = world.MotionIntentProblem(entity, move);
        problem
            .Should()
            .NotBeNull()
            .And.Match(reason, "the guard's read gives the reason that {0} refuses {1}", kind, write);
        refusal
            .Should()
            .BeOfType<InvalidOperationException>()
            .Which.Message.Should()
            .Contain(entity.ToString())
            .And.Contain(problem);
        world
            .Entities.NextStepIntent(entity)
            .Move.HasValue.Should()
            .BeFalse("a refused write leaves the next step no motion intent");
        world.Errors.Should().BeEmpty();
    }

    // ---- The refusals of each kind --------------------------------------------------------------------------------

    [Theory]
    [InlineData("dynamic plain body", "end pose")]
    [InlineData("dynamic plain body", "full sample")]
    [InlineData("dynamic hull", "end pose")]
    [InlineData("dynamic hull", "full sample")]
    public void ADynamicBodyRefusesAnEndPoseAndAFullSample(string kind, string write) =>
        AssertRefused(kind, write, $"*Dynamic body*{write}*");

    [Theory]
    [InlineData("static plain body", "end pose")]
    [InlineData("static plain body", "full sample")]
    [InlineData("static plain body", "steer target")]
    [InlineData("static hull", "end pose")]
    [InlineData("static hull", "full sample")]
    [InlineData("static hull", "steer target")]
    public void AStaticBodyRefusesEveryMotionIntent(string kind, string write) =>
        AssertRefused(kind, write, $"*Static body*{write}*");

    [Theory]
    [InlineData("end pose")]
    [InlineData("full sample")]
    [InlineData("steer target")]
    public void ACharacterRefusesEveryMotionIntent(string write) =>
        AssertRefused("character", write, $"*character*{write}*");

    [Theory]
    [InlineData("end pose")]
    [InlineData("full sample")]
    [InlineData("steer target")]
    public void AnAttachedEntityRefusesEveryMotionIntent(string write) =>
        AssertRefused("attached entity", write, $"*attached*{write}*");

    [Theory]
    [InlineData("kinematic plain body", "*Kinematic body*steer target*")]
    [InlineData("kinematic hull", "*Kinematic body*steer target*")]
    [InlineData("entity without a body", "*no body*steer target*")]
    [InlineData("owner of a root frame", "*root frame*steer target*")]
    [InlineData("owner of a root frame without a body", "*root frame*steer target*")]
    public void ASteerTargetNeedsADynamicBody(string kind, string reason) =>
        AssertRefused(kind, "steer target", reason);

    // ---- What a kinematic body and the owner of a root frame take -------------------------------------------------

    /// <summary>
    /// Writes, in every tick, the end pose one step on from where its entity starts the next step, at
    /// <paramref name="velocity"/> in its frame, and keeps each pose by the tick that wrote it.
    /// </summary>
    private sealed class EndPoses(Double3 velocity) : IComponent
    {
        public Dictionary<long, DPose> Written { get; } = [];

        public int Priority => 0;

        public EventMask Wants => EventMask.Of<Tick>();

        public void Handle(Self self, scoped ref EventList events, scoped ref Outbox outbox)
        {
            DPose start = self.PoseAtNextStepStartIn(self.Frame);
            var end = new DPose(start.Position + velocity * self.DeltaTime, start.Rotation);
            Written[self.Tick] = end;
            self.MoveKinematic(end);
        }
    }

    /// <summary>
    /// A kinematic hull 10 m above the ground moves along +X at 2 m/s by end poses. At every sync point its frame stands on
    /// the end pose that the step applied, moves at the chord between two end poses, follows its owner's end poses, and
    /// feels the ground's gravity reversed, because an end pose has no acceleration.
    /// </summary>
    [Fact]
    public void AKinematicHullTakesAnEndPoseAndItsFrameMovesAtTheChord()
    {
        using World world = NewWorld(backend: out _);
        FrameHandle ground = Ground(world, 9.81f);
        var line = new EndPoses(new(2, 0, 0));
        FrameHandle frame = world.GetOwnedFrame(
            world.Spawn(
                new()
                {
                    Frame = ground,
                    Pose = At(0, 10),
                    Body = Box(BodyMotion.Kinematic),
                    AsFrame = new(FrameBounds.Sphere(5)),
                    Components = [line],
                }
            )
        );

        for (int i = 0; i < 20; i++)
        {
            world.Tick();
            if (world.TickNumber < 3)
                continue;
            world
                .Frames.LocalPose(frame)
                .Should()
                .Be(line.Written[world.TickNumber - 2], "the hull ends each step exactly on its end pose");
            (world.Frames.LinearVelocity(frame) - new Double3(2, 0, 0))
                .Length.Should()
                .BeLessThan(1e-9, "the frame moves at the chord");
            world.Frames.Followed(frame).Should().Be(FollowKind.Kinematic);
            (world.Frames.ProperAcceleration(frame) - new Vector3(0, 9.81f, 0))
                .Length()
                .Should()
                .BeLessThan(1e-5f, "an end pose has no acceleration, so the frame feels the ground's gravity reversed");
        }

        world.Errors.Should().BeEmpty();
    }

    /// <summary>
    /// The owner of a root frame takes an end pose and a full sample, with a body or without: its body is only the frame's
    /// static interior, so physics does not move it, and the frame takes the motion intent's place at the sync point after
    /// the step that applies it.
    /// </summary>
    [Theory]
    [InlineData("owner of a root frame", "end pose")]
    [InlineData("owner of a root frame", "full sample")]
    [InlineData("owner of a root frame without a body", "end pose")]
    [InlineData("owner of a root frame without a body", "full sample")]
    public void TheOwnerOfARootFrameTakesAnEndPoseAndAFullSample(string kind, string write)
    {
        using World world = NewWorld(backend: out _);
        Entity owner = Spawn(world, Space(world), kind);
        FrameHandle root = world.GetOwnedFrame(owner);
        world.Tick();
        MotionIntent move = Move(write);

        Exception? refusal = InATick(world, owner, self => Write(self, move));
        world.Tick();

        refusal.Should().BeNull();
        world.MotionIntentProblem(owner, move).Should().BeNull();
        world.Frames.LocalPose(root).Should().Be(Turned, "the root frame took its owner's motion intent");
        world.Errors.Should().BeEmpty();
    }

    /// <summary>
    /// A kinematic body that one motion intent moved at 3 m/s along +X holds still in the next step, which has none: its
    /// place does not change from the sync point after the motion intent's step on, and it reads no velocity there.
    /// </summary>
    [Theory]
    [InlineData("kinematic plain body", "end pose")]
    [InlineData("kinematic plain body", "full sample")]
    [InlineData("kinematic hull", "end pose")]
    [InlineData("kinematic hull", "full sample")]
    public void AKinematicBodyHoldsStillInTheStepAfterItsLastMotionIntent(string kind, string write)
    {
        using World world = NewWorld(backend: out _);
        FrameHandle space = Space(world);
        Entity entity = Spawn(world, space, kind);
        world.Tick();
        double step = world.DeltaTime;
        var end = new DPose(new(3 * step, 0, 0), DQuaternion.Identity);
        MotionIntent move =
            write == "end pose"
                ? new(new EndPose(end))
                : new MotionIntent(
                    new MotionSample(
                        Pose: end,
                        ChordVelocity: new(3, 0, 0),
                        Velocity: new(3, 0, 0),
                        Acceleration: Double3.Zero,
                        AngularVelocity: Double3.Zero,
                        AngularAcceleration: Double3.Zero
                    )
                );

        InATick(world, entity, self => Write(self, move)).Should().BeNull();
        world.Tick();
        (world.PoseIn(entity, space).Position - end.Position)
            .Length.Should()
            .BeLessThan(1e-6, "the step moved the body onto its motion intent");
        Tick(world, 3);

        (world.PoseIn(entity, space).Position - end.Position)
            .Length.Should()
            .BeLessThan(1e-6, "the body holds still without a motion intent");
        world.VelocityIn(entity, space).Length.Should().BeLessThan(1e-6);
        world.Errors.Should().BeEmpty();
    }

    // ---- A kinematic body refuses a velocity ----------------------------------------------------------------------

    /// <summary>
    /// A kinematic body moves only by its motion intents and teleports, hull or plain, so it refuses
    /// <c>SetVelocity</c> and <c>SetAngularVelocity</c>: the writer throws, and the next step sets no velocity.
    /// </summary>
    [Theory]
    [InlineData("kinematic plain body", "SetVelocity")]
    [InlineData("kinematic plain body", "SetAngularVelocity")]
    [InlineData("kinematic hull", "SetVelocity")]
    [InlineData("kinematic hull", "SetAngularVelocity")]
    public void AKinematicBodyRefusesSetVelocityAndSetAngularVelocity(string kind, string write)
    {
        using World world = NewWorld(backend: out _);
        Entity entity = Spawn(world, Space(world), kind);
        world.Tick();

        Exception? refusal = InATick(
            world,
            entity,
            self =>
            {
                if (write == "SetVelocity")
                    self.Body.SetVelocity(new(1, 0, 0));
                else
                    self.Body.SetAngularVelocity(new(0, 1, 0));
            }
        );

        refusal
            .Should()
            .BeOfType<InvalidOperationException>()
            .Which.Message.Should()
            .Contain(entity.ToString())
            .And.Contain("kinematic")
            .And.Contain(write);
        (world.Entities.NextStepIntent(entity).Flags & (IntentFlags.SetVelocity | IntentFlags.SetAngularVelocity))
            .Should()
            .Be(IntentFlags.None);
        world.Errors.Should().BeEmpty();
    }
}

/// <summary>
/// Runs the tests of <see cref="RefusedRailsAllocationTests"/> alone, because their <see cref="StepAllocations"/> swaps
/// joltc's contact callbacks for the whole process.
/// </summary>
[CollectionDefinition(nameof(RefusedRailsAllocationTests), DisableParallelization = true)]
public sealed class RefusedRailsAllocationCollection;

/// <summary>
/// A rails component whose entity the guard of every motion intent refuses runs in every tick, so its judgment must not
/// allocate (see <see cref="World.TakesMotionIntent"/>). The test counts the bytes that every thread of the step allocates
/// (see <see cref="StepAllocations"/>). The world runs on the real backend.
/// </summary>
[Collection(nameof(RefusedRailsAllocationTests))]
public sealed class RefusedRailsAllocationTests
{
    private static readonly BodyDesc StaticBox = new(
        Shape: new BoxShapeDesc(new(2, 0.5f, 2)),
        Motion: BodyMotion.Static
    );

    /// <summary>
    /// A rails component whose entity the guard refuses logs the reason once and writes nothing (see
    /// <see cref="World.MotionIntentProblem"/>). It still runs in every tick, so it must judge its sample without building
    /// the reason, which allocates: here a static box with a <see cref="KeplerMotion"/> and one with a
    /// <see cref="SpinMotion"/>, which a static body refuses, run hundreds of ticks without allocating on any thread.
    /// </summary>
    [Fact]
    public void ARailsComponentThatTheGuardRefusesAllocatesNothingInTheTicksAfterItsLog()
    {
        using World world = NewWorldWithStepAllocations(out StepAllocations steps, out _, debugChecks: false);
        FrameHandle space = Scenes.AsteroidSpace(world);
        var log = new List<string>();
        world.Log += log.Add;
        world.Spawn(
            new()
            {
                Frame = space,
                Pose = At(5_000),
                Body = StaticBox,
                Components = [new KeplerMotion { Orbit = KeplerOrbit.Circular(5_000) }],
            }
        );
        world.Spawn(
            new()
            {
                Frame = space,
                Pose = At(0, 5_000),
                Body = StaticBox,
                Components = [new SpinMotion { Spin = new(Tilt: DQuaternion.Identity, Rate: 0.5) }],
            }
        );
        Tick(world, 10);
        log.Should().HaveCount(2, "each component logs its refusal once");

        const int Ticks = 200;
        long first = BytesPerTick(steps, world, Ticks);
        long second = BytesPerTick(steps, world, Ticks);

        Math.Min(first, second)
            .Should()
            .Be(
                0,
                $"a refused rails component must not allocate (the windows gave {first} and {second} bytes per tick)"
            );
        log.Should().HaveCount(2);
        world.Errors.Should().BeEmpty();
    }

    private static long BytesPerTick(StepAllocations steps, World world, int ticks) =>
        steps.Bytes(() => Tick(world, ticks)) / ticks;
}
