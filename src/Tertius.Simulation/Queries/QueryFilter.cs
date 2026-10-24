using System.Diagnostics;
using System.Runtime.CompilerServices;

namespace Tertius.Simulation;

/// <summary>The case of a <see cref="QueryFilter"/> that keeps the entities in a frame: <c>In(frame)</c>.</summary>
/// <param name="Frame">The frame. It is not the null frame.</param>
internal readonly record struct InFilter(FrameHandle Frame);

/// <summary>The case that keeps the entities near the entity that runs the query: <c>Near(radius)</c>.</summary>
/// <param name="Radius">The distance from the asker, in meters. It is finite and zero or more.</param>
internal readonly record struct NearAsker(double Radius);

/// <summary>The case that keeps the entities near another entity: <c>Near(other, radius)</c>.</summary>
/// <param name="Other">The center. It is not the null handle.</param>
/// <param name="Radius">The distance from the center, in meters. It is finite and zero or more.</param>
internal readonly record struct NearEntity(Entity Other, double Radius);

/// <summary>The case that keeps the entities near a fixed point in a frame: <c>Near(frame, point, radius)</c>.</summary>
/// <param name="Frame">The frame of <paramref name="Point"/>. It is not the null frame.</param>
/// <param name="Point">The center, in the axes of <paramref name="Frame"/>, in meters. Every coordinate is finite.</param>
/// <param name="Radius">The distance from the center, in meters. It is finite and zero or more.</param>
internal readonly record struct NearPoint(FrameHandle Frame, Double3 Point, double Radius);

/// <summary>The case that keeps the entities that a predicate accepts: <c>Where(predicate)</c>.</summary>
/// <param name="Predicate">The test of one entity.</param>
internal readonly record struct WhereFilter(EntityPredicate Predicate);

/// <summary>
/// One place or predicate filter of an entity query: an <c>In</c>, a <c>Near</c> or a <c>Where</c>. An
/// <see cref="EntityQuery"/> holds one of each inline, and the rest in its heap part or, for the shorthand chain, on the
/// stack. Every form tests a filter with <see cref="Passes"/>, so they share one filter code.
/// </summary>
/// <remarks>
/// This is a generated union (docs/plans/union-tooling.md) of <see cref="InFilter"/>, <see cref="NearAsker"/>,
/// <see cref="NearEntity"/>, <see cref="NearPoint"/> and <see cref="WhereFilter"/>. The default value holds no case: it
/// is the empty filter, which passes every entity. The static factories <see cref="In"/>, <c>Near</c> and
/// <see cref="Where"/> refuse a case that breaks the invariants that its record documents. EntityQuery and FoundEntities
/// build every filter through them, so a filter that holds a case always meets the invariants. The generated
/// constructors and implicit conversions store what they get and check nothing.
/// </remarks>
[GeneratedUnion(typeof(InFilter), typeof(NearAsker), typeof(NearEntity), typeof(NearPoint), typeof(WhereFilter))]
internal readonly partial struct QueryFilter
{
    // The factories check the invariants that the case records document. An exception names the parameter of the
    // factory, because EntityQuery and FoundEntities pass their arguments straight to the factories and their callers
    // know those names.

    public static QueryFilter In(FrameHandle frame) =>
        frame.IsNull
            ? throw new ArgumentException("In needs a frame, not the null frame.", nameof(frame))
            : new InFilter(frame);

    public static QueryFilter Near(double radius) => new NearAsker(CheckRadius(radius));

    public static QueryFilter Near(Entity other, double radius) =>
        other.IsNull
            ? throw new ArgumentException("Near needs an entity as its center, not the null handle.", nameof(other))
            : new NearEntity(other, CheckRadius(radius));

    public static QueryFilter Near(FrameHandle frame, Double3 point, double radius)
    {
        if (frame.IsNull)
            throw new ArgumentException("Near needs the frame of its point, not the null frame.", nameof(frame));
        return !double.IsFinite(point.X) || !double.IsFinite(point.Y) || !double.IsFinite(point.Z)
            ? throw new ArgumentException($"Near needs a finite point, not {point}.", nameof(point))
            : (QueryFilter)new NearPoint(frame, point, CheckRadius(radius));
    }

    public static QueryFilter Where(EntityPredicate predicate)
    {
        ArgumentNullException.ThrowIfNull(predicate);
        return new WhereFilter(predicate);
    }

    /// <summary>
    /// The frame and position of a Near filter's center in the snapshot. False when the center is not in a frame: the
    /// asker or the other entity is not live or owns a root frame, or the fixed point's frame is gone. Such a filter
    /// matches nothing. Also false for a filter that is not a Near filter, which has no center.
    /// </summary>
    public bool TryCenter(in Snapshot snapshot, out FrameHandle frame, out Double3 point)
    {
        switch (this)
        {
            case NearAsker:
                return TryCenterOfEntity(snapshot: snapshot, center: snapshot.Asker, centerFrame: out frame, centerPoint: out point);
            case NearEntity(var center, _):
                return TryCenterOfEntity(snapshot: snapshot, center: center, centerFrame: out frame, centerPoint: out point);
            case NearPoint(var pointFrame, var center, _):
                frame = pointFrame;
                point = center;
                return snapshot.World.Frames.IsAlive(frame);
            case InFilter or WhereFilter or null:
                frame = default;
                point = default;
                return false;
            default:
                throw new UnreachableException($"The query filter {this} has a case that has no center here.");
        }
    }

    /// <summary>
    /// True if <paramref name="entity"/>, whose front transform row is <paramref name="row"/>, passes this filter. The
    /// empty filter passes every entity, and that case is decided here, in the inlined part.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public bool Passes(in Snapshot snapshot, Entity entity, in TransformRow row) =>
        !HasValue || PassesHeldCase(snapshot, entity, row);

    // No discard arm: the compiler checks that every case has an arm, so a new case makes this a warning, not a silent pass.
    // Passes decides the empty filter before it calls, so the null arm only runs for a direct call.
    private bool PassesHeldCase(in Snapshot snapshot, Entity entity, in TransformRow row) => this switch
    {
        InFilter(var inFrame) => row.Frame == inFrame,
        WhereFilter(var test) => test(snapshot, entity),
        NearAsker(var distance) => IsWithinRadiusOfEntity(snapshot: snapshot, center: snapshot.Asker, row: row, distance: distance),
        NearEntity(var center, var distance) => IsWithinRadiusOfEntity(snapshot: snapshot, center: center, row: row, distance: distance),
        NearPoint(var pointFrame, var center, var distance) =>
            snapshot.World.Frames.IsAlive(pointFrame) && IsWithin(row: row, centerFrame: pointFrame, centerPoint: center, distance: distance),
        null => true
    };

    /// <summary>True if the row is in the frame of the live entity <paramref name="center"/>, within
    /// <paramref name="distance"/> meters of it.</summary>
    private static bool IsWithinRadiusOfEntity(in Snapshot snapshot, Entity center, in TransformRow row,
        double distance) =>
        TryCenterOfEntity(snapshot: snapshot, center: center, centerFrame: out FrameHandle centerFrame, centerPoint: out Double3 centerPoint)
        && IsWithin(row: row, centerFrame: centerFrame, centerPoint: centerPoint, distance: distance);

    private static bool IsWithin(in TransformRow row, FrameHandle centerFrame, Double3 centerPoint, double distance) =>
        row.Frame == centerFrame && (row.Pose.Position - centerPoint).LengthSquared <= distance * distance;

    private static bool TryCenterOfEntity(in Snapshot snapshot, Entity center, out FrameHandle centerFrame,
        out Double3 centerPoint)
    {
        EntityTable entities = snapshot.World.Entities;
        if (!entities.IsAlive(center))
        {
            centerFrame = default;
            centerPoint = default;
            return false;
        }

        ref readonly TransformRow row = ref entities.FrontRow(center);
        centerFrame = row.Frame;
        centerPoint = row.Pose.Position;
        return !centerFrame.IsNull;
    }

    private static double CheckRadius(double radius) =>
        radius >= 0 && double.IsFinite(radius)
            ? radius
            : throw new ArgumentOutOfRangeException(nameof(radius), radius,
                "A Near radius must be finite and zero or more.");
}
