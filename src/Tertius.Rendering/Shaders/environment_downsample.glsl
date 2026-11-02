// A run of up to three levels of a cube array's mips, k + 1 to k + 3, from level k (EnvironmentPrefilterPass.cs,
// docs/plans/environment-map.md, "The radiance map's mips", and docs/plans/environment-map-distances.md, "The level
// chain"). Each texel of a level is the mean of its four children in the level above it, weighted by each child's solid
// angle, in every channel. So a texel of the radiance map holds the mean radiance over its own solid angle, and its alpha
// the open-sky share of that solid angle, wherever it lies on the face. A texel of the distance cube holds the mean inverse
// distance over its solid angle.
//
// The stage that includes this file defines DOWNSAMPLE_FORMAT, the storage images' format: rgba16f for the radiance maps
// and the irradiance cubes (environment_downsample.comp), r16f for the distance cubes
// (environment_distance_downsample.comp). An r16f image loads as (w, 0, 0, 1) and stores its first channel, so the same
// math makes the mean of w.
//
// One invocation per texel of level k + 1 of one map, over (W_k+1, W_k+1, 6): face z of map m is layer 6m + z. A
// workgroup's 8 by 8 texels of level k + 1 are the children of 4 by 4 texels of level k + 2, and those of 2 by 2 texels
// of level k + 3. So the workgroup makes those too, from the texels that it wrote, and the chain takes one dispatch and
// one barrier for every three levels. It reads them back from the images, not from registers or shared memory, so a
// level is the mean of the level above as stored, in halves rounded as the device rounds them, whichever dispatch made
// that level.
#ifndef TERTIUS_ENVIRONMENT_DOWNSAMPLE_GLSL
#define TERTIUS_ENVIRONMENT_DOWNSAMPLE_GLSL

#include "cube_faces.glsl"

layout(local_size_x = 8, local_size_y = 8) in;

// Binding 0 holds level k, and bindings 1 to 3 levels k + 1 to k + 3, each a storage image of six layers per map
// (DescriptorLayouts.EnvironmentLevelsSet). A binding past the run's last level holds that level again, and nothing
// writes or reads it. The workgroup reads back levels k + 1 and k + 2, so they are coherent.
layout(set = 1, binding = 0, DOWNSAMPLE_FORMAT) uniform readonly image2DArray source;
layout(set = 1, binding = 1, DOWNSAMPLE_FORMAT) uniform coherent image2DArray level1;
layout(set = 1, binding = 2, DOWNSAMPLE_FORMAT) uniform coherent image2DArray level2;
layout(set = 1, binding = 3, DOWNSAMPLE_FORMAT) uniform writeonly image2DArray level3;

// EnvironmentDownsamplePushConstants in GpuTypes.cs.
layout(push_constant, std430) uniform DownsamplePush
{
    uint levelSize;   // W_k+1, of level k + 1 ≥ 1
    uint levelCount;  // the levels of the run, 1 to 3
    uint map;         // m: the dispatch reads and writes layers 6m to 6m + 5
} downsample;

// Texel `texel` of the run's level `level`, 0 for level k.
vec4 load(uint level, ivec3 texel)
{
    switch (level)
    {
        case 0u: return imageLoad(source, texel);
        case 1u: return imageLoad(level1, texel);
        default: return imageLoad(level2, texel);
    }
}

void store(uint level, ivec3 texel, vec4 value)
{
    switch (level)
    {
        case 1u: imageStore(level1, texel, value); break;
        case 2u: imageStore(level2, texel, value); break;
        default: imageStore(level3, texel, value); break;
    }
}

void main()
{
    uvec2 local = gl_LocalInvocationID.xy;
    uint layer = 6u * downsample.map + gl_WorkGroupID.z;
    uint size = 2u * downsample.levelSize;
    uint tile = 16u;
    // The push constants are the same in the whole dispatch, so every invocation reaches each barrier or none does.
    for (uint level = 1u; level <= downsample.levelCount; level++)
    {
        uint childSize = size;
        size = max(size / 2u, 1u);
        tile /= 2u;
        // The workgroup's texels of this level: tile by tile of them, the parents of its texels of the level above.
        uvec2 texel = tile * gl_WorkGroupID.xy + local;
        if (local.x < tile && local.y < tile && texel.x < size && texel.y < size)
        {
            vec4 light = vec4(0.0);
            float solidAngle = 0.0;
            // The children in row and column order, so one map always gives the same bits on one GPU.
            for (uint dy = 0u; dy < 2u; dy++)
                for (uint dx = 0u; dx < 2u; dx++)
                {
                    uvec2 child = 2u * texel + uvec2(dx, dy);
                    float childAngle = texelSolidAngle(child, childSize);
                    light += load(level - 1u, ivec3(child, layer)) * childAngle;
                    solidAngle += childAngle;
                }
            // The children's solid angles add up to the texel's own, so this is the mean over the texel.
            store(level, ivec3(texel, layer), light / solidAngle);
        }
        // The texels of this level reach the next level's invocations in the workgroup.
        memoryBarrierImage();
        barrier();
    }
}

#endif
