// The light of a hair fiber (docs/plans/skin-and-hair.md, "The hair model"): a diffuse part along the strand (Kajiya and
// Kay 1989), normalized so that an even light returns the fiber's color, and two highlights shifted along the strand
// (Scheuermann 2004), each a Gaussian of unit area in the longitudinal angle with Marschner's azimuthal term. HairFiber
// is the C# copy. lighting.glsl includes this file after ShadingInput, SurfaceLobes, DirectTerms and halfVectorOf.
#ifndef TERTIUS_HAIR_GLSL
#define TERTIUS_HAIR_GLSL

// Marschner's shifts of the two highlights, in rad: the primary toward the root, the secondary 1.5 times as far toward
// the tip.
const float PrimaryShift = -0.08726646;     // −5°
const float SecondaryShift = 0.13089969;    // +7.5°
const float InversePiSquared = 0.10132118;

// M: a Gaussian of unit area over θ_r around a lobe's cone, of `width` in θ_h. The width is at least the floored
// roughness squared, 0.045², so the divisions are finite, and exp of the exponent is 0 or a subnormal that a driver may
// flush to 0.
float longitudinalLobe(float thetaH, float shift, float width)
{
    float x = thetaH - shift;
    return exp(-x * x / (2.0 * width * width)) / (2.0 * width * sqrt(2.0 * PI));
}

// The light of one light from L toward V per unit irradiance. s.strand is T and s.normal the fiber normal n_f. The sines
// are clamped, so asin and sqrt see operands in their domain; a direction along the strand has no part across it, and
// takes the fiber normal, which gives a light along the strand a cos θ_i of 0 and so no light.
DirectTerms hairResponse(ShadingInput s, SurfaceLobes lobes, vec3 V, vec3 L)
{
    vec3 T = s.strand;
    float sinI = clamp(dot(T, L), -1.0, 1.0);
    float sinR = clamp(dot(T, V), -1.0, 1.0);
    float cosI = sqrt(1.0 - sinI * sinI);
    float thetaH = 0.5 * (asin(sinI) + asin(sinR));
    vec3 lAcross = normalizedOr(L - T * sinI, s.normal);
    vec3 vAcross = normalizedOr(V - T * sinR, s.normal);
    float azimuth = 0.25 * sqrt(0.5 + 0.5 * clamp(dot(lAcross, vAcross), -1.0, 1.0));
    vec3 primary = schlickFresnel(lobes.f0, dot(V, halfVectorOf(L, V, s.normal)))
        * longitudinalLobe(thetaH, PrimaryShift, lobes.alpha);
    // Light that crossed the fiber twice, tinted by it.
    vec3 secondary = lobes.f0 * lobes.diffuseColor * longitudinalLobe(thetaH, SecondaryShift, 2.0 * lobes.alpha);
    DirectTerms terms;
    terms.diffuse = lobes.diffuseColor * (cosI * InversePiSquared);
    terms.specular = (primary + secondary) * (azimuth * cosI);
    return terms;
}

#endif
