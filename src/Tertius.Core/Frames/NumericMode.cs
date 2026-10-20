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
    /// A following frame: it holds one fast or far kinematic body at rest at its origin, its anchor, and follows the
    /// anchor's motion intents (docs/plans/numeric-frames.md, "Following frames", and "Anchors", rule 3). The anchor is a
    /// hull, whose frame hangs at the origin, or a plain body, which is a member of the frame's system there. At each sync
    /// point the frame takes the place of its step plan, the record of the anchor's motion intent of the step that ends
    /// then, or in a step without one the record of a kinematic body that holds still where the step starts, and the plan's
    /// chord as its velocity: the change of place over that step, divided by its length, which is the velocity that Jolt
    /// gives a body that moves as the plan says. A new one starts with the chord of its anchor's motion
    /// over the step that ended (see <see cref="FrameTree.UpdateNumericFrames"/>). During a step it moves as a falling frame
    /// does, with the acceleration that lands it on the plan (see <see cref="FrameTree.BeginNumericStep"/>): on a Kepler
    /// orbit, the orbit's acceleration at its origin to about 3e-10 m/s² in low orbit. So the bodies in it move as they
    /// would in the semantic frame's own system, and they feel the field minus that acceleration. In a step without a motion
    /// intent the frame holds still in its semantic frame, and its anchor with it, as a kinematic body without a motion
    /// intent holds still in the semantic frame's own system (ticket ter-zsku). It never rotates: the anchor's rotation stays
    /// with the anchor.
    /// </summary>
    Following = 4,
}
