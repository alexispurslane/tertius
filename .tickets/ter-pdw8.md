---
id: ter-pdw8
status: open
deps: []
links: []
created: 2026-11-23T02:54:52Z
type: task
priority: 1
assignee: Alexis Purslane
parent: ter-8hyd
tags: [rendering, temporal, experiment]
---
# What a ray's end reads: one pixel of one draw against a resolved and a prefiltered light, measured on the census's stills

An experiment for the structural step ter-6fka, in the ray census's probe build (docs/research/ray-census). It changes no engine code on main.

The class. The ray census (ter-8pco) and the lamp hall's specks (ter-fe63) give one class for three faults (the lamp hall's specks, ter-fe63's speck cloud, the specks around the jamb leak's opening) and for the bounce light's noise in general: a ray's end reads one pixel of one draw. In code, hitLightOf (screen_trace.glsl) is imageLoad(hitLight, ivec3(q, kind)), and leavingLightOf fills it from this draw's direct light at q (imageLoad(directDiffuseLight, q)) plus the last draw's history. This draw's direct light is jittered and not resolved. So a source thinner than a pixel enters at its full value in one jitter phase (the sill: 162 in 1 draw of 25 against a ceiling of 0.22), and a far hit is a point sample of a solid angle that covers many pixels. Far hits and misses carry 76 to 100 % of the diffuse samples' variance in every census region. No cap bound these samples, and the accumulator keeps up to 12 % less light than it receives.

The question. How much of the far hits' variance, of the tail, and of the accumulator's loss goes away when the ray's end reads a filtered light, and what bias does each filter add?

## Design

Systems view. One box owns 'the light that leaves a hit toward a ray': today the plan's hit light image, written by leavingLightOf and read by hitLightOf. The experiment changes only what that box answers. It changes no ray, no density, no clamp, no evidence rule and no accumulator.

Variants (each a build switch of the probe build):
- V0: today. This draw's direct light plus the last draw's history, at the hit pixel.
- V1, resolved in time by reuse: the hit's light is the last draw's shown image before tone mapping (the upscaler's output, which is already resolved over the jitter phases), read at the hit point's reprojected place, pre-exposed like the hit light. No new history image and no new trust rule: the upscaler already owns that resolve. Costs to name: one draw of lag, the old eye's specular part in the light, and the upscaler's own faults (t3-5jws) entering the bounce.
- V2, prefiltered in space: the hit light gets a mip chain (plain 2 x 2 means of the hit light, in the probe build only), and a hit reads the level whose cell matches the ray's footprint at the hit: level = log2 of (hit distance times the square root of the ray's solid angle, in pixels), where the solid angle is 1 / (rays per pixel's hemisphere times the ray's density). State the formula that the lead uses. Costs to name: light that bleeds across a silhouette in a coarse cell.
- V3: V1 and V2 together (a mip chain of the last shown image).
If V1's source is not reachable in the probe build without engine work, the lead says so and substitutes a per-pixel mean of the direct light over one jitter period on the stills, marked as a stand-in.

Stills: the census's five stills at 2/3 and 0.9584, and the lamp hall at 1072 x 603 (bursts) and 1040 x 585 (clean). Same settle and count as the census (240 and 400 draws), clamps on.

Measures per variant and region:
1. The variance of the diffuse samples and of the rough reflection samples, and each class's share (miss, near hit, far hit), as in the census.
2. The brightest 0.1 % of the samples: value, class, distance.
3. The mean against V0's 400-draw mean and against the path trace of the still where one exists. This is the bias.
4. The share of the received light that the accumulator keeps (the census's 1.00 to 1.12 ratio).
5. Speck pixels in the lamp hall, at both sizes.
6. The shown picture with the unchanged denoiser: daub, boil and grain by the survey's measures, and crops at 1:1 beside the reference at three silhouettes per still (a dark object before a bright wall, a bright object before a dark wall, a window's edge), because V2's bias lives there.

Decision rule (the orchestrator applies it): a variant goes into ter-6fka's design if it removes the specks at every size, lowers the far hits' variance by a factor that the picture shows (boil or grain down by more than the spread between runs), and adds no leak or halo that a crop shows at 1:1. If V1 and V2 both pass, the one with less new state wins. If neither lowers the far hits' variance of bay-sunbeam (all far hits at 2.8 to 11 m on one sunlit patch), that scene's noise is visibility sampling of a small bright source, and the record says that a far-field store or more guided rays must carry it.

Out of scope: any engine change on main, any new trust rule, the capture points and the senders of ter-6fka (they follow from this record), and coverage in the direct light pass (remedy 2 of the back-face line's report, held as the alternative if V1 fails).

## Acceptance Criteria

A record under docs/research/ with the table of the six measures per variant, still and region, the crops, the formula of V2's level, the cost of each variant in passes and images, and a plain statement per variant: removes the specks or not, lowers the far hits' variance by what factor, adds which bias. The spread between two runs of V0 is in the table. The lead opened every crop and describes it.

