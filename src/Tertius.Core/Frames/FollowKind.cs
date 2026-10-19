namespace Tertius;

/// <summary>
/// How a frame followed its owner at a sync point: what moved the frame in the step that ended there (see
/// <see cref="FrameTree.Followed"/>, and "State dumps and saved state" in docs/plans/rails-components.md).
/// </summary>
public enum FollowKind : byte
{
    /// <summary>
    /// Nothing moved the frame on its own: it held still in its parent, or it followed a teleport of its owner's hull or a
    /// write of its owner's transform.
    /// </summary>
    Static = 0,

    /// <summary>The frame followed a full sample that a component of its owner wrote with <c>Self.MoveOnRails</c>.</summary>
    Rails = 1,

    /// <summary>The frame followed its owner's dynamic hull, which physics moved.</summary>
    Physics = 2,

    /// <summary>The frame followed an end pose that its owner wrote with <c>Self.MoveKinematic</c>.</summary>
    Kinematic = 3,
}
