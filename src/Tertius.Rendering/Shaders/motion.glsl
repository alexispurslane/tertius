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

// The least motion on screen that counts, in pixels per draw: the gate's threshold of ter-18hc.
const float LeastMotion = 1.0 / 32.0;

// The relative rounding of the motion target's half floats, which keep 10 bits below their leading bit: twice their
// largest rounding, so that a still surface's target and its still motion (stillMotionOf) stay inside it.
const float MotionRounding = 1.0 / 1024.0;

// The motion that a surface point would have if it stood still while the camera moved: the motion target's own rule
// (motionOf) at the point, relative to this frame's eye, in m. A still surface's motion target and this differ only by
// rounding: under a still camera by the float computation's last bits, a few 10^-6 pixels, and under a moving camera
// also by the target's half floats.
vec4 stillMotionOf(vec3 point)
{
    return motionOf(clipOf(point), lastClipOf(point - frame.previousEye.xyz));
}

// How far a pixel's motion target lies from the still motion of its surface point (stillMotionOf): x the distance of
// their places in pixels, y the rounding of that distance in pixels, z how far their depth ratios differ, and w the
// rounding of that. `point` is the point relative to this frame's eye, in m, and `target` its pixel's motion target.
vec4 motionApart(vec3 point, vec4 target)
{
    vec4 still = stillMotionOf(point);
    float apart = length((target.xy - still.xy) * frame.viewport.xy);
    float rounding = MotionRounding * length(still.xy * frame.viewport.xy);
    return vec4(apart, rounding, abs(target.z - still.z), MotionRounding * max(still.z, 1.0));
}

// Whether a surface point moved since the last draw (docs/plans/bounce-light-confidence.md, section 8.4): its place
// leaves a still point's by at least LeastMotion pixels and by more than the rounding, or its depth ratio by more than
// the rounding. `point` and `target` are motionApart's.
bool movedSurface(vec3 point, vec4 target)
{
    vec4 apart = motionApart(point, target);
    return apart.x >= max(LeastMotion, apart.y) || apart.z > apart.w;
}

// Whether a pixel's surface point moves on screen since the last draw by at least LeastMotion pixels, by its motion
// target `target`: the camera's moves count as well as the surface's (docs/plans/bounce-light-confidence.md, section
// 8.4).
bool movesOnScreen(vec4 target)
{
    return length(target.xy * frame.viewport.xy) >= LeastMotion;
}

#endif
