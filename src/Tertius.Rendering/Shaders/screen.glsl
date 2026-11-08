// The screen's geometry in the camera-relative world axes (docs/plans/screen-space-reflections.md, "The ray's image on
// the screen"). CameraView holds the unjittered C# geometry; this file also applies the frame's current and previous jitter.
#ifndef TERTIUS_SCREEN_GLSL
#define TERTIUS_SCREEN_GLSL

#include "frame.glsl"

// The view ray through a position on the screen, in pixels from the top left: forward + x right tan h − y up tan v. Not
// a unit vector; its component along forward is 1. lighting.vert interpolates the same vector.
vec3 viewRayThrough(vec2 pixel)
{
    vec2 ndc = (pixel + frame.renderParams.yz) * frame.viewport.zw * 2.0 - 1.0;
    return frame.cameraForward.xyz + ndc.x * frame.cameraRight.xyz - ndc.y * frame.cameraUp.xyz;
}

// The position on the screen of a point in front of the eye, in pixels from the top left. cameraRight and cameraUp
// carry the tangents of the half angles, so a dot over their squared length is the coordinate over that tangent.
vec2 pixelOf(vec3 point)
{
    float depth = dot(point, frame.cameraForward.xyz);
    vec2 ndc = vec2(
        dot(point, frame.cameraRight.xyz) / dot(frame.cameraRight.xyz, frame.cameraRight.xyz),
        -dot(point, frame.cameraUp.xyz) / dot(frame.cameraUp.xyz, frame.cameraUp.xyz)) / depth;
    return (ndc + 1.0) * 0.5 * frame.viewport.xy - frame.renderParams.yz;
}

// The sample place: the place of the last texel whose jittered sample saw the point that the pixel's jittered sample
// sees. It moves by this draw's jitter minus the last draw's. The surface test rebuilds a pixel's last point through it,
// and the pixel history rule below falls back to it at a silhouette.
vec2 historyPlace(vec2 pixel, vec2 motion)
{
    return (pixel + frame.renderParams.yz) * frame.viewport.zw + motion
        - frame.previousJitter.xy * frame.previousViewport.zw;
}

// The pixel history rule (ticket tiunr-4b2g, "jitter is not motion"): a history texel stands for its whole pixel,
// averaged over all jitter offsets, so a history moves only with the scene. Re-reading it at the sample place would
// resample it bilinearly in every jittered draw, and a history of hundreds of frames would blur.

// The pixel place: the pixel itself, moved by the scene's motion alone. A history texel stands for its whole pixel,
// averaged over all jitter offsets, so in a still scene this is the pixel's own texel center.
vec2 pixelPlace(vec2 pixel, vec2 motion)
{
    return pixel * frame.viewport.zw + motion;
}

// The place whose history the pixel reads, from its motion target texel: the pixel place where the surface test marked
// the pixel in w (surface_test.comp), else the sample place.
vec2 readPlace(vec2 pixel, vec4 motion)
{
    return motion.w != 0.0 ? pixelPlace(pixel, motion.xy) : historyPlace(pixel, motion.xy);
}

#endif
