# The lights whose shadows are cascades and far shadows take a loop of their own, so that the loops of the near lights
# hold no cascade code and the far loop holds no tile code. The image stays the same but for the order of the sum.
import sys
p = sys.argv[1] + '/src/Tertius.Rendering/Shaders/lighting.glsl'
s = open(p).read()
def sub(a, b, n=1):
    global s
    assert s.count(a) == n, (a, s.count(a))
    s = s.replace(a, b)
sub("""void shapeLight(bool fiber, bool tinted, uint shape, ShadingInput s, SurfaceLobes lobes, vec3 V, uint shadowMaps,
    uint view, inout DirectTerms sum)
{""", """bool farShadowed(GpuLight light)
{
    uint slot = light.shadow.x;
    return slot != NoSlot && slot != AtlasTilesSlot;
}

float nearVisibility(GpuLight light, vec3 position, vec3 L, float d, vec3 geometricNormal, float sinTheta,
    uint shadowMaps)
{
    if (light.shadow.x != AtlasTilesSlot || shadowMaps == ShadowMapsNone)
        return 1.0;
    return tileShadow(light, position, L, d, geometricNormal, sinTheta);
}

float farVisibility(GpuLight light, vec3 position, vec3 L, float d, vec3 geometricNormal, float sinTheta,
    uint shadowMaps)
{
    uint slot = light.shadow.x;
    float angularRadius = asin(lightSineAt(light, d));
    if (slot == FarShadowsOnlySlot || shadowMaps == ShadowMapsNone)
        return farShadowVisibility(position, L, angularRadius, light.occluders, 1.0);
    uint set = light.shadow.y;
    if (shadowMaps == ShadowMapsMainView)
    {
        float visibility = farShadowVisibility(position, L, angularRadius, light.occluders, beyondShadowMaps(position));
        if (visibility > 0.0)
            visibility *= cascadeShadow(set, position, geometricNormal, sinTheta);
        return visibility;
    }
    float visibility = farShadowVisibility(position, L, angularRadius, light.occluders, beyondShadowMapsHolding(set, position));
    if (visibility > 0.0)
        visibility *= cascadeShadowHolding(set, position, geometricNormal, sinTheta);
    return visibility;
}

void shapeLight(bool fiber, bool tinted, uint shape, bool far, ShadingInput s, SurfaceLobes lobes, vec3 V,
    uint shadowMaps, uint view, inout DirectTerms sum)
{""")
sub("""        if (!viewKeeps(view, lights[i]) || shapeOf(lights[i]) != shape)
            continue;
        LightPath p;
        if (!lightPath(i, fiber, tinted, shape, s, p))
            continue;
        float visibility = pathVisibility(i, p, s.position, shadowMaps);
        if (visibility > 0.0)""", """        if (!viewKeeps(view, lights[i]) || shapeOf(lights[i]) != shape || farShadowed(lights[i]) != far)
            continue;
        LightPath p;
        if (!lightPath(i, fiber, tinted, shape, s, p))
            continue;
        float visibility;
        if (far)
            visibility = farVisibility(lights[i], s.position, p.L, p.d, p.shadowNormal, p.sinTheta, shadowMaps);
        else
            visibility = nearVisibility(lights[i], s.position, p.L, p.d, p.shadowNormal, p.sinTheta, shadowMaps);
        if (visibility > 0.0)""")
sub("""    shapeLight(fiber, tinted, PointShape, s, lobes, V, shadowMaps, view, sum);
    shapeLight(fiber, tinted, TubeShape, s, lobes, V, shadowMaps, view, sum);
    shapeLight(fiber, tinted, RectShape, s, lobes, V, shadowMaps, view, sum);""", """    shapeLight(fiber, tinted, PointShape, false, s, lobes, V, shadowMaps, view, sum);
    shapeLight(fiber, tinted, PointShape, true, s, lobes, V, shadowMaps, view, sum);
    shapeLight(fiber, tinted, TubeShape, false, s, lobes, V, shadowMaps, view, sum);
    shapeLight(fiber, tinted, RectShape, false, s, lobes, V, shadowMaps, view, sum);""")
open(p, 'w').write(s)
