using System.Runtime.InteropServices;

namespace Tertius.Testing;

/// <summary>
/// The checked-in results that a test compares with, such as a pinned hash or a generated script, and the machine that
/// recorded them. Determinism holds on one computer, not across computers or architectures (user, 2026-11-12, ticket
/// ter-zkdz): an x86-64 machine runs the same steps to other bits than the arm64 Mac that recorded them. So a test of
/// determinism compares two runs on its own machine first, and compares with the recorded result only on the
/// architecture that recorded it. Elsewhere it skips that part with <see cref="SkipReason"/>.
/// </summary>
public static class RecordedReference
{
    /// <summary>The architecture of the machine that recorded the checked-in results: the arm64 Mac.</summary>
    public const Architecture RecordedOn = Architecture.Arm64;

    /// <summary>True when this process runs on <see cref="RecordedOn"/>, so the checked-in results are its reference.</summary>
    public static bool Applies => RuntimeInformation.ProcessArchitecture == RecordedOn;

    /// <summary>The reason of a skip on another architecture. It names both architectures.</summary>
    public static string SkipReason =>
        $"The checked-in reference was recorded on arm64, and this process runs on {RuntimeInformation.ProcessArchitecture}. "
        + "Determinism holds per computer, so only the two runs on this machine were compared.";
}
