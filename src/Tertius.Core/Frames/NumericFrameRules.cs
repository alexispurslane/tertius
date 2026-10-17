namespace Tertius;

/// <summary>
/// One entity with a body that is not attached, as the World hands it to <see cref="FrameTree.UpdateNumericFrames"/> at a
/// sync point. Speeds are in m/s and distances in meters, both in the axes of <see cref="Semantic"/>.
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

    /// <summary>It owns a frame that physics does not drive, so it stays in its semantic frame's own system.</summary>
    public bool Fixed { get; init; }

    /// <summary>
    /// It owns a frame whose Kepler driver moves it along an orbit, so it moves in its semantic frame's own system, where
    /// every other anchor rests. Such a hull joins only a group that does not fall, and the frame of a falling group that
    /// may touch it keeps a ghost of it, until rule 3 of "Anchors" in docs/plans/numeric-frames.md gives it a numeric frame
    /// that follows its orbit.
    /// </summary>
    public bool Orbits { get; init; }

    /// <summary>
    /// A sensor, which detects touches but does not push. Jolt makes a contact between a kinematic body and a static one
    /// only when one of them is a sensor, so a static sensor meets the kinematic ghost of a body that moves.
    /// </summary>
    public bool Sensor { get; init; }

    /// <summary>The frame that the entity owns, or null. A move puts that frame under the body's new home.</summary>
    public FrameHandle OwnedFrame { get; init; }

    /// <summary>
    /// True for a static body and for the hull of a frame that physics does not drive. No force moves an anchor, so it
    /// counts as infinitely heavy: a group that the merge test links to it takes its home (docs/plans/numeric-frames.md,
    /// "Anchors"). A hull that <see cref="Orbits"/> keeps the older rule: it joins only a group that does not fall.
    /// </summary>
    public bool Anchor => Static || Fixed;
}

/// <summary>
/// The motion of a group of bodies of one semantic frame that the merge test links.
/// </summary>
/// <param name="Velocity">The mass-weighted mean velocity of the bodies that are not static, relative to the semantic
/// frame. Zero when a body is static, because then the group cannot fall.</param>
/// <param name="Center">The center of mass of the bodies, in the semantic frame: of the static bodies alone when the group
/// has one, because a static body counts as infinitely heavy.</param>
/// <param name="Anchored">True when a body of the group is static.</param>
/// <param name="Fixed">True when a body of the group stays in the semantic frame's own system, so the whole group does.</param>
/// <param name="Frame">The numeric frame that holds the most of the group's mass, the first one on a tie, or null. For an
/// anchored group, the numeric frame that holds one of its static bodies, the first one on a tie, or null.</param>
/// <param name="FrameMass">The mass of the group's bodies in <paramref name="Frame"/>, in kg: +∞ when a static body of the
/// group is there, and 0 when the frame is null.</param>
internal readonly record struct NumericGroup(Double3 Velocity, Double3 Center, bool Anchored, bool Fixed, FrameHandle Frame, double FrameMass);

/// <summary>
/// The decisions of the membership and nesting rules of numeric frames (docs/plans/numeric-frames.md, "Membership" and
/// "Nesting for vast distances"), and of the numeric frames that keep ghosts ("Bubbles and ghosts"), as pure functions of
/// plain data. <see cref="FrameTree.UpdateNumericFrames"/> calls them and acts on the tree.
/// </summary>
internal static class NumericFrameRules
{
    /// <summary>
    /// The mode that a group's frame gets, or <see cref="NumericMode.None"/> for the semantic frame's own system.
    /// <paramref name="current"/> is the mode of the group's frame now, or None when it has none. A group leaves a numeric
    /// frame only below the lower thresholds, and a resting frame starts to fall only at the speed to get a falling frame.
    /// A fixed group never gets a frame, and an anchored group never falls.
    /// </summary>
    internal static NumericMode Mode(NumericMode current, Double3 velocity, Double3 center, bool anchored, bool isFixed,
        NumericFrameOptions options)
    {
        if (isFixed)
            return NumericMode.None;
        double speed = velocity.Length, distance = center.Length;
        bool fast = !anchored && speed >= options.CreateSpeed;
        return current switch
        {
            NumericMode.Falling when !anchored && speed >= options.LeaveSpeed => NumericMode.Falling,
            NumericMode.Falling or NumericMode.Resting => fast ? NumericMode.Falling : distance >= options.LeaveDistance ? NumericMode.Resting : NumericMode.None,
            _ => fast ? NumericMode.Falling : distance >= options.CreateDistance ? NumericMode.Resting : NumericMode.None,
        };
    }

    /// <summary>The mode that a body alone, outside a numeric frame, needs: None when it needs no frame.</summary>
    internal static NumericMode BodyMode(in NumericBody body, NumericFrameOptions options) =>
        Mode(NumericMode.None, body.Velocity, body.Position, body.Static, body.Fixed, options);

    /// <summary>
    /// True when the merge test links the body to every group that it may touch, also to a group that falls: every body
    /// but a hull that <see cref="NumericBody.Orbits"/>. A static body, or the hull of a frame with the Static or Spin
    /// driver, rests in its home, so a group that joins it there is as slow there as the group's own motion
    /// (docs/plans/numeric-frames.md, "Anchors", rule 1). A Kepler hull moves fast in its home, the semantic frame's own
    /// system, so a group that falls stays apart from it and keeps a ghost of it.
    /// </summary>
    internal static bool JoinsEveryGroup(in NumericBody body) => !body.Orbits;

    /// <summary>
    /// The merge test: true when the bounding spheres of the two bodies, <paramref name="separation"/> apart, grown by their
    /// relative speed times the lookahead time, overlap or touch. Two bodies of one system, a numeric frame's or the
    /// semantic frame's own, stay linked up to the split margin farther, so that a body at the edge of the reach does not
    /// go in and out at each sync point.
    /// </summary>
    internal static bool MayTouch(in NumericBody a, in NumericBody b, Double3 separation, NumericFrameOptions options)
    {
        double reach = a.Radius + b.Radius + (a.Velocity - b.Velocity).Length * options.LookaheadTime;
        if (a.Home == b.Home)
            reach += options.SplitMargin;
        return separation.LengthSquared <= reach * reach;
    }

    /// <summary>
    /// The radius of the sphere around a frame's origin that holds every body that sticks out of the frame's bubble
    /// <paramref name="bubble"/>, when the largest body of the frame that can move has a bounding sphere of radius
    /// <paramref name="largestRadius"/>. A body sticks out until its center is farther outside the bubble than its radius
    /// plus the exit margin, so its far side reaches at most two radii plus that margin past the bubble's longest radius.
    /// </summary>
    internal static double StickOutRadius(FrameBounds bubble, double largestRadius) =>
        bubble.MaxRadius + 2 * largestRadius + bubble.ExitMargin(largestRadius);

    /// <summary>
    /// How far from a frame's origin <paramref name="body"/>, a body of the frame's own system that can move, reaches while
    /// it sticks out of the frame's bubble <paramref name="bubble"/>. A body that leaves the bubble when it is fully outside
    /// reaches <see cref="StickOutRadius"/> for its radius. A hull of a frame that its driver moves on rails (see
    /// <see cref="NumericBody.Fixed"/>) never leaves, so it sticks out by as far as it lies: it reaches its distance from the
    /// origin plus its radius, and its speed relative to the frame times the lookahead time, for its move until the next
    /// update.
    /// </summary>
    internal static double StickOutReach(FrameBounds bubble, in NumericBody body, NumericFrameOptions options) =>
        body.Fixed
            ? body.Position.Length + body.Radius + body.Velocity.Length * options.LookaheadTime
            : StickOutRadius(bubble, body.Radius);

    /// <summary>
    /// The merge test of a body and the bodies that stick out of the bubble of a frame, which lie in the sphere of
    /// <paramref name="stickOutRadius"/> around the frame's origin (see <see cref="StickOutRadius"/>): true when the body's
    /// bounding sphere, grown by its speed relative to the frame's origin times the lookahead time, overlaps or touches that
    /// sphere. <paramref name="separation"/> is the place of the frame's origin relative to the body, and
    /// <paramref name="frameVelocity"/> the velocity of that origin relative to the body's semantic frame. The frame's
    /// rotation does not move the sphere. The test has no split margin: it moves no body, it only names the numeric frame
    /// of the body to keep the ghosts of the bodies that stick out.
    /// </summary>
    internal static bool MayTouchStickOut(in NumericBody body, Double3 separation, Double3 frameVelocity, double stickOutRadius,
        NumericFrameOptions options)
    {
        double reach = body.Radius + stickOutRadius + (body.Velocity - frameVelocity).Length * options.LookaheadTime;
        return separation.LengthSquared <= reach * reach;
    }

    /// <summary>
    /// How far the separation of two places <paramref name="a"/> and <paramref name="b"/> in a semantic frame's coordinates
    /// can be from the true one, in meters. Far from the frame's origin, each place rounds to a step of a double, about
    /// 2.2e-16 of its distance from the origin, and a sum along the numeric frames rounds a few times more. The slack is
    /// 1e-12 of the two distances, thousands of such steps. So a first test in those coordinates whose reach grows by the
    /// slack drops no pair that the test through the homes links.
    /// </summary>
    internal static double PlaceSlack(Double3 a, Double3 b) => 1e-12 * (a.Length + b.Length);

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
    /// The motion of the group <paramref name="members"/>, which is not empty. A static body counts as infinitely heavy
    /// (docs/plans/numeric-frames.md, "Anchors", rule 2), so a group with one gets the motion of its static bodies alone:
    /// the velocity zero, their center of mass, and as its frame the numeric frame that holds one of them, the first on a
    /// tie, or none when they all lie in the semantic frame's own system. Then <see cref="Mode"/> gives the group the mode
    /// that its static bodies get alone, so the group takes their home. A frame that holds a static body of the group has
    /// the mass +∞, which only comparisons read. Those motions are the limits of the mass-weighted means as the static
    /// bodies' mass grows without bound, and no sum multiplies or divides by the infinite mass, which would give NaN.
    /// <paramref name="homeMasses"/> is scratch: it ends with each numeric home of the group and the mass in it, in the
    /// order of the members.
    /// </summary>
    internal static NumericGroup Summarize(ReadOnlySpan<NumericBody> members, List<(FrameHandle Frame, double Mass)> homeMasses)
    {
        Double3 momentum = Double3.Zero, moment = Double3.Zero, staticMoment = Double3.Zero;
        double movingMass = 0, mass = 0, staticMass = 0;
        bool anchored = false, isFixed = false;
        homeMasses.Clear();
        foreach (ref readonly NumericBody body in members)
        {
            anchored |= body.Static;
            isFixed |= body.Fixed;
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
            if (body.Home == body.Semantic)
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
        FrameHandle frame = FrameHandle.Null;
        double frameMass = 0;
        foreach ((FrameHandle home, double homeMass) in homeMasses)
            if (homeMass > frameMass)
                (frame, frameMass) = (home, homeMass);
        // A frame without a static body is not the home of an anchored group, however much of its mass is there.
        if (anchored && !double.IsPositiveInfinity(frameMass))
            (frame, frameMass) = (FrameHandle.Null, 0);
        Double3 velocity = anchored || movingMass == 0 ? Double3.Zero : momentum / movingMass;
        Double3 center = anchored ? staticMoment / staticMass : moment / mass;
        return new NumericGroup(velocity, center, anchored, isFixed, frame, frameMass);
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
