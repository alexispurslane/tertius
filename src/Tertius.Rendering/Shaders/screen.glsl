// The screen's geometry in the camera-relative world axes (docs/plans/screen-space-reflections.md, "The ray's image on
// the screen"). CameraView's ViewRayThrough, PixelOf and InverseDepthOf are the C# copy. Change both together.
#ifndef TERTIUS_SCREEN_GLSL
#define TERTIUS_SCREEN_GLSL

#include "frame.glsl"

// The view ray through a position on the screen, in pixels from the top left: forward + x right tan h − y up tan v. Not
// a unit vector; its component along forward is 1. lighting.vert interpolates the same vector.
vec3 viewRayThrough(vec2 pixel)
{
    vec2 ndc = pixel * frame.viewport.zw * 2.0 - 1.0;
    return frame.cameraForward.xyz + ndc.x * frame.cameraRight.xyz - ndc.y * frame.cameraUp.xyz;
}

#endif
