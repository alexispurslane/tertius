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

// The solid angle of the part of a face from its center to the point (x, y) of [−1, 1]², signed.
float cornerArea(float x, float y)
{
    return atan(x * y, sqrt(x * x + y * y + 1.0));
}

// The exact solid angle, in sr, of a texel of a face of `size` texels on a side (Driscoll 2012, "Cubemap Texel Solid
// Angle").
float texelSolidAngle(uvec2 texel, uint size)
{
    float scale = 2.0 / float(size);
    vec2 low = vec2(texel) * scale - 1.0;
    vec2 high = vec2(texel + 1u) * scale - 1.0;
    return cornerArea(low.x, low.y) - cornerArea(low.x, high.y) - cornerArea(high.x, low.y) + cornerArea(high.x, high.y);
}

#endif
