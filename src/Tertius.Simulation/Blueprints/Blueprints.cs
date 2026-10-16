using System.Collections.Concurrent;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Linq.Expressions;
using System.Reflection;

namespace Tertius.Simulation;

/// <summary>
/// A named entity template: component types with optional field values, and optional body, render and frame data.
/// A blueprint can name a <see cref="Parent"/>: it then starts from the parent's resolved blueprint.
/// </summary>
public sealed record Blueprint(
    string Name,
    IReadOnlyList<ComponentSpec> Components,
    BodyDesc? Body = null,
    RenderData? Render = null,
    FrameSpec? Frame = null,
    string? Parent = null);

/// <summary>A component type and the values of some of its public fields or settable properties.</summary>
public sealed record ComponentSpec(Type Type, IReadOnlyDictionary<string, object?>? Fields = null);

/// <summary>
/// The blueprint registry. Reflection runs only in <see cref="Register"/>: it validates every spec and compiles one
/// factory delegate per (component type, field set). <see cref="Instantiate"/> then costs one delegate call per
/// component. Register blueprints before the ticks that use them; lookups are safe from any thread.
/// </summary>
/// <remarks>
/// Field values are shared by every instance. A mutable reference value, such as an array, is therefore the same
/// object in every spawned component. Prefer immutable values.
/// </remarks>
public sealed class Blueprints
{
    // Factories depend only on the type and the field names, so every registry shares them.
    private static readonly ConcurrentDictionary<FactoryKey, Func<object?[], IComponent>> Factories = new();

    private readonly ConcurrentDictionary<string, Entry> entries = new(StringComparer.Ordinal);
    private readonly Lock gate = new();

    /// <summary>The number of registered blueprints.</summary>
    public int Count => entries.Count;

    /// <summary>True if a blueprint with this name is registered.</summary>
    public bool Contains(string name) => entries.ContainsKey(name);

    /// <summary>
    /// Validates and adds a blueprint. A parent must already be registered. A component type that the parent also
    /// lists keeps the parent's position, and its field values merge, with the child's values winning. Other types
    /// follow the parent's, in order.
    /// </summary>
    /// <exception cref="ArgumentException">The blueprint is invalid. The message says why.</exception>
    public void Register(Blueprint blueprint)
    {
        ArgumentNullException.ThrowIfNull(blueprint);
        lock (gate)
        {
            string name = blueprint.Name;
            if (string.IsNullOrWhiteSpace(name))
                throw new ArgumentException("A blueprint needs a name.", nameof(blueprint));
            if (entries.ContainsKey(name))
                throw new ArgumentException($"Blueprint '{name}' is already registered.", nameof(blueprint));
            if (blueprint.Components is null)
                throw new ArgumentException($"Blueprint '{name}' has no component list.", nameof(blueprint));

            Entry? parent = null;
            if (blueprint.Parent is { } parentName && !entries.TryGetValue(parentName, out parent))
                throw new ArgumentException($"Blueprint '{name}' names the parent '{parentName}', which is not registered. Register parents first.", nameof(blueprint));

            var specs = new List<(Type Type, Dictionary<string, object?> Fields)>();
            if (parent != null)
                foreach ((Type type, Dictionary<string, object?> fields) in parent.Specs)
                    specs.Add((type, new Dictionary<string, object?>(fields, StringComparer.Ordinal)));

            var own = new HashSet<Type>();
            foreach (ComponentSpec spec in blueprint.Components)
            {
                if (spec is null)
                    throw new ArgumentException($"Blueprint '{name}' has a null component spec.", nameof(blueprint));
                ValidateType(name, spec.Type);
                if (!own.Add(spec.Type))
                    throw new ArgumentException($"Blueprint '{name}' lists {spec.Type.Name} twice.", nameof(blueprint));

                int index = specs.FindIndex(s => s.Type == spec.Type);
                if (index < 0)
                {
                    specs.Add((spec.Type, new Dictionary<string, object?>(StringComparer.Ordinal)));
                    index = specs.Count - 1;
                }
                if (spec.Fields != null)
                    foreach ((string field, object? value) in spec.Fields)
                        specs[index].Fields[field] = Coerce(name, spec.Type, field, value);
            }

            var recipes = new Recipe[specs.Count];
            var resolvedSpecs = new ComponentSpec[specs.Count];
            for (int i = 0; i < specs.Count; i++)
            {
                (Type type, Dictionary<string, object?> fields) = specs[i];
                string[] names = [.. fields.Keys];
                Array.Sort(names, StringComparer.Ordinal);
                var values = new object?[names.Length];
                for (int j = 0; j < names.Length; j++)
                    values[j] = fields[names[j]];
                var key = new FactoryKey(type, string.Join('\n', names));
                Func<object?[], IComponent> factory = Factories.GetOrAdd(key, static (_, arg) => Compile(arg.Type, arg.Names), (Type: type, Names: names));
                recipes[i] = new Recipe(factory, values);
                resolvedSpecs[i] = new ComponentSpec(type, new ReadOnlyDictionary<string, object?>(new Dictionary<string, object?>(fields, StringComparer.Ordinal)));
            }

            Blueprint? parentBlueprint = parent?.Resolved;
            var resolved = new Blueprint(
                name,
                Array.AsReadOnly(resolvedSpecs),
                blueprint.Body ?? parentBlueprint?.Body,
                blueprint.Render ?? parentBlueprint?.Render,
                blueprint.Frame ?? parentBlueprint?.Frame,
                Parent: null);
            entries[name] = new Entry(resolved, specs, recipes);
        }
    }

    /// <summary>The blueprint with inheritance applied. Its <see cref="Blueprint.Parent"/> is null.</summary>
    public Blueprint Resolve(string name) => Get(name).Resolved;

    /// <summary>Creates new instances of the blueprint's components, in blueprint order.</summary>
    public IComponent[] Instantiate(string name)
    {
        Recipe[] recipes = Get(name).Recipes;
        var components = new IComponent[recipes.Length];
        for (int i = 0; i < recipes.Length; i++)
            components[i] = recipes[i].Factory(recipes[i].Values);
        return components;
    }

    /// <summary>A spawn description for the blueprint at <paramref name="pose"/> in <paramref name="frame"/>, with fresh components.</summary>
    public SpawnDesc CreateSpawnDesc(string name, FrameHandle frame, in DPose pose)
    {
        Entry entry = Get(name);
        return new SpawnDesc
        {
            Frame = frame,
            Pose = pose,
            Body = entry.Resolved.Body,
            Render = entry.Resolved.Render,
            AsFrame = entry.Resolved.Frame,
            Components = Instantiate(name),
        };
    }

    /// <summary>The compiled factory of one component of a blueprint. For tests.</summary>
    internal Func<object?[], IComponent> FactoryOf(string name, int component) => Get(name).Recipes[component].Factory;

    private Entry Get(string name)
    {
        ArgumentNullException.ThrowIfNull(name);
        return entries.TryGetValue(name, out Entry? entry)
            ? entry
            : throw new KeyNotFoundException($"No blueprint named '{name}' is registered.");
    }

    private static void ValidateType(string blueprint, Type? type)
    {
        if (type is null)
            throw new ArgumentException($"Blueprint '{blueprint}' has a component spec with no type.");
        if (!typeof(IComponent).IsAssignableFrom(type))
            throw new ArgumentException($"Blueprint '{blueprint}': {type.FullName} does not implement IComponent.");
        if (!type.IsClass || type.IsAbstract || type.ContainsGenericParameters)
            throw new ArgumentException($"Blueprint '{blueprint}': {type.FullName} must be a concrete class.");
        if (type.IsGenericType)
            throw new ArgumentException($"Blueprint '{blueprint}': {type.FullName} is generic, and generic component types are refused (see ComponentTypes).");
        if (type.GetConstructor(BindingFlags.Public | BindingFlags.Instance, Type.EmptyTypes) is null)
            throw new ArgumentException($"Blueprint '{blueprint}': {type.FullName} needs a public parameterless constructor.");
    }

    /// <summary>A public instance field that is not read-only, or a public property with a public setter.</summary>
    private static MemberInfo? FindMember(Type type, string name, out Type memberType)
    {
        const BindingFlags Public = BindingFlags.Public | BindingFlags.Instance;
        if (type.GetField(name, Public) is { IsInitOnly: false, IsLiteral: false } field)
        {
            memberType = field.FieldType;
            return field;
        }
        if (type.GetProperty(name, Public) is { SetMethod.IsPublic: true } property && property.GetIndexParameters().Length == 0)
        {
            memberType = property.PropertyType;
            return property;
        }
        memberType = typeof(void);
        return null;
    }

    /// <summary>Checks that the field exists and can hold the value. Converts numbers and enum names to the field's type.</summary>
    private static object? Coerce(string blueprint, Type type, string field, object? value)
    {
        if (FindMember(type, field, out Type memberType) is null)
            throw new ArgumentException($"Blueprint '{blueprint}': {type.Name} has no public writable field or property named '{field}'.");
        Type target = Nullable.GetUnderlyingType(memberType) ?? memberType;
        if (value is null)
        {
            if (memberType.IsValueType && target == memberType)
                throw new ArgumentException($"Blueprint '{blueprint}': {type.Name}.{field} is a {memberType.Name} and cannot be null.");
            return null;
        }
        if (target.IsInstanceOfType(value))
            return value;
        try
        {
            if (target.IsEnum)
            {
                if (value is string text)
                    return Enum.Parse(target, text, ignoreCase: false);
                if (IsNumber(value.GetType()))
                    return Enum.ToObject(target, value);
            }
            else if (IsNumber(target) && IsNumber(value.GetType()))
            {
                return Convert.ChangeType(value, target, CultureInfo.InvariantCulture);
            }
        }
        catch (Exception ex) when (ex is ArgumentException or OverflowException or InvalidCastException)
        {
            throw new ArgumentException($"Blueprint '{blueprint}': {type.Name}.{field} is a {memberType.Name}; cannot convert {value} ({value.GetType().Name}): {ex.Message}", ex);
        }
        throw new ArgumentException($"Blueprint '{blueprint}': {type.Name}.{field} is a {memberType.Name}; a {value.GetType().Name} is not assignable to it.");
    }

    private static bool IsNumber(Type type) => Type.GetTypeCode(type) is
        TypeCode.Byte or TypeCode.SByte or TypeCode.Int16 or TypeCode.UInt16 or TypeCode.Int32 or TypeCode.UInt32 or
        TypeCode.Int64 or TypeCode.UInt64 or TypeCode.Single or TypeCode.Double or TypeCode.Decimal;

    /// <summary>Compiles <c>values =&gt; new T { F0 = (T0)values[0], F1 = (T1)values[1], ... }</c>.</summary>
    private static Func<object?[], IComponent> Compile(Type type, string[] names)
    {
        ParameterExpression values = Expression.Parameter(typeof(object[]), "values");
        var bindings = new MemberBinding[names.Length];
        for (int i = 0; i < names.Length; i++)
        {
            MemberInfo member = FindMember(type, names[i], out Type memberType)!;
            Expression value = Expression.Convert(Expression.ArrayIndex(values, Expression.Constant(i)), memberType);
            bindings[i] = Expression.Bind(member, value);
        }
        Expression body = Expression.Convert(Expression.MemberInit(Expression.New(type), bindings), typeof(IComponent));
        return Expression.Lambda<Func<object?[], IComponent>>(body, values).Compile();
    }

    private readonly record struct FactoryKey(Type Type, string Fields);

    private readonly record struct Recipe(Func<object?[], IComponent> Factory, object?[] Values);

    private sealed record Entry(Blueprint Resolved, List<(Type Type, Dictionary<string, object?> Fields)> Specs, Recipe[] Recipes);
}
