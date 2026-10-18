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
/// This is a custom C# 15 union of <see cref="InFilter"/>, <see cref="NearAsker"/>, <see cref="NearEntity"/>,
/// <see cref="NearPoint"/> and <see cref="WhereFilter"/>. <see cref="HasValue"/> and the <c>TryGetValue</c> methods let
/// the compiler pattern-match it without boxing. <see cref="Value"/> boxes, so use it only for debugging. The
/// constructors, and so the implicit conversions and the static factories, refuse a case that breaks the invariants that
/// its record documents, so a filter that holds a case always meets them. The default
/// value holds no case: it is the empty filter, which passes every entity. The cases share their fields: the frame of
/// <c>In</c> and of <c>NearPoint</c> is one field, and the radius of the three Near cases is one field. A case reads
/// only its own fields, so the fields of the cases that a filter does not hold may hold anything and never matter.
/// </remarks>
[Union]
internal readonly struct QueryFilter : IEquatable<QueryFilter>
{
    private const byte KindNone = 0, KindIn = 1, KindNearAsker = 2, KindNearEntity = 3, KindNearPoint = 4, KindWhere = 5;

    private readonly byte kind;
    /// <summary>The frame of <see cref="InFilter"/> and of <see cref="NearPoint"/>.</summary>
    private readonly FrameHandle frame;
    /// <summary>The center of <see cref="NearEntity"/>.</summary>
    private readonly Entity centerEntity;
    /// <summary>The center of <see cref="NearPoint"/>, in <see cref="frame"/>.</summary>
    private readonly Double3 point;
    /// <summary>The radius of the three Near cases, in meters.</summary>
    private readonly double radius;
    private readonly EntityPredicate? predicate;

    // Every way to build a filter goes through these five constructors, so they check the invariants that the case
    // records document. An exception names the parameter of the matching factory, because EntityQuery and FoundEntities
    // pass their arguments straight to the factories and their callers know those names.

    public QueryFilter(InFilter filter)
    {
        if (filter.Frame.IsNull)
            throw new ArgumentException("In needs a frame, not the null frame.", "frame");
        kind = KindIn;
        frame = filter.Frame;
    }

    public QueryFilter(NearAsker filter)
    {
        kind = KindNearAsker;
        radius = CheckRadius(filter.Radius);
    }

    public QueryFilter(NearEntity filter)
    {
        if (filter.Other.IsNull)
            throw new ArgumentException("Near needs an entity as its center, not the null handle.", "other");
        kind = KindNearEntity;
        centerEntity = filter.Other;
        radius = CheckRadius(filter.Radius);
    }

    public QueryFilter(NearPoint filter)
    {
        if (filter.Frame.IsNull)
            throw new ArgumentException("Near needs the frame of its point, not the null frame.", "frame");
        Double3 center = filter.Point;
        if (!double.IsFinite(center.X) || !double.IsFinite(center.Y) || !double.IsFinite(center.Z))
            throw new ArgumentException($"Near needs a finite point, not {center}.", "point");
        kind = KindNearPoint;
        frame = filter.Frame;
        point = center;
        radius = CheckRadius(filter.Radius);
    }

    public QueryFilter(WhereFilter filter)
    {
        ArgumentNullException.ThrowIfNull(filter.Predicate, "predicate");
        kind = KindWhere;
        predicate = filter.Predicate;
    }

    public static implicit operator QueryFilter(InFilter filter) => new(filter);
    public static implicit operator QueryFilter(NearAsker filter) => new(filter);
    public static implicit operator QueryFilter(NearEntity filter) => new(filter);
    public static implicit operator QueryFilter(NearPoint filter) => new(filter);
    public static implicit operator QueryFilter(WhereFilter filter) => new(filter);

    /// <summary>False for the empty filter, which passes every entity.</summary>
    public bool HasValue => kind != KindNone;

    public bool TryGetValue(out InFilter value)
    {
        value = kind == KindIn ? new InFilter(frame) : default;
        return kind == KindIn;
    }

    public bool TryGetValue(out NearAsker value)
    {
        value = kind == KindNearAsker ? new NearAsker(radius) : default;
        return kind == KindNearAsker;
    }

    public bool TryGetValue(out NearEntity value)
    {
        value = kind == KindNearEntity ? new NearEntity(centerEntity, radius) : default;
        return kind == KindNearEntity;
    }

    public bool TryGetValue(out NearPoint value)
    {
        value = kind == KindNearPoint ? new NearPoint(frame, point, radius) : default;
        return kind == KindNearPoint;
    }

    public bool TryGetValue(out WhereFilter value)
    {
        value = kind == KindWhere ? new WhereFilter(predicate!) : default;
        return kind == KindWhere;
    }

    public object? Value => kind switch
    {
        KindIn => new InFilter(frame),
        KindNearAsker => new NearAsker(radius),
        KindNearEntity => new NearEntity(centerEntity, radius),
        KindNearPoint => new NearPoint(frame, point, radius),
        KindWhere => new WhereFilter(predicate!),
        _ => null,
    };

    public static QueryFilter In(FrameHandle frame) => new(new InFilter(frame));

    public static QueryFilter Near(double radius) => new(new NearAsker(radius));

    public static QueryFilter Near(Entity other, double radius) => new(new NearEntity(other, radius));

    public static QueryFilter Near(FrameHandle frame, Double3 point, double radius) => new(new NearPoint(frame, point, radius));

    public static QueryFilter Where(EntityPredicate predicate) => new(new WhereFilter(predicate));

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
                return TryCenterOfEntity(snapshot, snapshot.Asker, out frame, out point);
            case NearEntity(var center, _):
                return TryCenterOfEntity(snapshot, center, out frame, out point);
            case NearPoint(var pointFrame, var center, _):
                frame = pointFrame;
                point = center;
                return snapshot.World.Frames.IsAlive(frame);
            default:
                frame = default;
                point = default;
                return false;
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
        NearAsker(var distance) => IsWithinRadiusOfEntity(snapshot, snapshot.Asker, row, distance),
        NearEntity(var center, var distance) => IsWithinRadiusOfEntity(snapshot, center, row, distance),
        NearPoint(var pointFrame, var center, var distance) =>
            snapshot.World.Frames.IsAlive(pointFrame) && IsWithin(row, pointFrame, center, distance),
        null => true,
    };

    /// <summary>True if the row is in the frame of the live entity <paramref name="center"/>, within <paramref name="distance"/> meters of it.</summary>
    private static bool IsWithinRadiusOfEntity(in Snapshot snapshot, Entity center, in TransformRow row, double distance) =>
        TryCenterOfEntity(snapshot, center, out FrameHandle centerFrame, out Double3 centerPoint)
        && IsWithin(row, centerFrame, centerPoint, distance);

    private static bool IsWithin(in TransformRow row, FrameHandle centerFrame, Double3 centerPoint, double distance) =>
        row.Frame == centerFrame && (row.Pose.Position - centerPoint).LengthSquared <= distance * distance;

    private static bool TryCenterOfEntity(in Snapshot snapshot, Entity center, out FrameHandle centerFrame, out Double3 centerPoint)
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
            : throw new ArgumentOutOfRangeException(nameof(radius), radius, "A Near radius must be finite and zero or more.");

    // The held case alone decides equality: the fields of the other cases are not compared.
    public bool Equals(QueryFilter other) => kind == other.kind && kind switch
    {
        KindIn => frame == other.frame,
        KindNearAsker => radius == other.radius,
        KindNearEntity => centerEntity == other.centerEntity && radius == other.radius,
        KindNearPoint => frame == other.frame && point == other.point && radius == other.radius,
        KindWhere => predicate == other.predicate,
        _ => true,
    };

    public override bool Equals(object? obj) => obj is QueryFilter filter && Equals(filter);

    public override int GetHashCode() => kind switch
    {
        KindIn => HashCode.Combine(kind, frame),
        KindNearAsker => HashCode.Combine(kind, radius),
        KindNearEntity => HashCode.Combine(kind, centerEntity, radius),
        KindNearPoint => HashCode.Combine(kind, frame, point, radius),
        KindWhere => HashCode.Combine(kind, predicate),
        _ => 0,
    };

    public static bool operator ==(QueryFilter a, QueryFilter b) => a.Equals(b);
    public static bool operator !=(QueryFilter a, QueryFilter b) => !a.Equals(b);

    public override string ToString() => this switch
    {
        InFilter f => $"QueryFilter({f})",
        NearAsker f => $"QueryFilter({f})",
        NearEntity f => $"QueryFilter({f})",
        NearPoint f => $"QueryFilter({f})",
        WhereFilter f => $"QueryFilter({f})",
        null => "QueryFilter(none)",
    };
}
