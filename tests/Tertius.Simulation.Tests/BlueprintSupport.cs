namespace Tertius.Simulation.Tests;

// The components and sample blueprints of the blueprint tests. Each component exists to give the blueprint registry one
// thing to accept or to refuse. None of them does anything when it runs.

/// <summary>A component with a public field and a public init-only property, both with defaults.</summary>
public sealed class Weapon : IComponent
{
    public float Damage = 1;
    public double Range { get; init; } = 10;

    public int Priority => 1;
    public EventMask Wants => EventMask.Of<Tick>();

    public void Handle(Self self, scoped ref EventList events, scoped ref Outbox outbox) { }
}

/// <summary>A component with an int field, and no fields that a test sets to anything else.</summary>
public sealed class Radio : IComponent
{
    public int Channel = 1;
    public int Priority => 2;
    public EventMask Wants => EventMask.Of<Ping>();

    public void Handle(Self self, scoped ref EventList events, scoped ref Outbox outbox) { }
}

/// <summary>A component that a blueprint cannot build: its only constructor takes an argument.</summary>
public sealed class NeedsArgument(int value) : IComponent
{
    public int Value = value;
    public int Priority => 0;
    public EventMask Wants => default;

    public void Handle(Self self, scoped ref EventList events, scoped ref Outbox outbox) { }
}

/// <summary>A component with a read-only field.</summary>
public sealed class Sealed : IComponent
{
    public readonly int Fixed = 3;
    public int Priority => 0;
    public EventMask Wants => default;

    public void Handle(Self self, scoped ref EventList events, scoped ref Outbox outbox) { }
}

/// <summary>A component that a blueprint cannot build: the class is abstract.</summary>
public abstract class AbstractComponent : IComponent
{
    public int Priority => 0;
    public EventMask Wants => default;

    public void Handle(Self self, scoped ref EventList events, scoped ref Outbox outbox) { }
}

/// <summary>A component with one field that a blueprint sets.</summary>
public sealed class Boxed : IComponent
{
    public int Content;
    public int Priority => 0;
    public EventMask Wants => default;

    public void Handle(Self self, scoped ref EventList events, scoped ref Outbox outbox) { }
}

/// <summary>A component that is a struct, not a class.</summary>
public struct ValueComponent : IComponent
{
    public readonly int Priority => 0;
    public readonly EventMask Wants => default;

    public readonly void Handle(Self self, scoped ref EventList events, scoped ref Outbox outbox) { }
}

/// <summary>A component with no public constructor.</summary>
public sealed class HiddenConstructor : IComponent
{
    private HiddenConstructor() { }

    public int Priority => 0;
    public EventMask Wants => default;

    public void Handle(Self self, scoped ref EventList events, scoped ref Outbox outbox) { }
}

/// <summary>
/// A component with one member of each kind that a blueprint may or may not set: fields with non-null defaults, a
/// reference-typed array, and the members that are not public writable instance fields or properties.
/// </summary>
public sealed class Labelled : IComponent
{
    public const int Limit = 9;
    public static int Counter;
    public string? Label = "default label";
    public int? Level = 5;
    public int[]? Marks;
    internal int Internal = 2;

    public int Computed => 7;
    // The blueprint tests need a property whose setter is private, so RCS1170 must not make it get-only.
#pragma warning disable RCS1170
    public int Locked { get; private set; } = 1;
#pragma warning restore RCS1170

    public int Priority => 0;
    public EventMask Wants => default;

    public void Handle(Self self, scoped ref EventList events, scoped ref Outbox outbox) { }
}

/// <summary>The blueprints that several blueprint test files start from.</summary>
internal static class BlueprintSamples
{
    public static Dictionary<string, object?> Fields(params (string Name, object? Value)[] fields) =>
        fields.ToDictionary(f => f.Name, f => f.Value);

    /// <summary>
    /// Registers "soldier", and "officer" which names "soldier" as its parent.
    /// <list type="bullet">
    /// <item>"soldier" has a Health (Max 120, Current 120, Side Rebel), a Weapon (Damage 5, Range 30), the ball body,
    /// and a visible mesh 4.</item>
    /// <item>"officer" has a Radio, and a Health (Max 150, Name Boss, Armor 3) that is merged into the soldier's.</item>
    /// </list>
    /// </summary>
    public static Blueprints Soldiers(Blueprints? into = null)
    {
        Blueprints blueprints = into ?? new Blueprints();
        blueprints.Register(
            new(
                Name: "soldier",
                Components:
                [
                    new(typeof(Health), Fields(("Max", 120), ("Current", 120f), ("Side", "Rebel"))),
                    new(typeof(Weapon), Fields(("Damage", 5.0), ("Range", 30))),
                ],
                Body: TestWorld.Ball,
                Render: new RenderData { Mesh = new(4), Flags = RenderFlags.Visible }
            )
        );
        blueprints.Register(
            new(
                Name: "officer",
                Components:
                [
                    new(typeof(Radio)),
                    new(typeof(Health), Fields(("Max", 150f), ("Name", "Boss"), ("Armor", 3))),
                ],
                Parent: "soldier"
            )
        );
        return blueprints;
    }
}
