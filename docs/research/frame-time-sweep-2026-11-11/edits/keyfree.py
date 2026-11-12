# A timing bound: a pixel's surface key costs no read and no rebuild, so every neighbor counts as the same surface.
import sys; sys.path.insert(0, '/private/tmp/tertius-sweep-20261111/edits'); import common
root = sys.argv[1]; common.fixexp(root)
p = root + common.SH + 'temporal.glsl'
common.sub(p, """    key.depth = texelFetch(gbufferDistance, pixel, 0).r / length(viewRayThrough(vec2(pixel) + 0.5));
    key.normal = octDecode(texelFetch(gbufferNormal, pixel, 0).ba);
    key.model = modelOfWord(texelFetch(gbufferShading, pixel, 0).a);
""", """    key.depth = 1.0;
    key.normal = vec3(0.0, 0.0, 1.0);
    key.model = 0u;
""")
