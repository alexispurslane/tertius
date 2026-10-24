using System.Reflection;
using System.Reflection.Emit;
using System.Runtime.Loader;

namespace Tertius.Simulation.Tests;

/// <summary>
/// The base of the component types that <see cref="EmittedComponents"/> builds at run time. It is abstract, so the scan
/// of component types skips it.
/// </summary>
public abstract class EmittedComponent : IComponent
{
    public int Priority => 0;
    public EventMask Wants => default;

    public void Handle(Self self, scoped ref EventList events, scoped ref Outbox outbox) { }
}

/// <summary>
/// Component types built at run time, in assemblies that load after the startup scan, as a mod's would. The engine
/// refuses generic component types, and a process can have only 512 component types, so the tests cannot declare such
/// types in this assembly: the startup scan would refuse the whole test assembly.
/// </summary>
internal static class EmittedComponents
{
    private static int assemblies;

    private static readonly Lazy<Type> openGeneric = new(() => TypeNamed(LoadLater(0, withGeneric: true), "G`1"));

    /// <summary>An open generic component type, <c>G&lt;T&gt;</c>, in an assembly of its own.</summary>
    public static Type OpenGeneric => openGeneric.Value;

    /// <summary>
    /// Builds an assembly with <paramref name="plainTypes"/> sealed component types, named <c>{namespace}.C{i}</c> with i
    /// from 0 unless <paramref name="nameOf"/> names them, and, if <paramref name="withGeneric"/>, a generic one named
    /// <c>{namespace}.G`1</c>. It loads the assembly
    /// into a new load context, like a mod. Every type derives from <see cref="EmittedComponent"/>, so the assembly
    /// references this test assembly and not Tertius.Simulation.
    /// </summary>
    /// <remarks>
    /// The process scans the loaded assemblies when the first world is created. This runs that scan first, so that the
    /// new assembly is always a later one: an assembly with a generic type that the startup scan saw would make every
    /// world of the test run fail.
    /// </remarks>
    public static Assembly LoadLater(int plainTypes, bool withGeneric = false, Func<int, string>? nameOf = null)
    {
        ComponentTypes.ScanLoadedAssemblies();
        string name = $"Tertius.Emitted{Interlocked.Increment(ref assemblies)}";
        var builder = new PersistedAssemblyBuilder(new(name), typeof(object).Assembly);
        ModuleBuilder module = builder.DefineDynamicModule(name);
        for (int i = 0; i < plainTypes; i++)
            Define(module, $"{name}.{nameOf?.Invoke(i) ?? $"C{i}"}", generic: false);
        if (withGeneric)
            Define(module, $"{name}.G`1", generic: true);
        using var stream = new MemoryStream();
        builder.Save(stream);
        stream.Position = 0;
        return new AssemblyLoadContext(name).LoadFromStream(stream);
    }

    /// <summary>
    /// A plain component type in a dynamic assembly, which can gain types after any scan, so a scan skips it and the type
    /// gets its id when it is first seen.
    /// </summary>
    public static Type InDynamicAssembly()
    {
        string name = $"Tertius.Dynamic{Interlocked.Increment(ref assemblies)}";
        var builder = AssemblyBuilder.DefineDynamicAssembly(new(name), AssemblyBuilderAccess.Run);
        ModuleBuilder module = builder.DefineDynamicModule(name);
        Define(module, $"{name}.D", generic: false);
        return builder.GetType($"{name}.D", throwOnError: true)!;
    }

    /// <summary>The type named <paramref name="name"/> in <paramref name="assembly"/>, without the namespace.</summary>
    public static Type TypeNamed(Assembly assembly, string name) =>
        assembly.GetType($"{assembly.GetName().Name}.{name}", throwOnError: true)!;

    /// <summary>A new instance of the type named <paramref name="name"/> in <paramref name="assembly"/>.</summary>
    public static IComponent Create(Assembly assembly, string name) =>
        (IComponent)Activator.CreateInstance(TypeNamed(assembly, name))!;

    private static void Define(ModuleBuilder module, string fullName, bool generic)
    {
        TypeBuilder type = module.DefineType(
            fullName,
            TypeAttributes.Public | TypeAttributes.Sealed | TypeAttributes.Class,
            typeof(EmittedComponent)
        );
        if (generic)
            type.DefineGenericParameters("T");
        ConstructorBuilder constructor = type.DefineConstructor(
            MethodAttributes.Public,
            CallingConventions.Standard,
            Type.EmptyTypes
        );
        ILGenerator il = constructor.GetILGenerator();
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(
            OpCodes.Call,
            typeof(EmittedComponent).GetConstructor(BindingFlags.NonPublic | BindingFlags.Instance, Type.EmptyTypes)!
        );
        il.Emit(OpCodes.Ret);
        type.CreateType();
    }
}
