// The six faces of a cube map, after Vulkan's table of cube map face selection (docs/plans/environment-map.md, "The
// faces and their axes"). CubeFaces.cs is the C# copy. A face's right is the way the texel column s grows, and its up the
// way the row t falls, because row 0 is the top of the image.
#ifndef TERTIUS_CUBE_FACES_GLSL
#define TERTIUS_CUBE_FACES_GLSL

struct FaceBasis
{
    vec3 forward;
    vec3 right;
    vec3 up;
};

// The axes of face `face`, a layer from 0 to 5 in the order +X, −X, +Y, −Y, +Z, −Z.
FaceBasis faceBasis(uint face)
{
    switch (face)
    {
        case 0u: return FaceBasis(vec3(1.0, 0.0, 0.0), vec3(0.0, 0.0, -1.0), vec3(0.0, 1.0, 0.0));
        case 1u: return FaceBasis(vec3(-1.0, 0.0, 0.0), vec3(0.0, 0.0, 1.0), vec3(0.0, 1.0, 0.0));
        case 2u: return FaceBasis(vec3(0.0, 1.0, 0.0), vec3(1.0, 0.0, 0.0), vec3(0.0, 0.0, -1.0));
        case 3u: return FaceBasis(vec3(0.0, -1.0, 0.0), vec3(1.0, 0.0, 0.0), vec3(0.0, 0.0, 1.0));
        case 4u: return FaceBasis(vec3(0.0, 0.0, 1.0), vec3(1.0, 0.0, 0.0), vec3(0.0, 1.0, 0.0));
        default: return FaceBasis(vec3(0.0, 0.0, -1.0), vec3(-1.0, 0.0, 0.0), vec3(0.0, 1.0, 0.0));
    }
}

// The direction through the point st of a face, st in [0, 1], before normalizing. At a texel center of a face of a
// power-of-two size, each component is exact, so a cube sample along it reads that texel alone.
vec3 faceRay(uint face, vec2 st)
{
    FaceBasis basis = faceBasis(face);
    return basis.forward + (2.0 * st.x - 1.0) * basis.right + (1.0 - 2.0 * st.y) * basis.up;
}

// The unit direction through the point st of a face, st in [0, 1], row 0 at the top.
vec3 faceDirection(uint face, vec2 st)
{
    return normalize(faceRay(face, st));
}

// The solid angle, in sr, of the spherical triangle that the points a, b and c of a face's plane, at distance 1 along its
// forward axis, span from the center of the cube (Van Oosterom and Strackee 1983):
// tan(Ω / 2) = |A · (B × C)| / (|A| |B| |C| + (A · B) |C| + (A · C) |B| + (B · C) |A|), with A = (a, 1) and so on.
// On that plane the triple product is twice the triangle's area, which the differences of the corners give exactly. Every
// term of the divisor is positive for the corners of one texel, so nothing cancels, as it does in Driscoll's difference of
// four nearly equal corner areas, which loses most of a float's bits toward a face's corner on a large face.
float triangleSolidAngle(vec2 a, vec2 b, vec2 c)
{
    vec3 ra = vec3(a, 1.0);
    vec3 rb = vec3(b, 1.0);
    vec3 rc = vec3(c, 1.0);
    float twiceArea = abs((b.x - a.x) * (c.y - a.y) - (c.x - a.x) * (b.y - a.y));
    float la = length(ra);
    float lb = length(rb);
    float lc = length(rc);
    float divisor = la * lb * lc + dot(ra, rb) * lc + dot(ra, rc) * lb + dot(rb, rc) * la;
    return 2.0 * atan(twiceArea / divisor);
}

// The exact solid angle, in sr, of a texel of a face of `size` texels on a side, as two spherical triangles: the value of
// CubeFaces.TexelSolidAngle (Driscoll 2012, "Cubemap Texel Solid Angle") in a form that keeps a float's precision.
float texelSolidAngle(uvec2 texel, uint size)
{
    float scale = 2.0 / float(size);
    vec2 low = vec2(texel) * scale - 1.0;
    vec2 high = vec2(texel + 1u) * scale - 1.0;
    return triangleSolidAngle(low, vec2(high.x, low.y), high) + triangleSolidAngle(low, high, vec2(low.x, high.y));
}

#endif
