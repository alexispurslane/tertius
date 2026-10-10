using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Runtime.CompilerServices;

namespace Tertius;

/// <summary>
/// Contracts for core data structures, business logic and internal orchestration: the code that keeps several
/// pieces of state coherent with each other. Do not use them in general procedural or transformational code, and
/// never use them to check behavior; that is what tests are for.
/// <para>
/// <see cref="Requires"/>, <see cref="Ensures"/> and <see cref="Assert"/> are compiled out unless the calling project
/// defines <c>TERTIUS_CONTRACTS</c>. Directory.Build.props defines it for Debug builds, so these checks cost nothing
/// in Release. <see cref="Invariant"/> is always on: use it inside an <c>AssertInvariants</c> method, which runs only
/// when someone asks for it (a debug switch or a test), and must then work in every build.
/// </para>
/// </summary>
public static class Contract
{
    /// <summary>A precondition: what must hold when the operation starts.</summary>
    [Conditional("TERTIUS_CONTRACTS")]
    public static void Requires(
        [DoesNotReturnIf(false)] bool condition,
        [CallerArgumentExpression(nameof(condition))] string? expression = null)
    {
        if (!condition)
            Fail("Precondition", expression);
    }

    /// <summary>A postcondition: what the operation guarantees when it returns.</summary>
    [Conditional("TERTIUS_CONTRACTS")]
    public static void Ensures(
        [DoesNotReturnIf(false)] bool condition,
        [CallerArgumentExpression(nameof(condition))] string? expression = null)
    {
        if (!condition)
            Fail("Postcondition", expression);
    }

    /// <summary>A fact that must hold at this point inside an operation.</summary>
    [Conditional("TERTIUS_CONTRACTS")]
    public static void Assert(
        [DoesNotReturnIf(false)] bool condition,
        [CallerArgumentExpression(nameof(condition))] string? expression = null)
    {
        if (!condition)
            Fail("Assertion", expression);
    }

    /// <summary>A class invariant, checked in every build. Call it only from an <c>AssertInvariants</c> method.</summary>
    public static void Invariant(
        [DoesNotReturnIf(false)] bool condition,
        [CallerArgumentExpression(nameof(condition))] string? expression = null)
    {
        if (!condition)
            Fail("Invariant", expression);
    }

    [DoesNotReturn, StackTraceHidden]
    private static void Fail(string kind, string? expression) =>
        throw new ContractException($"{kind} failed: {expression}");
}

/// <summary>A broken contract: a bug in the engine, not a bad input. Never catch it to recover.</summary>
public sealed class ContractException(string message) : Exception(message);
