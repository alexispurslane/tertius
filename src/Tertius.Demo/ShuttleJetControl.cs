using Tertius.Simulation;

namespace Tertius.Demo;

/// <summary>
/// The force that a hull's translation pairs give for one tick, which a component that flies the hull adds to its
/// entity's events (see <see cref="EventList.Add{T}"/>), for the <see cref="ShuttleJetControl"/> of the same entity, which
/// runs after it.
/// </summary>
public struct Translation : IEventPayload
{
    /// <summary>The force, in newtons, in the axes of the entity's frame.</summary>
    public Double3 Force;
}

/// <summary>
/// The torque that a hull's <see cref="AttitudeAutopilot"/> asks for in one tick, which it adds to its entity's events
/// (see <see cref="EventList.Add{T}"/>), for the <see cref="ShuttleJetControl"/> of the same entity, which runs after it.
/// </summary>
public struct Torque : IEventPayload
{
    /// <summary>The torque about the body's center of mass, in N·m, in the axes of the entity's frame.</summary>
    public Double3 Value;
}

/// <summary>
/// The control of the shuttle's main engine and jets, which are parts of the hull (docs/plans/blue-danube-demo.md, "The
/// jets as parts"). <see cref="Fit"/> builds it outside a tick: it reads the hull's center of mass from the engine's
/// <see cref="BodyFacts.CenterOfMass"/>, places the jets for it (<see cref="ShuttleJets"/>), spawns 25 attached entities of
/// the hull, the main engine's part at <see cref="ShuttleJets.MainEnginePlace"/> with a <see cref="MainEngine"/> and one part
/// for each jet at its place with a <see cref="JetThruster"/>, every one turned so that its -Z, along which its puffs leave,
/// points against its push, and adds the control to the hull. In each tick it reads the last <see cref="Throttle"/>, the
/// last <see cref="Torque"/> and the last <see cref="Translation"/> that the components before it added, sends the
/// throttle to the main engine's part, allocates the throttles of the jets for the torque and the force
/// (<see cref="ShuttleJets.Allocate"/>), and sends each jet whose pair fires its pair's throttle. A tick that brings none of them sends nothing, so no part pushes in the next tick. The
/// commands go by message, so each part pushes in the tick after the one that commands it. A torque or a force that is
/// not finite makes the control throw <see cref="ArgumentOutOfRangeException"/>, which the world logs as an error, and send
/// nothing in that tick.
/// </summary>
/// <remarks>
/// It turns the torque and the force from the axes of the hull's frame into the hull's own axes with the hull's rotation at
/// the end of the last finished step, the rotation that the attitude autopilot read for its torque. It runs at priority
/// 20, after the attitude autopilot at 10, which adds the torque, and after every layer of the flight program, which add the
/// throttle and the translation. It allocates nothing. The parts spawn outside a tick, because 25 spawns in one tick take
/// more than a chunk's pool of reserved slots, and the slots of the rest would then depend on thread timing
/// (<see cref="WorldOptions.SpawnPoolSize"/>).
/// </remarks>
public sealed class ShuttleJetControl : IComponent
{
    private readonly Entity[] jetParts;

    /// <summary>The throttle of each pair in the tick, from 0 to 1, in the order of <see cref="ShuttleJets.Pairs"/>.</summary>
    private readonly float[] throttles = new float[ShuttleJets.PairCount];

    private ShuttleJetControl(ShuttleJets layout, Entity mainEnginePart, Entity[] jetParts)
    {
        Layout = layout;
        MainEnginePart = mainEnginePart;
        this.jetParts = jetParts;
    }

    /// <summary>The jets, placed for the hull's center of mass.</summary>
    public ShuttleJets Layout { get; }

    /// <summary>The main engine's part.</summary>
    public Entity MainEnginePart { get; }

    /// <summary>The jets' parts, at <see cref="PartIndex"/> for each jet of each pair.</summary>
    public IReadOnlyList<Entity> JetParts => jetParts;

    // After the attitude autopilot at 10, which adds the torque, and after every layer of the flight program.
    public int Priority => 20;

    public EventMask Wants => EventMask.Of<Throttle>().And<Torque>().And<Translation>();

    /// <summary>
    /// Spawns the main engine and the jets as parts of <paramref name="hull"/>, placed for its center of mass, and adds
    /// their control to it, outside a tick. The main engine gives <paramref name="mainThrust"/> at full throttle, the
    /// couples are sized for <paramref name="inertia"/>, and each translation pair gives
    /// <paramref name="translationThrust"/>. Throws <see cref="ArgumentException"/> for a hull that is not live or has no
    /// body, <see cref="ArgumentOutOfRangeException"/> for a thrust that is not positive and finite and as
    /// <see cref="ShuttleJets"/> does, and <see cref="InvalidOperationException"/> during a tick.
    /// </summary>
    public static ShuttleJetControl Fit(World world, Entity hull, float mainThrust, PrincipalInertia inertia,
        double translationThrust)
    {
        if (!(mainThrust > 0 && float.IsFinite(mainThrust)))
            throw new ArgumentOutOfRangeException(nameof(mainThrust), mainThrust,
                "The thrust of an engine must be positive and finite.");
        if (!world.Snapshot.TryGetBody(hull, out BodyFacts body))
            throw new ArgumentException(
                $"The jets need a live hull with a body, whose center of mass they are placed for, not {hull}.",
                nameof(hull));
        var layout = new ShuttleJets(body.CenterOfMass, inertia, translationThrust);

        // Each part's -Z, along which its puffs leave, points against its push.
        Entity Part(Double3 place, Double3 exhaust, IComponent component) => world.Spawn(new()
        {
            AttachTo = hull,
            AttachOffset = Plume.AimedOffset(place.ToVector3(), exhaust),
            Components = [component]
        });

        Entity mainEngine = Part(layout.MainEnginePlace, Double3.UnitZ, new MainEngine(mainThrust));
        var jets = new Entity[2 * ShuttleJets.PairCount];
        for (int pair = 0; pair < ShuttleJets.PairCount; pair++)
        {
            JetPair each = layout.Pairs[pair];
            jets[PartIndex(pair, false)] = Part(each.First.Place, -each.First.Push, new JetThruster(each.First));
            jets[PartIndex(pair, true)] = Part(each.Second.Place, -each.Second.Push, new JetThruster(each.Second));
        }

        var control = new ShuttleJetControl(layout, mainEngine, jets);
        world.AddComponent(hull, control);
        return control;
    }

    /// <summary>
    /// The index in <see cref="JetParts"/> of the part of the first jet of pair <paramref name="pair"/> of
    /// <see cref="ShuttleJets.Pairs"/>, or of its second jet when <paramref name="second"/>.
    /// </summary>
    public static int PartIndex(int pair, bool second) => 2 * pair + (second ? 1 : 0);

    public void Handle(Self self, scoped ref EventList events, scoped ref Outbox outbox)
    {
        Throttle? throttle = null;
        foreach (ref Throttle each in events.Of<Throttle>())
            throttle = each;
        Double3 torque = Double3.Zero, force = Double3.Zero;
        foreach (ref Torque each in events.Of<Torque>())
            torque = each.Value;
        foreach (ref Translation each in events.Of<Translation>())
            force = each.Force;
        DQuaternion intoHull = self.PoseIn(self.Frame).Rotation.Conjugate();
        // The allocation throws for a torque or a force that is not finite, before any part gets a command.
        Layout.Allocate(intoHull.Rotate(torque), intoHull.Rotate(force), throttles);
        if (throttle is Throttle main)
            outbox.Send(MainEnginePart, main);
        for (int pair = 0; pair < ShuttleJets.PairCount; pair++)
        {
            if (throttles[pair] <= 0)
                continue;
            var command = new Throttle { Value = throttles[pair] };
            outbox.Send(jetParts[PartIndex(pair, false)], command);
            outbox.Send(jetParts[PartIndex(pair, true)], command);
        }
    }
}
