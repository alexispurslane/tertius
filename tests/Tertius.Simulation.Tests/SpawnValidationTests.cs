using AwesomeAssertions;

namespace Tertius.Simulation.Tests;

/// <summary>
/// Spawn descriptions that the world refuses. The world checks a description before it takes a slot, so a refused spawn
/// leaves no trace: it takes no slot, makes no entity and tells physics nothing. Outside a tick the caller gets an
/// <see cref="ArgumentException"/>. Inside a tick the component that asked gets it, and if the component lets it
/// escape, the scheduler reports it in <see cref="World.Errors"/>.
/// </summary>
public class SpawnValidationTests
{
    private static readonly FrameSpec SmallFrame = new(FrameBounds.Sphere(10));

    public static TheoryData<string> Defects => new(
        "no frame", "a destroyed frame", "an attached body", "an attached frame", "a null component",
        "the same component twice", "a frame with no bubble", "a stale attachment parent",
        "an attachment parent that owns a root frame");

    /// <summary>A description with the named defect. <paramref name="staleFrame"/> and <paramref name="staleParent"/> are handles that have been destroyed.</summary>
    private static SpawnDesc WithDefect(string defect, FrameHandle root, FrameHandle staleFrame, Entity liveParent,
        Entity staleParent)
    {
        var shared = new Recorder();
        return defect switch
        {
            "no frame" => new(),
            "a destroyed frame" => new() { Frame = staleFrame },
            "an attached body" => new() { Frame = root, AttachTo = liveParent, Body = TestWorld.Ball },
            "an attached frame" => new() { Frame = root, AttachTo = liveParent, AsFrame = SmallFrame },
            "a null component" => new() { Frame = root, Components = [null!] },
            "the same component twice" => new() { Frame = root, Components = [shared, shared] },
            "a frame with no bubble" => new() { Frame = root, AsFrame = new(default) },
            "a stale attachment parent" => new() { Frame = root, AttachTo = staleParent },
            // The owner of a root frame is in no frame, so it has none to give a child.
            "an attachment parent that owns a root frame" => new() { AttachTo = root.Entity },
            _ => throw new ArgumentOutOfRangeException(nameof(defect), defect, null)
        };
    }

    /// <summary>A world with a live entity to attach to, and a frame and an entity that were destroyed.</summary>
    private static TestWorld Prepared(out FrameHandle staleFrame, out Entity liveParent, out Entity staleParent)
    {
        var t = new TestWorld();
        // Slots 1, 2 and 3. The two destroyed ones are freed in the order 1, 3 and nothing reuses them before the test does.
        staleFrame = t.SpawnStaticFrame(TestWorld.At(100));
        liveParent = t.SpawnWith();
        staleParent = t.SpawnWith();
        t.World.Destroy(staleFrame.Entity);
        t.World.Destroy(staleParent);
        return t;
    }

    [Theory]
    [MemberData(nameof(Defects))]
    public void ADescriptionWithADefectIsRefusedWithAnArgumentError(string defect)
    {
        using TestWorld t = Prepared(out FrameHandle staleFrame, out Entity liveParent, out Entity staleParent);
        SpawnDesc desc = WithDefect(defect: defect, root: t.Root, staleFrame: staleFrame, liveParent: liveParent,
            staleParent: staleParent);

        Action spawn = () => t.World.Spawn(desc);

        spawn.Should().Throw<ArgumentException>();
    }

    [Theory]
    [MemberData(nameof(Defects))]
    public void ARefusedSpawnTakesNoSlot(string defect)
    {
        using TestWorld t = Prepared(out FrameHandle staleFrame, out Entity liveParent, out Entity staleParent);
        SpawnDesc desc = WithDefect(defect: defect, root: t.Root, staleFrame: staleFrame, liveParent: liveParent,
            staleParent: staleParent);
        Entity before = t.SpawnWith();

        Record.Exception(() => t.World.Spawn(desc));
        Entity after = t.SpawnWith();

        // The two destroyed entities left two free slots, in the order they were destroyed. A refused spawn that took one
        // of them would push "after" past the second.
        before.Index.Should().Be(staleFrame.Entity.Index);
        after.Index.Should().Be(staleParent.Index);
    }

    [Theory]
    [MemberData(nameof(Defects))]
    public void ARefusedSpawnMakesNoEntity(string defect)
    {
        using TestWorld t = Prepared(out FrameHandle staleFrame, out Entity liveParent, out Entity staleParent);
        SpawnDesc desc = WithDefect(defect: defect, root: t.Root, staleFrame: staleFrame, liveParent: liveParent,
            staleParent: staleParent);
        int liveBefore = t.World.Stats.LiveEntities;

        Record.Exception(() => t.World.Spawn(desc));

        t.World.Stats.LiveEntities.Should().Be(liveBefore);
    }

    [Fact]
    public void ARefusedSpawnTellsPhysicsNothing()
    {
        using TestWorld t = TestWorld.OnJolt(faults: out FaultInjectingBackend faults);
        List<PhysicsChanges> batches = faults.AppliedChanges;
        Record.Exception(() => t.World.Spawn(new() { Frame = default, Body = TestWorld.Ball }));

        t.Run(1);

        batches.SelectMany(batch => batch.BodiesCreated).Should().BeEmpty();
    }

    [Fact]
    public void ANullDescriptionIsRefusedWithANullArgumentError()
    {
        using var t = new TestWorld();

        Action spawn = () => t.World.Spawn(null!);

        spawn.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void ADescriptionIsCopiedSoLaterChangesToItsListsDoNotMatter()
    {
        using var t = new TestWorld();
        var kept = new Recorder();
        var components = new List<IComponent> { kept };
        var desc = new SpawnDesc { Frame = t.Root, Components = components };
        Entity entity = t.World.Spawn(desc);

        components.Add(new Recorder());
        components.Clear();

        t.World.GetComponents(entity).Should().Equal(kept);
    }

    [Fact]
    public void ComponentsAreOrderedByPriorityAndTiesKeepTheOrderTheyWereGiven()
    {
        using var t = new TestWorld();
        var a = new Recorder(EventMask.Of<Ping>(), priority: 2);
        var b = new Recorder(EventMask.Of<Ping>(), priority: 1);
        var c = new Recorder(EventMask.Of<Ping>(), priority: 2);
        var d = new Recorder(EventMask.Of<Ping>(), priority: 1);

        Entity entity = t.SpawnWith(a, b, c, d);

        // Lowest priority first, and equal priorities in the order given.
        t.World.GetComponents(entity).Should().Equal(b, d, a, c);
    }

    /// <summary>A component whose priority cannot be read.</summary>
    private sealed class BrokenPriority : IComponent
    {
        public int Priority => throw new InvalidOperationException("no priority");
        public EventMask Wants => default;

        public void Handle(Self self, scoped ref EventList events, scoped ref Outbox outbox)
        {
        }
    }

    /// <summary>A component whose wants cannot be read.</summary>
    private sealed class BrokenWants : IComponent
    {
        public int Priority => 0;
        public EventMask Wants => throw new InvalidOperationException("no wants");

        public void Handle(Self self, scoped ref EventList events, scoped ref Outbox outbox)
        {
        }
    }

    [Fact]
    public void AComponentWhosePriorityCannotBeReadFailsTheSpawnCallAndTakesNoSlot()
    {
        using var t = new TestWorld();
        Entity first = t.SpawnWith();

        Action spawn = () => t.SpawnWith(new BrokenPriority());

        spawn.Should().Throw<InvalidOperationException>().WithMessage("no priority");
        t.SpawnWith().Index.Should().Be(first.Index + 1);
    }

    [Fact]
    public void AComponentWhoseWantsCannotBeReadFailsTheSpawnCallAndTakesNoSlot()
    {
        using var t = new TestWorld();
        Entity first = t.SpawnWith();

        Action spawn = () => t.SpawnWith(new BrokenWants());

        spawn.Should().Throw<InvalidOperationException>().WithMessage("no wants");
        t.SpawnWith().Index.Should().Be(first.Index + 1);
    }

    // ---- Inside a tick --------------------------------------------------------------------------------------------

    [Fact]
    public void ARefusedSpawnInsideATickThrowsToTheComponentThatAsked()
    {
        using var t = new TestWorld();
        Exception? caught = null;
        t.SpawnWith(Lambda.OnTick((self, scoped ref events, scoped ref outbox) =>
        {
            try
            {
                self.Spawn(new());
            }
            catch (ArgumentException ex)
            {
                caught = ex;
            }
        }));

        t.Run(1);

        caught.Should().BeOfType<ArgumentException>();
        t.World.Errors.Should().BeEmpty();
    }

    [Fact]
    public void ARefusedSpawnInsideATickTakesNoSlotFromTheChunksPool()
    {
        using var t = new TestWorld();
        Entity child = default;
        Entity spawner = t.SpawnWith(Lambda.OnTick((self, scoped ref events, scoped ref outbox) =>
        {
            if (self.Tick != 0)
                return;
            try
            {
                self.Spawn(new());
            }
            catch (ArgumentException)
            {
                // Refused, as the previous test checks. This test looks only at the slot.
            }

            child = self.Spawn(new() { Frame = self.Frame });
        }));

        t.Run(1);

        // The pool starts at the slot after the spawner's. The refused spawn did not use it.
        child.Index.Should().Be(spawner.Index + 1);
    }

    [Fact]
    public void ARefusedSpawnThatEscapesTheComponentIsReportedAsAnError()
    {
        using var t = new TestWorld();
        t.SpawnWith(Lambda.OnTick((self, scoped ref events, scoped ref outbox) =>
        {
            if (self.Tick == 0)
                self.Spawn(new());
        }));
        int before = t.World.Stats.LiveEntities;

        t.Run(1);

        t.World.Errors.Should().ContainSingle().Which.Should().Contain("ArgumentException");
        t.World.Stats.LiveEntities.Should().Be(before);
    }

    // ---- Structural changes are for outside a tick -------------------------------------------------------------

    [Theory]
    [InlineData("Spawn")]
    [InlineData("Destroy")]
    [InlineData("AddComponent")]
    [InlineData("RemoveComponent")]
    [InlineData("Subscribe")]
    [InlineData("Unsubscribe")]
    [InlineData("PublishRenderSnapshot")]
    public void AWorldChangeThatIsOnlyForOutsideATickIsRejectedDuringOne(string operation)
    {
        using var t = new TestWorld();
        Topic topic = Topics.Get("tests.spawn.structural");
        var component = new Recorder();
        Entity target = t.SpawnWith(component);
        InvalidOperationException? caught = null;
        t.SpawnWith(Lambda.OnTick((self, scoped ref events, scoped ref outbox) =>
        {
            try
            {
                switch (operation)
                {
                    case "Spawn": t.World.Spawn(new() { Frame = t.Root }); break;
                    case "Destroy": t.World.Destroy(target); break;
                    case "AddComponent": t.World.AddComponent(target, new Recorder()); break;
                    case "RemoveComponent": t.World.RemoveComponent(target, component); break;
                    case "Subscribe": t.World.Subscribe(target, topic); break;
                    case "Unsubscribe": t.World.Unsubscribe(target, topic); break;
                    default: t.World.PublishRenderSnapshot(); break;
                }
            }
            catch (InvalidOperationException ex)
            {
                caught = ex;
            }
        }));
        int liveBefore = t.World.Stats.LiveEntities;

        t.Run(1);

        caught.Should().NotBeNull();
        caught!.Message.Should().Contain($"World.{operation}");
        t.World.IsAlive(target).Should().BeTrue();
        t.World.GetComponents(target).Should().Equal(component);
        t.World.Stats.LiveEntities.Should().Be(liveBefore);
    }
}
