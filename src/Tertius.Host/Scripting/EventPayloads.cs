using System.Reflection;
using System.Runtime.CompilerServices;

namespace Tertius.Host.Scripting;

/// <summary>
/// The payload types that scripts and dumps name by their C# name, such as <c>InputEvent</c>. Reflection finds them in
/// the loaded assemblies, and makes the two generic calls that a name known only at run time needs: make an event from
/// a boxed payload, and box the payload of an event.
/// </summary>
internal sealed class EventPayloads
{
    private readonly Dictionary<string, List<Candidate>> byName = [with(StringComparer.Ordinal)];
    private readonly Dictionary<Type, PayloadType> bound = [];

    /// <summary>One payload type, with its two generic calls bound once.</summary>
    public sealed class PayloadType(Type type, Func<Target, object, Event> create, Func<Event, object> box)
    {
        public Type Type => type;

        /// <summary>An event to <paramref name="target"/> from outside the simulation, with this payload.</summary>
        public Event Create(Target target, object payload) => create(target, payload);

        /// <summary>The payload of <paramref name="e"/>, boxed. The event must hold this type.</summary>
        public object Box(in Event e) => box(e);
    }

    /// <summary>A type that implements <see cref="IEventPayload"/>, and why an event cannot hold it, or null if it can.</summary>
    private readonly record struct Candidate(Type Type, string? Problem);

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
                    payloads.Add(new(type, ProblemOf(type)));
        }

        return payloads;
    }

    /// <summary>
    /// Why an event cannot hold the type, or null. The compiler checks the <c>unmanaged</c> constraint of every payload
    /// in code, but a type that reflection finds has passed no such check. A reference in the payload bytes would not
    /// keep its object alive, and a payload over <see cref="EventTypeRegistry.MaxPayloadSize"/> bytes does not fit.
    /// </summary>
    private static string? ProblemOf(Type type)
    {
        if (type.IsByRefLike)
            return "it is a ref struct";
        (bool hasReferences, int size) =
            ((bool, int))typeof(EventPayloads).GetMethod(nameof(Layout), BindingFlags.NonPublic | BindingFlags.Static)!
                .MakeGenericMethod(type).Invoke(null, null)!;
        return hasReferences
            ? "it has a field that holds a reference"
            : size > EventTypeRegistry.MaxPayloadSize
                ? $"it is {size} bytes, and a payload holds at most {EventTypeRegistry.MaxPayloadSize}"
                : null;
    }

    private static (bool HasReferences, int Size) Layout<T>() =>
        (RuntimeHelpers.IsReferenceOrContainsReferences<T>(), Unsafe.SizeOf<T>());

    private void Add(Candidate candidate)
    {
        if (!byName.TryGetValue(candidate.Type.Name, out List<Candidate>? candidates))
            byName.Add(candidate.Type.Name, candidates = []);
        candidates.Add(candidate);
    }

    /// <summary>The payload type with the C# name, or null and the reason: no such type, more than one, or a type that an event cannot hold.</summary>
    public PayloadType? Find(string name, out string? problem)
    {
        if (!byName.TryGetValue(name, out List<Candidate>? candidates))
        {
            problem = $"no loaded payload type is named '{name}'.";
            return null;
        }

        if (candidates.Count > 1)
        {
            problem =
                $"the name '{name}' is ambiguous: it names {string.Join(", ", candidates.Select(c => c.Type.FullName).Order(StringComparer.Ordinal))}. Rename one of them.";
            return null;
        }

        if (candidates[0].Problem is { } why)
        {
            problem = $"{candidates[0].Type.FullName} ({name}) cannot be an event payload: {why}.";
            return null;
        }

        problem = null;
        return Of(candidates[0].Type);
    }

    /// <summary>The payload type of a type that an event holds.</summary>
    public PayloadType Of(Type type)
    {
        if (!bound.TryGetValue(type, out PayloadType? payload))
        {
            MethodInfo create =
                typeof(EventPayloads).GetMethod(nameof(CreateEvent), BindingFlags.NonPublic | BindingFlags.Static)!
                    .MakeGenericMethod(type);
            MethodInfo box =
                typeof(EventPayloads).GetMethod(nameof(BoxPayload), BindingFlags.NonPublic | BindingFlags.Static)!
                    .MakeGenericMethod(type);
            payload = new(type, create.CreateDelegate<Func<Target, object, Event>>(),
                box.CreateDelegate<Func<Event, object>>());
            bound.Add(type, payload);
        }

        return payload;
    }

    private static Event CreateEvent<T>(Target target, object payload) where T : unmanaged, IEventPayload =>
        Event.Create(target, Entity.Null, (T)payload);

    private static object BoxPayload<T>(Event e) where T : unmanaged, IEventPayload => e.As<T>();
}
