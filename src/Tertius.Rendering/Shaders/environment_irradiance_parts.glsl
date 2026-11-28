#ifndef TERTIUS_ENVIRONMENT_IRRADIANCE_PARTS_GLSL
#define TERTIUS_ENVIRONMENT_IRRADIANCE_PARTS_GLSL

// The buffer layout matches IrradianceCube.ContributionBytes. Slots run by map, then capture face.
// Each slot holds every direction of its map's irradiance cube, in face, row and column order.
const uint IrradianceFaceSize = 16u;
const uint IrradianceFaceTexels = IrradianceFaceSize * IrradianceFaceSize;
const uint IrradianceTexels = 6u * IrradianceFaceTexels;

struct IrradiancePart
{
    vec4 current; // Weighted radiance and open-sky sum published on the previous draw.
    vec4 target;  // Weighted sum from the latest capture.
    float weight; // Sum of the directional weights for this capture face.
};

layout(set = 0, binding = 2, std430) buffer IrradianceParts
{
    uint endDraw[6 * 42]; // Last draw in the blend, by capture slot. 42 is EnvironmentChain.MaxMaps.
    IrradiancePart parts[];
} irradiance;

#endif
