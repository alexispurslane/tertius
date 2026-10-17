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

    /// <summary>
    /// A resting frame that holds only other numeric frames, on a grid of whole steps of
    /// <c>NumericFrameOptions.NestGrid</c> in its semantic parent, so that its position is exact in a double. It keeps the
    /// positions of numeric frames far from their semantic frame's origin small. It jumps by whole grid steps, never by a
    /// fraction of one. See "Nesting for vast distances" in the doc.
    /// </summary>
    Nesting = 3,

    /// <summary>
    /// Follows the Kepler orbit of the one frame that it holds, its orbiter: the frame of a hull whose Kepler driver moves
    /// it fast or far in its semantic frame, so that the hull rests in this frame (see "Anchors", rule 3, in the doc). At
    /// each sync point, its place in its semantic frame is the orbiter's place on the orbit, and the orbiter hangs at its
    /// origin. Its velocity is the chord of the orbit over the step that ends then: the change of place over that step,
    /// divided by its length, which is the velocity that Jolt gives a body that moves along the orbit. During a step it
    /// moves as a falling frame does, with the orbit's acceleration at its origin. So the bodies in it move as they would
    /// in the semantic frame's own system, and they feel the field minus that acceleration. It never rotates: the
    /// orbiter's rotation stays with the orbiter.
    /// </summary>
    Orbiting = 4,
}
