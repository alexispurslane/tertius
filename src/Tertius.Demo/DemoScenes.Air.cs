namespace Tertius.Demo;

public static partial class DemoScenes
{
    /// <summary>
    /// The air of the demo's scenes, which only puffs feel (docs/plans/blue-danube-demo.md, "The plumes"): Earth's air in
    /// earth-surface, which thins with height, and the air of the shuttle's cabin and of the station's bay, at the density
    /// of the air at the surface. Every air has one drag, <see cref="Drag"/>, so a puff of one look slows the same in all
    /// of them.
    /// </summary>
    public static class Air
    {
        /// <summary>The density of the air at Earth's surface, in kg/m³: the standard atmosphere's 1.225 at sea level.</summary>
        public const float SurfaceDensity = 1.225f;

        /// <summary>The height over which Earth's air thins by the factor e, in meters: 8.5 km.</summary>
        public const float ScaleHeight = 8500;

        /// <summary>
        /// The drag of the air on a puff at the air's temperature, per second. A puff's drag is this times its lift share,
        /// so hot exhaust, three times as hot as the air, slows at 6 per second when it is born, and stops within about
        /// 1/6 of its speed in meters.
        /// </summary>
        public const float Drag = 2;

        /// <summary>Earth's air, for earth-surface, whose origin is Earth's center: it thins with height above
        /// <see cref="EarthRadius"/>.</summary>
        public static MediumField Earth => MediumField.Planet(EarthRadius, SurfaceDensity, ScaleHeight, Drag);

        /// <summary>Air at the surface's density that fills <paramref name="region"/> of a frame, in its axes, or the whole frame when it is null.</summary>
        public static MediumField Filling(BoundingBox? region) => MediumField.Uniform(SurfaceDensity, Drag, region);
    }
}
