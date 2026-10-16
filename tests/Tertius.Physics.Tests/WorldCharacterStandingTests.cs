using System.Numerics;
using Tertius.Simulation;
using static Tertius.Testing.WorldTesting;

namespace Tertius.Physics.Tests;

/// <summary>
/// Whether physics stood a character on walkable ground under gravity in its last step, as components and the snapshot
/// see it on the World and the real backend: <see cref="BodyHandle.Standing"/> and <see cref="Snapshot.IsStanding"/>.
/// </summary>
public class WorldCharacterStandingTests
{
    private static readonly BodyDesc Person = new(new CapsuleShapeDesc(0.6f, 0.3f), BodyMotion.Character, Mass: 70);

    /// <summary>The height of the center of a person who stands on a floor whose top is at y = 0, plus 2 cm.</summary>
    private const double StandingHeight = 0.92;

    [Fact]
    public void ACharacterOnAFloorUnderGravityStandsForItsComponentsAndInTheSnapshot()
    {
        using World world = NewWorld(out _);
        FrameHandle ground = Ground(world, 9.81f);
        Floor(world, ground);
        var log = new StandingLog();
        Entity person = world.Spawn(new SpawnDesc { Frame = ground, Pose = At(0, StandingHeight), Body = Person, Components = [log] });

        Tick(world, 30);

        world.Errors.Should().BeEmpty();
        world.Snapshot.IsStanding(person).Should().BeTrue();
        log.Seen[^1].Standing.Should().BeTrue();
    }

    [Fact]
    public void ACharacterThatJumpsDoesNotStandInTheAir()
    {
        using World world = NewWorld(out _);
        FrameHandle ground = Ground(world, 9.81f);
        Floor(world, ground);
        var pilot = new Pilot();
        var log = new StandingLog();
        Entity person = world.Spawn(new SpawnDesc { Frame = ground, Pose = At(0, StandingHeight), Body = Person, Components = [pilot, log] });
        Tick(world, 20);

        pilot.Once = body => body.Jump(5);
        Tick(world, 12);

        world.PoseInOwnFrame(person).Position.Y.Should().BeGreaterThan(1.2, "the character is in the air");
        world.Snapshot.IsStanding(person).Should().BeFalse();
        log.Seen[^1].Standing.Should().BeFalse();
    }

    [Fact]
    public void ACharacterThatAForcePressesOntoAFloorInZeroGravityDoesNotStand()
    {
        using World world = NewWorld(out _);
        FrameHandle space = world.CreateRootFrame(new FrameSpec(FrameBounds.Sphere(1e6f), MotionDriver.Static));
        Floor(world, space);
        // 70 N on 70 kg presses the character onto the floor at 1 m/s², so Jolt finds the floor under it in every
        // update, as it does under gravity.
        var pilot = new Pilot { EveryTick = body => body.AddForce(new Vector3(0, -70, 0)) };
        var log = new StandingLog();
        Entity person = world.Spawn(new SpawnDesc { Frame = space, Pose = At(0, StandingHeight), Body = Person, Components = [pilot, log] });

        Tick(world, 30);

        world.PoseInOwnFrame(person).Position.Y.Should().BeApproximately(StandingHeight, 0.03, "the character rests on the floor");
        world.Snapshot.IsStanding(person).Should().BeFalse();
        log.Seen.Should().AllSatisfy(seen => seen.Standing.Should().BeFalse());
    }

    [Fact]
    public void ADynamicBodyAndAnEntityWithoutABodyNeverStand()
    {
        using World world = NewWorld(out _);
        FrameHandle ground = Ground(world, 9.81f);
        Floor(world, ground);
        var boxLog = new StandingLog();
        Entity box = world.Spawn(new SpawnDesc { Frame = ground, Pose = At(0, 0.5), Body = Cube(), Components = [boxLog] });
        var markerLog = new StandingLog();
        Entity marker = world.Spawn(new SpawnDesc { Frame = ground, Pose = At(3, 0.5), Components = [markerLog] });

        Tick(world, 30);

        world.Snapshot.IsStanding(box).Should().BeFalse();
        world.Snapshot.IsStanding(marker).Should().BeFalse();
        boxLog.Seen.Should().HaveCount(30).And.AllSatisfy(seen => seen.Standing.Should().BeFalse());
        markerLog.Seen.Should().HaveCount(30).And.AllSatisfy(seen => seen.Standing.Should().BeFalse());
    }

    [Fact]
    public void AComponentSeesTheStandingStateOfThePhysicsStepOfThePreviousTick()
    {
        using World world = NewWorld(out _);
        FrameHandle ground = Ground(world, 9.81f);
        Floor(world, ground);
        var log = new StandingLog();
        // 0.5 m above the floor, so the character falls for about 0.3 s, then stands.
        Entity person = world.Spawn(new SpawnDesc { Frame = ground, Pose = At(0, StandingHeight + 0.5), Body = Person, Components = [log] });

        // The snapshot after each tick, by the number of ticks done.
        var snapshot = new Dictionary<long, bool> { [world.TickNumber] = world.Snapshot.IsStanding(person) };
        for (int i = 0; i < 40; i++)
        {
            world.Tick();
            snapshot[world.TickNumber] = world.Snapshot.IsStanding(person);
        }

        snapshot.Values.Should().Contain(false).And.Contain(true, "the character fell, then stood");
        // Tick N runs while physics step N runs, and the sync point at its end applies step N. So the components of tick
        // N see step N - 1, which the snapshot showed after N ticks were done.
        log.Seen.Should().HaveCount(40);
        foreach ((long tick, bool standing) in log.Seen)
            standing.Should().Be(snapshot[tick], "tick {0} sees the state after {0} ticks", tick);
    }

    [Fact]
    public void AnEntityThatTakesTheSlotOfAStandingCharacterDoesNotStand()
    {
        using World world = NewWorld(out _);
        FrameHandle ground = Ground(world, 9.81f);
        Floor(world, ground);
        Entity person = world.Spawn(new SpawnDesc { Frame = ground, Pose = At(0, StandingHeight), Body = Person });
        Tick(world, 30);
        world.Snapshot.IsStanding(person).Should().BeTrue();

        world.Destroy(person);
        Entity newcomer = world.Spawn(new SpawnDesc { Frame = ground, Pose = At(0, 20), Body = Person });

        newcomer.Index.Should().Be(person.Index, "the newcomer takes the free slot");
        world.Snapshot.IsStanding(newcomer).Should().BeFalse();
    }

    [Fact]
    public void TheStandingStateOfAStaleHandleIsAnError()
    {
        using World world = NewWorld(out _);
        FrameHandle ground = Ground(world, 9.81f);
        Entity person = world.Spawn(new SpawnDesc { Frame = ground, Pose = At(0, StandingHeight), Body = Person });
        world.Destroy(person);

        Action read = () => world.Snapshot.IsStanding(person);

        read.Should().Throw<StaleEntityException>();
    }

    /// <summary>
    /// Physics reports a character state only for a live character, so any other comes only from a bug. A test stands
    /// in for that bug: it adds a standing state for a dynamic box, or for an entity that the World does not know.
    /// </summary>
    [Theory]
    [InlineData("a dynamic box")]
    [InlineData("an unknown entity")]
    public void ACharacterStateForAnythingButALiveCharacterIsABug(string about)
    {
        var faults = new FaultInjectingBackend(new JoltPhysicsBackend(new JoltBackendOptions { WorkerThreads = 2 }));
        using var world = new World(new WorldOptions { MaxDegreeOfParallelism = 1, DebugChecks = true, DisposePhysics = true }, faults);
        FrameHandle ground = Ground(world, 9.81f);
        Floor(world, ground);
        Entity box = world.Spawn(new SpawnDesc { Frame = ground, Pose = At(0, 0.5), Body = Cube() });
        world.Tick();
        Entity named = about == "a dynamic box" ? box : new Entity(999, 1);
        faults.EditNextOutput = output => output.Characters.Add(new CharacterState(named, Standing: true));

#if TERTIUS_CONTRACTS
        Action tick = () => world.Tick();

        tick.Should().Throw<ContractException>().WithMessage($"*character state of {named}*");
#else
        Tick(world, 3);

        world.Errors.Should().ContainSingle().Which.Should().Contain($"ignored the character state of {named}");
        world.Snapshot.IsStanding(box).Should().BeFalse();
#endif
    }

    /// <summary>
    /// Physics reports each character once in a step, so a second state comes only from a bug. A test stands in for that
    /// bug: in one step it reports a walking person as not standing, then adds a second state that says that the person
    /// stands. A Release build skips only the second state, so the person no longer stands, and the rest of its update
    /// lands: it walks on.
    /// </summary>
    [Fact]
    public void ASecondCharacterStateOfOneCharacterInOneStepIsABug()
    {
        var faults = new FaultInjectingBackend(new JoltPhysicsBackend(new JoltBackendOptions { WorkerThreads = 2 }));
        using var world = new World(new WorldOptions { MaxDegreeOfParallelism = 1, DebugChecks = true, DisposePhysics = true }, faults);
        FrameHandle ground = Ground(world, 9.81f);
        Floor(world, ground);
        var pilot = new Pilot { EveryTick = body => body.MoveCharacter(new Vector3(1, 0, 0)) };
        Entity person = world.Spawn(new SpawnDesc { Frame = ground, Pose = At(0, StandingHeight), Body = Person, Components = [pilot] });
        Tick(world, 30);
        world.Snapshot.IsStanding(person).Should().BeTrue();
        double x = world.PoseInOwnFrame(person).Position.X;
        faults.EditNextOutput = output =>
        {
            int first = output.Characters.FindIndex(state => state.Entity == person);
            output.Characters[first] = new CharacterState(person, Standing: false);
            output.Characters.Add(new CharacterState(person, Standing: true));
        };

#if TERTIUS_CONTRACTS
        Action tick = () => world.Tick();

        tick.Should().Throw<ContractException>().WithMessage($"*character state of {person}*another character state in this step*");
#else
        world.Tick();

        world.Errors.Should().ContainSingle().Which.Should().Contain($"ignored the character state of {person}");
        world.Snapshot.IsStanding(person).Should().BeFalse("the first state of the step landed, and the second did not");
        world.PoseInOwnFrame(person).Position.X.Should().BeGreaterThan(x + 0.01, "the body state of the step landed");
#endif
    }

    /// <summary>
    /// A character state is part of the physics update of its entity. When another output that names the entity comes only
    /// from a bug, a Release build skips the whole update, and the character keeps the state of its last step. A test
    /// stands in for that bug: it adds a contact of a standing person with itself, in the step that reports it not
    /// standing.
    /// </summary>
    [Fact]
    public void ACharacterWhoseUpdateABrokenOutputSkipsKeepsTheStandingStateOfItsLastStep()
    {
        var faults = new FaultInjectingBackend(new JoltPhysicsBackend(new JoltBackendOptions { WorkerThreads = 2 }));
        using var world = new World(new WorldOptions { MaxDegreeOfParallelism = 1, DebugChecks = true, DisposePhysics = true }, faults);
        FrameHandle ground = Ground(world, 9.81f);
        Floor(world, ground);
        Entity person = world.Spawn(new SpawnDesc { Frame = ground, Pose = At(0, StandingHeight), Body = Person });
        Tick(world, 30);
        world.Snapshot.IsStanding(person).Should().BeTrue();
        faults.EditNextOutput = output =>
        {
            output.Contacts.Add(new ContactStarted(person, person, ground, Double3.Zero, Vector3.UnitY, 0));
            for (int i = 0; i < output.Characters.Count; i++)
                if (output.Characters[i].Entity == person)
                    output.Characters[i] = new CharacterState(person, Standing: false);
        };

#if TERTIUS_CONTRACTS
        Action tick = () => world.Tick();

        tick.Should().Throw<ContractException>().WithMessage($"*{person}*");
#else
        world.Tick();

        world.Errors.Should().ContainSingle().Which.Should().Contain(person.ToString());
        world.Snapshot.IsStanding(person).Should().BeTrue("the skipped update did not change it");
#endif
    }

#if !TERTIUS_CONTRACTS
    /// <summary>
    /// What one step skips does not carry into the next step. A test breaks the update of a standing person in one step,
    /// as above, and in the next step reports the person not standing, with nothing broken. That state lands. Only a
    /// Release build gets past the broken step, because a Debug build fails a contract there.
    /// </summary>
    [Fact]
    public void TheStepAfterABrokenStepSetsTheStandingStateThatItReports()
    {
        var faults = new FaultInjectingBackend(new JoltPhysicsBackend(new JoltBackendOptions { WorkerThreads = 2 }));
        using var world = new World(new WorldOptions { MaxDegreeOfParallelism = 1, DebugChecks = true, DisposePhysics = true }, faults);
        FrameHandle ground = Ground(world, 9.81f);
        Floor(world, ground);
        Entity person = world.Spawn(new SpawnDesc { Frame = ground, Pose = At(0, StandingHeight), Body = Person });
        Tick(world, 30);
        faults.EditNextOutput = output => output.Contacts.Add(new ContactStarted(person, person, ground, Double3.Zero, Vector3.UnitY, 0));
        world.Tick();
        world.Errors.Should().ContainSingle().Which.Should().Contain(person.ToString());
        world.Snapshot.IsStanding(person).Should().BeTrue();

        faults.EditNextOutput = output =>
        {
            for (int i = 0; i < output.Characters.Count; i++)
                if (output.Characters[i].Entity == person)
                    output.Characters[i] = new CharacterState(person, Standing: false);
        };
        world.Tick();

        world.Errors.Should().ContainSingle("the second step broke nothing");
        world.Snapshot.IsStanding(person).Should().BeFalse("the state of the second step landed");
    }
#endif
}

/// <summary>Records what <see cref="BodyHandle.Standing"/> says on every tick.</summary>
internal sealed class StandingLog : IComponent
{
    public List<(long Tick, bool Standing)> Seen { get; } = new();

    public int Priority => 0;

    public EventMask Wants => EventMask.Of<Tick>();

    public void Handle(Self self, scoped ref EventList events, scoped ref Outbox outbox) => Seen.Add((self.Tick, self.Body.Standing));
}
