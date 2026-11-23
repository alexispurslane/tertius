# Tool code of ter-pdw8, the fifth probe step (the surface share), applied after apply-rayend4.py on the same build copy
# (never to src). A ray's end reads this draw's direct part of the surface that the pixel shows this draw, times the share
# of the recent draws in which the pixel showed that surface; the history part stays as today. The plan writes the
# changed value into the hit light's layers 0 and 1, which every ray reads (no other read path), and the same direct part
# times the share into the filtered guide's layer 14, so the guide and the hit read one writer's value (bit 4 is set
# with each share variant, so the guide reads layer 14 and the atlas is built).
#   direct part D = this draw's direct light times the material factors, plus the glow (as leavingLightOf adds it)
#   hit light     = leaving light - (1 - share) D
#   S1 (bit 6, 64; run as 80): the share from new state per pixel (layer 15): the reference surface (its distance from
#     the eye in m and its geometric normal) and q, the running mean of "this draw shows another surface" over 64 draws
#     (weight 1 / min(draw + 1, 64), the draws since the reset). A draw shows the reference surface when its distance lies
#     within 5 % of the reference's and the geometric normals lie within 25° (SameNormalCosine), or when both are sky.
#     When q passes 0.5 the reference switches to this draw's surface and q becomes 1 - q. share = 1 - q on the
#     reference surface and q on another. q, not 1 - q, is stored, because a half float holds a small q exactly enough
#     and would round a 1/64 update of a share near 1 away.
#   S2 (bit 7, 128; run as 144): no new state. The share from the history that the pixel holds at this surface:
#     share = min(1, (L + 1) / P), with L the diffuse history's length in draws where the reprojection's surface test
#     holds a history at the pixel (holdsHistory) and 0 where it does not, and P = min(phases, draws since the reset + 1),
#     phases = the upscaler's jitter phase count, (int)(8 (1920 / render width)^2), for the record's output width of
#     1920 px (25 at 1072, 27 at 1040, 18 at 1280).
#   Layer 16 (every variant, for the census dump): holdsHistory (1 / 0, -1 for the sky), L, S1's share, S2's share.
# Usage: python3 apply-rayend5.py <build copy root>
import sys
from pathlib import Path

root = Path(sys.argv[1])


def edit(rel, old, new, count=1):
    path = root / rel
    text = path.read_text()
    assert text.count(old) == count, (rel, old[:80], text.count(old))
    path.write_text(text.replace(old, new))


S = 'src/Tertius.Rendering/Shaders/'
P = S + 'screen_trace_plan.comp'
edit(P,
     '''vec3 probeDirectReflection; // the direct part: this draw's direct light times the material factors, plus the glow
vec3 probeDirectDiffuse;''',
     '''vec3 probeDirectReflection; // the direct part: this draw's direct light times the material factors, plus the glow
vec3 probeDirectDiffuse;
float probeDistance;  // the surface's distance from the eye in m, 0 for the sky
vec3 probeNormal;     // its geometric normal, 0 for the sky and an impostor
float probeHolds;     // 1 where the diffuse history's surface test holds a history, 0 where not, -1 for the sky
float probeLength;    // the diffuse history's length in draws where it holds one, else 0''')
edit(P,
     '''        {   // PROBE (ter-pdw8): the direct part, as leavingLightOf adds it.''',
     '''        probeDistance = texelFetch(gbufferDistance, pixel, 0).r; // PROBE (ter-pdw8): the surface share's inputs
        probeNormal = p.s.geometricNormal;
        probeHolds = holdsHistory(r.diffuse) ? 1.0 : 0.0;
        probeLength = holdsHistory(r.diffuse) ? historyLengthOf(r, KindDiffuse) : 0.0;
        {   // PROBE (ter-pdw8): the direct part, as leavingLightOf adds it.''')
edit(P,
     '''        probeDirectDiffuse = glowAt(pixel); // PROBE (ter-pdw8): an impostor's light is all direct
        probeDirectReflection = glowAt(pixel);''',
     '''        probeDirectDiffuse = glowAt(pixel); // PROBE (ter-pdw8): an impostor's light is all direct
        probeDirectReflection = glowAt(pixel);
        probeDistance = texelFetch(gbufferDistance, pixel, 0).r;
        probeHolds = 1.0;   // an impostor keeps no history; S2 gives its glow a share of 1
        probeLength = 64.0;''')
edit(P,
     '''    probeDirectReflection = vec3(0.0);
    probeDirectDiffuse = vec3(0.0);
#ifdef''',
     '''    probeDirectReflection = vec3(0.0);
    probeDirectDiffuse = vec3(0.0);
    probeDistance = 0.0;
    probeNormal = vec3(0.0);
    probeHolds = -1.0;
    probeLength = 0.0;
#ifdef''')
edit(P,
     '''void main()
{''',
     '''// PROBE (ter-pdw8): the surface share of S1 and S2 (apply-rayend5.py), its diagnosis in layer 16, and under S1 or S2
// the hit light and the guide's level 0 with the share applied to the direct part.
void probeSurfaceShare(ivec2 pixel)
{
    float draws = float(trace.drawIndex) + 1.0;
    vec4 state = imageLoad(hitLight, ivec3(pixel, 15));
    bool fresh = trace.drawIndex == 0u || !(state.g >= 0.0 && state.g <= 1.0);
    float reference = abs(state.r);
    vec2 nxy = state.ba;
    vec3 referenceNormal = vec3(nxy, (state.r < 0.0 ? -1.0 : 1.0) * sqrt(max(1.0 - dot(nxy, nxy), 0.0)));
    bool normals = dot(probeNormal, probeNormal) > 0.25 && dot(nxy, nxy) + referenceNormal.z * referenceNormal.z > 0.25;
    bool same = probeDistance <= 0.0 ? reference <= 0.0
        : reference > 0.0 && abs(probeDistance - reference) <= 0.05 * reference
            && (!normals || dot(probeNormal, referenceNormal) >= SameNormalCosine);
    float q = fresh ? 0.0 : state.g + ((same ? 0.0 : 1.0) - state.g) / min(draws, 64.0);
    if (fresh || (!same && q > 0.5))
    {
        if (!fresh)
            q = 1.0 - q;
        reference = probeDistance;
        referenceNormal = probeNormal;
        same = true;
    }
    float share1 = same ? 1.0 - q : q;
    imageStore(hitLight, ivec3(pixel, 15),
        vec4(referenceNormal.z < 0.0 ? -reference : reference, q, referenceNormal.xy));
    float phases = min(floor(8.0 * pow(1920.0 / frame.viewport.x, 2.0)), draws);
    float share2 = min(1.0, (probeLength + 1.0) / phases);
    imageStore(hitLight, ivec3(pixel, 16), vec4(probeHolds, probeLength, share1, share2));
    if ((rayEndVariant & 192u) == 0u || probeCovered <= 0.0)
        return;
    float share = (rayEndVariant & 64u) != 0u ? share1 : share2;
    imageStore(hitLight, ivec3(pixel, KindReflection),
        vec4(max(probeReflection - (1.0 - share) * probeDirectReflection, vec3(0.0)), 1.0));
    imageStore(hitLight, ivec3(pixel, KindDiffuse),
        vec4(max(probeDiffuse - (1.0 - share) * probeDirectDiffuse, vec3(0.0)), 1.0));
    imageStore(hitLight, ivec3(pixel, 14), vec4(dot(share * probeDirectDiffuse, vec3(0.2126, 0.7152, 0.0722))));
}

void main()
{''')
edit(P,
     '''    probeUpdateMeans(pixel);
    imageStore(rayPlan, pixel, uvec4(plan));''',
     '''    probeUpdateMeans(pixel);
    probeSurfaceShare(pixel);
    imageStore(rayPlan, pixel, uvec4(plan));''')
edit('src/Tertius.Rendering/Frame/ScreenTraceImages.cs',
     '''            layers: 15, // PROBE (ter-pdw8): 4 to 7 stand-in (c), 8 to 13 the rays' hits, 14 the filtered guide''',
     '''            layers: 17, // PROBE (ter-pdw8): 4 to 7 stand-in (c), 8 to 13 the rays' hits, 14 the guide, 15 and 16 the share''')
edit('src/Tertius.Rendering/Scene/RayCensus.cs',
     '''        Dump("hit-diffuse-c-direct-mean", renderer.CensusReadLevel(images.HitLight, 5, 0));''',
     '''        Dump("hit-diffuse-c-direct-mean", renderer.CensusReadLevel(images.HitLight, 5, 0));
        Dump("share-state", renderer.CensusReadLevel(images.HitLight, 15, 0));
        Dump("share-diagnosis", renderer.CensusReadLevel(images.HitLight, 16, 0));''')
print('applied')
