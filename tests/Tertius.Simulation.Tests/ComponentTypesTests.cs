using System.Reflection;
using AwesomeAssertions;

namespace Tertius.Simulation.Tests;

/// <summary>
/// The ids of the component types: the startup scan, the cap of 512 types, the refusal of generic types, and the same
/// checks for an assembly that loads later. The startup scan is tested on registries of their own, built from emitted
/// assemblies, and the later loads on the registry of the process, with assemblies that load after its scan.
/// </summary>
public sealed class ComponentTypesTests
{
    [Fact]
    public void TheStartupScanGivesTheComponentTypesIdsInTheOrderOfTheirFullNames()
    {
        string[] names = ["Zed", "Alpha", "Mid"];
        Assembly assembly = EmittedComponents.LoadLater(3, nameOf: i => names[i]);

        var registry = new ComponentTypeRegistry([assembly]);

        registry.Count.Should().Be(3);
        registry.IdOf(EmittedComponents.TypeNamed(assembly, "Alpha")).Should().Be(0);
        registry.IdOf(EmittedComponents.TypeNamed(assembly, "Mid")).Should().Be(1);
        registry.IdOf(EmittedComponents.TypeNamed(assembly, "Zed")).Should().Be(2);
        registry.AssertInvariants();
    }

    [Fact]
    public void TheStartupScanTakesUpTo512ComponentTypes()
    {
        var registry = new ComponentTypeRegistry([EmittedComponents.LoadLater(ComponentTypes.Capacity)]);

        registry.Count.Should().Be(ComponentTypes.Capacity);
    }

    [Fact]
    public void TheStartupScanFailsWithAClearErrorPast512ComponentTypes()
    {
        Assembly assembly = EmittedComponents.LoadLater(ComponentTypes.Capacity + 1);

        Action scan = () => _ = new ComponentTypeRegistry([assembly]);

        scan.Should().Throw<InvalidOperationException>().WithMessage("*513 component types*at most 512*");
    }

    [Fact]
    public void TheStartupScanRefusesAGenericComponentType()
    {
        Assembly assembly = EmittedComponents.LoadLater(1, withGeneric: true);

        Action scan = () => _ = new ComponentTypeRegistry([assembly]);

        scan.Should().Throw<InvalidOperationException>().WithMessage("Generic component types are refused*G`1*");
    }

    [Fact]
    public void ATypeOfAnAssemblyThatNoScanSawScansThatAssemblyWhole()
    {
        var registry = new ComponentTypeRegistry([]);

        int flammable = registry.IdOf(typeof(Flammable));

        registry.IdOf(typeof(Flammable)).Should().Be(flammable);
        registry.Count.Should().BeGreaterThan(5, "the scan of this test assembly gave all of its component types ids");
        registry.IdOf(typeof(Wet)).Should().BeLessThan(registry.Count);
        registry.AssertInvariants();
    }

    [Fact]
    public void ATypeOfADynamicAssemblyGetsItsIdWhenItIsFirstSeen()
    {
        var registry = new ComponentTypeRegistry([]);
        Type dynamic = EmittedComponents.InDynamicAssembly();

        int id = registry.IdOf(dynamic);

        id.Should().Be(0, "nothing else has an id in this registry");
        registry.IdOf(dynamic).Should().Be(0);
        registry.Count.Should().Be(1);
    }

    [Fact]
    public void ATypeOfADynamicAssemblyIsRefusedWhenEveryIdIsTaken()
    {
        var registry = new ComponentTypeRegistry([EmittedComponents.LoadLater(ComponentTypes.Capacity)]);

        Action lookUp = () => registry.IdOf(EmittedComponents.InDynamicAssembly());

        lookUp.Should().Throw<InvalidOperationException>().WithMessage("*every id is taken*");
    }

    [Fact]
    public void AnAbstractTypeOrAnInterfaceHasNoComponentTypeId()
    {
        Action abstractType = () => ComponentTypes.IdOf(typeof(Flag));
        Action interfaceType = () => ComponentTypes.IdOf(typeof(IComponent));
        Action other = () => ComponentTypes.IdOf(typeof(string));

        abstractType.Should().Throw<ArgumentException>().WithMessage("*not a concrete component type*");
        interfaceType.Should().Throw<ArgumentException>();
        other.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void AClosedGenericComponentTypeIsRefused()
    {
        Action lookUp = () => ComponentTypes.IdOf(EmittedComponents.OpenGeneric.MakeGenericType(typeof(int)));

        lookUp.Should().Throw<ArgumentException>().WithMessage("Generic component types are refused*");
    }

    [Fact]
    public void AnAssemblyThatLoadsLaterGetsItsIdsAfterTheTypesThatHaveThemInTheOrderOfTheirFullNames()
    {
        Assembly assembly = EmittedComponents.LoadLater(2, nameOf: i => i == 0 ? "Second" : "First");

        int first = ComponentTypes.IdOf(EmittedComponents.TypeNamed(assembly, "First"));
        int second = ComponentTypes.IdOf(EmittedComponents.TypeNamed(assembly, "Second"));

        second.Should().Be(first + 1, "the scan of the assembly gave both types ids, in the order of their names");
        ComponentTypes.Registry.AssertInvariants();
    }

    [Fact]
    public void AnAssemblyThatLoadsLaterWithAGenericComponentTypeIsRefusedWhole()
    {
        Assembly assembly = EmittedComponents.LoadLater(1, withGeneric: true);

        Action lookUp = () => ComponentTypes.IdOf(EmittedComponents.TypeNamed(assembly, "C0"));

        lookUp.Should().Throw<InvalidOperationException>().WithMessage("Generic component types are refused*G`1*");
        lookUp.Should().Throw<InvalidOperationException>("the refusal stays");
    }

    [Fact]
    public void AnAssemblyThatLoadsLaterAndWouldPassTheCapAddsNoneOfItsTypes()
    {
        Assembly assembly = EmittedComponents.LoadLater(ComponentTypes.Capacity + 10);
        int before = ComponentTypes.Count;

        Action lookUp = () => ComponentTypes.IdOf(EmittedComponents.TypeNamed(assembly, "C0"));

        lookUp
            .Should()
            .Throw<InvalidOperationException>()
            .WithMessage($"*{ComponentTypes.Capacity + 10} component types*cap of 512*");
        ComponentTypes
            .Count.Should()
            .BeLessThanOrEqualTo(before + 1, "only another test's dynamic type may have joined meanwhile");
        ComponentTypes.Registry.AssertInvariants();
    }

    [Fact]
    public void SpawningAComponentOfARefusedTypeFailsBeforeAnythingChanges()
    {
        Assembly assembly = EmittedComponents.LoadLater(1, withGeneric: true);
        using var t = new TestWorld();
        // A free slot that the failed spawn must not take.
        Entity probe = t.SpawnWith();
        t.World.Destroy(probe);
        int live = t.World.Stats.LiveEntities;

        Action spawn = () => t.SpawnWith(EmittedComponents.Create(assembly, "C0"));

        spawn.Should().Throw<InvalidOperationException>().WithMessage("Generic component types are refused*");
        t.World.Stats.LiveEntities.Should().Be(live);
        t.SpawnWith().Index.Should().Be(probe.Index, "the failed spawn reserved no slot");
    }

    [Fact]
    public void AddingAComponentOfARefusedTypeOutsideATickFailsBeforeAnythingChanges()
    {
        Assembly assembly = EmittedComponents.LoadLater(1, withGeneric: true);
        using var t = new TestWorld();
        Entity entity = t.SpawnWith(new Metal());

        Action add = () => t.World.AddComponent(entity, EmittedComponents.Create(assembly, "C0"));

        add.Should().Throw<InvalidOperationException>();
        t.World.GetComponents(entity).Should().ContainSingle();
    }
}
