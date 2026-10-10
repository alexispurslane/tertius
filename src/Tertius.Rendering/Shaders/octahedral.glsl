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

vec3 octDecode(vec2 e)
{
    vec3 n = vec3(e, 1.0 - abs(e.x) - abs(e.y));
    if (n.z < 0.0)
        n.xy = (1.0 - abs(n.yx)) * signNotZero(n.xy);
    return normalize(n);
}

#endif
