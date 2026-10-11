using System.Reflection;

namespace Tertius.Host.Scripting;

/// <summary>
/// The payload types that scripts and dumps name by their C# name, such as <c>InputEvent</c>. Reflection finds them in
/// the loaded assemblies, and makes the two generic calls that a name known only at run time needs: make an event from
/// a boxed payload, and box the payload of an event.
/// </summary>
internal sealed class EventPayloads
{
    /// <summary>One payload type, with its two generic calls bound once.</summary>
    public sealed class PayloadType(Type type, Func<Target, object, Event> create, Func<Event, object> box)
    {
        public Type Type => type;

        /// <summary>An event to <paramref name="target"/> from outside the simulation, with this payload.</summary>
        public Event Create(Target target, object payload) => create(target, payload);

        /// <summary>The payload of <paramref name="e"/>, boxed. The event must hold this type.</summary>
        public object Box(in Event e) => box(e);
    }

    private readonly Dictionary<string, List<Type>> byName = new(StringComparer.Ordinal);
    private readonly Dictionary<Type, PayloadType> bound = new();

    /// <summary>
    /// Finds every payload type in the assemblies that are loaded now: the core assembly that declares
    /// <see cref="IEventPayload"/>, and every assembly that references it. A type that is in an assembly that loads
    /// later is not found.
    /// </summary>
    public static EventPayloads FromLoadedAssemblies()
    {
        var payloads = new EventPayloads();
        Assembly core = typeof(IEventPayload).Assembly;
        string coreName = core.GetName().Name!;
        foreach (Assembly assembly in AppDomain.CurrentDomain.GetAssemblies())
        {
            if (assembly.IsDynamic)
                continue;
            if (assembly != core && !assembly.GetReferencedAssemblies().Any(reference => reference.Name == coreName))
                continue;
            foreach (Type type in assembly.GetTypes())
                if (type.IsValueType && !type.ContainsGenericParameters && typeof(IEventPayload).IsAssignableFrom(type))
                    payloads.Add(type);
        }
        return payloads;
    }

    private void Add(Type type)
    {
        if (!byName.TryGetValue(type.Name, out List<Type>? types))
            byName.Add(type.Name, types = new List<Type>());
        types.Add(type);
    }

    /// <summary>The payload type with the C# name, or null and the reason: no such type, or more than one.</summary>
    public PayloadType? Find(string name, out string? problem)
    {
        if (!byName.TryGetValue(name, out List<Type>? types))
        {
            problem = $"no loaded payload type is named '{name}'.";
            return null;
        }
        if (types.Count > 1)
        {
            problem = $"the name '{name}' is ambiguous: it names {string.Join(", ", types.Select(t => t.FullName).Order(StringComparer.Ordinal))}. Rename one of them.";
            return null;
        }
        problem = null;
        return Of(types[0]);
    }

    /// <summary>The payload type of a type that an event holds.</summary>
    public PayloadType Of(Type type)
    {
        if (!bound.TryGetValue(type, out PayloadType? payload))
        {
            MethodInfo create = typeof(EventPayloads).GetMethod(nameof(CreateEvent), BindingFlags.NonPublic | BindingFlags.Static)!.MakeGenericMethod(type);
            MethodInfo box = typeof(EventPayloads).GetMethod(nameof(BoxPayload), BindingFlags.NonPublic | BindingFlags.Static)!.MakeGenericMethod(type);
            payload = new PayloadType(type, create.CreateDelegate<Func<Target, object, Event>>(), box.CreateDelegate<Func<Event, object>>());
            bound.Add(type, payload);
        }
        return payload;
    }

    private static Event CreateEvent<T>(Target target, object payload) where T : unmanaged, IEventPayload =>
        Event.Create(target, Entity.Null, (T)payload);

    private static object BoxPayload<T>(Event e) where T : unmanaged, IEventPayload => e.As<T>();
}
