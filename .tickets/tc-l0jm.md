---
id: tc-l0jm
status: open
deps: [tc-8142]
links: []
created: 2026-11-23T00:19:38Z
type: chore
priority: 3
assignee: Alexis Purslane
parent: t7-3e22
---
# One shared GLSL function for the variance around a pivot value: the clamp's box and the filter's young-history variance

Since tc-8142 (2026-11-22) two shaders take a variance around a pivot value, so that equal inputs give a variance of exactly 0 under any driver's rounding:
- accumulate_spatial.comp, the clamp's box: the moments of the 3 x 3 fast histories around the pixel's own fast history (vec3). Before tc-8142 it took the plain moments, and on the cloud card (NVIDIA, Linux) they cancelled to a σ of about 1e-4 of the light over 9 equal values, which moved a test's length by one half-float step.
- diffuse_filter_variance.comp:42, the young-history variance: the moments of the luminance around the pixel's own luminance (float). It already had this form.

With two real uses the shared function is due (line orchestrator's answer 82). One function in an include that both shaders read, generic over float and vec3 by two overloads, so that the cancellation has no third place to come back. Not before the merge of t7-3e22.

## Acceptance Criteria

- Both shaders call the one function. Their SPIR-V results are unchanged where the arithmetic is unchanged, or every moved test value is listed with its old and new value.
- A comment of the function says why the pivot: equal inputs give exactly 0 under any rounding (tc-8142).


## Notes

**2026-11-23T04:11:17Z**

The review of tc-8142 (one reviewer, claude-opus-5-5 at high effort, 2026-11-22) widens this ticket. 1. A third place takes a spread around a pivot: region_drift.glsl, regionOlderFirst() and regionTestOf() (lines 162 to 188), for the same reason. Its sums are split across adders, so the shared part may be only the last step that turns a sum, a sum of squares and a count into a variance. Bring it in, or record why it stays separate. 2. Without precise, GLSL lets a compiler reassociate the sums, and a rewrite of the sum of (x - o) as (sum of x) - 9 o would lose the exact zero. Mark the offset, the moments and the squares precise in the shared function (the cost is negligible for 9 taps), or weaken the comments' 'under any driver's rounding' to per-operation IEEE rounding. 3. The narrow-box test goes red only on NVIDIA. A direct test can pin the rule on every machine: a flat box of equal fast histories traces a sigma of exactly 0 (AccumulationTraceTests, the Deviation layer), at light levels where the plain moments leave a nonzero remainder in single floats. ARCHITECTURE.md, 'Portable Vulkan', now names the three places.
