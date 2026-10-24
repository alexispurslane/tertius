namespace Tertius;

/// <summary>
/// One entity with a body that is not attached, as the World hands it to <see cref="FrameTree.UpdateNumericFrames"/> at a
/// sync point, or the interior of a frame owner: a body at rest at the origin of the owner's frame, in that frame's own
/// system, which owns no frame. Physics keeps the interior static while the hull is static and kinematic while the hull
/// can move, and the update counts it as <see cref="Static"/> and <see cref="Fixed"/> in either motion, because the
/// interior and every place that it can meet are fixed in the frame's own system. Speeds are in m/s and distances in meters, both in the
/// axes of <see cref="Semantic"/>.
/// </summary>
internal readonly record struct NumericBody
{
    public Entity Entity { get; init; }

    /// <summary>The frame of the entity's row: a semantic frame.</summary>
    public FrameHandle Semantic { get; init; }

    /// <summary>The frame whose physics system holds the body: <see cref="Semantic"/>, or a numeric frame below it.</summary>
    public FrameHandle Home { get; init; }

    /// <summary>The position in <see cref="Semantic"/>. Far from its origin, it rounds.</summary>
    public Double3 Position { get; init; }

    /// <summary>The position in <see cref="Home"/>, which stays exact near the home's origin.</summary>
    public Double3 HomePosition { get; init; }

    /// <summary>The velocity relative to <see cref="Semantic"/>.</summary>
    public Double3 Velocity { get; init; }

    /// <summary>The radius of the body's bounding sphere.</summary>
    public double Radius { get; init; }

    /// <summary>The mass in kg, above zero.</summary>
    public double Mass { get; init; }

    /// <summary>
    /// Physics keeps the body static, so it cannot take a velocity, and its group cannot fall. That is the motion of the
    /// description, or for the hull of a frame, the motion that the hull has in the step that the update plans, the next
    /// one. A change of motion that its owner wrote in the tick counts, although the frame tree stores it only when that
    /// step has finished (see <see cref="FrameTree.HullMotion"/>).
    /// </summary>
    public bool Static { get; init; }

    /// <summary>
    /// Physics keeps the body kinematic, so no contact changes its velocity: only the game moves it. In a contact, Jolt
    /// treats it as infinitely heavy, so a still door stops a crate as a static wall does. A kinematic hull, which the
    /// samples of a rails component move, is also <see cref="Fixed"/>.
    /// </summary>
    public bool Kinematic { get; init; }

    /// <summary>
    /// It stays in its semantic frame's own system, and so does its group. It is a hull whose motion is not dynamic, which
    /// holds still or follows the samples of a rails component, or the interior of a frame owner, which physics keeps at
    /// rest at the origin of the frame's own system, in either motion of the hull. A kinematic hull that lives in a following frame of its own (see
    /// <see cref="NumericFrameRules.FollowsInFrame"/>) is the one exception.
    /// </summary>
    public bool Fixed { get; init; }

    /// <summary>
    /// It is <see cref="Kinematic"/>: a hull or a plain body that only its motion intents and teleports move. When it is
    /// fast or far in its semantic frame, it lives in a following frame of its own, which follows its motion intents
    /// (<see cref="NumericMode.Following"/>), where it rests at the origin as the frame's anchor (docs/plans/numeric-frames.md,
    /// "Anchors", rule 3, and "Following frames"). It is a read of the motion, so no body can follow without being kinematic.
    /// </summary>
    public bool Follows => Kinematic;

    /// <summary>
    /// The mode of <see cref="Home"/>: <see cref="NumericMode.None"/> for the semantic frame's own system. The update fills
    /// it in from the tree, so the World leaves it unset.
    /// </summary>
    public NumericMode HomeMode { get; init; }

    /// <summary>
    /// <see cref="Home"/> is the following frame whose anchor the body is (see <see cref="FrameTree.Anchors"/>), so the body
    /// rests at its origin, or its frame hangs there. The update fills it in from the tree, as it fills
    /// <see cref="HomeMode"/>, so the World leaves it unset. A body that follows in another home, such as a plain body that
    /// sped up in the following frame of another anchor, has none of its own yet.
    /// </summary>
    public bool AnchorsHome { get; init; }

    /// <summary>
    /// A sensor, which detects touches but does not push. Jolt makes a contact between a kinematic body and a static one
    /// only when one of them is a sensor, so a static sensor meets the kinematic ghost of a body that moves.
    /// </summary>
    public bool Sensor { get; init; }

    /// <summary>A character, which always thrusts (see <see cref="Thrusts"/>): the game can walk it on any tick, and a walk is a thrust.</summary>
    public bool Character { get; init; }

    /// <summary>
    /// The acceleration in m/s² that the force and impulse intents of the entity ask of the body in the step after the sync
    /// point: (|force| + |impulse| / step) / mass. Gravity is no intent, and a torque does not move the body, so neither
    /// counts. Zero for a body that physics gives no force, which is any body that is neither dynamic nor a character, and
    /// for the interior of a frame owner. Its thrust clock reads it (see <see cref="NumericFrameRules.ThrustClock"/>).
    /// </summary>
    public double ThrustAcceleration { get; init; }

    /// <summary>
    /// The body thrusts, so its group gets a falling frame from the lower speeds of
    /// <see cref="NumericFrameOptions.ThrustCreateSpeed"/> and <see cref="NumericFrameOptions.ThrustLeaveSpeed"/>: it is a
    /// <see cref="Character"/>, or its thrust clock says so (see <see cref="NumericFrameRules.ThrustClock"/>). The update fills
    /// it in, as it fills <see cref="HomeMode"/>, so the World leaves it unset.
    /// </summary>
    public bool Thrusts { get; init; }

    /// <summary>
    /// The merge test links the body to the place of a ghost that is fixed in its semantic frame's own system (see
    /// <see cref="GhostPlace"/>), so its group stays in that system, as a group with a fixed hull does (see
    /// <see cref="NumericFrameRules.Summarize"/>). A body that lives in a following frame of its own stays there, apart from
    /// its group, and that system keeps a ghost of it, as for any anchor at rest there. The update fills it in, as it fills
    /// <see cref="HomeMode"/>, so the World leaves it unset.
    /// </summary>
    public bool MayTouchFixedGhost { get; init; }

    /// <summary>The frame that the entity owns, or null. A move puts that frame under the body's new home.</summary>
    public FrameHandle OwnedFrame { get; init; }

    /// <summary>
    /// True when physics lets the body's ghosts reach the content of a frame, its bubble grown by its excess, and not only
    /// its bubble (fork F2 of docs/plans/complete-ghosts.md): a static body, a kinematic body or a hull. The stick-out ghost
    /// of a member of the frame in the frame's tree parent meets none of them but a sensor, so only a ghost of the body in
    /// the frame brings them together with the frame's members. A dynamic plain body or a character reaches only the bubble,
    /// as in <c>BodyRecord.GhostsReachContent</c> of the physics backend.
    /// </summary>
    public bool GhostsReachContent => Static || Kinematic || !OwnedFrame.IsNull;

    /// <summary>
    /// True when the body's ghost in a frame whose system holds no hull of a frame that holds the body meets hulls: a static
    /// body or a hull. Such a frame is a semantic child of the body's semantic frame, or a sibling of the bubble that the
    /// body sticks out of. Physics gives a ghost there the role of a child ghost, the body's own role for a static body and
    /// the static role of the owner's interior for a hull, which the mask of a hull names, and the ghost role, which it does
    /// not, to a ghost of a plain body that can move (see <c>GhostBody.Role</c> of the physics backend and
    /// <see cref="GhostPlace.MeetsHulls"/>).
    /// </summary>
    public bool GhostsMeetHulls => Static || !OwnedFrame.IsNull;
}

/// <summary>
/// The place of a ghost that physics can keep in a frame of the semantic frame P that
/// <see cref="FrameTree.UpdateNumericFrames"/>
/// works on: the ghost of a body of another semantic frame, which the merge test takes as an input, because no body of P can
/// move it (ticket ter-4psc). A place is not a body: it never moves, keeps no frame and gets no move or clock.
/// </summary>
/// <param name="Home">The frame whose system holds the ghost: the tree parent of the child bubble that the body sticks out
/// of, or P itself.</param>
/// <param name="HomePosition">The place of the ghost's center in <paramref name="Home"/>.</param>
/// <param name="Velocity">The velocity of the ghost relative to P, in P's axes.</param>
/// <param name="Radius">The radius of the bounding sphere of the ghost's body, in meters.</param>
/// <param name="Target">The index among the bodies of the update of the hull whose home holds the ghost, which a body that
/// can touch the ghost joins, or -1 when the ghost is fixed in P's own system.</param>
/// <param name="OfStaticBody">The body behind the ghost is static, so its ghost meets only what a static body meets (see
/// <see cref="NumericFrameRules.MeetsGhost"/>).</param>
/// <param name="Sensor">The body behind the ghost is a sensor, and so is its ghost.</param>
/// <param name="MeetsHulls">The ghost meets hulls: it is a ghost in a child frame or in a sibling of a static body or of a hull
/// (see <see cref="NumericBody.GhostsMeetHulls"/>). A ghost in the tree parent of the bubble that the body sticks out of
/// has the ghost role, which the mask of a hull does not name (see <see cref="NumericFrameRules.MeetsGhost"/>).</param>
internal readonly record struct GhostPlace(
    FrameHandle Home,
    Double3 HomePosition,
    Double3 Velocity,
    double Radius,
    int Target,
    bool OfStaticBody,
    bool Sensor,
    bool MeetsHulls
);

/// <summary>
/// The motion of a group of bodies of one semantic frame that the merge test links.
/// </summary>
/// <param name="Velocity">The mass-weighted mean velocity of the bodies that are not static, relative to the semantic
/// frame: of the kinematic bodies alone when the group has one, because a kinematic body counts as infinitely heavy. Zero
/// when a body is static, because then the group cannot fall.</param>
/// <param name="Center">The center of mass of the bodies, in the semantic frame: of the static bodies alone when the group
/// has one, and else of the kinematic bodies alone when it has one, because both count as infinitely heavy.</param>
/// <param name="Anchored">True when a body of the group is static.</param>
/// <param name="Fixed">True when a body of the group stays in the semantic frame's own system, so the whole group does: a
/// fixed body, or one that may touch a ghost fixed there (see <see cref="NumericBody.MayTouchFixedGhost"/>).</param>
/// <param name="Frame">The numeric frame that holds the most of the group's mass, the first one on a tie, or null. For an
/// anchored group, the numeric frame that holds one of its static bodies, the first one on a tie, or null.</param>
/// <param name="FrameMass">The mass of the group's bodies in <paramref name="Frame"/>, in kg: +∞ when a static body of the
/// group is there, and 0 when the frame is null.</param>
/// <param name="Anchor">The index among the members of the first body that lives in a following frame of its own (see
/// <see cref="NumericFrameRules.FollowsInFrame"/>), or -1: the anchor whose following frame the group takes when it
/// <paramref name="Follows"/>.</param>
/// <param name="Follows">True when the group takes the following frame of <paramref name="Anchor"/>: no anchor of the group rests
/// in the semantic frame.</param>
/// <param name="Thrusts">True when a body of the group that is not an anchor thrusts (see
/// <see cref="NumericBody.Thrusts"/>), so
/// the group uses the speeds of a group that thrusts. A kinematic body never thrusts.</param>
internal readonly record struct NumericGroup(
    Double3 Velocity,
    Double3 Center,
    bool Anchored,
    bool Fixed,
    FrameHandle Frame,
    double FrameMass,
    int Anchor = -1,
    bool Follows = false,
    bool Thrusts = false
);

/// <summary>
/// The decisions of the membership and nesting rules of numeric frames (docs/plans/numeric-frames.md, "Membership" and
/// "Nesting for vast distances"), and of the places of the ghosts that the merge test takes as inputs ("Bubbles and
/// ghosts"), as pure functions of plain data. <see cref="FrameTree.UpdateNumericFrames"/> calls them and acts on the tree.
/// </summary>
internal static class NumericFrameRules
{
    /// <summary>
    /// True when <paramref name="body"/> is an anchor in the rules of the update (docs/plans/numeric-frames.md, "Anchors" and
    /// "Following frames"): no force moves it, and no group can take it into another home, so it counts as infinitely heavy,
    /// and a group that the merge test links to it takes its home. The split clock of a numeric frame holds none of its
    /// anchors, and an anchor gets no leave clock. A static body, a hull that is not dynamic and the interior of a frame
    /// owner are anchors always. A kinematic body is one only while it lives in a following frame of its own (see
    /// <see cref="FollowsInFrame"/>), which follows its motion intents. A plain kinematic anchor can leave that frame, by a
    /// hand-off into a bubble or when it slows down below the thresholds to leave a numeric frame, and then it is no anchor:
    /// a plain kinematic body that is slow and near is infinitely heavy in its group, but the group's home is its home.
    /// </summary>
    internal static bool IsAnchor(in NumericBody body, NumericFrameOptions options) =>
        body.Static || body.Fixed || FollowsInFrame(body, options);

    /// <summary>
    /// The mode that a group's frame gets, or <see cref="NumericMode.None"/> for the semantic frame's own system.
    /// <paramref name="current"/> is the mode of the group's frame now, or None when it has none: a following frame counts as
    /// a falling one. A group leaves a numeric frame only below the lower thresholds, and a resting frame starts to fall
    /// only at the speed to get a falling frame. A group that <paramref name="thrusts"/> uses the speeds of
    /// <see cref="NumericFrameOptions.ThrustCreateSpeed"/> and <see cref="NumericFrameOptions.ThrustLeaveSpeed"/>, and any
    /// other group those of <see cref="NumericFrameOptions.CreateSpeed"/> and <see cref="NumericFrameOptions.LeaveSpeed"/>.
    /// A fixed group never gets a frame, and an anchored group never falls.
    /// </summary>
    internal static NumericMode Mode(
        NumericMode current,
        Double3 velocity,
        Double3 center,
        bool anchored,
        bool isFixed,
        bool thrusts,
        NumericFrameOptions options
    )
    {
        if (isFixed)
            return NumericMode.None;
        double speed = velocity.Length,
            distance = center.Length;
        double createSpeed = thrusts ? options.ThrustCreateSpeed : options.CreateSpeed;
        double leaveSpeed = thrusts ? options.ThrustLeaveSpeed : options.LeaveSpeed;
        bool fast = !anchored && speed >= createSpeed;
        bool keepsFalling = current is NumericMode.Falling or NumericMode.Following && !anchored && speed >= leaveSpeed;
        if (fast || keepsFalling)
            return NumericMode.Falling;
        // A group in a numeric frame keeps it out to the lower distance, and a group without one needs the higher.
        double restDistance = current switch
        {
            NumericMode.Falling or NumericMode.Resting or NumericMode.Following => options.LeaveDistance,
            NumericMode.None or NumericMode.Nesting => options.CreateDistance,
        };
        return distance >= restDistance ? NumericMode.Resting : NumericMode.None;
    }

    /// <summary>
    /// The mode that a group gets: <see cref="NumericMode.Following"/> when it takes the following frame of its anchor (see
    /// <see cref="NumericGroup.Follows"/>), and else the mode of <see cref="Mode"/>.
    /// </summary>
    internal static NumericMode GroupMode(NumericMode current, in NumericGroup group, NumericFrameOptions options) =>
        group.Follows
            ? NumericMode.Following
            : Mode(
                current: current,
                velocity: group.Velocity,
                center: group.Center,
                anchored: group.Anchored,
                isFixed: group.Fixed,
                thrusts: group.Thrusts,
                options: options
            );

    /// <summary>
    /// The mode that a body alone, outside a numeric frame, needs: None when it needs no frame. It uses the speeds of a body
    /// that thrusts when the body does (see <see cref="NumericBody.Thrusts"/>).
    /// </summary>
    internal static NumericMode BodyMode(in NumericBody body, NumericFrameOptions options) =>
        BodyMode(body, body.Thrusts, options);

    /// <summary>
    /// The mode that a body alone, outside a numeric frame, needs, with the speeds of a body that thrusts when
    /// <paramref name="thrusts"/> is true: None when it needs no frame. A kinematic body never thrusts, and when the speed
    /// and distance rules of a free body give it a frame, it needs a following frame (see <see cref="FollowsInFrame"/>).
    /// Else it is a body as any other: a hull is fixed in its semantic frame's own system, and a plain kinematic body takes
    /// the speeds of a body that thrusts with the semantic frame's other bodies, because it can join the group of one.
    /// </summary>
    internal static NumericMode BodyMode(in NumericBody body, bool thrusts, NumericFrameOptions options) =>
        body.Follows
        && Mode(
            current: NumericMode.None,
            velocity: body.Velocity,
            center: body.Position,
            anchored: false,
            isFixed: false,
            thrusts: false,
            options: options
        ) != NumericMode.None
            ? NumericMode.Following
            : Mode(
                current: NumericMode.None,
                velocity: body.Velocity,
                center: body.Position,
                anchored: body.Static,
                isFixed: body.Fixed,
                thrusts: thrusts,
                options: options
            );

    /// <summary>
    /// True when <paramref name="body"/>, a body that follows (see <see cref="NumericBody.Follows"/>), lives in a following
    /// frame of its own as its anchor (docs/plans/numeric-frames.md, "Anchors", rule 3, and "Following frames"): when the
    /// speed and distance rules that give any body a numeric frame give one to the body alone, by its velocity and its place
    /// in its semantic frame. A body in a numeric frame keeps it down to the thresholds to leave one, as any group does. Its
    /// motion intents move the body, so its own motion decides, never the group that the merge test links it to. A kinematic
    /// body never thrusts, so it uses the speeds of free bodies.
    /// </summary>
    internal static bool FollowsInFrame(in NumericBody body, NumericFrameOptions options) =>
        body.Follows
        && Mode(
            current: body.HomeMode == NumericMode.None ? NumericMode.None : NumericMode.Falling,
            velocity: body.Velocity,
            center: body.Position,
            anchored: false,
            isFixed: false,
            thrusts: false,
            options: options
        ) != NumericMode.None;

    /// <summary>
    /// The merge test: true when the bounding spheres of the two bodies, <paramref name="separation"/> apart, grown by their
    /// relative speed times the lookahead time, overlap or touch. Two bodies of one system, a numeric frame's or the
    /// semantic frame's own, stay linked up to the split margin farther, so that a body at the edge of the reach does not
    /// go in and out at each sync point.
    /// </summary>
    internal static bool MayTouch(
        in NumericBody a,
        in NumericBody b,
        Double3 separation,
        NumericFrameOptions options
    ) =>
        MayTouch(
            radii: a.Radius + b.Radius,
            relativeVelocity: a.Velocity - b.Velocity,
            oneSystem: a.Home == b.Home,
            separation: separation,
            options: options
        );

    /// <summary>
    /// The merge test of <paramref name="body"/> and the place of a ghost (see <see cref="GhostPlace"/>), the same as the test
    /// of two bodies: true when the body's bounding sphere and the ghost's, <paramref name="separation"/> apart, grown by
    /// their relative speed times the lookahead time, overlap or touch. A body in the system that holds the ghost stays
    /// linked up to the split margin farther, so that a body at the edge of the reach does not go in and out at each sync
    /// point.
    /// </summary>
    internal static bool MayTouch(
        in NumericBody body,
        in GhostPlace place,
        Double3 separation,
        NumericFrameOptions options
    ) =>
        MayTouch(
            radii: body.Radius + place.Radius,
            relativeVelocity: body.Velocity - place.Velocity,
            oneSystem: body.Home == place.Home,
            separation: separation,
            options: options
        );

    private static bool MayTouch(
        double radii,
        Double3 relativeVelocity,
        bool oneSystem,
        Double3 separation,
        NumericFrameOptions options
    )
    {
        double reach = radii + relativeVelocity.Length * options.LookaheadTime;
        if (oneSystem)
            reach += options.SplitMargin;
        return separation.LengthSquared <= reach * reach;
    }

    /// <summary>
    /// How far a body that moves at <paramref name="speed"/> m/s can move before the next update, as the tests of the places
    /// of ghosts take it: its speed times the lookahead time, and at least the split margin. The floor keeps the answer for a
    /// slow body the same whether it moves or not. So a crate that rests 1 mm inside its bubble gives a place while it is
    /// awake at 1 mm/s and while it sleeps, and a fast rock near it does not move between systems at each change. A margin
    /// added to the motion only moves that edge 10 m inward.
    /// </summary>
    private static double Allowance(double speed, NumericFrameOptions options) =>
        Math.Max(speed * options.LookaheadTime, options.SplitMargin);

    /// <summary>
    /// True when <paramref name="body"/>, a body of the frame whose bubble is <paramref name="bubble"/>, can stick out of that
    /// bubble before the next update, so that physics can give it a ghost in the frame's tree parent: it can move, and its
    /// bounding sphere, grown by how far it can move (see <see cref="Allowance"/>), reaches outside the bubble. A static body
    /// never moves, and a static body that is not a hull has no such ghost. A hull that moves is not static, and it never
    /// leaves the bubble, so it sticks out by as far as it lies.
    /// </summary>
    internal static bool MayStickOut(FrameBounds bubble, in NumericBody body, NumericFrameOptions options) =>
        !body.Static
        && bubble.SignedDistance(body.Position) > -(body.Radius + Allowance(body.Velocity.Length, options));

    /// <summary>
    /// The content excess of a frame before the next update: how far, in meters, the bounding sphere of a body of
    /// <paramref name="bodies"/>, the bodies of the frame whose bubble is <paramref name="bubble"/>, can reach beyond the
    /// bubble, each grown by how far it can move (see <see cref="Allowance"/>), as <see cref="MayStickOut"/> grows it. Only a
    /// body that can stick out counts, so a static body does not. Zero while every body stays inside. Physics gives a body
    /// of the frame's semantic parent a ghost in the frame when its bounding sphere reaches the bubble grown by the excess,
    /// the frame's content, and so does the sibling test of a body that sticks out of a sibling bubble (fork F2 of
    /// docs/plans/complete-ghosts.md).
    /// </summary>
    internal static double ContentExcess(
        FrameBounds bubble,
        ReadOnlySpan<NumericBody> bodies,
        NumericFrameOptions options
    )
    {
        double excess = 0;
        foreach (ref readonly NumericBody body in bodies)
            if (!body.Static)
                excess = Math.Max(
                    excess,
                    bubble.SignedDistance(body.Position) + body.Radius + Allowance(body.Velocity.Length, options)
                );
        return excess;
    }

    /// <summary>
    /// True when a body outside a frame's bubble <paramref name="bubble"/>, at <paramref name="position"/> in the frame and
    /// moving at <paramref name="velocity"/> relative to it, can touch the bubble before the next update, so that physics can
    /// give it a ghost in the frame's own system: the bubble is within <paramref name="reach"/> of the body, grown by how far
    /// the body can move (see <see cref="Allowance"/>).
    /// </summary>
    internal static bool MayTouchBubble(
        FrameBounds bubble,
        Double3 position,
        Double3 velocity,
        double reach,
        NumericFrameOptions options
    ) => bubble.SignedDistance(position) <= reach + Allowance(velocity.Length, options);

    /// <summary>
    /// The first test of <see cref="MayTouchBubble"/>, on distances and speeds alone. It is false when a body, or every body
    /// of a semantic sibling, cannot touch a bubble before the next update. <paramref name="distance"/> is the distance from
    /// the bubble's center to the body, or to the sibling's center. <paramref name="reach"/> is the longest radius of the
    /// bubble plus how far the body, or any body of the sibling, reaches beyond that point. A bubble lies within its longest
    /// radius of its center, so its signed distance is never below the distance less that radius.
    /// <paramref name="speedBound"/> is at least the speed relative to the bubble's frame that the full test uses. Both tests
    /// take their numbers in the coordinates of a semantic frame, which round far from its origin. So the reach grows by
    /// 1e-12 of <paramref name="scale"/>, the distances of the points from that origin, and of the reach itself. That is
    /// thousands of steps of a double, so the first test drops nothing that the full test keeps.
    /// </summary>
    internal static bool MayReachBubble(
        double distance,
        double reach,
        double speedBound,
        double scale,
        NumericFrameOptions options
    )
    {
        double far = reach + Allowance(speedBound, options);
        return distance <= far + 1e-12 * (scale + far);
    }

    /// <summary>
    /// True when Jolt makes a contact between <paramref name="body"/> and the ghost behind <paramref name="place"/>, so that
    /// the place links the body. It mirrors the two tests that physics runs on each pair of bodies:
    /// <list type="bullet">
    /// <item>The table of layers. Physics gives a ghost of a static body or of a hull in a child frame or in a sibling the
    /// role of a child ghost, which the mask of a frame hull names, and every other ghost the ghost role, which it does not
    /// (see <see cref="GhostPlace.MeetsHulls"/>).
    /// So a hull meets only a place that meets hulls. The collision groups and masks of the two bodies do not count, as in
    /// the merge test of two bodies.</item>
    /// <item>The motions. The ghost of a body that can move is kinematic, and Jolt makes a contact between a kinematic body
    /// and another body only when the other one is dynamic or either one is a sensor. The engine never asks Jolt for other
    /// contacts. The ghost of a static body meets only what the static body meets: a dynamic body, and a kinematic one when
    /// the static body is a sensor. A character counts as dynamic, and a hull is never a sensor.</item>
    /// </list>
    /// </summary>
    internal static bool MeetsGhost(in NumericBody body, in GhostPlace place)
    {
        if (!body.OwnedFrame.IsNull && !place.MeetsHulls)
            return false;
        bool dynamic = !(body.Static || body.Kinematic);
        return dynamic || (body.Kinematic && place.Sensor) || (body.Sensor && !place.OfStaticBody);
    }

    /// <summary>
    /// The split clock of one numeric frame over a sync point of <paramref name="dt"/> seconds. <paramref name="together"/>
    /// is true when the merge test links all of its members, and <paramref name="apart"/> is how long they have been apart
    /// before, in seconds. Hold is true when the members must stay in one group although they are apart. Apart is the new
    /// time, which starts again at zero when they are together or when the cooldown has passed and they split.
    /// </summary>
    internal static (bool Hold, double Apart) SplitClock(
        bool together,
        double apart,
        double dt,
        NumericFrameOptions options
    )
    {
        if (together)
            return (false, 0);
        double time = apart + dt;
        return time >= options.SplitCooldown ? (false, 0) : (true, time);
    }

    /// <summary>
    /// The thrust clock of one body over a tick of <paramref name="dt"/> seconds, whose force and impulse intents ask for
    /// <paramref name="acceleration"/> m/s² (see <see cref="NumericBody.ThrustAcceleration"/>). <paramref name="thrustTime"/>
    /// is the time of the thrust ticks so far and <paramref name="quietTime"/> the time since the last one, both in seconds.
    /// A tick whose acceleration reaches <see cref="NumericFrameOptions.MinThrustAcceleration"/> is a thrust tick: it adds
    /// <paramref name="dt"/> to the thrust time and clears the quiet time. Any other tick adds <paramref name="dt"/> to the
    /// quiet time, and when that reaches <see cref="NumericFrameOptions.ThrustWindow"/>, the clock starts again at zero.
    /// Thrusts is true while the thrust time is at least <see cref="NumericFrameOptions.ThrustOnset"/>. So a single impulse
    /// never counts, pulses less than the window apart add up, and a body that thrusts keeps counting for the window after
    /// its last thrust tick.
    /// </summary>
    internal static (bool Thrusts, double ThrustTime, double QuietTime) ThrustClock(
        double acceleration,
        double thrustTime,
        double quietTime,
        double dt,
        NumericFrameOptions options
    )
    {
        if (acceleration >= options.MinThrustAcceleration)
        {
            thrustTime += dt;
            quietTime = 0;
        }
        else
        {
            quietTime += dt;
            if (quietTime >= options.ThrustWindow)
                return (false, 0, 0);
        }

        return (thrustTime >= options.ThrustOnset, thrustTime, quietTime);
    }

    /// <summary>
    /// The leave clock of a body that the last update kept in its semantic frame's own system although it needs a frame by
    /// itself, over a sync point of <paramref name="dt"/> seconds. <paramref name="mode"/> is the mode that the body's group
    /// gets, and <paramref name="frame"/> the numeric frame that holds most of the group's mass, or null. The body is about
    /// to leave when its group would rest in a new frame: then Apart is <paramref name="apart"/>, the time it has been about
    /// to leave before, plus <paramref name="dt"/>, and Hold is true while that is below the split cooldown. Apart keeps
    /// counting past the cooldown, so that a member that another member holds does not start again. Else the clock is zero
    /// and holds nothing: a group that stays needs no hold, a group that joins a numeric frame merges at once, as numeric
    /// frames do, and a group that falls leaves at once, because the speed thresholds give falling its own hysteresis.
    /// </summary>
    internal static (bool Hold, double Apart) LeaveClock(
        NumericMode mode,
        FrameHandle frame,
        double apart,
        double dt,
        NumericFrameOptions options
    )
    {
        if (mode != NumericMode.Resting || !frame.IsNull)
            return (false, 0);
        double time = apart + dt;
        return (time < options.SplitCooldown, time);
    }

    /// <summary>
    /// The motion of the group <paramref name="members"/>, which is not empty. An anchor counts as infinitely heavy
    /// (docs/plans/numeric-frames.md, "Anchors", rule 2):
    /// <list type="bullet">
    /// <item>A body that lives in a following frame of its own (see <see cref="FollowsInFrame"/>) moves as its motion
    /// intents say, so it adds nothing to the group's motion, and the first such body among the members is the group's
    /// <see cref="NumericGroup.Anchor"/>. Any other kinematic hull is fixed in the semantic frame's own system, as a static
    /// hull is, and any other kinematic plain body counts as one below.</item>
    /// <item>A fixed hull, or a body that may touch a ghost fixed in the semantic frame's own system, fixes the group, also a
    /// body that lives in a following frame of its own: the place of that ghost is an anchor at rest in that system. A
    /// static body anchors the group, with the motion of its static bodies alone: the velocity zero, their center of mass, and as
    /// its frame the numeric frame that holds one of them, the first on a tie, or none when they all lie in the semantic
    /// frame's own system. Then <see cref="Mode"/> gives the group the mode that
    /// its static bodies get alone, so the group takes their home. These anchors rest in the semantic frame, so they win
    /// over a following frame, and each body of the group that follows keeps its own frame and a ghost in the group's home.</item>
    /// <item>A group with such an anchor and no anchor at rest <see cref="NumericGroup.Follows"/>: it takes the anchor's
    /// following frame, the anchor's home when it anchors it (see <see cref="NumericBody.AnchorsHome"/>), or else a new
    /// one.</item>
    /// <item>A group with a kinematic body and no anchor has the velocity and the center of its kinematic bodies alone: no
    /// contact changes their velocity, so the group moves with them, and in a falling frame they are slow, as a door in a
    /// fast crate's group must be, because Jolt gives a kinematic body no linear cast. A kinematic body can move between
    /// frames, so it is no anchor while it does not follow, and the frame that the group keeps is chosen by the masses as
    /// before.</item>
    /// </list>
    /// A frame that holds a static body of the group, or the following frame of its anchor, has the mass +∞, which only
    /// comparisons read. Those motions are the limits of the mass-weighted means as the anchors' mass grows without bound,
    /// and no sum multiplies or divides by the infinite mass, which would give NaN. Only the group of its anchor keeps a
    /// following frame, so a body in one adds no mass to it. <paramref name="homeMasses"/> is scratch: it ends with each numeric
    /// home of the group but a following frame, and the mass in it, in the order of the members.
    /// </summary>
    internal static NumericGroup Summarize(
        ReadOnlySpan<NumericBody> members,
        List<(FrameHandle Frame, double Mass)> homeMasses,
        NumericFrameOptions options
    )
    {
        Double3 momentum = Double3.Zero,
            moment = Double3.Zero,
            staticMoment = Double3.Zero,
            kinematicMomentum = Double3.Zero,
            kinematicMoment = Double3.Zero;
        double movingMass = 0,
            mass = 0,
            staticMass = 0,
            kinematicMass = 0;
        bool anchored = false,
            isFixed = false,
            thrusts = false;
        int anchor = -1;
        homeMasses.Clear();
        for (int i = 0; i < members.Length; i++)
        {
            ref readonly NumericBody body = ref members[i];
            if (FollowsInFrame(body, options))
            {
                if (anchor < 0)
                    anchor = i;
                // The place of a fixed ghost that the body may touch is an anchor at rest in the semantic frame's own system.
                isFixed |= body.MayTouchFixedGhost;
                continue;
            }

            anchored |= body.Static;
            isFixed |= body.Fixed || body.MayTouchFixedGhost;
            // A body that follows is the anchor above, so only a static or fixed body is an anchor here.
            thrusts |= body.Thrusts && !body.Static && !body.Fixed;
            mass += body.Mass;
            moment += body.Position * body.Mass;
            if (body.Static)
            {
                staticMass += body.Mass;
                staticMoment += body.Position * body.Mass;
            }
            else
            {
                movingMass += body.Mass;
                momentum += body.Velocity * body.Mass;
            }

            if (body.Kinematic)
            {
                kinematicMass += body.Mass;
                kinematicMomentum += body.Velocity * body.Mass;
                kinematicMoment += body.Position * body.Mass;
            }

            if (body.Home == body.Semantic || body.HomeMode == NumericMode.Following)
                continue;
            // ∞ + m and ∞ + ∞ are exact, so the sum stays +∞ once a static body is in it.
            double weight = body.Static ? double.PositiveInfinity : body.Mass;
            int h = 0;
            while (h < homeMasses.Count && homeMasses[h].Frame != body.Home)
                h++;
            if (h == homeMasses.Count)
                homeMasses.Add((body.Home, weight));
            else
                homeMasses[h] = (body.Home, homeMasses[h].Mass + weight);
        }

        if (anchor >= 0 && !anchored && !isFixed)
        {
            ref readonly NumericBody follower = ref members[anchor];
            FrameHandle following = follower.AnchorsHome ? follower.Home : FrameHandle.Null;
            return new(
                Velocity: Double3.Zero,
                Center: follower.Position,
                Anchored: false,
                Fixed: false,
                Frame: following,
                FrameMass: following.IsNull ? 0 : double.PositiveInfinity,
                Anchor: anchor,
                Follows: true,
                Thrusts: thrusts
            );
        }

        FrameHandle frame = FrameHandle.Null;
        double frameMass = 0;
        foreach ((FrameHandle home, double homeMass) in homeMasses)
            if (homeMass > frameMass)
                (frame, frameMass) = (home, homeMass);
        // A frame without a static body is not the home of an anchored group, however much of its mass is there.
        if (anchored && !double.IsPositiveInfinity(frameMass))
            (frame, frameMass) = (FrameHandle.Null, 0);
        Double3 velocity = Double3.Zero;
        if (!anchored && movingMass != 0)
            velocity = kinematicMass > 0 ? kinematicMomentum / kinematicMass : momentum / movingMass;
        Double3 center;
        if (anchored)
            center = staticMoment / staticMass;
        else
            center = kinematicMass > 0 ? kinematicMoment / kinematicMass : moment / mass;
        return new(
            Velocity: velocity,
            Center: center,
            Anchored: anchored,
            Fixed: isFixed,
            Frame: frame,
            FrameMass: frameMass,
            Anchor: anchor,
            Thrusts: thrusts
        );
    }

    /// <summary>
    /// The velocity that a kept frame of mode <paramref name="frameMode"/> and velocity <paramref name="frameVelocity"/>
    /// takes for its group, which gets <paramref name="groupMode"/> and moves at <paramref name="groupVelocity"/>: none
    /// when the group comes to rest, the group's velocity when the frame starts to fall or when the members' common
    /// velocity relative to the frame passes the drain speed, and else its own.
    /// </summary>
    internal static Double3 DrainVelocity(
        NumericMode frameMode,
        NumericMode groupMode,
        Double3 frameVelocity,
        Double3 groupVelocity,
        NumericFrameOptions options
    )
    {
        if (groupMode == NumericMode.Resting)
            return Double3.Zero;
        if (frameMode == NumericMode.Resting || (groupVelocity - frameVelocity).Length > options.DrainSpeed)
            return groupVelocity;
        return frameVelocity;
    }

    /// <summary>
    /// The origin that a kept frame at <paramref name="framePosition"/> takes: the group's center of mass when it is
    /// farther than the recenter distance, and else its own. Both are in the semantic frame.
    /// </summary>
    internal static Double3 Origin(Double3 framePosition, Double3 center, NumericFrameOptions options) =>
        (center - framePosition).Length > options.RecenterDistance ? center : framePosition;

    /// <summary>True when a numeric frame goes away: no body calls it home and it holds no frame.</summary>
    internal static bool Collects(bool holdsBody, bool holdsFrame) => !holdsBody && !holdsFrame;

    /// <summary>True when a falling or resting frame at <paramref name="position"/> in its tree parent must nest.</summary>
    internal static bool NeedsNest(Double3 position, NumericFrameOptions options) =>
        position.Length > options.NestDistance;

    /// <summary>The grid point nearest <paramref name="position"/>. The grid is a power of two, so this is exact.</summary>
    internal static Double3 GridPoint(Double3 position, double grid) =>
        new(
            Math.Round(position.X / grid) * grid,
            Math.Round(position.Y / grid) * grid,
            Math.Round(position.Z / grid) * grid
        );

    /// <summary>True when every coordinate of <paramref name="position"/> is a whole multiple of <paramref name="grid"/>.</summary>
    internal static bool OnGrid(Double3 position, double grid) => GridPoint(position, grid) == position;

    /// <summary>
    /// True when a nesting frame whose frames are at <paramref name="members"/> in it may jump by <paramref name="step"/>:
    /// every one of them is then within half the nest distance of it.
    /// </summary>
    internal static bool NestCanJump(ReadOnlySpan<Double3> members, Double3 step, NumericFrameOptions options)
    {
        foreach (Double3 member in members)
            if ((member - step).Length > options.NestDistance / 2)
                return false;
        return true;
    }

    /// <summary>
    /// The nesting frame of <paramref name="nests"/>, each with its place in the semantic frame, nearest a frame at
    /// <paramref name="position"/> in a parent at <paramref name="parentPlace"/>. Null when none is within half the nest
    /// distance. On a tie the later one wins.
    /// </summary>
    internal static FrameHandle NearestNest(
        ReadOnlySpan<(FrameHandle Nest, Double3 Place)> nests,
        Double3 parentPlace,
        Double3 position,
        NumericFrameOptions options
    )
    {
        FrameHandle nearest = FrameHandle.Null;
        double best = options.NestDistance / 2;
        foreach ((FrameHandle nest, Double3 place) in nests)
        {
            double distance = (parentPlace - place + position).Length;
            if (distance <= best)
                (nearest, best) = (nest, distance);
        }

        return nearest;
    }
}
