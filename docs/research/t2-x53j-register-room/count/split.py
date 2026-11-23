# SCRATCH t0-t2oq (the register count, never merged): design 2 of the direct shader's register budget, the visibility
# loop first. python3 split.py <shader folder> <array size>
import sys, re
folder, size = sys.argv[1], int(sys.argv[2])
def edit(name, pairs):
    path = f"{folder}/{name}"
    s = open(path).read()
    for old, new in pairs:
        assert s.count(old) == 1, (name, old[:60], s.count(old))
        s = s.replace(old, new)
    open(path, "w").write(s)

split_code = '''
#ifdef VISIBILITY_FIRST
// SCRATCH t0-t2oq (design 2 of the register budget): the visibility of every shadowed light of the main view at the
// pixel, before the response loops, in the order of the light list. The size: ShadowAtlas.Tiles lamps, plus
// MaxCascadedLights cascade sets, plus as many far-shadows-only lights.
const uint MaxShadowedLights = %du;
float shadowedVisibility[MaxShadowedLights];

// The share of `light` that its shadows let through at `position` in the main view, where the light can reach it:
// lightPath's direction, distance, turned geometric normal and angle, made here, so no caller holds them. 0 outside the
// light's reach and a spot's cone, where its response is 0.
float visibilityOf(GpuLight light, vec3 position, vec3 geometricNormal)
{
    vec3 toCenter = light.positionReach.xyz - position;
    float distanceSq = dot(toCenter, toCenter);
    float reach = light.positionReach.w;
    if (distanceSq >= reach * reach)
        return 0.0;
    float inverseDistance = inversesqrt(max(distanceSq, 1e-8));
    vec3 L = toCenter * inverseDistance;
    float d = distanceSq * inverseDistance;
    if (light.intensityKind.w == SpotKind && dot(-L, light.directionCosOuter.xyz) <= light.directionCosOuter.w)
        return 0.0;
    float geometricNdotL = dot(geometricNormal, L);
    vec3 shadowNormal = geometricNdotL < 0.0 ? -geometricNormal : geometricNormal;
    float sinTheta = sqrt(max(1.0 - geometricNdotL * geometricNdotL, 0.0));
    return lightVisibility(light, position, L, d, shadowNormal, sinTheta, ShadowMapsMainView);
}

// Fills shadowedVisibility for the surface point at `position` with the geometric normal `geometricNormal`.
void fillVisibilities(vec3 position, vec3 geometricNormal)
{
    uint n = 0u;
    for (uint i = 0u; i < frame.counts.x && n < MaxShadowedLights; i++)
    {
        if (!viewKeeps(MainView, lights[i]) || lights[i].shadow.x == NoSlot)
            continue;
        shadowedVisibility[n] = visibilityOf(lights[i], position, geometricNormal);
        n++;
    }
}

// The visibility of light i in a response loop, from its place `slot` among the shadowed lights of the main view.
float seenAt(uint i, uint slot)
{
    return lights[i].shadow.x == NoSlot ? 1.0 : slot < MaxShadowedLights ? shadowedVisibility[slot] : 0.0;
}
#endif
''' % size

edit("lighting.glsl", [
    ("// MaterialSample.TubeTerms per unit of the tube's scalar irradiance", split_code + "\n// MaterialSample.TubeTerms per unit of the tube's scalar irradiance"),
    ('''    if ((frame.counts.w & shape) == 0u)
        return;
    for (uint i = 0u; i < frame.counts.x; i++)
    {
        if (!viewKeeps(view, lights[i]) || shapeOf(lights[i]) != shape)
            continue;
        LightPath p;
        if (!lightPath(i, fiber, tinted, shape, s, p))
            continue;
        float visibility = pathVisibility(i, p, s.position, shadowMaps);''',
     '''    if ((frame.counts.w & shape) == 0u)
        return;
#ifdef VISIBILITY_FIRST
    uint n = 0u;
#endif
    for (uint i = 0u; i < frame.counts.x; i++)
    {
#ifdef VISIBILITY_FIRST
        if (!viewKeeps(view, lights[i]))
            continue;
        uint slot = n;
        if (lights[i].shadow.x != NoSlot)
            n++;
        if (shapeOf(lights[i]) != shape)
            continue;
#else
        if (!viewKeeps(view, lights[i]) || shapeOf(lights[i]) != shape)
            continue;
#endif
        LightPath p;
        if (!lightPath(i, fiber, tinted, shape, s, p))
            continue;
#ifdef VISIBILITY_FIRST
        float visibility = seenAt(i, slot);
#else
        float visibility = pathVisibility(i, p, s.position, shadowMaps);
#endif'''),
])
edit("key_fill_light.glsl", [
    ('''    if ((frame.counts.w & shape) == 0u)
        return;
    for (uint i = 0u; i < frame.counts.x; i++)
    {
        if (!viewKeeps(MainView, lights[i]) || shapeOf(lights[i]) != shape)
            continue;''',
     '''    if ((frame.counts.w & shape) == 0u)
        return;
#ifdef VISIBILITY_FIRST
    uint n = 0u;
#endif
    for (uint i = 0u; i < frame.counts.x; i++)
    {
#ifdef VISIBILITY_FIRST
        if (!viewKeeps(MainView, lights[i]))
            continue;
        uint slot = n;
        if (lights[i].shadow.x != NoSlot)
            n++;
        if (shapeOf(lights[i]) != shape)
            continue;
#else
        if (!viewKeeps(MainView, lights[i]) || shapeOf(lights[i]) != shape)
            continue;
#endif'''),
    ('''        float visibility = pathVisibility(i, p, s.position, ShadowMapsMainView);
        if (visibility <= 0.0)
            continue;''',
     '''#ifdef VISIBILITY_FIRST
        float visibility = seenAt(i, slot);
#else
        float visibility = pathVisibility(i, p, s.position, ShadowMapsMainView);
#endif
        if (visibility <= 0.0)
            continue;'''),
])
for frag in ("direct.frag", "direct_key_fill.frag"):
    edit(frag, [
        ('#extension GL_GOOGLE_include_directive : require\n', '#extension GL_GOOGLE_include_directive : require\n#define VISIBILITY_FIRST\n'),
        ('    ShadingInput s = materialAt(pixel);\n', '    ShadingInput s = materialAt(pixel);\n    fillVisibilities(s.position, s.geometricNormal);\n'),
    ])
