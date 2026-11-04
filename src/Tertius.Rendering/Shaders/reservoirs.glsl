// Set 3 of the reservoir layout (LightReservoirs.cs): the reservoirs that a pass of ReSTIR reads, and the surface test's
// outputs, which the candidates' temporal reuse reads. Every image holds the render size.
#ifndef TERTIUS_RESERVOIRS_GLSL
#define TERTIUS_RESERVOIRS_GLSL

#extension GL_EXT_samplerless_texture_functions : require

layout(set = 3, binding = 0) uniform utexture2D readReservoirs;               // RG32UI, as packReservoir packs them
layout(set = 3, binding = 1, r8ui) uniform readonly uimage2D surfaceTest;     // the pass bits (signals.glsl)
layout(set = 3, binding = 2, rgba16f) uniform readonly image2D motionTarget;  // the G-buffer's motion target

#endif
