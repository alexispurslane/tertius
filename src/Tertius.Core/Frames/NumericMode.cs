namespace Tertius;

/// <summary>
/// How a frame moves as a numeric frame: a frame that the engine makes around a group of bodies, only for precision. See
/// docs/plans/numeric-frames.md.
/// </summary>
internal enum NumericMode : byte
{
    /// <summary>A semantic frame: one that content or gameplay made.</summary>
    None = 0,

    /// <summary>
    /// Falls freely in the field of its semantic frame. Each step it keeps the field at its origin, at the start of the
    /// step, as its acceleration, and its members feel the field at their true positions minus that acceleration.
    /// </summary>
    Falling = 1,

    /// <summary>Stays at one place in its parent. Its members feel the whole field.</summary>
    Resting = 2,
}
