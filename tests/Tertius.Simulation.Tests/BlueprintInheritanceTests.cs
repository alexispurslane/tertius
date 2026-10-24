using AwesomeAssertions;
using static Tertius.Simulation.Tests.BlueprintSamples;

namespace Tertius.Simulation.Tests;

/// <summary>
/// Blueprints that name a parent. <see cref="Blueprints.Register"/> merges the parent's resolved blueprint with the
/// child: a component type that both list keeps the parent's place and its field values merge, with the child's
/// values winning; other component types follow the parent's, in order; the child's body, render row and frame
/// replace the parent's when it has them. The tests use the "soldier" and "officer" blueprints of
/// <see cref="BlueprintSamples.Soldiers"/>.
/// </summary>
public sealed class BlueprintInheritanceTests
{
    private static readonly FrameSpec Outpost = new(FrameBounds.Sphere(30));

    // ---- Components and fields --------------------------------------------------------------------------------

    [Fact]
    public void AChildListsItsParentsComponentsFirstAndItsOwnNewOnesAfter()
    {
        Blueprint officer = Soldiers().Resolve("officer");

        // The officer lists the Radio first and the Health second. The soldier's Health and Weapon still come first.
        officer.Components.Select(c => c.Type).Should().Equal(typeof(Health), typeof(Weapon), typeof(Radio));
    }

    [Fact]
    public void AComponentTypeThatBothListKeepsTheParentsPlaceEvenIfTheChildListsItLater()
    {
        Blueprints blueprints = Soldiers();
        blueprints.Register(new(Name: "veteran",
            Components: [new(typeof(Weapon), Fields(("Damage", 9f))), new(typeof(Health))],
            Parent: "soldier"));

        blueprints.Resolve("veteran").Components.Select(c => c.Type).Should().Equal(typeof(Health), typeof(Weapon));
    }

    [Fact]
    public void AChildsFieldValuesWinOverItsParents()
    {
        IReadOnlyDictionary<string, object?> health = Soldiers().Resolve("officer").Components[0].Fields!;

        health["Max"].Should().Be(150f);
    }

    [Fact]
    public void AChildAddsFieldsTheParentDoesNotSet()
    {
        IReadOnlyDictionary<string, object?> health = Soldiers().Resolve("officer").Components[0].Fields!;

        health["Name"].Should().Be("Boss");
        health["Armor"].Should().Be(3);
    }

    [Fact]
    public void AChildKeepsTheFieldsOfItsParentThatItDoesNotMention()
    {
        IReadOnlyDictionary<string, object?> health = Soldiers().Resolve("officer").Components[0].Fields!;

        health["Side"].Should().Be(Faction.Rebel);
        health["Current"].Should().Be(120f);
    }

    [Fact]
    public void AComponentOfTheParentThatTheChildDoesNotListKeepsAllItsFields()
    {
        IReadOnlyDictionary<string, object?> weapon = Soldiers().Resolve("officer").Components[1].Fields!;

        weapon.ToDictionary(f => f.Key, f => f.Value).Should().Equal(new Dictionary<string, object?>
            { ["Damage"] = 5f, ["Range"] = 30.0 });
    }

    [Fact]
    public void AnInstantiatedChildHasTheMergedFieldValues()
    {
        IComponent[] components = Soldiers().Instantiate("officer");

        var health = (Health)components[0];
        health.Max.Should().Be(150f);
        health.Current.Should().Be(120f);
        health.Side.Should().Be(Faction.Rebel);
        health.Name.Should().Be("Boss");
        health.Armor.Should().Be(3);
        ((Weapon)components[1]).Damage.Should().Be(5f);
    }

    [Fact]
    public void AChildCanSetAnInheritedFieldToNull()
    {
        var blueprints = new Blueprints();
        blueprints.Register(new(Name: "parent",
            Components: [new(typeof(Labelled), Fields(("Label", "parent label")))]));
        blueprints.Register(new(Name: "child", Components: [new(typeof(Labelled), Fields(("Label", null)))],
            Parent: "parent"));

        ((Labelled)blueprints.Instantiate("child")[0]).Label.Should().BeNull();
    }

    [Fact]
    public void AGrandchildMergesWithTheWholeChainOfItsAncestors()
    {
        var blueprints = new Blueprints();
        blueprints.Register(new(Name: "a", Components: [new(typeof(Labelled), Fields(("Label", "a"), ("Level", 1)))]));
        blueprints.Register(new(Name: "b", Components: [new(typeof(Labelled), Fields(("Level", 2)))], Parent: "a"));
        blueprints.Register(new(Name: "c",
            Components: [new(typeof(Radio)), new(typeof(Labelled), Fields(("Marks", new[] { 7 })))],
            Parent: "b"));

        IComponent[] components = blueprints.Instantiate("c");

        components.Select(x => x.GetType()).Should().Equal(typeof(Labelled), typeof(Radio));
        var labelled = (Labelled)components[0];
        // "Label" comes from a, "Level" from b (b overrides a), and "Marks" from c.
        labelled.Label.Should().Be("a");
        labelled.Level.Should().Be(2);
        labelled.Marks.Should().Equal(7);
    }

    // ---- Body, render row and frame ---------------------------------------------------------------------------

    [Fact]
    public void AChildInheritsItsParentsBodyRenderRowAndFrame()
    {
        var blueprints = new Blueprints();
        var render = new RenderData { Mesh = new(2) };
        blueprints.Register(new(Name: "hull", Components: [], Body: TestWorld.Ball, Render: render, Frame: Outpost));
        blueprints.Register(new(Name: "ship", Components: [new(typeof(Radio))], Parent: "hull"));

        Blueprint ship = blueprints.Resolve("ship");

        ship.Body.Should().BeSameAs(TestWorld.Ball);
        ship.Render!.Value.Mesh.Should().Be(new MeshId(2));
        ship.Frame.Should().BeSameAs(Outpost);
    }

    [Fact]
    public void AChildsOwnBodyReplacesItsParents()
    {
        var blueprints = new Blueprints();
        var lighter = new BodyDesc(Shape: new SphereShapeDesc(0.25f), Mass: 0.1f);
        blueprints.Register(new(Name: "parent", Components: [], Body: TestWorld.Ball));
        blueprints.Register(new(Name: "child", Components: [], Body: lighter, Parent: "parent"));

        blueprints.Resolve("child").Body.Should().BeSameAs(lighter);
    }

    [Fact]
    public void AChildsOwnRenderRowReplacesItsParents()
    {
        var blueprints = new Blueprints();
        blueprints.Register(new(Name: "parent", Components: [], Render: new RenderData { Mesh = new(1) }));
        blueprints.Register(new(Name: "child", Components: [], Render: new RenderData { Mesh = new(9) },
            Parent: "parent"));

        blueprints.Resolve("child").Render!.Value.Mesh.Should().Be(new MeshId(9));
    }

    [Fact]
    public void AChildsOwnFrameSpecReplacesItsParents()
    {
        var blueprints = new Blueprints();
        var bigger = new FrameSpec(FrameBounds.Sphere(300));
        blueprints.Register(new(Name: "parent", Components: [], Frame: Outpost));
        blueprints.Register(new(Name: "child", Components: [], Frame: bigger, Parent: "parent"));

        blueprints.Resolve("child").Frame.Should().BeSameAs(bigger);
    }

    [Fact]
    public void AParentWithNoBodyRenderRowOrFrameGivesItsChildNone()
    {
        var blueprints = new Blueprints();
        blueprints.Register(new(Name: "crate", Components: []));
        blueprints.Register(new(Name: "big crate", Components: [], Parent: "crate"));

        Blueprint bigCrate = blueprints.Resolve("big crate");

        bigCrate.Body.Should().BeNull();
        bigCrate.Render.Should().BeNull();
        bigCrate.Frame.Should().BeNull();
    }

    [Fact]
    public void AChildOfAChildInheritsTheBodyOfTheFirstAncestorThatHasOne()
    {
        Blueprints blueprints = Soldiers();
        blueprints.Register(new(Name: "recruit", Components: [], Parent: "officer"));

        blueprints.Resolve("recruit").Body.Should().BeSameAs(TestWorld.Ball);
    }

    // ---- What a resolved blueprint looks like -----------------------------------------------------------------

    [Fact]
    public void AResolvedBlueprintNamesNoParentAndKeepsItsOwnName()
    {
        Blueprint officer = Soldiers().Resolve("officer");

        officer.Parent.Should().BeNull();
        officer.Name.Should().Be("officer");
    }

    [Fact]
    public void RegisteringAChildLeavesItsParentUnchanged()
    {
        Blueprints blueprints = Soldiers();

        var soldierHealth = (Health)blueprints.Instantiate("soldier")[0];

        soldierHealth.Max.Should().Be(120f);
        soldierHealth.Name.Should().BeNull();
        blueprints.Resolve("soldier").Components.Select(c => c.Type).Should().Equal(typeof(Health), typeof(Weapon));
    }

    // ---- In a world -------------------------------------------------------------------------------------------

    [Fact]
    public void AnEntitySpawnedFromAChildHasTheBodyAndRenderRowOfItsParent()
    {
        using var t = new TestWorld();
        Soldiers(t.World.Blueprints);

        Entity officer = t.World.Spawn("officer", t.Root, TestWorld.At(3));

        t.World.Snapshot.HasBody(officer).Should().BeTrue();
        t.World.GetRender(officer).Mesh.Should().Be(new MeshId(4));
    }
}
