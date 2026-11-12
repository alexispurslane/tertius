# The diffuse filter's taps read only the light: no G-buffer read and no rebuild of the point and the normal per tap.
import sys; sys.path.insert(0, '/private/tmp/tertius-sweep-20261111/edits'); import common
root = sys.argv[1]; common.fixexp(root)
p = root + common.SH + 'diffuse_filter.glsl'
common.sub(p, """    vec3 ray = viewRayThrough(vec2(pixel) + 0.5);
    float distanceToEye = texelFetch(gbufferDistance, pixel, 0).r;
    tap.point = normalize(ray) * distanceToEye;
    // The view ray's component along forward is 1, so the view depth is the distance over its length.
    tap.depth = distanceToEye / length(ray);
    tap.normal = octDecode(texelFetch(gbufferNormal, pixel, 0).ba);
""", """    tap.point = vec3(0.0);
    tap.depth = 1.0;
    tap.normal = vec3(0.0, 0.0, 1.0);
""")
common.sub(p, "    return insideFilter(neighbor) && filteredAt(neighbor) ? neighbor : ivec2(-1);",
              "    return insideFilter(neighbor) ? neighbor : ivec2(-1);")
