namespace Tertius.Demo;

/// <summary>
/// The floor of a station's wheel as the coda reads it (docs/plans/blue-danube-demo.md, "The coda"): the inner face of a
/// ring about the station's +Y, <paramref name="Radius"/> from the axis at its joints, made of <paramref name="Segments"/>
/// flat facets, as <see cref="RevolvedSolid.Ring"/> builds it. Every place is in the station's axes. The angle θ of a
/// place runs about +Y from +X toward -Z, as RevolvedSolid measures it, and facet k spans θ from k to k + 1 times
/// <see cref="Facet"/>. The station turns the way of growing θ, so spinward is the way of growing θ.
/// </summary>
/// <param name="Radius">The ring's radius at the joints of its facets, in meters.</param>
/// <param name="Segments">The number of facets, at least 3.</param>
public readonly record struct WheelFloor(double Radius, int Segments)
{
    /// <summary>The angle that one facet spans, in radians.</summary>
    public double Facet => Math.Tau / Segments;

    /// <summary>How far the middle of a facet lies from the axis, in meters: the facets lie inside the ring's circle.</summary>
    public double FacetDistance => Radius * Math.Cos(Facet / 2);

    /// <summary>The angle θ of <paramref name="place"/> about +Y, from +X toward -Z, from 0 to 2π.</summary>
    public static double Theta(Double3 place)
    {
        double theta = Math.Atan2(-place.Z, place.X);
        return theta < 0 ? theta + Math.Tau : theta;
    }

    /// <summary>The index of the facet whose span of θ holds <paramref name="place"/>, from 0 to <see cref="Segments"/> - 1.</summary>
    public int FacetIndex(Double3 place) => Math.Min(Segments - 1, (int)Math.Floor(Theta(place) / Facet));

    /// <summary>The outward unit normal of facet <paramref name="facet"/>: from the axis through the facet's middle.</summary>
    public Double3 Normal(int facet)
    {
        double middle = (facet + 0.5) * Facet;
        return new(Math.Cos(middle), 0, -Math.Sin(middle));
    }

    /// <summary>The outward unit normal of the facet under <paramref name="place"/>.</summary>
    public Double3 Normal(Double3 place) => Normal(FacetIndex(place));

    /// <summary>How high <paramref name="place"/> lies above the plane of the facet under it, along the facet's normal, in meters.</summary>
    public double Height(Double3 place) => FacetDistance - Double3.Dot(place, Normal(place));

    /// <summary>
    /// The place <paramref name="height"/> above facet <paramref name="facet"/>, <paramref name="along"/> meters spinward of
    /// its middle along its plane, at <paramref name="y"/> along the axis.
    /// </summary>
    public Double3 OnFacet(int facet, double height, double y, double along = 0)
    {
        Double3 normal = Normal(facet);
        Double3 spinward = new(normal.Z, 0, -normal.X);
        return normal * (FacetDistance - height) + spinward * along + new Double3(0, y, 0);
    }

    /// <summary>
    /// The place <paramref name="height"/> above the floor at the angle <paramref name="theta"/>, at <paramref name="y"/>
    /// along
    /// the axis: on the plane of the facet under that angle, in its direction from the axis, and then along the facet's
    /// normal toward the axis.
    /// </summary>
    public Double3 Above(double theta, double height, double y)
    {
        var direction = new Double3(Math.Cos(theta), 0, -Math.Sin(theta));
        Double3 normal = Normal(direction);
        return direction * (FacetDistance / Double3.Dot(direction, normal)) - normal * height + new Double3(0, y, 0);
    }

    /// <summary>The turn about +Y that lays a box flat on facet <paramref name="facet"/>: its +X along the facet's outward normal.</summary>
    public DQuaternion FacetTurn(int facet) => DQuaternion.FromAxisAngle(Double3.UnitY, (facet + 0.5) * Facet);

    /// <summary>
    /// How far spinward <paramref name="to"/> lies from <paramref name="from"/> along the floor, in meters: the angle between
    /// them about the axis, the short way round, times <see cref="Radius"/>. It is negative for a place antispinward.
    /// </summary>
    public double Spinward(Double3 from, Double3 to)
    {
        double turn = Theta(to) - Theta(from);
        turn -= Math.Tau * Math.Round(turn / Math.Tau);
        return Radius * turn;
    }

    /// <summary>The unit direction of growing θ at <paramref name="place"/>, across the axis: the station's way there.</summary>
    public static Double3 SpinwardAt(Double3 place) =>
        Double3.Cross(Double3.UnitY, new(place.X, 0, place.Z)).Normalized();

    /// <summary>The unit direction toward the axis at <paramref name="place"/>, across it: up on the floor.</summary>
    public static Double3 UpAt(Double3 place) => -new Double3(place.X, 0, place.Z).Normalized();
}
