using System.Numerics;

namespace Tertius.Simulation.Tests;

public sealed class Weapon : IComponent
{
    public float Damage = 1;
    public double Range { get; init; } = 10;

    public int Priority => 1;
    public EventMask Wants => EventMask.Of<Tick>();

    public void Handle(Self self, scoped ref EventList events, scoped ref Outbox outbox)
    {
    }
}

public sealed class Radio : IComponent
{
    public int Channel = 1;
    public int Priority => 2;
    public EventMask Wants => EventMask.Of<Ping>();

    public void Handle(Self self, scoped ref EventList events, scoped ref Outbox outbox)
    {
    }
}

public sealed class NeedsArgument(int value) : IComponent
{
    public int Value = value;
    public int Priority => 0;
    public EventMask Wants => default;

    public void Handle(Self self, scoped ref EventList events, scoped ref Outbox outbox)
    {
    }
}

public sealed class Sealed : IComponent
{
    public readonly int Fixed = 3;
    public int Priority => 0;
    public EventMask Wants => default;

    public void Handle(Self self, scoped ref EventList events, scoped ref Outbox outbox)
    {
    }
}

public abstract class AbstractComponent : IComponent
{
    public int Priority => 0;
    public EventMask Wants => default;

    public void Handle(Self self, scoped ref EventList events, scoped ref Outbox outbox)
    {
    }
}

public class BlueprintTests
{
    private static Dictionary<string, object?> Fields(params (string Name, object? Value)[] fields) =>
        fields.ToDictionary(f => f.Name, f => f.Value);

    private static Blueprints Soldiers()
    {
        var blueprints = new Blueprints();
        blueprints.Register(new Blueprint(
            "soldier",
            [
                new ComponentSpec(typeof(Health), Fields(("Max", 120), ("Current", 120f), ("Side", "Rebel"))),
                new ComponentSpec(typeof(Weapon), Fields(("Damage", 5.0), ("Range", 30))),
            ],
            Body: TestWorld.Ball,
            Render: new RenderData { Mesh = new MeshId(4), Flags = RenderFlags.Visible }));
        blueprints.Register(new Blueprint(
            "officer",
            [
                new ComponentSpec(typeof(Radio)),
                new ComponentSpec(typeof(Health), Fields(("Max", 150f), ("Name", "Boss"), ("Armor", 3))),
            ],
            Parent: "soldier"));
        return blueprints;
    }

    [Fact]
    public void InstantiateAppliesFieldValuesWithConversions()
    {
        Blueprints blueprints = Soldiers();
        IComponent[] components = blueprints.Instantiate("soldier");

        Health health = Assert.IsType<Health>(components[0]);
        Assert.Equal(120f, health.Max);
        Assert.Equal(120f, health.Current);
        Assert.Equal(Faction.Rebel, health.Side);
        Assert.Null(health.Name);
        Assert.Null(health.Armor);
        Weapon weapon = Assert.IsType<Weapon>(components[1]);
        Assert.Equal(5f, weapon.Damage);
        Assert.Equal(30, weapon.Range);

        // Every call makes new instances.
        IComponent[] again = blueprints.Instantiate("soldier");
        Assert.NotSame(components[0], again[0]);
        Assert.NotSame(components[1], again[1]);
    }

    [Fact]
    public void AChildInheritsMergesAndOverrides()
    {
        Blueprints blueprints = Soldiers();
        Blueprint officer = blueprints.Resolve("officer");

        Assert.Null(officer.Parent);
        Assert.Equal([typeof(Health), typeof(Weapon), typeof(Radio)], officer.Components.Select(c => c.Type));
        IReadOnlyDictionary<string, object?> health = officer.Components[0].Fields!;
        Assert.Equal(150f, health["Max"]);
        Assert.Equal(Faction.Rebel, health["Side"]);
        Assert.Equal("Boss", health["Name"]);
        Assert.Equal(120f, health["Current"]);
        Assert.Same(TestWorld.Ball, officer.Body);
        Assert.Equal(new MeshId(4), officer.Render!.Value.Mesh);

        Health h = Assert.IsType<Health>(blueprints.Instantiate("officer")[0]);
        Assert.Equal(150f, h.Max);
        Assert.Equal(3, h.Armor);
        Assert.Equal(Faction.Rebel, h.Side);
        Assert.Equal(5f, Assert.IsType<Weapon>(blueprints.Instantiate("officer")[1]).Damage);
        // The parent is unchanged.
        Assert.Equal(120f, Assert.IsType<Health>(blueprints.Instantiate("soldier")[0]).Max);
    }

    [Fact]
    public void FactoriesAreCompiledOncePerTypeAndFieldSet()
    {
        var blueprints = new Blueprints();
        blueprints.Register(new Blueprint("light", [new ComponentSpec(typeof(Weapon), Fields(("Damage", 1f), ("Range", 5.0)))]));
        blueprints.Register(new Blueprint("heavy", [new ComponentSpec(typeof(Weapon), Fields(("Range", 50.0), ("Damage", 9f)))]));
        blueprints.Register(new Blueprint("plain", [new ComponentSpec(typeof(Weapon), Fields(("Damage", 2f)))]));

        Assert.Same(blueprints.FactoryOf("light", 0), blueprints.FactoryOf("heavy", 0));
        Assert.NotSame(blueprints.FactoryOf("light", 0), blueprints.FactoryOf("plain", 0));
        Assert.Equal(9f, Assert.IsType<Weapon>(blueprints.Instantiate("heavy")[0]).Damage);
        Assert.Equal(5.0, Assert.IsType<Weapon>(blueprints.Instantiate("light")[0]).Range);
        Assert.Equal(10.0, Assert.IsType<Weapon>(blueprints.Instantiate("plain")[0]).Range);
    }

    [Theory]
    [InlineData("unknown field")]
    [InlineData("wrong type")]
    [InlineData("null value type")]
    [InlineData("overflow")]
    [InlineData("bad enum")]
    [InlineData("not a component")]
    [InlineData("no parameterless constructor")]
    [InlineData("abstract")]
    [InlineData("read-only field")]
    [InlineData("missing parent")]
    [InlineData("duplicate type")]
    [InlineData("null type")]
    [InlineData("no name")]
    public void RegisterRejectsInvalidBlueprints(string problem)
    {
        var blueprints = new Blueprints();
        Blueprint blueprint = problem switch
        {
            "unknown field" => new Blueprint("x", [new ComponentSpec(typeof(Health), Fields(("Maximum", 1f)))]),
            "wrong type" => new Blueprint("x", [new ComponentSpec(typeof(Health), Fields(("Max", "lots")))]),
            "null value type" => new Blueprint("x", [new ComponentSpec(typeof(Health), Fields(("Max", null)))]),
            "overflow" => new Blueprint("x", [new ComponentSpec(typeof(Radio), Fields(("Channel", 1e20)))]),
            "bad enum" => new Blueprint("x", [new ComponentSpec(typeof(Health), Fields(("Side", "Pirates")))]),
            "not a component" => new Blueprint("x", [new ComponentSpec(typeof(string))]),
            "no parameterless constructor" => new Blueprint("x", [new ComponentSpec(typeof(NeedsArgument))]),
            "abstract" => new Blueprint("x", [new ComponentSpec(typeof(AbstractComponent))]),
            "read-only field" => new Blueprint("x", [new ComponentSpec(typeof(Sealed), Fields(("Fixed", 4)))]),
            "missing parent" => new Blueprint("x", [], Parent: "nobody"),
            "duplicate type" => new Blueprint("x", [new ComponentSpec(typeof(Radio)), new ComponentSpec(typeof(Radio))]),
            "null type" => new Blueprint("x", [new ComponentSpec(null!)]),
            "no name" => new Blueprint(" ", []),
            _ => throw new ArgumentOutOfRangeException(nameof(problem)),
        };
        ArgumentException ex = Assert.ThrowsAny<ArgumentException>(() => blueprints.Register(blueprint));
        Assert.False(string.IsNullOrWhiteSpace(ex.Message));
        Assert.False(blueprints.Contains("x"));
    }

    [Fact]
    public void NamesAreUniqueAndLookupsOfUnknownNamesFail()
    {
        Blueprints blueprints = Soldiers();
        Assert.True(blueprints.Contains("soldier"));
        Assert.Equal(2, blueprints.Count);
        Assert.Throws<ArgumentException>(() => blueprints.Register(new Blueprint("soldier", [])));
        Assert.Throws<KeyNotFoundException>(() => blueprints.Instantiate("ghost"));
        Assert.Throws<KeyNotFoundException>(() => blueprints.Resolve("ghost"));
    }

    [Fact]
    public void TheWorldSpawnsBlueprintsOutsideAndDuringATick()
    {
        using World world = TestWorld.Create(out FrameHandle root, physics: TestPhysics.Create());
        foreach (string name in new[] { "soldier", "officer" })
            world.Blueprints.Register(Soldiers().Resolve(name) with { Name = name });
        world.Blueprints.Register(new Blueprint("outpost", [new ComponentSpec(typeof(Radio))], Frame: new FrameSpec(FrameBounds.Sphere(30), MotionDriver.Static)));

        Entity officer = world.Spawn("officer", root, TestWorld.At(3));
        Assert.Equal(150f, world.GetComponent<Health>(officer)!.Max);
        Assert.Equal(3, world.GetComponents(officer).Count);
        Assert.True(world.Snapshot.HasBody(officer));
        Assert.Equal(new MeshId(4), world.GetRender(officer).Mesh);
        Entity outpost = world.Spawn("outpost", root, TestWorld.At(100));
        Assert.False(world.GetOwnedFrame(outpost).IsNull);

        Entity recruit = default;
        world.SpawnWith(root, new Lambda(EventMask.Of<Tick>(), (Self self, scoped ref EventList events, scoped ref Outbox outbox) =>
        {
            if (self.Tick == 0)
                recruit = self.Spawn("soldier", self.Frame, TestWorld.At(-3));
        }));
        world.Tick();

        Assert.True(world.IsAlive(recruit));
        Assert.Equal(120f, world.GetComponent<Health>(recruit)!.Max);
        Assert.NotSame(world.GetComponent<Health>(officer), world.GetComponent<Health>(recruit));

        world.Post(Event.Create(recruit, Entity.Null, new Damage { Amount = 20 }));
        world.Run(2);
        Assert.Equal(100f, world.GetComponent<Health>(recruit)!.Current);
        Assert.Empty(world.Errors);
    }
}
