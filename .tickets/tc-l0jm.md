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

