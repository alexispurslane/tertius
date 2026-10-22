namespace Tertius.Testing;

/// <summary>The event payload that the shared test components use: <see cref="Recorder"/> wants it by default.</summary>
public struct Ping : IEventPayload
{
    public int Value;
}
