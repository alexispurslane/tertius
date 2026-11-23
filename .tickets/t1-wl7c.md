---
id: t1-wl7c
status: in_progress
deps: []
links: [ter-pdw8]
created: 2026-11-23T06:28:18Z
type: feature
priority: 1
assignee: Alexis Purslane
parent: ter-6fka
tags: [rendering, temporal, rays]
---
# The surface share: a pixel sends its fresh light in full only where the last draw showed the same surface there

The engine build of the ray end experiment's result (ter-pdw8, docs/research/ray-end-reads/README.md). The orchestrator accepted it on 2026-11-23 (ter-6fka's note of that day, main 9b6e14855) as the first change of the structural step and the remedy of the speck class "a pixel is not a surface". The record's numbers are on 3549d3283, before Design X. This build is on main after Design X, and it measures everything again there.

## The fault that the build removes

Under the jitter, a pixel can show two surfaces in turn. The lamp hall's render row 314 shows the sky in 24 of 25 jitter phases at 1072 x 603, and the sunlit face of a window sill, thinner than a pixel, in one. In that one draw the pixel's direct light is 162 against about 0.2 around it. A ray that ends on that pixel reads 162, and the guide aims rays at it: guided rays are 0.25 of all diffuse samples and 0.906 of the samples above 5. The result is a cloud of specks on the ceiling.

## The model

Terms, one name each:
- **Fresh light** of a pixel: the light that this draw computed for the pixel's surface, before any history. Toward a diffuse ray it is the pixel's glow plus its diffuse material factor times its direct diffuse light. Toward a reflection ray it adds the specular material factor times the direct specular light.
- **Surface test** of a pixel: the existing test `holdsHistory(Reprojection)` in signals.glsl on the pixel's diffuse reprojection (the surface place with `surfacePassBits`). It holds where the last draw showed the same surface at the pixel's last place.
- **P**: the phase count of the jitter that this draw uses, 1 for a draw without jitter.
- **Spread**: min(P, draws since the reset + 1), a whole number of 1 or more. It is the number of draws over which a new surface's fresh light counts.
- **Surface share** of a pixel: 1 where the surface test holds, else 1 / spread.

The rule: a pixel sends its fresh light times its surface share. A pixel whose surface was there in the last draw sends all of it. A pixel that shows a surface for the first time sends the part that one draw in P deserves, because the jitter can show that surface in as few as one draw of P. A reset draw sends all (min(P, 1) = 1), then at most 1/2, then at most 1/3.

What causes what:
- The jitter's owner (FrameHistory's pending frame, with Fsr3Upscaler.JitterPhaseCount) causes P, and it holds the draw's index since the reset. It writes the spread into the frame constants each draw. The frame block holds no draw index today, and the shaders need none: the owner that knows both values does the min.
- The surface test pass causes the pass bits. Nothing new is stored: the share is a derived read of the pass bits and the spread.
- Two readers take the shared fresh light: the plan's hit light (`leavingLightOf` in screen_trace.glsl, written by screen_trace_plan.comp) and the guide's finest level (`guideLightAt` in light_pyramid_finest.comp). Today each of the two derives the fresh diffuse light by its own copy of one expression. With the share, a copy that forgets it brings the fault back through the guide: the record measured the guide alone, with the hit unshared, at 3.6 times worse on the ceiling. So both read one function.

No new image and no new state.

## Signatures

frame.glsl and the C# frame constants: `renderParams.w`, unused today, holds the spread.

```glsl
    vec4 renderParams;        // x main-view mip bias, yz jitter in render pixels, w the surface share's spread:
                              // min(the jitter's phase count, draws since the reset + 1), 1 without jitter
```

signals.glsl, beside `holdsHistory`:

```glsl
// The share of a pixel's fresh light that the pixel sends (ARCHITECTURE.md, "Surface share"): all of it where the
// reprojection of its surface place holds a history, so the last draw showed this surface there. Elsewhere the part that
// one draw of the jitter's phases deserves, 1 / min(P, draws since the reset + 1): under the jitter a pixel can show a
// surface thinner than a pixel in one draw of P.
float surfaceShare(Reprojection surface)
{
    return holdsHistory(surface) ? 1.0 : 1.0 / frame.renderParams.w;
}
```

where
- `surface` = the pixel's diffuse reprojection: its last place's four taps with their surface pass bits.
- `frame.renderParams.w` = the spread, a whole number from 1 to P. It is 1 in the reset draw and in every draw without jitter.

One function for the fresh diffuse light, in a header that both readers include (the lead chooses the file):

```glsl
// The fresh diffuse light that the pixel q sends: its glow and its direct diffuse light times its diffuse factor, times
// its surface share.
vec3 sentDiffuseLight(ivec2 q, vec3 diffuseFactor, Reprojection surface);
```

`leavingLightOf` then reads:

```glsl
    float share = surfaceShare(r.diffuse);
    vec3 light = sentDiffuseLight(q, factors.diffuse, r.diffuse) + factors.diffuse * diffuseHistory;
    vec3 reflection = light + factors.specular * (share * imageLoad(directSpecularLight, q).rgb + specularHistory);
```

and `guideLightAt` takes its light of a surface pixel from `sentDiffuseLight`. light_pyramid_finest.comp gains the bindings that the surface place needs (the surface test's word and the motion target). The lead may change these signatures where the code shows a better one, and records the change and its reason.

C#: the pending frame owns one phase count per draw. The jitter's offset and the spread both read it, and one named member gives the spread (for example `PendingFrame.SurfaceShareSpread`). VulkanRenderer.cs:951 computes the last draw's count a second time for the last jitter; the lead leaves that or folds it in, as the code allows.

## Settled decisions

1. The share scales the whole fresh light: the direct light and the glow (orchestrator, 2026-11-23). A lit emitter thinner than a pixel is in the same class as the sill. The lead tests it with one emitter strip thinner than a pixel. If a measure speaks against the glow, the lead reports it and keeps the direct part only.
2. P comes from the jitter's owner in C# as a frame value. The probe's `8.0 * pow(1920.0 / frame.viewport.x, 2.0)` is a second copy of the jitter's rule with the display width as a constant. It does not enter the engine.
3. The share reads the diffuse reprojection for both ray kinds (the line orchestrator, for the orchestrator to reverse). The surface pass bits are the witness of "this surface was here". The specular pass bits add conditions of the specular history that say nothing about a new surface. The probe did the same.
4. The guide reads the same shared value through the same function (the line orchestrator; the record's section 6.4 gives the reason).
5. An impostor pixel sends its glow with share 1, as in the probe, unless the surface test covers impostors too. The lead finds out which holds and states it.
6. The accepted limit, to state in ARCHITECTURE.md: a lit surface thinner than a pixel sends about 1 / P of its true bounce, and a newly seen surface loses about one draw of its direct bounce (1 - 1/P of a draw).
7. No rule against the specks beside this one. The caps and the clamp stay as they are.

## Acceptance, on main after Design X

- Tests first, red on main for the right reason: a sunlit strip thinner than a pixel gives no capture speck on the surface that its bounce lights. A newly seen surface sends its full direct light from its second draw. A reset draw sends all of every pixel's fresh light. A draw without jitter has P = 1 and changes nothing. The share falls as 1, 1/2, 1/3 after a reset for a pixel that never holds a history, up to 1/P.
- The specks are gone at 1072 x 603, 1040 x 585, at render scale 0.96 and under the dynamic render scale, in both window shapes (1920 x 1080 and 1800 x 1008). Measure with the record's tools (capture specks, mean specks, the largest sample).
- No leak at 1:1 (the record's 19 crops again, on main), and every region's mean within 1 % of main.
- A moving sunlit object keeps its bounce. The experiment did not run this case. If the surface test fails on a moving surface in every draw, that surface sends 1 / P for as long as it moves. Use a scene with a moving lit figure or door. Report the map of the pixels with a share below 1 in one draw of the motion, and the bounce of the moving surface against main.
- A cut onto the lamp hall's ceiling shows no flash of specks in its first P draws. After a cut the share is 1, then 1/2, then 1/3, so the first draws are the weak point.
- Side-by-side videos, main against the build, real frames at 60 a second, one fixed exposure, H.264 at CRF 12 or lower: the lamp hall's ceiling at 1072 x 603, the walk (cabin-walk), the exit-cabin motion film and the cut. The lead looks at frames of each at 1:1 and describes them.
- The frame's GPU time equal within the noise on the Mac at a quiet load, with the 1-minute load in each row. A card job starts with one discarded warm-up run (t1-80jd).
- The usual gates: the review with every finding resolved, Rendering in Checked and in Release on the final head merged with main, lavapipe for the changed shaders' tests, coverage of the changed lines, the mutants of the changed GLSL and Rendering C# through tools/mutate.py with one result each, ARCHITECTURE.md updated ("Surface share", the limit of decision 6, the guide's text), every changed reference image reviewed as reference, actual and FLIP map.
- After the merge, one take each of ter-fe63's scene, t2b-d2m6's wall and the jamb opening's specks, so that the orchestrator can close or keep those three tickets.

## Out of scope

- The general mottle. S2' does not change it (far-hit variance factor x1.00 to x1.01 outside the specks). It needs a far-field store or better sampling (ter-6fka).
- The bright samples of ter-fe63 that are real hits on a wide sunlit patch. The take after the merge shows how much of that cloud stays.
- The anti-lag's short histories (t1-g1jd) and the clamp's lost light (ter-18hc).
- A per-pixel resolve or a prefilter of the hit light. The record closes V1, V2 and V3.


## Notes

**2026-11-23T06:34:37Z**

2026-11-23, the line orchestrator, from the orchestrator. The orchestrator read this ticket and agrees with the line orchestrator's four decisions in it (one function for the hit light and the guide, one frame value for the spread, the diffuse reprojection for both ray kinds, an impostor's share of 1 unless the surface test covers impostors). One more acceptance condition, for the lead's brief: the key and fill shader direct_key_fill.frag sits at register r115 on the M1 with no room, and one more register costs 0.10 to 0.28 ms of the frame in every view (t1-gout's note of 2026-11-23, t2-x53j). This build touches signals.glsl and the frame block, which that shader includes. Until the guard of t2-x53j exists, the lead runs the register count script (it comes to main with commit A of t1-gout; tools/README.md says how to run it) before its ready report and states the highest register of direct_key_fill.frag and direct.frag on main and on its head.

**2026-11-23T09:27:56Z**

2026-11-23, the line orchestrator. Launched in the slot that the jamb work freed, as a continuation with a fresh context in the jamb lead's thread and worktree, because the disk (27 GiB free) does not allow a new worktree with a full build (30). Child thread: thread:delegated-task:command%3Amcp%3A1606afbd-dec8-4bf8-a33c-bc8fce97f948%3Adelegate-task%3Aslabs-3-t1-r9vj-lead-1. taskId: node:delegated-task:command%3Amcp%3A00721b96-2f79-4a0a-b25d-c2978632070b%3Athread-send%3Aslabs-3-t1-wl7c-surface-share-lead-1. clientRequestId: slabs-3-t1-wl7c-surface-share-lead-1. Target: claudeAgent, claude-opus-5-5, effort high; one reviewer stage with the same target. Worktree /Users/alexispurslane/.t3/worktrees/tertius/t3-2b720d96-b40221aeff8f27f7, branch t3/fade-lit-front-trace, which keeps its name. Review base 373da00eb. Cloud money: 0.25 dollars. Set in the brief beside the ticket: the moving lit surface is taken early and is a fork if it fails; the capture specks are required at every size and the census measures only if the census probe applies to main with small effort; main's stills and films for the comparison come from the worktree before the first engine change if the disk allows no second build; the register count before the ready report; four interim messages.
