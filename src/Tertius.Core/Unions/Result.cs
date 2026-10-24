using System.Runtime.CompilerServices;

namespace Tertius;

#pragma warning disable TU0001 // The union generator does not support type parameters (STYLE.md, "Expected failures").
/// <summary>
/// The result of a method whose caller needs the reason for a failure: a value of type <typeparamref name="T"/> or a
/// problem of type <typeparamref name="TProblem"/> (STYLE.md, "Expected failures"). The default value holds neither.
/// </summary>
/// <typeparam name="T">The type of the value. It differs from <typeparamref name="TProblem"/>.</typeparam>
/// <typeparam name="TProblem">The type of the problem.</typeparam>
[Union]
public readonly struct Result<T, TProblem>
#pragma warning restore TU0001
    where T : notnull
    where TProblem : notnull
{
    private readonly T? value;
    private readonly TProblem? problem;
    private readonly Holds holds;

    /// <summary>A result that holds <paramref name="value"/>.</summary>
    public Result(T value)
    {
        this.value = value;
        holds = Holds.Value;
    }

    /// <summary>A result that holds <paramref name="problem"/>.</summary>
    public Result(TProblem problem)
    {
        this.problem = problem;
        holds = Holds.Problem;
    }

    private enum Holds : byte
    {
        Nothing,
        Value,
        Problem,
    }

    /// <summary>True unless this is the default value, which holds neither case.</summary>
    public bool HasValue => holds != Holds.Nothing;

    /// <summary>The case that the result holds, boxed for a struct, or null for the default value.</summary>
    public object? Value =>
        holds switch
        {
            Holds.Value => value,
            Holds.Problem => problem,
            Holds.Nothing => null,
        };

    public static implicit operator Result<T, TProblem>(T value) => new(value);

    public static implicit operator Result<T, TProblem>(TProblem problem) => new(problem);

    /// <summary>True, with the value in <paramref name="value"/>, when the result holds a value.</summary>
    public bool TryGetValue(out T value)
    {
        // value holds the default of T unless the result holds a value, and the false return says so.
#pragma warning disable TS0003
        value = this.value!;
#pragma warning restore TS0003
        return holds == Holds.Value;
    }

    /// <summary>True, with the problem in <paramref name="problem"/>, when the result holds a problem.</summary>
    public bool TryGetValue(out TProblem problem)
    {
        // problem holds the default of TProblem unless the result holds a problem, and the false return says so.
#pragma warning disable TS0003
        problem = this.problem!;
#pragma warning restore TS0003
        return holds == Holds.Problem;
    }
}

/// <summary>The value of a result as a nullable reference, for <c>??</c> and <c>?.</c>.</summary>
public static class ResultOfClass
{
    extension<T, TProblem>(Result<T, TProblem> result)
        where T : class
        where TProblem : notnull
    {
        /// <summary>The value, or null when the result holds a problem or nothing.</summary>
        public T? Ok => result.TryGetValue(out T value) ? value : null;
    }
}

/// <summary>The value of a result as a <see cref="Nullable{T}"/>, for <c>??</c> and <c>?.</c>.</summary>
public static class ResultOfStruct
{
    extension<T, TProblem>(Result<T, TProblem> result)
        where T : struct
        where TProblem : notnull
    {
        /// <summary>The value, or null when the result holds a problem or nothing.</summary>
        public T? Ok => result.TryGetValue(out T value) ? value : null;
    }
}

/// <summary>The problem of a result as a nullable reference.</summary>
public static class ResultProblem
{
    extension<T, TProblem>(Result<T, TProblem> result)
        where T : notnull
        where TProblem : class
    {
        /// <summary>The problem, or null when the result holds a value or nothing.</summary>
        public TProblem? Problem => result.TryGetValue(out TProblem problem) ? problem : null;
    }
}
