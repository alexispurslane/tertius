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
    // 1 when the instance's entity holds a light, whose face its glow is, else 0 (GpuInstance.HoldsLight).
    uint holdsLight;
    // The instance's place among the frame's placed instances, the same in every view and depth range that draws it,
    // which its hair pixels' card codes mix in (GpuInstance.PlacedIndex).
    uint placedIndex;
};                            // std430: 92 bytes of members, a stride of 96

// A hair pixel's card code (docs/plans/skin-and-hair.md, "The card number"): the number of its card in its mesh, mixed
// with its instance's place into 13 bits. 5063, about 2^13 / φ and odd, puts neighbor instances' codes far apart, and the
// cards of one instance take distinct codes up to 2^13 cards. CardCode.Of is the C# twin.
uint cardCode(uint card, uint placedIndex)
{
    return (card + placedIndex * 5063u) & 0x1FFFu;
}

// The glow that a surface sends to other surfaces as their light: its emission, or none where its entity holds a light. An
// entity sends its power once (user, 2026-10-27): one that holds a light sends it through that light, and its glow is the
// light's face, not a second source. The bounce source (occlusion_depth.comp) and the environment faces
// (environment_face.frag) read the rule here. holdsLight is GpuInstance.holdsLight, or the G-buffer's copy of it.
vec3 glowSent(vec3 emissive, uint holdsLight)
{
    return holdsLight == 0u ? emissive : vec3(0.0);
}

// GpuMaterial.NoMap: the map index of a material that has no such map.
const uint NoMap = 0xFFFFFFFFu;

struct GpuMaterial
{
    vec4 baseColor;               // linear RGB, alpha: coverage under cutout or fringed, opacity under glass
    vec3 emissive;                // linear RGB radiance
    float metallic;               // a standard model's, 0 for skin and hair
    float roughness;              // r₀, before the floor
    float reflectance;
    uint baseColorMap;            // bindless indices, already resolved to the default textures
    uint normalMap;               // NoMap: none, so the shading normal is the vertex normal
    uint metallicRoughnessMap;    // glTF layout: roughness in G, metallic in B
    uint shading;                 // ShadingCode.Word: bits 0 and 1 the model's code, bits 2 and 3 a hair's strand direction
    float alphaCutoff;            // a cutout or fringed material's cutoff, 0 otherwise
};                                // std430: 60 bytes of members, a stride of 64

// The shading models' codes, ShadingCode in C#: bits 0 and 1 of GpuMaterial.shading, and the shading target's alpha.
const uint ShadingStandard = 0u;
const uint ShadingSkin = 1u;
const uint ShadingHair = 2u;

// The shading target's B and A (docs/plans/skin-and-hair.md, "The shading target"): bit 0 of B is the light mark, bits 0
// and 1 of A the model's code, and the 13 bits above them, 7 of B and 6 of A, a hair pixel's card code, 0 for another
// model. CardCode (Channels, Read, LightMarkOf and ModelOf) holds the C# twins.
uvec2 lightModelAndCardWords(uint holdsLight, uint model, uint cardCode)
{
    uint written = model == ShadingHair ? cardCode : 0u;
    return uvec2(holdsLight | ((written & 0x7Fu) << 1), model | ((written >> 7) << 2));
}

uint lightMarkOfWord(uint b)
{
    return b & 1u;
}

uint modelOfWord(uint a)
{
    return a & 3u;
}

// NoCard: the card code of a pixel that is not hair, which no 13-bit code equals.
const uint NoCard = 0xFFFFFFFFu;

uint cardOfWords(uint b, uint a)
{
    return modelOfWord(a) == ShadingHair ? (b >> 1) | ((a >> 2) << 7) : NoCard;
}

// The shadow slot of a light in GpuLight.shadow.x (GpuLight.*Slot in GpuTypes.cs).
const uint NoSlot = 0u;
const uint CascadeSetSlot = 1u;
const uint FarShadowsOnlySlot = 2u;
const uint AtlasTilesSlot = 3u;

struct GpuLight
{
    vec4 positionReach;       // xyz world position, w reach in meters
    vec4 intensityKind;       // rgb color × intensity in W/sr, w 0 = point, 1 = spot
    vec4 directionCosOuter;   // xyz unit spot axis, w cos(outer half-angle); zero for a point light
    vec4 spotRadius;          // x cos(inner half-angle), zero for a point light; y the emitter's radius in meters
    uvec4 shadow;             // x the slot; y the set or the first tile view; z the tile views
    uvec3 occluders;          // x the first occluder, y the occluders, z how many of the first ones the cascade set also draws
    uint map;                 // the map of the light's carrier: a face of map i keeps it when map <= i
    uint inMainView;          // 1 when the light's reach touches the main view's frustum, else 0
};                            // std430: 100 bytes of members, a stride of 112

struct GpuCascadeSet
{
    mat4 viewProj[4];         // shadow clip space from world space per cascade; xy in [-1, 1], z reversed (1 nearest the light)
    vec4 axisX;               // xyz light-space x axis, w lateral half-extent / depth half-range of the boxes
    vec4 axisY;               // xyz light-space y axis
};                            // std140: 288 bytes

struct GpuShadowTile
{
    mat4 viewProj;            // tile clip space from world space: reversed-Z perspective with an infinite far plane from the light
    uint layer;               // the layer of the atlas that holds the tile
    float texelPerMeter;      // the world size of one texel at 1 m from the light along the view axis
    float near;               // the near plane, in meters from the light
    float unused;
};                            // std430: 80 bytes

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
    vec4 ambientSky;          // rgb sky color × intensity
    vec4 ambientGround;       // rgb ground color × intensity
    vec4 viewport;            // width, height, 1 / width, 1 / height
    vec4 params;              // x the most pre-exposed light that one puff adds to a pixel, y time in seconds, z angular size of one pixel
    uvec4 counts;             // x lights, y unused, z cascades of each drawn cascade set (0: no set this frame), w unused
    // The cascade sets of the far lights (see ShadowCascades.cs). The sets share one camera and one map size, so these
    // four hold for every set.
    vec4 shadowSplits;        // view depth where each cascade ends
    vec4 shadowBlendStart;    // view depth where each cascade starts to blend into the next (the last fades out)
    vec4 shadowTexel;         // world size of one shadow texel per cascade
    vec4 shadowParams;        // x 1 / map size, y normal offset in texels, z constant depth bias, w smallest receiver cosine
    vec4 rootRotation;        // the root frame's axes in world axes, as a quaternion (xyz, w)
    GpuCascadeSet cascadeSets[4];   // ShadowCascades.MaxSets; set i belongs to the light whose slot names it
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
// The tile views of the local shadows (ShadowAtlas.TileViewsBinding): a near light's AtlasTiles slot names its views.
// Partially bound: empty without the atlas, when no light has such a slot.
layout(set = 0, binding = 15, std430) readonly buffer ShadowTileBlock { GpuShadowTile shadowTiles[]; };

layout(set = 1, binding = 0) uniform texture2D textures[];

#endif
