# Timing proxy of a hit light image. (1) A ray's hit reads its light with one image read, instead of the hit pixel's
# material, its two reprojections, eight history taps and both direct lights. (2) The resolve computes the full hit light
# of every pixel once, as the pass that writes such an image does, and adds it times 1e-30 to its sample, which changes
# no stored value. So the ray passes show the saving and the resolve shows the cost. The rays' light is wrong.
import sys; sys.path.insert(0, '/private/tmp/tertius-sweep-20261111/edits'); import common
root = sys.argv[1]; common.fixexp(root)
p = root + common.SH + 'screen_trace.glsl'
common.sub(p, 'vec3 hitLightOf(ivec2 q, uint kind)\n{',
    'vec3 hitLightOf(ivec2 q, uint kind)\n{\n    return min(imageLoad(directDiffuseLight, q).rgb, vec3(MaxRadiance));\n}\n\nvec3 fullHitLightOf(ivec2 q, uint kind)\n{')
common.sub(root + common.SH + 'screen_trace_resolve.comp',
    '    imageStore(specularSample, pixel, vec4(min(specularLight, vec3(MaxRadiance)), hitDistance));',
    '    specularLight += fullHitLightOf(pixel, KindReflection) * 1e-30;\n    imageStore(specularSample, pixel, vec4(min(specularLight, vec3(MaxRadiance)), hitDistance));')
