// The fragment rule of the back-face pass (docs/plans/back-face-depth-layer.md, decision 4): which of its fragments count
// for a pixel, and the back share that a counted fragment writes. The pass's depth test keeps the nearest counted one.
#ifndef TERTIUS_BACK_FACE_GLSL
#define TERTIUS_BACK_FACE_GLSL

// ShaderConstants.BothFacesId: true in the pipelines of the runs whose both faces the pass draws, false in those of the
// closed one-sided runs.
layout(constant_id = 7) const bool bothFaces = false;

layout(location = 0) out float outShare;

// The tolerance within which a fragment's distance counts as the front surface's own, as a share of that distance.
// It keeps a front face from counting as its own back face. It is a float tolerance, not a thickness.
const float OwnSurfaceShare = 1e-4;

// The back share of a fragment at `distance` m from the eye over a front surface at `front` m, or a negative value
// for a fragment that does not count. A run that draws both faces counts a fragment at the front's own distance and
// gives it the share 1. Over the sky, whose front is 0, every fragment gives 0, the share of no fragment.
float backShareOf(float distance, float front, bool bothFaces)
{
    if (distance > front * (1.0 + OwnSurfaceShare))
        return front / distance;
    bool ownSurface = distance >= front * (1.0 - OwnSurfaceShare);
    return bothFaces && ownSurface ? 1.0 : -1.0;
}

// Writes the back share of the fragment at camera-relative `position` over the pixel's front surface, or discards a
// fragment that does not count.
void writeBackShare(vec3 position)
{
    float front = texelFetch(gbufferDistance, ivec2(gl_FragCoord.xy), 0).r;
    float share = backShareOf(length(position - draw.eye.xyz), front, bothFaces);
    if (share < 0.0)
        discard;
    outShare = share;
}

#endif
