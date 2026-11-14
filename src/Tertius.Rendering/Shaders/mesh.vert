#version 460
#extension GL_GOOGLE_include_directive : require
#include "frame.glsl"
#include "draw.glsl"
#include "motion.glsl"

// ShaderConstants.WritesMotion: true in the G-buffer's pipelines, which write the motion target.
layout(constant_id = 4) const bool writesMotion = false;

layout(location = 0) in vec3 inPosition;
layout(location = 1) in vec3 inNormal;
layout(location = 2) in vec4 inTangent;
layout(location = 3) in vec2 inTexCoord;
layout(location = 4) in uint inCard;    // GpuVertex.Card: the number of the vertex's card in its mesh

layout(location = 0) out vec3 outPosition;
layout(location = 1) out vec3 outNormal;
layout(location = 2) out vec4 outTangent;
layout(location = 3) out vec2 outTexCoord;
// x: the material. y: the instance's environment map. z: whether its entity holds a light, which gbuffer.frag and
// environment_face.frag read. w: the card code, which gbuffer.frag writes for a hair pixel. They share a location so that
// a fragment shader that reads only some of them, such as environment_face.frag, still consumes the whole location: a
// face has no ambient light, and its light pass takes the face's map from its own push constants.
layout(location = 4) flat out uvec4 outInstanceWords;
layout(location = 5) flat out vec4 outHemisphere;
// The vertex's place in this frame's and in the last frame's motion clip space, x, y and w (motion.glsl). Only the
// G-buffer's pipelines write them, and a last w of 0 marks an instance without history.
layout(location = 6) out vec3 outMotionNow;
layout(location = 7) out vec3 outMotionLast;

// The vertex's position in mesh space as the last frame drew it: its place in the last frame's deformed copy in the kept
// positions, or this frame's position without such a copy. A copy of the same vertex count keeps the static mesh's
// vertex order, so the vertex's place in it is its place in this frame's frame mesh.
vec3 lastPositionOf(GpuMotion motion)
{
    if (motion.previousFirstVertex == NoKeptCopy)
        return inPosition;
    uint k = 3u * (motion.previousFirstVertex + uint(gl_VertexIndex) - motion.firstVertex);
    return vec3(keptPositions[k], keptPositions[k + 1u], keptPositions[k + 2u]);
}

void main()
{
    // gl_InstanceIndex includes the indirect command's firstInstance, so it indexes the instance buffer directly.
    GpuInstance instance = instances[gl_InstanceIndex];
    vec4 p = vec4(inPosition, 1.0);
    vec3 world = vec3(dot(instance.modelRow0, p), dot(instance.modelRow1, p), dot(instance.modelRow2, p));
    mat3 linear = transpose(mat3(instance.modelRow0.xyz, instance.modelRow1.xyz, instance.modelRow2.xyz));

    outPosition = world;
    outNormal = linear * (inNormal * instance.inverseScaleSq);
    outTangent = vec4(linear * inTangent.xyz, inTangent.w);
    outTexCoord = inTexCoord;
    // Flat, from the provoking vertex: every vertex of a triangle carries the triangle's card.
    outInstanceWords = uvec4(instance.material, instance.map, instance.holdsLight, cardCode(inCard, instance.placedIndex));
    outHemisphere = instance.hemisphere;
    gl_Position = draw.viewProj * vec4(world, 1.0);
    outMotionNow = vec3(0.0);
    outMotionLast = vec3(0.0);
    if (writesMotion)
    {
        GpuMotion motion = motions[instance.placedIndex];
        vec4 q = vec4(lastPositionOf(motion), 1.0);
        vec3 last = vec3(dot(motion.previousRow0, q), dot(motion.previousRow1, q), dot(motion.previousRow2, q));
        outMotionNow = clipOf(world);
        outMotionLast = motion.kind == MotionNew ? vec3(0.0) : lastClipOf(last);
    }
}
