using AwesomeAssertions;
using static Tertius.Simulation.Tests.BlueprintSamples;

namespace Tertius.Simulation.Tests;

/// <summary>
/// Invalid input to the blueprint registry. <see cref="Blueprints.Register"/> checks everything that reflection could
/// get wrong when a blueprint is registered, so that a bad blueprint fails at load time with a message that names the
/// blueprint and the member, and never in the middle of a tick. A rejected blueprint registers nothing. The lookup
/// methods fail with <see cref="KeyNotFoundException"/> for a name that nobody registered.
/// </summary>
public class BlueprintValidationTests
{
    /// <summary>
    /// A blueprint named "x" with the named defect, and a piece of the message that must say what the defect is.
    /// Every message must also name the blueprint, except for the defects of the name itself.
    /// </summary>
    private static (Blueprint Blueprint, string MessageHas) Invalid(string problem) => problem switch
    {
        "unknown field" => (new Blueprint("x", [new ComponentSpec(typeof(Health), Fields(("Maximum", 1f)))]), "Maximum"),
        "field name in the wrong case" => (new Blueprint("x", [new ComponentSpec(typeof(Health), Fields(("max", 1f)))]), "max"),
        "string for a number" => (new Blueprint("x", [new ComponentSpec(typeof(Health), Fields(("Max", "lots")))]), "Max"),
        "number for a string" => (new Blueprint("x", [new ComponentSpec(typeof(Health), Fields(("Name", 5)))]), "Name"),
        "null for a value type" => (new Blueprint("x", [new ComponentSpec(typeof(Health), Fields(("Max", null)))]), "cannot be null"),
        "number too large for the field" => (new Blueprint("x", [new ComponentSpec(typeof(Radio), Fields(("Channel", 1e20)))]), "Channel"),
        "unknown enum name" => (new Blueprint("x", [new ComponentSpec(typeof(Health), Fields(("Side", "Pirates")))]), "Side"),
        "enum name in the wrong case" => (new Blueprint("x", [new ComponentSpec(typeof(Health), Fields(("Side", "rebel")))]), "Side"),
        "read-only field" => (new Blueprint("x", [new ComponentSpec(typeof(Sealed), Fields(("Fixed", 4)))]), "Fixed"),
        "property without a setter" => (new Blueprint("x", [new ComponentSpec(typeof(Labelled), Fields(("Computed", 1)))]), "Computed"),
        "property with a private setter" => (new Blueprint("x", [new ComponentSpec(typeof(Labelled), Fields(("Locked", 1)))]), "Locked"),
        "constant" => (new Blueprint("x", [new ComponentSpec(typeof(Labelled), Fields(("Limit", 1)))]), "Limit"),
        "static field" => (new Blueprint("x", [new ComponentSpec(typeof(Labelled), Fields(("Counter", 1)))]), "Counter"),
        "non-public field" => (new Blueprint("x", [new ComponentSpec(typeof(Labelled), Fields(("Internal", 1)))]), "Internal"),
        "type that is not a component" => (new Blueprint("x", [new ComponentSpec(typeof(string))]), "IComponent"),
        "abstract component" => (new Blueprint("x", [new ComponentSpec(typeof(AbstractComponent))]), "concrete class"),
        "struct component" => (new Blueprint("x", [new ComponentSpec(typeof(ValueComponent))]), "concrete class"),
        "open generic component" => (new Blueprint("x", [new ComponentSpec(typeof(Boxed<>))]), "concrete class"),
        "component with no parameterless constructor" => (new Blueprint("x", [new ComponentSpec(typeof(NeedsArgument))]), "parameterless constructor"),
        "component with no public constructor" => (new Blueprint("x", [new ComponentSpec(typeof(HiddenConstructor))]), "parameterless constructor"),
        "same component type twice" => (new Blueprint("x", [new ComponentSpec(typeof(Radio)), new ComponentSpec(typeof(Radio))]), "Radio"),
        "component spec with no type" => (new Blueprint("x", [new ComponentSpec(null!)]), "no type"),
        "null component spec" => (new Blueprint("x", [null!]), "null component spec"),
        "no component list" => (new Blueprint("x", null!), "component list"),
        "parent that is not registered" => (new Blueprint("x", [], Parent: "nobody"), "nobody"),
        "its own name as the parent" => (new Blueprint("x", [], Parent: "x"), "not registered"),
        "invalid spec after a valid one" => (new Blueprint("x", [new ComponentSpec(typeof(Radio)), new ComponentSpec(typeof(Health), Fields(("Maximum", 1f)))]), "Maximum"),
        "empty name" => (new Blueprint("", []), "needs a name"),
        "blank name" => (new Blueprint("   ", []), "needs a name"),
        _ => throw new ArgumentOutOfRangeException(nameof(problem), problem, null),
    };

    private static readonly string[] Problems =
    [
        "unknown field", "field name in the wrong case", "string for a number", "number for a string", "null for a value type",
        "number too large for the field", "unknown enum name", "enum name in the wrong case", "read-only field",
        "property without a setter", "property with a private setter", "constant", "static field", "non-public field",
        "type that is not a component", "abstract component", "struct component", "open generic component",
        "component with no parameterless constructor", "component with no public constructor", "same component type twice",
        "component spec with no type", "null component spec", "no component list", "parent that is not registered",
        "its own name as the parent", "invalid spec after a valid one", "empty name", "blank name",
    ];

    public static TheoryData<string> InvalidBlueprints => new(Problems);

    public static TheoryData<string> InvalidBlueprintsWithAName => new(Problems.Where(p => p is not ("empty name" or "blank name")));

    [Theory]
    [MemberData(nameof(InvalidBlueprints))]
    public void ABlueprintWithADefectIsRejectedAndSaysWhatTheDefectIs(string problem)
    {
        var blueprints = new Blueprints();
        (Blueprint blueprint, string messageHas) = Invalid(problem);

        Action register = () => blueprints.Register(blueprint);

        register.Should().Throw<ArgumentException>().Where(ex => ex.Message.Contains(messageHas));
    }

    [Theory]
    [MemberData(nameof(InvalidBlueprints))]
    public void ARejectedBlueprintRegistersNothing(string problem)
    {
        var blueprints = new Blueprints();
        (Blueprint blueprint, _) = Invalid(problem);

        // The rejection itself is the subject of the first test; this one looks only at what is left behind.
        Record.Exception(() => blueprints.Register(blueprint));

        blueprints.Count.Should().Be(0);
        blueprints.Contains("x").Should().BeFalse();
    }

    [Theory]
    [MemberData(nameof(InvalidBlueprintsWithAName))]
    public void TheMessageOfARejectedBlueprintNamesTheBlueprint(string problem)
    {
        var blueprints = new Blueprints();
        (Blueprint blueprint, _) = Invalid(problem);

        Action register = () => blueprints.Register(blueprint);

        register.Should().Throw<ArgumentException>().Where(ex => ex.Message.Contains("'x'"));
    }

    [Fact]
    public void ARejectedNameCanBeRegisteredLaterWithAValidBlueprint()
    {
        var blueprints = new Blueprints();
        Action bad = () => blueprints.Register(Invalid("unknown field").Blueprint);
        bad.Should().Throw<ArgumentException>();

        blueprints.Register(new Blueprint("x", [new ComponentSpec(typeof(Radio))]));

        blueprints.Instantiate("x").Should().ContainSingle().Which.Should().BeOfType<Radio>();
    }

    [Fact]
    public void AParentMustBeRegisteredBeforeItsChild()
    {
        var blueprints = new Blueprints();
        var child = new Blueprint("officer", [], Parent: "soldier");
        Action early = () => blueprints.Register(child);
        early.Should().Throw<ArgumentException>().Where(ex => ex.Message.Contains("soldier"));

        blueprints.Register(new Blueprint("soldier", [new ComponentSpec(typeof(Radio))]));
        blueprints.Register(child);

        blueprints.Resolve("officer").Components.Select(c => c.Type).Should().Equal(typeof(Radio));
    }

    [Fact]
    public void RegisteringNothingIsRejectedWithANullArgumentError()
    {
        var blueprints = new Blueprints();

        Action register = () => blueprints.Register(null!);

        register.Should().Throw<ArgumentNullException>();
    }

    // ---- Names ------------------------------------------------------------------------------------------------

    [Fact]
    public void ANameThatIsAlreadyRegisteredIsRejectedAndTheFirstBlueprintStays()
    {
        Blueprints blueprints = Soldiers();

        Action again = () => blueprints.Register(new Blueprint("soldier", []));

        again.Should().Throw<ArgumentException>().Where(ex => ex.Message.Contains("'soldier'"));
        blueprints.Instantiate("soldier").Should().HaveCount(2);
    }

    [Fact]
    public void BlueprintNamesAreCaseSensitive()
    {
        Blueprints blueprints = Soldiers();

        blueprints.Register(new Blueprint("Soldier", []));

        blueprints.Instantiate("Soldier").Should().BeEmpty();
        blueprints.Instantiate("soldier").Should().HaveCount(2);
    }

    [Fact]
    public void TheRegistryCountsAndFindsTheBlueprintsRegistered()
    {
        Blueprints blueprints = Soldiers();

        blueprints.Count.Should().Be(2);
        blueprints.Contains("soldier").Should().BeTrue();
        blueprints.Contains("officer").Should().BeTrue();
        blueprints.Contains("ghost").Should().BeFalse();
    }

    [Fact]
    public void InstantiatingAnUnknownNameFails()
    {
        Action instantiate = () => Soldiers().Instantiate("ghost");

        instantiate.Should().Throw<KeyNotFoundException>().WithMessage("*ghost*");
    }

    [Fact]
    public void ResolvingAnUnknownNameFails()
    {
        Action resolve = () => Soldiers().Resolve("ghost");

        resolve.Should().Throw<KeyNotFoundException>().WithMessage("*ghost*");
    }

    [Fact]
    public void CreatingASpawnDescriptionForAnUnknownNameFails()
    {
        Action create = () => Soldiers().CreateSpawnDesc("ghost", default, DPose.Identity);

        create.Should().Throw<KeyNotFoundException>().WithMessage("*ghost*");
    }

    [Fact]
    public void LookingUpANullNameFails()
    {
        Action instantiate = () => Soldiers().Instantiate(null!);

        instantiate.Should().Throw<ArgumentNullException>();
    }

    // ---- The spawn description --------------------------------------------------------------------------------

    [Fact]
    public void ASpawnDescriptionHasTheFrameAndPoseThatWereAskedFor()
    {
        using var t = new TestWorld();

        SpawnDesc desc = Soldiers().CreateSpawnDesc("soldier", t.Root, TestWorld.At(3, 4, 5));

        desc.Frame.Should().Be(t.Root);
        desc.Pose.Should().Be(TestWorld.At(3, 4, 5));
    }

    [Fact]
    public void ASpawnDescriptionCarriesTheBodyRenderRowAndFrameOfTheResolvedBlueprint()
    {
        var blueprints = new Blueprints();
        var spec = new FrameSpec(FrameBounds.Sphere(30), MotionDriver.Static);
        blueprints.Register(new Blueprint("hull", [], TestWorld.Ball, new RenderData { Mesh = new MeshId(6) }, spec));

        SpawnDesc desc = blueprints.CreateSpawnDesc("hull", default, DPose.Identity);

        desc.Body.Should().BeSameAs(TestWorld.Ball);
        desc.Render!.Value.Mesh.Should().Be(new MeshId(6));
        desc.AsFrame.Should().BeSameAs(spec);
    }

    [Fact]
    public void EachSpawnDescriptionHasFreshComponents()
    {
        Blueprints blueprints = Soldiers();

        SpawnDesc first = blueprints.CreateSpawnDesc("soldier", default, DPose.Identity);
        SpawnDesc second = blueprints.CreateSpawnDesc("soldier", default, DPose.Identity);

        first.Components.Should().HaveCount(2);
        second.Components[0].Should().NotBeSameAs(first.Components[0]);
        second.Components[1].Should().NotBeSameAs(first.Components[1]);
    }
}
