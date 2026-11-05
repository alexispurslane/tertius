namespace Tertius.Rendering;

/// <summary>A material at one point, after its maps, as the BRDF reads it.</summary>
/// <remarks>The terms and their sources are in docs/plans/pbr-brdf.md, "The BRDF".</remarks>
internal readonly record struct MaterialSample(Double3 BaseColor, double Metallic, double Roughness, double Reflectance)
{
    // A normal map can turn the shading normal away from the eye, and the visibility term divides by μ.
    private const double MinViewCosine = 1e-4;

    private static Double3 One => new(1, 1, 1);

    /// <summary>The Lambert color, b (1 − m): a metal has none.</summary>
    public Double3 DiffuseColor => BaseColor * (1 - Metallic);

    /// <summary>The share of light reflected head on, per channel: 0.16 ρ² for a non-metal, b for a metal.</summary>
    public Double3 F0 => One * ((1 - Metallic) * 0.16 * Reflectance * Reflectance) + BaseColor * Metallic;

    /// <summary>The GGX lobe of the material's roughness, after the floor.</summary>
    public GgxLobe Lobe => new(Roughness);

    /// <summary>Schlick's Fresnel term at the cosine of the view direction to the half vector.</summary>
    public Double3 Fresnel(double vDotH) => F0 + (One - F0) * Math.Pow(1 - vDotH, 5);

    /// <summary>f_ss, the BRDF with single scattering, per steradian, for unit directions above the horizon.</summary>
    public Double3 SingleScatteringBrdf(Double3 normal, Double3 toView, Double3 toLight) =>
        DiffuseColor / Math.PI + SingleScatteringLobe(normal: normal, toView: toView, toLight: toLight);

    /// <summary>
    /// f · max(n·l, 0): the share of the irradiance from <paramref name="toLight"/> that the point sends toward
    /// <paramref name="toView"/>, per steradian, with the energy compensation.
    /// </summary>
    /// <param name="lightSine">sin ϕ of the light's angular radius at the point, R / d: 0 for a point light.</param>
    public Double3 Reflected(Double3 normal, Double3 toView, Double3 toLight, BrdfTables tables, double lightSine)
    {
        DirectTerms terms = ReflectedTerms(
            normal: normal,
            toView: toView,
            toLight: toLight,
            tables: tables,
            lightSine: lightSine
        );
        return terms.Diffuse + terms.Specular;
    }

    /// <summary>
    /// <see cref="Reflected"/> in its two parts, the C# copy of surfaceResponse: c_diff / π · (n·l) toward the light's
    /// center, and the compensated lobe times n·l toward the representative point of a sphere light, normalized
    /// (docs/plans/pbr-brdf.md, "Sized lights"). A light of radius 0 is a point light.
    /// </summary>
    /// <param name="lightSine">sin ϕ of the light's angular radius at the point, R / d: 0 for a point light.</param>
    public DirectTerms ReflectedTerms(
        Double3 normal,
        Double3 toView,
        Double3 toLight,
        BrdfTables tables,
        double lightSine
    )
    {
        double nDotL = Double3.Dot(normal, toLight);
        if (nDotL <= 0)
            return default;
        double lobeAlbedo = tables.SampleDfg(nDotV: ViewCosine(normal, toView), roughness: Lobe.Roughness).LobeAlbedo;
        return new(
            Diffuse: DiffuseColor / Math.PI * nDotL,
            Specular: SpecularResponse(
                normal: normal,
                toView: toView,
                toLight: toLight,
                lightSine: lightSine,
                compensation: EnergyCompensation(lobeAlbedo)
            )
        );
    }

    /// <summary>
    /// The C# copy of hemisphereAmbient: the diffuse color by the normal's sky share, and the compensated specular albedo
    /// by the lobe's sky share.
    /// </summary>
    public AmbientTerms HemisphereTerms(
        Double3 normal,
        Double3 toView,
        Double3 sky,
        Double3 ground,
        double normalSkyShare,
        double lobeSkyShare,
        BrdfTables tables
    ) =>
        new(
            Diffuse: DiffuseColor * Double3.Lerp(ground, sky, normalSkyShare),
            Specular: CompensatedSpecularAlbedo(normal: normal, toView: toView, tables: tables)
                * Double3.Lerp(ground, sky, lobeSkyShare)
        );

    /// <summary>
    /// The C# copy of ambientLight: the light that the hemisphere ambient light sends toward <paramref name="toView"/>,
    /// as the sum of its diffuse and specular terms.
    /// </summary>
    public Double3 ReflectedHemisphere(
        Double3 normal,
        Double3 toView,
        Double3 sky,
        Double3 ground,
        double normalSkyShare,
        double lobeSkyShare,
        BrdfTables tables
    )
    {
        AmbientTerms terms = HemisphereTerms(
            normal: normal,
            toView: toView,
            sky: sky,
            ground: ground,
            normalSkyShare: normalSkyShare,
            lobeSkyShare: lobeSkyShare,
            tables: tables
        );
        return terms.Diffuse + terms.Specular;
    }

    /// <summary>
    /// E · c, the compensated specular albedo per channel: the share of a uniform light that the specular part sends
    /// toward <paramref name="toView"/>. It weights the ambient specular term, and a pane lets through what it leaves.
    /// </summary>
    public Double3 CompensatedSpecularAlbedo(Double3 normal, Double3 toView, BrdfTables tables)
    {
        (double schlickPart, double lobeAlbedo) = tables.SampleDfg(
            nDotV: ViewCosine(normal, toView),
            roughness: Lobe.Roughness
        );
        return (One * schlickPart + F0 * (lobeAlbedo - schlickPart)) * EnergyCompensation(lobeAlbedo);
    }

    /// <summary>
    /// T = (1 − opacity)(1 − E · c), per channel and clamped to [0, 1]: the share of the light behind a pane of this
    /// material that passes through it toward <paramref name="toView"/> (ARCHITECTURE.md, "Transparent objects").
    /// </summary>
    /// <param name="opacity">The base color's alpha, clamped to [0, 1] as the transparent pass clamps it.</param>
    public Double3 Transmittance(double opacity, Double3 normal, Double3 toView, BrdfTables tables)
    {
        Double3 passed =
            (1 - Math.Clamp(opacity, 0, 1))
            * (One - CompensatedSpecularAlbedo(normal: normal, toView: toView, tables: tables));
        return new(Math.Clamp(passed.X, 0, 1), Math.Clamp(passed.Y, 0, 1), Math.Clamp(passed.Z, 0, 1));
    }

    /// <summary>μ, the cosine of the view direction to the normal, with its floor.</summary>
    private static double ViewCosine(Double3 normal, Double3 toView) =>
        Math.Clamp(Double3.Dot(normal, toView), MinViewCosine, 1);

    /// <summary>
    /// The C# copy of specularResponse: D · Vis · F · c · (n·l) toward the representative point l of a sphere light, with
    /// D normalized, or toward <paramref name="toLight"/> for a light of radius 0.
    /// </summary>
    private Double3 SpecularResponse(
        Double3 normal,
        Double3 toView,
        Double3 toLight,
        double lightSine,
        Double3 compensation
    )
    {
        bool sized = lightSine > 0;
        Double3 toPoint = sized
            ? GgxLobe.SphereLightDirection(
                toLight: toLight,
                mirror: 2 * Double3.Dot(normal, toView) * normal - toView,
                lightSine: lightSine
            )
            : toLight;
        double nDotPoint = Double3.Dot(normal, toPoint);
        if (nDotPoint <= 0)
            return Double3.Zero;
        Double3 lobe = SingleScatteringLobe(normal: normal, toView: toView, toLight: toPoint);
        if (sized)
            lobe *= Lobe.SphereNormalization(
                lightSine: lightSine,
                vDotH: Double3.Dot(toView, (toView + toPoint).Normalized())
            );
        return lobe * compensation * nDotPoint;
    }

    /// <summary>D · Vis · F, the GGX lobe with single scattering, per steradian.</summary>
    private Double3 SingleScatteringLobe(Double3 normal, Double3 toView, Double3 toLight)
    {
        Double3 halfVector = (toView + toLight).Normalized();
        GgxLobe lobe = Lobe;
        double distribution = lobe.Distribution(Double3.Dot(normal, halfVector));
        double visibility = lobe.Visibility(nDotL: Double3.Dot(normal, toLight), nDotV: ViewCosine(normal, toView));
        return Fresnel(Double3.Dot(toView, halfVector)) * (distribution * visibility);
    }

    /// <summary>
    /// c = 1 + F0 (1 / E₁ − 1), per channel, from the lobe albedo E₁: gives back the light that single scattering
    /// loses.
    /// </summary>
    private Double3 EnergyCompensation(double lobeAlbedo) => One + F0 * (1 / lobeAlbedo - 1);
}
