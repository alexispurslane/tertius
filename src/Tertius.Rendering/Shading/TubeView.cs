namespace Tertius.Rendering;

/// <summary>
/// A tube light's capsule seen from a shaded point, per unit of its face radiance (ARCHITECTURE.md, "Tube lights"): the
/// C# copy of tubeView in lighting.glsl. The side counts as a line whose elements show the cylinder's width 2R sin ψ, and
/// the two ends as two half spheres at the segment's ends.
/// </summary>
internal readonly record struct TubeView
{
    /// <summary>
    /// The capsule around the segment from <paramref name="toCenter"/> − <paramref name="halfSegment"/> to
    /// <paramref name="toCenter"/> + <paramref name="halfSegment"/>, relative to the shaded point, in m.
    /// </summary>
    /// <param name="radius">The capsule's radius in m, floored at LightUnits.NearestDistance as the shader floors it.</param>
    public TubeView(Double3 toCenter, Double3 halfSegment, double radius)
    {
        ToCenter = toCenter;
        HalfSegment = halfSegment;
        Radius = Math.Max(radius, LightUnits.NearestDistance);

        double halfLength = halfSegment.Length;
        Double3 axis = halfLength > 0 ? halfSegment / halfLength : Double3.UnitY;
        double along = Double3.Dot(toCenter, axis);
        Double3 across = toCenter - along * axis;
        double distance = across.Length;

        // A point beside the segment and inside the capsule counts as on its surface, where the line's light is the side's
        // πL. Beyond an end the floor only keeps the angle's division finite, so the side's light falls to 0 on the axis.
        bool beside = (along - halfLength) * (along + halfLength) <= 0;
        double floored = Math.Max(distance, beside ? Radius : 1e-20);
        Double3 inward = distance > 0 ? across / distance : Double3.Zero;
        double near = along - halfLength,
            far = along + halfLength;
        double nearSquared = floored * floored + near * near,
            farSquared = floored * floored + far * far;

        // The angle between the directions to the two ends, without the cancellation of two arctangents.
        double angle = Math.Atan2(floored * (far - near), floored * floored + near * far);
        double nearDistance = Math.Sqrt(nearSquared),
            farDistance = Math.Sqrt(farSquared);
        double sideSolidAngle = beside
            ? 2 * Radius / floored * (far / farDistance - near / nearDistance)
            // Beyond an end the sines of the two ends lie close: their difference without the cancellation.
            : 2
                * Radius
                * floored
                * (far * far - near * near)
                / ((far * nearDistance + near * farDistance) * nearDistance * farDistance);
        Double3 sideVector =
            Radius
            * (
                inward * (angle / floored + far / farSquared - near / nearSquared)
                - axis * (floored * (1 / farSquared - 1 / nearSquared))
            );

        double halfSphere = Math.PI * Radius * Radius / 2;
        (double nearEndSolidAngle, Double3 nearEndVector) = HalfSphere(toCenter - halfSegment, halfSphere, Radius);
        (double farEndSolidAngle, Double3 farEndVector) = HalfSphere(toCenter + halfSegment, halfSphere, Radius);
        SolidAngle = sideSolidAngle + nearEndSolidAngle + farEndSolidAngle;
        CosineVector = sideVector + nearEndVector + farEndVector;
    }

    /// <summary>From the shaded point to the segment's middle, in m.</summary>
    public Double3 ToCenter { get; }

    /// <summary>From the segment's middle to one of its ends, in m.</summary>
    public Double3 HalfSegment { get; }

    /// <summary>The capsule's radius, in m, at least LightUnits.NearestDistance.</summary>
    public double Radius { get; }

    /// <summary>
    /// Ω, the solid angle that the capsule fills seen from the point, in sr: its scalar irradiance over its face radiance.
    /// The specular part takes it.
    /// </summary>
    public double SolidAngle { get; }

    /// <summary>
    /// The vector irradiance over the face radiance, in sr: n · it is the irradiance on a surface of normal n, over the face
    /// radiance, where the whole capsule lies above the surface. The diffuse part takes it.
    /// </summary>
    public Double3 CosineVector { get; }

    /// <summary>
    /// Karis's representative point of the capsule (Karis 2013, p. 17): the point of the segment nearest the
    /// <paramref name="mirror"/> ray, then the point of the sphere around it nearest the ray. Its unit direction, and the
    /// sines of the widened lobe's two half-widths (<see cref="GgxLobe.TubeNormalization"/>). tubeLightDirection in
    /// lighting.glsl is the GPU copy.
    /// </summary>
    public (Double3 Direction, double CrossSine, double AlongSine) Representative(Double3 mirror)
    {
        Double3 start = ToCenter - HalfSegment;
        Double3 segment = 2 * HalfSegment;
        double mirrorAlong = Double3.Dot(mirror, segment);
        double denominator = segment.LengthSquared - mirrorAlong * mirrorAlong;

        // A ray along the segment, or a segment of length 0, is as near to every point: the middle stands for them.
        double share =
            denominator > 1e-6 * segment.LengthSquared
                ? Math.Clamp(
                    (Double3.Dot(mirror, start) * mirrorAlong - Double3.Dot(start, segment)) / denominator,
                    0,
                    1
                )
                : 0.5;

        Double3 closest = start + share * segment;
        double distance = closest.Length;
        Double3 toClosest = closest.NormalizedOr(ToCenter.NormalizedOr(mirror));
        double crossSine = Math.Min(Radius / Math.Max(distance, 1e-6), 1);
        // An ellipse of the half-widths CrossSine and AlongSine covers the capsule's solid angle.
        double alongSine = Math.Min(SolidAngle / (Math.PI * crossSine), 1);
        return (GgxLobe.SphereLightDirection(toClosest, mirror, crossSine), crossSine, alongSine);
    }

    /// <summary>
    /// The solid angle, in sr, and the vector irradiance over the face radiance of a half sphere of
    /// <paramref name="area"/> m² seen across, at <paramref name="toEnd"/>, floored at <paramref name="radius"/>.
    /// </summary>
    private static (double SolidAngle, Double3 Vector) HalfSphere(Double3 toEnd, double area, double radius)
    {
        double share = area / Math.Max(toEnd.LengthSquared, radius * radius);
        return (SolidAngle: share, Vector: toEnd.NormalizedOr(Double3.Zero) * share);
    }
}
