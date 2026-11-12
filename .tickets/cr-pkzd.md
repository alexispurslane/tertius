---
id: cr-pkzd
status: closed
deps: []
links: []
created: 2026-11-12T10:22:45Z
type: bug
priority: 2
assignee: Alexis Purslane
parent: ter-8hyd
---
# The light-age cut takes only bright samples, so the reflections jump up after a lamp goes out

Found in ter-g3tk. Before the blend, the specular temporal dispatch cut the slow history to the sample's light age only when the sample held at least 25 % (AgeCutShare) of the history's luminance. After the cabin's lamps went out, that restarted the histories from their bright samples: four draws after the switch, the reflections jumped up by 72 % (survey switch case cabin, reflections alone). Without the condition the jump is 42 %. The fall itself still takes about 126 draws, because the light age sees no change in bounce light (ter-g3tk, cause 2).

## Acceptance Criteria

The reflections' jump after the cabin's lamps go out falls from +72 % to about +42 %. rect-lamps and bay-sunbeam keep their still blotch and specks; the films rect-lamps and lamp-hall-4-spot-moving keep their flicker and blotch. Full Checked and Release suites pass.


## Notes

**2026-11-12T10:22:45Z**

Built on branch ter-g3tk-cut-rule (2026-11-12), ready to merge; the user decides.

Why the condition existed: da70cd8c2 (2026-11-08) added it with the reason "a dim ray does not speak for the light that the history holds", and the test ARayDimmerThanAQuarterOfTheReceiversHistoryDoesNotCutIt held it: in a receiver whose history the bright sky outweighs, one ray at a dimmed emitter should not throw the whole history away. No ticket, plan or measurement backs the value 25 %. The reason predicts more cuts where light changes everywhere, so moving lamps decide.

Measures, main (641450420's renderer) against the branch, survey split at t1-4hij's fixed stops:
| Film | Measure | main | branch |
|---|---|---:|---:|
| rect-lamps | flicker all / reflections | 0.0015 / 0.0007 | 0.0015 / 0.0007 |
| rect-lamps | blotch middle / end | 0.0414 / 0.0359 | 0.0413 / 0.0360 |
| lamp-hall-4-spot-moving | flicker all / reflections | 0.0059 / 0.0002 | 0.0059 / 0.0002 |
| lamp-hall-4-spot-moving | blotch middle / end | 0.0475 / 0.0449 | 0.0473 / 0.0447 |
| lamp-hall-4-spot-moving | direct blotch (trail) middle / end | 0.0344 / 0.0375 | 0.0344 / 0.0375 |
The only change: the reflections' own level in the 4-spot film reads -1.2 % against its still at the end (main -0.6 %); the whole image's level is the same (+0.46 %). Stills (survey still part, earlier build of the same change): rect-lamps blotch 0.051 / specks 0.77 (main 0.051 / 0.79), bay-sunbeam 0.041 / 0.055 (same). Lamps-off switch, reflections alone: jump +42 % (main +72 %), slowest region 126 draws (main 128).

Red-green: ARayDimmerThanAQuarterOfTheReceiversHistoryDoesNotCutIt became ARayOfAChangedLightCutsTheReceiversHistoryHoweverDimTheRayIs; on the build before the shader change it failed for the right reason (none of the 22 receivers with a dim ray of age 0 was cut), after it all 44 LightAgeHistoryTests pass. tools/test-all.sh: every module passes in Checked and Release. The diagnostic keeps reporting the share (AgeCutShare, now under TERTIUS_LIGHT_AGE_DIAGNOSTIC) for tools/Tertius.ReflectionReference. No C# of Core, Simulation or Physics changed, so no Stryker run applies.

| id | place | change | result | killed by | s |
|---|---|---|---|---|---:|
| S01 | src/Tertius.Rendering/Shaders/accumulate_temporal.comp:316 | a pixel without a sample cuts too | survived | judgment call: hasSample is false only for a non-finite sample, and the resolve caps every sample at MaxRadiance, so no scene reaches it (ter-dbzx) | 46.9 |
| S02 | same | no test of the age code | survived | equivalent: an age code of 0 gives 0u - 1u, the largest uint, so min keeps the length | 52.3 |
| S03 | same | the 25 % condition comes back | killed | ARayOfAChangedLightCutsTheReceiversHistoryHoweverDimTheRayIs | 91.5 |
| S04 | same | no cut | killed | ARayOfAChangedLight..., AReceiverWhoseRayHitsTheDimmedEmitter..., TheCappedPreciseHistoryAlsoShortensToTheLightsAge | 48.0 |
4 rows: killed 2, survived 2 (one equivalent, one judgment call). Frame time: the change removes one luminance comparison, so the deck's GPU frame span does not move measurably (not measured).
