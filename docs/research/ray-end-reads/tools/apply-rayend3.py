# Tool code of ter-pdw8, the third probe step, applied after apply-rayend2.py on the same build copy (never to src).
#   - Bit 4 (16): the guide reads the same filtered light as stand-in (c)'s hit: the guided rays draw their targets and
#     densities from a sum pyramid of the direct part's running mean (layer 14 of the hit light), in place of the light
#     pyramid of this draw's direct light. Level 0 of layer 14 is the luminance of the direct mean of the diffuse kind,
#     times the coverage, where the pixel is a traced surface this draw, and 0 elsewhere (no target in the sky). Level k
#     holds the mean over its full 2^k x 2^k block with missing pixels as 0, so its sum is the value times 4^k, exactly as
#     the light pyramid's sum; half floats hold the means without overflow. The light pyramid is still built but unread.
#   - Bit 5 (32): the running means (both stand-ins) weigh a draw by 1/8 from 8 draws on, in place of 1/64: the test of
#     whether the old stand-in's loss is the half float's rounding (an update of 1/64 of a small change falls below half
#     a step of the format and is lost).
# Variant values used: 16 (V0's hit read, the filtered guide), 17 (stand-in (c) with the filtered guide), 36 (the old
# stand-in with weight 1/8).
# Usage: python3 apply-rayend3.py <build copy root>
import sys
from pathlib import Path

root = Path(sys.argv[1])


def edit(rel, old, new, count=1):
    path = root / rel
    text = path.read_text()
    assert text.count(old) == count, (rel, old[:80], text.count(old))
    path.write_text(text.replace(old, new))


S = 'src/Tertius.Rendering/Shaders/'
G = S + 'screen_trace.glsl'

edit(G,
     '''float pyramidLightAt(int level, ivec2 cell)
{
    if (level == 0)''',
     '''// PROBE (ter-pdw8): the filtered guide's sum over the cell of the level (bit 4): layer 14 of the atlas times 4^level.
float probeGuideSumAt(int level, ivec2 cell)
{
    if (any(lessThan(cell, ivec2(0))) || any(greaterThanEqual(cell, probeLevelSize(level))))
        return 0.0;
    return imageLoad(hitLight, ivec3(probeAtlasOf(level, cell), 14)).r * exp2(2.0 * float(level));
}

float pyramidLightAt(int level, ivec2 cell)
{
    if ((rayEndVariant & 16u) != 0u)
        return probeGuideSumAt(level, cell);
    if (level == 0)''')
edit(G,
     '''float guideTotal()
{
    return''',
     '''float guideTotal()
{
    if ((rayEndVariant & 16u) != 0u)
        return probeGuideSumAt(probeTopLevel(), ivec2(0));
    return''')
edit(G,
     '''    int top = textureQueryLevels(lightLevels);
    ivec2 cell = ivec2(0);''',
     '''    int top = (rayEndVariant & 16u) != 0u ? probeTopLevel() : textureQueryLevels(lightLevels); // PROBE (ter-pdw8)
    ivec2 cell = ivec2(0);''')
edit(G,
     '''    float light = inside ? texelFetch(lightFinest, q, 0).r : 0.0;
    if (light <= 0.0)
        return 0.0;''',
     '''    float light = inside ? texelFetch(lightFinest, q, 0).r : 0.0;
    if ((rayEndVariant & 16u) != 0u) // PROBE (ter-pdw8)
        light = probeGuideSumAt(0, q);
    if (light <= 0.0)
        return 0.0;''')

P = S + 'screen_trace_plan.comp'
edit(P,
     '''    float count = min(float(trace.drawIndex) + 1.0, 64.0);''',
     '''    float count = min(float(trace.drawIndex) + 1.0, (rayEndVariant & 32u) != 0u ? 8.0 : 64.0);''')
edit(P,
     '''        imageStore(hitLight, ivec3(pixel, 6 + kind), vec4(probeCovered > 0.0 ? sum + history : vec3(0.0), probeCovered));''',
     '''        imageStore(hitLight, ivec3(pixel, 6 + kind), vec4(probeCovered > 0.0 ? sum + history : vec3(0.0), probeCovered));
        // Level 0 of the filtered guide (bit 4): the direct mean's luminance where a walk can hit the pixel this draw.
        if (kind == 1)
            imageStore(hitLight, ivec3(pixel, 14), vec4(probeCovered > 0.0 ? dot(sum, vec3(0.2126, 0.7152, 0.0722)) : 0.0));''')

M = S + 'screen_trace_hit_mips.comp'
edit(M,
     '''    for (int layer = 0; layer < 8; layer++)''',
     '''    // PROBE (ter-pdw8): layer 14, the filtered guide: the mean over the full block, a missing child as 0.
    float guide = 0.0;
    for (int j = 0; j < 2; j++)
        for (int i = 0; i < 2; i++)
        {
            ivec2 child = 2 * cell + ivec2(i, j);
            if (all(lessThan(child, children)))
                guide += imageLoad(hitLight, ivec3(probeAtlasOf(level - 1, child), 14)).r;
        }
    imageStore(hitLight, ivec3(probeAtlasOf(level, cell), 14), vec4(0.25 * guide));
    for (int layer = 0; layer < 8; layer++)''')

edit('src/Tertius.Rendering/Frame/ScreenTraceImages.cs',
     '''            layers: 14, // PROBE (ter-pdw8): 4 to 7 stand-in (c), 8 to 13 the rays' hits''',
     '''            layers: 15, // PROBE (ter-pdw8): 4 to 7 stand-in (c), 8 to 13 the rays' hits, 14 the filtered guide''')
edit('src/Tertius.Rendering/Passes/ScreenTracePass.cs',
     '''        if ((Variant & 2u) == 0u)''',
     '''        if ((Variant & 18u) == 0u) // PROBE (ter-pdw8): the atlas serves V2 and the filtered guide''')
print('applied')
