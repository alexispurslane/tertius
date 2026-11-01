using System.Numerics;

namespace Tertius.Rendering;

/// <summary>
/// The painted atmosphere of a frame (docs/plans/atmosphere.md, "The model"): what the shaders read of a snapshot's
/// atmosphere record, at the interpolated eye. The sky is the planet's painted sky times the day factor and the density
/// share at the eye. The haze's light is the painted haze, which the shaders dim by the day factor at each hazed point. The
/// shaders compute the haze's transmittance, the day factor and the sun's tint per point from the planet's shape and the
/// sun's place (Shaders/atmosphere.glsl).
/// </summary>
/// <remarks>
/// IMPORTANT: this class and atmosphere.glsl hold the same three rules: the density share s(p), the sun's tint and the day
/// factor D. The C# copy serves the values at the eye, once per frame, and the impostors' light signature; the GLSL copy
/// serves every shaded point. A change to one rule edits both.
/// </remarks>
internal static class PaintedAtmosphere
{
    /// <summary>
    /// Half the width of the dusk, in rad of the sun's elevation: 6°, the end of civil twilight. The sky is dark below −6°,
    /// half at 0°, and full above +6°.
    /// </summary>
    public const float DuskHalfWidth = 6 * MathF.PI / 180;

    /// <summary>
    /// The largest inverse length, in 1/m, that the shaders read: 2^62. A height or a distance in the shaders is at most
    /// <see cref="ShaderInput.PositionLimit"/>, 2^62 m, so each product stays below 2^124 and finite.
    /// </summary>
    public const float InverseLengthLimit = 4611686018427387904f;

    /// <summary>No atmosphere: a black sky, no haze, no sun and a white tint, so that no light changes.</summary>
    public static GpuAtmosphere None => new() { SunTint = new(Vector3.One, 0) };

    /// <summary>
    /// The atmosphere that the shaders read for <paramref name="snapshot"/>, interpolated by <paramref name="alpha"/>, with
    /// the planet's center and the sun relative to <paramref name="eye"/>, the interpolated eye relative to the render
    /// origin. A snapshot without an atmosphere, or one whose planet lies past the shaders' range of places, gives
    /// <see cref="None"/>. A sun past that range counts as none, so the planet has night. The values are read into the
    /// shaders' ranges: the sky and the haze's light at most <see cref="ShaderInput.MaxRadiance"/> per channel, the tint
    /// from 0 to 1, and each inverse length at most <see cref="InverseLengthLimit"/>.
    /// </summary>
    public static GpuAtmosphere Prepare(RenderSnapshot snapshot, float alpha, Vector3 eye)
    {
        if (snapshot.Atmosphere is not { } air)
            return None;
        Vector3 center = Vector3.Lerp(air.PreviousCenter, air.Center, alpha) - eye;
        if (!ShaderInput.InPositionRange(center, 0))
            return None;
        PlanetMedium planet = air.Medium;
        Vector3? toSun = SunPlace(snapshot: snapshot, sun: air.Sun, alpha: alpha, eye: eye);
        float day = toSun is { } place ? DayFactor(Elevation(up: -center, toSun: place)) : 0;
        double height = planet.HeightAt(center.Length());
        float eyeShare = (float)Math.Exp(-height / planet.ScaleHeight);
        Vector4 haze = planet.Haze is { } painted
            ? new(Capped(painted.Radiance), InverseLength(painted.Distance))
            : Vector4.Zero;
        return new()
        {
            CenterRadius = new(center, (float)Math.Min(planet.Radius, ShaderInput.PositionLimit)),
            Sky = new(Capped(planet.SkyRadiance * (day * eyeShare)), InverseLength(planet.ScaleHeight)),
            Haze = haze,
            SunTint = new(planet.SunTint is { } tint ? ShaderInput.Fraction(tint) : Vector3.One, 0),
            Sun = toSun is { } sunPlace ? new(sunPlace, 1) : Vector4.Zero,
            Eye = new(x: (float)Math.Min(height, ShaderInput.PositionLimit), y: eyeShare, z: 0, w: 0),
        };
    }

    /// <summary>
    /// The factor by which the sun's light at <paramref name="position"/>, camera-relative, takes the tint of
    /// <paramref name="atmosphere"/>: mix(1, tint, s(p)) per channel, from 0 to 1. The C# copy of sunTintAt in
    /// atmosphere.glsl, which the lighting shader applies to the light that <see cref="GpuFrameConstants.Counts"/>.Y names.
    /// </summary>
    public static Vector3 SunTintAt(in GpuAtmosphere atmosphere, Vector3 position)
    {
        var center = new Vector3(atmosphere.CenterRadius.X, atmosphere.CenterRadius.Y, atmosphere.CenterRadius.Z);
        float height = MathF.Max(Vector3.Distance(position, center) - atmosphere.CenterRadius.W, 0);
        float share = MathF.Exp(-height * atmosphere.Sky.W);
        var tint = new Vector3(atmosphere.SunTint.X, atmosphere.SunTint.Y, atmosphere.SunTint.Z);
        return Vector3.Lerp(Vector3.One, tint, share);
    }

    /// <summary>
    /// D(e): how much of its full-day light the sky and the haze send when the sun stands <paramref name="elevation"/> rad
    /// above the horizon, from 0 to 1: smoothstep from −<see cref="DuskHalfWidth"/> to +<see cref="DuskHalfWidth"/>.
    /// </summary>
    public static float DayFactor(float elevation)
    {
        float t = Math.Clamp((elevation + DuskHalfWidth) / (2 * DuskHalfWidth), 0, 1);
        return t * t * (3 - 2 * t);
    }

    /// <summary>
    /// The place of the sun <paramref name="sun"/> of <paramref name="snapshot"/> relative to <paramref name="eye"/>, in m,
    /// or null without a sun: no index, an index past the lights, a light that is not a point light, or a place past the
    /// shaders' range.
    /// </summary>
    private static Vector3? SunPlace(RenderSnapshot snapshot, int? sun, float alpha, Vector3 eye)
    {
        if (sun is not { } index || (uint)index >= (uint)snapshot.Lights.Count)
            return null;
        if (!snapshot.Lights[index].TryGetValue(out PointRenderLight light))
            return null;
        Vector3 toSun = Vector3.Lerp(light.PreviousPosition, light.Position, alpha) - eye;
        return ShaderInput.InPositionRange(toSun, 0) ? toSun : null;
    }

    /// <summary>
    /// The elevation, in rad, of a sun in the direction <paramref name="toSun"/> above the horizon whose up is
    /// <paramref name="up"/>, the direction away from the planet's center. A place at the planet's center has no horizon,
    /// and a sun at the place no direction: both count as overhead, π/2. dayFactorAt in atmosphere.glsl applies the same
    /// rule at a shaded point.
    /// </summary>
    private static float Elevation(Vector3 up, Vector3 toSun)
    {
        float lengths = up.Length() * toSun.Length();
        if (!(lengths > 0) || !float.IsFinite(lengths))
            return MathF.PI / 2;
        return MathF.Asin(Math.Clamp(Vector3.Dot(up, toSun) / lengths, -1, 1));
    }

    /// <summary>A radiance as the shaders read it, at most <see cref="ShaderInput.MaxRadiance"/> per channel.</summary>
    private static Vector3 Capped(Vector3 radiance) =>
        Vector3.Min(ShaderInput.Radiance(radiance), new(ShaderInput.MaxRadiance));

    /// <summary>1 / <paramref name="length"/>, at most <see cref="InverseLengthLimit"/>; the limit for a length that is not above 0.</summary>
    private static float InverseLength(float length) =>
        length > 0 ? MathF.Min(1 / length, InverseLengthLimit) : InverseLengthLimit;
}
