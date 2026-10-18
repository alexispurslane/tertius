namespace Tertius;

/// <summary>
/// One entity with a body that is not attached, as the World hands it to <see cref="FrameTree.UpdateNumericFrames"/> at a
/// sync point, or the interior of a frame owner: a static body at the origin of the owner's frame, in that frame's own
/// system, which is <see cref="Fixed"/> there and owns no frame. Speeds are in m/s and distances in meters, both in the
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
    /// description, or for the hull of a frame, the motion of the frame's driver (see <see cref="BodyDesc.FrameHullMotion"/>),
    /// whatever the description says: the hull of a Physics, Kepler or Spin frame moves.
    /// </summary>
    public bool Static { get; init; }

    /// <summary>
    /// Physics keeps the body kinematic, so no contact changes its velocity: only the game moves it. In a contact, Jolt
    /// treats it as infinitely heavy, so a still door stops a crate as a static wall does. The hull of a Kepler or Spin
    /// frame is kinematic too, and also <see cref="Fixed"/>.
    /// </summary>
    public bool Kinematic { get; init; }

    /// <summary>
    /// It stays in its semantic frame's own system, and so does its group. It is the hull of a frame that physics does not
    /// drive, which the Static, Spin or Kepler driver moves on rails, or the interior of a frame owner, which physics keeps
    /// as a static body in the frame's own system, at its origin. A Kepler hull that lives in an orbit frame of its own
    /// (see <see cref="NumericFrameRules.OrbitsInFrame"/>) is the one exception.
    /// </summary>
    public bool Fixed { get; init; }

    /// <summary>
    /// It owns a frame whose Kepler driver moves it along an orbit. When the orbit moves it fast or far in its semantic
    /// frame, it lives in a numeric frame of its own that follows the orbit (<see cref="NumericMode.Orbiting"/>), where it
    /// rests (docs/plans/numeric-frames.md, "Anchors", rule 3).
    /// </summary>
    public bool Orbits { get; init; }

    /// <summary>
    /// The mode of <see cref="Home"/>: <see cref="NumericMode.None"/> for the semantic frame's own system. The update fills
    /// it in from the tree, so the World leaves it unset.
    /// </summary>
    public NumericMode HomeMode { get; init; }

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
    /// <see cref="NumericFrameRules.Summarize"/>). The update fills it in, as it fills <see cref="HomeMode"/>, so the World
    /// leaves it unset.
    /// </summary>
    public bool MayTouchFixedGhost { get; init; }

    /// <summary>The frame that the entity owns, or null. A move puts that frame under the body's new home.</summary>
    public FrameHandle OwnedFrame { get; init; }

    /// <summary>
    /// True for a static body, for the hull of a frame that physics does not drive, and for the interior of a frame owner.
    /// No force moves an anchor, so it counts as infinitely heavy: a group that the merge test links to it takes its home
    /// (docs/plans/numeric-frames.md, "Anchors").
    /// </summary>
    public bool Anchor => Static || Fixed;
}

/// <summary>
/// The place of a ghost that physics can keep in a frame of the semantic frame P that <see cref="FrameTree.UpdateNumericFrames"/>
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
internal readonly record struct GhostPlace(FrameHandle Home, Double3 HomePosition, Double3 Velocity, double Radius, int Target,
    bool OfStaticBody, bool Sensor);

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
/// <param name="Orbiter">The index among the members of the first Kepler hull that lives in an orbit frame of its own (see
/// <see cref="NumericFrameRules.OrbitsInFrame"/>), or -1.</param>
/// <param name="Orbits">True when the group takes the orbit frame of <paramref name="Orbiter"/>: no anchor of the group rests
/// in the semantic frame.</param>
/// <param name="Thrusts">True when a body of the group that is not an anchor thrusts (see <see cref="NumericBody.Thrusts"/>), so
/// the group uses the speeds of a group that thrusts. A Kepler hull is an anchor, so it never counts.</param>
internal readonly record struct NumericGroup(Double3 Velocity, Double3 Center, bool Anchored, bool Fixed, FrameHandle Frame, double FrameMass,
    int Orbiter = -1, bool Orbits = false, bool Thrusts = false);

/// <summary>
/// The decisions of the membership and nesting rules of numeric frames (docs/plans/numeric-frames.md, "Membership" and
/// "Nesting for vast distances"), and of the places of the ghosts that the merge test takes as inputs ("Bubbles and
/// ghosts"), as pure functions of plain data. <see cref="FrameTree.UpdateNumericFrames"/> calls them and acts on the tree.
/// </summary>
internal static class NumericFrameRules
{
    /// <summary>
    /// The mode that a group's frame gets, or <see cref="NumericMode.None"/> for the semantic frame's own system.
    /// <paramref name="current"/> is the mode of the group's frame now, or None when it has none: an orbit frame counts as
    /// a falling one. A group leaves a numeric frame only below the lower thresholds, and a resting frame starts to fall
    /// only at the speed to get a falling frame. A group that <paramref name="thrusts"/> uses the speeds of
    /// <see cref="NumericFrameOptions.ThrustCreateSpeed"/> and <see cref="NumericFrameOptions.ThrustLeaveSpeed"/>, and any
    /// other group those of <see cref="NumericFrameOptions.CreateSpeed"/> and <see cref="NumericFrameOptions.LeaveSpeed"/>.
    /// A fixed group never gets a frame, and an anchored group never falls.
    /// </summary>
    internal static NumericMode Mode(NumericMode current, Double3 velocity, Double3 center, bool anchored, bool isFixed,
        bool thrusts, NumericFrameOptions options)
    {
        if (isFixed)
            return NumericMode.None;
        double speed = velocity.Length, distance = center.Length;
        double createSpeed = thrusts ? options.ThrustCreateSpeed : options.CreateSpeed;
        double leaveSpeed = thrusts ? options.ThrustLeaveSpeed : options.LeaveSpeed;
        bool fast = !anchored && speed >= createSpeed;
        return current switch
        {
            NumericMode.Falling or NumericMode.Orbiting when !anchored && speed >= leaveSpeed => NumericMode.Falling,
            NumericMode.Falling or NumericMode.Resting or NumericMode.Orbiting =>
                fast ? NumericMode.Falling : distance >= options.LeaveDistance ? NumericMode.Resting : NumericMode.None,
            _ => fast ? NumericMode.Falling : distance >= options.CreateDistance ? NumericMode.Resting : NumericMode.None,
        };
    }

    /// <summary>
    /// The mode that a group gets: <see cref="NumericMode.Orbiting"/> when it takes the orbit frame of its Kepler hull (see
    /// <see cref="NumericGroup.Orbits"/>), and else the mode of <see cref="Mode"/>.
    /// </summary>
    internal static NumericMode GroupMode(NumericMode current, in NumericGroup group, NumericFrameOptions options) =>
        group.Orbits ? NumericMode.Orbiting : Mode(current, group.Velocity, group.Center, group.Anchored, group.Fixed, group.Thrusts, options);

    /// <summary>
    /// The mode that a body alone, outside a numeric frame, needs: None when it needs no frame. It uses the speeds of a body
    /// that thrusts when the body does (see <see cref="NumericBody.Thrusts"/>).
    /// </summary>
    internal static NumericMode BodyMode(in NumericBody body, NumericFrameOptions options) => BodyMode(body, body.Thrusts, options);

    /// <summary>
    /// The mode that a body alone, outside a numeric frame, needs, with the speeds of a body that thrusts when
    /// <paramref name="thrusts"/> is true: None when it needs no frame. A Kepler hull never thrusts, and when the speed and
    /// distance rules give it a frame, it needs an orbit frame (see <see cref="OrbitsInFrame"/>).
    /// </summary>
    internal static NumericMode BodyMode(in NumericBody body, bool thrusts, NumericFrameOptions options) =>
        !body.Orbits ? Mode(NumericMode.None, body.Velocity, body.Position, body.Static, body.Fixed, thrusts, options)
        : Mode(NumericMode.None, body.Velocity, body.Position, false, false, false, options) == NumericMode.None ? NumericMode.None
        : NumericMode.Orbiting;

    /// <summary>
    /// True when <paramref name="body"/> is the hull of a Kepler frame that lives in an orbit frame of its own
    /// (docs/plans/numeric-frames.md, "Anchors", rule 3): when the speed and distance rules that give any body a numeric
    /// frame give one to the hull alone, by its velocity and its place in its semantic frame. A hull in a numeric frame
    /// keeps it down to the thresholds to leave one, as any group does. The orbit moves the hull, so its own motion decides,
    /// never the group that the merge test links it to. Its orbit moves it, so it never thrusts: it uses the speeds of free
    /// bodies.
    /// </summary>
    internal static bool OrbitsInFrame(in NumericBody body, NumericFrameOptions options) =>
        body.Orbits && Mode(body.HomeMode == NumericMode.None ? NumericMode.None : NumericMode.Falling, body.Velocity, body.Position,
            anchored: false, isFixed: false, thrusts: false, options) != NumericMode.None;

    /// <summary>
    /// The pull of a point gravity of <paramref name="mu"/> m³/s² at <paramref name="position"/>, in m/s²: the acceleration
    /// of a Kepler orbit there, which takes its G·M from that gravity (see <see cref="FrameTree.KeplerMu"/>).
    /// </summary>
    internal static Double3 OrbitAcceleration(Double3 position, double mu)
    {
        double r2 = position.LengthSquared;
        return position * (-mu / (r2 * Math.Sqrt(r2)));
    }

    /// <summary>
    /// The chord velocity of a Kepler orbit of <paramref name="mu"/> m³/s² over the step of <paramref name="dt"/> seconds
    /// that ends where the orbit is at <paramref name="position"/> with <paramref name="velocity"/>: the change of place
    /// over that step divided by its length. It is the velocity that Jolt gives a body that moves along the orbit, and a
    /// frame that starts a step at it, with the orbit's acceleration (see <see cref="OrbitAcceleration"/>), ends the step on
    /// the orbit in the scheme that Jolt uses for bodies. The Taylor series of the two-body motion to the jerk leaves out
    /// terms of the fourth order in the step, under 1e-13 m over a step of 1/60 s in low orbit. It comes from the state, not
    /// from the difference of two places, so it keeps every digit of a velocity far from the parent, where a place rounds.
    /// </summary>
    internal static Double3 OrbitChord(Double3 position, Double3 velocity, double mu, double dt)
    {
        double r2 = position.LengthSquared;
        double pull = -mu / (r2 * Math.Sqrt(r2));
        Double3 acceleration = position * pull;
        Double3 jerk = (velocity - position * (3 * Double3.Dot(position, velocity) / r2)) * pull;
        return velocity - acceleration * (dt / 2) + jerk * (dt * dt / 6);
    }

    /// <summary>
    /// The merge test: true when the bounding spheres of the two bodies, <paramref name="separation"/> apart, grown by their
    /// relative speed times the lookahead time, overlap or touch. Two bodies of one system, a numeric frame's or the
    /// semantic frame's own, stay linked up to the split margin farther, so that a body at the edge of the reach does not
    /// go in and out at each sync point.
    /// </summary>
    internal static bool MayTouch(in NumericBody a, in NumericBody b, Double3 separation, NumericFrameOptions options) =>
        MayTouch(a.Radius + b.Radius, a.Velocity - b.Velocity, a.Home == b.Home, separation, options);

    /// <summary>
    /// The merge test of <paramref name="body"/> and the place of a ghost (see <see cref="GhostPlace"/>), the same as the test
    /// of two bodies: true when the body's bounding sphere and the ghost's, <paramref name="separation"/> apart, grown by
    /// their relative speed times the lookahead time, overlap or touch. A body in the system that holds the ghost stays
    /// linked up to the split margin farther, so that a body at the edge of the reach does not go in and out at each sync
    /// point.
    /// </summary>
    internal static bool MayTouch(in NumericBody body, in GhostPlace place, Double3 separation, NumericFrameOptions options) =>
        MayTouch(body.Radius + place.Radius, body.Velocity - place.Velocity, body.Home == place.Home, separation, options);

    private static bool MayTouch(double radii, Double3 relativeVelocity, bool oneSystem, Double3 separation, NumericFrameOptions options)
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
    private static double Allowance(double speed, NumericFrameOptions options) => Math.Max(speed * options.LookaheadTime, options.SplitMargin);

    /// <summary>
    /// True when <paramref name="body"/>, a body of the frame whose bubble is <paramref name="bubble"/>, can stick out of that
    /// bubble before the next update, so that physics can give it a ghost in the frame's tree parent: it can move, and its
    /// bounding sphere, grown by how far it can move (see <see cref="Allowance"/>), reaches outside the bubble. A static body
    /// never moves, and a static body that is not a hull has no such ghost. The hull of a frame that its driver moves is not
    /// static, and it never leaves the bubble, so it sticks out by as far as it lies.
    /// </summary>
    internal static bool MayStickOut(FrameBounds bubble, in NumericBody body, NumericFrameOptions options) =>
        !body.Static && bubble.SignedDistance(body.Position) > -(body.Radius + Allowance(body.Velocity.Length, options));

    /// <summary>
    /// True when a body outside a frame's bubble <paramref name="bubble"/>, at <paramref name="position"/> in the frame and
    /// moving at <paramref name="velocity"/> relative to it, can touch the bubble before the next update, so that physics can
    /// give it a ghost in the frame's own system: the bubble is within <paramref name="reach"/> of the body, grown by how far
    /// the body can move (see <see cref="Allowance"/>).
    /// </summary>
    internal static bool MayTouchBubble(FrameBounds bubble, Double3 position, Double3 velocity, double reach, NumericFrameOptions options) =>
        bubble.SignedDistance(position) <= reach + Allowance(velocity.Length, options);

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
    internal static bool MayReachBubble(double distance, double reach, double speedBound, double scale, NumericFrameOptions options)
    {
        double far = reach + Allowance(speedBound, options);
        return distance <= far + 1e-12 * (scale + far);
    }

    /// <summary>
    /// True when Jolt makes a contact between <paramref name="body"/> and the ghost behind <paramref name="place"/>, so that
    /// the place links the body. It mirrors the two tests that physics runs on each pair of bodies:
    /// <list type="bullet">
    /// <item>The table of layers. Physics gives every ghost behind a place the ghost layer of its body, and the mask of a frame
    /// hull names only the static and hull roles. So a hull never meets the ghost. The collision groups and masks of the two
    /// bodies do not count, as in the merge test of two bodies.</item>
    /// <item>The motions. The ghost of a body that can move is kinematic, and Jolt makes a contact between a kinematic body
    /// and another body only when the other one is dynamic or either one is a sensor. The engine never asks Jolt for other
    /// contacts. The ghost of a static body meets only what the static body meets: a dynamic body, and a kinematic one when
    /// the static body is a sensor. A character counts as dynamic.</item>
    /// </list>
    /// </summary>
    internal static bool MeetsGhost(in NumericBody body, in GhostPlace place)
    {
        if (!body.OwnedFrame.IsNull)
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
    internal static (bool Hold, double Apart) SplitClock(bool together, double apart, double dt, NumericFrameOptions options)
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
    internal static (bool Thrusts, double ThrustTime, double QuietTime) ThrustClock(double acceleration, double thrustTime, double quietTime,
        double dt, NumericFrameOptions options)
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
    internal static (bool Hold, double Apart) LeaveClock(NumericMode mode, FrameHandle frame, double apart, double dt, NumericFrameOptions options)
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
    /// <item>A Kepler hull that lives in an orbit frame of its own (see <see cref="OrbitsInFrame"/>) moves as its orbit
    /// says, so it adds nothing to the group's motion, and the first such hull among the members is the group's
    /// <see cref="NumericGroup.Orbiter"/>. Any other Kepler hull is fixed in the semantic frame's own system, as the hull of
    /// a Static or Spin frame is.</item>
    /// <item>A fixed hull, or a body that may touch a ghost fixed in the semantic frame's own system, fixes the group, and a
    /// static body anchors it, with the motion of its static bodies alone: the velocity zero, their center of mass, and as
    /// its frame the numeric frame that holds one of them, the first on a tie, or none when they all lie in the semantic
    /// frame's own system. Then <see cref="Mode"/> gives the group the mode that
    /// its static bodies get alone, so the group takes their home. These anchors rest in the semantic frame, so they win
    /// over an orbit frame, and each orbiting hull of the group keeps its own frame and a ghost in the group's home.</item>
    /// <item>A group with an orbiter and no anchor at rest <see cref="NumericGroup.Orbits"/>: it takes the orbiter's orbit
    /// frame, the orbiter's home when it is in one, or else a new one.</item>
    /// <item>A group with a kinematic body and no anchor has the velocity and the center of its kinematic bodies alone: no
    /// contact changes their velocity, so the group moves with them, and in a falling frame they are slow, as a door in a
    /// fast crate's group must be, because Jolt gives a kinematic body no linear cast. A kinematic body can move between
    /// frames, so it is no anchor, and the frame that the group keeps is chosen by the masses as before.</item>
    /// </list>
    /// A frame that holds a static body of the group, or the orbit frame of its orbiter, has the mass +∞, which only
    /// comparisons read. Those motions are the limits of the mass-weighted means as the anchors' mass grows without bound,
    /// and no sum multiplies or divides by the infinite mass, which would give NaN. Only the group of its hull keeps an
    /// orbit frame, so a body in one adds no mass to it. <paramref name="homeMasses"/> is scratch: it ends with each numeric
    /// home of the group but an orbit frame, and the mass in it, in the order of the members.
    /// </summary>
    internal static NumericGroup Summarize(ReadOnlySpan<NumericBody> members, List<(FrameHandle Frame, double Mass)> homeMasses,
        NumericFrameOptions options)
    {
        Double3 momentum = Double3.Zero, moment = Double3.Zero, staticMoment = Double3.Zero, kinematicMomentum = Double3.Zero, kinematicMoment = Double3.Zero;
        double movingMass = 0, mass = 0, staticMass = 0, kinematicMass = 0;
        bool anchored = false, isFixed = false, thrusts = false;
        int orbiter = -1;
        homeMasses.Clear();
        for (int i = 0; i < members.Length; i++)
        {
            ref readonly NumericBody body = ref members[i];
            if (OrbitsInFrame(body, options))
            {
                if (orbiter < 0)
                    orbiter = i;
                continue;
            }
            anchored |= body.Static;
            isFixed |= body.Fixed || body.MayTouchFixedGhost;
            thrusts |= body.Thrusts && !body.Anchor;
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
            if (body.Home == body.Semantic || body.HomeMode == NumericMode.Orbiting)
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
        if (orbiter >= 0 && !anchored && !isFixed)
        {
            ref readonly NumericBody hull = ref members[orbiter];
            FrameHandle orbit = hull.HomeMode == NumericMode.Orbiting ? hull.Home : FrameHandle.Null;
            return new NumericGroup(Double3.Zero, hull.Position, false, false, orbit, orbit.IsNull ? 0 : double.PositiveInfinity, orbiter, true, thrusts);
        }
        FrameHandle frame = FrameHandle.Null;
        double frameMass = 0;
        foreach ((FrameHandle home, double homeMass) in homeMasses)
            if (homeMass > frameMass)
                (frame, frameMass) = (home, homeMass);
        // A frame without a static body is not the home of an anchored group, however much of its mass is there.
        if (anchored && !double.IsPositiveInfinity(frameMass))
            (frame, frameMass) = (FrameHandle.Null, 0);
        Double3 velocity = anchored || movingMass == 0 ? Double3.Zero : kinematicMass > 0 ? kinematicMomentum / kinematicMass : momentum / movingMass;
        Double3 center = anchored ? staticMoment / staticMass : kinematicMass > 0 ? kinematicMoment / kinematicMass : moment / mass;
        return new NumericGroup(velocity, center, anchored, isFixed, frame, frameMass, orbiter, Thrusts: thrusts);
    }

    /// <summary>
    /// The velocity that a kept frame of mode <paramref name="frameMode"/> and velocity <paramref name="frameVelocity"/>
    /// takes for its group, which gets <paramref name="groupMode"/> and moves at <paramref name="groupVelocity"/>: none
    /// when the group comes to rest, the group's velocity when the frame starts to fall or when the members' common
    /// velocity relative to the frame passes the drain speed, and else its own.
    /// </summary>
    internal static Double3 DrainVelocity(NumericMode frameMode, NumericMode groupMode, Double3 frameVelocity, Double3 groupVelocity,
        NumericFrameOptions options) =>
        groupMode == NumericMode.Resting ? Double3.Zero
        : frameMode == NumericMode.Resting || (groupVelocity - frameVelocity).Length > options.DrainSpeed ? groupVelocity
        : frameVelocity;

    /// <summary>
    /// The origin that a kept frame at <paramref name="framePosition"/> takes: the group's center of mass when it is
    /// farther than the recenter distance, and else its own. Both are in the semantic frame.
    /// </summary>
    internal static Double3 Origin(Double3 framePosition, Double3 center, NumericFrameOptions options) =>
        (center - framePosition).Length > options.RecenterDistance ? center : framePosition;

    /// <summary>True when a numeric frame goes away: no body calls it home and it holds no frame.</summary>
    internal static bool Collects(bool holdsBody, bool holdsFrame) => !holdsBody && !holdsFrame;

    /// <summary>True when a falling or resting frame at <paramref name="position"/> in its tree parent must nest.</summary>
    internal static bool NeedsNest(Double3 position, NumericFrameOptions options) => position.Length > options.NestDistance;

    /// <summary>The grid point nearest <paramref name="position"/>. The grid is a power of two, so this is exact.</summary>
    internal static Double3 GridPoint(Double3 position, double grid) =>
        new(Math.Round(position.X / grid) * grid, Math.Round(position.Y / grid) * grid, Math.Round(position.Z / grid) * grid);

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
    internal static FrameHandle NearestNest(ReadOnlySpan<(FrameHandle Nest, Double3 Place)> nests, Double3 parentPlace, Double3 position,
        NumericFrameOptions options)
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
