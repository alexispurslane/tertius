// The motion target (docs/plans/temporal-renderer.md, "Jitter"): for each pixel, xy the last drawn frame's place of its
// surface point minus this frame's place, in units of the image's width and height, with y down the image, and z the
// ratio of the point's view depth in the last frame to its view depth in this frame. Needs frame.glsl.
#ifndef TERTIUS_MOTION_GLSL
#define TERTIUS_MOTION_GLSL

// The motion of a pixel whose surface point has no last place: it moves the point two images away, outside the last
// frame's image, where no history lies.
const vec2 NoHistoryMotion = vec2(2.0);

// The places of a point in this frame and in the last frame, as clip x, y and w of the two motion projections
// (frame.motionViewProj and frame.previousViewProj). A last w of 0 or less has no last place: a new instance, or a point
// behind the last frame's eye.
vec4 motionOf(vec3 now, vec3 last)
{
    if (last.z <= 0.0 || now.z <= 0.0)
        return vec4(NoHistoryMotion, 1.0, 0.0);
    return vec4(0.5 * (last.xy / last.z - now.xy / now.z), last.z / now.z, 0.0);
}

// The place of a point relative to this frame's eye in the last frame's motion clip space, from its last position
// relative to the last frame's eye.
vec3 lastClipOf(vec3 last)
{
    return (frame.previousViewProj * vec4(last, 1.0)).xyw;
}

// The place of a point relative to this frame's eye in this frame's motion clip space.
vec3 clipOf(vec3 world)
{
    return (frame.motionViewProj * vec4(world, 1.0)).xyw;
}

#endif
