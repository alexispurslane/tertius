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
    // The up of the hemisphere ambient light: xyz the up direction with w 1, or the point that up leads away from with
    // w 2. All zero mixes the sky and the ground evenly.
    vec4 hemisphere;
    // The instance's map: the index, root first, of the environment map that lights it. 0 with the map off.
    uint map;
};                            // std430: 84 bytes of members, a stride of 96

// GpuMaterial.NoMap: the map index of a material that has no such map.
const uint NoMap = 0xFFFFFFFFu;

struct GpuMaterial
{
    vec4 baseColor;               // linear RGB, alpha for transparent objects
    vec3 emissive;                // linear RGB radiance
    float metallic;
    float roughness;              // r₀, before the floor
    float reflectance;
    uint baseColorMap;            // bindless indices, already resolved to the default textures
    uint normalMap;               // NoMap: none, so the shading normal is the vertex normal
    uint metallicRoughnessMap;    // glTF layout: roughness in G, metallic in B
};                                // std430: 52 bytes of members, a stride of 64

struct GpuLight
{
    vec4 positionRange;       // xyz world position, w range in meters
    vec4 radianceKind;        // rgb color × intensity, w 0 = point, 1 = spot
    vec4 directionCosOuter;   // xyz unit spot direction, w cos(outer half-angle)
    float cosInner;           // cos(inner half-angle)
    uint map;                 // the map of the light's carrier: a face of map i keeps it when map <= i
};                            // std430: 56 bytes of members, a stride of 64

struct GpuOutline
{
    vec4 rotation;            // the frame's axes in world axes, as a quaternion (xyz, w)
    vec3 center;              // world position of the frame's origin
    uint color;               // sRGB-encoded display color, unpackUnorm4x8
    vec3 radii;               // along the frame's axes, in meters
    uint flags;               // bit 0: a sphere, which also draws its rim as seen from the eye
};

struct GpuPuff
{
    vec4 positionRadius;      // xyz world center, w radius in meters, both at the render age
    vec4 colorKind;           // rgb the own share × the radiance (glow) or the albedo (lit), w the case: 0 glow, 1 lit
    uint map;                 // the puff's map, whose mean radiance is a lit puff's ambient light
};                            // std430: 36 bytes of members, a stride of 48

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
    vec4 params;              // x the most pre-exposed light that one puff adds to a pixel, y time in seconds, z angular size of one pixel
    uvec4 counts;             // x lights, y occluders, z shadow cascades to sample (0: no shadow maps this frame), w occluders of casters in the maps (first)
    // Sun shadow cascades (see ShadowCascades.cs). Light space: x = shadowAxisX, y = shadowAxisY, z = sunDirection.
    mat4 shadowViewProj[4];   // shadow clip space from world space; xy in [-1, 1], z reversed (1 nearest the sun)
    vec4 shadowSplits;        // view depth where each cascade ends
    vec4 shadowBlendStart;    // view depth where each cascade starts to blend into the next (the last fades out)
    vec4 shadowTexel;         // world size of one shadow texel per cascade
    vec4 shadowAxisX;         // xyz light-space x axis, w lateral half-extent / depth half-range of the cascade boxes
    vec4 shadowAxisY;         // xyz light-space y axis
    vec4 shadowParams;        // x 1 / map size, y normal offset in texels, z constant depth bias, w smallest receiver cosine
    vec4 rootRotation;        // the root frame's axes in world axes, as a quaternion (xyz, w)
    vec4 mapAxes[42];         // R_i: the axes of map i's frame in world axes, a unit quaternion (xyz, w), root first
} frame;

layout(set = 0, binding = 1, std430) readonly buffer InstanceBlock { GpuInstance instances[]; };
layout(set = 0, binding = 2, std430) readonly buffer MaterialBlock { GpuMaterial materials[]; };
layout(set = 0, binding = 3, std430) readonly buffer LightBlock { GpuLight lights[]; };
// xyz world center, w radius.
layout(set = 0, binding = 4, std430) readonly buffer OccluderBlock { vec4 occluders[]; };
layout(set = 0, binding = 5) uniform sampler materialSampler;
layout(set = 0, binding = 6, std430) readonly buffer OutlineBlock { GpuOutline outlines[]; };
layout(set = 0, binding = 7, std430) readonly buffer PuffBlock { GpuPuff puffs[]; };
// The exposure in stops that this frame's passes use (ExposureResources.cs). The adapt step of the frame before wrote it.
layout(set = 0, binding = 11, std430) readonly buffer ExposureBlock { float exposureStops; };

layout(set = 1, binding = 0) uniform texture2D textures[];

#endif
