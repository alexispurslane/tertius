// The frame descriptor set (set 0) and the bindless texture array (set 1).
// Every struct here mirrors a C# struct in GpuTypes.cs. Change both together.
//
// Space conventions: "world" in the shaders means camera-relative world space. It has the axes of the camera's
// reference frame and its origin at the interpolated camera position, so the eye is at the origin for the main view.
#ifndef TERTIUS_FRAME_GLSL
#define TERTIUS_FRAME_GLSL

#extension GL_EXT_nonuniform_qualifier : require

const float PI = 3.14159265358979323846;

struct GpuInstance
{
    // Rows of the 3x4 model matrix (world from mesh space).
    vec4 modelRow0;
    vec4 modelRow1;
    vec4 modelRow2;
    // 1 / scale². modelLinear * (n * inverseScaleSq) = R * S⁻¹ * n, the correct normal transform.
    vec3 inverseScaleSq;
    uint material;
};

struct GpuMaterial
{
    vec4 baseColor;           // linear RGB, alpha for transparent objects
    vec3 emissive;            // linear RGB radiance
    float specularIntensity;
    float gloss;
    uint albedoTexture;       // bindless indices, already resolved to the default textures
    uint normalTexture;
    uint specularGlossTexture;
};

struct GpuLight
{
    vec4 positionRange;       // xyz world position, w range in meters
    vec4 radianceKind;        // rgb color × intensity, w 0 = point, 1 = spot
    vec4 directionCosOuter;   // xyz unit spot direction, w cos(outer half-angle)
    vec4 spot;                // x cos(inner half-angle)
};

layout(set = 0, binding = 0, std140) uniform FrameBlock
{
    mat4 rangeViewProj[3];    // far, middle, near: clip space from world space
    vec4 cameraRight;         // xyz right × tan(horizontal half-fov)
    vec4 cameraUp;            // xyz up × tan(vertical half-fov)
    vec4 cameraForward;       // xyz unit forward
    vec4 sunDirection;        // xyz unit vector toward the sun, w angular radius in radians
    vec4 sunRadiance;         // rgb color × intensity
    vec4 ambientSky;          // rgb sky color × intensity
    vec4 ambientGround;       // rgb ground color × intensity
    vec4 viewport;            // width, height, 1 / width, 1 / height
    vec4 params;              // x exposure, y time in seconds, z angular size of one pixel
    uvec4 counts;             // x lights, y occluders, z shadow cascades to sample (0: no shadow maps this frame)
    // Sun shadow cascades (see ShadowCascades.cs). Light space: x = shadowAxisX, y = shadowAxisY, z = sunDirection.
    mat4 shadowViewProj[4];   // shadow clip space from world space; xy in [-1, 1], z reversed (1 nearest the sun)
    vec4 shadowSplits;        // view depth where each cascade ends
    vec4 shadowBlendStart;    // view depth where each cascade starts to blend into the next (the last fades out)
    vec4 shadowTexel;         // world size of one shadow texel per cascade
    vec4 shadowAxisX;         // xyz light-space x axis, w lateral half-extent / depth half-range of the cascade boxes
    vec4 shadowAxisY;         // xyz light-space y axis
    vec4 shadowParams;        // x 1 / map size, y normal offset in texels, z constant depth bias, w smallest receiver cosine
} frame;

layout(set = 0, binding = 1, std430) readonly buffer InstanceBlock { GpuInstance instances[]; };
layout(set = 0, binding = 2, std430) readonly buffer MaterialBlock { GpuMaterial materials[]; };
layout(set = 0, binding = 3, std430) readonly buffer LightBlock { GpuLight lights[]; };
// xyz world center, w radius.
layout(set = 0, binding = 4, std430) readonly buffer OccluderBlock { vec4 occluders[]; };
layout(set = 0, binding = 5) uniform sampler materialSampler;

layout(set = 1, binding = 0) uniform texture2D textures[];

#endif
