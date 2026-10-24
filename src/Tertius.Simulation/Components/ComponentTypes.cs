using System.Collections.Concurrent;
using System.Reflection;

namespace Tertius.Simulation;

/// <summary>
/// The ids of the component types, from 0 to <see cref="Capacity"/> - 1: the bits of <see cref="ComponentFlags"/>. The
/// first world that a process creates scans the loaded assemblies, and gives their concrete component types ids in the
/// order of their full names, so that each type keeps its id in every run with the same set of types. An assembly that
/// loads later, such as a mod, is scanned with the same checks when the world first sees one of its component types.
/// Thread-safe.
/// </summary>
/// <remarks>
/// Generic component types are refused, open or closed: a scan sees only the open form of a generic type, so it cannot
/// count the closed forms that code builds later. A scan that finds one fails, and so does a scan that would pass
/// <see cref="Capacity"/> types. The startup scan throws from the <see cref="World"/> constructor; a later scan refuses
/// every type of its assembly, and adds none of them.
/// </remarks>
public static class ComponentTypes
{
    /// <summary>The most component types that a process can have: the bits of <see cref="ComponentFlags"/>.</summary>
    public const int Capacity = 512;

    private static readonly Lock Gate = new();

    /// <summary>How many component types have an id.</summary>
    public static int Count => Registry.Count;

    internal static ComponentTypeRegistry Registry
    {
        get
        {
            ComponentTypeRegistry? current = Volatile.Read(ref field);
            if (current != null)
                return current;
            lock (Gate)
            {
                // A failed scan leaves the field null, so that each later call fails with the same clear error.
                field ??=
                    new(MayDefineComponents(AppDomain.CurrentDomain.GetAssemblies()));
                return field;
            }
        }
    }

    /// <summary>
    /// Scans the loaded assemblies for component types, once per process. The <see cref="World"/> constructor calls it.
    /// Throws <see cref="InvalidOperationException"/> when they define more than <see cref="Capacity"/> component types
    /// or a generic one, and again on each later call.
    /// </summary>
    public static void ScanLoadedAssemblies() => _ = Registry;

    /// <summary>
    /// The id of a concrete component type. A type whose assembly was not scanned yet scans that assembly first. Throws
    /// <see cref="ArgumentException"/> for a type that is not a concrete component type or is generic, and
    /// <see cref="InvalidOperationException"/> for a type whose assembly failed its scan, or that would pass the cap.
    /// </summary>
    public static int IdOf(Type type) => Registry.IdOf(type);

    /// <summary>The short name of the type with id <paramref name="id"/>, or a placeholder for an id that no type has.</summary>
    public static string NameOf(int id) => Registry.NameOf(id);

    /// <summary>
    /// The assemblies that can define a component type: Tertius.Simulation, and every assembly that references one of
    /// them, directly or through other loaded assemblies. The others, such as the framework, cannot implement
    /// <see cref="IComponent"/>, and scanning their types would cost much time at startup.
    /// </summary>
    internal static List<Assembly> MayDefineComponents(Assembly[] loaded)
    {
        Assembly simulation = typeof(IComponent).Assembly;
        // An assembly without a name cannot be referenced by name, so it adds nothing to the names.
        var names = new HashSet<string>(StringComparer.Ordinal);
        if (simulation.GetName().Name is { } simulationName)
            names.Add(simulationName);
        var result = new List<Assembly> { simulation };
        List<Assembly> rest = [.. loaded.Where(a => !a.IsDynamic && a != simulation)];
        bool grew = true;
        while (grew)
        {
            grew = false;
            for (int i = rest.Count - 1; i >= 0; i--)
            {
                if (
                    !rest[i]
                        .GetReferencedAssemblies()
                        .Any(reference => reference.Name is { } name && names.Contains(name))
                )
                    continue;
                if (rest[i].GetName().Name is { } name)
                    names.Add(name);
                result.Add(rest[i]);
                rest.RemoveAt(i);
                grew = true;
            }
        }

        return result;
    }
}

/// <summary>
/// The id of the component type <typeparamref name="T"/>, cached after the first lookup. A lookup that throws is not
/// cached, so each later use throws the same clear error, not a <see cref="TypeInitializationException"/>.
/// </summary>
internal static class ComponentType<T>
    where T : IComponent
{
    public static int Id
    {
        get
        {
            int value = Volatile.Read(ref field);
            if (value < 0)
            {
                // Two threads may both look it up. They get the same id, so the race does not matter.
                value = ComponentTypes.IdOf(typeof(T));
                Volatile.Write(ref field, value);
            }

            return value;
        }
    } = -1;
}

/// <summary>
/// The component types of one set of assemblies and their ids. <see cref="ComponentTypes"/> holds the one for the
/// process. Invariants: the ids are dense, from 0 up to <see cref="Count"/> - 1, each type has one id, and there are at
/// most <see cref="ComponentTypes.Capacity"/>. A scanned assembly either added all of its component types, or none of
/// them and keeps the reason. Thread-safe.
/// </summary>
internal sealed class ComponentTypeRegistry
{
    private readonly Lock gate = new();

    // Read without the lock, written under it.
    private readonly ConcurrentDictionary<Type, int> ids = new();

    private readonly List<Type> types = [];

    // Every scanned assembly, with the reason that its scan failed, or null when it added its types.
    private readonly Dictionary<Assembly, string?> scanned = [];

    /// <summary>
    /// The startup scan: the concrete component types of every assembly of <paramref name="assemblies"/> that is not
    /// dynamic, sorted by full name, take the ids from 0 up. A dynamic assembly can gain types after any scan, so each
    /// of its types gets an id when it is first seen instead. Throws <see cref="InvalidOperationException"/> when the
    /// assemblies define a generic component type, or more than <see cref="ComponentTypes.Capacity"/> of them.
    /// </summary>
    public ComponentTypeRegistry(IEnumerable<Assembly> assemblies)
    {
        var found = new List<Type>();
        var generic = new List<Type>();
        var scannedNow = new List<Assembly>();
        foreach (Assembly assembly in assemblies)
        {
            if (assembly.IsDynamic || scannedNow.Contains(assembly))
                continue;
            Collect(assembly, found, generic);
            scannedNow.Add(assembly);
        }

        if (generic.Count > 0)
            throw new InvalidOperationException(GenericProblem(generic));
        if (found.Count > ComponentTypes.Capacity)
            throw new InvalidOperationException(
                $"The loaded assemblies define {found.Count} component types, but a process can have at most {ComponentTypes.Capacity}: component flags hold one bit for each type."
            );
        foreach (Assembly assembly in scannedNow)
            scanned.Add(assembly, null);
        AddSorted(found);
    }

    public int Count
    {
        get
        {
            lock (gate)
            {
                return types.Count;
            }
        }
    }

    /// <inheritdoc cref="ComponentTypes.IdOf"/>
    public int IdOf(Type type)
    {
        ArgumentNullException.ThrowIfNull(type);
        if (ids.TryGetValue(type, out int id))
            return id;
        if (!IsConcreteComponent(type))
            throw new ArgumentException(
                $"{type.FullName} is not a concrete component type: a class or struct that implements IComponent.",
                nameof(type)
            );
        if (IsGeneric(type))
            throw new ArgumentException(GenericProblem([type]), nameof(type));
        lock (gate)
        {
            if (ids.TryGetValue(type, out id))
                return id;
            Assembly assembly = type.Assembly;
            if (!assembly.IsDynamic)
            {
                if (!scanned.TryGetValue(assembly, out string? problem))
                    problem = Scan(assembly);
                if (problem != null)
                    throw new InvalidOperationException(problem);
                if (ids.TryGetValue(type, out id))
                    return id;
            }

            // A type of a dynamic assembly, or one that the scan could not load.
            return types.Count == ComponentTypes.Capacity
                ? throw new InvalidOperationException(
                    $"{type.FullName} cannot get a component type id: a process can have at most {ComponentTypes.Capacity} component types, and every id is taken."
                )
                : Add(type);
        }
    }

    public string NameOf(int id)
    {
        lock (gate)
        {
            return (uint)id < (uint)types.Count ? types[id].Name : $"component:{id}";
        }
    }

    /// <summary>Checks every invariant in the class comment.</summary>
    internal void AssertInvariants()
    {
        lock (gate)
        {
            Contract.Invariant(types.Count <= ComponentTypes.Capacity && ids.Count == types.Count);
            for (int i = 0; i < types.Count; i++)
                Contract.Invariant(ids.TryGetValue(types[i], out int id) && id == i);
            foreach ((Assembly assembly, string? problem) in scanned)
                if (problem != null)
                    Contract.Invariant(!types.Any(type => type.Assembly == assembly));
        }
    }

    /// <summary>
    /// A later scan, under the lock: the assembly adds all of its component types in full-name order, after the types
    /// that have ids, or none of them. Returns why it added none, or null.
    /// </summary>
    private string? Scan(Assembly assembly)
    {
        var found = new List<Type>();
        var generic = new List<Type>();
        Collect(assembly, found, generic);
        found.RemoveAll(ids.ContainsKey);
        string? problem = null;
        if (generic.Count > 0)
            problem = GenericProblem(generic);
        else if (types.Count + found.Count > ComponentTypes.Capacity)
            problem =
                $"The assembly {assembly.GetName().Name} defines {found.Count} component types. With the {types.Count} that have ids, that passes the cap of {ComponentTypes.Capacity} component types in a process, so none of its types can be used.";
        if (problem == null)
            AddSorted(found);
        scanned.Add(assembly, problem);
        return problem;
    }

    private void AddSorted(List<Type> found)
    {
        found.Sort(static (a, b) => string.CompareOrdinal(a.FullName, b.FullName));
        foreach (Type type in found)
            Add(type);
    }

    private int Add(Type type)
    {
        int id = types.Count;
        types.Add(type);
        ids[type] = id;
        return id;
    }

    private static void Collect(Assembly assembly, List<Type> found, List<Type> generic)
    {
        Type?[] all;
        try
        {
            all = assembly.GetTypes();
        }
        catch (ReflectionTypeLoadException ex)
        {
            all = ex.Types;
        }

        foreach (Type? type in all)
            if (type != null && IsConcreteComponent(type))
                (IsGeneric(type) ? generic : found).Add(type);
    }

    private static bool IsConcreteComponent(Type type) =>
        !type.IsAbstract && !type.IsInterface && typeof(IComponent).IsAssignableFrom(type);

    // A type nested in a generic type is generic too: it has the parameters of the outer type.
    private static bool IsGeneric(Type type) => type.IsGenericType || type.ContainsGenericParameters;

    private static string GenericProblem(List<Type> generic) =>
        $"Generic component types are refused, open or closed, because a scan of the assemblies cannot count their closed forms: {string.Join(", ", generic.Select(t => t.FullName ?? t.Name))}. Make each one non-generic.";
}
