using System.Runtime.CompilerServices;

namespace Tertius.Simulation;

/// <summary>The kinds of <see cref="QueryFilter"/>. <see cref="None"/> is the default value, which passes every entity.</summary>
internal enum QueryFilterKind : byte
{
    None,
    /// <summary>In a frame: <c>In(frame)</c>.</summary>
    In,
    /// <summary>Near the entity that runs the query: <c>Near(radius)</c>.</summary>
    NearAsker,
    /// <summary>Near another entity: <c>Near(other, radius)</c>.</summary>
    NearEntity,
    /// <summary>Near a fixed point in a frame: <c>Near(frame, point, radius)</c>.</summary>
    NearPoint,
    /// <summary>A predicate: <c>Where(predicate)</c>.</summary>
    Where,
}

/// <summary>
/// One place or predicate filter of an entity query: an <c>In</c>, a <c>Near</c> or a <c>Where</c>. An
/// <see cref="EntityQuery"/> holds one of each inline, and the rest in its heap part or, for the shorthand chain, on the
/// stack. Every form tests a filter with <see cref="Passes"/>, so they share one filter code.
/// </summary>
internal readonly record struct QueryFilter
{
    public QueryFilterKind Kind { get; private init; }
    /// <summary>The frame of <see cref="QueryFilterKind.In"/> and of <see cref="QueryFilterKind.NearPoint"/>.</summary>
    public FrameHandle Frame { get; private init; }
    /// <summary>The center of <see cref="QueryFilterKind.NearEntity"/>.</summary>
    public Entity Other { get; private init; }
    /// <summary>The center of <see cref="QueryFilterKind.NearPoint"/>, in <see cref="Frame"/>.</summary>
    public Double3 Point { get; private init; }
    /// <summary>The radius of a Near filter, in meters.</summary>
    public double Radius { get; private init; }
    public EntityPredicate? Predicate { get; private init; }

    public bool IsNear => Kind is QueryFilterKind.NearAsker or QueryFilterKind.NearEntity or QueryFilterKind.NearPoint;

    public static QueryFilter In(FrameHandle frame)
    {
        if (frame.IsNull)
            throw new ArgumentException("In needs a frame, not the null frame.", nameof(frame));
        return new QueryFilter { Kind = QueryFilterKind.In, Frame = frame };
    }

    public static QueryFilter Near(double radius) => new() { Kind = QueryFilterKind.NearAsker, Radius = CheckRadius(radius) };

    public static QueryFilter Near(Entity other, double radius)
    {
        if (other.IsNull)
            throw new ArgumentException("Near needs an entity as its center, not the null handle.", nameof(other));
        return new QueryFilter { Kind = QueryFilterKind.NearEntity, Other = other, Radius = CheckRadius(radius) };
    }

    public static QueryFilter Near(FrameHandle frame, Double3 point, double radius)
    {
        if (frame.IsNull)
            throw new ArgumentException("Near needs the frame of its point, not the null frame.", nameof(frame));
        if (!double.IsFinite(point.X) || !double.IsFinite(point.Y) || !double.IsFinite(point.Z))
            throw new ArgumentException($"Near needs a finite point, not {point}.", nameof(point));
        return new QueryFilter { Kind = QueryFilterKind.NearPoint, Frame = frame, Point = point, Radius = CheckRadius(radius) };
    }

    public static QueryFilter Where(EntityPredicate predicate)
    {
        ArgumentNullException.ThrowIfNull(predicate);
        return new QueryFilter { Kind = QueryFilterKind.Where, Predicate = predicate };
    }

    /// <summary>
    /// The frame and position of a Near filter's center in the snapshot. False when the center is not in a frame: the
    /// asker or the other entity is not live or owns a root frame, or the fixed point's frame is gone. Such a filter
    /// matches nothing.
    /// </summary>
    public bool TryCenter(in Snapshot snapshot, out FrameHandle frame, out Double3 point)
    {
        if (Kind == QueryFilterKind.NearPoint)
        {
            frame = Frame;
            point = Point;
            return snapshot.World.Frames.IsAlive(frame);
        }
        Entity center = Kind == QueryFilterKind.NearAsker ? snapshot.Asker : Other;
        EntityTable entities = snapshot.World.Entities;
        if (!entities.IsAlive(center))
        {
            frame = default;
            point = default;
            return false;
        }
        ref readonly TransformRow row = ref entities.FrontRow(center);
        frame = row.Frame;
        point = row.Pose.Position;
        return !frame.IsNull;
    }

    /// <summary>True if <paramref name="entity"/>, whose front transform row is <paramref name="row"/>, passes this filter.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public bool Passes(in Snapshot snapshot, Entity entity, in TransformRow row)
    {
        switch (Kind)
        {
            case QueryFilterKind.None:
                return true;
            case QueryFilterKind.In:
                return row.Frame == Frame;
            case QueryFilterKind.Where:
                return Predicate!(snapshot, entity);
            default:
                return TryCenter(snapshot, out FrameHandle frame, out Double3 point)
                    && row.Frame == frame
                    && (row.Pose.Position - point).LengthSquared <= Radius * Radius;
        }
    }

    private static double CheckRadius(double radius) =>
        radius >= 0 && double.IsFinite(radius)
            ? radius
            : throw new ArgumentOutOfRangeException(nameof(radius), radius, "A Near radius must be finite and zero or more.");
}
