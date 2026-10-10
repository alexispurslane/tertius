using AwesomeAssertions;
using static Tertius.Simulation.Tests.BlueprintSamples;

namespace Tertius.Simulation.Tests;

/// <summary>
/// Making components from a blueprint, and spawning entities from one. Reflection runs once, when a blueprint is
/// registered; these tests check what <see cref="Blueprints.Instantiate"/> and the world's spawn methods do with the
/// result. Inheritance and invalid input have their own files.
/// </summary>
public class BlueprintTests
{
    // ---- Instantiate ------------------------------------------------------------------------------------------

    [Fact]
    public void AnInstantiatedBlueprintHasOneComponentPerSpecInBlueprintOrder()
    {
        IComponent[] components = Soldiers().Instantiate("soldier");

        components.Select(c => c.GetType()).Should().Equal(typeof(Health), typeof(Weapon));
    }

    [Fact]
    public void ANumberIsConvertedToTheNumericTypeOfTheFieldItSets()
    {
        var blueprints = new Blueprints();
        blueprints.Register(new Blueprint("radio", [new ComponentSpec(typeof(Radio), Fields(("Channel", (byte)3)))]));
        blueprints.Register(new Blueprint("weapon", [new ComponentSpec(typeof(Weapon), Fields(("Damage", 5.0), ("Range", 30)))]));

        // A byte for an int field, a double for a float field and an int for a double property.
        ((Radio)blueprints.Instantiate("radio")[0]).Channel.Should().Be(3);
        var weapon = (Weapon)blueprints.Instantiate("weapon")[0];
        weapon.Damage.Should().Be(5f);
        weapon.Range.Should().Be(30.0);
    }

    [Fact]
    public void AnEnumNameIsConvertedToTheEnumValue()
    {
        var health = (Health)Soldiers().Instantiate("soldier")[0];

        health.Side.Should().Be(Faction.Rebel);
    }

    [Fact]
    public void ANumberIsConvertedToTheEnumValueWithThatNumber()
    {
        var blueprints = new Blueprints();
        blueprints.Register(new Blueprint("enemy", [new ComponentSpec(typeof(Health), Fields(("Side", 2)))]));

        // Faction.Empire is the third value of the enum, so its number is 2.
        ((Health)blueprints.Instantiate("enemy")[0]).Side.Should().Be(Faction.Empire);
    }

    [Fact]
    public void APropertyWithASetterTakesItsValueFromTheBlueprint()
    {
        var health = (Health)Soldiers().Instantiate("soldier")[0];

        // Health.Current starts at 100 when nobody sets it; the soldier blueprint sets 120 through the property.
        health.Current.Should().Be(120f);
    }

    [Fact]
    public void AnInitOnlyPropertyTakesItsValueFromTheBlueprint()
    {
        var weapon = (Weapon)Soldiers().Instantiate("soldier")[1];

        weapon.Range.Should().Be(30);
    }

    [Fact]
    public void AFieldThatTheBlueprintDoesNotMentionKeepsTheComponentsOwnDefault()
    {
        var blueprints = new Blueprints();
        blueprints.Register(new Blueprint("plain", [new ComponentSpec(typeof(Weapon), Fields(("Damage", 2f)))]));
        var weapon = (Weapon)blueprints.Instantiate("plain")[0];

        weapon.Damage.Should().Be(2f);
        weapon.Range.Should().Be(10.0);
    }

    [Fact]
    public void AComponentWithNoFieldValuesIsBuiltWithItsDefaults()
    {
        var blueprints = new Blueprints();
        blueprints.Register(new Blueprint("radio", [new ComponentSpec(typeof(Radio))]));

        ((Radio)blueprints.Instantiate("radio")[0]).Channel.Should().Be(1);
    }

    [Fact]
    public void ANullableFieldTakesAPlainValue()
    {
        var blueprints = new Blueprints();
        blueprints.Register(new Blueprint("tag", [new ComponentSpec(typeof(Labelled), Fields(("Level", 8)))]));

        ((Labelled)blueprints.Instantiate("tag")[0]).Level.Should().Be(8);
    }

    [Fact]
    public void ANullValueClearsAFieldThatHasANonNullDefault()
    {
        var blueprints = new Blueprints();
        blueprints.Register(new Blueprint("tag", [new ComponentSpec(typeof(Labelled), Fields(("Label", null), ("Level", null)))]));

        var labelled = (Labelled)blueprints.Instantiate("tag")[0];

        labelled.Label.Should().BeNull();
        labelled.Level.Should().BeNull();
    }

    [Fact]
    public void EveryInstantiateMakesNewComponentInstances()
    {
        Blueprints blueprints = Soldiers();

        IComponent[] first = blueprints.Instantiate("soldier");
        IComponent[] second = blueprints.Instantiate("soldier");

        second[0].Should().NotBeSameAs(first[0]);
        second[1].Should().NotBeSameAs(first[1]);
    }

    [Fact]
    public void AReferenceValueInABlueprintIsTheSameObjectInEveryInstance()
    {
        // Documented: field values are shared. Reflection runs once at registration, so the value is stored, not copied.
        int[] marks = [1, 2, 3];
        var blueprints = new Blueprints();
        blueprints.Register(new Blueprint("tag", [new ComponentSpec(typeof(Labelled), Fields(("Marks", marks)))]));

        var first = (Labelled)blueprints.Instantiate("tag")[0];
        var second = (Labelled)blueprints.Instantiate("tag")[0];

        first.Marks.Should().BeSameAs(marks);
        second.Marks.Should().BeSameAs(marks);
    }

    [Fact]
    public void ABlueprintWithNoComponentsInstantiatesToNothing()
    {
        var blueprints = new Blueprints();
        blueprints.Register(new Blueprint("empty", []));

        blueprints.Instantiate("empty").Should().BeEmpty();
    }

    [Fact]
    public void ABlueprintWithAClosedGenericComponentTypeBuildsIt()
    {
        var blueprints = new Blueprints();
        blueprints.Register(new Blueprint("box", [new ComponentSpec(typeof(Boxed<int>), Fields(("Content", 42)))]));

        ((Boxed<int>)blueprints.Instantiate("box")[0]).Content.Should().Be(42);
    }

    // ---- The compiled factories -------------------------------------------------------------------------------

    [Fact]
    public void FieldValuesLandInTheRightFieldsWhateverOrderTheBlueprintListsThem()
    {
        var blueprints = new Blueprints();
        blueprints.Register(new Blueprint("light", [new ComponentSpec(typeof(Weapon), Fields(("Damage", 1f), ("Range", 5.0)))]));
        blueprints.Register(new Blueprint("heavy", [new ComponentSpec(typeof(Weapon), Fields(("Range", 50.0), ("Damage", 9f)))]));

        var light = (Weapon)blueprints.Instantiate("light")[0];
        var heavy = (Weapon)blueprints.Instantiate("heavy")[0];

        (light.Damage, light.Range).Should().Be((1f, 5.0));
        (heavy.Damage, heavy.Range).Should().Be((9f, 50.0));
    }

    [Fact]
    public void BlueprintsThatSetTheSameFieldsShareOneCompiledFactory()
    {
        // Reflection and expression compiling are the costly steps. They happen once per component type and set of field names.
        var blueprints = new Blueprints();
        blueprints.Register(new Blueprint("light", [new ComponentSpec(typeof(Weapon), Fields(("Damage", 1f), ("Range", 5.0)))]));
        blueprints.Register(new Blueprint("heavy", [new ComponentSpec(typeof(Weapon), Fields(("Range", 50.0), ("Damage", 9f)))]));

        blueprints.FactoryOf("heavy", 0).Should().BeSameAs(blueprints.FactoryOf("light", 0));
    }

    [Fact]
    public void BlueprintsThatSetDifferentFieldsDoNotShareAFactory()
    {
        var blueprints = new Blueprints();
        blueprints.Register(new Blueprint("light", [new ComponentSpec(typeof(Weapon), Fields(("Damage", 1f), ("Range", 5.0)))]));
        blueprints.Register(new Blueprint("plain", [new ComponentSpec(typeof(Weapon), Fields(("Damage", 2f)))]));

        blueprints.FactoryOf("plain", 0).Should().NotBeSameAs(blueprints.FactoryOf("light", 0));
    }

    [Fact]
    public void TwoRegistriesShareTheFactoriesOfTheSameTypeAndFieldSet()
    {
        var first = new Blueprints();
        var second = new Blueprints();
        first.Register(new Blueprint("a", [new ComponentSpec(typeof(Radio), Fields(("Channel", 4)))]));
        second.Register(new Blueprint("b", [new ComponentSpec(typeof(Radio), Fields(("Channel", 9)))]));

        second.FactoryOf("b", 0).Should().BeSameAs(first.FactoryOf("a", 0));
        ((Radio)second.Instantiate("b")[0]).Channel.Should().Be(9);
    }

    // ---- Spawning in a world ----------------------------------------------------------------------------------

    [Fact]
    public void ASpawnedBlueprintHasFreshComponentsWithTheBlueprintsValues()
    {
        using var t = new TestWorld();
        Soldiers(t.World.Blueprints);

        Entity officer = t.World.Spawn("officer", t.Root, TestWorld.At(3));

        t.World.GetComponents(officer).Select(c => c.GetType()).Should().Equal(typeof(Health), typeof(Weapon), typeof(Radio));
        t.World.GetComponent<Health>(officer)!.Max.Should().Be(150f);
    }

    [Fact]
    public void ASpawnedBlueprintIsInTheGivenFrameAtTheGivenPose()
    {
        using var t = new TestWorld();
        Soldiers(t.World.Blueprints);
        FrameHandle deck = t.SpawnStaticFrame(TestWorld.At(1000));

        Entity soldier = t.World.Spawn("soldier", deck, TestWorld.At(3, 4, 5));

        t.World.GetFrame(soldier).Should().Be(deck);
        t.World.GetPose(soldier).Should().Be(TestWorld.At(3, 4, 5));
    }

    [Fact]
    public void ASpawnedBlueprintGetsItsRenderRow()
    {
        using var t = new TestWorld();
        Soldiers(t.World.Blueprints);

        Entity soldier = t.World.Spawn("soldier", t.Root, TestWorld.At(0));

        t.World.GetRender(soldier).Mesh.Should().Be(new MeshId(4));
    }

    [Fact]
    public void ASpawnedBlueprintWithABodyHasABody()
    {
        using var t = new TestWorld();
        Soldiers(t.World.Blueprints);

        Entity soldier = t.World.Spawn("soldier", t.Root, TestWorld.At(0));

        t.World.Snapshot.HasBody(soldier).Should().BeTrue();
    }

    [Fact]
    public void ASpawnedBlueprintsBodyIsHandedToPhysics()
    {
        IPhysicsBackend physics = TestPhysics.Create();
        List<ChangeBatch> batches = physics.RecordChanges();
        using var t = new TestWorld(physics);
        Soldiers(t.World.Blueprints);
        Entity soldier = t.World.Spawn("soldier", t.Root, TestWorld.At(7));

        t.Run(1);

        // The first batch holds the root frame and everything spawned before tick 0.
        BodyCreate create = batches[0].BodiesCreated.Should().ContainSingle().Subject;
        create.Entity.Should().Be(soldier);
        create.Frame.Should().Be(t.Root);
        create.Desc.Should().BeSameAs(TestWorld.Ball);
    }

    [Fact]
    public void ASpawnedBlueprintWithoutABodyHasNone()
    {
        using var t = new TestWorld();
        t.World.Blueprints.Register(new Blueprint("crate", [new ComponentSpec(typeof(Radio))]));

        Entity crate = t.World.Spawn("crate", t.Root, TestWorld.At(0));

        t.World.Snapshot.HasBody(crate).Should().BeFalse();
    }

    [Fact]
    public void ABlueprintWithAFrameSpecSpawnsAFrame()
    {
        using var t = new TestWorld();
        t.World.Blueprints.Register(new Blueprint("outpost", [new ComponentSpec(typeof(Radio))],
            Frame: new FrameSpec(FrameBounds.Sphere(30), MotionDriver.Static)));

        Entity outpost = t.World.Spawn("outpost", t.Root, TestWorld.At(100));

        FrameHandle frame = t.World.GetOwnedFrame(outpost);
        frame.IsNull.Should().BeFalse();
        t.World.Frames.Parent(frame).Should().Be(t.Root);
    }

    [Fact]
    public void TwoEntitiesSpawnedFromOneBlueprintDoNotShareComponents()
    {
        using var t = new TestWorld();
        Soldiers(t.World.Blueprints);

        Entity first = t.World.Spawn("soldier", t.Root, TestWorld.At(0));
        Entity second = t.World.Spawn("soldier", t.Root, TestWorld.At(1));

        t.World.GetComponent<Health>(second).Should().NotBeSameAs(t.World.GetComponent<Health>(first));
    }

    [Fact]
    public void AComponentCanSpawnABlueprintDuringATick()
    {
        using var t = new TestWorld();
        Soldiers(t.World.Blueprints);
        Entity recruit = default;
        t.SpawnWith(Lambda.OnTick((self, scoped ref events, scoped ref outbox) =>
        {
            if (self.Tick == 0)
                recruit = self.Spawn("soldier", self.Frame, TestWorld.At(-3));
        }));

        t.Run(1);

        t.World.IsAlive(recruit).Should().BeTrue();
        t.World.GetComponent<Health>(recruit)!.Max.Should().Be(120f);
        t.World.GetPose(recruit).Should().Be(TestWorld.At(-3));
        t.World.Errors.Should().BeEmpty();
    }

    [Fact]
    public void AnEntityMadeFromABlueprintHandlesMessagesWithItsBlueprintComponents()
    {
        using var t = new TestWorld();
        Soldiers(t.World.Blueprints);
        Entity soldier = t.World.Spawn("soldier", t.Root, TestWorld.At(0));

        t.World.Post(Event.Create(soldier, Entity.Null, new Damage { Amount = 20 }));
        t.Run(2);

        // The soldier blueprint sets Current to 120.
        t.World.GetComponent<Health>(soldier)!.Current.Should().Be(100f);
    }

    [Fact]
    public void SpawningAnUnknownBlueprintThrowsAndCreatesNoEntity()
    {
        using var t = new TestWorld();
        int before = t.World.Stats.LiveEntities;

        Action spawn = () => t.World.Spawn("ghost", t.Root, TestWorld.At(0));

        spawn.Should().Throw<KeyNotFoundException>().WithMessage("*ghost*");
        t.World.Stats.LiveEntities.Should().Be(before);
    }

    [Fact]
    public void ASpawnOfAnUnknownBlueprintDuringATickIsReportedAndSpawnsNothing()
    {
        using var t = new TestWorld();
        t.SpawnWith(Lambda.OnTick((self, scoped ref events, scoped ref outbox) =>
        {
            if (self.Tick == 0)
                self.Spawn("ghost", self.Frame, TestWorld.At(0));
        }));
        int before = t.World.Stats.LiveEntities;

        t.Run(1);

        t.World.Errors.Should().ContainSingle().Which.Should().Contain("KeyNotFoundException").And.Contain("ghost");
        t.World.Stats.LiveEntities.Should().Be(before);
    }
}
