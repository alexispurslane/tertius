using Tertius.Rendering;

namespace Tertius.ReflectionReference;

internal readonly record struct ReferenceHit(Double3 Position, Double3 Normal, double Distance);

internal readonly record struct ReferencePrimaryHit(ReferenceSurface Surface, ReferenceHit Hit);

internal readonly record struct ReferenceFace(Double3 Origin, Double3 Across, Double3 Down, Double3 Normal)
{
    public double Area => Double3.Cross(Across, Down).Length;

    public Double3 Position(double across, double down) => Origin + across * Across + down * Down;
}

internal readonly record struct ReferenceEmitter(Double3 Minimum, Double3 Maximum, Double3 Radiance)
{
    public double Area
    {
        get
        {
            Double3 size = Maximum - Minimum;
            return 2 * (size.X * size.Y + size.Y * size.Z + size.X * size.Z);
        }
    }

    public ReferenceHit? Intersect(Double3 origin, Double3 direction)
    {
        double entry = double.NegativeInfinity;
        double exit = double.PositiveInfinity;
        Double3 entryNormal = default;
        Double3 exitNormal = default;
        for (int axis = 0; axis < 3; axis++)
        {
            double position = Component(origin, axis);
            double along = Component(direction, axis);
            double minimum = Component(Minimum, axis);
            double maximum = Component(Maximum, axis);
            if (along == 0)
            {
                if (position < minimum || position > maximum)
                    return null;
                continue;
            }
            double near = (minimum - position) / along;
            double far = (maximum - position) / along;
            Double3 normal = Axis(axis);
            Double3 nearNormal = -normal;
            Double3 farNormal = normal;
            if (near > far)
            {
                (near, far) = (far, near);
                (nearNormal, farNormal) = (farNormal, nearNormal);
            }
            if (near > entry)
            {
                entry = near;
                entryNormal = nearNormal;
            }
            if (far < exit)
            {
                exit = far;
                exitNormal = farNormal;
            }
            if (entry > exit)
                return null;
        }
        if (exit <= 0 || !double.IsFinite(exit))
            return null;
        double distance = entry > 0 ? entry : exit;
        return new(origin + distance * direction, entry > 0 ? entryNormal : exitNormal, distance);
    }

    public (Double3 Position, Double3 Normal) Surface(double face, double across, double down)
    {
        double remaining = face * Area;
        for (int index = 0; index < 6; index++)
        {
            ReferenceFace surface = Face(index);
            if (remaining >= surface.Area && index < 5)
            {
                remaining -= surface.Area;
                continue;
            }
            return (surface.Position(across, down), surface.Normal);
        }
        throw new InvalidOperationException();
    }

    public ReferenceFace Face(int index)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(index);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(index, 5);
        Double3 size = Maximum - Minimum;
        bool upper = index % 2 == 1;
        return (index / 2) switch
        {
            0 => new(
                Origin: new(upper ? Maximum.X : Minimum.X, Minimum.Y, Minimum.Z),
                Across: size.Y * Double3.UnitY,
                Down: size.Z * Double3.UnitZ,
                Normal: (upper ? 1 : -1) * Double3.UnitX
            ),
            1 => new(
                Origin: new(Minimum.X, upper ? Maximum.Y : Minimum.Y, Minimum.Z),
                Across: size.X * Double3.UnitX,
                Down: size.Z * Double3.UnitZ,
                Normal: (upper ? 1 : -1) * Double3.UnitY
            ),
            _ => new(
                Origin: new(Minimum.X, Minimum.Y, upper ? Maximum.Z : Minimum.Z),
                Across: size.X * Double3.UnitX,
                Down: size.Y * Double3.UnitY,
                Normal: (upper ? 1 : -1) * Double3.UnitZ
            ),
        };
    }

    public double DirectionDensity(ReferenceHit hit, Double3 direction)
    {
        double cosine = -Double3.Dot(hit.Normal, direction);
        return cosine > 0 ? hit.Distance * hit.Distance / (Area * cosine) : 0;
    }

    private static double Component(Double3 value, int axis) =>
        axis switch
        {
            0 => value.X,
            1 => value.Y,
            _ => value.Z,
        };

    private static Double3 Axis(int axis) =>
        axis switch
        {
            0 => Double3.UnitX,
            1 => Double3.UnitY,
            _ => Double3.UnitZ,
        };
}

internal readonly record struct ReferenceCamera(Double3 Eye, Double3 Target, double VerticalFov, int Width, int Height)
{
    public ReferencePrimaryHit? PrimaryHit(Double3 ray, ReferenceEmitter emitter)
    {
        if (ray.Y >= 0)
            return null;
        double distance = -Eye.Y / ray.Y;
        if (emitter.Intersect(Eye, ray) is { } blocker && blocker.Distance < distance)
            return new(Surface: ReferenceSurface.Emitter, Hit: blocker);
        return new(Surface: ReferenceSurface.Plate, Hit: new(Eye + distance * ray, Double3.UnitY, distance));
    }

    public Double3 Ray(double pixelX, double pixelY)
    {
        Double3 forward = (Target - Eye).Normalized();
        Double3 referenceUp = Math.Abs(forward.Y) > 0.999 ? -Double3.UnitZ : Double3.UnitY;
        Double3 right = Double3.Cross(forward, referenceUp).Normalized();
        var up = Double3.Cross(right, forward);
        double tangent = Math.Tan(VerticalFov / 2);
        double horizontal = (2 * pixelX - Width) * tangent / Height;
        double vertical = (Height - 2 * pixelY) * tangent / Height;
        return (forward + right * horizontal + up * vertical).Normalized();
    }
}

internal sealed partial class ReferenceIntegral(ReferenceEmitter emitter, MaterialSample material)
{
    public DirectTerms Estimate(Double3 point, Double3 toView, int pairs, uint seed, bool emitterOnly = false)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(pairs);
        ReferenceSample[] samples = ReferenceSample.Sequence(pairs);
        var shift = ReferenceSample.Shift(seed);
        Double3 diffuse = default;
        Double3 specular = default;
        foreach (ReferenceSample sample in samples)
        {
            DirectTerms light = Sample(
                point: point,
                toView: toView,
                sample: sample.Rotated(shift),
                emitterOnly: emitterOnly
            );
            diffuse += light.Diffuse;
            specular += light.Specular;
        }
        return new(diffuse / pairs, specular / pairs);
    }

    public DirectTerms Sample(
        Double3 point,
        Double3 toView,
        ReferenceSample sample,
        bool emitterOnly = false,
        Double3? normal = null
    )
    {
        Double3 surfaceNormal = normal ?? Double3.UnitY;
        Double3 diffuse = default;
        Double3 specular = default;
        if (!emitterOnly)
        {
            Double3 local = material.Lobe.SampleHalfVector(u1: sample.Across, u2: sample.Down);
            Double3 tangent =
                Math.Abs(surfaceNormal.X) < 0.9
                    ? (Double3.UnitX - surfaceNormal.X * surfaceNormal).Normalized()
                    : (Double3.UnitZ - surfaceNormal.Z * surfaceNormal).Normalized();
            Double3 half =
                local.X * tangent + local.Y * Double3.Cross(tangent, surfaceNormal) + local.Z * surfaceNormal;
            double viewHalf = Double3.Dot(toView, half);
            if (viewHalf > 0)
            {
                Double3 direction = 2 * viewHalf * half - toView;
                DirectTerms light = Along(
                    point: point,
                    toView: toView,
                    direction: direction,
                    emitterOnly: false,
                    normal: surfaceNormal
                );
                diffuse += light.Diffuse;
                specular += light.Specular;
            }
        }
        (Double3 position, Double3 emitterNormal) = emitter.Surface(
            face: sample.Face,
            across: sample.Across,
            down: sample.Down
        );
        Double3 toward = (position - point).Normalized();
        if (Double3.Dot(emitterNormal, -toward) > 0)
        {
            DirectTerms light = Along(
                point: point,
                toView: toView,
                direction: toward,
                emitterOnly: emitterOnly,
                normal: surfaceNormal
            );
            diffuse += light.Diffuse;
            specular += light.Specular;
        }
        return new(diffuse, specular);
    }

    private DirectTerms Along(Double3 point, Double3 toView, Double3 direction, bool emitterOnly, Double3 normal)
    {
        if (Double3.Dot(direction, normal) <= 0 || emitter.Intersect(point, direction) is not { } hit)
            return default;
        double density = emitter.DirectionDensity(hit, direction);
        if (!emitterOnly)
        {
            Double3 half = (toView + direction).Normalized();
            double viewHalf = Double3.Dot(toView, half);
            double normalHalf = Double3.Dot(normal, half);
            if (normalHalf > 0 && viewHalf > 0)
                density += material.Lobe.Distribution(normalHalf) * normalHalf / (4 * viewHalf);
        }
        if (density <= 0)
            return default;
        DirectTerms response = material.ReflectedTerms(
            normal: normal,
            toView: toView,
            toLight: direction,
            tables: BrdfTables.Shared,
            lightSine: 0
        );
        return new(response.Diffuse * emitter.Radiance / density, response.Specular * emitter.Radiance / density);
    }
}
