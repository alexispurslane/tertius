// The bloom pass (ARCHITECTURE.md, "Bloom"): every draw reads one image, the HDR image or a bloom level, through the
// linear clamp sampler (linear, clamped to the edge), and writes the next level.

#include "downsample.glsl"

layout(set = 0, binding = 0) uniform texture2D source;
layout(set = 0, binding = 1) uniform sampler linearClamp;

layout(push_constant, std430) uniform BloomPush
{
    float threshold;   // the brightness of the pre-exposed HDR image, as the largest channel, at the center of the soft knee
    float knee;        // the half-width of the soft knee around the threshold, as a fraction of it
    uint levels;       // how many levels the up chain sums into level 0
} bloom;

layout(location = 0) out vec4 outColor;
