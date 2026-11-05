// Octahedral unit-vector encoding (Cigolle et al. 2014). The G-buffer stores world-space normals this way in an
// RG16_SNORM target.
#ifndef TERTIUS_OCTAHEDRAL_GLSL
#define TERTIUS_OCTAHEDRAL_GLSL

vec2 signNotZero(vec2 v)
{
    return vec2(v.x >= 0.0 ? 1.0 : -1.0, v.y >= 0.0 ? 1.0 : -1.0);
}

vec2 octEncode(vec3 n)
{
    n /= abs(n.x) + abs(n.y) + abs(n.z);
    return n.z >= 0.0 ? n.xy : (1.0 - abs(n.yx)) * signNotZero(n.xy);
}

vec3 octDecodeUnnormalized(vec2 e)
{
    vec3 n = vec3(e, 1.0 - abs(e.x) - abs(e.y));
    if (n.z < 0.0)
        n.xy = (1.0 - abs(n.yx)) * signNotZero(n.xy);
    return n;
}

vec3 octDecode(vec2 encoded)
{
    return normalize(octDecodeUnnormalized(encoded));
}

// The strand's octahedral code in the shading target's R and G (docs/plans/skin-and-hair.md, "The shading target").
// StrandCode is the C# twin. floor(x + 0.5) rounds, because GLSL's round leaves its ties to the driver. A unit strand's
// code lies in [−1, 1]², so the clamp only bounds rounding, on finite values.
uvec2 strandCode(vec3 strand)
{
    return uvec2(clamp(floor((octEncode(strand) * 0.5 + 0.5) * 255.0 + 0.5), 0.0, 255.0));
}

vec3 strandOfCode(uvec2 code)
{
    return octDecode(vec2(code) / 255.0 * 2.0 - 1.0);
}

#endif
